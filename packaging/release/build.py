#!/usr/bin/env python3
"""Portable 0.14.5 release assembler. It never installs into a game folder."""
from __future__ import annotations

import argparse
from datetime import datetime, timezone
from hashlib import sha256
import json
from pathlib import Path, PurePosixPath
import re
import shutil
import stat
import struct
import subprocess
import tempfile
import urllib.request
from zipfile import ZipFile, ZIP_DEFLATED

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[1]
VERSION = '0.14.5'
LAUNCHER_VERSION = '1.0.6'
FRESH_DEFAULTS = dict(characterShuffle=True, dialogue=True, shuffledUnites=True, s1Atb=False,
    s1AtbInstalled=False, s1Treasure=False, f6View='Hidden',
    s1JoiningWeaponLevel='TirVariance', s2JoiningWeaponLevel='RiouVariance',
    s1JoiningEquipment='MatchRole', s2JoiningEquipment='MatchRole')
GAME_HASH = 'e093f175eaf3734d293ca0a236c652dd7fcc72ea5fa10d7b21103edf12f11c3a'
LOADER = 'BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.697.zip'
FIX = 'Suikoden_Fix-v1.5.2b.zip'
FIX_DLL_HASH = '4b1a38e7512e4a91cbd86f94d9f233749f83dbb0584979242f8062faf6132f46'
RUNTIME_NAMES = ('SuikodenRandomizer.dll', 'SuikodenRandomizer.Core.dll',
                 'SuikodenRandomizer.Suikoden2.Core.dll', 'SuikodenRandomizer.Shared.dll')
ATB = 'SuikodenRandomizer.S1BattlePreview.dll'
PUBLIC_ATB_VERSION = '0.10.1'
ARCHIVE_SUFFIXES = {'.zip', '.7z', '.rar', '.tar', '.gz', '.tgz', '.cab', '.nupkg'}
FORBIDDEN = {'gameassembly.dll', 'unityplayer.dll', 'gsd1.dll', 'gsd2.dll',
             'logoutput.log', 'launcher-preferences.json', 'install.cmd'}


def digest(data: bytes) -> str:
    return sha256(data).hexdigest()


def sha(path: Path) -> str:
    return digest(path.read_bytes())


def pe_file_version(data: bytes) -> str:
    """Read the numeric file version from a PE32/PE32+ DLL's RT_VERSION resource."""
    try:
        pe, = struct.unpack_from('<I', data, 0x3c)
        if data[:2] != b'MZ' or data[pe:pe + 4] != b'PE\0\0':
            raise ValueError
        section_count, = struct.unpack_from('<H', data, pe + 6)
        optional_size, flags = struct.unpack_from('<HH', data, pe + 20)
        optional = pe + 24
        magic, = struct.unpack_from('<H', data, optional)
        directories = {0x10b: 96, 0x20b: 112}[magic]
        directory_count, = struct.unpack_from('<I', data, optional + directories - 4)
        if not (flags & 0x2000) or directory_count < 3 or optional_size < directories + 24:
            raise ValueError
        resource_rva, resource_size = struct.unpack_from('<II', data, optional + directories + 16)

        def rva_bytes(rva: int, size: int) -> bytes:
            for index in range(section_count):
                section = optional + optional_size + 40 * index
                start, raw_size, raw_offset = struct.unpack_from('<III', data, section + 12)
                offset = rva - start
                if 0 <= offset and offset + size <= raw_size and raw_offset + offset + size <= len(data):
                    return data[raw_offset + offset:raw_offset + offset + size]
            raise ValueError

        resources = rva_bytes(resource_rva, resource_size)

        def entries(offset: int):
            named, numbered = struct.unpack_from('<HH', resources, offset + 12)
            for index in range(named + numbered):
                yield struct.unpack_from('<II', resources, offset + 16 + 8 * index)

        def child_directory(offset: int, resource_id: int) -> int:
            for identifier, child in entries(offset):
                if identifier == resource_id and child & 0x80000000:
                    return child & 0x7fffffff
            raise ValueError

        # Type RT_VERSION (16), name VS_VERSION_INFO (1), then language.
        languages = child_directory(child_directory(0, 16), 1)
        versions = set()
        key = 'VS_VERSION_INFO\0'.encode('utf-16-le')
        fixed = (6 + len(key) + 3) & ~3
        for _, leaf in entries(languages):
            rva, size = struct.unpack_from('<II', resources, leaf)
            info = rva_bytes(rva, size)
            length, value_size, kind = struct.unpack_from('<HHH', info)
            if info[6:6 + len(key)] != key or value_size != 52 or kind != 0:
                raise ValueError
            if not fixed + value_size <= length <= len(info):
                raise ValueError
            signature, _, high, low = struct.unpack_from('<IIII', info, fixed)
            if signature != 0xfeef04bd:
                raise ValueError
            versions.add(f'{high >> 16}.{high & 0xffff}.{low >> 16}.{low & 0xffff}')
        if len(versions) != 1:
            raise ValueError
        return versions.pop()
    except (ValueError, KeyError, struct.error) as error:
        raise ValueError('Battle preview DLL has no readable, consistent PE file version.') from error


