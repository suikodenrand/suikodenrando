"""Package the standalone PlayStation generator from a reviewed Git revision."""

import argparse
import hashlib
from pathlib import Path
import re
import subprocess
from zipfile import ZIP_DEFLATED, ZipFile, ZipInfo


ROOT = Path(__file__).resolve().parents[2]
FILES = (
    "README.md",
    "Seed-Randomizer.cmd",
    "VERSION",
    "disc.py",
    "field.py",
    "overlays.py",
    "pilot.py",
    "roster.py",
    "seed.py",
    "seed_gui.py",
    "suikoden2.py",
    "suikoden2_portraits.py",
    "suikoden2_scene.py",
)


def git(*args):
    return subprocess.check_output(["git", *args], cwd=ROOT)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("revision", help="Reviewed Git tag or commit")
    parser.add_argument("output", type=Path, help="Directory for the ZIP and SHA-256 checksum")
    args = parser.parse_args()

    commit = git("rev-parse", "--verify", "--end-of-options", args.revision + "^{commit}").decode().strip()
    payload = {name: git("show", f"{commit}:tools/psx/{name}") for name in FILES}
    version = payload["VERSION"].decode("ascii").strip()
    if not re.fullmatch(r"[0-9]+\.[0-9]+\.[0-9]+(?:-[a-z0-9.]+)?", version):
        parser.error("The reviewed revision has an invalid PlayStation version")

    launcher = "Seed-Randomizer.cmd"
    payload[launcher] = payload[launcher].replace(b"\r\n", b"\n").replace(b"\n", b"\r\n")
    payload["LICENSE"] = git("show", f"{commit}:LICENSE")
    payload["SOURCE.txt"] = f"https://github.com/suikodenrand/suikodenrando/tree/{commit}/tools/psx\n".encode("ascii")

    name = f"Stars-of-Destiny-PlayStation-{version}"
    args.output.mkdir(parents=True, exist_ok=True)
    archive = args.output / (name + ".zip")
    with ZipFile(archive, "x") as package:
        for filename, contents in sorted(payload.items()):
            package.writestr(ZipInfo(f"{name}/{filename}"), contents, compress_type=ZIP_DEFLATED)
    digest = hashlib.sha256(archive.read_bytes()).hexdigest()
    with (args.output / "SHA256SUMS.txt").open("x", encoding="ascii", newline="\n") as sums:
        sums.write(f"{digest}  {archive.name}\n")
    print(archive)


if __name__ == "__main__":
    main()
