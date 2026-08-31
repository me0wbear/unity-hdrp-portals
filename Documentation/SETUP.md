# Portals: setup guide

Seamless portals for Unity 6 on HDRP. The crossing is invisible to the eye: the
view in the opening lives under the same exposure, tonemapping and antialiasing
as the rest of the frame, and the frame before you cross and the frame after
are the same picture.

## 1. Requirements

- Unity **6000.5.9f1**
- HDRP **17.5.0**
- Cinemachine **3.1.4** — optional, only needed if it drives your camera
- A third-party player controller — optional, the module works without one

## 2. Installation

Copy the `Assets/portal` folder into your project as a whole. Everything is
inside it: code, shaders, materials and prefabs. No project settings need
changing.

Check that the `Assets/portal/PortalScreenMat.mat` material is built on the
`Portals/PortalScreen` shader. If the material got lost, rebuild it with
**Tools → Portals → Rebuild Module Assets**.

## 3. Ready-made prefabs

The module ships three prefabs. Any of them can be dragged into a scene and
worked with as is, with nothing assembled by hand.

| Prefab | What is inside | When to take it |
| --- | --- | --- |
| `Portal.prefab` | A single portal: root, trigger zone, quad with the material | You need one end: an asymmetric pair, a portal in an existing composition |
| `PortalPair.prefab` | Two ends already linked to each other both ways | The usual case: you need a working passage |
| `PortalPlayer.prefab` | Character controller, traveller, camera bridge and a camera at eye level | To walk through on foot without wiring your own controller |

The ends of the pair sit thirty metres apart and face each other. The distance
is not cosmetic: a pair standing close catches itself in frame, and instead of
two views you get a corridor of reflections. After dragging the pair in, move
the ends one by one, by their own roots.

What remains to do by hand: give the portals a camera. The camera reference
lives in the scene, and a prefab knows nothing about the scene, so the
`Player Camera` field stays empty. **Tools → Portals → Wire Scene** fills it —
it hands one camera to every portal in the scene. **Tools → Portals →
Validate Scene** checks that nothing was missed.

The controls in `PortalPlayer.prefab` are a demo: walk, run and mouse look.
They exist so you can step through a portal and look around, not to build a
game on. In your own project replace `PortalDemoController` with your
controller — the other components on the same object stay as they are.

If the prefabs got lost or fell out of sync with the component fields, rebuild
them with **Tools → Portals → Rebuild Prefabs**.

## 4. A portal by hand

The fast path is **Tools → Portals → Create Portal Pair**: it creates a linked
pair with the camera already assigned. Then place the portals where they
belong.

The manual path, step by step.

### 4.1 The root

Create an empty GameObject and name it `Portal_A`.

Place it where the opening should be. The reference point is the centre of the
opening. If a three-metre opening stands on the floor, the root goes at height
1.5.

Rotate it so the local **+Z** axis looks at the player. That is the portal's
front side, and everything else is measured from it.

### 4.2 The component and the trigger zone

Add the `Portal` component.

Add a Box Collider. Enable Is Trigger. Set Size: X and Y to the opening size,
Z to the zone thickness — 1.0-1.5 is enough.

The trigger describes the zone around the opening. The crossing itself is
detected by distance to the portal plane, not by the trigger, so it keeps
working even while the player's controller is temporarily disabled.

### 4.3 The quad

Create a child object and name it `Screen`.

- Local position and rotation — zero.
- The local scale carries the opening size: X — width, Y — height, Z — one.
- Mesh Filter: the built-in **Quad**.
- Mesh Renderer: the `PortalScreenMat` material, shadows off — both casting
  and receiving.

In the `Portal` component on the root, assign this Mesh Renderer to the
**Screen** field.

The quad must be a **direct child** of the portal root. Its transform is
rewritten every frame, so nothing may stand between it and the root. Hang a
decorative frame as the quad's sibling, not its parent.

### 4.4 The second portal

Repeat steps 4.1-4.3 for `Portal_B`. The orientation can be anything: a
rotated pair works the same as a pair face to face.

Then link the pair both ways: on `Portal_A` set **Exit Portal** to `Portal_B`,
on `Portal_B` — to `Portal_A`.

## 5. The player

