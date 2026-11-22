"""Regression checks for the PSX shuffle and disc writer."""

import hashlib
import io
from pathlib import Path
import re
import struct
from tempfile import TemporaryDirectory
from types import SimpleNamespace
import unittest
from unittest.mock import patch

from disc import BLOCK_SIZE, PAYLOAD_OFFSET, SECTOR_SIZE, Disc, new_sector, original_track, update_integrity, write_dual_word
from overlays import Overlay
from pilot import MAP_ADDRESS
from roster import GUARDED_COMBAT_IDS, NAMED_HERO, assign, permute
from seed import seed_code, write_disc
import suikoden2
from suikoden2_scene import FieldArt, MAP_BASES, MAP_LIMIT, ScenePatcher, align, native_name, word


def directory_record(name, lba, size, directory=False):
    row = bytearray(33 + len(name) + (not len(name) % 2))
    row[0] = len(row)
    row[25] = 2 if directory else 0
    row[32] = len(name)
    row[33:33 + len(name)] = name
    write_dual_word(row, 2, lba)
    write_dual_word(row, 10, size)
    return row


def fixture_disc():
    blocks = [b""] * 24
    volume = bytearray(BLOCK_SIZE)
    volume[:7] = b"\x01CD001\x01"
    write_dual_word(volume, 80, len(blocks))
    root = directory_record(b"\0", 21, BLOCK_SIZE, directory=True)
    volume[156:156 + len(root)] = root
    blocks[16] = volume
    blocks[21] = root + directory_record(b"\1", 21, BLOCK_SIZE, directory=True) + directory_record(
        b"TEST.BIN;1", 22, 4,
    )
    blocks[22] = b"base"
    return io.BytesIO(b"".join(new_sector(lba, data) for lba, data in enumerate(blocks)))


def fixture_field_art(marker, face_size):
    start, palette, clut, frames, animations = 36, 44, 80, 84, 104
    header = 144
    package = bytearray(header + 32 + 5 + face_size)
    package[start + 4] = marker
    struct.pack_into("<I", package, palette, start)
    struct.pack_into("<I", package, clut, palette + 4)
    struct.pack_into("<HHBBH6I", package, header, 1, 4, 1, 0x21, 0,
                     136, 140, palette, clut, frames, animations)
    struct.pack_into("<I", package, header + 32, face_size)
    package[header + 37:] = bytes([marker]) * face_size
    return FieldArt.from_package(package, header)


