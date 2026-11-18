#!/usr/bin/env python3
"""Split the exact published 0.14.5 manual ZIP without rebuilding its binaries.

Creates a plugins/configuration/manual-instructions ZIP and a separate optional
self-contained launcher ZIP. Never installs, executes, downloads or publishes.
"""
from __future__ import annotations

import argparse
import configparser
from hashlib import sha256
from io import BytesIO
import json
from pathlib import Path, PurePosixPath
import re
import stat
from zipfile import ZipFile, ZipInfo, ZIP_DEFLATED, ZIP_STORED

HERE = Path(__file__).resolve().parent
VERSION = "0.14.5"
SOURCE_TAG = "https://github.com/suikodenrand/suikodenrando/tree/v0.14.5"
ZIP_TIME = (2026, 11, 15, 0, 0, 0)
LAUNCHER = "StarsOfDestiny Launcher/"
PLUGIN_SUPPORT = "StarsOfDestiny Manual/"
LAUNCHER_SUPPORT = "StarsOfDestiny Launcher Package/"
RUNTIME_NAMES = {
    "SuikodenRandomizer.dll", "SuikodenRandomizer.Core.dll",
    "SuikodenRandomizer.Suikoden2.Core.dll", "SuikodenRandomizer.Shared.dll",
}
ATB_NAME = "SuikodenRandomizer.S1BattlePreview.dll"
CFG_NAMES = {
    "agilen.starsofdestiny.cfg", "agilen.suikoden.recruitment_randomizer.cfg",
    "agilen.suikoden2.recruitment_randomizer.cfg",
    "agilen.suikoden1.battle-preview.cfg", "d3xMachina.suikoden_fix.cfg",
}
PLUGIN_PATHS = ({"BepInEx/plugins/SuikodenRandomizer/" + x for x in RUNTIME_NAMES}
                | {"BepInEx/plugins/Suikoden_Fix.dll", "BepInEx/" + ATB_NAME}
                | {"BepInEx/config/" + x for x in CFG_NAMES})
ARCHIVES = {".zip", ".7z", ".rar", ".tar", ".gz", ".tgz", ".cab", ".nupkg"}
SCRIPTS_EXES = {".exe", ".cmd", ".bat", ".ps1", ".psm1", ".vbs", ".js", ".jse",
                ".wsf", ".wsh", ".com", ".scr", ".msi", ".msp", ".hta", ".sh"}
NOTICE_NAMES = {"dotnet10-Core-LICENSE.txt", "dotnet10-Core-THIRD-PARTY-NOTICES.txt",
                "dotnet10-WindowsDesktop-LICENSE.txt"}
REQUIRED_TEMPLATES = {
    "plugins": {"START HERE.txt", "REQUIREMENTS.txt", PLUGIN_SUPPORT + "SETTINGS.txt",
                PLUGIN_SUPPORT + "MANUAL UPGRADE.txt", PLUGIN_SUPPORT + "Troubleshooting.txt",
                PLUGIN_SUPPORT + "Features and defaults.txt", PLUGIN_SUPPORT + "Licenses/Third-party notices.txt"},
    "launcher": {"LAUNCHER - START HERE.txt", LAUNCHER_SUPPORT + "Third-party notices.txt"},
}


def digest(data: bytes) -> str:
    return sha256(data).hexdigest()


def json_bytes(value) -> bytes:
    return (json.dumps(value, indent=2, sort_keys=True) + "\n").encode("utf-8")


def safe_name(name: str) -> str:
    path = PurePosixPath(name)
    if (not name or path.is_absolute() or "\\" in name or ":" in name
            or any(c in name for c in "\r\n\0")
            or any(part in {"", ".", ".."} or part.endswith((".", " "))
                   or re.fullmatch(r"(?i)(con|prn|aux|nul|com[1-9]|lpt[1-9])(?:\..*)?", part)
                   for part in name.split("/"))):
        raise ValueError("Unsafe package path: " + name)
    return name


def read_zip(path: Path) -> dict[str, bytes]:
    result = {}
    seen = set()
    with ZipFile(path) as archive:
        if archive.testzip() is not None:
            raise ValueError("ZIP CRC failure: " + str(path))
        for entry in archive.infolist():
            name = safe_name(entry.filename.rstrip("/"))
            mode = stat.S_IFMT(entry.external_attr >> 16)
            if (entry.flag_bits & 1 or mode not in (0, stat.S_IFREG, stat.S_IFDIR)
                    or entry.compress_type not in (ZIP_STORED, ZIP_DEFLATED)):
                raise ValueError("Encrypted, linked or unsupported ZIP entry: " + name)
            if name.casefold() in seen:
                raise ValueError("Duplicate/case-colliding ZIP path: " + name)
            seen.add(name.casefold())
            if entry.is_dir():
                continue
            if PurePosixPath(name).suffix.lower() in ARCHIVES:
                raise ValueError("Nested archive: " + name)
            result[name] = archive.read(entry)
    return result


