"""Patch character graphics and English dialogue in Suikoden II PS1 map overlays."""

from collections import Counter, defaultdict
from dataclasses import dataclass, replace
import re
import struct

from suikoden2_portraits import tag_mode


# The two adjacent buffers used by the native map loader.
MAP_BASES = (0x8010DC50, 0x8015DC50)
MAP_LIMIT = 0x50000
POINTER = re.compile(rb"(?=[\x00-\xff]{2}[\x10-\x1f]\x80)")
LETTER = rb"\x11-\x2a\x3b-\x54"
GLYPHS = {c: 0x11 + index for index, c in enumerate("abcdefghijklmnopqrstuvwxyz")}
GLYPHS.update({c.upper(): 0x3B + index for index, c in enumerate("abcdefghijklmnopqrstuvwxyz")})
GLYPHS.update({" ": 0x10, "-": 0x6C, ".": 0x66})


def word(data, offset):
    return struct.unpack_from("<I", data, offset)[0]


def align(value):
    return (value + 3) & -4


def native_name(text):
    return bytes(GLYPHS[letter] for letter in text)


@dataclass(frozen=True)
class FieldArt:
    """A portable walking graph, with its optional dialogue portrait."""

    data: bytes
    header: int
    palette: int
    graphics: int
    animations: int
    pointers: tuple[int, ...]

    @classmethod
    def from_package(cls, data, header, *, base=0, portrait=True):
        graphics = header + 32
        if not 0 <= header <= len(data) - 32 or portrait and graphics > len(data) - 4:
            raise ValueError("Invalid Suikoden II field sprite address")
        frames, animations = struct.unpack_from("<HH", data, header)
        first, second, palette, clut, frame_table, animation_table = (
            pointer - base for pointer in struct.unpack_from("<6I", data, header + 8)
        )
        if not 0 <= palette < header:
            raise ValueError("Invalid Suikoden II field sprite palette")
        start = word(data, palette) - base
        end = graphics + 5 + word(data, graphics) if portrait else graphics
        if not (0 < start < palette < clut < frame_table < animation_table < first < second < header
                and frames > 0 and animations > 0 and frame_table + frames * 20 <= animation_table
                and animation_table + animations * 8 == first
                and start + 5 + word(data, start) <= palette and end <= len(data)):
            raise ValueError("Unexpected Suikoden II field sprite layout")

        pointers = [palette, clut, *range(header + 8, header + 32, 4)]
        for index in range(frames):
            pointers.extend((frame_table + index * 20 + 8, frame_table + index * 20 + 16))
        pointers.extend(animation_table + index * 8 + 4 for index in range(animations))
        normalized = bytearray(data[start:end])
        cells = []
        for pointer in pointers:
            address = word(data, pointer)
            if address == 0:
                continue
            target = address - base
            if not start <= target < graphics:
                raise ValueError("Suikoden II field sprite points outside its package")
            cell = pointer - start
            cells.append(cell)
            struct.pack_into("<I", normalized, cell, target - start)
        return cls(bytes(normalized), header - start, palette - start, graphics - start, animations, tuple(cells))

    def at(self, address, *, portrait=True):
        data = bytearray(self.data if portrait else self.data[:self.graphics])
        for cell in self.pointers:
            struct.pack_into("<I", data, cell, word(self.data, cell) + address)
        return data

    def walking_texture(self):
        # The length-prefixed texture and the first palette identify the walking appearance.
        return self.data[:5 + word(self.data, 0)], self.data[self.palette + 4:self.palette + 36]

    def portrait(self):
        return self.data[self.graphics:]

    def with_palette(self, palette_role):
        mode = self.graphics + 4
        tagged = bytes((tag_mode(self.data[mode], palette_role),))
        return replace(self, data=self.data[:mode] + tagged + self.data[mode + 1:])


