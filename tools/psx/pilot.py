"""Build a local Suikoden I PSX opening-party playtest."""

import argparse
from dataclasses import dataclass
import hashlib
from pathlib import Path
import re
import shutil
import struct
import tempfile

from disc import Disc, track_path


BASE_SHA1 = "04f7de4839b8f857b184ceafb52a51c77455cea9"
BASE_SIZE = 590119152
BOOT = "SLUS_002.92"
MAIN = "MAIN.EXE"
PLAY = "DATA/04_PLAY/"
EARLY_MAPS = ("VA1", "VA2", "VA3", "VA4", "VAA")
AREA = "DATA/01_AREA.A/"
MAP_ADDRESS = 0x80010000
INIT_START = 0x33A74
INIT_STRIDE = 0x50
WEAPON_LEVEL = 0x45
UNITE_START = 0xACD0C
UNITE_STRIDE = 36
GLYPHS = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ"
# Each tuple gives the first pointer, the end of the pointer table and the
# end of the adjacent text allocation for that English script bank.
TEXT_BANKS = {
    "VA1": (
        (0x3F5DC, 0x3F608, 0x3F5DC), (0x3FC00, 0x3FC34, 0x3FC00),
        (0x40BC0, 0x40BD0, 0x40BC0), (0x41CBC, 0x41D7C, 0x41CBC),
        (0x42180, 0x421A8, 0x4212C), (0x43B0C, 0x43BC4, 0x42BE4),
        (0x43C5C, 0x43C70, 0x43670), (0x43C70, 0x43CC8, 0x43B0C),
        (0x43F5C, 0x43F84, 0x43F5C),
    ),
    "VA2": (
        (0x2A504, 0x2A5B4, 0x29748), (0x2A600, 0x2A670, 0x2A2D8),
        (0x2B8BC, 0x2B97C, 0x2B3E4),
    ),
    "VA3": (
        (0x2D7F8, 0x2D820, 0x2C388), (0x2D820, 0x2D8DC, 0x2D084),
        (0x2EB7C, 0x2EC3C, 0x2E6A4),
    ),
    "VAA": ((0x532FC, 0x53368, 0x532FC), (0x55850, 0x558E0, 0x55814)),
}


@dataclass(frozen=True)
class Fighter:
    id: int
    name: str
    file: str
    name_offset: int
    template_index: int


GREMIO = Fighter(2, "Gremio", "GRE.BIN", 0xB010C, 1)
PAHN = Fighter(6, "Pahn", "PAN.BIN", 0xB0114, 2)
TAI_HO = Fighter(32, "Tai Ho", "TAI.BIN", 0xB0180, 8)
YAM_KOO = Fighter(38, "Yam Koo", "YAM.BIN", 0xB0188, 15)
OPENING = ((GREMIO, TAI_HO), (PAHN, YAM_KOO))


def encode_name(name):
    return bytes(0x10 if glyph == " " else 0x11 + GLYPHS.index(glyph) for glyph in name)


def read_word(data, offset):
    return struct.unpack_from("<I", data, offset)[0]


def write_word(data, offset, value):
    struct.pack_into("<I", data, offset, value)


def replace_at(data, offset, original, updated, context):
    if len(updated) != len(original) or data[offset:offset + len(original)] != original:
        raise ValueError(f"Unexpected bytes for {context} at 0x{offset:X}")
    data[offset:offset + len(original)] = updated


def asset_for_role(role, source, capacity):
    if len(source) > capacity:
        raise ValueError("The replacement character package exceeds its allocation")
    return bytes((role.id,)) + source[1:].ljust(capacity - 1, b"\0")


def character_asset(disc, fighter):
    payload = disc.read_file(PLAY + fighter.file)
    if payload[0] != fighter.id or payload[1:11].split(b"\0", 1)[0] != encode_name(fighter.name):
        raise ValueError(f"Unexpected character package for {fighter.name}")
    return payload


def patch_init(boot):
    original = bytes(boot)
    for role, donor in OPENING:
        role_offset = INIT_START + INIT_STRIDE * role.template_index
        donor_offset = INIT_START + INIT_STRIDE * donor.template_index
        if (read_word(original, role_offset) != role.id
                or read_word(original, donor_offset) != donor.id):
            raise ValueError("Unexpected New Game character initializer")
        # Use the fighter's native initial stats, inventory and weapon. The
        # opening role supplies the initial sharpening level.
        boot[role_offset + 4:role_offset + INIT_STRIDE] = original[
            donor_offset + 4:donor_offset + INIT_STRIDE
        ]
        boot[role_offset + WEAPON_LEVEL] = original[role_offset + WEAPON_LEVEL]


