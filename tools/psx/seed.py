"""Build a seeded Stars of Destiny disc for Suikoden I or II on PlayStation."""

import argparse
from dataclasses import dataclass
import hashlib
import json
from pathlib import Path
import secrets
import shutil
import struct
import sys
import tempfile

from disc import Disc, original_track
from field import read_support_sprites
from overlays import OverlayPatcher
from pilot import BASE_SHA1, BASE_SIZE, BOOT, MAIN
from roster import ALGORITHM, NAMED_HERO, STAR_COUNT, assign, cast_package, patch_boot, patch_main, read_cast
import suikoden2


BOOT_FILES = 0x3580C
MAIN_FILES = 0xA3D64
FILE_TABLE_END = 0x362C4
TRACK_NAME = "Stars-of-Destiny-PSX"
TOOL_DIRECTORY = Path(__file__).resolve().parent
LOCAL_WORKSPACE = TOOL_DIRECTORY.parent.parent.parent if TOOL_DIRECTORY.parent.name == "tools" else TOOL_DIRECTORY
LOCAL_PROJECT = LOCAL_WORKSPACE / "psx-randomizer"
GAME_I, GAME_II = "Suikoden I", "Suikoden II"


@dataclass(frozen=True)
class BuildResult:
    cue: Path
    seed: str
    shuffled_stars: int
    shuffled_guests: int
    track_sha256: str


def new_seed():
    return secrets.token_hex(6).upper()


def seed_code(seed, shuffle_hero, shuffle_support, *, guarded=False, sequel=False):
    if sequel:
        settings = f"{suikoden2.ALGORITHM}\0{seed}"
    elif guarded:
        settings = f"{ALGORITHM}\0guarded-combat\0{seed}"
    else:
        settings = f"{ALGORITHM}\0{int(shuffle_hero)}\0{int(shuffle_support)}\0{seed}"
    return hashlib.sha256(settings.encode("utf-8")).hexdigest()[:12]


def default_output(cue, seed, shuffle_hero, shuffle_support, *, guarded=False):
    folder = LOCAL_PROJECT / "seeds" if LOCAL_PROJECT.is_dir() else cue.parent / "Stars-of-Destiny-PSX-Seeds"
    sequel = game_for_track(original_track(cue)) == GAME_II
    code = seed_code(seed, shuffle_hero, shuffle_support, guarded=guarded, sequel=sequel)
    return folder / (f"II-{code}" if sequel else code)


def game_for_track(source):
    size = source.stat().st_size
    if size == BASE_SIZE:
        return GAME_I
    if size == suikoden2.BASE_SIZE:
        return GAME_II
    raise ValueError("Choose the original U.S. Suikoden I v1.1 (SLUS-00292) or Suikoden II (SLUS-00958) disc")


def identify_game(image):
    return game_for_track(original_track(image))


def local_sources():
    first = LOCAL_PROJECT / "original" / "Suikoden (v1.1).cue"
    if not first.is_file():
        first = LOCAL_WORKSPACE / first.name
    sources = {GAME_I: first} if first.is_file() else {}
    for descriptor in sorted(LOCAL_PROJECT.glob("Suikoden II*/*")):
        if descriptor.suffix.lower() not in (".cue", ".ccd"):
            continue
        try:
            game = identify_game(descriptor)
        except (OSError, ValueError):
            continue
        if game == GAME_II:
            sources[GAME_II] = descriptor
            break
    return sources


def patch_file_tables(disc, boot, main, replacements):
    table = bytes(boot[BOOT_FILES:FILE_TABLE_END])
    if main[MAIN_FILES:MAIN_FILES + len(table)] != table:
        raise ValueError("The two disc file tables disagree")
    entries = {}
    for index, extent in enumerate(struct.iter_unpack("<II", table)):
        entries.setdefault(extent, []).append(index * 8)
    for path, replacement in replacements.items():
        original = disc.files[path]
        cells = entries.get((original.size, original.lba))
        if not cells:
            raise ValueError(f"Missing native disc entry for {path}")
        for cell in cells:
            struct.pack_into("<II", boot, BOOT_FILES + cell, replacement.size, replacement.lba)
            struct.pack_into("<II", main, MAIN_FILES + cell, replacement.size, replacement.lba)


