#!/usr/bin/env python3
"""Build the optional Windows CTB download from a completed 0.14.6 release build."""
import argparse
import json
from pathlib import Path
import stat
import tempfile
from zipfile import ZIP_DEFLATED, ZipFile, ZipInfo

from build import ATB, PACKAGE_REVISION, PUBLIC_ATB_VERSION, VERSION as HOST_VERSION, public_atb_payload, source_commit
from package_launcher import REPO, ROOT, VERSION as LAUNCHER_VERSION, ZIP_TIME, checked_path, digest, json_bytes, read_publish

PREVIEW_VERSION = "0.5.2"
SUPPORT = "Stars of Destiny CTB Preview/"


def package(build: Path, output: Path, dotnet: str, launcher_dir: Path | None = None,
            runtime_source: str | None = None, launcher_source: str | None = None) -> dict:
    build, output = checked_path(build), checked_path(output)
    if any((p / "Suikoden I and II HD Remaster.exe").exists() for p in (output, *output.parents)):
        raise ValueError("Choose an output directory outside the game installation")
    report = json.loads(checked_path(build / "release-build.json").read_text(encoding="utf-8-sig"))
    if report["version"] != HOST_VERSION:
        raise ValueError(f"The CTB preview requires the {HOST_VERSION} release build")
    battle = public_atb_payload(checked_path(build / "atb" / ATB), report["reviewedAtbSha256"])
    battle_hash = digest(battle)
    launcher = read_publish(checked_path(launcher_dir or build / "launcher"), battle_hash, dotnet)
    sources = {}
    for component, commit in (("runtime", runtime_source or report.get("sourceCommit")),
                              ("launcher", launcher_source or (None if launcher_dir else report.get("sourceCommit")))):
        if commit:
            sources[component] = source_commit(commit)
    files = {ROOT + name: data for name, data in launcher.items()}
    files["BepInEx/" + ATB] = battle
    for source, archive_name in (
        ("docs/CTB-PROTOTYPE.md", "README.md"),
        ("LICENSE", "LICENSE.txt"),
        ("THIRD_PARTY_NOTICES.md", "THIRD_PARTY_NOTICES.md"),
        ("packaging/ctb/examples/s1-ctb-tuning.json", "Examples/s1-ctb-tuning.json"),
    ):
        files[SUPPORT + archive_name] = (REPO / source).read_bytes()
    metadata = {
        "schema": "stars-of-destiny-ctb-preview-v1",
        "ctbVersion": PREVIEW_VERSION, "packageRevision": PACKAGE_REVISION, "binarySourceCommits": sources,
        "battlePluginVersion": PUBLIC_ATB_VERSION,
        "randomizerVersion": HOST_VERSION, "launcherVersion": LAUNCHER_VERSION, "editorVersion": "0.5.1",
        "reviewedAtbSha256": battle_hash, "ctbEnabledByDefault": False,
        "files": [{"path": name, "sha256": digest(data), "bytes": len(data)} for name, data in sorted(files.items())],
    }
    files[SUPPORT + "payload-manifest.json"] = json_bytes(metadata)
    output.mkdir(parents=True, exist_ok=True)
    archive_path = output / f"Stars-of-Destiny-S1-CTB-Preview-{PREVIEW_VERSION}-{PACKAGE_REVISION}-Windows.zip"
    checksum = archive_path.with_name(archive_path.name + ".sha256")
    verification = output / "ctb-package-verification.json"
    if any(p.exists() for p in (archive_path, checksum, verification)):
        raise FileExistsError("The preview output already exists. Choose a fresh directory.")
    with tempfile.TemporaryDirectory(dir=output) as staging:
        staged = Path(staging) / archive_path.name
        with ZipFile(staged, "x") as archive:
            for name, data in sorted(files.items()):
                entry = ZipInfo(name, ZIP_TIME)
                entry.compress_type = ZIP_DEFLATED
                entry.create_system = 3
                entry.external_attr = (stat.S_IFREG | 0o644) << 16
                archive.writestr(entry, data, compresslevel=6)
        with ZipFile(staged) as archive:
            if len(archive.infolist()) != len(files) or set(archive.namelist()) != set(files):
                raise ValueError("Final ZIP member list differs from the release payload")
            for name, data in files.items():
                if archive.read(name) != data:
                    raise ValueError("Final ZIP member differs from the release input: " + name)
        result = {"name": archive_path.name, "sha256": digest(staged.read_bytes()), "bytes": staged.stat().st_size,
                  "files": len(files), "reviewedAtbSha256": battle_hash, "status": "passed"}
        staged.rename(archive_path)
    checksum.write_text(f"{result['sha256']}  {archive_path.name}\n", encoding="ascii")
    verification.write_bytes(json_bytes(result))
    return result


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--build", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--launcher-dir", type=Path, help="Use a separate qualified launcher publish")
    parser.add_argument("--runtime-source-commit", type=source_commit)
    parser.add_argument("--launcher-source-commit", type=source_commit)
    args = parser.parse_args()
    print(json.dumps(package(args.build, args.output, args.dotnet, args.launcher_dir,
                             args.runtime_source_commit, args.launcher_source_commit), indent=2))
