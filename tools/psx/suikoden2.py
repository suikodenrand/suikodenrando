"""Character shuffle for the U.S. Suikoden II PlayStation disc (SLUS-00958)."""

from collections import Counter
from dataclasses import dataclass
import struct

from roster import permute
from suikoden2_portraits import palette_roles, patch_portrait_rendering
from suikoden2_scene import FieldArt, ScenePatcher, word


ALGORITHM = "psx-s2-combat-v4"
ROSTER_ALGORITHM = "psx-s2-combat-v1"
BASE_SHA1 = "5a1c6314fb79c2cdab90ccae72f1e7e6fcb89cc4"
BASE_SIZE = 513907296
EXECUTABLE = "SLUS_009.58"
PLAY = "CDROM/120_PLAY/"
GAME = "CDROM/280_GAME/"
BOOT = "CDROM/270_BOOT/"
BATTLE = "CDROM/150_BPRG/BUFF0/"
# Riou's player slot and every Jowy entry in the U.S. disc's name table.
PROTECTED_STORY_IDS = frozenset((1, 80, 81, 146, 155, 174))
COMBAT_IDS = frozenset(range(2, 80)) - PROTECTED_STORY_IDS
DOUBLE_SLOTS = frozenset((49, 50, 72, 74))
NAME_SIZE = 9
KAREN_ID = 25

@dataclass(frozen=True)
class Fighter:
    name: str
    path: str
    package: bytes


def decode_name(row):
    alphabet = {code: chr(letter + code - start)
                for start, letter in ((0x11, ord("a")), (0x3B, ord("A"))) for code in range(start, start + 26)}
    alphabet.update({0x10: " ", 0x66: "-"})
    try:
        return "".join(alphabet[code] for code in row.split(b"\0", 1)[0])
    except KeyError as exc:
        raise ValueError("Unexpected Suikoden II character name encoding") from exc


def read_cast(disc):
    names = disc.read_file(GAME + "NAME.BIN")
    cast, field_packages = {}, {}
    for path in sorted(disc.files):
        if not path.startswith(PLAY) or not path.endswith(".BIN"):
            continue
        data = disc.read_file(path)
        kind, role, battle, portrait, _, _, slots, field, graphics = struct.unpack_from("<9I", data)
        if role not in COMBAT_IDS:
            continue
        if kind != 4 or graphics != field + 32 or graphics >= len(data):
            raise ValueError(f"Unexpected Suikoden II package layout in {path}")
        if battle == portrait == 0 and path.endswith("_D.BIN"):
            field_packages[role] = (path, data)
            continue
        if not (battle == 36 and battle < portrait < field and slots == 1 + (role in DOUBLE_SLOTS)) or role in cast:
            raise ValueError(f"Unexpected Suikoden II fighter package in {path}")
        name = decode_name(names[role * NAME_SIZE:(role + 1) * NAME_SIZE])
        cast[role] = Fighter(name, path, data)
    if cast.keys() != COMBAT_IDS:
        raise ValueError("The Suikoden II combat roster is incomplete")
    return cast, field_packages


def assign(seed):
    assignments = permute(COMBAT_IDS - DOUBLE_SLOTS, seed, ROSTER_ALGORITHM + "-one-slot")
    assignments.update(permute(COMBAT_IDS & DOUBLE_SLOTS, seed, ROSTER_ALGORITHM + "-two-slots"))
    return assignments


def as_role(package, role, portrait):
    result = bytearray(package)
    struct.pack_into("<I", result, 4, role)
    offset = word(package, 32)
    result[offset:offset + len(portrait)] = portrait
    return result


def field_package(role, art):
    header_size = 36
    header = struct.pack("<9I", 4, role, 0, 0, 0, 0, 1, header_size + art.header, header_size + art.graphics)
    return header + art.at(header_size)


def rows_for_roles(original, assignments, stride, size):
    if len(original) != size:
        raise ValueError("Unexpected size of a Suikoden II character table")
    data = bytearray(original)
    for role, donor in assignments.items():
        data[role * stride:(role + 1) * stride] = original[donor * stride:(donor + 1) * stride]
    return data


def patch_character_data(disc, assignments):
    layouts = {"NAME": (9, 1620), "CKD": (2, 256), "KOTEI": (18, 1494),
               "VARIA": (36, 3060), "ARMSD": (66, 5544)}
    originals = {name: disc.read_file(f"{GAME}{name}.BIN") for name in layouts}
    tables = {name: rows_for_roles(originals[name], assignments, stride, size)
              for name, (stride, size) in layouts.items()}

    varia = tables["VARIA"]
    for role in assignments:
        slot = role * 36 + 19
        varia[slot] = max(1, originals["VARIA"][slot]) if varia[slot] else 0

    edits = {f"{GAME}{name}.BIN": data for name, data in tables.items()}
    # ALL is loaded at New Game. The event presets have their own encoded save-state copies.
    layouts = {GAME + "ALL.BIN": (0, 4974), BOOT + "EVENTDAT.BIN": (0x6AF4, 35392)}
    layouts.update({f"{BOOT}EVENTDA{index}.BIN": (0x6E04, 36112) for index in range(1, 6)})
    for path, (offset, size) in layouts.items():
        original = disc.read_file(path)
        if (len(original) != size or original[offset:offset + 8] != originals["CKD"][:8]
                or any(original[offset + 0x100:offset + 0x112])):
            raise ValueError(f"Unexpected Suikoden II character table in {path}")
        edits[path] = patch_starting_cast(original, offset, assignments)
    return edits


