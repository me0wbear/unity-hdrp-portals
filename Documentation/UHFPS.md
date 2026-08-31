# Portals and UHFPS

How to wire the module to a UHFPS player, what the camera bridge does on a
crossing, and how to break down its two characteristic failures: the view
snapping to a wrong direction after a crossing, and a blink on the crossing
frame.

The module does not reference UHFPS and compiles without it. Everything below
works through reflection against the type names `UHFPS.Runtime.LookController`
and `UHFPS.Runtime.PlayerStateMachine`, and is verified on stubs that copy the
real class names and signatures. **The bridge has not been confirmed on the
live asset** — which is why it logs everything it finds and fails to find, and
lets you pick the reconciliation mode without touching code.

## 1. Preparing the player

There is no ready UHFPS player prefab in the module, and there cannot be one:
a prefab cannot carry components of an asset the project does not reference,
and the traveller and the bridge must sit on the root the UHFPS controller
moves. A menu item stands in for the prefab.

Select the UHFPS player root and run
**Tools → Portals → Prepare UHFPS Player**. It performs the manual steps from
sections 5 and 6 of [`SETUP.md`](SETUP.md): adds `PortalTraveller` and
`PortalCameraBridge`, finds the camera among the children and wires
**View Point**, **Traveller** and **Gameplay Camera**. Then run
**Tools → Portals → Wire Scene** so the portals get the same camera.

If there is no camera among the root's children, the menu item says so in the
log and leaves the camera fields empty — assign them by hand.

## 2. What the bridge does on a crossing

1. Carries the Cinemachine state across and closes the crossing with a hard
   cut.
2. Resets the HDRP frame history: the pipeline does not detect a teleport on
   its own.
3. Rotates the stored UHFPS state: the look angle and the world-space
   velocity.

The third point is governed by the **Uhfps Look Mode** field on the bridge,
and here is why.

## 3. Reconciling the look angle

UHFPS is built around an invariant: the player root rotation is always zero,
the direction lives in the camera's look rotation, and a non-zero root
rotation is transferred there by the asset itself. The portal, however,
rotates exactly the root on a crossing. How these two mechanisms combine on a
given version of the asset cannot be known in advance, so the mode is a
setting:

| Mode | What it does | When it is right |
|---|---|---|
| **Add Yaw Delta** | Adds the crossing rotation to the stored yaw, leaves the root alone | The controller rewrites the root from its stored angle every frame and eats the root rotation. The default |
| **Transfer Root Yaw** | Moves the root's resulting yaw into the stored angle and zeroes the root yaw | The asset transfers a non-zero root rotation itself, and with the additive mode the rotation doubles |
| **Do Not Touch** | Leaves the stored angle alone | For diagnosis: if the view is already correct like this, the asset reconciles on its own |

Picking by symptom after leaving a rotated portal:

- the view snaps back to its old world direction — keep **Add Yaw Delta** and
  check the binding log, see section 4;
- the rotation doubles — switch to **Transfer Root Yaw**;
- everything is correct — then **Do Not Touch** is enough and any other mode
  is redundant.

## 4. The binding log

On first activation the bridge writes one line per UHFPS member it intends to
work with:

- `UHFPS binding: LookController.LookRotation (field) will be adjusted on
  crossings` — the member is found and will be rotated;
- `UHFPS component ... was found, but member ... was not` — this version of
  the asset stores its state differently; the bridge will not touch it, and
  the view after a crossing will be turned by the stored angle. The prime
  suspect when the view snaps;
- `UHFPS type ... is present in the project, but no component of it was
  found` — the components sit under a different root than the bridge;
- no lines at all — the UHFPS types are not in the project and the bridge is
  dormant.

Members are looked up both as fields and as properties: asset versions differ.

## 5. The blink on the crossing frame

The frame history reset exists so the first frame after a teleport does not go
out with motion vectors from the old pose. But the reset has a price: it also
restarts the adaptation of automatic exposure. With auto exposure in the
scene, that reads as a blink exactly on the crossing frame. With fixed
exposure the reset is harmless — the module's sandbox uses fixed exposure and
shows no blink.

The trade is chosen with the **Reset Camera History** toggle on the bridge:

| Reset Camera History | Price |
|---|---|
| On | A one-frame blink under automatic exposure |
| Off | A one-frame motion smear across the whole frame |

If the blink bothers you and the smear is acceptable, turn the toggle off. If
you need both auto exposure and the reset, set the exposure to Fixed or Curve
at least in a volume around the portals. The grading difference between rooms
is carried by **Blend Volumes Through Portal** regardless — it does not need
auto exposure.

## 6. What to send if it does not work

A snap, a doubled rotation or a blink on the live UHFPS is exactly what the
bridge logs for. Three things are enough for a diagnosis: the binding lines
from section 4, the selected reconciliation mode, and the UHFPS version.
