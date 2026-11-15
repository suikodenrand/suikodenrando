#!/usr/bin/env python3
"""Portable 0.14.4 release assembler. It never installs into a game folder."""
from __future__ import annotations

import argparse
from datetime import datetime, timezone
from hashlib import sha256
import json
from pathlib import Path, PurePosixPath
import re
import shutil
import stat
import subprocess
import tempfile
import urllib.request
from zipfile import ZipFile, ZIP_DEFLATED

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[1]
VERSION = '0.14.4'
LAUNCHER_VERSION = '1.0.5'
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
ARCHIVE_SUFFIXES = {'.zip', '.7z', '.rar', '.tar', '.gz', '.tgz', '.cab', '.nupkg'}
FORBIDDEN = {'gameassembly.dll', 'unityplayer.dll', 'gsd1.dll', 'gsd2.dll',
             'logoutput.log', 'launcher-preferences.json', 'install.cmd'}


def digest(data: bytes) -> str:
    return sha256(data).hexdigest()


def sha(path: Path) -> str:
    return digest(path.read_bytes())


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
    stage.copy(prefix + atb_path, args.atb_dll, 'atb' if setup else 'dormant-atb')
    with ZipFile(args.dependencies / FIX) as z:
        data = z.read('Suikoden_Fix.dll')
        if digest(data) != FIX_DLL_HASH:
            raise ValueError('Suikoden Fix DLL checksum mismatch')
        stage.add(prefix + 'BepInEx/plugins/Suikoden_Fix.dll', data)
    for p in regular_files(args.launcher_dir):
        name = p.relative_to(args.launcher_dir).as_posix()
        allowed_json = {'StarsOfDestiny.Launcher.deps.json', 'StarsOfDestiny.Launcher.runtimeconfig.json'}
        allowed_text = {'dotnet10-Core-LICENSE.txt', 'dotnet10-Core-THIRD-PARTY-NOTICES.txt',
                        'dotnet10-WindowsDesktop-LICENSE.txt'}
        if (p.suffix.lower() not in {'.dll', '.exe', '.json', '.txt', '.pdb'} or
                (p.suffix.lower() == '.json' and name not in allowed_json) or
                (p.suffix.lower() == '.txt' and name not in allowed_text) or
                (p.suffix.lower() == '.exe' and name not in {'StarsOfDestiny.Launcher.exe', 'createdump.exe'})):
            raise ValueError('Unexpected file in launcher publish directory: ' + name)
        if p.suffix.lower() != '.pdb':
            stage.copy(prefix + 'StarsOfDestiny Launcher/' + name, p)
    for p in sorted((HERE / 'defaults').glob('*.cfg')):
        role = 'atb' if setup and p.name == 'agilen.suikoden1.battle-preview.cfg' else 'required'
        stage.copy(prefix + 'BepInEx/config/' + p.name, p, role)


