"""Read and update Mode 2 Form 1 files on a PlayStation disc."""

from configparser import ConfigParser, Error as ConfigError
from dataclasses import dataclass
from pathlib import Path
import re
import struct


SECTOR_SIZE = 2352
BLOCK_SIZE = 2048
PAYLOAD_OFFSET = 24
PAYLOAD_END = PAYLOAD_OFFSET + BLOCK_SIZE
SYNC = b"\x00" + b"\xff" * 10 + b"\x00"


def track_path(cue):
    lines = [
        line.strip()
        for line in cue.read_text(encoding="utf-8-sig").splitlines()
        if line.strip() and not line.lstrip().startswith("REM ")
    ]
    if len(lines) != 3:
        raise ValueError("Expected one MODE2/2352 data track at INDEX 01 00:00:00")
    name = re.fullmatch(r'FILE "([^"]+)" BINARY', lines[0], re.IGNORECASE)
    if not name or [line.upper() for line in lines[1:]] != [
        "TRACK 01 MODE2/2352", "INDEX 01 00:00:00"
    ]:
        raise ValueError("Expected one MODE2/2352 data track at INDEX 01 00:00:00")
    return (cue.parent / name[1]).resolve(strict=True)


def original_track(image):
    if image.suffix.lower() == ".cue":
        return track_path(image)
    if image.suffix.lower() != ".ccd":
        raise ValueError("Choose the original disc's CUE or CloneCD CCD file")

    descriptor = ConfigParser(interpolation=None)
    try:
        descriptor.read_string(image.read_text(encoding="utf-8-sig"))
        tracks = [section for section in descriptor.sections() if section.upper().startswith("TRACK ")]
        valid = (len(tracks) == 1 and tracks[0].upper() == "TRACK 1"
                 and descriptor.getint("Disc", "Sessions") == 1
                 and descriptor.getint("Disc", "DataTracksScrambled") == 0
                 and descriptor.getint(tracks[0], "MODE") == 2
                 and descriptor.getint(tracks[0], "INDEX 1") == 0)
    except (ConfigError, ValueError):
        valid = False
    if not valid:
        raise ValueError("Expected a CloneCD image with one unscrambled Mode 2 track starting at sector 0")
    return image.with_suffix(".img").resolve(strict=True)


def make_integrity_tables():
    edc, forward, backward = [], [], [0] * 256
    for byte in range(256):
        value = byte
        for _ in range(8):
            value = (value >> 1) ^ (0xD8018001 if value & 1 else 0)
        edc.append(value)
        doubled = (byte << 1) ^ (0x11D if byte & 0x80 else 0)
        forward.append(doubled)
        backward[byte ^ doubled] = byte
    return edc, forward, backward


EDC, ECC_FORWARD, ECC_BACKWARD = make_integrity_tables()


