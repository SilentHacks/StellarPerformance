# Canvas culling (2.0.0)

`CanvasCulling=true` hides inspected world-space ship-part canvases outside every
active camera that can render their graphics. It is independently derived from the
game's visual lifecycle and our private visibility experiments. No third-party mod
implementation or assembly is included or executed.

## Behavior and ownership

The module observes native visual add/remove events. Initial discovery and recovery
after cleanup use `ShipPartVisuals`' native part map: visual prefabs do not need the
part-ID components used by physics prefabs. Destroyed ships are pruned, and scene
unload, shutdown and module failure restore canvases hidden by this module and drop
references. Native-disabled canvases retain their disabled state.

Visibility is evaluated in `Canvas.preWillRenderCanvases`, before native uGUI
layout/clipping/graphic rebuild. It reads current camera projection/view matrices,
viewport effects and canvas world transforms on each invocation. There is no 100 ms
visibility timer. Cameras rendering to textures participate too. The graphics'
layers determine which cameras matter; the root canvas layer is insufficient.
An infinite far plane retains the other five frustum planes. Uncertain data keeps
the canvas enabled.

The expensive per-frame geometry survey was rejected. Fixed native graphic slots
are enclosed once in canvas-local bounds, transformed conservatively for the current
world pose and expanded by 0.5 m total (0.25 m per side). This margin changes only
the decision to draw, and draws extra UI near the frustum boundary. Native text
updates, input, masks, clipping, billboards, GameObjects, colliders, ID meshes and
native gameplay callbacks remain live. Restored canvases can rebuild through native
uGUI/TMP in that same draw. There are no new room, gas, circuit, production, physics,
save-format or network-format approximations.

Selected controls and focused TMP input fields remain enabled, including while
dragging or looking away. The supported prefab set has fixed text slots with native
autosizing, clipping or bounded overflow behavior. Unknown visual types and canvas
scripts, nested canvases, layout groups/fitters, animation controllers, child
billboards and free-size unmasked overflow text retain native drawing. A root rect
resize releases ownership. Arbitrary later changes to child layout, layers, fonts
or scripts by other mods are outside the inspected contract; turn this switch off
for such UI modifications.

Storage is bounded to 4,096 canvases, 64 graphics per canvas and 32 active cameras.
Canvases beyond the limit stay native; camera overflow restores owned canvases.
Normal feature paths install no diagnostic observers or timing counters.

## Evidence

On the tested 37-ship world, stationary FPS rose from **50.77 to 64.48 (+27.01%)**,
equivalent to **21.26% less mean frame time**. A timed moving-camera route measured
**49.40 to 62.39 FPS (+26.30%)**. These are incremental results above the current
mod's other enabled defaults, not comparisons against an entirely unmodified game.

| Fresh process | Order | Native canvas FPS | Culled FPS | FPS gain |
| --- | --- | ---: | ---: | ---: |
| Bench02 | culling/native/culling | 50.01 | 65.07 | 30.13% |
| Bench03 | native/culling/native | 51.54 | 63.90 | 23.96% |
| Stationary aggregate | equal process weights | 50.77 | 64.48 | 27.01% |
| Motion01 | native/culling/native | 49.40 | 62.39 | 26.30% |

Each stationary process warmed for 35 seconds, then captured three 25-second phases
with five seconds of settling between changes. Motion used a 25-second warm-up and
three 24-second captures; each capture reset the same clock-driven translation/yaw/
pitch route and covered three eight-second cycles. The two stationary processes
reverse the triplet order. Repeated arms within a process use geometric means; the
aggregate gives each fresh process equal weight in log space. Two repeats are not
a general confidence interval, and frames are not independent replicates.

All nine final traces (12,553 frames) retained focus, recorded no save writes and ran
the native 50 Hz tick configuration. Correctness/timing observers were absent from
FPS captures. Tests used a Ryzen 7 3800X / RTX 2070 SUPER, D3D11, 2560x1080, quality
level 0, vsync off and uncapped FPS, retaining the installed mod stack. Both modes
use the same DLL; the private driver restores owned canvases and switches only the
culling active flag. The supported user setting remains restart-only.

The reference view contained 497 part canvases: 487 qualified and 417 were hidden.
Separate lifecycle timing gave a median 0.617 ms per culling invocation and a 0.843 ms
95th percentile; the 0.504 ms mean includes empty/menu/cleanup passes. Those timings
are not added to FPS claims, which already include the complete feature cost.

The final native acceptance pass completed 14 grouped checks, including 180 scripted
camera poses, 3,801 visible recoveries, 302,592 hidden-graphic checks and 109 immediate
bitwise authoritative part/physics/gas comparisons with no canvas-induced changes.
It added 48 visual parts, removed 16, destroyed the fixture and preserved original
collider identities. Host RPC checks separately verified real building, writing,
pickup/replacement, removal and piloting. The 59 offline tests also passed.

Stationary A/B screenshots were visually inspected without finding missing visible
readouts. Animated water and live numeric changes prevent a global pixel-exact
comparison; no percentage of visual equivalence is claimed. The independent runtime
oracle found no hidden graphic that should have been visible in its checked samples.

All 157 original backed-up files, including 135 existing world files, were restored
and hash-verified after each run. No private driver or game process remains. The
final unreleased net472 DLL SHA-256 is
`F7F5D8C9F23569814C410C8231FA905D4D385C7ABCAE67E8A21D37F92F7A9024`.
No package, permanent deployment or publication is part of this slice.

The compact machine-readable record is [canvas-culling-evidence.json](canvas-culling-evidence.json).
Raw drivers, frame traces, screenshots, disposable worlds and restoration receipts
remain outside this repository in the private canvas-culling experiment directory.

The shipping assembly targets net472. Offline tests compile the same sources for
net9.0 and never install runtime detours on CoreCLR. They exercise affine envelopes
against all eight transformed corners, frustum boundaries and independent corner
oracles, invalid/infinite-far projections, eligibility exclusions and module metadata.

Runtime checks use the inspected Mono game, installed mod stack and an isolated copy
of the 37-ship reference world. Scripted camera poses cover translations, rapid yaw,
pitch, FOV/viewport changes and moving visual ships. An independent Unity geometry
oracle checks hidden graphic rectangles and generated text geometry during actual UI
render callbacks. A separate camera facing the opposite direction tests camera union.

Native visual construction/removal/destruction, resizing, disabled canvases, bounded
tracking overflow, unknown scripts, cleanup/re-discovery and an injected module
failure exercise ownership. Single-player host RPC tests place a real part, enter
writing mode, submit a constant through native UI/network callbacks, verify the exact
authoritative value and current glyph geometry, pick up/place/remove it, and enter/exit
a pilot seat. Native physics-ray/filter/hover callbacks remain operational.

These are scripted native API/callback checks, not OS mouse/keyboard playthroughs or
remote multiplayer acceptance. They do not establish arbitrary mod compatibility,
all game content or long-duration flight/building equivalence. Earlier batching and
networking gains are separate comparisons and must not be added to this slice's gain.
