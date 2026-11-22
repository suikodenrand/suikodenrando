"""Replace verified picture, character, and text references in map overlays."""

from collections import Counter, defaultdict
import re
import struct

from field import FieldSprite
from pilot import MAP_ADDRESS, TEXT_BANKS, encode_name, read_word, write_word
from roster import STAR_COUNT, WAR_PORTRAITS, cast_package, portrait_at


# The reviewed field and war overlays load at 0x80010000. Keep their data
# below the resident executable and its work areas.
MAX_OVERLAY_SIZE = 0xA0000
ADDRESS_WORD = re.compile(rb"(?=[\x00-\xff]{2}[\x01-\x0a]\x80)")
TEXT = re.compile(rb"(?:[\x01-\x04\x10-\x7e\xbf]|[\x05\x06][\x00-\xff]){2,2047}\x00")
LETTERS = rb"\x11-\x44"
VISIBLE_TEXT = re.compile(rb"[\x11-\x44]{2}")


class Overlay:
    def __init__(self, original):
        self.original = original
        self.data = bytearray(original)
        self.stored = {}
        self.free = []
        self.addresses = None

    def reserve(self, size):
        candidates = [(end - start, start, index) for index, (start, end) in enumerate(self.free)
                      if end - start >= size]
        if candidates:
            _, start, index = min(candidates)
            _, end = self.free.pop(index)
            tail = (start + size + 3) & -4
            if tail < end:
                self.free.append((tail, end))
            return start
        self.data.extend(b"\0" * (-len(self.data) % 4))
        offset = len(self.data)
        self.data.extend(b"\0" * size)
        return offset

    def allocate(self, content):
        offset = self.reserve(len(content))
        self.data[offset:offset + len(content)] = content
        return offset

    def store(self, content):
        if content not in self.stored:
            self.stored[content] = self.allocate(content)
        return self.stored[content]

    def references(self, target):
        if self.addresses is None:
            self.addresses = defaultdict(list)
            for match in ADDRESS_WORD.finditer(self.original):
                cell = match.start()
                if cell % 4 == 0:
                    address = read_word(self.original, cell) - MAP_ADDRESS
                    self.addresses[address].append(cell)
        return self.addresses.get(target, ())

    def release(self, start, end, references):
        known = set(references)
        for address in range(start, end, 4):
            if any(cell not in known and not start <= cell < end for cell in self.references(address)):
                return
        self.free.append((start, end))

    def repoint(self, references, target):
        for cell in references:
            write_word(self.data, cell, MAP_ADDRESS + target)


