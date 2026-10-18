"""Compare complete raw frame traces by matched run, never by independent frames.

Python 3, standard library only. Manifest: JSON array with Id, Profile (N/S/D),
Block, WorldSHA256 and ModSHA256 ("absent" for N). Files: <Id>-frames.csv (ms,focused,gc0)
and <Id>-MelonLoader.log, beside the manifest. N means this mod absent, with the
same other mods. S is the conservative profile; D is the shipping defaults.
Use --minimal for N/M (absent/five-feature 1.0.0 defaults), with configuration hashes
and exact installed-module receipts. The report establishes no gameplay equivalence
and never silently drops bad runs.
"""

import argparse
import csv
import json
import math
from pathlib import Path
import random
import re
import statistics
from itertools import combinations
import hashlib


def quantile(values, p):
    ordered = sorted(values)
    position = (len(ordered) - 1) * p
    lo = math.floor(position)
    hi = math.ceil(position)
    return ordered[lo] + (ordered[hi] - ordered[lo]) * (position - lo)


def frame_stats(rows):
    if not rows:
        raise ValueError("Empty frame trace")
    ms = [float(row["ms"]) for row in rows]
    if any(not math.isfinite(value) or value <= 0 for value in ms):
        raise ValueError("Frame intervals must be positive finite numbers")
    if any(str(row["focused"]) not in ("0", "1") for row in rows):
        raise ValueError("Focus must be recorded as 0 or 1")
    gc = [int(row["gc0"]) for row in rows]
    if any(value < 0 for value in gc):
        raise ValueError("GC deltas cannot be negative")
    total = sum(ms)
    slowest = sorted(ms, reverse=True)[:max(1, math.ceil(len(ms) * .01))]
    return {
        "frames": len(ms), "seconds": total / 1000,
        "meanMs": statistics.mean(ms), "averageFps": 1000 * len(ms) / total,
        "p95Ms": quantile(ms, .95), "p99Ms": quantile(ms, .99),
        "onePercentLowFps": 1000 / statistics.mean(slowest), "maxMs": max(ms),
        "over25MsPerMinute": sum(value > 25 for value in ms) * 60000 / total,
        "over50MsPerMinute": sum(value > 50 for value in ms) * 60000 / total,
        "over100MsPerMinute": sum(value > 100 for value in ms) * 60000 / total,
        "focusedFrames": sum(str(row["focused"]) == "1" for row in rows),
        "gc0": sum(gc),
    }


def paired_effect(baseline, candidate):
    """Log ratios weight each matched block equally, regardless of frame count."""
    if baseline.keys() != candidate.keys():
        raise ValueError("Missing or unmatched blocks")
    ratios = [math.log(candidate[block] / baseline[block]) for block in sorted(baseline)]
    estimate = math.expm1(statistics.mean(ratios)) * 100
    interval = None
    # A small pilot cannot support a useful bootstrap interval. Never resample frames.
    if len(ratios) >= 6:
        rng = random.Random(1000)
        means = [statistics.mean(rng.choices(ratios, k=len(ratios))) for _ in range(10000)]
        interval = [math.expm1(quantile(means, p)) * 100 for p in (.025, .975)]
    return {
        "matchedRuns": len(ratios), "geometricMeanChangePercent": estimate,
        "perRunChangePercent": [math.expm1(value) * 100 for value in ratios],
        "exploratoryBootstrap95Percent": interval,
        "intervalNote": "Percentile bootstrap of matched log ratios, 10000 draws, seed 1000; assumes independent representative runs."
        if interval else "Not estimated: fewer than six matched runs; report the observed differences as a pilot.",
    }