def public_atb_payload(path: Path, expected_hash: str) -> bytes:
    data = path.read_bytes()
    if re.fullmatch(r'[0-9A-Fa-f]{64}', expected_hash) is None or digest(data) != expected_hash.lower():
        raise ValueError('ATB DLL differs from the hash reviewed by this launcher source. Review your rebuilt ATB, '
                         'publish the launcher with -p:ReviewedAtbSha256=<hash>, and pass that same hash through '
                         '--reviewed-atb-sha256. Do not disable the hash check. See BUILDING.md.')
    version = pe_file_version(data)
    if version != PUBLIC_ATB_VERSION + '.0':
        raise ValueError(f'Public packages require battle preview {PUBLIC_ATB_VERSION}.0, but the supplied DLL is {version}.')
    return data


def package_revision(value: str) -> str:
    if re.fullmatch(r'r[1-9][0-9]*', value) is None:
        raise argparse.ArgumentTypeError('Package revision must be r followed by a positive integer, for example r2')
    return value


def source_commit(value: str) -> str:
    if re.fullmatch(r'[0-9A-Fa-f]{40}', value) is None:
        raise argparse.ArgumentTypeError('Source commit must be the full 40-character Git commit SHA')
    return value.lower()


def distribution_version(args) -> str:
    revision = getattr(args, 'package_revision', None)
    return VERSION + ('-' + package_revision(revision) if revision is not None else '')


def artifact_name(args, suffix: str) -> str:
    return f'Stars-of-Destiny-{distribution_version(args)}-{suffix}'


def distribution_metadata(args) -> dict:
    # These record the build inputs' declared provenance; the caller must verify
    # that the reused DLL bytes actually correspond to those reviewed commits.
    sources = {}
    for component in ('runtime', 'launcher'):
        commit = getattr(args, component + '_source_commit', None)
        if commit is not None:
            sources[component] = source_commit(commit)
    return dict(packageRevision=getattr(args, 'package_revision', None),
                distributionVersion=distribution_version(args), binarySourceCommits=sources)


def save(path: Path, value) -> None:
    path.write_text(json.dumps(value, indent=2) + '\n', encoding='utf-8')


def safe_name(name: str) -> str:
    p = PurePosixPath(name)
    if (not name or p.is_absolute() or '\\' in name or ':' in name or
            any(c in name for c in '\r\n\0') or
            any(x in {'', '.', '..'} or x.endswith(('.', ' ')) or
                re.fullmatch(r'(?i)(con|prn|aux|nul|com[1-9]|lpt[1-9])(?:\..*)?', x)
                for x in name.split('/'))):
        raise ValueError('Unsafe package path: ' + name)
    return name


