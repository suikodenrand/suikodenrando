"""Exercise the shipped Windows restore command against disposable game folders."""
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest


RESTORE = Path(__file__).resolve().parents[1] / 'Restore.ps1'
POWERSHELL = shutil.which('powershell.exe')
PLUGIN = r'BepInEx\plugins\SuikodenRandomizer\SuikodenRandomizer.dll'


def digest(data):
    return None if data is None else hashlib.sha256(data).hexdigest()


@unittest.skipUnless(POWERSHELL, 'Windows PowerShell required for the shipped installer')
class RestoreTests(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        self.game = Path(temp.name)
        (self.game / 'Suikoden I and II HD Remaster.exe').write_bytes(b'fixture')
        (self.game / 'GameAssembly.dll').write_bytes(b'game')
        self.receipt = self.game / 'SRR-Backups/FullPreview-fixture'
        self.receipt.mkdir(parents=True)
        self.operations = []

    def operation(self, target, old, new, *, current, backup):
        self.operations.append(dict(target=target, originalHash=digest(old), newHash=digest(new)))
        for root, contents in ((self.game, current), (self.receipt / 'Original', backup)):
            if contents is not None:
                path = root / target
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(contents)

    def journal(self, status):
        (self.receipt / 'transaction.json').write_text(json.dumps(dict(
            schema='stars-of-destiny-full-install-v1', gamePath=str(self.game),
            gameAssemblySha256=digest(b'game'), status=status, operations=self.operations)), encoding='utf-8')

    def restore(self, *, success=True, check=False, receipt=None, tool=RESTORE):
        result = subprocess.run([POWERSHELL, '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', str(tool),
            '-GamePath', str(self.game), '-RestoreTransaction', str(receipt or self.receipt), '-NonInteractive',
            *(['-CheckOnly'] if check else [])], capture_output=True, text=True, timeout=60)
        self.assertEqual(result.returncode, 0 if success else 1, result.stdout + result.stderr)
        return result.stdout

    def test_interrupted_install_recovers_each_rename_state_and_can_be_retried(self):
        self.operation(PLUGIN, b'old', b'new', current=None, backup=b'old')
        untouched = r'StarsOfDestiny Setup\pending.txt'
        added = r'StarsOfDestiny Setup\added.txt'
        retired = r'StarsOfDestiny Setup\retired.txt'
        absent = r'StarsOfDestiny Setup\not-added-yet.txt'
        self.operation(untouched, b'prior', b'later', current=b'prior', backup=None)
        self.operation(added, None, b'added', current=b'added', backup=None)
        self.operation(retired, b'previous', None, current=None, backup=b'previous')
        self.operation(absent, None, b'new file', current=None, backup=None)
        self.journal('applying')
        self.restore(check=True)
        self.assertFalse((self.game / PLUGIN).exists())
        self.restore()
        self.assertEqual((self.game / PLUGIN).read_bytes(), b'old')
        self.assertEqual((self.game / untouched).read_bytes(), b'prior')
        self.assertEqual((self.game / retired).read_bytes(), b'previous')
        self.assertFalse((self.game / added).exists())
        self.assertFalse((self.game / absent).exists())
        self.restore()
        self.assertEqual(len(list(self.receipt.parent.iterdir())), 2)

    def test_changed_game_removes_new_files_and_preserves_incompatible_originals(self):
        added = r'StarsOfDestiny Setup\added.txt'
        replaced = r'StarsOfDestiny Setup\replaced.txt'
        self.operation(PLUGIN, b'old plugin', b'new plugin', current=b'new plugin', backup=b'old plugin')
        self.operation(added, None, b'added', current=b'added', backup=None)
        self.operation(replaced, b'prior', b'later', current=b'later', backup=b'prior')
        self.journal('complete')
        (self.game / 'GameAssembly.dll').write_bytes(b'steam update')
        self.restore()
        self.assertFalse((self.game / PLUGIN).exists())
        self.assertEqual((self.receipt / 'Original' / PLUGIN).read_bytes(), b'old plugin')
        self.assertFalse((self.game / added).exists())
        self.assertEqual((self.game / replaced).read_bytes(), b'prior')
        self.restore(check=True)
        undo, = (p for p in self.receipt.parent.iterdir() if p != self.receipt)
        self.restore(receipt=undo, tool=undo / 'Recover.ps1')
        self.assertFalse((self.game / PLUGIN).exists())
        self.assertEqual((self.game / 'GameAssembly.dll').read_bytes(), b'steam update')

    def test_conflicts_and_missing_backups_fail_before_writing(self):
        self.operation(PLUGIN, b'old', b'new', current=None, backup=None)
        self.journal('applying')
        self.assertIn('missing or changed in the backup', self.restore(success=False))
        self.assertFalse((self.game / PLUGIN).exists())
        path = self.game / PLUGIN
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(b'local edit')
        self.assertIn('file changed', self.restore(success=False))
        self.assertEqual(path.read_bytes(), b'local edit')
