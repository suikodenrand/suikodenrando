#!/usr/bin/env python3
"""Package the self-contained Windows launcher; never install, launch or publish.

Build the launcher from the recorded source commit first, then run the existing
launcher ATB build-pairing tests against that publish and the reviewed ATB DLL.
This recipe records the reviewed DLL hash; it does not execute the launcher or
claim to replace those tests by parsing its compiled ATB guard.
"""
from __future__ import annotations

import argparse
from hashlib import sha256
import json
from pathlib import Path, PurePosixPath
import re
import stat
import struct
import subprocess
from zipfile import ZipFile, ZipInfo, ZIP_DEFLATED

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[1]
VERSION = "1.0.5"
SUPPORTED_HOST = "0.14.4"
ROOT = "StarsOfDestiny Launcher/"
SUPPORT = "StarsOfDestiny Launcher Package/"
EXES = {"StarsOfDestiny.Launcher.exe", "createdump.exe"}
JSON_FILES = {"StarsOfDestiny.Launcher.deps.json", "StarsOfDestiny.Launcher.runtimeconfig.json"}
NOTICES = {"dotnet10-Core-LICENSE.txt", "dotnet10-Core-THIRD-PARTY-NOTICES.txt",
           "dotnet10-WindowsDesktop-LICENSE.txt"}
REQUIRED_DLLS = {"StarsOfDestiny.Launcher.dll", "StarsOfDestiny.Launcher.Core.dll",
                 "coreclr.dll", "clrjit.dll", "hostfxr.dll", "hostpolicy.dll",
                 "System.Private.CoreLib.dll", "System.Windows.Forms.dll"}
FORBIDDEN_DLLS = {"gameassembly.dll", "unityplayer.dll", "gsd1.dll", "gsd2.dll",
                  "suikodenrandomizer.dll", "suikodenrandomizer.suikoden2.dll",
                  "suikodenrandomizer.s1battlepreview.dll", "suikoden_fix.dll",
                  "winhttp.dll", "dobby.dll"}
ZIP_TIME = (2026, 11, 15, 0, 0, 0)

START_HERE = """STARS OF DESTINY LAUNCHER 1.0.5 - OPTIONAL WINDOWS DOWNLOAD
For Stars of Destiny randomizer plug-ins 0.14.4 | Steam | Windows x64

This is the launcher only. Install the randomizer plug-ins, Suikoden Fix and
the required BepInEx 6 Unity IL2CPP Windows x64 loader separately. This ZIP
does not install or replace them and cannot randomize the game by itself.
It contains the launcher's complete private .NET 10 runtime; a separate
.NET runtime, SDK or compiler is not required.

MANUAL INSTALL / UPGRADE
1. Close both the game and any running launcher.
2. In Steam, open the game's Properties > Installed Files > Browse, or use
   Manage > Browse local files. Locate Suikoden I and II HD Remaster.exe.
3. Back up the entire existing StarsOfDestiny Launcher folder outside the
   game directory, including launcher-preferences.json if it exists.
   Keep a paired backup of Save and BepInEx/config before editing settings.
4. Extract this ZIP outside the game folder first. Copy its entire
   StarsOfDestiny Launcher folder directly beside the game EXE. When
   upgrading, replace the old launcher folder as a complete set, rather
   than mixing runtime files or copying only the launcher EXE.
5. If you want to retain launcher preferences, copy only your backed-up
   launcher-preferences.json into the new launcher folder. Keep the backup.
   Do not copy older DLLs or .NET runtime files into the replacement folder.
6. Open StarsOfDestiny Launcher/StarsOfDestiny.Launcher.exe. Confirm the
   detected game folder and settings before applying or launching.

Simply copying these files does not change randomizer settings or saves.
Applying settings or launching through the launcher is a separate action.
Saved campaigns retain their own seed, mapping and joining policies.
The optional S1 ATB DLL is not bundled here. Existing reviewed ATB can be
activated through the launcher when the plug-in package supplies it; turn
off S1 shuffled Unite attacks before enabling ATB. S2 has no ATB support.

To undo this launcher-only upgrade, close the game and launcher, preserve
any new preferences you want to keep, and restore your complete backed-up
launcher folder. Do not remove BepInEx or any game, plug-in or save files.

Changes: StarsOfDestiny Launcher Package/CHANGELOG.md
Project license and file checksums are in StarsOfDestiny Launcher Package.
Microsoft runtime licenses/notices remain inside StarsOfDestiny Launcher.
This package has not been installed or gameplay-tested by this recipe.
"""


