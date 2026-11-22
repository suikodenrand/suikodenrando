"""Read field sprites from fighter packages and reviewed map records."""

from dataclasses import dataclass
import re
import struct

from pilot import MAP_ADDRESS, field_parts, read_word, write_word


# Each map position is the sprite header immediately before the star's
# portrait. These stars have map graphics and no standalone 04_PLAY file.
SUPPORT_SOURCES = {
    24: ("VB5BB", 0x2DA10), 27: ("VB5C4", 0x5DDE4), 34: ("VB5C4", 0x70B38),
    44: ("VB3", 0x4BEAC), 46: ("VB1", 0x2EC48), 50: ("VB5BA", 0x309C8),
    57: ("VB5BA", 0x32870), 58: ("VB5C3", 0x3A988), 60: ("VB5C4", 0x5F95C),
    77: ("VAAD", 0x1C7D4), 78: ("VB5B1", 0x4D018), 79: ("VB5C4", 0x73FF8),
    82: ("VB5C4", 0x64B44), 83: ("VB5C4", 0x66718), 84: ("VB5C3", 0x42880),
    85: ("VB3", 0x599B8), 86: ("VB5BA", 0x346A4), 87: ("VB5CA", 0x40B24),
    88: ("VB5B1", 0x5F358), 89: ("VB1", 0x34EF8), 90: ("VB5CA", 0x42524),
    93: ("VB5C3", 0x3F6BC), 94: ("VB5C3", 0x40ED8), 96: ("VB5B1", 0x5A5B4),
    97: ("VB5CG", 0x67914), 98: ("VB5CG", 0x69570), 99: ("VB5C1", 0x67370),
    100: ("VB5C1", 0x6095C), 103: ("VB5C3", 0x44208), 104: ("VB5B1", 0x5D934),
    105: ("VB5B1", 0x5BE50), 106: ("VB5CA", 0x3DB44),
}


@dataclass(frozen=True)
class FieldSprite:
    data: bytes
    head: int
    pointers: tuple[tuple[int, int], ...]

    @classmethod
    def from_parts(cls, data, start, head, end, pointers):
        return cls(data[start:end], head - start,
                   tuple((cell - start, target - start) for cell, target in pointers.items()))

    @classmethod
    def from_package(cls, package):
        return cls.from_parts(package, *field_parts(package))

    @property
    def anchor(self):
        return self.data[self.head + 24:self.head + 152]

    def at(self, head_in_map):
        data = bytearray(self.data)
        base = MAP_ADDRESS + head_in_map - self.head
        for cell, address in self.pointers:
            write_word(data, cell, base + address)
        return data


def support_sprite(data, head, character):
    if data[head:head + 8] != b"\x0c\0\x04\0\x01\x81\0\0":
        raise ValueError(f"Unexpected field header for {character.name}")
    end = (head + 28 + read_word(data, head + 24) + 3) & -4
    tag = struct.pack("<II", character.id, MAP_ADDRESS + end)
    if (not any(match.start() % 4 == 0 for match in re.finditer(re.escape(tag), data))
            or data[end + 4:end + 20] != character.portrait[0][4:20]):
        raise ValueError(f"The source map does not identify the field sprite for {character.name}")
    pointers = {}
    for offset, count in ((8, 1), (12, 1), (16, 12), (20, 4)):
        cell = head + offset
        table = read_word(data, cell) - MAP_ADDRESS
        if not 0 <= table <= head or table + count * 4 > end:
            raise ValueError(f"Invalid field animation table for {character.name}")
        pointers[cell] = table
        for item in range(table, table + count * 4, 4):
            pointers[item] = read_word(data, item) - MAP_ADDRESS
    start = min(head, *pointers, *pointers.values())
    if start < 0 or max(pointers.values()) >= end or end > len(data):
        raise ValueError(f"Invalid field graphics extent for {character.name}")
    return FieldSprite.from_parts(data, start, head, end, pointers)


def read_support_sprites(disc, cast):
    if SUPPORT_SOURCES.keys() != {hero.id for hero in cast.values() if hero.package is None}:
        raise ValueError("The support roster does not match the reviewed map sources")
    maps = {}
    sprites = {}
    for role, (name, head) in SUPPORT_SOURCES.items():
        if name not in maps:
            region = "01_AREA.A" if name.startswith("VA") else "02_AREA.B"
            maps[name] = disc.read_file(f"DATA/{region}/{name}.BIN")
        sprites[role] = support_sprite(maps[name], head, cast[role])
    return sprites