def regular_files(folder: Path):
    for p in sorted(folder.rglob('*')):
        if p.is_symlink() or p.is_junction():
            raise ValueError('Linked input: ' + str(p))
        if p.is_file():
            yield p


def dependencies(args):
    records = json.loads((HERE / 'dependencies.json').read_text())['dependencies']
    needed = {x['name']: x for x in records if args.mode in x['modes']}
    args.dependencies.mkdir(parents=True, exist_ok=True)
    for name, record in needed.items():
        path = args.dependencies / name
        if not path.exists() and args.download_dependencies:
            request = urllib.request.Request(record['url'], headers={'User-Agent': 'StarsOfDestiny-release-builder'})
            with urllib.request.urlopen(request, timeout=120) as response:
                data = response.read()
            if digest(data) != record['sha256']:
                raise ValueError('Downloaded dependency checksum mismatch: ' + name)
            path.write_bytes(data)
        if not path.is_file() or sha(path) != record['sha256']:
            raise ValueError(f'Missing or mismatched dependency {name}; put the pinned archive in {args.dependencies} '
                             'or use --download-dependencies. See dependencies.json.')
    return needed


class Stage:
    def __init__(self, directory: Path):
        if directory.exists():
            raise ValueError('Stage exists; choose a fresh output directory: ' + str(directory))
        directory.mkdir(parents=True)
        self.directory = directory
        self.files = []
        self.seen = set()

    def add(self, name: str, data: bytes, role='required'):
        safe_name(name)
        if name.casefold() in self.seen:
            raise ValueError('Duplicate package name: ' + name)
        self.seen.add(name.casefold())
        if Path(name).name.lower() in FORBIDDEN or Path(name).suffix.lower() in ARCHIVE_SUFFIXES:
            raise ValueError('Disallowed distributed file: ' + name)
        path = self.directory / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(data)
        self.files.append(dict(path=name, sha256=digest(data), bytes=len(data), component=role))

    def copy(self, name: str, source: Path, role='required'):
        self.add(name, source.read_bytes(), role)

    def verify(self):
        for entry in self.files:
            if sha(self.directory / entry['path']) != entry['sha256']:
                raise ValueError('Staged file changed: ' + entry['path'])


def archive_members(path: Path):
    with ZipFile(path) as z:
        if z.testzip() is not None:
            raise ValueError('Damaged ZIP: ' + str(path))
        seen = set()
        for entry in z.infolist():
            name = entry.filename.rstrip('/')
            safe_name(name)
            mode = entry.external_attr >> 16
            if stat.S_IFMT(mode) not in (0, stat.S_IFREG, stat.S_IFDIR):
                raise ValueError('Special archive member: ' + entry.filename)
            if entry.is_dir():
                continue
            if name.casefold() in seen:
                raise ValueError('Case-colliding archive path: ' + name)
            seen.add(name.casefold())
            yield name, z.read(entry)


def source_licenses(args):
    """Reconstruct exact corresponding sources; no upstream ZIP is redistributed."""
    notices = {p.name: p.read_bytes() for p in regular_files(HERE / 'licenses/setup')}
    manifest = json.loads(notices['source-manifest.json'])
    for tree in manifest['originalSourceArchives']:
        original = args.dependencies / Path(tree['originalArchive']).name
        if sha(original) != tree['originalArchiveSha256']:
            raise ValueError('Source archive checksum mismatch')
        members = dict(archive_members(original))
        for item in tree['files']:
            data = members[item['sourceArchiveEntry']]
            if digest(data) != item['sha256']:
                raise ValueError('Corresponding source bytes differ: ' + item['file'])
            notices[safe_name(item['file'])] = data
    for entry in manifest['files']:
        if digest(notices[entry['file']]) != entry['sha256']:
            raise ValueError('License/source manifest checksum mismatch: ' + entry['file'])
    return notices