class SeedTests(unittest.TestCase):
    def test_assignment_repeats_and_respects_native_templates(self):
        cast = {role: SimpleNamespace(package=b"fighter" if role < 78 else None) for role in range(110)}
        first = assign(cast, "Agilen", shuffle_hero=True)
        again = assign(dict(reversed(list(cast.items()))), "Agilen", shuffle_hero=True)
        self.assertEqual(first, again)
        self.assertNotEqual(first, assign(cast, "Another seed", shuffle_hero=True))
        self.assertEqual(set(first), set(first.values()))
        self.assertTrue(all(role != donor and (role < 78) == (donor < 78) for role, donor in first.items()))
        fixed = assign(cast, "Agilen", shuffle_support=False)
        self.assertEqual(fixed[NAMED_HERO], NAMED_HERO)
        self.assertTrue(all(fixed[role] == role for role in range(78, 110)))

    def test_versioned_permutation(self):
        self.assertEqual(permute(range(6), "Agilen", "fighters"), {0: 3, 1: 5, 2: 4, 3: 1, 4: 0, 5: 2})

    def test_guarded_roster_matches_original_combat_catalog(self):
        core = Path(__file__).resolve().parents[2] / "src" / "SuikodenRandomizer.Core" / "CombatCatalog.cs"
        pool = re.search(r"\bCombatIds\s*=\s*\{([^}]+)\}", core.read_text(encoding="utf-8"))
        self.assertIsNotNone(pool)
        expected = {int(value) for value in re.findall(r"\d+", pool[1])}
        self.assertEqual(len(expected), 75)
        self.assertEqual(tuple(sorted(expected)), GUARDED_COMBAT_IDS)

        cast = {role: SimpleNamespace(package=b"fighter") for role in range(110)}
        first = assign(cast, "Agilen", guarded=True)
        self.assertEqual(first, assign(cast, "Agilen", guarded=True, shuffle_support=False))
        self.assertEqual({role for role, donor in first.items() if role != donor}, expected)
        self.assertEqual({first[role] for role in expected}, expected)
        self.assertEqual(set(first), set(first.values()))

        with self.assertRaises(ValueError):
            assign(cast, "Agilen", guarded=True, shuffle_hero=True)
        cast[GUARDED_COMBAT_IDS[0]].package = None
        with self.assertRaises(ValueError):
            assign(cast, "Agilen", guarded=True)

    def test_guarded_seed_filename_uses_its_own_profile(self):
        self.assertEqual(seed_code("Agilen-PSX-01", True, True), "4395b3036b64")
        guarded = seed_code("Agilen-PSX-01", False, True, guarded=True)
        self.assertEqual(guarded, seed_code("Agilen-PSX-01", False, False, guarded=True))
        self.assertNotEqual(guarded, seed_code("Agilen-PSX-01", False, False))

    def test_suikoden_ii_uses_a_separate_combat_pool_and_seed_code(self):
        cast = suikoden2.assign("Agilen-PSX-II-01")
        self.assertEqual(set(cast), set(range(2, 80)))
        self.assertEqual(set(cast), set(cast.values()))
        self.assertTrue(all(role != donor for role, donor in cast.items()))
        self.assertEqual({cast[role] for role in (49, 50, 72, 74)}, {49, 50, 72, 74})
        self.assertEqual(cast[2], 73)
        self.assertEqual(cast, suikoden2.assign("Agilen-PSX-II-01"))
        self.assertNotEqual(cast, suikoden2.assign("another seed"))
        code = seed_code("Agilen-PSX-II-01", False, True, sequel=True)
        self.assertEqual(code, "27f7f55cba47")
        self.assertNotEqual(code, seed_code("Agilen-PSX-II-01", False, True, guarded=True))

    def test_suikoden_ii_karen_motion_follows_the_shuffled_role(self):
        checks = suikoden2.KAREN_MOTION_CHECKS
        files = {path: bytearray(sites[-1][0] + 4) for path, sites in checks.items()}
        for path, sites in checks.items():
            for offset, instruction in sites:
                struct.pack_into("<I", files[path], offset, instruction)

        executable = files.pop(suikoden2.EXECUTABLE)
        suikoden2.patch_karen_motion(executable, files, {25: 28, 28: 25})
        for path, sites in checks.items():
            data = executable if path == suikoden2.EXECUTABLE else files[path]
            for offset, instruction in sites:
                self.assertEqual(word(data, offset), (instruction & 0xFFFF0000) | 28)
        with self.assertRaisesRegex(ValueError, "Unexpected Suikoden II animation check"):
            suikoden2.patch_karen_motion(executable, files, {25: 28, 28: 25})

    def test_suikoden_ii_clonecd_descriptor_requires_a_single_data_track(self):
        descriptor = "[Disc]\nSessions=1\nDataTracksScrambled=0\n[TRACK 1]\nMODE=2\nINDEX 1=0\n"
        with TemporaryDirectory() as directory:
            ccd = Path(directory) / "original.ccd"
            img = ccd.with_suffix(".img")
            img.touch()
            ccd.write_text(descriptor, encoding="ascii")
            self.assertEqual(original_track(ccd), img.resolve())
            ccd.write_text(descriptor + "[TRACK 2]\nMODE=0\nINDEX 1=123\n", encoding="ascii")
            with self.assertRaisesRegex(ValueError, "one unscrambled Mode 2 track"):
                original_track(ccd)

    def test_suikoden_ii_map_repoints_only_the_matching_actor(self):
        base = MAP_BASES[1]
        art = fixture_field_art(1, 8)
        donor = fixture_field_art(2, 4)
        cast = {2: SimpleNamespace(name="Flik"), 3: SimpleNamespace(name="Viktor")}
        donor_with_palette = donor.with_palette(5)
        patcher = ScenePatcher(cast, {2: art, 3: donor}, {2: 3, 3: 2},
                               {2: art.with_palette(11), 3: donor_with_palette})
        self.assertEqual(donor_with_palette.portrait()[:4], donor.portrait()[:4])
        self.assertEqual(donor_with_palette.portrait()[4], 0x8A)
        self.assertEqual(donor_with_palette.portrait()[5:], donor.portrait()[5:])
        protected = (1, 80, 81, 146, 155, 174)
        self.assertEqual(suikoden2.PROTECTED_STORY_IDS, set(protected))
        walking_start = 16 * (2 + len(protected))
        data = bytearray(walking_start)
        original_header = base + len(data) + art.header
        struct.pack_into("<I", data, 0, base + 36)
        data.extend(art.at(base + len(data), portrait=False))
        data.extend(bytes(32))
        face = base + len(data)
        data.extend(art.portrait())
        struct.pack_into("<III", data, 16, 2, original_header, face)
        for index, role in enumerate(protected, start=2):
            struct.pack_into("<III", data, index * 16, role, original_header, face)
        patched = patcher.patch(bytes(data))
        new_header, new_face = struct.unpack_from("<II", patched, 20)
        start = new_header - base - donor.header
        self.assertGreaterEqual(start, align(len(data)))
        self.assertEqual(patched[32:len(data)], data[32:])
        self.assertEqual(patched[start:start + donor.graphics], donor.at(base + start, portrait=False))
        self.assertEqual(patched[new_face - base:new_face - base + len(donor.portrait())],
                         donor_with_palette.portrait())
        self.assertEqual(patcher.counts["field_actors"], 1)
        self.assertEqual(patcher.counts["face_portraits"], 1)

        data.extend(bytes(MAP_LIMIT - len(data)))
        self.assertIsNone(patcher.patch(bytes(data)))
        data[32:walking_start] = bytes(walking_start - 32)
        patched = patcher.patch(bytes(data))
        self.assertEqual(len(patched), MAP_LIMIT)
        self.assertEqual(patched[walking_start:walking_start + donor.graphics],
                         donor.at(base + walking_start, portrait=False))

    def test_suikoden_ii_dialogue_relocates_a_longer_name_and_preserves_controls(self):
        base = MAP_BASES[0]
        cast = {2: SimpleNamespace(name="Flik"), 3: SimpleNamespace(name="Viktor")}
        patcher = ScenePatcher(cast, {}, {2: 3, 3: 2}, {})
        message = b"\xA4" + native_name("Flik") + b"\x05\0\xA4\0"
        new_message = b"\xA4" + native_name("Viktor") + b"\x05\0\xA4\0"
        # Two full names in the line force an allocation past the original padded length.
        message = message[:-2] + native_name("Flik") + message[-2:]
        new_message = new_message[:-2] + native_name("Viktor") + new_message[-2:]
        other = b"\xA4" + native_name("Jowy") + b"\xA4\0"
        data = bytearray(96)
        second = align(48 + len(message))
        struct.pack_into("<I", data, 0, base + 32)
        struct.pack_into("<II", data, 16, base + 48, base + second)
        data[48:48 + len(message)] = message
        data[second:second + len(other)] = other
        patched = patcher.patch(bytes(data))
        dest = word(patched, 16) - base
        self.assertEqual(dest, len(data))
        self.assertEqual(patched[dest:dest + len(new_message)], new_message)
        self.assertEqual(patched[second:second + len(other)], other)
        self.assertEqual(patcher.counts["dialogue_lines"], 1)

    def test_suikoden_ii_new_game_cast_keeps_story_progression(self):
        original = bytearray(4974)
        variable = 0x77A
        for role, level, weapon in ((2, 5, 7), (3, 5, 4)):
            original[role * 2] = role
            original[0x100 + role * 18] = role
            start = variable + role * 36
            original[start:start + 4] = bytes((level, role, 0, 0))
            original[start + 4] = role * 10
            original[start + 19:start + 21] = bytes((weapon, role))
        patched = suikoden2.patch_starting_cast(original, 0, {2: 3, 3: 2})
        start = variable + 2 * 36
        self.assertEqual(patched[4], 3)
        self.assertEqual(patched[0x100 + 2 * 18], 3)
        self.assertEqual(patched[start:start + 5], bytes((5, 2, 0, 0, 30)))
        self.assertEqual(patched[start + 19:start + 21], bytes((7, 3)))

    def test_oversize_disc_file_updates_iso_and_integrity(self):
        stream = fixture_disc()
        disc = Disc(stream)
        self.assertEqual(disc.read_file("TEST.BIN"), b"base")
        replacement = b"r" * (BLOCK_SIZE + 5)
        plans, blocks = disc.plan_replacements({"TEST.BIN": replacement})
        self.assertEqual(plans["TEST.BIN"].lba, 24)
        disc.write_replacement("TEST.BIN", replacement, plans["TEST.BIN"])
        disc.update_volume_size(blocks)
        self.assertEqual(len(stream.getvalue()), 26 * SECTOR_SIZE)
        self.assertEqual(Disc(stream).read_file("TEST.BIN"), replacement)
        record_lba, record = disc.records["TEST.BIN"]
        for lba in (16, record_lba, 24, 25):
            raw = disc.sector(lba)
            refreshed = bytearray(raw)
            update_integrity(refreshed)
            self.assertEqual(raw, refreshed)
        raw = disc.sector(record_lba)
        self.assertEqual(struct.unpack_from(">I", raw, PAYLOAD_OFFSET + record + 6)[0], 24)
        self.assertEqual(struct.unpack_from(">I", raw, PAYLOAD_OFFSET + record + 14)[0], len(replacement))

    def test_overlay_does_not_reuse_an_extent_with_an_external_reference(self):
        original = bytearray(96)
        struct.pack_into("<I", original, 0, MAP_ADDRESS + 36)
        struct.pack_into("<I", original, 4, MAP_ADDRESS + 40)
        overlay = Overlay(original)
        overlay.release(32, 64, [0])
        self.assertEqual(overlay.allocate(b"donor"), 96)

    def test_disc_writer_checks_the_copy_and_hashes_the_saved_track(self):
        source = fixture_disc()
        edits = {"TEST.BIN": b"x" * (BLOCK_SIZE + 1)}
        replacements, blocks = Disc(source).plan_replacements(edits)
        expected_source = hashlib.sha1(source.getbuffer()).hexdigest()
        with TemporaryDirectory() as directory:
            track = Path(directory) / "test.bin"
            with patch("seed.BASE_SHA1", "0" * 40):
                with self.assertRaisesRegex(ValueError, "copied disc"):
                    write_disc(source, track, edits, replacements, blocks, lambda _: None)
            with patch("seed.BASE_SHA1", expected_source):
                recorded = write_disc(source, track, edits, replacements, blocks, lambda _: None)
            self.assertEqual(recorded, hashlib.sha256(track.read_bytes()).hexdigest())


if __name__ == "__main__":
    unittest.main()
