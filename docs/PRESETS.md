# Live presets (2.0.0)

Press **F8**, or choose **Performance** in a menu, to open the selector. Choose a
level and Apply. All settings stay visible; editing one creates a Custom selection.
Keys 1–4 select a named level, 5 keeps the displayed settings as Custom, Enter
applies, and Escape or F8 closes. Numeric fields accept direct entry as well as
slider adjustment. Hover a setting for its trade-off. The panel uses
the game's local pause controls and shields the menu behind it from pointer input.
Opening it does not pause a multiplayer server. Choices are saved through
MelonLoader preferences. No preset change requires a game restart, world reload
or reconnect.

Balanced retains the current development defaults, including canvas culling,
batch recovery and expanded replication deduplication. Existing individual core
feature/compatibility overrides remain respected; presets change the room policy
and the presentation settings below. SignalReduction, PowerTransferScan,
SpatialClustering, EmptyWarpUpdates and SaveSerialization default on for new or
missing preferences in every tier, including Faithful and Custom. Existing
stored `false` values and compatibility fallbacks stay respected. These startup
switches require a restart and are not reset by the live selector. Faithful keeps
the selected core optimisations while using native room maths and presentation.

| Setting | Faithful | Balanced (default) | Performance | Aggressive |
| --- | --- | --- | --- | --- |
| Room reconstruction | Native | Incremental | Incremental | Incremental |
| Main-view scene contours | Native | Native | Silhouettes | Off |
| Main-light shadows | Native | Native | 150 m / 2 cascades / 2048 | 100 m / 2 cascades / 2048 |
| Visual vegetation search range | Native | Native | 75% | 50% |
| Thruster-flame emission | Native | Native | Native | 50% |
| Cable glow/flow targets | Native | Native | 30 Hz | 15 Hz |
| Terrain detail reuse while nearly still | Native | Native | Up to 6 frames | Up to 15 frames |
| Readouts, small labels, part-label distance | Native | Native | Native | Native |

Performance and Aggressive never hide whole categories of scenery. Shadows stay
soft and enabled, every label and readout stays visible, and the full landscape,
sky and clouds are drawn. Performance keeps a 33 ms cable-target interval and
Aggressive about 67 ms. Numeric readouts stay native in both. Workload-specific
measurements and historical results are recorded below.

Custom retains the displayed policy when you begin editing. Controls cover room
reconstruction, contour mode, shadow filtering/distance/cascades/resolution,
vegetation range (10–100%), exhaust density (10–100%), readout refresh (native or
1–60 Hz), cable effects (native or 5–60 Hz), terrain detail reuse (native or 2–30
frames), small-label omission (off or 1–32 pixels) and inspected part-label
distance (native or 4–2000 m). Apply is enabled only for changed, valid settings.
Closing discards unapplied edits. Presets do not change factory/circuit/fluid/
physics stepping, native picking, authoritative part assignments/callbacks or
genuine network updates. Terrain meshes, structural geometry, colliders and solver
behaviour remain native.

## How each setting works

