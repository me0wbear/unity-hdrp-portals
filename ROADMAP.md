# Roadmap

Written against a concrete reference: [Fluid Seamless Portals — Full][fluid],
version 2.0, which is the closest commercial equivalent. Its advertised feature
set is the yardstick here, not because matching a bullet list is a goal in
itself, but because it is the list a buyer compares against.

Two things are worth knowing before reading the gaps.

The reference asset's own documentation lists, under known issues, that
"URP/HDRP camera effects may cause a little sudden change during teleportation".
That is one of the two causes of the contrast change described in Track 1
below, and it is not a solved problem there either.

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
| Real depth in the opening | Partial: fog yes, depth of field beyond 0.8 m, occlusion no | Not documented |
| Volume state carried across the crossing | Partial, within a blend distance | Listed as a known issue |
| Screen space occlusion inside the opening | No, wrong under the oblique projection | Not documented |
| Measured regression suite | 55 unit tests plus scene checks | None published |
| Pipelines | HDRP only | Built-in, URP, HDRP |
| Ready-made environment art | No | Yes, 850 MB of it |

The gap that matters commercially is the bottom two rows. The gap that matters
for a game being built on this module is Track 1.

## Track 1 — finish what is already broken

Nothing here is a new feature. All of it is visible today.

### 1.1 Screen space effects under the oblique projection

**This is the confirmed cause of the contrast change at a crossing**, isolated
by measurement in the sandbox and reproduced both ways.

**Symptom.** The same view looks different through the opening and after
stepping through. Reads as a contrast change, concentrated where surfaces meet.

**Cause.** The virtual camera uses an oblique near plane so nothing behind the
exit is drawn. HDRP's ambient occlusion linearises depth with
`LinearEyeDepth(depth, _ZBufferParams)`, and the pipeline's own header says
what that costs:

```
// Z buffer to linear view space (eye) depth.
// Does NOT correctly handle oblique view frustums.
```

So the occlusion pass reads wrong world positions on every portal camera. With
the actual runtime matrices, a point 10 m from the camera reconstructs as
**2.878 m** under the portal projection against 10.000 m on the main camera.
The spatial denoise for the occlusion uses the same formula, so the error is
filtered, not corrected.

**Measured**, region 320x200 at the same pose, 8 bit channel levels:

| Mode | MAE R/G/B | Worst in region |
| --- | --- | --- |
| Oblique projection with occlusion, as shipped | 0.80 / 0.97 / 1.28 | 20 |
| Occlusion off in both cameras | 0.04 / 0.01 / 0.02 | 2 |
| Occlusion on, ordinary projection | 0.05 / 0.02 / 0.03 | 2 |

Both controls land in the same place, which rules out each cause on its own.

**Scope is wider than the sandbox.** Ambient occlusion is not enabled by the
sandbox volume; it arrives from the HDRP defaults. Any HDRP project with stock
settings meets this. And every effect that linearises depth the same way is
affected on those cameras, not only occlusion: screen space reflections,
screen space global illumination, contact shadows and volumetrics all read
depth through the same formula. Occlusion is simply the one that was measured.

**The fix space is binary.** There is no configuration in which HDRP's
occlusion is correct on an oblique camera, because obliqueness makes device
depth depend on screen x and y, and no `_ZBufferParams` can undo that. So
either the virtual camera stops being oblique, and the clipping it provides
comes from somewhere else, or those effects stop running on the virtual camera
and the opening gets them from elsewhere.

**Direction, in the order worth trying.**

1. *Mitigate now.* Turn the depth dependent screen space effects off on the
   virtual cameras through frame settings, the way exposure and post processing
   already are. Measured effect: worst case 20 levels down to 2. Cost: the
   opening loses its own ambient occlusion. Put it behind a field so it can be
   turned back on.

2. *Fix properly.* Get the content depth into the main camera's depth buffer
   **before** the pipeline's screen space effects run, and let the main camera
   shade the opening. That depth is in the main camera's own convention by
   construction, so occlusion, depth of field and reflections all become
   correct at once. `AfterOpaqueDepthAndNormal` is the injection point: the
   pipeline regenerates the depth pyramid immediately after it and tracks
   whether a custom pass modified depth.

   This was attempted during the depth work and reverted: at very close range
   the frame went white, and fog risks being applied twice because the virtual
   camera has already baked its own. Both need diagnosis rather than assumption
   before this is called feasible.

   If it works it closes 1.1 and 1.2 together and probably reflections too.

