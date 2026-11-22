"""Read the PSX roster and build a stable, role-preserving permutation."""

from dataclasses import dataclass
import hashlib
import struct

from pilot import (
    INIT_START, INIT_STRIDE, MAP_ADDRESS, PLAY, UNITE_START, UNITE_STRIDE,
    WEAPON_LEVEL, encode_name, face_parts, read_word, write_word,
)


ALGORITHM = "psx-roster-v1"
NAMED_HERO = 8
TED = 108
STAR_COUNT = 108
ROSTER_SIZE = 110
FIGHTER_COUNT = 78
# Mirrors CombatCatalog.NormalCombatIds in the original randomizer.
GUARDED_COMBAT_IDS = (
    0, 1, 2, 3, 4, 5, 6, 7, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23,
    25, 26, 28, 29, 30, 31, 32, 33, 35, 36, 37, 38, 39, 40, 41, 42, 43, 45, 47, 48, 49,
    51, 52, 53, 54, 55, 56, 59, 61, 62, 63, 64, 65, 66, 67, 68, 69, 70, 71, 72, 73, 74,
    75, 76, 80, 81, 91, 92, 95, 101, 102, 107,
)
CAST_ORDER = 0xB0094
MENU_POINTERS = 0xB0588
MAIN_ADDRESS = 0x800C0000
UNITE_COUNT = 32
WAR_PORTRAITS = {
    "WAR1.BIN": 0x83150,
    "WAR_GARA.BIN": 0x7B8A4,
    "WAR_KITA.BIN": 0x7C558,
    "WAR_KUWA.BIN": 0x7B91C,
    "WAR_LAST.BIN": 0x7C388,
    "WAR_SCA1.BIN": 0x7B5F8,
    "WAR_SCA2.BIN": 0x7C0AC,
    "WAR_SHAS.BIN": 0x7B8E0,
    "WAR_TEO1.BIN": 0x7C7F8,
    "WAR_TEO2.BIN": 0x7CB88,
}
PORTRAIT_BANK = "DATA/16_WAR/WAR_TEO1.BIN"
RUNE = slice(0x4C, 0x50)
ALPHABET = " abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ"


@dataclass(frozen=True)
class Character:
    id: int
    index: int
    name: str
    short_name: str
    name_pointer: int
    portrait: tuple[bytes, bytes]
    template: int | None
    path: str | None
    package: bytes | None


def decode_name(data):
    name = data.split(b"\0", 1)[0]
    if not name or any(not 0x10 <= value <= 0x44 for value in name):
        raise ValueError("Unexpected character name encoding")
    return "".join(ALPHABET[value - 0x10] for value in name)


def portrait_at(data, image_pointer, palette_pointer):
    image = image_pointer - MAP_ADDRESS
    palette = palette_pointer - MAP_ADDRESS
    if not (0 <= image <= len(data) - 4 and 0 <= palette <= len(data) - 512):
        raise ValueError("Portrait pointers are outside their map")
    size = read_word(data, image)
    end = image + ((size + 7) & -4)
    if not 0x100 <= size <= 0x1000 or image + 4 + size > len(data):
        raise ValueError("Unexpected compressed portrait size")
    return data[image:end], data[palette:palette + 512]


def read_cast(disc, boot, main):
    order = main[CAST_ORDER:CAST_ORDER + ROSTER_SIZE]
    if set(order) != set(range(ROSTER_SIZE)) or order[0] != NAMED_HERO:
        raise ValueError("Unexpected PSX character order")
    templates = {
        read_word(boot, offset): offset
        for offset in range(INIT_START, INIT_START + FIGHTER_COUNT * INIT_STRIDE, INIT_STRIDE)
    }
    packages = {}
    for path in sorted(disc.files):
        if path.startswith(PLAY):
            data = disc.read_file(path)
            if data[0] in packages:
                raise ValueError(f"Duplicate character package ID in {path}")
            packages[data[0]] = (path, data)
    if len(packages) != FIGHTER_COUNT or packages.keys() != templates.keys():
        raise ValueError("Character packages and New Game templates disagree")

    bank = disc.read_file(PORTRAIT_BANK)
    table = WAR_PORTRAITS["WAR_TEO1.BIN"]
    cast = {}
    for index, character in enumerate(order):
        pointer = read_word(main, MENU_POINTERS + index * 4)
        name = "Hero" if character == NAMED_HERO else decode_name(
            main[pointer - MAIN_ADDRESS:pointer - MAIN_ADDRESS + 32]
        )
        path, package = packages.get(character, (None, None))
        if package is None:
            portrait = portrait_at(bank, *struct.unpack_from("<II", bank, table + index * 8))
            short_name = name.split(" ")[0]
        else:
            portrait = face_parts(package)
            short_name = decode_name(package[1:11])
        cast[character] = Character(
            character, index, name, short_name, pointer, portrait,
            templates.get(character), path, package,
        )
    # This table contains the 32 stars with no standalone fighter packages.
    anchor = portrait_at(bank, *struct.unpack_from("<II", bank, table + 8))
    if anchor != cast[2].portrait:
        raise ValueError("The castle/war portrait table does not match the roster order")
    return cast