def patch_menu(main):
    for role, donor in OPENING:
        replace_at(
            main, role.name_offset, encode_name(role.name).ljust(8, b"\0"),
            encode_name(donor.name).ljust(8, b"\0"), "menu name"
        )


def patch_unites(main):
    # Retarget Fisherman Attack to the story IDs that own its native fighters.
    # Ted and Odessa are valid IDs outside the opening party. Their use here
    # excludes the original recipes for the replaced fighters in the playtest.
    for index, expected, replacement in (
        (0, b"\x06\x02", b"\x6C\x6D"),
        (1, b"\x20\x26", b"\x02\x06"),
    ):
        offset = UNITE_START + index * UNITE_STRIDE
        if main[offset + 24:offset + 26] != bytes((2, index + 1)):
            raise ValueError("Unexpected PSX Unite record")
        replace_at(main, offset + 26, expected, replacement, "Unite participants")
    # Beat'em Up also requires the original Pahn and must not accept Yam Koo.
    replace_at(main, UNITE_START + 27 * UNITE_STRIDE + 26, b"\x06", b"\x6D",
               "Beat'em Up participant")


def face_parts(payload):
    face, palette = struct.unpack_from("<II", payload, 0x2C)
    if not 0x34 <= face < palette or len(payload) - palette != 512:
        raise ValueError("Unexpected portrait and palette boundaries")
    return payload[face:palette], payload[palette:]


def field_parts(asset):
    head, end = struct.unpack_from("<II", asset, 0x28)
    pose_count, direction_count, frame_tables, format_flags = struct.unpack_from("<HHBB", asset, head)
    pointers = {}
    for offset, count in (
        (8, frame_tables), (12, format_flags & 0x3F), (16, pose_count), (20, direction_count)
    ):
        table = read_word(asset, head + offset)
        pointers[head + offset] = table
        for cell in range(table, table + count * 4, 4):
            pointers[cell] = read_word(asset, cell)
    start = min(head, *pointers, *pointers.values())
    if start < 0x34 or max(pointers, default=head) + 4 > end or max(pointers.values()) >= end:
        raise ValueError("Unexpected pointers in the field sprite package")
    return start, head, end, pointers


def field_block(asset, header_in_map):
    start, head, end, pointers = field_parts(asset)
    block = bytearray(asset[start:end])
    base = MAP_ADDRESS + header_in_map - head
    for cell, address in pointers.items():
        write_word(block, cell - start, base + address)
    return block


def replace_field_sprites(data, old_asset, new_asset):
    old_start, old_head, old_end, _ = field_parts(old_asset)
    new_start, new_head, new_end, _ = field_parts(new_asset)
    anchor = old_asset[old_head + 24:old_head + 152]
    original = bytes(data)
    for match in re.finditer(re.escape(anchor), original):
        head = match.start() - 24
        start = head + old_start - old_head
        end = head + old_end - old_head
        if start < 0 or original[start:end] != field_block(old_asset, head):
            continue
        new_pos = head + new_start - new_head
        new_end_pos = head + new_end - new_head
        if not start <= new_pos <= head < new_end_pos <= end:
            raise ValueError("Replacement field sprite exceeds its map allocation")
        data[start:end] = b"\0" * (end - start)
        data[new_pos:new_end_pos] = field_block(new_asset, head)


def replace_portraits(data, role, old_asset, new_asset):
    old_face, old_palette = face_parts(old_asset)
    new_face, new_palette = face_parts(new_asset)
    original = bytes(data)
    pal_offset = original.find(new_palette)
    # A map portrait descriptor is (character ID, compressed image, palette).
    for offset in range(0, len(original) - 11, 4):
        if read_word(original, offset) != role.id:
            continue
        face = read_word(original, offset + 4) - MAP_ADDRESS
        palette = read_word(original, offset + 8) - MAP_ADDRESS
        if (face < 0 or palette < 0 or original[face:face + len(old_face)] != old_face
                or original[palette:palette + len(old_palette)] != old_palette):
            continue
        if pal_offset == -1 or len(new_face) > len(old_face):
            raise ValueError(f"The {role.name} portrait cannot use this map's graphics allocation")
        data[face:face + len(old_face)] = new_face.ljust(len(old_face), b"\0")
        write_word(data, offset + 8, MAP_ADDRESS + pal_offset)