class MapOverlay:
    def __init__(self, original, base):
        self.original = original
        self.base = base
        self.data = bytearray(original)
        self.references = defaultdict(list)
        for match in POINTER.finditer(original):
            cell = match.start()
            if cell % 4 == 0:
                offset = word(original, cell) - base
                if 0 <= offset < len(original):
                    self.references[offset].append(cell)
        self.free = [(align(len(original)), MAP_LIMIT)]
        self.code_addresses = None

    def literal_addresses(self):
        """Recognize MIPS high/low pairs that can refer to map data outside a pointer table."""
        if self.code_addresses is None:
            self.code_addresses = set()
            words = tuple(value for value, in struct.iter_unpack("<I", self.original[:len(self.original) & -4]))
            for index, instruction in enumerate(words):
                if instruction >> 21 != 15 << 5:
                    continue
                register = (instruction >> 16) & 31
                high = (instruction & 0xFFFF) << 16
                if high + 0x8000 < self.base or high - 0x8000 >= self.base + len(self.original):
                    continue
                for following in words[index + 1:index + 7]:
                    opcode = following >> 26
                    if ((following >> 21) & 31) != register or opcode not in (8, 9, 13, 32, 33, 35, 36, 37, 40, 41, 43):
                        continue
                    low = following & 0xFFFF
                    if opcode != 13 and low >= 0x8000:
                        low -= 0x10000
                    target = high + low - self.base
                    if 0 <= target < len(self.original):
                        self.code_addresses.add(target)
        return self.code_addresses

    def owns(self, start, stop, cells, *, allow_literal_start=False):
        literals = self.literal_addresses()
        for offset in range(start, stop):
            if offset in literals and (offset != start or not allow_literal_start):
                return False
            if any(cell not in cells and not start <= cell < stop for cell in self.references.get(offset, ())):
                return False
        return True

    def repoint(self, cells, offset):
        for cell in cells:
            struct.pack_into("<I", self.data, cell, self.base + offset)

    def place(self, size, reclaim=None):
        # Keep the allocator unchanged when a replacement cannot fit.
        free = sorted(self.free if reclaim is None else [*self.free, reclaim])
        merged = []
        for start, stop in free:
            if merged and start <= merged[-1][1]:
                merged[-1] = (merged[-1][0], max(stop, merged[-1][1]))
            else:
                merged.append((start, stop))
        choices = [(stop - start, start, index) for index, (start, stop) in enumerate(merged)
                   if stop - start >= size]
        if not choices:
            return None
        _, start, index = min(choices)
        _, stop = merged.pop(index)
        aligned = align(start + size)
        if aligned < stop:
            merged.append((aligned, stop))
        self.free = merged
        self.data.extend(bytes(max(0, start + size - len(self.data))))
        return start


