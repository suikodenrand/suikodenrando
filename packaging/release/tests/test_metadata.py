"""Check distributed metadata/defaults without building or installing a package."""
import configparser
import hashlib
import importlib.util
import json
from pathlib import Path
import re
import unittest
import xml.etree.ElementTree as ET


HERE = Path(__file__).resolve().parents[1]
REPO = HERE.parents[1]
spec = importlib.util.spec_from_file_location('release_build', HERE / 'build.py')
release = importlib.util.module_from_spec(spec)
spec.loader.exec_module(release)


class PackageMetadataChecks(unittest.TestCase):
    def test_runtime_and_setup_versions_agree(self):
        project = ET.parse(REPO / 'src/SuikodenRandomizer.Unified/SuikodenRandomizer.Unified.csproj')
        self.assertEqual(release.VERSION, project.findtext('./PropertyGroup/Version'))
        plugin = (REPO / 'src/SuikodenRandomizer.Unified/UnifiedPlugin.cs').read_text(encoding='utf-8')
        self.assertEqual(release.VERSION, re.search(r'const string Version = "([^"]+)"', plugin).group(1))
        installer = (HERE / 'Install.ps1').read_text(encoding='utf-8')
        self.assertIn(f"$manifest.packageVersion -ne '{release.VERSION}'", installer)
        nsis = (HERE / 'StarsOfDestiny.Full.nsi').read_text(encoding='utf-8')
        self.assertIn(f'VIProductVersion "{release.VERSION}.0"', nsis)
        self.assertIn(f'"ProductVersion" "{release.VERSION}"', nsis)
        orchestrator = (REPO / 'packaging/Build-Release.ps1').read_text(encoding='utf-8')
        self.assertIn(f"'-p:Version={release.VERSION}'", orchestrator)

    def test_launcher_version_agrees_with_package(self):
        project = ET.parse(REPO / 'src/StarsOfDestiny.Launcher/StarsOfDestiny.Launcher.csproj')
        self.assertEqual(release.LAUNCHER_VERSION, project.findtext('./PropertyGroup/Version'))

    def test_new_inventory_defaults_and_independent_weapon_policies(self):
        for game, name, weapon in [
            ('s1', 'agilen.suikoden.recruitment_randomizer.cfg', 'TirVariance'),
            ('s2', 'agilen.suikoden2.recruitment_randomizer.cfg', 'RiouVariance'),
        ]:
            with self.subTest(game=game):
                cfg = configparser.ConfigParser(interpolation=None)
                cfg.read(HERE / 'defaults' / name, encoding='utf-8')
                self.assertEqual('MatchRole', cfg['Shuffle']['JoiningEquipment'])
                self.assertEqual(weapon, cfg['Shuffle']['JoiningWeaponLevel'])
                self.assertEqual(cfg['Shuffle']['JoiningEquipment'], release.FRESH_DEFAULTS[f'{game}JoiningEquipment'])
                self.assertEqual(cfg['Shuffle']['JoiningWeaponLevel'], release.FRESH_DEFAULTS[f'{game}JoiningWeaponLevel'])
                self.assertEqual('Shuffle', cfg['Shuffle']['Mode'])

    def test_experimental_components_stay_disabled_by_default(self):
        atb = configparser.ConfigParser(interpolation=None)
        atb.read(HERE / 'defaults/agilen.suikoden1.battle-preview.cfg', encoding='utf-8')
        self.assertFalse(atb.getboolean('Preview', 'Enabled'))
        self.assertFalse(release.FRESH_DEFAULTS['s1Atb'])
        self.assertFalse(release.FRESH_DEFAULTS['s1AtbInstalled'])
        s1 = configparser.ConfigParser(interpolation=None)
        s1.read(HERE / 'defaults/agilen.suikoden.recruitment_randomizer.cfg', encoding='utf-8')
        self.assertFalse(s1.getboolean('Treasure', 'Enabled'))
        self.assertFalse(s1.getboolean('Treasure', 'ShuffleChests'))
        self.assertFalse(release.FRESH_DEFAULTS['s1Treasure'])

    def test_manual_notice_manifest_matches_exact_distributed_bytes(self):
        base = HERE / 'licenses/manual'
        manifest = json.loads((base / 'source-manifest.json').read_text(encoding='utf-8'))
        self.assertEqual(release.VERSION, manifest['packageVersion'])
        for entry in manifest['files']:
            with self.subTest(file=entry['file']):
                content = (base / entry['file']).read_bytes()
                self.assertEqual(entry['sha256'], hashlib.sha256(content).hexdigest())
                self.assertEqual(entry['bytes'], len(content))

    def test_player_guides_explain_inventory_scope_and_legacy_saves(self):
        for kind in ('setup', 'manual'):
            with self.subTest(package=kind):
                text = (HERE / 'docs' / kind / 'Features and defaults.txt').read_text(encoding='utf-8')
                self.assertIn(f'STARS OF DESTINY {release.VERSION}', text)
                self.assertIn('JoiningEquipment=MatchRole', text)
                self.assertIn('consumables such as Medicine', text)
                self.assertIn('comparable equipment', text)
                self.assertIn('keep Donor behavior', text)
                self.assertIn('Native first-join', text)


if __name__ == '__main__':
    unittest.main()