def read_source(path: Path, expected_hash: str) -> dict[str, bytes]:
    contents = path.read_bytes()
    if not re.fullmatch(r"[a-fA-F0-9]{64}", expected_hash) or digest(contents) != expected_hash.lower():
        raise ValueError("Input must match the SHA-256 of the verified manual ZIP")
    files = read_zip(BytesIO(contents))
    if {n for n in files if n.startswith("BepInEx/")} != PLUGIN_PATHS:
        raise ValueError("Published package member set differs from the reviewed source")
    if any(not (n.startswith(("BepInEx/", LAUNCHER, PLUGIN_SUPPORT))
                    or n in {"START HERE.txt", "REQUIREMENTS.txt"}) for n in files):
        raise ValueError("Unexpected published-package file")
    metadata = {PLUGIN_SUPPORT + name for name in ("payload-manifest.json", "Verification.json", "FILES.txt", "SHA256SUMS.txt")}
    manifest = json.loads(files[PLUGIN_SUPPORT + "payload-manifest.json"])
    receipt = json.loads(files[PLUGIN_SUPPORT + "Verification.json"])
    if (manifest.get("schema") != "stars-of-destiny-mod-only-manual-v1"
            or manifest.get("packageVersion") != VERSION or manifest.get("launcherVersion") != "1.0.6"
            or manifest.get("optionalAtbDormant") is not True or manifest.get("bepInExFilesIncluded") is not False
            or receipt.get("status") != "passed" or receipt.get("packageVersion") != VERSION
            or receipt.get("reviewedAtbSha256") != digest(files["BepInEx/" + ATB_NAME])):
        raise ValueError("Expected the verified current manual release and matching optional ATB")
    records = manifest["files"]
    if (len(records) != len(files) - len(metadata)
            or {entry["path"] for entry in records} != set(files) - metadata):
        raise ValueError("Manual release manifest membership differs from the input archive")
    for entry in records:
        data = files[entry["path"]]
        if entry["bytes"] != len(data) or entry["sha256"] != digest(data):
            raise ValueError("Manual release manifest hash mismatch: " + entry["path"])
    return files


def add(files: dict[str, bytes], name: str, data: bytes):
    safe_name(name)
    if name.casefold() in {n.casefold() for n in files}:
        raise ValueError("Duplicate output path: " + name)
    files[name] = data


def templates(folder: Path) -> dict[str, bytes]:
    if not folder.is_dir():
        raise ValueError("Missing distribution templates: " + str(folder))
    result = {}
    for path in sorted(folder.rglob("*")):
        if path.is_symlink() or path.is_junction():
            raise ValueError("Linked template: " + str(path))
        if path.is_file():
            name = path.relative_to(folder).as_posix()
            if path.suffix.lower() not in {".txt", ".md"}:
                raise ValueError("Unexpected template type: " + name)
            add(result, name, path.read_bytes())
    return result


def configuration(files: dict[str, bytes], name: str) -> configparser.ConfigParser:
    config = configparser.ConfigParser(interpolation=None)
    config.read_string(files["BepInEx/config/" + name].decode("utf-8-sig").replace("\r", ""))
    return config


def check_defaults(files: dict[str, bytes]):
    for name, weapon in (("agilen.suikoden.recruitment_randomizer.cfg", "TirVariance"),
                         ("agilen.suikoden2.recruitment_randomizer.cfg", "RiouVariance")):
        config = configuration(files, name)
        if (config["Shuffle"]["JoiningEquipment"] != "MatchRole"
                or config["Shuffle"]["JoiningWeaponLevel"] != weapon
                or config["Shuffle"]["Mode"] != "Shuffle"
                or config["Start"]["Point"] != "Opening"
                or not config.getboolean("Battle", "ShuffledUnitesPreview")):
            raise ValueError("Release default policy changed: " + name)
    s1 = configuration(files, "agilen.suikoden.recruitment_randomizer.cfg")
    atb = configuration(files, "agilen.suikoden1.battle-preview.cfg")
    shared = configuration(files, "agilen.starsofdestiny.cfg")
    if (s1.getboolean("Treasure", "Enabled") or s1.getboolean("Treasure", "ShuffleChests")
            or atb.getboolean("Preview", "Enabled") or shared["Interface"]["ViewMode"] != "Hidden"):
        raise ValueError("Chest, ATB or F6 defaults changed")


