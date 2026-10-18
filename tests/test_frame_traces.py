"""Run with python -m unittest discover -s tests -p test_frame_traces.py."""
import importlib.util
import math
import json
import hashlib
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location("traces", Path(__file__).parents[1] / "scripts" / "CompareFrameTraces.py")
traces = importlib.util.module_from_spec(spec)
spec.loader.exec_module(traces)


class FrameTraceTests(unittest.TestCase):
    def test_minimal_protocol_checks_five_modules_and_configuration_receipts(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            records = []
            modules = ["ship-grid-bounds", "fluid-cable-topology", "incremental-rooms", "single-player-rooms", "ship-part-batching"]
            for profile in "NM":
                run = profile + "1"
                config = b"[StellarPerformance]\n"
                records.append({"Id": run, "Profile": profile, "Block": 1, "WorldSHA256": "same", "ModSHA256": "absent" if profile == "N" else "candidate", "ConfigSHA256": hashlib.sha256(config).hexdigest()})
                (root / (run + "-preferences.cfg")).write_bytes(config)
                (root / (run + "-frames.csv")).write_text("ms,focused,gc0\n" + "10,1,0\n" * 4500)
                ready = "Stellar Performance 1.0.0 ready.\n" + "".join(f"[Stellar_Performance] {m} installed.\n" for m in modules) if profile == "M" else ""
                (root / (run + "-MelonLoader.log")).write_text(ready + "CLAIMS system=same\nCLAIMS world ready; ships=20\n" + f"CLAIMS TRACE END {run}; saveFileChanged=False;\n")
                (root / (run + "-Player.log")).write_text("")
            manifest = root / "runs.json"
            manifest.write_text(json.dumps(records))
            report = traces.compare(manifest, minimal=True)
            self.assertTrue(report["eligibleForPilotComparison"], report["flags"])
            self.assertEqual(5, len(report["effects"]))
            log = root / "M1-MelonLoader.log"
            log.write_text(log.read_text().replace("ship-part-batching installed.", "ship-part-batching skipped."))
            (root / "N1-preferences.cfg").write_text("changed")
            report = traces.compare(manifest, minimal=True)
            self.assertFalse(report["eligibleForPilotComparison"])
            self.assertTrue(any("module set" in flag for flag in report["flags"]))
            self.assertTrue(any("hash mismatch" in flag for flag in report["flags"]))

    def test_fps_uses_elapsed_time_and_slowest_percent_is_explicit(self):
        result = traces.frame_stats([{"ms": x, "focused": "1", "gc0": "0"} for x in [10] * 99 + [100]])
        self.assertAlmostEqual(100000 / 1090, result["averageFps"])
        self.assertEqual(10, result["onePercentLowFps"])
        self.assertAlmostEqual(10.9, result["p99Ms"])

    def test_invalid_samples_are_rejected_instead_of_removed(self):
        for value in [0, -1, math.nan, math.inf]:
            with self.assertRaises(ValueError):
                traces.frame_stats([{"ms": value, "focused": "1", "gc0": "0"}])

    def test_unfocused_frames_remain_in_statistics(self):
        result = traces.frame_stats([{"ms": 100, "focused": "0", "gc0": "1"}, {"ms": 10, "focused": "1", "gc0": "0"}])
        self.assertEqual(2, result["frames"])
        self.assertEqual(55, result["meanMs"])
        self.assertEqual(1, result["focusedFrames"])

    def test_paired_effect_uses_run_ratios_and_small_pilot_has_no_interval(self):
        result = traces.paired_effect({1: 10, 2: 20, 3: 30}, {1: 5, 2: 10, 3: 15})
        self.assertAlmostEqual(-50, result["geometricMeanChangePercent"])
        self.assertIsNone(result["exploratoryBootstrap95Percent"])

    def test_missing_pairs_are_not_silently_dropped(self):
        with self.assertRaises(ValueError):
            traces.paired_effect({1: 10, 2: 20}, {1: 10})

    def test_bootstrap_is_reproducible_and_samples_runs(self):
        baseline = {i: 10 for i in range(6)}
        candidate = {i: 5 for i in range(6)}
        result = traces.paired_effect(baseline, candidate)
        self.assertEqual(result, traces.paired_effect(baseline, candidate))
        self.assertEqual([-50, -50], result["exploratoryBootstrap95Percent"])

    def test_complete_pilot_checks_readiness_and_retains_bad_runs(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            records = []
            for profile in "NSD":
                run = profile + "1"
                records.append({"Id": run, "Profile": profile, "Block": 1, "WorldSHA256": "same", "ModSHA256": "absent" if profile == "N" else "same-mod"})
                (root / (run + "-frames.csv")).write_text("ms,focused,gc0\n" + "10,1,0\n" * 4500)
                ready = "Stellar Performance 0.9.0 ready.\n" if profile != "N" else ""
                (root / (run + "-MelonLoader.log")).write_text(ready + "CLAIMS system=identical\nCLAIMS world ready; ships=20\n" + f"CLAIMS TRACE END {run}; frames=4500; saveFileChanged=False; focusedFrames=4500\n")
                (root / (run + "-Player.log")).write_text("")
            manifest = root / "runs.json"
            manifest.write_text(json.dumps(records))
            self.assertTrue(traces.compare(manifest)["eligibleForPilotComparison"])
            # A completed, focused loading-screen trace must never become evidence.
            log = root / "N1-MelonLoader.log"
            log.write_text(log.read_text().replace("CLAIMS world ready; ships=20", ""))
            (root / "N1-Player.log").write_text("SteamAPI_Init() failed")
            report = traces.compare(manifest)
            self.assertFalse(report["eligibleForPilotComparison"])
            self.assertEqual(3, len(report["runs"]))
            self.assertTrue(any("loading-screen" in flag for flag in report["flags"]))


if __name__ == "__main__":
    unittest.main()