def digest(data: bytes) -> str:
    return sha256(data).hexdigest()


def json_bytes(value) -> bytes:
    return (json.dumps(value, indent=2, sort_keys=True) + "\n").encode("utf-8")


def safe_name(name: str) -> str:
    if (not name or PurePosixPath(name).is_absolute() or "\\" in name or ":" in name
            or any(c in name for c in "\0\r\n")
            or any(p in {"", ".", ".."} or p.endswith((".", " "))
                   or re.fullmatch(r"(?i)(con|prn|aux|nul|com[1-9]|lpt[1-9])(?:\..*)?", p)
                   for p in name.split("/"))):
        raise ValueError("Unsafe package path: " + name)
    return name


def checked_path(path: Path) -> Path:
    absolute = path.absolute()
    for ancestor in (absolute, *absolute.parents):
        if ancestor.is_symlink() or ancestor.is_junction():
            raise ValueError("Linked input/output path: " + str(ancestor))
    return absolute.resolve()


def pe_machine(data: bytes) -> int:
    if len(data) < 64 or data[:2] != b"MZ":
        raise ValueError("Expected a Windows PE executable")
    offset = struct.unpack_from("<I", data, 0x3C)[0]
    if offset + 6 > len(data) or data[offset:offset + 4] != b"PE\0\0":
        raise ValueError("Invalid Windows PE header")
    return struct.unpack_from("<H", data, offset + 4)[0]


def read_publish(folder: Path) -> dict[str, bytes]:
    if not folder.is_dir():
        raise ValueError("Missing complete launcher publish folder")
    files = {}
    seen = set()
    for path in sorted(folder.rglob("*")):
        if path.is_symlink() or path.is_junction():
            raise ValueError("Linked publish file or directory: " + str(path))
        if not path.is_file():
            continue
        name = safe_name(path.relative_to(folder).as_posix())
        if name.casefold() in seen:
            raise ValueError("Case-colliding publish path: " + name)
        seen.add(name.casefold())
        suffix = path.suffix.lower()
        if suffix == ".pdb":
            continue
        if (suffix not in {".dll", ".exe", ".json", ".txt"}
                or (suffix == ".exe" and name not in EXES)
                or (suffix == ".json" and name not in JSON_FILES)
                or (suffix == ".txt" and name not in NOTICES)
                or path.name.casefold() in FORBIDDEN_DLLS):
            raise ValueError("Unexpected publish file (personal settings, scripts and archives are not allowed): " + name)
        files[name] = path.read_bytes()
    required = EXES | JSON_FILES | NOTICES | REQUIRED_DLLS
    if any(name not in files or not files[name] for name in required):
        raise ValueError("Incomplete launcher/runtime/notices: " + ", ".join(sorted(
            name for name in required if name not in files or not files[name])))
    for name in (*EXES, "coreclr.dll", "clrjit.dll", "hostfxr.dll", "hostpolicy.dll"):
        if pe_machine(files[name]) != 0x8664:
            raise ValueError("Expected win-x64 executable/native runtime: " + name)
    deps = json.loads(files["StarsOfDestiny.Launcher.deps.json"])
    own_key = "StarsOfDestiny.Launcher/" + VERSION
    target = deps["runtimeTarget"]["name"]
    if (not target.endswith("/win-x64")
            or own_key not in deps["libraries"]
            or deps["libraries"][own_key].get("type") != "project"
            or own_key not in deps["targets"].get(target, {})
            or any(k.startswith("StarsOfDestiny.Launcher/") and k != own_key for k in deps["libraries"])):
        raise ValueError("Publish dependencies must identify StarsOfDestiny.Launcher/1.0.5 for win-x64")
    declared_assets = set()
    for library in deps["targets"][target].values():
        for asset_type in ("runtime", "native", "resources"):
            for name in library.get(asset_type, {}):
                safe_name(name)
                if not name.endswith("/_._"):
                    declared_assets.add(name)
    missing_assets = {name for name in declared_assets if name not in files or not files[name]}
    if missing_assets:
        raise ValueError("Publish is missing dependency-declared runtime assets: " + ", ".join(sorted(missing_assets)))
    options = json.loads(files["StarsOfDestiny.Launcher.runtimeconfig.json"])["runtimeOptions"]
    frameworks = {x["name"]: x["version"] for x in options.get("includedFrameworks", [])}
    if (options.get("tfm") != "net10.0" or options.get("framework") or options.get("frameworks")
            or any(not frameworks.get(n, "").startswith("10.")
                   for n in ("Microsoft.NETCore.App", "Microsoft.WindowsDesktop.App"))):
        raise ValueError("Launcher must include its private .NET 10 Core and Windows Desktop runtime")
    return files