On the player's root object (the one with the CharacterController) add
`PortalTraveller`.

In the **View Point** field assign the camera's Transform. The crossing is
counted at this point: the player must be carried over exactly when the plane
crosses the eye, otherwise the frame after the crossing will not match the
frame before it.

On both portals assign the same camera — the one the player looks through — to
the **Player Camera** field.

## 6. View continuity

If Cinemachine drives the camera, or the controller keeps its own stored look
angle, add `PortalCameraBridge` to the player object and assign its
**Traveller** and **Gameplay Camera**.

The bridge does three things on a crossing:

1. Carries the Cinemachine state across and closes the crossing with a hard
   cut. Without it the camera travels the whole way from the old pose to the
   new one in front of the player.
2. Resets the HDRP frame history. The pipeline does not detect a teleport on
   its own, and one frame would go out with motion vectors computed from the
   pose before the crossing.
3. Rotates the controller's stored look angle and world-space velocity.

The third point works through reflection against UHFPS: it looks up the types
`UHFPS.Runtime.LookController` and `UHFPS.Runtime.PlayerStateMachine`. The
module does not reference UHFPS and compiles without it. If your controller is
your own, the stored-angle rotation is yours to write: subscribe to
`PortalTraveller.Teleported` and rotate your state by `context.Rotation`.

The UHFPS integration is described separately in [`UHFPS.md`](UHFPS.md):
preparing the player with one menu item, the look-angle modes, reading the
binding log, and the blink on the crossing frame.

**Limitation:** the UHFPS bridge is verified against stubs that copy the real
class names and signatures. It has not been confirmed on the live asset.

## 7. Clones and slicing

While a traveller crosses the plane, half of it has passed and half has not.
Without a clone it drives into the wall on one side and appears out of nowhere
on the other. `PortalTraveller` creates the clone itself, poses it on the far
side and slices both halves: the original by the entrance plane, the clone by
the exit plane.

The clone is an image only: scripts, colliders, bodies, cameras, light and
audio sources are stripped from the copy.

### Teaching your material to slice

The module sets three properties on every renderer of a traveller:

| Property | Type | Meaning |
|---|---|---|
| `_SliceCentre` | Vector | A point on the slicing plane, world space |
| `_SliceNormal` | Vector | The plane normal, pointing at the half that **stays** |
| `_SliceEnabled` | Float | 1 — slice, 0 — do not |

Materials that know nothing about these properties simply ignore them, so
setting them is safe on anything. To make a material slice, add fragment
discard to it.

In Shader Graph: `Position` in World space → `Subtract` with `_SliceCentre` →
`Dot Product` with `_SliceNormal` → `Lerp(1, result, _SliceEnabled)` → into
`Alpha`, with Alpha Clipping enabled at threshold 0.

In a hand-written shader, in the fragment function:

```hlsl
float side = dot(positionWS - _SliceCentre.xyz, _SliceNormal.xyz);
clip(lerp(1.0, side, _SliceEnabled));
```

The module does not ship a ready sliced material: it depends on what your
character looks like, and replacing yours with a foreign one would be worse
than not slicing at all. If slicing is not needed, or the material cannot do
it, choose the behaviour in the **Clone Fallback** field of the `Portal`
component: draw the clone whole, or not at all.

**Limitation:** skinned animation is not carried to the clone. The copy
repeats the root's pose, which is fine for rigid geometry — props, crates, a
simple player body.

## 8. Validating the scene

**Tools → Portals → Validate Scene** walks every portal and reports what it
lacks: an unlinked pair, a pair linked one way, a missing camera, a missing
quad, a quad in the wrong place in the hierarchy, a missing trigger, a
traveller without a view point.

**Tools → Portals → Wire Scene** links all unpaired portals pairwise and hands
one camera to all of them.

## 9. Portal component fields