def verify_payload(files: dict[str, bytes], source: dict[str, bytes], kind: str):
    seen = set()
    for name in files:
        safe_name(name)
        if name.casefold() in seen:
            raise ValueError("Case-colliding output: " + name)
        seen.add(name.casefold())
        suffix = PurePosixPath(name).suffix.lower()
        if suffix in ARCHIVES:
            raise ValueError("Nested output archive: " + name)
        if kind == "plugins" and suffix in SCRIPTS_EXES:
            raise ValueError("Executable or script in plugins-only distribution: " + name)
        if not name.startswith(LAUNCHER) and (name.startswith(("BepInEx/core/", "dotnet/"))
                or PurePosixPath(name).name.casefold() in {"winhttp.dll", "doorstop_config.ini", ".doorstop_version"}):
            raise ValueError("Loader file in split output: " + name)
    expected = {n: data for n, data in source.items()
                if n.startswith("BepInEx/" if kind == "plugins" else LAUNCHER)}
    actual = {n: data for n, data in files.items()
              if n.startswith("BepInEx/" if kind == "plugins" else LAUNCHER)}
    if actual != expected:
        raise ValueError("Split runtime/configuration bytes or member set changed: " + kind)
    if kind == "plugins":
        if any(n.startswith(LAUNCHER) or (n.lower().endswith(".dll") and n not in PLUGIN_PATHS) for n in files):
            raise ValueError("Unexpected binary in plugins distribution")
        check_defaults(files)
    elif kind == "launcher":
        if any(n.startswith("BepInEx/") for n in files):
            raise ValueError("Plugin/configuration unexpectedly in optional launcher")
        options = json.loads(files[LAUNCHER + "StarsOfDestiny.Launcher.runtimeconfig.json"])["runtimeOptions"]
        if ("includedFrameworks" not in options or LAUNCHER + "coreclr.dll" not in files
                or any(LAUNCHER + n not in files or not files[LAUNCHER + n] for n in NOTICE_NAMES)):
            raise ValueError("Optional launcher runtime or notices incomplete")
    else:
        raise ValueError("Unknown distribution")


def add_metadata(files: dict[str, bytes], kind: str, recipe_sha256: str, source_hash: str):
    support = PLUGIN_SUPPORT if kind == "plugins" else LAUNCHER_SUPPORT
    records = [{"path": name, "sha256": digest(data), "bytes": len(data)} for name, data in sorted(files.items())]
    manifest = {
        "schema": "stars-of-destiny-split-distribution-v1", "packageVersion": VERSION,
        "distribution": "plugins-only-nexus" if kind == "plugins" else "optional-launcher-windows",
        "launcherVersion": "1.0.6" if kind == "launcher" else None,
        "binarySource": SOURCE_TAG, "sourceManualZipSha256": source_hash,
        "packagingRecipeSha256": recipe_sha256,
        "bepInExFilesIncluded": False, "files": records,
        "optionalAtbDormant": kind == "plugins",
        "claim": "Exact released binary bytes repackaged; no gameplay or platform-approval claim.",
    }
    if kind == "plugins":
        manifest["reviewedAtbSha256"] = digest(files["BepInEx/" + ATB_NAME])
    add(files, support + "payload-manifest.json", json_bytes(manifest))
    all_names = sorted([*files, support + "FILES.txt", support + "SHA256SUMS.txt"])
    add(files, support + "FILES.txt", ("Paths relative to the game directory; retain backups before manual installation.\n"
                                       + "\n".join(all_names) + "\n").encode("utf-8"))
    sums = "".join(f"{digest(data)}  {name}\n" for name, data in sorted(files.items()))
    add(files, support + "SHA256SUMS.txt", sums.encode("utf-8"))


def write_zip(path: Path, files: dict[str, bytes]):
    with ZipFile(path, "x", ZIP_DEFLATED, compresslevel=6) as archive:
        for name, data in sorted(files.items()):
            entry = ZipInfo(name, ZIP_TIME)
            entry.compress_type = ZIP_DEFLATED
            entry.create_system = 3
            entry.external_attr = (stat.S_IFREG | 0o644) << 16
            archive.writestr(entry, data, compresslevel=6)
    if read_zip(path) != files:
        raise ValueError("Final ZIP bytes/member set mismatch: " + str(path))
    checksum = digest(path.read_bytes())
    path.with_name(path.name + ".sha256").write_text(checksum + "  " + path.name + "\n", encoding="ascii")
    return {"name": path.name, "sha256": checksum, "bytes": path.stat().st_size, "files": len(files)}


