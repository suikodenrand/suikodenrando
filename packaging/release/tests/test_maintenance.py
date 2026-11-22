"""Exercise the Windows setup engine's repair and removal against a small verified payload."""
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest


HERE = Path(__file__).resolve().parents[1]
POWERSHELL = shutil.which('powershell.exe')
EXE = 'Suikoden I and II HD Remaster.exe'
PLUGIN = 'BepInEx/plugins/SuikodenRandomizer/SuikodenRandomizer.dll'
BATTLE = 'BepInEx/plugins/SuikodenRandomizer.S1BattlePreview.dll'
PARKED_BATTLE = 'BepInEx/SuikodenRandomizer.S1BattlePreview.dll'
GAME_CONFIG = 'BepInEx/config/agilen.starsofdestiny.cfg'
LOADER_CONFIG = 'BepInEx/config/BepInEx.cfg'
BINDING = 'BepInEx/config/SuikodenRandomizer/avatar-shuffle/game.json'
PREFERENCES = 'StarsOfDestiny Launcher/launcher-preferences.json'


def write(root, name, data):
    path = root / name
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(data)


def snapshot(root):
    return {p.relative_to(root).as_posix():p.read_bytes() for p in root.rglob('*') if p.is_file()}


@unittest.skipUnless(POWERSHELL, 'Windows PowerShell required for the shipped installer')
class SetupMaintenanceTests(unittest.TestCase):
    def setUp(self):
        parent = Path(tempfile.gettempdir()).resolve(strict=True)
        scratch = tempfile.TemporaryDirectory(dir=parent)
        root = Path(scratch.name).resolve(strict=True)
        self.assertEqual(root.parent,parent)
        self.addCleanup(scratch.cleanup)
        self.game = root/'Game [fixture]'
        self.stage = root/'Setup'
        self.stage.mkdir()
        write(self.game, EXE, b'game marker')
        write(self.game, 'GameAssembly.dll', b'game build')
        write(self.game, 'Save/Data1', b'native data')
        write(self.game, 'SuikodenFix/Save/Data1.json', b'save mirror')
        self.protected = snapshot(self.game)
        self.files = {name: b'placeholder' for name in (
            'winhttp.dll','.doorstop_version','doorstop_config.ini','changelog.txt','dotnet/coreclr.dll',
            'BepInEx/core/BepInEx.Unity.IL2CPP.dll','BepInEx/core/BepInEx.Core.dll',
            PLUGIN,BATTLE,'BepInEx/plugins/Suikoden_Fix.dll','StarsOfDestiny Launcher/StarsOfDestiny.Launcher.exe',
            'StarsOfDestiny Setup/Install.ps1','StarsOfDestiny Setup/Restore.ps1','StarsOfDestiny Setup/Restore.cmd',
            *[f'BepInEx/plugins/SuikodenRandomizer/SuikodenRandomizer.{name}.dll'
              for name in ('Core','Suikoden2.Core','Shared')],
            *[f'BepInEx/config/{p.name}' for p in (HERE/'defaults').glob('*.cfg')])}
        self.files['doorstop_config.ini'] = (b'[General]\nenabled = true\n'
            b'target_assembly = BepInEx\\core\\BepInEx.Unity.IL2CPP.dll\n'
            b'[Il2Cpp]\ncoreclr_path = dotnet\\coreclr.dll\ncorlib_dir = dotnet\n')
        records = []
        for name,data in self.files.items():
            path = f'Game Files/{name}'
            write(self.stage,path,data)
            records.append({'path':path,'sha256':hashlib.sha256(data).hexdigest()})
        (self.stage/'payload-manifest.json').write_text(json.dumps({
            'schema':'stars-full-package-v1','packageVersion':'0.14.6',
            'gameAssemblySha256':hashlib.sha256(b'game build').hexdigest(),'files':records}), encoding='utf-8')
        for name in ('Install.ps1', 'Maintenance.ps1'):
            shutil.copyfile(HERE/name,self.stage/name)
        self.run_setup('Install', '-IncludeAtb')

    def run_setup(self, mode, *flags, expect=0):
        result = subprocess.run([POWERSHELL,'-NoProfile','-ExecutionPolicy','Bypass','-File',
            str(self.stage/'Install.ps1'),'-GamePath',str(self.game),'-NonInteractive','-Mode',mode,*flags],
            capture_output=True,text=True,timeout=60)
        self.assertEqual(result.returncode,expect,result.stdout+result.stderr)
        return result.stdout

    def set_user_data(self):
        for name in (GAME_CONFIG,LOADER_CONFIG,BINDING,PREFERENCES):
            write(self.game,name,b'user data')
        write(self.game,'BepInEx/interop/GSD1.dll',b'generated file')
        write(self.game,'BepInEx/LogOutput.log',b'log file')

    def test_repair_restores_missing_and_damaged_files_with_existing_settings(self):
        self.set_user_data()
        (self.game/PLUGIN).unlink()
        (self.game/BATTLE).unlink()
        write(self.game,'BepInEx/core/BepInEx.Core.dll',b'damaged')
        write(self.game,'doorstop_config.ini',b'[General]\nenabled = false\n')
        self.run_setup('Repair')
        for name in (PLUGIN,BATTLE,'doorstop_config.ini','BepInEx/core/BepInEx.Core.dll'):
            self.assertEqual((self.game/name).read_bytes(),self.files[name])
        self.assertEqual((self.game/GAME_CONFIG).read_bytes(),b'user data')
        self.assertEqual((self.game/BINDING).read_bytes(),b'user data')
        self.assertEqual((self.game/PREFERENCES).read_bytes(),b'user data')

    def test_repair_keeps_a_dormant_battle_preview_dormant(self):
        (self.game/BATTLE).rename(self.game/PARKED_BATTLE)
        write(self.game,PARKED_BATTLE,b'damaged')
        self.run_setup('Repair')
        self.assertFalse((self.game/BATTLE).exists())
        self.assertEqual((self.game/PARKED_BATTLE).read_bytes(),self.files[BATTLE])

    def test_uninstall_preserves_settings_for_reinstall_by_default(self):
        self.set_user_data()
        receipt = snapshot(self.game/'SRR-Backups')
        before = snapshot(self.game)
        self.assertIn('Check complete',self.run_setup('Uninstall','-CheckOnly','-RemoveConfig'))
        self.assertEqual(snapshot(self.game),before)
        self.run_setup('Uninstall')
        for name in (GAME_CONFIG,BINDING,PREFERENCES,LOADER_CONFIG):
            self.assertEqual((self.game/name).read_bytes(),b'user data')
        for name in (PLUGIN,BATTLE,'winhttp.dll','dotnet','BepInEx/interop','StarsOfDestiny Setup'):
            self.assertFalse((self.game/name).exists(),name)
        self.assertEqual(snapshot(self.game/'SRR-Backups'),receipt)
        self.run_setup('Install')
        self.assertEqual((self.game/GAME_CONFIG).read_bytes(),b'user data')

    def test_uninstall_can_delete_mod_configuration_after_game_update(self):
        self.set_user_data()
        write(self.game,'GameAssembly.dll',b'updated game')
        write(self.game,'changelog.txt',b'unrelated notes')
        write(self.game,PARKED_BATTLE,b'dormant preview')
        self.run_setup('Uninstall','-RemoveConfig')
        self.assertEqual({name:data for name,data in snapshot(self.game).items()
            if not name.startswith('SRR-Backups/')}, {**self.protected,
            'GameAssembly.dll':b'updated game','changelog.txt':b'unrelated notes'})

    def test_shared_loader_and_third_party_config_are_preserved(self):
        self.set_user_data()
        write(self.game,'BepInEx/plugins/ThirdParty.dll',b'other mod')
        write(self.game,'BepInEx/config/ThirdParty.cfg',b'other settings')
        self.assertIn('shared',self.run_setup('Uninstall','-RemoveConfig'))
        for name in (PLUGIN,BATTLE,GAME_CONFIG,BINDING,PREFERENCES):
            self.assertFalse((self.game/name).exists(),name)
        for name in ('winhttp.dll',LOADER_CONFIG,'BepInEx/plugins/ThirdParty.dll',
            'BepInEx/config/ThirdParty.cfg','BepInEx/interop/GSD1.dll'):
            self.assertTrue((self.game/name).exists(),name)


if __name__ == '__main__':
    unittest.main()
