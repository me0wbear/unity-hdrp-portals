# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.0.0] - 2026-08-31

First public release.

### Added

- `Portal`, `PortalTraveller`, `PortalCameraBridge` and `PortalBudget` components.
- Recursive rendering: a virtual camera per level, each into a screen-sized
  target, sampled by screen-space UV so the opening matches the frame pixel for
  pixel.
- Composite in frame: the virtual camera renders without post-processing and
  without exposure control, and the quad hands the result to HDRP as emissive.
  The main camera's exposure, tonemapping, bloom and antialiasing then apply to
  the opening and its surroundings together.
- Content depth written into the main camera's depth buffer, so fog, depth of
  field and ambient occlusion work off the distance to what is visible through
  the opening rather than the distance to the quad.
- Oblique near-plane clipping against the exit portal's plane.
- Aperture handling: the quad is pushed back along the portal normal when the
  viewer comes closer than the near clip plane would allow, so the opening never
  shows a gap in the last centimetres.
- Distance-based crossing detection, which keeps working while a
  `CharacterController` is disabled and trigger events are silent.
- Clones with plane slicing during the crossing, driven by `_SliceCentre`,
  `_SliceNormal` and `_SliceEnabled` material properties.
- Volume blending across the opening, so two rooms with different grading do not
  snap at the moment of crossing.
- Optional Cinemachine integration in a separate assembly, compiled only when
  the package is present.
- Optional UHFPS bridge through reflection, with no compile-time dependency.
- `Portal.prefab`, `PortalPair.prefab` and `PortalPlayer.prefab`.
- Sandbox scene at `Assets/portal/Examples/PortalSandbox.unity`.
- Editor tools under **Tools → Portals**: create a pair, wire the scene, validate
  the scene, rebuild module assets, rebuild prefabs, build the sandbox scene.
- Test lab under `Assets/LabTools`: 34 unit tests and scripted scene checks that
  build a player and measure the frames.

### Known issues

- Two portals placed face to face overwrite each other's view.
- Motion vectors inside the opening belong to the quad rather than to the content.
- Depth of field over-blurs the opening in the last centimetres before crossing.
- The Seam check does not build, failing during scene serialization.

[Unreleased]: https://github.com/me0wbear/unity-hdrp-portals/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/me0wbear/unity-hdrp-portals/releases/tag/v1.0.0