def compile_setup(args, stage):
    if args.stage_only:
        return None
    if args.makensis is None:
        raise ValueError('--makensis is required for --mode all unless --stage-only is used')
    target = args.output / f'Stars-of-Destiny-{VERSION}-Setup.exe'
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
    for name in ('START HERE.txt', 'Features and defaults.txt', 'Troubleshooting.txt'):
        stage.copy('Game Files/StarsOfDestiny Setup/' + name, HERE / 'docs/setup' / name)
    stage.copy('Install.ps1', HERE / 'Install.ps1')
    for name in ('Install.ps1', 'Restore.ps1', 'Restore.cmd'):
        stage.copy('Game Files/StarsOfDestiny Setup/' + name, HERE / name)
    for name, data in source_licenses(args).items():
        stage.add('Game Files/StarsOfDestiny Setup/Licenses/' + name, data)
    stage.copy('Game Files/StarsOfDestiny Setup/Licenses/Stars-of-Destiny-MIT-LICENSE.txt', REPO / 'LICENSE')
    payload = [entry for entry in stage.files if entry['path'].startswith('Game Files/')]
    manifest = dict(schema='stars-full-package-v1', packageVersion=VERSION, kind='full-release',
        launcherVersion=LAUNCHER_VERSION, atbVersion='0.10.1', bepInExVersion='6.0.0-be.697',
        suikodenFixRelease='1.5.2b', gameAssemblySha256=GAME_HASH,
        freshDefaults=FRESH_DEFAULTS,
        sourcePackaging='extracted-folders', supportPackaging='single-installed-copy',
        unusedInstallCmdRemoved=True, published=False, files=payload)
    stage.add('payload-manifest.json', (json.dumps(manifest, indent=2) + '\n').encode())
    stage.verify()
    return dict(payloadFiles=len(payload), stage=str(stage.directory), installer=compile_setup(args, stage))


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
        launcherVersion=LAUNCHER_VERSION, freshDefaults=FRESH_DEFAULTS,
        bepInExFilesIncluded=False, externalRequirements=dict(bepInEx='6.0.0-be.697+5362580 Unity IL2CPP Windows x64'),
        optionalAtbDormant=True, supportedGameAssemblySha256=GAME_HASH, gameBuildCheckEnforced=False,
        files=list(stage.files))
    stage.add(support + 'payload-manifest.json', (json.dumps(manifest, indent=2) + '\n').encode())
    verification = dict(schema='stars-of-destiny-source-build-packaging-v1', packageVersion=VERSION,
        status='passed', packageChecks=['Input dependencies matched pinned archive SHA-256 values.',
            'The launcher publish includes its private runtime and required notices.',
            'Every staged file was recorded by SHA-256; the resulting ZIP is checked byte-for-byte.'],
        reviewedAtbSha256=sha(args.atb_dll), launcherReviewedAtbHashDeclaration=args.reviewed_atb_sha256,
        bepInExFilesIncluded=False, nativeGameBootTested=False, platformApprovalClaimed=False,
        limitation='These are packaging checks for this local rebuild, not the historical published-binary test receipt.')
    stage.add(support + 'Verification.json', (json.dumps(verification, indent=2) + '\n').encode())
    all_names = sorted([entry['path'] for entry in stage.files] + [support + 'FILES.txt', support + 'SHA256SUMS.txt'])
    stage.add(support + 'FILES.txt', ('Paths relative to the game folder. For undo use your backup and MANUAL UPGRADE.txt.\n'
              + '\n'.join(all_names) + '\n').encode())
    sums = ''.join(f"{entry['sha256']}  {entry['path']}\n" for entry in sorted(stage.files, key=lambda x: x['path']))
    stage.add(support + 'SHA256SUMS.txt', sums.encode())
    stage.verify()
    archive = args.output / f'Stars-of-Destiny-{VERSION}-Mod-Only-Windows.zip'
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
    return dict(files=len(stage.files), name=archive.name, sha256=sha(archive), bytes=archive.stat().st_size)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--mode', choices=('manual', 'all'), required=True)
    parser.add_argument('--runtime-dir', type=Path, required=True, help='Directory containing the four combined runtime DLLs')
    parser.add_argument('--atb-dll', type=Path, required=True)
    parser.add_argument('--launcher-dir', type=Path, required=True, help='Self-contained win-x64 launcher publish directory')
    parser.add_argument('--dependencies', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--makensis', type=Path)
    parser.add_argument('--reviewed-atb-sha256', help='Same reviewed hash passed as ReviewedAtbSha256 when publishing a custom launcher')
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
    config = json.loads((args.launcher_dir / 'StarsOfDestiny.Launcher.runtimeconfig.json').read_text())
    if 'includedFrameworks' not in config['runtimeOptions'] or not (args.launcher_dir / 'coreclr.dll').is_file():
        raise ValueError('Launcher publish must be self-contained')
    for name in ('dotnet10-Core-LICENSE.txt', 'dotnet10-Core-THIRD-PARTY-NOTICES.txt',
                 'dotnet10-WindowsDesktop-LICENSE.txt'):
        if not (args.launcher_dir / name).is_file() or (args.launcher_dir / name).stat().st_size == 0:
            raise ValueError('Launcher runtime notice is missing: ' + name + '; see BUILDING.md')
    source = REPO / 'src/StarsOfDestiny.Launcher.Core/GameInstallation.cs'
    reviewed = re.search(r'ReviewedAtbHash\s*=\s*"([0-9A-Fa-f]{64})"', source.read_text())
    expected_atb = args.reviewed_atb_sha256 or (reviewed.group(1) if reviewed else '')
    if re.fullmatch(r'[0-9A-Fa-f]{64}', expected_atb) is None or sha(args.atb_dll).lower() != expected_atb.lower():
        raise ValueError('ATB DLL differs from the hash reviewed by this launcher source. Review your rebuilt ATB, '
                         'publish the launcher with -p:ReviewedAtbSha256=<hash>, and pass that same hash through '
                         '--reviewed-atb-sha256. Do not disable the hash check. See BUILDING.md.')
    dependencies(args)
    args.output.mkdir(parents=True, exist_ok=True)
    result = dict(packageVersion=VERSION, builtUtc=datetime.now(timezone.utc).isoformat(),
                  claim='Packaging checks only; no game launch, native gameplay, install or platform approval is claimed.')
    result['manual'] = manual(args)
    if args.mode == 'all':
        result['setup'] = setup(args)
    save(args.output / 'build-verification.json', result)
    print(json.dumps(result, indent=2))


if __name__ == '__main__':
    main()