def common(stage, args, *, setup):
    prefix = 'Game Files/' if setup else ''
    for name in RUNTIME_NAMES:
        stage.copy(prefix + 'BepInEx/plugins/SuikodenRandomizer/' + name, args.runtime_dir / name)
    atb_path = 'BepInEx/plugins/' + ATB if setup else 'BepInEx/' + ATB
    stage.add(prefix + atb_path, args.atb_payload, 'atb' if setup else 'dormant-atb')
    with ZipFile(args.dependencies / FIX) as z:
        data = z.read('Suikoden_Fix.dll')
        if digest(data) != FIX_DLL_HASH:
            raise ValueError('Suikoden Fix DLL checksum mismatch')
        stage.add(prefix + 'BepInEx/plugins/Suikoden_Fix.dll', data)
    for name, data in args.launcher_files.items():
        stage.add(prefix + 'StarsOfDestiny Launcher/' + name, data)
    for p in sorted((HERE / 'defaults').glob('*.cfg')):
        role = 'atb' if setup and p.name == 'agilen.suikoden1.battle-preview.cfg' else 'required'
        stage.copy(prefix + 'BepInEx/config/' + p.name, p, role)


def compile_setup(args, stage):
    if args.stage_only:
        return None
    if args.makensis is None:
        raise ValueError('--makensis is required for --mode all unless --stage-only is used')
    target = args.output / artifact_name(args, 'Setup.exe')
    base = Path(tempfile.gettempdir()).resolve()
    temporary = Path(tempfile.mkdtemp(prefix='sod-release-', dir=base)).resolve()
    if temporary.parent != base:
        raise ValueError('Unexpected temporary compiler directory')
    try:
        payload = temporary / 'p'
        shutil.copytree(stage.directory, payload)
        expected = {p.relative_to(stage.directory).as_posix(): sha(p) for p in regular_files(stage.directory)}
        actual = {p.relative_to(payload).as_posix(): sha(p) for p in regular_files(payload)}
        if actual != expected:
            raise ValueError('Temporary compiler input changed')
        result = subprocess.run([str(args.makensis), '/V3', f'/DPACKAGE_DIR={payload}',
                                 f'/DOUTPUT_EXE={target}', str(HERE / 'StarsOfDestiny.Full.nsi')],
                                capture_output=True, text=True, errors='replace', timeout=600)
        (args.output / 'nsis-build.log').write_text(result.stdout + result.stderr, encoding='utf-8')
        if result.returncode or not target.is_file():
            raise RuntimeError('NSIS compilation failed; see nsis-build.log')
        target.with_name(target.name + '.sha256').write_text(sha(target) + '  ' + target.name + '\n', encoding='ascii')
        return dict(name=target.name, sha256=sha(target), bytes=target.stat().st_size)
    finally:
        if temporary.resolve().parent != base or temporary.is_symlink() or temporary.is_junction():
            raise ValueError('Unsafe compiler cleanup target')
        shutil.rmtree(temporary)