def check_source_commit(source_commit: str):
    def git(*args):
        result = subprocess.run(["git", *args], cwd=REPO, capture_output=True, text=True, timeout=30)
        if result.returncode:
            raise ValueError("Unable to verify the packaging source repository")
        return result.stdout.strip()
    if git("rev-parse", "HEAD").lower() != source_commit.lower():
        raise ValueError("--source-commit must match the current source repository HEAD")
    if git("status", "--porcelain=v1", "--untracked-files=all"):
        raise ValueError("Commit all source, packaging and documentation changes before packaging; the worktree must be clean")


def add(files: dict[str, bytes], name: str, data: bytes):
    safe_name(name)
    if name.casefold() in {n.casefold() for n in files}:
        raise ValueError("Duplicate package path: " + name)
    files[name] = data


def package(launcher_dir: Path, atb_dll: Path, output: Path, source_commit: str):
    if re.fullmatch(r"[0-9a-fA-F]{40}", source_commit) is None:
        raise ValueError("--source-commit requires a full 40-character Git commit SHA")
    check_source_commit(source_commit)
    launcher_dir, atb_dll, output = map(checked_path, (launcher_dir, atb_dll, output))
    if output.exists():
        raise ValueError("Choose a fresh output directory: " + str(output))
    if any((p / "Suikoden I and II HD Remaster.exe").exists() for p in (output, *output.parents)):
        raise ValueError("Output must not be inside a game installation")
    if not atb_dll.is_file() or atb_dll.name != "SuikodenRandomizer.S1BattlePreview.dll":
        raise ValueError("--atb-dll must identify the reviewed SuikodenRandomizer.S1BattlePreview.dll")
    atb_hash = digest(atb_dll.read_bytes())
    publish = read_publish(launcher_dir)
    notes = REPO / "docs/launcher-1.0.5.md"
    license_file = REPO / "LICENSE"
    if not notes.is_file() or not notes.read_text(encoding="utf-8").strip():
        raise ValueError("Missing docs/launcher-1.0.5.md release notes")
    if not license_file.is_file() or not license_file.read_bytes():
        raise ValueError("Missing project MIT LICENSE")
    files = {ROOT + name: data for name, data in publish.items()}
    add(files, "LAUNCHER - START HERE.txt", START_HERE.encode("utf-8"))
    add(files, SUPPORT + "CHANGELOG.md", notes.read_bytes())
    add(files, SUPPORT + "Stars-of-Destiny-MIT-LICENSE.txt", license_file.read_bytes())
    add(files, SUPPORT + "Third-party notices.txt", (
        "MICROSOFT .NET 10 RUNTIME\n\n"
        "This optional launcher includes its self-contained Microsoft .NET runtime.\n"
        "Keep all three runtime license/notice files inside StarsOfDestiny Launcher:\n"
        + "".join("  " + n + "\n" for n in sorted(NOTICES))
        + "\nProject code is MIT licensed; dependencies retain their original terms.\n"
        "The game, BepInEx, randomizer host, Suikoden Fix and optional ATB plug-in\n"
        "are not included in this launcher-only ZIP.\n").encode("utf-8"))
    records = [{"path": name, "sha256": digest(data), "bytes": len(data)} for name, data in sorted(files.items())]
    manifest = {
        "schema": "stars-of-destiny-optional-launcher-v1", "launcherVersion": VERSION,
        "supportedRandomizerHostVersion": SUPPORTED_HOST, "distribution": "optional-launcher-windows-x64",
        "sourceCommit": source_commit.lower(),
        "sourceUrl": "https://github.com/suikodenrand/suikodenrando/tree/" + source_commit.lower(),
        "reviewedAtbSha256": atb_hash, "atbDllIncluded": False,
        "compiledAtbGuardVerification": "Run StarsOfDestiny.Launcher.AtbBuild.Tests separately against this publish and reviewed ATB DLL.",
        "recipeSha256": digest(Path(__file__).read_bytes()), "selfContainedRuntime": True,
        "gameAndLoaderAndPluginsIncluded": False, "files": records,
    }
    add(files, SUPPORT + "payload-manifest.json", json_bytes(manifest))
    all_names = sorted([*files, SUPPORT + "FILES.txt", SUPPORT + "SHA256SUMS.txt"])
    add(files, SUPPORT + "FILES.txt", ("Paths relative to the game folder; back up the entire old launcher folder before replacing it.\n"
                                       + "\n".join(all_names) + "\n").encode("utf-8"))
    add(files, SUPPORT + "SHA256SUMS.txt", "".join(
        f"{digest(data)}  {name}\n" for name, data in sorted(files.items())).encode("utf-8"))
    output.mkdir(parents=True)
    archive_path = output / f"Stars-of-Destiny-Launcher-{VERSION}-Windows.zip"
    with ZipFile(archive_path, "x", ZIP_DEFLATED, compresslevel=6) as archive:
        for name, data in sorted(files.items()):
            entry = ZipInfo(name, ZIP_TIME)
            entry.compress_type = ZIP_DEFLATED
            entry.create_system = 3
            entry.external_attr = (stat.S_IFREG | 0o644) << 16
            archive.writestr(entry, data, compresslevel=6)
    with ZipFile(archive_path) as archive:
        if archive.testzip() is not None or set(archive.namelist()) != set(files) or len(archive.infolist()) != len(files):
            raise ValueError("Final ZIP CRC/member verification failed")
        for name, data in files.items():
            if archive.read(name) != data:
                raise ValueError("Final ZIP byte verification failed: " + name)
    result = {
        "name": archive_path.name, "sha256": digest(archive_path.read_bytes()),
        "bytes": archive_path.stat().st_size, "files": len(files), "status": "passed",
        "launcherVersion": VERSION, "supportedRandomizerHostVersion": SUPPORTED_HOST,
        "sourceCommit": source_commit.lower(), "reviewedAtbSha256": atb_hash,
        "checks": ["Complete self-contained win-x64 launcher 1.0.5", "Private runtime license notices preserved",
                   "No personal preferences, scripts, nested archives, links or unexpected executable files",
                   "Every ZIP member and CRC checked against original input bytes"],
        "claim": "Packaging checks only; no install, launch, native gameplay, compiled ATB guard check or publication performed.",
    }
    archive_path.with_name(archive_path.name + ".sha256").write_text(
        result["sha256"] + "  " + archive_path.name + "\n", encoding="ascii")
    (output / "launcher-package-verification.json").write_bytes(json_bytes(result))
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--launcher-dir", type=Path, required=True)
    parser.add_argument("--atb-dll", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--source-commit", required=True)
    args = parser.parse_args()
    print(json.dumps(package(args.launcher_dir, args.atb_dll, args.output, args.source_commit), indent=2))


if __name__ == "__main__":
    main()