def compare(manifest, minimal=False):
    records = json.loads(manifest.read_text(encoding="utf-8-sig"))
    if not isinstance(records, list) or not records:
        raise ValueError("Manifest must be a nonempty JSON array")
    runs, flags, seen, systems, populations = [], [], set(), set(), set()
    for record in records:
        run_id, profile, block = record["Id"], record["Profile"], record["Block"]
        if profile not in (("N", "M") if minimal else ("N", "S", "D")) or (profile, block) in seen:
            raise ValueError("Invalid profile or duplicate profile/block")
        if Path(run_id).name != run_id or "/" in run_id or "\\" in run_id:
            raise ValueError("Run IDs must be filenames")
        seen.add((profile, block))
        with (manifest.parent / (run_id + "-frames.csv")).open(newline="", encoding="utf-8-sig") as stream:
            stats = frame_stats(list(csv.DictReader(stream)))
        log = (manifest.parent / (run_id + "-MelonLoader.log")).read_text(encoding="utf-8-sig")
        if f"CLAIMS TRACE END {run_id};" not in log:
            flags.append(f"{run_id}: no completion receipt")
        population = re.search(r"CLAIMS world ready; ships=(\d+)", log)
        if not population or int(population.group(1)) <= 0:
            flags.append(f"{run_id}: loaded simulation workload not established")
        else:
            populations.add(int(population.group(1)))
        system = re.search(r"CLAIMS system=(.*)", log)
        if system:
            systems.add(system.group(1).strip())
        else:
            flags.append(f"{run_id}: system/graphics receipt unavailable")
        player = (manifest.parent / (run_id + "-Player.log")).read_text(encoding="utf-8-sig")
        if "SteamAPI_Init() failed" in player or "Steam is not initialized" in player:
            flags.append(f"{run_id}: Steam host failed; loading-screen timings are invalid")
        if re.search(r"\b\w+Exception:", player):
            flags.append(f"{run_id}: native exception; review Player.log")
        if "saveFileChanged=False;" not in log:
            flags.append(f"{run_id}: unchanged world save file not established")
        if stats["focusedFrames"] != stats["frames"]:
            flags.append(f"{run_id}: unfocused frames retained; comparison invalid")
        if not 44 <= stats["seconds"] <= 47:
            flags.append(f"{run_id}: duration outside the declared 45-second protocol")
        if "[ERROR]" in log or "[Error]" in log:
            flags.append(f"{run_id}: runtime errors; review the complete log")
        ready = re.search(r"Stellar Performance \S+ ready\.", log) is not None
        if ready != (profile != "N"):
            flags.append(f"{run_id}: mod presence does not match profile")
        if minimal:
            config = manifest.parent / (run_id + "-preferences.cfg")
            if hashlib.sha256(config.read_bytes()).hexdigest().upper() != record["ConfigSHA256"].upper():
                flags.append(f"{run_id}: configuration hash mismatch")
            if profile == "N" and record["ModSHA256"] != "absent":
                flags.append(f"{run_id}: absent profile has a DLL hash")
            if profile == "M":
                if "Stellar Performance 1.0.0 ready." not in log:
                    flags.append(f"{run_id}: candidate version receipt missing")
                installed = re.findall(r"\] ([a-z-]+) installed\.", log)
                expected = {"ship-grid-bounds", "fluid-cable-topology", "incremental-rooms", "single-player-rooms", "ship-part-batching"}
                if set(installed) != expected or len(installed) != 5:
                    flags.append(f"{run_id}: installed module set differs from the five-feature core")
        runs.append({**record, **stats})
    if len({record["WorldSHA256"] for record in records}) != 1:
        flags.append("Starting world hashes differ")
    if len(systems) > 1:
        flags.append("System/graphics receipts differ")
    if len(populations) > 1:
        flags.append("Loaded ship counts differ")
    if len({record["ModSHA256"] for record in records if record["Profile"] != "N"}) > 1:
        flags.append("Candidate DLL hashes differ")
    effects = {}
    for base, candidate in combinations(("N", "M") if minimal else ("N", "S", "D"), 2):
        for metric in ("meanMs", "p95Ms", "p99Ms", "averageFps", "onePercentLowFps"):
            a = {run["Block"]: run[metric] for run in runs if run["Profile"] == base}
            b = {run["Block"]: run[metric] for run in runs if run["Profile"] == candidate}
            if not a or not b or a.keys() != b.keys():
                flags.append(f"{base}/{candidate}: incomplete matched runs; no effect calculated")
                break
            effects[f"{base}/{candidate}/{metric}"] = paired_effect(a, b)
    return {
        "runs": runs, "effects": effects, "flags": sorted(set(flags)),
        "eligibleForPilotComparison": not flags,
        "notes": [
            "Negative meanMs/p99Ms changes favour the candidate; positive onePercentLowFps changes favour it.",
            "No traces or frames are silently excluded. Flags must be resolved before interpreting effects.",
            "N retains the other installed mods. Differences measure this mod's incremental effect on that stack.",
            "Matching starting hashes do not prove equivalent simulation activity, view, settings or hardware; inspect receipts and screenshots.",
            "Save-file timestamps detect writes; they do not detect aborted saves or CPU work before a write. Review native save logs too.",
            "Stationary pilot runs establish no travel, construction, multiplayer or gameplay-equivalence claim.",
        ],
    }


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("manifest", type=Path)
    parser.add_argument("--minimal", action="store_true", help="Validate the 1.0.0 N/M five-feature protocol")
    args = parser.parse_args()
    report = compare(args.manifest, minimal=args.minimal)
    print(json.dumps(report, indent=2, allow_nan=False))
    raise SystemExit(0 if report["eligibleForPilotComparison"] else 2)
