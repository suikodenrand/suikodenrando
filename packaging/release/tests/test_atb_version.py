"""Validate public-package preflight with small in-memory PE version resources."""
import importlib.util
import json
from pathlib import Path
import subprocess
import struct
import sys
import tempfile
import unittest
from unittest.mock import patch


spec = importlib.util.spec_from_file_location('atb_release_build', Path(__file__).resolve().parents[1] / 'build.py')
release = importlib.util.module_from_spec(spec)
spec.loader.exec_module(release)


def battle_dll(major, minor, build, revision, *, pe32_plus=False):
    """PE DLL with one .rsrc section and a neutral RT_VERSION/1 resource."""
    image = bytearray(1024)
    image[:2] = b'MZ'
    struct.pack_into('<I', image, 0x3c, 0x80)
    optional_size = 240 if pe32_plus else 224
    machine = 0x8664 if pe32_plus else 0x14c
    struct.pack_into('<4sHHIIIHH', image, 0x80, b'PE\0\0', machine, 1, 0, 0, 0, optional_size, 0x2002)
    optional = 0x98
    directories = 112 if pe32_plus else 96
    struct.pack_into('<H', image, optional, 0x20b if pe32_plus else 0x10b)
    struct.pack_into('<I', image, optional + directories - 4, 16)
    resource_rva = 0x2000
    raw_offset = 0x200
    struct.pack_into('<II', image, optional + directories + 16, resource_rva, 256)
    struct.pack_into('<8sIIII', image, optional + optional_size, b'.rsrc', 256, resource_rva, 512, raw_offset)

    for offset, identifier, child in [(0, 16, 0x80000018), (24, 1, 0x80000030), (48, 0, 72)]:
        struct.pack_into('<HHII', image, raw_offset + offset + 12, 0, 1, identifier, child)
    info = struct.pack('<HHH', 92, 52, 0) + 'VS_VERSION_INFO\0'.encode('utf-16-le') + bytes(2)
    info += struct.pack('<13I', 0xfeef04bd, 0x10000, major << 16 | minor, build << 16 | revision, *([0] * 9))
    struct.pack_into('<IIII', image, raw_offset + 72, resource_rva + 88, len(info), 0, 0)
    image[raw_offset + 88:raw_offset + 88 + len(info)] = info
    return bytes(image)


class PublicBattlePreviewTests(unittest.TestCase):
    def test_manual_and_setup_reject_a_launcher_missing_its_executable(self):
        with tempfile.TemporaryDirectory() as folder:
            base = Path(folder)
            launcher = base / 'launcher'
            launcher.mkdir()
            (launcher / 'StarsOfDestiny.Launcher.runtimeconfig.json').write_text(
                '{"runtimeOptions":{"includedFrameworks":[]}}', encoding='utf-8')
            for name in ('coreclr.dll', 'dotnet10-Core-LICENSE.txt', 'dotnet10-Core-THIRD-PARTY-NOTICES.txt',
                         'dotnet10-WindowsDesktop-LICENSE.txt'):
                (launcher / name).write_bytes(b'fixture')
            for name in release.RUNTIME_NAMES:
                (base / name).write_bytes(b'fixture')
            atb = base / release.ATB
            atb.write_bytes(battle_dll(0, 15, 2, 0))
            for mode in ('manual', 'all'):
                with self.subTest(mode=mode):
                    output = base / mode
                    result = subprocess.run([sys.executable, str(release.HERE / 'build.py'), '--mode', mode,
                        '--runtime-dir', str(base), '--launcher-dir', str(launcher), '--atb-dll', str(atb),
                        '--reviewed-atb-sha256', release.sha(atb), '--output', str(output),
                        '--dependencies', str(base / 'dependencies')], capture_output=True, text=True)
                    self.assertNotEqual(result.returncode, 0)
                    self.assertIn('StarsOfDestiny.Launcher.exe', result.stderr)
                    self.assertIn('Incomplete launcher', result.stderr)
                    self.assertFalse(output.exists())

    def test_custom_reviewed_public_builds_are_accepted(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / release.ATB
            for pe32_plus in (False, True):
                with self.subTest(pe32_plus=pe32_plus):
                    data = battle_dll(0, 15, 2, 0, pe32_plus=pe32_plus)
                    path.write_bytes(data)
                    self.assertEqual(data, release.public_atb_payload(path, release.digest(data)))
                    with self.assertRaisesRegex(ValueError, 'differs from the hash'):
                        release.public_atb_payload(path, '0' * 64)

    def test_missing_version_resource_is_rejected_even_with_a_matching_hash(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / release.ATB
            path.write_bytes(b'0.15.2.0')
            with self.assertRaisesRegex(ValueError, 'PE file version'):
                release.public_atb_payload(path, release.sha(path))

    def test_both_public_package_modes_reject_a_reviewed_incompatible_dll_before_staging(self):
        with tempfile.TemporaryDirectory() as folder:
            base = Path(folder)
            path = base / release.ATB
            path.write_bytes(battle_dll(9, 8, 7, 6))
            for name in (*release.RUNTIME_NAMES, 'coreclr.dll', 'dotnet10-Core-LICENSE.txt',
                         'dotnet10-Core-THIRD-PARTY-NOTICES.txt', 'dotnet10-WindowsDesktop-LICENSE.txt'):
                (base / name).write_bytes(b'fixture')
            (base / 'StarsOfDestiny.Launcher.runtimeconfig.json').write_text(json.dumps(
                {'runtimeOptions': {'includedFrameworks': []}}), encoding='utf-8')
            for mode in ('manual', 'all'):
                output = base / mode
                args = ['build.py', '--mode', mode, '--runtime-dir', str(base), '--launcher-dir', str(base),
                        '--atb-dll', str(path), '--reviewed-atb-sha256', release.sha(path),
                        '--dependencies', str(base / 'dependencies'), '--output', str(output), '--stage-only']
                with self.subTest(mode=mode), patch.object(sys, 'argv', args):
                    with self.assertRaisesRegex(ValueError, r'0\.15\.2\.0.*9\.8\.7\.6'):
                        release.main()
                    self.assertFalse(output.exists())
                    self.assertFalse((base / 'dependencies').exists())