def parity(data, major_count, minor_count, major_multiplier, minor_increment):
    size = major_count * minor_count
    result = bytearray(2 * major_count)
    for major in range(major_count):
        i = (major // 2) * major_multiplier + (major & 1)
        a, b = 0, 0
        for _ in range(minor_count):
            value = data[i]
            i = (i + minor_increment) % size
            a = ECC_FORWARD[a ^ value]
            b ^= value
        a = ECC_BACKWARD[ECC_FORWARD[a] ^ b]
        result[major] = a
        result[major_count + major] = a ^ b
    return result


def form1(sector):
    if (len(sector) != SECTOR_SIZE or sector[:12] != SYNC or sector[15] != 2
            or sector[16:20] != sector[20:24] or sector[18] & 0x20):
        raise ValueError("Expected a Mode 2 Form 1 sector")
    return sector


def update_integrity(sector):
    form1(sector)
    value = 0
    for byte in sector[16:PAYLOAD_END]:
        value = (value >> 8) ^ EDC[(value ^ byte) & 255]
    struct.pack_into("<I", sector, PAYLOAD_END, value)
    work = b"\0" * 4 + sector[16:PAYLOAD_END + 4]
    p = parity(work, 86, 24, 2, 86)
    sector[PAYLOAD_END + 4:2248] = p
    sector[2248:] = parity(work + p, 52, 43, 86, 88)


def block_count(size):
    return (size + BLOCK_SIZE - 1) // BLOCK_SIZE


def new_sector(lba, chunk, last=False):
    minutes, frames = divmod(lba + 150, 75 * 60)
    seconds, frames = divmod(frames, 75)
    address = bytes(value // 10 * 16 + value % 10 for value in (minutes, seconds, frames))
    subheader = bytes((0, 0, 0x89 if last else 0x08, 0))
    sector = bytearray(SECTOR_SIZE)
    sector[:PAYLOAD_OFFSET] = SYNC + address + b"\x02" + subheader * 2
    sector[PAYLOAD_OFFSET:PAYLOAD_OFFSET + len(chunk)] = chunk
    update_integrity(sector)
    return sector


def write_dual_word(data, offset, value):
    struct.pack_into("<I", data, offset, value)
    struct.pack_into(">I", data, offset + 4, value)


@dataclass(frozen=True)
class Extent:
    lba: int
    size: int


class Disc:
    def __init__(self, stream):
        self.stream = stream
        pvd = self.sector(16)[PAYLOAD_OFFSET:PAYLOAD_END]
        if pvd[:7] != b"\x01CD001\x01":
            raise ValueError("The track does not have a valid ISO 9660 descriptor")
        self.files = {}
        self.records = {}
        self.read_directory(self.extent(pvd, 156), "", set())

    @staticmethod
    def extent(row, offset):
        lba = struct.unpack_from("<I", row, offset + 2)[0]
        size = struct.unpack_from("<I", row, offset + 10)[0]
        return Extent(lba, size)

    def sector(self, lba):
        self.stream.seek(lba * SECTOR_SIZE)
        return form1(self.stream.read(SECTOR_SIZE))

    def read(self, extent):
        return b"".join(
            self.sector(extent.lba + n)[PAYLOAD_OFFSET:PAYLOAD_END]
            for n in range(block_count(extent.size))
        )[:extent.size]

    def read_directory(self, extent, prefix, read_dirs):
        if extent in read_dirs:
            raise ValueError("ISO directory visited twice")
        read_dirs.add(extent)
        data = self.read(extent)
        i = 0
        while i < len(data):
            length = data[i]
            if length == 0:
                i = (i // BLOCK_SIZE + 1) * BLOCK_SIZE
                continue
            record_offset = i
            record = data[i:i + length]
            i += length
            name = record[33:33 + record[32]]
            if name in (b"\0", b"\1"):
                continue
            path = prefix + name.decode("ascii").split(";", 1)[0]
            target = self.extent(record, 0)
            flags = record[25]
            if flags & 0x80:
                raise ValueError(f"Multi-extent file: {path}")
            if flags & 2:
                self.read_directory(target, path + "/", read_dirs)
            else:
                self.files[path] = target
                self.records[path] = (extent.lba + record_offset // BLOCK_SIZE,
                                      record_offset % BLOCK_SIZE)

    def read_file(self, path):
        return self.read(self.files[path])

    def write_file(self, path, data):
        extent = self.files[path]
        if len(data) != extent.size:
            raise ValueError(f"Replacement changes the size of {path}")
        return self.write_extent(extent, data)

    def write_extent(self, extent, data):
        written = 0
        for offset in range(0, len(data), BLOCK_SIZE):
            lba = extent.lba + offset // BLOCK_SIZE
            sector = bytearray(self.sector(lba))
            chunk = data[offset:offset + BLOCK_SIZE]
            end = PAYLOAD_OFFSET + len(chunk)
            if chunk != sector[PAYLOAD_OFFSET:end]:
                sector[PAYLOAD_OFFSET:end] = chunk
                update_integrity(sector)
                self.stream.seek(lba * SECTOR_SIZE)
                self.stream.write(sector)
                written += 1
        return written

    def plan_replacements(self, edits):
        """Keep files in their original allocation when possible."""
        size = self.stream.seek(0, 2)
        next_lba, remainder = divmod(size, SECTOR_SIZE)
        if remainder:
            raise ValueError("The source track ends in a partial sector")
        replacements = {}
        for path, contents in edits.items():
            original = self.files[path]
            if len(contents) == original.size:
                continue
            if block_count(len(contents)) <= block_count(original.size):
                replacements[path] = Extent(original.lba, len(contents))
            else:
                replacements[path] = Extent(next_lba, len(contents))
                next_lba += block_count(len(contents))
        return replacements, next_lba

    def write_replacement(self, path, data, extent):
        original = self.files[path]
        if len(data) != extent.size:
            raise ValueError(f"Unexpected replacement size for {path}")
        if extent.lba == original.lba:
            written = self.write_extent(extent, data)
        else:
            written = block_count(extent.size)
            if self.stream.seek(0, 2) != extent.lba * SECTOR_SIZE:
                raise ValueError(f"Replacement for {path} is out of order")
            for block in range(written):
                chunk = data[block * BLOCK_SIZE:(block + 1) * BLOCK_SIZE]
                self.stream.write(new_sector(extent.lba + block, chunk, block == written - 1))

        lba, offset = self.records[path]
        sector = bytearray(self.sector(lba))
        write_dual_word(sector, PAYLOAD_OFFSET + offset + 2, extent.lba)
        write_dual_word(sector, PAYLOAD_OFFSET + offset + 10, extent.size)
        self.write_sector(lba, sector)
        self.files[path] = extent
        return written

    def update_volume_size(self, blocks):
        if self.stream.seek(0, 2) != blocks * SECTOR_SIZE:
            raise ValueError("The track does not match the planned volume size")
        sector = bytearray(self.sector(16))
        write_dual_word(sector, PAYLOAD_OFFSET + 80, blocks)
        self.write_sector(16, sector)

    def write_sector(self, lba, sector):
        update_integrity(sector)
        self.stream.seek(lba * SECTOR_SIZE)
        self.stream.write(sector)
