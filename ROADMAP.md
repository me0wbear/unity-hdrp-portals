# Roadmap

Written against a concrete reference: [Fluid Seamless Portals — Full][fluid],
version 2.0, which is the closest commercial equivalent. Its advertised feature
set is the yardstick here, not because matching a bullet list is a goal in
itself, but because it is the list a buyer compares against.

Two things are worth knowing before reading the gaps.

The reference asset's own documentation lists, under known issues, that
"URP/HDRP camera effects may cause a little sudden change during teleportation".
That is the same colour grading shift described in [Track 1](#track-1--finish-what-is-already-broken)
below. It is not a solved problem there either.

Recursion and portals facing each other arrived in their 2.0; the 1.3
documentation says outright not to place one portal facing another. Both work
here today, the second one measured.

## Where this module already stands

| Capability | Here | Reference |
| --- | --- | --- |
| Seamless crossing | Yes, crossing frame delta 0.0003 | Yes |
| Recursion | Yes, depth per portal, scene wide budget | Yes, from 2.0 |
| Portals facing each other | Yes | From 2.0; 1.3 says do not |
| Rigidbodies and props through | Yes, velocity and angular velocity rotated | Yes |
| Clones with plane slicing | Yes | Yes |
| Rotated and skewed pairs | Yes | Yes |
| Real depth in the opening for fog, DoF, SSAO | Yes | Not documented |
| Volume state carried across the crossing | Partial, within a blend distance | Listed as a known issue |
| Measured regression suite | 55 unit tests plus scene checks | None published |
| Pipelines | HDRP only | Built-in, URP, HDRP |
| Ready-made environment art | No | Yes, 850 MB of it |

The gap that matters commercially is the bottom two rows. The gap that matters
for a game being built on this module is Track 1.

## Track 1 — finish what is already broken

Nothing here is a new feature. All of it is visible today.

### 1.1 Colour grading through the opening

**Symptom.** Standing in room A and looking through a portal at room B, the
geometry is B's but the grading is A's. Step through and the colour changes.

**Cause.** The virtual camera renders without post-processing, which is what
makes the crossing seamless: the opening lives under the main camera's exposure
and tonemap along with everything else. But colour grading is a Volume effect
and Volumes are sampled at the main camera's position. Lighting and fog come
out right, because the virtual camera renders those itself. Exposure and
grading do not.

**Current mitigation.** `Blend Volumes Through Portal` moves a volume anchor
toward the exit as you approach, over `Volume Blend Distance`, default 2.5 m.
Measured: 0.2786 mismatch at 3 m, 0.0003 at the crossing. It hides the seam
only inside that distance.

**Direction.** Grade the virtual camera's target with the destination Volume
before compositing, rather than moving the viewer's anchor. That means running
a colour grading pass on the portal target with the Volume stack evaluated at
the virtual camera's position, and leaving exposure to the main camera as it is
now, so the crossing stays seamless.

**Verify.** ColorCheck `far delta` must fall from 0.2786 toward the `cross
delta` of 0.0003, with two rooms graded deliberately differently. That check
already exists and already measures exactly this.

**Size.** Medium. Touches the composite, which is the riskiest part of the
module. Do it first anyway: it is the defect a player sees.

### 1.2 Depth of field below 0.8 m

**Symptom.** Closer than about 0.8 m the opening blurs when it should not.

**State.** The device depth reconstruction fix moved the boundary from 3 m to
0.8 m. 0.8 m is where `PortalAperture` begins pushing the quad back along the
portal normal, so the remainder is about the aperture rather than the
projection.

**Verify.** BubbleCheck with `PORTAL_FARDOF=1`. Content sits 8 m away and the
blur range is 4–9 m, so every sample must be soft. Today: 0.00266 at 1.5 m,
correct; 0.00803 at 0.8 m and 0.00772 at the crossing, both wrongly sharp,
against 0.00227 for the same view rendered directly.

**Size.** Small to medium once someone sits with the aperture maths.

### 1.3 Motion vectors in the opening

**Symptom.** A faint trail behind the content when the camera moves fast.

**State.** Motion vectors belong to the quad, not to what is seen through it.
Taking them from the virtual camera was tried and reverted: the values come
back non-zero on a completely static scene, and temporal antialiasing then
resolves the frame along a movement that never happened, blurring the whole
opening.

**Direction.** Compute the content's screen motion directly from the virtual
camera's current and previous view projection matrices rather than trusting the
AOV output. The same class of bug as 1.1 in the depth work: the buffer is not
in the convention it appears to be.

**Verify.** GhostCheck, plus a frame to frame difference while strafing past an
opening.

**Size.** Medium.

### 1.4 Harness defects

- `tools/check.sh` hardcodes the project path, so run from a git worktree it
  builds and measures a different checkout while appearing to test the branch.
  One line: `PROJECT="${PORTAL_PROJECT:-$(pwd -W)}"`.
- The Seam check does not build, failing with `level0 is corrupted` during
  scene serialization. It fails identically on unmodified code, so it is the
  harness. It is also the only check that measures frame to frame continuity
  across the crossing, which makes it the one worth repairing first.

**Size.** Small. Do these before anything in Track 2, because they are how
everything else gets verified.

## Track 2 — cost

The FPS drop when looking at a portal is not a bug, but it decides whether the
module is usable in a real scene.

Measured in the editor on the sandbox: looking away, 2.3 ms, 535 triangles, 17
set pass calls. Looking at the opening, 8.5 ms, 2500 triangles, 93 set pass
calls. The scene is drawn once per recursion level, so the multiplier follows
scene complexity, not portal complexity.

The benchmark in `Assets/portal/Examples/Benchmark` shows where the cost is:
halving the target resolution at depth 2 saves 1.3 ms, quartering it saves
nothing further. The cost is draw calls, not fill rate.

Worth doing, in order:

1. **Cull recursion levels by screen coverage.** A level whose opening covers a
   handful of pixels does not need a full scene render. Cheap to implement,
   large effect on deep recursion.
2. **Frustum cull the virtual cameras against the opening.** A level currently
   renders everything the virtual camera can see, not only what can reach the
   viewer through the opening. Narrowing the virtual camera's frustum to the
   opening's silhouette cuts geometry per level.
3. **Reuse a level across frames when neither the viewer nor the contents
   moved.** Risky with temporal effects; measure before believing it.

**Verify.** The benchmark, extended with a scene that has real geometry rather
than five crates.

## Track 3 — features the reference has

Ordered by what a first person horror project actually uses, not by the
reference's ordering.

### 3.1 Raycasts through portals

Shooting, interaction prompts and AI line of sight all break at an opening: a
ray stops at the quad. Needs a helper that takes a ray, finds the first portal
it crosses, transforms it through and continues, up to a depth limit.

Self contained, no rendering risk, directly useful in a shooter or a horror
game. **Do this one first of the three.**

### 3.2 Arbitrary exit orientation, "multi-gravity"

Portals leading to a wall or a ceiling. The transform maths already handles
arbitrary rotation, so the work is in the traveller: rotating the controller's
up vector, and deciding what a `CharacterController` does when its up axis
changes, which Unity's does not support directly.

Larger than it looks, and only worth it if the game wants the effect.

### 3.3 Portals placed at runtime

Shooting a portal onto a wall. Mostly already possible: create the pair, place
it, wire the camera. What is missing is a supported API and validation that
placement against arbitrary geometry does not leave the opening intersecting a
wall.

### 3.4 Nested portals from different pairs

Seeing pair B through pair A. The current recursion assumes one chain per
portal; a second pair inside the view is not rendered from the virtual camera's
position. This is the largest rendering change on the list.

### 3.5 Non-euclidean tunnels

A corridor longer inside than outside. Built from the same primitives, mostly a
content and prefab problem once 3.4 exists.

## Track 4 — reach

### 4.1 URP

The reference supports Built-in, URP and HDRP; this module is HDRP only. The
composite leans on HDRP specifics: AOV buffers, custom passes at a named
injection point, `HDCamera` frame history, frame settings and the exposure
contract that keeps the opening under the main camera's grading. A URP port is
a second implementation of the composite behind a shared front end, not a
configuration switch.

Worth doing only if the module is to be sold or published widely. It does
nothing for a project already on HDRP.

### 4.2 UPM package

Installation is currently "clone the repo" or "copy a folder". Blocking issues:
shader includes use absolute `Assets/portal/...` paths, and the editor tools
load prefabs by absolute path. Both must become package relative first.

### 4.3 Example content

The reference ships 850 MB of environment art. Matching that is an art
budget decision, not an engineering one. The sandbox and integration scenes
cover demonstrating the module; they do not sell it.

## Suggested order

1. Track 1.4, the harness, because it is how everything else is verified.
2. Track 1.1, colour grading, because it is the defect that shows.
3. Track 3.1, raycasts, small and immediately useful.
4. Track 2.1 and 2.2, cost, before the scene gets heavy.
5. Track 1.2 and 1.3, the remaining rendering defects.
6. Everything else on demand.

[fluid]: https://assetstore.unity.com/packages/3d/environments/fluid-seamless-portals-full-266857