def patch_messages(data, first_pointer, end_pointer, text_end):
    pointers = tuple(range(first_pointer, end_pointer, 4))
    starts = tuple(read_word(data, pointer) - MAP_ADDRESS for pointer in pointers)
    destinations = starts[1:] + (text_end,)
    entries = []
    for start, end in zip(starts, destinations):
        original = bytes(data[start:end])
        if not 0 <= start < end <= first_pointer or not original.endswith(b"\0"):
            raise ValueError(f"Unexpected dialogue boundary at 0x{start:X}")
        entry = original.rstrip(b"\0")
        for role, donor in OPENING:
            entry = entry.replace(encode_name(role.name), encode_name(donor.name))
        entries.append(entry + b"\0")
    if all(len(entry) <= end - start for start, end, entry in zip(starts, destinations, entries)):
        for start, end, entry in zip(starts, destinations, entries):
            data[start:end] = entry.ljust(end - start, b"\0")
        return

    packed = b"".join(entries)
    if len(packed) > text_end - starts[0]:
        raise ValueError(f"Dialogue edits exceed the allocation at 0x{starts[0]:X}")
    positions = {}
    cursor = starts[0]
    for start, entry in zip(starts, entries):
        positions[MAP_ADDRESS + start] = MAP_ADDRESS + cursor
        cursor += len(entry)
    data[starts[0]:text_end] = packed.ljust(text_end - starts[0], b"\0")
    for pointer in pointers:
        write_word(data, pointer, positions[read_word(data, pointer)])


def patch_opening_maps(disc, assets):
    for name in EARLY_MAPS:
        path = AREA + name + ".BIN"
        original = disc.read_file(path)
        data = bytearray(original)
        for role, donor in OPENING:
            old_asset, new_asset = assets[role], assets[donor]
            offset = original.find(old_asset)
            if offset >= 0:
                replace_at(data, offset, old_asset,
                           asset_for_role(role, new_asset, len(old_asset)), "embedded battle package")
            replace_field_sprites(data, old_asset, new_asset)
            replace_portraits(data, role, old_asset, new_asset)
        for bank in TEXT_BANKS.get(name, ()):
            patch_messages(data, *bank)
        if data != original:
            yield path, data


def plan(disc):
    assets = {fighter: character_asset(disc, fighter) for pair in OPENING for fighter in pair}
    boot = bytearray(disc.read_file(BOOT))
    main = bytearray(disc.read_file(MAIN))
    patch_init(boot)
    patch_menu(main)
    patch_unites(main)
    yield BOOT, boot
    yield MAIN, main
    for role, donor in OPENING:
        yield PLAY + role.file, asset_for_role(role, assets[donor], len(assets[role]))
    yield from patch_opening_maps(disc, assets)


def build(cue, output):
    source = track_path(cue)
    if source.stat().st_size != BASE_SIZE:
        raise ValueError("The opening pilot requires the reviewed SLUS-00292 disc image")
    with source.open("rb") as stream:
        if hashlib.file_digest(stream, "sha1").hexdigest() != BASE_SHA1:
            raise ValueError(f"Unsupported disc SHA-1. Expected {BASE_SHA1}")
        edits = tuple(plan(Disc(stream)))
        if output.exists():
            raise FileExistsError(f"Output already exists: {output}")
        output.parent.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(dir=output.parent) as temporary:
            staged = Path(temporary) / output.name
            staged.mkdir()
            name = "Stars-of-Destiny-PSX-Opening"
            track = staged / (name + ".bin")
            with track.open("w+b") as target:
                stream.seek(0)
                shutil.copyfileobj(stream, target, 8 << 20)
                disc = Disc(target)
                for path, contents in edits:
                    sectors = disc.write_file(path, contents)
                    if disc.read_file(path) != contents:
                        raise ValueError(f"Readback failed for {path}")
                    print(f"{path}  {sectors} updated sectors")
            (staged / (name + ".cue")).write_text(
                f'FILE "{name}.bin" BINARY\n  TRACK 01 MODE2/2352\n    INDEX 01 00:00:00\n',
                encoding="ascii",
            )
            staged.rename(output)
    return output / (name + ".cue")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("cue", type=Path, help="Path to the reviewed source CUE")
    parser.add_argument("output", type=Path, help="New directory for the patched BIN/CUE")
    args = parser.parse_args()
    try:
        result = build(args.cue.resolve(), args.output.resolve())
    except (OSError, ValueError) as error:
        parser.exit(1, f"{error}\n")
    print(result)


if __name__ == "__main__":
    main()