class ScenePatcher:
    def __init__(self, cast, arts, assignments, replacement_arts):
        self.arts = arts
        self.assignments = assignments
        self.replacement_arts = replacement_arts
        self.counts = Counter()
        self.walkers = defaultdict(set)
        for role, art in arts.items():
            self.walkers[art.walking_texture()].add(role)

        self.aliases = {}
        # Extra spellings from the English Remaster dialogue catalog.
        alternate = {11: ("Georg Prime",), 35: ("Freed Y.", "Freed"), 42: ("GADGET",)}
        ambiguous = {"Bob", "Gadget", "Hoi"}
        for role, donor in assignments.items():
            for name in (cast[role].name, *alternate.get(role, ())):
                if len(name) < 3 or name in ambiguous:
                    continue
                replacement = cast[donor].name
                if name.isupper():
                    replacement = replacement.upper()
                self.aliases[native_name(name)] = native_name(replacement)
        forms = sorted(self.aliases, key=lambda value: (-len(value), value))
        self.names = re.compile(rb"(?<![" + LETTER + rb"])(?:"
                                + b"|".join(re.escape(value) for value in forms) + rb")(?![" + LETTER + rb"])")

    def patch(self, original):
        first = word(original, 0)
        base = next((address for address in MAP_BASES if address <= first < address + len(original)), None)
        if base is None or len(original) > MAP_LIMIT:
            return None
        overlay = MapOverlay(original, base)
        self.patch_dialogue(overlay)
        self.patch_actors(overlay)
        return overlay.data if overlay.data != original else None

    def patch_dialogue(self, overlay):
        original = overlay.original
        entries = {}
        for start in overlay.references:
            if start % 4 or original[start] != 0xA4:
                continue
            terminator = original.find(b"\xA4\0", start + 1, start + 1024)
            if terminator == -1:
                continue
            stop = terminator + 2
            if not any(original[stop:align(stop)]):
                entries[start] = stop

        # Verify adjacent messages and adjacent table entries before interpreting a pointer as dialogue.
        verified = set()
        for start, stop in entries.items():
            next_start = align(stop)
            if next_start in entries and any(
                cell + 4 in overlay.references[next_start] for cell in overlay.references[start]
            ):
                verified.update((start, next_start))

        for start in sorted(verified):
            stop = entries[start]
            message = original[start:stop]
            changed = self.names.sub(lambda match: self.aliases[match[0]], message)
            if changed == message:
                continue
            cells = overlay.references[start]
            padded = align(stop)
            if len(changed) <= padded - start and overlay.owns(start, padded, cells, allow_literal_start=True):
                overlay.data[start:padded] = changed.ljust(padded - start, b"\0")
            else:
                dest = overlay.place(len(changed))
                if dest is None:
                    self.counts["dialogue_unfitted"] += 1
                    continue
                overlay.data[dest:dest + len(changed)] = changed
                overlay.repoint(cells, dest)
            self.counts["dialogue_lines"] += 1

    def patch_actors(self, overlay):
        original, base = overlay.original, overlay.base
        walkers = {}
        portraits = defaultdict(list)
        for header, cells in overlay.references.items():
            for cell in cells:
                if cell < 4 or cell + 12 > len(original):
                    continue
                role, _, face, flags = struct.unpack_from("<4I", original, cell - 4)
                if not 1 <= role <= 0xFFFE or flags > 0xFF:
                    continue
                if role not in self.arts and (face or role < 0x8000):
                    continue
                if face:
                    face_offset = face - base
                    picture = self.arts[role].portrait()
                    if (face_offset < 0 or face_offset % 4 or face_offset + len(picture) > len(original)
                            or original[face_offset:face_offset + len(picture)] != picture):
                        continue
                try:
                    old = FieldArt.from_package(original, header, base=base, portrait=False)
                except ValueError:
                    continue
                start = header - old.header
                if start % 4:
                    continue
                if role not in self.arts:
                    possible = self.walkers.get(old.walking_texture(), ())
                    if len(possible) != 1 or old.animations != 4:
                        continue
                    role = next(iter(possible))
                walkers.setdefault((start, role), (old, []))[1].append(cell)
                if face:
                    portraits[(face_offset, role)].append(cell + 4)

        edits = []
        for (start, role), (old, cells) in walkers.items():
            new = self.replacement_arts[self.assignments[role]]
            if new.animations >= old.animations:
                edits.append((start, len(old.data), cells, new, False))
            else:
                self.counts["field_pose_kept"] += len(cells)
        for (start, role), cells in portraits.items():
            edits.append((start, len(self.arts[role].portrait()), cells,
                          self.replacement_arts[self.assignments[role]], True))

        def replacement_size(art, face):
            return len(art.data) - art.graphics if face else art.graphics

        # Many maps store portraits in their own bank. Plan both kinds of asset together so
        # shorter replacements can free room for larger ones without moving other map data.
        for start, length, cells, new, face in sorted(
            edits, key=lambda edit: replacement_size(edit[3], edit[4]) - edit[1],
        ):
            stop = start + length
            size = replacement_size(new, face)
            padded = align(stop)
            end = stop & -4 if any(original[stop:padded]) else padded
            reclaim = (start, end) if overlay.owns(start, end, set(cells)) else None
            dest = overlay.place(size, reclaim)
            if dest is None:
                self.counts["portrait_unfitted" if face else "field_unfitted"] += len(cells)
                continue
            content = new.portrait() if face else new.at(base + dest, portrait=False)
            overlay.data[dest:dest + size] = content
            overlay.repoint(cells, dest if face else dest + new.header)
            self.counts["face_portraits" if face else "field_actors"] += len(cells)