3. *Fallback if 2 proves impossible.* Compute occlusion ourselves in a custom
   pass on the portal target, reconstructing position with the inverse oblique
   projection. `PortalContentDepth.shader` already does exactly that maths.
   Expensive, and matching the look of the pipeline's own occlusion is its own
   problem. Only if 2 fails.

**Verify.** The comparison above needs to become a lab check rather than a
temporary player, otherwise the fix gets graded by opinion. Add the two control
modes as switches, as the investigation did.

**Also worth measuring, not yet done.** Obliqueness grows as the view to the
portal gets more grazing, so the error should grow with the angle. If it does
not, the mechanism is not fully understood. Angled portals, recursion levels
below the first, and a moving scene were all outside the investigation.

**Size.** Step 1 is small. Step 2 is the largest single item in Track 1.

### 1.2 Volume grading across the crossing

A **separate** cause with the same symptom, and the one previously assumed to
be the whole story. It is not: the sandbox has a single global volume shared by
both rooms, so no grading difference can exist there, yet the shift is still
visible. Both causes are real, in different scenes.

**Symptom.** Rooms with different volume profiles: the opening shows the far
room's geometry under the near room's grading.

**Cause.** The virtual camera renders without post processing, which is what
keeps the crossing seamless. Colour grading is a volume effect sampled at the
main camera's position. Lighting and fog come out right because the virtual
camera renders those itself; exposure and grading do not.

**Current mitigation.** `Blend Volumes Through Portal` moves a volume anchor
toward the exit over `Volume Blend Distance`, default 2.5 m. ColorCheck
measures 0.2786 at 3 m against 0.0003 at the crossing, so it works only inside
that distance.

**Direction.** Grade the virtual camera's target with the destination volume
before compositing, instead of moving the viewer's anchor, leaving exposure to
the main camera so the crossing stays seamless.

**Verify.** ColorCheck `far delta` falling from 0.2786 toward the crossing
delta.

**Size.** Medium. Touches the composite.

### 1.3 Depth of field below 0.8 m

**Symptom.** Closer than about 0.8 m the opening blurs when it should not.

**State.** The device depth reconstruction fix moved the boundary from 3 m to
0.8 m. 0.8 m is where `PortalAperture` begins pushing the quad back along the
portal normal, so the remainder is about the aperture rather than the
projection. Step 2 of 1.1 would close this as well, since depth of field reads
the same depth.

**Verify.** BubbleCheck with `PORTAL_FARDOF=1`. Content sits 8 m away and the
blur range is 4-9 m, so every sample must be soft. Today: 0.00266 at 1.5 m,
correct; 0.00803 at 0.8 m and 0.00772 at the crossing, both wrongly sharp,
against 0.00227 for the same view rendered directly.

**Size.** Small to medium on its own, free if 1.1 step 2 lands.

### 1.4 Motion vectors in the opening

**Symptom.** A faint trail behind the content when the camera moves fast.

**State.** Motion vectors belong to the quad, not to what is seen through it.
Taking them from the virtual camera was tried and reverted: the values come
back non-zero on a completely static scene, and temporal antialiasing then
resolves the frame along a movement that never happened, blurring the whole
opening.

**Direction.** Compute the content's screen motion directly from the virtual
camera's current and previous view projection matrices rather than trusting the
AOV output. The same class of bug as the depth reconstruction: the buffer is not
in the convention it appears to be.

**Verify.** GhostCheck, plus a frame to frame difference while strafing past an
opening.

**Size.** Medium.

### 1.5 Harness defects

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

1. Track 1.5, the harness, because it is how everything else is verified, and
   because 1.1 needs its comparison turned into a check before it can be graded.
2. Track 1.1 step 1, the frame settings mitigation. Small, measured, ships today.
3. Track 1.1 step 2, content depth into the main camera before its screen space
   effects run. The largest item here, and it closes 1.1 and 1.3 together.
4. Track 3.1, raycasts, small and immediately useful.
5. Track 2.1 and 2.2, cost, before the scene gets heavy.
6. Track 1.2 and 1.4, grading and motion vectors.
7. Everything else on demand.

[fluid]: https://assetstore.unity.com/packages/3d/environments/fluid-seamless-portals-full-266857