class OverlayPatcher:
    def __init__(self, cast, assignments, support_sprites):
        self.cast = cast
        self.assignments = assignments
        self.changed = {role: cast[donor] for role, donor in assignments.items() if role != donor}
        self.packages = {role: cast_package(role, donor) for role, donor in self.changed.items()
                         if donor.package is not None}
        self.fields = {role: FieldSprite.from_package(cast[role].package) for role in self.packages}
        self.fields.update(support_sprites)
        self.donor_fields = {role: FieldSprite.from_package(data) for role, data in self.packages.items()}
        self.donor_fields.update({role: support_sprites[assignments[role]] for role in support_sprites})
        self.palettes = {hero.portrait[1] for hero in cast.values()}
        self.anchors = defaultdict(list)
        for role in self.packages:
            self.anchors[cast[role].package[:64]].append((role, "package"))
        for role, sprite in self.fields.items():
            self.anchors[sprite.anchor].append((role, "field"))
        self.asset_search = re.compile(b"|".join(re.escape(anchor) for anchor in self.anchors))

        self.aliases = self.dialogue_aliases()
        self.name_search = re.compile(
            b"(?<![" + LETTERS + b"])(?:"
            + b"|".join(re.escape(name) for name in sorted(self.aliases, key=lambda n: (-len(n), n)))
            + b")(?![" + LETTERS + b"])"
        )
        self.counts = Counter()

    def dialogue_aliases(self):
        aliases = {}
        # These short names also occur as ordinary English words or labels.
        ambiguous = {"Hero", "Rock", "Window", "Apple", "Zen", "Max", "Gen", "Gon", "Mace"}
        for role, donor in self.changed.items():
            hero = self.cast[role]
            for name in (hero.name, hero.short_name):
                if name not in ambiguous and len(name) >= 3:
                    aliases[encode_name(name)] = encode_name(donor.short_name)
        return aliases

    def patch(self, path, original):
        overlay = Overlay(original)
        name = path.rsplit("/", 1)[-1]
        if path.startswith("DATA/16_WAR/") and name in WAR_PORTRAITS:
            self.patch_war(overlay, WAR_PORTRAITS[name])
        else:
            self.patch_assets(overlay)
            self.patch_portraits(overlay)
        self.patch_dialogue(overlay, TEXT_BANKS.get(name.removesuffix(".BIN"), ()))
        if len(overlay.data) > MAX_OVERLAY_SIZE and len(overlay.data) > len(original):
            raise ValueError(f"Character data exceeds the overlay memory limit in {path}")
        if overlay.data != original:
            return overlay.data
        return None

    def patch_assets(self, overlay):
        for match in self.asset_search.finditer(overlay.original):
            if match.start() % 4:
                continue
            for role, kind in self.anchors[match[0]]:
                if kind == "package":
                    self.patch_package(overlay, role, match.start())
                else:
                    self.patch_field(overlay, role, match.start() - 24)

    def patch_package(self, overlay, role, start):
        original = self.cast[role].package
        end = start + len(original)
        if overlay.original[start:end] != original:
            return
        references = overlay.references(start)
        if not references:
            return
        package = self.packages[role]
        if len(package) <= len(original):
            overlay.data[start:end] = package.ljust(len(original), b"\0")
        else:
            overlay.release(start, end, references)
            overlay.repoint(references, overlay.store(package))
        self.counts["embedded_fighters"] += 1

    def patch_field(self, overlay, role, head):
        original = self.fields[role]
        start = head - original.head
        end = start + len(original.data)
        if start < 0 or overlay.original[start:end] != original.at(head):
            return
        replacement = self.donor_fields[role]
        position = head - replacement.head
        stop = position + len(replacement.data)
        if start <= position <= head < stop <= end:
            overlay.data[start:end] = b"\0" * (end - start)
            overlay.data[position:stop] = replacement.at(head)
        else:
            references = overlay.references(head)
            if not references:
                return
            overlay.release(start, end, references)
            position = overlay.reserve(len(replacement.data))
            destination = position + replacement.head
            overlay.data[position:position + len(replacement.data)] = replacement.at(destination)
            overlay.repoint(references, destination)
        self.counts["field_sprites"] += 1

    def put_portrait(self, overlay, portrait):
        image, palette = portrait
        return MAP_ADDRESS + overlay.store(image), MAP_ADDRESS + overlay.store(palette)

    def patch_portraits(self, overlay):
        data = overlay.original
        # A map's portrait table contains triples of (story ID, image, palette).
        # A stock palette and the length-prefixed compressed image independently
        # identify these triples among the map's other tagged pointers.
        descriptors = defaultdict(list)
        for match in ADDRESS_WORD.finditer(data):
            cell = match.start()
            if cell < 4 or cell % 4 or cell + 8 > len(data):
                continue
            role, image_ptr, palette_ptr = struct.unpack_from("<III", data, cell - 4)
            if role not in self.changed:
                continue
            image, palette = image_ptr - MAP_ADDRESS, palette_ptr - MAP_ADDRESS
            if not (cell < image < len(data) - 4 and cell < palette <= len(data) - 512):
                continue
            size = read_word(data, image)
            if (data[palette:palette + 512] not in self.palettes or not 0x600 <= size <= 0x1000
                    or image + 4 + size > len(data)):
                continue
            descriptors[role, image_ptr, palette_ptr].append(cell)

        for (role, image_ptr, palette_ptr), cells in descriptors.items():
            old_image, _ = portrait_at(data, image_ptr, palette_ptr)
            new_image, new_palette = self.changed[role].portrait
            image = image_ptr - MAP_ADDRESS
            owned = set(overlay.references(image)) == set(cells)
            if len(new_image) <= len(old_image) and owned:
                overlay.data[image:image + len(old_image)] = new_image.ljust(len(old_image), b"\0")
                replacement = image_ptr, MAP_ADDRESS + overlay.store(new_palette)
            else:
                if owned:
                    overlay.release(image, image + len(old_image), cells)
                replacement = self.put_portrait(overlay, (new_image, new_palette))
            for cell in cells:
                struct.pack_into("<II", overlay.data, cell, *replacement)
            self.counts["map_portraits"] += len(cells)

    def patch_war(self, overlay, table):
        original = overlay.original
        for role, donor in self.changed.items():
            if role >= STAR_COUNT:
                continue
            source = table + donor.index * 8
            destination = table + self.cast[role].index * 8
            original_portrait = struct.unpack_from("<II", original, destination)
            # Early war maps use a tiny placeholder for a star who is not available yet.
            if len(portrait_at(original, *original_portrait)[0]) < 0x600:
                continue
            replacement = struct.unpack_from("<II", original, source) if donor.index < STAR_COUNT else None
            if replacement is None or len(portrait_at(original, *replacement)[0]) < 0x600:
                replacement = self.put_portrait(overlay, donor.portrait)
            struct.pack_into("<II", overlay.data, destination, *replacement)
            self.counts["war_portraits"] += 1

    def patch_dialogue(self, overlay, banks):
        data = overlay.original
        if not self.name_search.search(data):
            return
        messages = {}

        def text_end(target):
            if target not in messages:
                match = TEXT.match(data, target, min(len(data), target + 2048))
                end = match.end() if match and VISIBLE_TEXT.search(match[0]) else 0
                aligned = (end + 3) & -4
                messages[target] = end if aligned <= len(data) and not any(data[end:aligned]) else 0
            return messages[target]

        cells = set()
        for match in ADDRESS_WORD.finditer(data):
            cell = match.start()
            if cell % 4 or cell + 8 > len(data):
                continue
            first, second = struct.unpack_from("<II", data, cell)
            start, next_start = first - MAP_ADDRESS, second - MAP_ADDRESS
            if not (0 < start < next_start < cell and (start | next_start) % 4 == 0):
                continue
            end = text_end(start)
            if end and (end + 3) & -4 == next_start and text_end(next_start):
                cells.update((cell, cell + 4))
        for first, end, _ in banks:
            cells.update(range(first, end, 4))

        processed = set()
        for cell in sorted(cells):
            start = read_word(data, cell) - MAP_ADDRESS
            if not 0 <= start < len(data) or start in processed:
                continue
            processed.add(start)
            end = text_end(start)
            if not end or not self.name_search.search(data, start, end):
                continue
            original = data[start:end]
            replacement = self.replace_names(original)
            if replacement == original:
                continue
            aligned = (end + 3) & -4
            references = overlay.references(start)
            if len(replacement) <= aligned - start:
                overlay.data[start:aligned] = replacement.ljust(aligned - start, b"\0")
            else:
                overlay.repoint(references, overlay.store(replacement))
            self.counts["dialogue_lines"] += 1

    def replace_names(self, message):
        # Preserve script control arguments and the original line/page breaks.
        tokens = re.split(rb"([\x05\x06][\x00-\xff]|[\x01\x02])", message)
        for index in range(0, len(tokens), 2):
            original = tokens[index]
            replacement = self.name_search.sub(lambda match: self.aliases[match[0]], original)
            if len(replacement) <= max(len(original), 36):
                tokens[index] = replacement
        return b"".join(tokens)