**Scene contours.** The game's main-view contour effect normally samples depth and
normals, which makes the renderer draw the whole scene an extra time first.
Silhouettes keep object outlines from depth alone and skip that extra pass (about
half the frame's triangles in the reference view). Off removes the contour effect.
Hover, selection, build-tool and tutorial outlines and the ID/picking path stay native.

The game's atmosphere and cloud effects read the depth texture without declaring
that they need it, so they depended on the contour pass to produce depth early.
An earlier development build turned contours off without accounting for this: sky
and clouds were painted over terrain, trees and anything above the horizon. Both
reduced modes now declare the depth dependency, so the renderer copies depth
before those effects run. The declaration is removed when contours return to
native. Unknown renderer builds keep native contours.

**Cable effects.** Cable glow and fluid-flow targets are recomputed at the chosen
rate. Glow and scrolling flow animate every frame towards the latest targets, and a
cable change refreshes immediately. Circuits, fluids and the server's signal values
are unaffected; only the client's cable appearance can lag by up to one interval.

**Terrain detail reuse.** The visual terrain-detail, vegetation, resource-hotspot and
ambient-sound searches depend only on where you are on a planet. While the camera
has moved less than 0.25 m (or 0.2% of altitude), the previous search is reused for
up to the chosen number of frames. Any meaningful movement or a settings change
refreshes it. Server-side terrain, collisions and resource placement are separate
and unchanged.

**Vegetation range** changes only the exact inspected visual observer, retaining a
20 m floor where the native range permits. Native collision/audio observers,
resource placement and terrain data are unchanged. The percentage controls the
search range, not a uniform percentage of trees. Balanced and Faithful return the
original floating-point values directly.

**Shadows** are capped rather than increased when the game already uses lower
values. Leaving the override restores the captured native asset settings. Exhaust
changes only the native emission rate from `ThrusterFlameVFX.SetIntensity`;
intensity, thrust/fuel, particle speed/size and audio are unchanged. Existing
particles are not deleted.

**Readouts and labels** (Custom only). The readout cap intercepts only the
inspected signal-display and gas-meter text formatting methods; the latest value
is published at the next refresh, on focus, and when leaving the cap. Small-label
omission and the part-label distance apply only to passive canvases already
inspected by canvas culling and retain focused controls. These did not show a
measurable benefit and are not used by a named level.

## Transition and multiplayer rules

Compatible hooks install once. The selector queues an immutable policy for the
main thread; room switching waits until no room edit/mutation scope is active.
An in-flight update finishes under its original policy. Both room paths continue
using the native structures and existing gas state. Choosing Faithful changes
future arithmetic and does not reverse earlier rounding differences. A visual
policy change flushes pending readouts, restores owned graphics overrides and
applies the new settings. No world reconstruction is forced just to change level.

Presets are local preferences, with no preset-name handshake or new gameplay
packet format. The authoritative host controls world calculations. A client's
Faithful choice cannot override a host's incremental-room policy. Local visual
limits do not cap server updates. A host-only installation can reduce redundant
outbound notifications for native clients; a client-only installation supplies
local improvements without changing the host. Matching levels are unnecessary.
Stellar Mod Manager can still display its general mod-list mismatch warning for
a one-sided installation; the tested connections and native updates succeeded.
Single-player room forwarding restores native client mirrors when peers join.

Compatibility guards can leave a feature unavailable; selecting a preset does
not override them. The panel reports unavailable room/visual modules and the
loader log records the reason. Other mods changing the same text methods, later
graphics/material/layout changes or observer callbacks remain compatibility
limits. Existing diagnostic captures and experimental reliable retry stay outside
the preset ladder and retain their existing defaults.

## Validation

### Shared CPU baseline and tier check (23 October 2026)

The five new guarded CPU features now default on for missing/new preferences in
every tier. Existing opt-outs are preserved. This changes startup policy, not
the CPU algorithms validated in [CPU research](CPU_RESEARCH.md). Faithful still
uses native room maths; Balanced and the higher levels retain the documented
incremental-room rounding tradeoff. Higher levels never weaken CPU guards or
reduce simulation stepping.

Before the stronger Aggressive retune, a fresh graphics-enabled process measured
the shared CPU build with those five
switches requested and the installed mod stack. The native-save module correctly
skipped its conflicting replacement writer. The sequence was Balanced,
Performance, Aggressive, Aggressive, Performance, Balanced, with 35 focused
seconds of initial warm-up, a screenshot and five seconds settling per change,
then 30 seconds of whole-frame intervals per phase. No profiler or comparison
observer ran during the captures.

| Level | Observations (FPS) | Mean FPS | Relative to Balanced |
| --- | --- | ---: | ---: |
| Balanced | 65.30, 65.90 | 65.60 | Reference |
| Performance | 73.60, 76.12 | 74.86 | +14.1% |
| Aggressive | 77.09, 75.75 | 76.42 | +16.5% |

These are two observations per level in one ordinary 37-ship view, not universal
gains or a comparison with an unmodified game. Aggressive's additional 2.1% over
Performance is inside the practical noise band. Actual native physics endpoints
advanced at approximately 50 Hz throughout. All six immediate transition
fingerprints matched, captures retained focus and wrote no saves, and all 157
original installation/configuration/save files remained hash-identical. Reviewed
screenshots retained the distant forest, island, wall tops and correct sky depth.
The updated source passed 113 mod tests and 15 trace-analysis tests; this does
not rerun the earlier multiplayer/building acceptance.

### Stronger Aggressive refresh limits (23 October 2026)

The first private screen retained every other Aggressive setting and alternated
15 Hz / 15 frames, 5 Hz / 30 frames, 5 Hz / 30 frames, 15 Hz / 15 frames. The
previous limits measured 80.06 / 79.41 FPS; the stronger limits measured
84.84 / 84.08 FPS: **79.74 → 84.46 FPS mean (+5.9%)**. All four windows retained
focus, native 50 Hz stepping and unchanged save files. This motivated a candidate
Aggressive mapping. The independent fresh-process follow-up did not reproduce
the gain outside the noise band, so the named setting remains 15 Hz / 15 frames.

The fresh-process confirmation used Balanced, Performance, previous Aggressive,
candidate Aggressive, candidate Aggressive, previous Aggressive, Performance,
Balanced, with the same 35-second focused warm-up and 30-second captures:

| Policy | Observations (FPS) | Mean FPS |
| --- | --- | ---: |
| Balanced | 68.23, 67.16 | 67.69 |
| Performance (30 Hz / 6 frames) | 76.87, 76.27 | 76.57 |
| Retained Aggressive (15 Hz / 15 frames) | 78.53, 80.25 | 79.39 |
| Rejected named candidate (5 Hz / 30 frames) | 80.50, 80.72 | 80.61 |

The stronger candidate added only **1.5%** over the retained Aggressive policy in
this independent run, inside the noise band. Its earlier +5.9% therefore does not
establish a reliable gain. Performance and retained Aggressive were +13.1% and
+17.3% above Balanced here. Aggressive's extra benefit over Performance varied
from 2.1% in the first process to 3.7% here; do not promise that small difference.
All eight captures kept focus and native actual physics near 50 Hz, immediate
transition checks passed, and all original files remained unchanged. The temporary
candidate DLL's identity stays attached to this run; the final source restores
the 15 Hz / 15-frame mapping.

The stronger candidate remains available through Custom: cable glow/fluid-flow
animation stays live, but targets can lag by up to 200 ms and nearly still terrain
searches can reuse up to 30 frames. Performance remains at 30 Hz / 6 frames,
Aggressive at 15 Hz / 15 frames, and Balanced/Faithful native. No simulation or
numeric-readout cadence changes were considered for the higher tiers.

Two additional private CPU experiments were screened. Empty typed-part dispatch
preserved 296 emitted cases and 296 actual installed-wrapper cases, including
ordered callbacks and exception outcomes. Its isolated 1,024-empty-ship update
improved from about 2.36 to 1.26 ms, but whole-game stress FPS only improved from
11.74 to 12.19 (+3.8%, paired observations +4.8% / +2.8%). It remains private
pending stronger repeatable evidence. This measured path does not change room,
mass, gyro preparation or simulation cadence.

Skipping material writes whose input equalled the earlier readback was rejected.
The native setter/readback round trip changed finite `_Color` values by one raw
float bit; even finite-only write elision failed the native comparison. This was
a private presentation fixture, not a shipping simulation change. No FPS gain is
claimed for it. The offscreen-cable survey (82 of 97 hidden, no shadow/realtime-GI
effects) and client endpoint plans remain research leads, not enabled features.

A metadata comparison also found identical disassembled instructions in all 182
method bodies across the five CPU features and signal-plan helper versus the
previously validated CPU build. Their algorithms were unchanged by promotion.

Run identities, raw-window summaries, native checks and preservation receipts are
in [tier-frontier-evidence.json](tier-frontier-evidence.json). Private drivers,
failed checks and detailed logs remain outside this repository.

The historical multiplayer, switching and selector checks below were performed
on earlier builds and were not rerun for the 22 October redesign or 23 October
CPU/tier update. Those later changes were checked with offline tests and the
single-player sessions documented here.

The offline suite passes 83 cases, including the redesigned levels, depth declaration, native URP depth-copy scheduling, cosmetic-cap timing and terrain-reuse transpiler. The scheduling regressions cover every affected atmosphere/cloud pass and repeated restoration to native inputs without requiring a normals prepass. Coverage freezes Balanced's room and
presentation defaults, bounds custom values and alternates native/optimised room
edits on the same trees while comparing geometry, volume, gas and saved seeds.
It also emits the inspected vegetation observer body, checks preserved native
instructions and verifies bit-preserving native detail settings and unaffected
nonvisual observer ranges. Offline tests never install CoreCLR runtime detours.

The retuned shipping implementation passed 40 additional native live switches,
with identical immediate authoritative fingerprints and all 6,981 collider
identities retained. Actual shadow distance/cascades/resolution/filtering restored
on return to Balanced. Of 70 visible eligible labels in the checked view,
Aggressive hid 63; selecting a hidden control restored it, and Balanced restored
the original visible labels. Both new exhaust multipliers and native restoration
passed while particle speed and size remained unchanged. These checks establish
the compared transitions, not a long-duration equivalence proof.

The expanded 1280×720 panel was reviewed in native screenshots. Real keyboard
input applied Performance, edited Custom to 18 m labels / 60% vegetation, applied
and saved it, closed/reopened with the same values, applied Aggressive, restored
Balanced and closed with local pause cleared. Numeric focus was placed by the
private game-side recorder. Invalid label/vegetation entries were also checked;
no OS mouse acceptance is claimed. The first recorder run exited after a status
file-sharing race; the corrected recorder completed the rerun without changing
the shipping DLL.

The final retuned build also passed a nine-phase graphics-enabled loopback peer
session with 12 saved-world part types and two successive clients. This included
Performance-host/Aggressive-client and Aggressive-host/Performance-client,
duplicate suppression, genuine changes, pulse/return updates, hull samples,
disconnect/rejoin and open selector panels while network ticks advanced. The
earlier all-16-combinations and one-sided results below belong to the original
preset implementation; the final retuning was checked with this smaller peer run.

The original preset implementation passed 48 immediate switches across the four named levels
without changes in the compared authoritative fingerprints. It also passed 108
room-edit transitions, including partition/join, breach/reseal and gas reload
fixtures. The installed replication path retained native-decoded values and
authoritative callbacks in 24,000 constructed transitions and 80,000 paired native
machine steps. Timed text checks observed latest-value publication after 150 ms
for Performance and 250 ms for Aggressive, immediate focused updates and pending
text restoration on returning to Balanced. These are sampling checks of the
configured 100/200 ms intervals, not universal latency bounds. Exhaust checks
verified the emission reduction and restoration while preserving native speed/size.

Real keyboard input opened the panel, selected/applied policies, persisted settings
and closed it with local pause state restored. The Custom controls inherited the
active policy and were reviewed in native game screenshots. Windows capture timed
out, so screenshot review used the private in-game recorder; no OS mouse-click
acceptance is claimed.

The subsequent selector redesign keeps all settings visible, replaces explanatory
blocks with hover hints, and automatically marks edits as Custom. Its separate
native UI check verified direct numeric entry without triggering preset shortcuts,
an unapplied Custom draft, rejection of an invalid frequency, live application and
saved values for 12 Hz / 8 pixels / 250 m / 75% exhaust, preview inheritance,
discard/reopen behaviour, pause restoration and return to Balanced. Numeric-field
focus was placed by the private game-side driver; typing and shortcuts used real
OS keyboard input. The 1280×720 screenshots were reviewed, and all 69 offline
tests passed. The UI build is recorded in [preset-ui-evidence.json](preset-ui-evidence.json).
Core policies and application paths are unchanged; the performance and multiplayer
results below belong to the original preset build and were not rerun for this UI edit.

Graphics-enabled separate host/client processes over loopback IP passed all 16
named-preset combinations. Additional host-only and client-only installations
passed. Each primary peer case covered 23 controlled phases on 12 real saved-world
part types: initial state, duplicate bursts, genuine changes/returns, same-frame
pulses, storage removal/re-addition, 16 hull-edit phases and disconnect/rejoin with
a second client process. Selected part states were held while publishing controlled
updates. In one-sided fixtures, the private driver explicitly prepared pending
native client room geometry before the edit phases when the ship was unloaded;
these checks do not establish natural streaming or a manual production playthrough.
Room comparisons allow bijective room-ID remapping and require matching geometry
and connectivity. Mod Manager warnings did not prevent the tested joins.

An additional nine-phase peer session opened and closed the same selector paths
on both host and client while connected. Time scale remained 1, network ticks
advanced and controlled native state/hull updates continued while the panels were
open. This used game-side Open/Close calls; real OS keyboard input was verified
separately in the single-player UI checks. Disconnect/rejoin snapshots also passed.

Private drivers, screenshots, raw traces, isolated worlds, failed pilots and
restoration receipts stay outside the repository. A first unloaded-room fixture,
an initial-add/edit sequencing problem and a mutable control-file sharing race
were retained and corrected before the successful one-sided runs. The original
157 backed-up files, including 135 world files, were hash-verified after completed
runs. Tests do not establish WAN/Steam transport, arbitrary mods, all content or
moving-scene/long-duration equivalence. Native shader/coroutine log warnings remain;
an error-free engine rendering/teardown claim is not made.

Native evidence and measured profile results are recorded in the accompanying
[preset-evidence.json](preset-evidence.json). Combined preset estimates from
discussion are not benchmark claims.

## Redesigned preset measurements (22 October 2026)

The 22 October levels replaced the earlier Performance/Aggressive settings.
Their Aggressive mapping uses 15 Hz / 15 frames. Those earlier
settings disabled contours without the depth declaration, so sky and clouds
overdrew geometry above the horizon. Their screenshots were accepted in error, and
the "retuned" results below were measured on that defective rendering. They are
withdrawn as preset evidence.

A fresh profile of Balanced in the reference view (about 18 ms per frame) found
the frame limited by the main CPU thread. The render thread was idle about 80% of
the time. Game simulation and networking took about 11 ms, cable visuals 1.5 ms,
planet/terrain visuals 1.4 ms, world-space UI 2.5 ms and rendering commands 2.9 ms.
Single graphics changes therefore stayed inside the ±3% noise band on this PC:
contour modes, every shadow reduction (including shadows off) and the readout cap.
Removing the depth-normals pass halved the drawn triangles without raising FPS.
Single cosmetic caps measured about +5% (cables at 15 Hz) and +6% (terrain reuse),
and 75% or 50% vegetation about +3% (inside the noise band). These are single
screening observations.

The final levels were then confirmed in one fresh process. It used four rounds in
alternating order (Balanced, Performance, Aggressive, Aggressive, Performance,
Balanced, twice over), 25 seconds warm-up, five seconds settling and 20 seconds of
raw frame intervals per phase:

| Level | Observations (FPS) | Mean | Relative to Balanced |
| --- | --- | --- | --- |
| Balanced | 55.5, 54.3, 55.5, 54.9 | 55.1 | Reference |
| Performance | 61.6, 61.5, 59.9, 62.3 | 61.3 | +11% |
| Aggressive | 61.0, 62.3, 62.5, 59.0 | 61.2 | +11% |

Every Performance and Aggressive observation exceeded every Balanced observation
by at least 6%. Aggressive was not faster than Performance here. Its additional
reductions (no contours, shorter shadows, less vegetation and exhaust) lower GPU
work and may help on graphics-limited PCs; that benefit has not been measured.
Screenshots of every phase showed the full forest, island, wall tops, sky and
clouds, with no missing geometry. A 1024-pixel hard-shadow variant streaked lit
walls without an FPS benefit and was rejected. Authoritative world fingerprints were
unchanged after every switch, and no world saves were written during captures.

Same 37-ship seed, Ryzen 7 3800X / RTX 2070 SUPER, D3D11, 2560×1080, quality level
0, vsync off, uncapped FPS, native configured 50 Hz and a stationary camera. Moving,
active-exhaust and multiplayer FPS gains are not established. Measured DLL SHA-256:
`6335A967EDC021C006220188AD9480117D859FD475C1B2D83D04095DB1BAE4AA`. Raw trace
hashes are recorded in [preset-redesign-evidence.json](preset-redesign-evidence.json).
The original installation and all 157 backed-up files, including 135 world files,
were restored and hash-verified after each session.

## Retuned preset measurements (withdrawn: defective contour rendering)

The final native-terrain build gives a useful step above Balanced in this world.
Two fresh processes switched in opposite orders, with Balanced at both ends,
25 seconds focused warm-up, five seconds settling per switch and 20 seconds of
raw frame intervals per phase. Percentages below use the mean of that process's
Balanced endpoint FPS as a simple reference. All frames, including stalls, remain
included. Changes within about ±3% are treated as inconclusive; this is ordinary
gameplay validation, not a formal confidence estimate.

| Preset | Forward order | Reverse order | Observed uplift over Balanced |
| --- | --- | --- | --- |
| Balanced | 63.7 / 64.4 FPS | 63.2 / 64.7 FPS | Reference |
| Performance | 71.5 FPS | 71.8 FPS | About 12% in both runs |
| Aggressive | 74.1 FPS | 75.6 FPS | About 16% / 18% |

Aggressive's additional benefit over Performance is only about 4–5% here, for
substantially shorter label/vegetation ranges, absent shadows and sparse exhaust.
Performance is the better trade-off for this view. A valid earlier native-terrain
screen using the same Aggressive settings reached **78.3 FPS**, but the final
repeated runs are the more useful expectation. A faithful 100 FPS result was not
reproduced in this reference view.

A separate 24-second-per-level look sweep rotated the rendering cameras through
three matching eight-second cycles, without translating them or moving the player.
Balanced endpoints measured 60.4 / 62.6 FPS, Performance 73.1 FPS and Aggressive
76.3 FPS: about +19% / +24% against the endpoint mean. This is one scripted camera
route, not manual piloting or a multiplayer FPS result. Immediate authoritative
fingerprints remained unchanged at every level switch; screenshot review retained
native water/surfaces in the delivered configuration.

A fresh old-default DLL control measured 64.9 / 65.8 FPS, versus the final
stationary Balanced endpoints around 64 FPS. The roughly 2% difference is inside
the practical noise band; no meaningful default gain or regression is established.

The old levels had little additional scope: Balanced already removed much of the
offscreen-canvas cost; most visible readouts were nearby; there were no live
exhaust particles; and the earlier shadow policy retained expensive map resolution
and filtering. The retuning reaches more visible part UI, lowers shadow maps and
filtering, and reduces vegetation drawing. The combined results do not isolate
each feature's contribution and must not be added to older Balanced gains.

Exact circuit-list pooling/endpoint preparation, hidden-readout formatting deferral
and an unchanged vegetation-chunk preparation check did not establish repeatable
FPS benefits. They remain private experiments. No simulation step is skipped,
combined or given a multiplied timestep. Render scaling caused native attachment
dimension errors and severe slowdowns; terrain coarsening produced missing water/
surface detail. Both were rejected, including the visually invalid 80–83 FPS
terrain combinations.

The same 37-ship seed, Ryzen 7 3800X / RTX 2070 SUPER, D3D11, 2560×1080,
quality level 0, vsync off, uncapped FPS and native configured 50 Hz were used.
Captures retained focus and had no world save writes. Experimental private hooks,
profiling counters and heavy correctness probes were absent from the final frame
captures; native switch checks ran afterward. Active-exhaust and multiplayer FPS
improvements are not established. Raw trace hashes, settings, DLL identities,
rejected screens and restoration receipts are recorded in
[preset-push-evidence.json](preset-push-evidence.json).

Final shipping DLL SHA-256:
`001B587DF551403182741E7EE397E905C0C2CAFDB0BA0276DA661AD8D578397F`.
The original installation and all 157 backed-up files, including 135 world files,
were restored and hash-verified. No permanent installation, package or publication
was made.

## Original preset measurements (superseded)

Interpretation updated 22 October 2026: these are ordinary gameplay captures with
natural variance. Treat changes within roughly ±3% FPS as inconclusive. This is a
practical reporting rule, not a statistical confidence interval or proof that
larger changes are real. The original Performance and Aggressive settings did not
establish a dependable performance step above Balanced. No expected FPS uplift
should be advertised for those earlier settings from these results. The retuned
settings in the table above are measured separately in `preset-push-evidence.json`.

Three fresh matched pairs compared the previous development DLL with the new
Balanced default. Pair order was control/candidate, candidate/control,
control/candidate. Each used 35 seconds focused warm-up, a screenshot plus three
seconds settling and 20 seconds of raw frame intervals. The equal-weight paired
FPS change was **+0.10%**; median per-run FPS was **62.14 / 62.30**. Individual
pairs moved in opposite directions. This establishes no dependable new default
FPS gain or regression from the selector on this workload, and no frame-tail
improvement is claimed.

Two additional fresh processes compared live levels in opposite orders:
Balanced/Performance/Aggressive/Faithful/Balanced and
Balanced/Faithful/Aggressive/Performance/Balanced. Each phase settled for five
seconds and captured 20 seconds. References interpolate log mean frame time
between that process's Balanced endpoints; process effects have equal weights.

| Preset | FPS changes in the two processes, relative to Balanced | Equal-process effect |
| --- | --- | --- |
| Faithful | +0.53%, -1.45% | -0.46% |
| Performance | -1.10%, +6.54% | +2.65% |
| Aggressive | +2.84%, +5.63% | +4.23% |

Performance's +2.65% aggregate is inside that noise band, and its observations
disagree. Aggressive's +2.84% observation is also inside the band; +5.63% is a
tentative lead, with visible compromises, rather than confirmation of a dependable
benefit. These are two process observations on one
stationary world, not a population confidence interval or a reliable prediction
for other worlds/hardware. Balanced endpoints were 63.02/62.68 FPS in the first
process and 62.26/59.79 in the second; interpolation accounts for smooth drift,
but cannot remove arbitrary nonlinear drift or GC variation. All frames and
stalls are retained. Feature effects are not additive.

Hardware/workload: Ryzen 7 3800X, RTX 2070 SUPER, D3D11, 2560x1080 borderless
fullscreen, quality level 0, vsync off, uncapped FPS, the same immutable 37-ship
seed and installed mod stack. All captures retained focus, had no world save
writes and used native configured 50 Hz. Expensive correctness probes and network
diagnostics were absent from frame captures. Active-exhaust, moving-camera and
multiplayer FPS gains are not established by these measurements.

The original tier design also had limited additional scope: Balanced already includes the
large offscreen-canvas saving. Higher tiers limit formatting/visibility for only
passive signal-display and gas-meter readouts, with extra focus/camera checks;
other part UI and native state callbacks keep running. Those shadows changed distance
and cascades, while shadow-map resolution and filtering remained unchanged. Earlier
research on the same world found no live particles, so active exhaust needs its
own representative workload. The current captures do not isolate which remaining
CPU or GPU cost limits FPS or attribute the tier effects to individual settings.

Original native-tested and frame-tested candidate SHA-256 (before the selector UI redesign):
`2667A68D30DB67BC2C74B9460FDA998865B95EC0C494284231D8AF518B0955BF`.
The control was
`B4F9A5598733815684476A6F7340E41B63102364CD0D7546AB920F8135ED25FF`.
There is no package, permanent installation or publication from this slice.