| Field | What it does |
|---|---|
| **Screen** | The quad the view is shown on. A direct child of the root. |
| **Exit Portal** | The paired portal. Link both ways. |
| **Player Camera** | The camera the view is computed for. |
| **Resolution Divider** | Divider of the target size. One means screen size and a pixel-for-pixel match. Higher is cheaper and blurrier. |
| **Recursion Depth** | How many times the portal is seen inside itself. Zero — no recursion. Every level costs a camera and screen-sized buffers. |
| **Restrict View To Opening** | Render each level only inside the screen rectangle its opening occupies. Same image, same pixel density; the cost follows the opening's area instead of the whole frame. |
| **Screen Space Effects In View** | Bring HDRP's depth-reading screen space effects back on the virtual cameras. Off by default: their projection is oblique, HDRP linearises its depth incorrectly, and the occlusion computed there differs from the view after the crossing. |
| **Clipping Offset** | Offset of the clip plane from the exit plane. Removes flicker exactly at the opening's edge. |
| **Clipping Safety Factor** | Reserve for holding the quad right at the face. Lower — the quad stays closer to the opening plane, higher — more reliable at point-blank range. |
| **Cull When Offscreen** | Do not compute the view while the opening is out of sight. Disable only for debugging. |
| **Write Content Depth** | Write the depth of what is visible through the portal into the main camera's buffer. Without it depth of field blurs the opening into mush, treating it as a plane in front of the face. |
| **Blend Volumes Through Portal** | Carry the destination side's volume state across in advance. Needed when the rooms differ in grading. |
| **Volume Blend Distance** | The distance at which that carry-over begins. |
| **Fade Occlusion Near Crossing** | Fade the main camera's ambient occlusion near an opening, so the frames on both sides of the teleport carry matching occlusion instead of a visible cut. |
| **Fallback Color** | What fills the opening beyond the recursion depth. |
| **Clone Fallback** | What to do with the clone when its material cannot slice. |

## 10. Performance

Each recursion level is a separate camera with screen-sized buffers. Portals
are computed only while visible; the ones out of sight are suspended. With
**Restrict View To Opening** on, a level renders only the screen rectangle of
its opening, so the cost follows the opening's size on screen.

`PortalSystem.Budget` caps the total number of live levels across the scene.
When exhausted, recursion depth is cut rather than whole portals: better to
show every opening shallower than some of them black.

## 11. Limitations

- **The opening must be a real hole in the geometry.** A portal laid over a
  solid wall will show the inside of that wall from close up.
- **Slicing works only on materials that support it.** The property contract
  is in section 7; no ready material ships with the module.
- **Skinned animation is not carried to the clone.**
- **Collisions are not split by side during the crossing.** A traveller can
  catch on geometry around the exit.
- **Stereo rendering is not supported.**
- **Sound does not pass through a portal.**
- **Motion vectors in the opening belong to the quad, not to what is seen
  through it.** Fast camera movement leaves a faint trail on the content: the
  quad and the content move differently across the screen, and temporal
  antialiasing and motion blur follow the former. Substituting the real
  vectors was tried: what the virtual camera returns is non-zero on a
  completely static scene, and the opening blurred as a whole. A sharp image
  with a faint trail beats mush, so the substitution was removed.
- **Depth of field over-blurs the opening in the last centimetres before the
  crossing.** At a distance everything is right: the depth is substituted with
  the distance to the content and the blur follows it. Point-blank the
  substitution stops reaching the blur computation. If it bothers you, limit
  the near blur: `Near Blur` in Depth of Field starting beyond the portal.

## 12. If the opening is empty

The module warns in the log about everything that is fixed by configuration: a
disabled quad renderer, a lost material, an unsupported shader, a material
without the `_MainTex` property.

If there are no warnings and the opening is black, check in order:

1. **Exit Portal** is assigned and linked both ways.
2. **Player Camera** is assigned and is the camera that draws the frame.
3. The portal's local **+Z** axis looks at the player, not away.
4. The quad is a direct child of the root, its local position and rotation are
   zero.

## 13. The sandbox

`Assets/portal/Examples/PortalSandbox.unity` is a scene for looking at the
portal with your own hands. Open it, press Play, walk.

Inside are two rooms: a cold grey-blue one where the game starts, and a warm
one with three coloured pillars. The difference in colour and light is
deliberate — it makes it obvious that the opening shows a different place, not
a reflection. A pair of portals facing each other stands separately: recursion
is visible in it. Crates with physics lie by the wall; they can be pushed
through the opening.

Controls: WASD, Shift — run, mouse — look.

The scene is rebuilt with **Tools → Portals → Build Sandbox Scene** if it
needs to be returned to its original state after experiments.
