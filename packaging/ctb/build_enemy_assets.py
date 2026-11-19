"""Validate locally extracted S1 thumbnails and stage only the CTB runtime assets.

No image libraries, downloads, game execution or artwork generation are used by
the packaging path. Original game artwork stays outside the source repository.
"""
from __future__ import annotations

import argparse
import binascii
import hashlib
import json
import re
import struct
import sys
from pathlib import Path

sys.dont_write_bytecode = True
SOURCE_COUNT = 136
PNG_SIGNATURE = b"\x89PNG\r\n\x1a\n"


class AssetValidationError(ValueError):
    pass


def digest(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def check_png(data: bytes, label: str) -> None:
    if not data.startswith(PNG_SIGNATURE):
        raise AssetValidationError(f"Not a PNG: {label}")
    position, first, saw_data, saw_end = len(PNG_SIGNATURE), True, False, False
    while position < len(data):
        if len(data) - position < 12:
            raise AssetValidationError(f"Truncated PNG chunk: {label}")
        length = struct.unpack_from(">I", data, position)[0]
        end = position + length + 12
        if end > len(data):
            raise AssetValidationError(f"Truncated PNG chunk data: {label}")
        kind = data[position + 4:position + 8]
        payload = data[position + 8:end - 4]
        expected_crc = struct.unpack_from(">I", data, end - 4)[0]
        if binascii.crc32(kind + payload) & 0xFFFFFFFF != expected_crc:
            raise AssetValidationError(f"PNG chunk CRC mismatch: {label}")
        if first:
            if kind != b"IHDR" or length != 13:
                raise AssetValidationError(f"Missing PNG IHDR: {label}")
            width, height, depth, colour, compression, filtering, interlace = struct.unpack(">IIBBBBB", payload)
            if (width, height, depth, colour, compression, filtering, interlace) != (96, 96, 8, 6, 0, 0, 0):
                raise AssetValidationError(f"Expected a static 96x96 RGBA8 PNG: {label}")
            first = False
        elif kind == b"IHDR" or kind == b"acTL":
            raise AssetValidationError(f"Unexpected PNG header or animation: {label}")
        if kind == b"IDAT":
            saw_data = True
        if kind == b"IEND":
            if length != 0 or end != len(data):
                raise AssetValidationError(f"Invalid PNG end: {label}")
            saw_end = True
            break
        position = end
    if first or not saw_data or not saw_end:
        raise AssetValidationError(f"Incomplete PNG: {label}")


def relative_file(root: Path, value: object) -> Path:
    if not isinstance(value, str) or not value or Path(value).is_absolute():
        raise AssetValidationError("Thumbnail paths must be nonempty and relative to the source directory.")
    path = (root / value).resolve()
    try:
        path.relative_to(root)
    except ValueError as error:
        raise AssetValidationError(f"Thumbnail path leaves the source directory: {value}") from error
    if not path.is_file():
        raise AssetValidationError(f"Missing extracted thumbnail: {path}")
    return path


def validate(source: Path) -> tuple[dict, list[tuple[dict, bytes]]]:
    root = source.resolve()
    manifest_path = root / "manifest.json"
    if not root.is_dir() or not manifest_path.is_file():
        raise AssetValidationError(
            "Enemy thumbnail extraction was not found. Supply -EnemyThumbnailsDirectory with the "
            "S1-Enemy-Thumbnails folder containing manifest.json and thumbnails-96.")
    try:
        manifest_bytes = manifest_path.read_bytes()
        manifest = json.loads(manifest_bytes.decode("utf-8-sig"))
    except (OSError, UnicodeError, json.JSONDecodeError) as error:
        raise AssetValidationError(f"Could not read extracted-thumbnail manifest: {manifest_path}") from error
    if not isinstance(manifest, dict):
        raise AssetValidationError("The extracted-thumbnail manifest must be a JSON object.")
    records = manifest.get("records")
    if manifest.get("schemaVersion") != 1 or not isinstance(records, list) or len(records) != SOURCE_COUNT:
        raise AssetValidationError("Expected schema 1 with exactly 136 original S1 thumbnail records.")
    counts = manifest.get("counts", {})
    if not isinstance(counts, dict) or counts.get("sourceRecords") != SOURCE_COUNT or counts.get("thumbnails") != SOURCE_COUNT or counts.get("unresolved") != 0:
        raise AssetValidationError("Enemy thumbnail extraction must report 136 source records, 136 thumbnails and zero unresolved.")
    ids, aliases, prepared = set(), {}, []
    for row in records:
        if not isinstance(row, dict):
            raise AssetValidationError("Every thumbnail record must be an object.")
        source_id, opd_name = row.get("sourceId"), row.get("opdName")
        if not isinstance(source_id, str) or not re.fullmatch(r"[A-Za-z0-9_]+", source_id):
            raise AssetValidationError(f"Invalid canonical enemy source ID: {source_id!r}")
        if source_id.casefold() in ids:
            raise AssetValidationError(f"Duplicate canonical enemy source ID: {source_id}")
        ids.add(source_id.casefold())
        if not isinstance(opd_name, str) or not re.fullmatch(r"[A-Za-z0-9_]+", opd_name):
            raise AssetValidationError(f"Missing or invalid native OPD alias for {source_id}")
        expected = row.get("thumbnailSha256")
        if not isinstance(expected, str) or not re.fullmatch(r"[A-Fa-f0-9]{64}", expected):
            raise AssetValidationError(f"Missing thumbnail SHA-256 for {source_id}")
        data = relative_file(root, row.get("thumbnail")).read_bytes()
        actual = digest(data)
        if actual != expected.lower():
            raise AssetValidationError(f"Extracted thumbnail SHA-256 mismatch: {source_id}")
        if row.get("thumbnailSize") != [96, 96]:
            raise AssetValidationError(f"Manifest does not declare a 96x96 thumbnail: {source_id}")
        check_png(data, source_id)
        for alias in (source_id, opd_name):
            key = alias.casefold()
            if key in aliases and aliases[key] != actual:
                raise AssetValidationError(f"Conflicting enemy alias resolves to different PNG hashes: {alias}")
            aliases[key] = actual
        prepared.append(({"sourceId": source_id, "opdName": opd_name,
                          "file": source_id + ".png", "sha256": actual}, data))
    prepared.sort(key=lambda value: value[0]["sourceId"])
    summary = {"sourceManifestSha256": digest(manifest_bytes), "entries": len(prepared),
               "aliasKeys": len(aliases), "pngSize": [96, 96]}
    return summary, prepared


def stage(source: Path, output: Path) -> dict:
    summary, prepared = validate(source)
    destination = output.resolve()
    if destination.exists():
        raise AssetValidationError(f"Enemy asset output already exists; use a fresh stage: {destination}")
    destination.mkdir(parents=True)
    for entry, data in prepared:
        (destination / entry["file"]).write_bytes(data)
    index = {"schemaVersion": 1, "entries": [entry for entry, _ in prepared]}
    index_bytes = (json.dumps(index, indent=2, ensure_ascii=False) + "\n").encode("utf-8")
    (destination / "index.json").write_bytes(index_bytes)
    return {**summary, "output": str(destination), "indexSha256": digest(index_bytes)}


def self_test() -> bool:
    import tempfile
    import unittest
    import zlib

    def chunk(kind: bytes, payload: bytes) -> bytes:
        return struct.pack(">I", len(payload)) + kind + payload + struct.pack(">I", binascii.crc32(kind + payload) & 0xFFFFFFFF)

    def png(width=96, colour=b"\x56\xb4\xe9\xff"):
        rows = (b"\0" + colour * width) * 96
        return PNG_SIGNATURE + chunk(b"IHDR", struct.pack(">IIBBBBB", width, 96, 8, 6, 0, 0, 0)) + chunk(b"IDAT", zlib.compress(rows)) + chunk(b"IEND", b"")

    class AssetTests(unittest.TestCase):
        def setUp(self):
            self.temporary = tempfile.TemporaryDirectory(prefix="ctb-assets-test-")
            self.addCleanup(self.temporary.cleanup)
            self.root = Path(self.temporary.name)
            self.source = self.root / "source"
            (self.source / "thumbnails-96").mkdir(parents=True)
            self.image = png()
            self.manifest = {"schemaVersion": 1, "counts": {"sourceRecords": 136, "thumbnails": 136, "unresolved": 0}, "records": []}
            for number in range(SOURCE_COUNT):
                source_id = f"enemy_{number:03d}"
                relative = f"thumbnails-96/{source_id}.png"
                (self.source / relative).write_bytes(self.image)
                self.manifest["records"].append({"sourceId": source_id, "opdName": source_id + "_00",
                    "thumbnail": relative, "thumbnailSize": [96, 96], "thumbnailSha256": digest(self.image)})
            self.save_manifest()

        def save_manifest(self):
            (self.source / "manifest.json").write_text(json.dumps(self.manifest), encoding="utf-8")

        def test_stage_is_deterministic_and_copies_only_normalized_images(self):
            (self.source / "raw-frame.png").write_bytes(b"must not be included")
            first, second = self.root / "first", self.root / "second"
            a, b = stage(self.source, first), stage(self.source, second)
            self.assertEqual(a["indexSha256"], b["indexSha256"])
            index = json.loads((first / "index.json").read_text(encoding="utf-8"))
            self.assertEqual(len(index["entries"]), SOURCE_COUNT)
            self.assertEqual(len(list(first.iterdir())), SOURCE_COUNT + 1)
            self.assertEqual((first / "enemy_000.png").read_bytes(), self.image)
            self.assertEqual(index["entries"][0]["opdName"], "enemy_000_00")

        def test_hash_mismatch_fails_before_any_output_is_created(self):
            (self.source / self.manifest["records"][0]["thumbnail"]).write_bytes(png(colour=b"\xff\0\0\xff"))
            destination = self.root / "stage"
            with self.assertRaisesRegex(AssetValidationError, "SHA-256 mismatch"):
                stage(self.source, destination)
            self.assertFalse(destination.exists())

        def test_different_hashes_cannot_share_an_alias(self):
            row = self.manifest["records"][1]
            row["opdName"] = self.manifest["records"][0]["opdName"]
            data = png(colour=b"\xff\0\0\xff")
            (self.source / row["thumbnail"]).write_bytes(data)
            row["thumbnailSha256"] = digest(data)
            self.save_manifest()
            with self.assertRaisesRegex(AssetValidationError, "Conflicting enemy alias"):
                validate(self.source)

        def test_identical_pixels_may_share_the_same_native_opd_alias(self):
            self.manifest["records"][1]["opdName"] = self.manifest["records"][0]["opdName"]
            self.save_manifest()
            summary, entries = validate(self.source)
            self.assertEqual(len(entries), SOURCE_COUNT)
            self.assertEqual(summary["aliasKeys"], 271)

        def test_wrong_dimensions_and_corrupt_chunk_crc_are_rejected(self):
            for bad in (png(width=95), self.image[:20] + bytes([self.image[20] ^ 1]) + self.image[21:]):
                row = self.manifest["records"][0]
                (self.source / row["thumbnail"]).write_bytes(bad)
                row["thumbnailSha256"] = digest(bad)
                self.save_manifest()
                with self.assertRaises(AssetValidationError):
                    validate(self.source)

        def test_traversal_and_case_colliding_source_ids_are_rejected(self):
            self.manifest["records"][0]["thumbnail"] = "../outside.png"
            (self.root / "outside.png").write_bytes(self.image)
            self.save_manifest()
            with self.assertRaisesRegex(AssetValidationError, "leaves the source directory"):
                validate(self.source)
            self.manifest["records"][0]["thumbnail"] = "thumbnails-96/enemy_000.png"
            self.manifest["records"][1]["sourceId"] = "ENEMY_000"
            self.save_manifest()
            with self.assertRaisesRegex(AssetValidationError, "Duplicate canonical"):
                validate(self.source)

        def test_missing_or_incomplete_extraction_fails_with_clear_instructions(self):
            with self.assertRaisesRegex(AssetValidationError, "EnemyThumbnailsDirectory"):
                validate(self.root / "missing")
            self.manifest["counts"]["unresolved"] = 1
            self.save_manifest()
            with self.assertRaisesRegex(AssetValidationError, "zero unresolved"):
                validate(self.source)

    result = unittest.TextTestRunner(stream=sys.stdout, verbosity=2).run(unittest.defaultTestLoader.loadTestsFromTestCase(AssetTests))
    return result.wasSuccessful()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--validate-only", action="store_true")
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    if args.self_test:
        return 0 if self_test() else 1
    if args.source is None or (args.output is None and not args.validate_only):
        parser.error("--source and either --output or --validate-only are required")
    if args.output is not None and args.validate_only:
        parser.error("--output and --validate-only are mutually exclusive")
    try:
        result = validate(args.source)[0] if args.validate_only else stage(args.source, args.output)
    except (AssetValidationError, OSError) as error:
        print("Enemy asset packaging failed: " + str(error), file=sys.stderr)
        return 1
    print(json.dumps(result, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
