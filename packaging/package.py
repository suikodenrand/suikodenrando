import argparse
import hashlib
import json
from pathlib import Path
from zipfile import ZIP_DEFLATED, ZipFile

parser = argparse.ArgumentParser(description="Package the Before Viktor Windows test installer.")
parser.add_argument("--source", type=Path, required=True, help="0.8.6 source checkout")
parser.add_argument("--staging", type=Path, default=Path("packaging/windows"))
parser.add_argument("--output", type=Path, required=True)
parser.add_argument("--commit", help="Test branch commit included in the payload")
args = parser.parse_args()

source = args.source.resolve()
staging = args.staging.resolve()
package = staging / "installer/package.json"
metadata = json.loads(package.read_text())
if f"<Version>{metadata['version']}</Version>" not in (source / "Directory.Build.props").read_text():
    raise SystemExit("Source assembly version does not match the installer.")
required = [
    "src/SuikodenRandomizer.AvatarPlugin/SuikodenRandomizer.AvatarPlugin.csproj",
    "src/SuikodenRandomizer.AvatarPlugin/ViktorCheckpointData.cs",
    "tests/SuikodenRandomizer.FixedRoster.Tests/SuikodenRandomizer.FixedRoster.Tests.csproj",
]
for name in required:
    if not (source / name).is_file():
        raise SystemExit(f"Required source file is missing: {name}")

excluded_directories = {".git", ".codex", "packaging", "bin", "obj", "validation", "__pycache__"}
excluded_files = {"RELEASE_VALIDATION.json", "SOURCE_SHA256.json"}
payload = staging / "installer/payload.zip"
with ZipFile(payload, "w", compression=ZIP_DEFLATED, compresslevel=9) as archive:
    for path in sorted(source.rglob("*")):
        relative = path.relative_to(source)
        if path.is_file() and not path.is_symlink() and not (set(relative.parts) & excluded_directories) and path.name not in excluded_files:
            archive.write(path, relative.as_posix())
with ZipFile(payload) as archive:
    if archive.testzip() is not None:
        raise SystemExit("Source payload failed ZIP integrity checks.")
metadata["payloadSha256"] = hashlib.sha256(payload.read_bytes()).hexdigest()
if args.commit:
    metadata["sourceCommit"] = args.commit
package.write_text(json.dumps(metadata, indent=2) + "\n")

args.output.parent.mkdir(parents=True, exist_ok=True)
with ZipFile(args.output, "w", compression=ZIP_DEFLATED, compresslevel=9) as archive:
    for path in sorted(staging.rglob("*")):
        if path.is_file() and not path.is_symlink():
            archive.write(path, path.relative_to(staging).as_posix())
with ZipFile(args.output) as archive:
    if archive.testzip() is not None or archive.read("installer/payload.zip") != payload.read_bytes():
        raise SystemExit("Installer archive verification failed.")
print(f"{args.output} ({args.output.stat().st_size} bytes)")
print(f"SHA256 {hashlib.sha256(args.output.read_bytes()).hexdigest()}")