def patch_starting_cast(original, config, assignments):
    fixed = config + 0x100
    variable = config + 0x77A
    data = bytearray(original)
    for role, donor in assignments.items():
        for offset, stride in ((config, 2), (fixed, 18)):
            target, source = offset + role * stride, offset + donor * stride
            data[target:target + stride] = original[source:source + stride]

        target, source = variable + role * 36, variable + donor * 36
        if original[target] == original[source]:
            data[target + 4:target + 19] = original[source + 4:source + 19]
        # The story role defines recruitment level, XP and sharpening progression.
        data[target + 19] = max(1, original[target + 19]) if original[source + 19] else 0
        data[target + 20:target + 34] = original[source + 20:source + 34]
    return data


# Member bytes start one byte after the participant count in each native structure.
UNITE_TABLES = {"BP0_BEF.BIN": (0x1F25D, 0x48), "BP0_MAK.BIN": (0x1F25D, 0x48),
                "BP0_FST.BIN": (0x235AD, 0x4C), "BP0_SEC.BIN": (0x22D81, 0x4C)}


def guard_unites(disc, assignments):
    edits = {}
    disabled = set()
    for file, (start, stride) in UNITE_TABLES.items():
        path = BATTLE + file
        data = bytearray(disc.read_file(path))
        if data[start - 1:start + 5] != b"\x02\x03\x02\0\0\0":
            raise ValueError(f"Unexpected Suikoden II Unite table in {file}")
        for recipe in range(1, 51):
            cell = start + (recipe - 1) * stride
            size = data[cell - 1]
            members = data[cell:cell + size]
            if not 2 <= size <= 5 or any(not 1 <= (member & 0x7F) <= 83 for member in members):
                raise ValueError(f"Unexpected Suikoden II Unite recipe {recipe} in {file}")
            if any((member & 0x7F) in assignments for member in members):
                # The native scripts can require individual fighters' original poses and IDs.
                # ID 127 cannot appear as a fighter, so the recipe stays out of the command menu.
                data[cell] = (data[cell] & 0x80) | 127
                disabled.add(recipe)
        edits[path] = data
    return edits, len(disabled)


# The executable starts Karen's special entrance. The battle overlays
# attach her extra frames from BF_DAT before running the attack script.
KAREN_MOTION_CHECKS = {
    EXECUTABLE: ((0x50904, 0x24130019),),
    BATTLE + "BP0_FST.BIN": ((0x1BB8, 0x24050019), (0x5278, 0x24050019)),
    BATTLE + "BP0_SEC.BIN": ((0x171C, 0x24050019),),
}


def patch_karen_motion(executable, battle_edits, assignments):
    role = next(role for role, donor in assignments.items() if donor == KAREN_ID)
    for path, checks in KAREN_MOTION_CHECKS.items():
        data = executable if path == EXECUTABLE else battle_edits[path]
        for offset, instruction in checks:
            if word(data, offset) != instruction:
                raise ValueError(f"Unexpected Suikoden II animation check in {path}")
            struct.pack_into("<H", data, offset, role)


def patch_file_table(disc, executable, replacements):
    # The verified SLUS executable contains the sector and size table used by the native CD loader.
    for path, extent in replacements.items():
        original = disc.files[path]
        entry = struct.pack("<II", original.size, original.lba)
        cell = executable.find(entry, 0x7D000, 0x81000)
        if cell < 0 or cell % 4 or executable.find(entry, cell + 1, 0x81000) >= 0:
            raise ValueError(f"Missing or ambiguous Suikoden II disc file entry for {path}")
        struct.pack_into("<II", executable, cell, extent.size, extent.lba)


def plan(disc, seed, progress):
    cast, field_packages = read_cast(disc)
    assignments = assign(seed)
    executable = bytearray(disc.read_file(EXECUTABLE))
    arts = {role: FieldArt.from_package(fighter.package, word(fighter.package, 28))
            for role, fighter in cast.items()}
    palettes = palette_roles(executable, cast)
    replacement_arts = {role: art.with_palette(palettes[role]) for role, art in arts.items()}
    edits = {cast[role].path: as_role(cast[donor].package, role, replacement_arts[donor].portrait())
             for role, donor in assignments.items()}
    for role, (path, original) in field_packages.items():
        # Compare this layout to the original field-only package before writing a new one.
        art = FieldArt.from_package(original, word(original, 28))
        if original != field_package(role, art):
            raise ValueError(f"Unexpected Suikoden II field package in {path}")
        edits[path] = field_package(role, replacement_arts[assignments[role]])
    edits.update(patch_character_data(disc, assignments))
    unite_edits, disabled = guard_unites(disc, assignments)
    patch_karen_motion(executable, unite_edits, assignments)
    patch_portrait_rendering(executable)
    edits.update(unite_edits)

    counts = Counter(fighter_packages=len(cast), field_packages=len(field_packages), guarded_unites=disabled)
    scene = ScenePatcher(cast, arts, assignments, replacement_arts)
    maps = [path for path in sorted(disc.files) if path.startswith("CDROM/") and path.endswith(".BIN")
            and path.split("/")[1][:3].isdigit() and 10 <= int(path.split("/")[1][:3]) <= 110]
    for index, path in enumerate(maps, 1):
        if index == 1 or index % 40 == 0:
            progress(f"Checking Suikoden II maps ({index} of {len(maps)})")
        original = disc.read_file(path)
        if len(original) < 64:
            continue
        patched = scene.patch(original)
        if patched is not None:
            edits[path] = patched
            counts["maps"] += 1
    counts.update(scene.counts)

    replacements, volume_size = disc.plan_replacements(edits)
    patch_file_table(disc, executable, replacements)
    edits[EXECUTABLE] = executable
    return edits, replacements, volume_size, cast, assignments, dict(counts)
