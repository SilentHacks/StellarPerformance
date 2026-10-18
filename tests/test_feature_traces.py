import importlib.util
import hashlib
import json
from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).parents[1] / 'scripts'))
spec = importlib.util.spec_from_file_location('features', Path(__file__).parents[1] / 'scripts/CompareFeatureTraces.py')
features = importlib.util.module_from_spec(spec)
spec.loader.exec_module(features)


class FeatureTraceTests(unittest.TestCase):
    def make_fixture(self, directory):
        records = []
        config = b'[StellarPerformance]\nPartSimulationDivisor = 1\n'
        for index, (run_id, feature) in enumerate([('B00', 'reference'), ('F01', 'picking'), ('B01', 'reference')]):
            records.append({'Id': run_id, 'Feature': feature, 'StartedUtc': f'2026-10-17T12:00:0{index}+00:00',
                            'WorldSHA256': 'same-world', 'ModSHA256': 'same-binary',
                            'ConfigSHA256': hashlib.sha256(config).hexdigest().upper()})
            (directory / (run_id + '-preferences.cfg')).write_bytes(config)
            (directory / (run_id + '-frames.csv')).write_text('ms,focused,gc0\n' + '10000,1,0\n' * 3)
            (directory / (run_id + '-MelonLoader.log')).write_text(
                f'FEATURE measurement={run_id}; feature={feature};\nCLAIMS system=test graphics\n'
                f'CLAIMS world ready; ships=20\nCLAIMS TRACE END {run_id}; saveFileChanged=False;\n')
            (directory / (run_id + '-Player.log')).write_text('Native completion\n')
        (directory / 'runs.json').write_text(json.dumps(records))

    def test_control_is_geometric_and_time_weighted(self):
        controls = [{'timestamp': 0, 'ms': 10}, {'timestamp': 10, 'ms': 40}]
        self.assertAlmostEqual(20, features.control_estimate(controls, 5, 'ms'))
        self.assertAlmostEqual(10 * 4 ** .2, features.control_estimate(controls, 2, 'ms'))

    def test_no_extrapolated_control_for_unbracketed_run(self):
        controls = [{'timestamp': 0, 'ms': 10}, {'timestamp': 10, 'ms': 20}]
        self.assertIsNone(features.control_estimate(controls, 11, 'ms'))
        self.assertIsNone(features.control_estimate(controls, -1, 'ms'))

    def test_exact_control_time_does_not_divide_by_zero(self):
        self.assertEqual(10, features.control_estimate([{'timestamp': 3, 'ms': 10}], 3, 'ms'))

    def test_complete_matching_receipts_are_accepted(self):
        with tempfile.TemporaryDirectory() as name:
            directory = Path(name)
            self.make_fixture(directory)
            report = features.analyse(directory)
            self.assertEqual([], report['flags'])
            self.assertEqual(1, report['features']['picking']['runs'])
            self.assertAlmostEqual(0, report['features']['picking']['medianChangePercent'])
            self.assertEqual(64, len(report['runs'][0]['traceSha256']))

    def test_configuration_tampering_invalidates_run(self):
        with tempfile.TemporaryDirectory() as name:
            directory = Path(name)
            self.make_fixture(directory)
            (directory / 'F01-preferences.cfg').write_text('wrong settings')
            report = features.analyse(directory)
            self.assertTrue(any('configuration receipt hash' in flag for flag in report['flags']))
            self.assertNotIn('picking', report['features'])

    def test_graphics_changes_are_not_silently_compared(self):
        with tempfile.TemporaryDirectory() as name:
            directory = Path(name)
            self.make_fixture(directory)
            path = directory / 'F01-MelonLoader.log'
            path.write_text(path.read_text().replace('test graphics', 'different graphics'))
            self.assertIn('System/graphics receipts differ', features.analyse(directory)['flags'])

    def test_wrong_feature_identity_and_unfocused_frames_are_rejected(self):
        with tempfile.TemporaryDirectory() as name:
            directory = Path(name)
            self.make_fixture(directory)
            path = directory / 'F01-MelonLoader.log'
            path.write_text(path.read_text().replace('feature=picking;', 'feature=reference;'))
            path = directory / 'F01-frames.csv'
            path.write_text(path.read_text().replace('10000,1,0', '10000,0,0', 1))
            report = features.analyse(directory)
            self.assertTrue(any('feature identity' in flag for flag in report['flags']))
            self.assertTrue(any('unfocused frames' in flag for flag in report['flags']))
            self.assertNotIn('picking', report['features'])


if __name__ == '__main__':
    unittest.main()
