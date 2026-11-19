"""Package revisions keep distinct distribution files without changing the runtime version."""
import argparse
import importlib.util
import json
from pathlib import Path
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch
from zipfile import ZipFile


HERE = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('revision_release_build', HERE / 'build.py')
release = importlib.util.module_from_spec(spec)
spec.loader.exec_module(release)


class DistributionRevisionChecks(unittest.TestCase):
    def test_default_and_revised_names_are_distinct(self):
        base = SimpleNamespace(package_revision=None)
        revised = SimpleNamespace(package_revision='r2')
        for suffix in ('Setup.exe', 'Mod-Only-Windows.zip'):
            with self.subTest(suffix=suffix):
                self.assertEqual('Stars-of-Destiny-0.14.6-' + suffix, release.artifact_name(base, suffix))
                self.assertEqual('Stars-of-Destiny-0.14.6-r2-' + suffix, release.artifact_name(revised, suffix))
                self.assertNotEqual(release.artifact_name(base, suffix), release.artifact_name(revised, suffix))

    def test_revisions_reject_unsafe_or_ambiguous_values(self):
        for value in ('', 'r0', 'r02', 'R2', '2', '-r2', 'r2/other', '../r2', 'r2\\other', 'r2.exe', 'r2\n'):
            with self.subTest(value=value), self.assertRaises(argparse.ArgumentTypeError):
                release.package_revision(value)
        self.assertEqual('r2', release.package_revision('r2'))
        self.assertEqual('r123', release.package_revision('r123'))

    def test_declared_source_commits_are_full_and_normalized(self):
        with self.assertRaises(argparse.ArgumentTypeError):
            release.source_commit('c45431e')
        args = SimpleNamespace(package_revision='r2', runtime_source_commit='A' * 40,
                               launcher_source_commit='B' * 40)
        metadata = release.distribution_metadata(args)
        self.assertEqual({'runtime': 'a' * 40, 'launcher': 'b' * 40}, metadata['binarySourceCommits'])
        self.assertEqual('0.14.6-r2', metadata['distributionVersion'])

    def test_manual_archive_records_revision_and_keeps_runtime_version(self):
        with tempfile.TemporaryDirectory() as folder:
            base = Path(folder)
            atb = base / release.ATB
            atb.write_bytes(b'Fixture only; never loaded')
            args = SimpleNamespace(output=base / 'packages', package_revision='r2',
                runtime_source_commit='a' * 40, launcher_source_commit='b' * 40,
                atb_payload=atb.read_bytes(), reviewed_atb_sha256=release.sha(atb))
            args.output.mkdir()

            def fixture_common(stage, unused_args, *, setup):
                self.assertFalse(setup)
                stage.add('BepInEx/plugins/fixture.dll', b'Fixture only; never loaded')

            with patch.object(release, 'common', fixture_common):
                result = release.manual(args)
            self.assertEqual('Stars-of-Destiny-0.14.6-r2-Mod-Only-Windows.zip', result['name'])
            self.assertFalse((args.output / 'Stars-of-Destiny-0.14.6-Mod-Only-Windows.zip').exists())
            with ZipFile(args.output / result['name']) as archive:
                manifest = json.loads(archive.read('StarsOfDestiny Manual/payload-manifest.json'))
                verification = json.loads(archive.read('StarsOfDestiny Manual/Verification.json'))
                for document in (manifest, verification):
                    self.assertEqual('0.14.6', document['packageVersion'])
                    self.assertEqual('r2', document['packageRevision'])
                    self.assertEqual('0.14.6-r2', document['distributionVersion'])
                    self.assertEqual({'runtime': 'a' * 40, 'launcher': 'b' * 40}, document['binarySourceCommits'])
                self.assertEqual('1.0.7', manifest['launcherVersion'])
            sidecar = args.output / (result['name'] + '.sha256')
            self.assertEqual(result['sha256'] + '  ' + result['name'] + '\n', sidecar.read_text(encoding='ascii'))

    def test_setup_stage_records_revision_and_includes_build_information(self):
        with tempfile.TemporaryDirectory() as folder:
            base = Path(folder)
            with ZipFile(base / release.LOADER, 'w') as archive:
                archive.writestr('BepInEx/core/fixture.dll', b'Fixture only; never loaded')
            args = SimpleNamespace(output=base / 'packages', dependencies=base, package_revision='r2',
                runtime_source_commit='a' * 40, launcher_source_commit='b' * 40)
            args.output.mkdir()

            def fixture_common(stage, unused_args, *, setup):
                self.assertTrue(setup)
                stage.add('Game Files/BepInEx/plugins/fixture.dll', b'Fixture only; never loaded')

            with patch.object(release, 'common', fixture_common), patch.object(release, 'source_licenses', return_value={}), \
                    patch.object(release, 'compile_setup', return_value=None):
                result = release.setup(args)
            manifest = json.loads((Path(result['stage']) / 'payload-manifest.json').read_text(encoding='utf-8'))
            self.assertEqual('0.14.6', manifest['packageVersion'])
            self.assertEqual('r2', manifest['packageRevision'])
            self.assertEqual('0.14.6-r2', manifest['distributionVersion'])
            self.assertEqual('1.0.7', manifest['launcherVersion'])
            relative = 'Game Files/StarsOfDestiny Setup/Build information.txt'
            self.assertIn(relative, {entry['path'] for entry in manifest['files']})
            self.assertEqual((release.HERE / 'docs/setup/Build information.txt').read_bytes(),
                             (Path(result['stage']) / relative).read_bytes())


if __name__ == '__main__':
    unittest.main()