def permute(group, seed, label):
    """Deterministic Sattolo shuffle, with no unchanged assignments."""
    roles = sorted(group)
    donors = roles.copy()
    key = f"{ALGORITHM}\0{label}\0{seed}".encode("utf-8")
    for index in range(len(donors) - 1, 0, -1):
        digest = hashlib.sha256(key + index.to_bytes(2, "little")).digest()
        other = int.from_bytes(digest, "little") % index
        donors[index], donors[other] = donors[other], donors[index]
    return dict(zip(roles, donors))


def assign(cast, seed, shuffle_hero=False, shuffle_support=True, *, guarded=False):
    result = {role: role for role in sorted(cast)}
    if guarded:
        if shuffle_hero:
            raise ValueError("Tir is fixed in the guarded combat roster")
        if any(role not in cast or cast[role].package is None for role in GUARDED_COMBAT_IDS):
            raise ValueError("The disc is missing a fighter from the original combat roster")
        result.update(permute(GUARDED_COMBAT_IDS, seed, "guarded-fighters"))
        return result

    fighters = [role for role in result if cast[role].package is not None
                and (shuffle_hero or role != NAMED_HERO)]
    result.update(permute(fighters, seed, "fighters"))
    if shuffle_support:
        result.update(permute((role for role in result if cast[role].package is None), seed, "support"))
    return result


def patch_boot(boot, cast, assignments):
    patched = bytearray(boot)
    for role, donor in assignments.items():
        target, source = cast[role].template, cast[donor].template
        if target is None or role == donor:
            continue
        patched[target + 4:target + INIT_STRIDE] = boot[source + 4:source + INIT_STRIDE]
        patched[target + WEAPON_LEVEL] = boot[target + WEAPON_LEVEL]
        if role in (NAMED_HERO, TED) or donor == TED:
            # The opening Soul Eater and its transfer belong to the story slots.
            patched[target + RUNE.start:target + RUNE.stop] = boot[target + RUNE.start:target + RUNE.stop]
    return patched


def patch_main(main, cast, assignments):
    patched = bytearray(main)
    for role, donor in assignments.items():
        if role == donor or role == NAMED_HERO:
            continue
        pointer = cast[donor].name_pointer
        if donor == NAMED_HERO:
            # The named player's original string still exists beside the menu table.
            hero_name = 0xB0104
            if main[hero_name:hero_name + 4] != encode_name("Hero"):
                raise ValueError("Could not find the fallback name for the original Hero")
            pointer = MAIN_ADDRESS + hero_name
        write_word(patched, MENU_POINTERS + cast[role].index * 4, pointer)

    owner = {donor: role for role, donor in assignments.items()}
    for index in range(UNITE_COUNT):
        offset = UNITE_START + index * UNITE_STRIDE
        count = main[offset + 24]
        original = main[offset + 26:offset + 26 + count]
        if not 2 <= count <= 4 or any(character not in owner for character in original):
            raise ValueError(f"Unexpected participants for Unite entry {index}")
        patched[offset + 26:offset + 26 + count] = bytes(owner[character] for character in original)
    return patched


def cast_package(role, donor):
    package = bytearray(donor.package)
    package[0] = role
    if role == NAMED_HERO and donor.id != NAMED_HERO:
        # The player field animation can request a thirteenth pose. Give it
        # the donor's idle pose when its original package has twelve.
        head, face, palette = struct.unpack_from("<III", package, 0x28)
        count = struct.unpack_from("<H", package, head)[0]
        if count != 12:
            raise ValueError("Unexpected pose count in the player replacement")
        table = read_word(package, head + 16)
        poses = package[table:table + count * 4] + package[table:table + 4]
        package[face:face] = poses
        struct.pack_into("<H", package, head, count + 1)
        write_word(package, head + 16, face)
        write_word(package, 0x2C, face + len(poses))
        write_word(package, 0x30, palette + len(poses))
    return bytes(package)
