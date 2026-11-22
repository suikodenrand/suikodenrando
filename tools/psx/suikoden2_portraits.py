"""Keep the PS1's portrait palette with each replacement picture."""

import struct


EXE_BASE = 0x8006B000
DECOMPRESS = 0x80075D04
PALETTE_LOOKUP = 0x80089F40
PALETTE_TABLE = 0x81A98


def palette_roles(executable, roles):
    table = struct.unpack_from("<26I", executable, PALETTE_TABLE)

    def palette(role):
        index, shift = divmod(role, 10)
        return (table[index] >> (3 * shift)) & 7

    # The original drawing function selects one of the shared palettes by role.
    # Use a nonzero native role for each palette, since menus use zero for an empty picture.
    return {role: next(candidate for candidate in range(1, 64) if palette(candidate) == palette(role))
            for role in roles}


def tag_mode(mode, palette_role):
    if mode not in (0, 1) or not 1 <= palette_role < 64:
        raise ValueError("Unexpected Suikoden II portrait compression or palette")
    return 0x80 | (palette_role << 1) | mode


def patch_portrait_rendering(executable):
    """Read the palette from the picture when a map or party package supplies one."""

    def offset(address):
        return address - EXE_BASE + 0x800

    def jump(address, *, link=False):
        return (0x0C000000 if link else 0x08000000) | ((address & 0x0FFFFFFF) >> 2)

    def branch(opcode, instruction, address, target):
        distance = (target - (address + (instruction + 1) * 4)) // 4
        if not -32768 <= distance < 32768:
            raise ValueError("Suikoden II portrait branch is out of range")
        return opcode | (distance & 0xFFFF)

    menu_decode = DECOMPRESS + 13 * 4
    field_palette = PALETTE_LOOKUP + 14 * 4
    patches = (
        (DECOMPRESS, (
            0x27BDFFE8, 0x00A03021, 0x24850005, 0xAFBF0010, 0x90830004, 0x8C840000,
            0x10600005, 0x24020001, 0x10620007, 0, 0x0801D753, 0,
            0x0C01D7D6, 0, 0x0801D753, 0, 0x0C01D778, 0, 0x8FBF0010, 0x27BD0018, 0x03E00008, 0,
        ), (
            # Tail-call the original codecs. A negative mode byte also carries a palette role.
            0x80830004, 0x00A03021, 0x8C870000, 0x04610002, 0x24850005, 0x30630001,
            branch(0x10600000, 6, DECOMPRESS, 0x80075F58), 0x00E02021, 0x24020001,
            branch(0x10620000, 9, DECOMPRESS, 0x80075DE0), 0, 0x03E00008, 0,
            # The menu keeps its palette role at sp + 0x18 + fp while loading the picture.
            0x80880004, 0x03BE4821, branch(0x05010000, 2, menu_decode, DECOMPRESS), 0x00084042,
            0x3108003F, 0xA1280018, jump(DECOMPRESS), 0, 0,
        )),
        (PALETTE_LOOKUP, (
            0x308400FF, 0x3C02CCCC, 0x3442CCCD, 0x00820019, 0x3C02800F, 0x2442C298,
            0x00003810, 0x000718C2, 0x306600FF, 0x00063080, 0x00C23021, 0x00031080,
            0x00431021, 0x00021040, 0x00822023, 0x308400FF, 0x00042840, 0x00A42821,
            0x24030007, 0x8CC20000, 0x00A31804, 0x00431024, 0x00A21006, 0x03E00008, 0x304200FF,
        ), (
            # Read the same packed table using the quotient and remainder of role / 10.
            0x308400FF, 0x2402000A, 0x0082001B, 0x3C08800F, 0x00001012, 0x00001810,
            0x00021080, 0x00481021, 0x8C42C298, 0x00034040, 0x00681821, 0x00621006,
            0x03E00008, 0x30420007,
            # Field dialogue holds the actual packed picture in s2. Untagged pictures keep their palette.
            0x82480004, 0, branch(0x05010000, 2, field_palette, PALETTE_LOOKUP), 0x00084042,
            jump(PALETTE_LOOKUP), 0x3104003F, 0, 0, 0, 0, 0,
        )),
        (0x80089BE4, (jump(DECOMPRESS, link=True), 0xAFA60038),
         (jump(menu_decode, link=True), 0xAFA60038)),
        (0x800A2804, (jump(PALETTE_LOOKUP, link=True), 0x02202021),
         (jump(field_palette, link=True), 0x02202021)),
    )
    for address, original, replacement in patches:
        at = offset(address)
        layout = f"<{len(original)}I"
        if struct.unpack_from(layout, executable, at) != original:
            raise ValueError("Unexpected Suikoden II portrait rendering code")
        struct.pack_into(layout, executable, at, *replacement)