def plan(disc, seed, shuffle_hero, shuffle_support, progress, *, guarded=False):
    boot, main = disc.read_file(BOOT), disc.read_file(MAIN)
    cast = read_cast(disc, boot, main)
    assignments = assign(cast, seed, shuffle_hero, shuffle_support, guarded=guarded)
    support_sprites = read_support_sprites(disc, cast) if shuffle_support and not guarded else {}
    overlays = OverlayPatcher(cast, assignments, support_sprites)
    edits = {
        cast[role].path: cast_package(role, cast[donor])
        for role, donor in assignments.items() if role != donor and cast[role].path is not None
    }
    maps = [path for path in sorted(disc.files)
            if path.startswith("DATA/") and path.endswith(".BIN") and not path.startswith("DATA/04_PLAY/")]
    for index, path in enumerate(maps, 1):
        if index == 1 or index % 20 == 0:
            progress(f"Checking game maps ({index} of {len(maps)})")
        data = overlays.patch(path, disc.read_file(path))
        if data is not None:
            edits[path] = data

    replacements, volume_size = disc.plan_replacements(edits)
    patched_boot = patch_boot(boot, cast, assignments)
    patched_main = patch_main(main, cast, assignments)
    patch_file_tables(disc, patched_boot, patched_main, replacements)
    edits[BOOT], edits[MAIN] = patched_boot, patched_main
    return edits, replacements, volume_size, cast, assignments, dict(overlays.counts)


def write_disc(source, track, edits, replacements, volume_size, progress, *, source_sha1=None):
    progress("Copying the original disc")
    with track.open("w+b") as target:
        source.seek(0)
        shutil.copyfileobj(source, target, 8 << 20)
        target.seek(0)
        if hashlib.file_digest(target, "sha1").hexdigest() != (source_sha1 or BASE_SHA1):
            raise ValueError("The copied disc does not match the original image")
        disc = Disc(target)
        for index, (path, contents) in enumerate(edits.items(), 1):
            if index == 1 or index % 20 == 0:
                progress(f"Writing disc changes ({index} of {len(edits)})")
            if path in replacements:
                disc.write_replacement(path, contents, replacements[path])
            else:
                disc.write_file(path, contents)
        disc.update_volume_size(volume_size)
    progress("Verifying the patched disc")
    with track.open("rb") as target:
        readback = Disc(target)
        for path, contents in edits.items():
            if readback.read_file(path) != contents:
                raise ValueError(f"Disc verification failed for {path}")
        target.seek(0)
        return hashlib.file_digest(target, "sha256").hexdigest()


