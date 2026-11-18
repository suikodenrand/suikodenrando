"""Distribution-policy tests; no game binaries or runtime execution required."""
import importlib.util
import json
from pathlib import Path
import stat
import tempfile
import unittest
from zipfile import ZipFile, ZipInfo

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("split_nexus", ROOT / "split_nexus.py")
split = importlib.util.module_from_spec(spec)
spec.loader.exec_module(split)


def fixture():
    result = {name: ("unchanged:" + name).encode() for name in split.PLUGIN_PATHS}
    for name in split.CFG_NAMES:
        result["BepInEx/config/" + name] = (ROOT / "defaults" / name).read_bytes()
    for name in ("StarsOfDestiny.Launcher.exe", "createdump.exe", "coreclr.dll", *split.NOTICE_NAMES):
        result[split.LAUNCHER + name] = ("unchanged:" + name).encode()
    result[split.LAUNCHER + "StarsOfDestiny.Launcher.runtimeconfig.json"] = json.dumps({
        "runtimeOptions": {"includedFrameworks": [{"name": "Microsoft.NETCore.App", "version": "10.0.14"}]}
    }).encode()
    return result


class SplitDistributionTests(unittest.TestCase):
    def test_preserves_binary_bytes_and_atb_is_dormant(self):
        source = fixture()
        plugins = {n: d for n, d in source.items() if n.startswith("BepInEx/")}
        split.verify_payload(plugins, source, "plugins")
        self.assertIn("BepInEx/" + split.ATB_NAME, plugins)
        self.assertNotIn("BepInEx/plugins/" + split.ATB_NAME, plugins)
        altered = dict(plugins)
        altered[next(n for n in plugins if n.endswith("SuikodenRandomizer.Core.dll"))] = b"modified"
        with self.assertRaisesRegex(ValueError, "bytes or member set changed"):
            split.verify_payload(altered, source, "plugins")

    def test_rejects_active_atb_duplicate(self):
        source = fixture()
        plugins = {n: d for n, d in source.items() if n.startswith("BepInEx/")}
        plugins["BepInEx/plugins/" + split.ATB_NAME] = b"duplicate"
        with self.assertRaises(ValueError):
            split.verify_payload(plugins, source, "plugins")

    def test_rejects_executables_and_scripts_anywhere_in_plugins(self):
        source = fixture()
        for name in ("launcher.exe", "Instructions/install.cmd", "BepInEx/config/run.ps1"):
            plugins = {n: d for n, d in source.items() if n.startswith("BepInEx/")}
            plugins[name] = b"unexpected"
            with self.subTest(name=name), self.assertRaisesRegex(ValueError, "Executable or script"):
                split.verify_payload(plugins, source, "plugins")

    def test_rejects_loader_and_nested_archives(self):
        source = fixture()
        for name in ("BepInEx/core/loader.dll", "dotnet/runtime.dll", "winhttp.dll", "docs/source.zip"):
            plugins = {n: d for n, d in source.items() if n.startswith("BepInEx/")}
            plugins[name] = b"unexpected"
            with self.subTest(name=name), self.assertRaises(ValueError):
                split.verify_payload(plugins, source, "plugins")

    def test_detects_policy_change_even_when_source_config_matches_it(self):
        for filename, old, new in (
            ("agilen.suikoden.recruitment_randomizer.cfg", b"JoiningEquipment = MatchRole", b"JoiningEquipment = Donor"),
            ("agilen.suikoden2.recruitment_randomizer.cfg", b"JoiningWeaponLevel = RiouVariance", b"JoiningWeaponLevel = Donor"),
            ("agilen.suikoden.recruitment_randomizer.cfg", b"ShuffleChests = false", b"ShuffleChests = true"),
            ("agilen.suikoden1.battle-preview.cfg", b"Enabled = false", b"Enabled = true"),
        ):
            source = fixture()
            key = "BepInEx/config/" + filename
            self.assertIn(old, source[key])
            source[key] = source[key].replace(old, new)
            plugins = {n: d for n, d in source.items() if n.startswith("BepInEx/")}
            with self.subTest(filename=filename, old=old), self.assertRaises(ValueError):
                split.verify_payload(plugins, source, "plugins")

    def test_launcher_requires_complete_original_set_and_notices(self):
        source = fixture()
        launcher = {n: d for n, d in source.items() if n.startswith(split.LAUNCHER)}
        split.verify_payload(launcher, source, "launcher")
        launcher.pop(split.LAUNCHER + "coreclr.dll")
        with self.assertRaises(ValueError):
            split.verify_payload(launcher, source, "launcher")

    def test_zip_rejects_traversal_collision_symlinks_and_nested_archives(self):
        cases = (("../escape.txt",), ("File.txt", "file.TXT"), ("nested.zip",), ("linked.txt",))
        with tempfile.TemporaryDirectory() as folder:
            for i, names in enumerate(cases):
                path = Path(folder) / f"bad{i}.zip"
                with ZipFile(path, "w") as archive:
                    for name in names:
                        entry = ZipInfo(name)
                        if name == "linked.txt":
                            entry.create_system = 3
                            entry.external_attr = (stat.S_IFLNK | 0o777) << 16
                        archive.writestr(entry, b"invalid")
                with self.subTest(names=names), self.assertRaises(ValueError):
                    split.read_zip(path)

    def test_rejects_different_source_zip_before_splitting(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "unreviewed.zip"
            path.write_bytes(b"different package")
            with self.assertRaisesRegex(ValueError, "verified manual ZIP"):
                split.read_source(path, "a" * 64)

    def test_matching_external_hash_still_requires_current_manifest_and_correct_payload(self):
        source = fixture()
        records = [{"path": name, "bytes": len(data), "sha256": split.digest(data)} for name, data in source.items()]
        manifest = dict(schema="stars-of-destiny-mod-only-manual-v1", packageVersion=split.VERSION,
                        launcherVersion="1.0.6", optionalAtbDormant=True, bepInExFilesIncluded=False, files=records)
        source[split.PLUGIN_SUPPORT + "Verification.json"] = split.json_bytes(dict(status="passed",
            packageVersion=split.VERSION, reviewedAtbSha256=split.digest(source["BepInEx/" + split.ATB_NAME])))
        for name in ["FILES.txt", "SHA256SUMS.txt"]:
            source[split.PLUGIN_SUPPORT + name] = b"fixture"
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "manual.zip"
            def read():
                source[split.PLUGIN_SUPPORT + "payload-manifest.json"] = split.json_bytes(manifest)
                with ZipFile(path, "w") as archive:
                    for name, data in source.items():
                        archive.writestr(name, data)
                return split.read_source(path, split.digest(path.read_bytes()))
            self.assertEqual(source, read())
            manifest["packageVersion"] = "older"
            with self.assertRaisesRegex(ValueError, "current manual release"):
                read()
            manifest["packageVersion"] = split.VERSION
            records[0]["sha256"] = "0" * 64
            with self.assertRaisesRegex(ValueError, "manifest hash mismatch"):
                read()

    def test_reproducible_zip_and_complete_checksum_list(self):
        files = {"START HERE.txt": b"instructions", "BepInEx/plugins/example.dll": b"original bytes",
                 "BepInEx/" + split.ATB_NAME: b"reviewed dormant ATB"}
        split.add_metadata(files, "plugins", "a" * 64, "b" * 64)
        with tempfile.TemporaryDirectory() as folder:
            first, second = Path(folder) / "first.zip", Path(folder) / "second.zip"
            split.write_zip(first, files)
            split.write_zip(second, files)
            self.assertEqual(first.read_bytes(), second.read_bytes())
            final = split.read_zip(first)
            sums_name = split.PLUGIN_SUPPORT + "SHA256SUMS.txt"
            recorded = dict(line.split("  ", 1)[::-1] for line in final[sums_name].decode().splitlines())
            self.assertEqual(set(final) - {sums_name}, set(recorded))
            for name, checksum in recorded.items():
                self.assertEqual(split.digest(final[name]), checksum)
            listing = final[split.PLUGIN_SUPPORT + "FILES.txt"].decode().splitlines()[1:]
            self.assertEqual(sorted(final), listing)
            manifest = json.loads(final[split.PLUGIN_SUPPORT + "payload-manifest.json"])
            self.assertEqual("b" * 64, manifest["sourceManualZipSha256"])


if __name__ == "__main__":
    unittest.main()