def setup(args):
    stage = Stage(args.output / 'setup-stage')
    for name, data in archive_members(args.dependencies / LOADER):
        if not (name.startswith(('BepInEx/core/', 'dotnet/')) or name in
                {'.doorstop_version', 'changelog.txt', 'doorstop_config.ini', 'winhttp.dll'}):
            raise ValueError('Unexpected loader path: ' + name)
        stage.add('Game Files/' + name, data)
    common(stage, args, setup=True)
    for name in ('START HERE.txt', 'Features and defaults.txt', 'Troubleshooting.txt', 'Build information.txt'):
        stage.copy('Game Files/StarsOfDestiny Setup/' + name, HERE / 'docs/setup' / name)
    stage.copy('Install.ps1', HERE / 'Install.ps1')
    for name in ('Install.ps1', 'Restore.ps1', 'Restore.cmd'):
        stage.copy('Game Files/StarsOfDestiny Setup/' + name, HERE / name)
    for name, data in source_licenses(args).items():
        stage.add('Game Files/StarsOfDestiny Setup/Licenses/' + name, data)
    stage.copy('Game Files/StarsOfDestiny Setup/Licenses/Stars-of-Destiny-MIT-LICENSE.txt', REPO / 'LICENSE')
    payload = [entry for entry in stage.files if entry['path'].startswith('Game Files/')]
    manifest = dict(schema='stars-full-package-v1', packageVersion=VERSION, kind='full-release',
        **distribution_metadata(args),
        launcherVersion=LAUNCHER_VERSION, atbVersion=PUBLIC_ATB_VERSION, bepInExVersion='6.0.0-be.697',
        suikodenFixRelease='1.5.2b', gameAssemblySha256=GAME_HASH,
        freshDefaults=FRESH_DEFAULTS,
        sourcePackaging='extracted-folders', supportPackaging='single-installed-copy',
        unusedInstallCmdRemoved=True, published=False, files=payload)
    stage.add('payload-manifest.json', (json.dumps(manifest, indent=2) + '\n').encode())
    stage.verify()
    return dict(payloadFiles=len(payload), stage=str(stage.directory),
                **distribution_metadata(args), installer=compile_setup(args, stage))