def build(cue, output, seed, *, shuffle_hero=False, shuffle_support=True, guarded=False, spoiler=False, progress=None):
    cue, output = Path(cue).resolve(), Path(output).resolve()
    if not seed.strip() or len(seed) > 128 or not seed.isprintable():
        raise ValueError("Enter a seed with 1 to 128 printable characters")
    if output.exists():
        raise FileExistsError(f"Output already exists: {output}")
    if progress is None:
        progress = lambda message: None
    progress("Checking the original game image")
    source = original_track(cue)
    game = game_for_track(source)
    sequel = game == GAME_II
    source_sha1 = suikoden2.BASE_SHA1 if sequel else BASE_SHA1
    if sequel and shuffle_hero:
        raise ValueError("Suikoden II uses the guarded combat roster with Riou fixed")
    with source.open("rb") as stream:
        if hashlib.file_digest(stream, "sha1").hexdigest() != source_sha1:
            raise ValueError(f"The source disc does not match the supported U.S. {game} dump")
        disc = Disc(stream)
        if sequel:
            edits, replacements, volume_size, cast, assignments, counts = suikoden2.plan(disc, seed, progress)
            stars, guests = len(assignments), 0
        else:
            edits, replacements, volume_size, cast, assignments, counts = plan(
                disc, seed, shuffle_hero, shuffle_support, progress, guarded=guarded,
            )
            stars = sum(role < STAR_COUNT and role != donor for role, donor in assignments.items())
            guests = sum(role >= STAR_COUNT and role != donor for role, donor in assignments.items())
        code = seed_code(seed, shuffle_hero, shuffle_support, guarded=guarded, sequel=sequel)
        track_name = f"{TRACK_NAME}-II-{code}" if sequel else f"{TRACK_NAME}-{code}"
        output.parent.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix="psx-seed-", dir=output.parent) as temporary:
            staged = Path(temporary) / output.name
            staged.mkdir()
            track = staged / (track_name + ".bin")
            digest = write_disc(stream, track, edits, replacements, volume_size, progress, source_sha1=source_sha1)
            (staged / (track_name + ".cue")).write_text(
                f'FILE "{track_name}.bin" BINARY\n  TRACK 01 MODE2/2352\n    INDEX 01 00:00:00\n',
                encoding="ascii",
            )
            manifest = {
                "algorithm": suikoden2.ALGORITHM if sequel else ALGORITHM,
                "seed": seed,
                "roster_mode": "guarded-combat" if guarded or sequel else "expanded",
                "source_sha1": source_sha1,
                "track_sha256": digest,
                "shuffle_named_hero": shuffle_hero,
                "shuffle_support_stars": shuffle_support and not (guarded or sequel),
                "shuffled_stars": stars,
                "shuffled_guests": guests,
                "changes": counts,
            }
            if sequel:
                manifest["game"] = game
            (staged / "seed.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
            if spoiler:
                lines = [f"Stars of Destiny PSX · {seed}", "Story role → Seeded character", ""]
                order = sorted(assignments) if sequel else sorted(assignments, key=lambda role: cast[role].index)
                for role in order:
                    donor = assignments[role]
                    original, replacement = cast[role], cast[donor]
                    lines.append(f"{original.name} → {replacement.name}")
                if not sequel and assignments[NAMED_HERO] != NAMED_HERO:
                    lines.extend(("", "The player slot still displays the name entered at New Game."))
                (staged / "spoiler.txt").write_text("\n".join(lines) + "\n", encoding="utf-8")
            if output.exists():
                raise FileExistsError(f"Output already exists: {output}")
            staged.rename(output)
    return BuildResult(output / (track_name + ".cue"), seed, stars, guests, digest)


def main(argv=None):
    if argv is None:
        argv = sys.argv[1:]
    if not argv:
        from seed_gui import launch
        launch(build, default_output, new_seed, identify_game, local_sources(), (GAME_I, GAME_II))
        return

    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("cue", type=Path, help="Original U.S. Suikoden I v1.1 or Suikoden II CUE/CCD")
    parser.add_argument("output", type=Path, nargs="?", help="New directory for the patched BIN/CUE")
    parser.add_argument("--seed", help="Repeatable seed text. Defaults to a newly generated seed")
    roster = parser.add_mutually_exclusive_group()
    roster.add_argument("--shuffle-hero", action="store_true", help="Include the named player character in Suikoden I")
    roster.add_argument("--guarded", action="store_true", help="Use the combat roster in Suikoden I. Always on in II")
    parser.add_argument("--keep-support", action="store_true", help="Keep the original support characters in Suikoden I")
    parser.add_argument("--spoiler", action="store_true", help="Write the full role-to-character mapping")
    args = parser.parse_args(argv)
    seed = new_seed() if args.seed is None else args.seed
    shuffle_support = not args.keep_support
    try:
        output = args.output or default_output(args.cue.resolve(), seed, args.shuffle_hero, shuffle_support,
                                              guarded=args.guarded)
        print(f"Seed {seed}", flush=True)
        result = build(args.cue, output, seed, shuffle_hero=args.shuffle_hero, shuffle_support=shuffle_support,
                       guarded=args.guarded,
                       spoiler=args.spoiler, progress=lambda message: print(message, flush=True))
    except (OSError, ValueError) as error:
        parser.exit(1, f"{error}\n")
    guests = f" and {result.shuffled_guests} guests" if result.shuffled_guests else ""
    print(f"Shuffled {result.shuffled_stars} Stars of Destiny{guests}")
    print(result.cue)


if __name__ == "__main__":
    main()