def build(manual_zip: Path, output: Path, template_root: Path, source_hash: str):
    raw_output = output.absolute()
    for ancestor in (raw_output, *raw_output.parents):
        if (ancestor.is_symlink() or ancestor.is_junction()
                or (ancestor / "Suikoden I and II HD Remaster.exe").exists()):
            raise ValueError("Output must not be linked or inside a game installation")
    manual_zip, output, template_root = manual_zip.resolve(), raw_output.resolve(), template_root.resolve()
    if output.exists():
        raise ValueError("Choose a fresh output directory: " + str(output))
    source_hash = source_hash.lower()
    source = read_source(manual_zip, source_hash)
    plugin_files = {n: data for n, data in source.items() if n.startswith("BepInEx/")}
    launcher_files = {n: data for n, data in source.items() if n.startswith(LAUNCHER)}
    add(plugin_files, PLUGIN_SUPPORT + "Licenses/Stars-of-Destiny-MIT-LICENSE.txt",
        source[PLUGIN_SUPPORT + "Licenses/Stars-of-Destiny-MIT-LICENSE.txt"])
    add(plugin_files, PLUGIN_SUPPORT + "Licenses/Suikoden-Fix-LICENSE.txt",
        source[PLUGIN_SUPPORT + "Licenses/Suikoden-Fix-LICENSE.txt"])
    add(launcher_files, LAUNCHER_SUPPORT + "Stars-of-Destiny-MIT-LICENSE.txt",
        source[PLUGIN_SUPPORT + "Licenses/Stars-of-Destiny-MIT-LICENSE.txt"])
    for kind, files in (("plugins", plugin_files), ("launcher", launcher_files)):
        instructions = templates(template_root / kind)
        if not REQUIRED_TEMPLATES[kind].issubset(instructions):
            raise ValueError("Missing required manual instructions/notices: "
                             + ", ".join(sorted(REQUIRED_TEMPLATES[kind] - instructions.keys())))
        for name, data in instructions.items():
            add(files, name, data)
        support = PLUGIN_SUPPORT + "Licenses/" if kind == "plugins" else LAUNCHER_SUPPORT
        if support + "Third-party notices.txt" not in files:
            raise ValueError("Distribution-specific third-party notices missing: " + kind)
        verify_payload(files, source, kind)
        add_metadata(files, kind, digest(Path(__file__).read_bytes()), source_hash)
        verify_payload(files, source, kind)
    output.mkdir(parents=True)
    artifacts = [write_zip(output / f"Stars-of-Destiny-{VERSION}-Plugins-Only-Nexus.zip", plugin_files),
                 write_zip(output / f"Stars-of-Destiny-{VERSION}-Optional-Launcher-Windows.zip", launcher_files)]
    report = {
        "schema": "stars-of-destiny-split-build-verification-v1", "packageVersion": VERSION,
        "sourceManualZipSha256": source_hash, "binarySource": SOURCE_TAG,
        "status": "passed", "artifacts": artifacts,
        "checks": ["Pinned source ZIP SHA-256", "Safe paths and no duplicate, linked, encrypted or nested archive members",
                   "Exact released binary/configuration bytes", "No EXE or scripts in Nexus plugins ZIP",
                   "Complete launcher private runtime and notices", "Optional ATB outside active plugins tree",
                   "Original disabled ATB/chest and per-game joining defaults", "Final ZIP CRC and every member hash"],
        "claim": "Packaging checks only; no installation, game execution, native playtesting or platform approval claimed.",
    }
    (output / "build-verification.json").write_bytes(json_bytes(report))
    (output / "SHA256SUMS-Split-0.14.5.txt").write_text(
        "".join(f"{x['sha256']}  {x['name']}\n" for x in artifacts), encoding="ascii")
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--manual-zip", type=Path, required=True)
    parser.add_argument("--manual-sha256", required=True, help="SHA-256 from the verified manual release build")
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--templates", type=Path, default=HERE / "nexus-templates")
    args = parser.parse_args()
    print(json.dumps(build(args.manual_zip, args.output, args.templates, args.manual_sha256), indent=2))


if __name__ == "__main__":
    main()