def manual(args):
    stage = Stage(args.output / 'manual-stage')
    common(stage, args, setup=False)
    support = 'StarsOfDestiny Manual/'
    for p in regular_files(HERE / 'licenses/manual'):
        stage.copy(support + 'Licenses/' + p.name, p)
    stage.copy(support + 'Licenses/Stars-of-Destiny-MIT-LICENSE.txt', REPO / 'LICENSE')
    for p in regular_files(HERE / 'docs/manual'):
        stage.copy(p.name if p.name in {'START HERE.txt', 'REQUIREMENTS.txt'} else support + p.name, p)
    manifest = dict(schema='stars-of-destiny-mod-only-manual-v1', packageVersion=VERSION,
        **distribution_metadata(args),
        launcherVersion=LAUNCHER_VERSION, freshDefaults=FRESH_DEFAULTS,
        bepInExFilesIncluded=False, externalRequirements=dict(bepInEx='6.0.0-be.697+5362580 Unity IL2CPP Windows x64'),
        optionalAtbDormant=True, supportedGameAssemblySha256=GAME_HASH, gameBuildCheckEnforced=False,
        files=list(stage.files))
    stage.add(support + 'payload-manifest.json', (json.dumps(manifest, indent=2) + '\n').encode())
    verification = dict(schema='stars-of-destiny-source-build-packaging-v1', packageVersion=VERSION,
        **distribution_metadata(args),
        status='passed', packageChecks=['Input dependencies matched pinned archive SHA-256 values.',
            'The launcher publish includes its private runtime and required notices.',
            'Every staged file was recorded by SHA-256; the resulting ZIP is checked byte-for-byte.'],
        reviewedAtbSha256=digest(args.atb_payload), launcherReviewedAtbHashDeclaration=args.reviewed_atb_sha256,
        bepInExFilesIncluded=False, nativeGameBootTested=False, platformApprovalClaimed=False,
        limitation='These are packaging checks for this local rebuild, not the historical published-binary test receipt.')
    stage.add(support + 'Verification.json', (json.dumps(verification, indent=2) + '\n').encode())
    all_names = sorted([entry['path'] for entry in stage.files] + [support + 'FILES.txt', support + 'SHA256SUMS.txt'])
    stage.add(support + 'FILES.txt', ('Paths relative to the game folder. For undo use your backup and MANUAL UPGRADE.txt.\n'
              + '\n'.join(all_names) + '\n').encode())
    sums = ''.join(f"{entry['sha256']}  {entry['path']}\n" for entry in sorted(stage.files, key=lambda x: x['path']))
    stage.add(support + 'SHA256SUMS.txt', sums.encode())
    stage.verify()
    archive = args.output / artifact_name(args, 'Mod-Only-Windows.zip')
    with ZipFile(archive, 'w', ZIP_DEFLATED, compresslevel=6) as z:
        for entry in stage.files:
            z.write(stage.directory / entry['path'], entry['path'])
    with ZipFile(archive) as z:
        if z.testzip() is not None or len(z.namelist()) != len(stage.files):
            raise ValueError('Final ZIP verification failed')
        for entry in stage.files:
            if digest(z.read(entry['path'])) != entry['sha256']:
                raise ValueError('ZIP entry checksum mismatch')
    archive.with_name(archive.name + '.sha256').write_text(sha(archive) + '  ' + archive.name + '\n', encoding='ascii')
    return dict(files=len(stage.files), name=archive.name, sha256=sha(archive), bytes=archive.stat().st_size,
                **distribution_metadata(args))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--mode', choices=('manual', 'all'), required=True)
    parser.add_argument('--runtime-dir', type=Path, required=True, help='Directory containing the four combined runtime DLLs')
    parser.add_argument('--atb-dll', type=Path, required=True)
    parser.add_argument('--launcher-dir', type=Path, required=True, help='Self-contained win-x64 launcher publish directory')
    parser.add_argument('--dependencies', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--package-revision', type=package_revision,
                        help='Optional distribution revision, e.g. r2; runtime version remains ' + VERSION)
    parser.add_argument('--runtime-source-commit', type=source_commit,
                        help='Declared reviewed source commit for reused runtime DLLs; bytes must be verified separately')
    parser.add_argument('--launcher-source-commit', type=source_commit,
                        help='Declared reviewed source commit for the launcher publish; bytes must be verified separately')
    parser.add_argument('--makensis', type=Path)
    parser.add_argument('--reviewed-atb-sha256', help='Same reviewed hash passed as ReviewedAtbSha256 when publishing a custom launcher')
    parser.add_argument('--dotnet', default='dotnet', help='Path to the .NET 10 SDK host for metadata verification')
    parser.add_argument('--download-dependencies', action='store_true')
    parser.add_argument('--stage-only', action='store_true', help='For all mode, qualify the setup stage without invoking NSIS')
    args = parser.parse_args()
    for name in ('runtime_dir', 'atb_dll', 'launcher_dir', 'dependencies', 'output'):
        setattr(args, name, getattr(args, name).resolve())
    if args.makensis:
        args.makensis = args.makensis.resolve()
    for ancestor in (args.output, *args.output.parents):
        if ancestor.is_symlink() or ancestor.is_junction() or (ancestor / 'Suikoden I and II HD Remaster.exe').exists():
            raise ValueError('Release output must not be a linked path or inside a game installation')
    for name in RUNTIME_NAMES:
        if not (args.runtime_dir / name).is_file():
            raise ValueError('Missing runtime DLL: ' + name)
    source = REPO / 'src/StarsOfDestiny.Launcher.Core/GameInstallation.cs'
    reviewed = re.search(r'ReviewedAtbHash\s*=\s*"([0-9A-Fa-f]{64})"', source.read_text())
    expected_atb = args.reviewed_atb_sha256 or (reviewed.group(1) if reviewed else '')
    args.atb_payload = public_atb_payload(args.atb_dll, expected_atb)
    from package_launcher import read_publish
    args.launcher_files = read_publish(args.launcher_dir, digest(args.atb_payload), args.dotnet)
    dependencies(args)
    args.output.mkdir(parents=True, exist_ok=True)
    result = dict(packageVersion=VERSION, builtUtc=datetime.now(timezone.utc).isoformat(),
                  launcherVersion=LAUNCHER_VERSION, **distribution_metadata(args),
                  claim='Packaging checks only; no game launch, native gameplay, install or platform approval is claimed.')
    result['manual'] = manual(args)
    if args.mode == 'all':
        result['setup'] = setup(args)
    save(args.output / 'build-verification.json', result)
    print(json.dumps(result, indent=2))


if __name__ == '__main__':
    main()
