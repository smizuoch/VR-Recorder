# ADR-0008: OpenVR overlay pose contract

- Status: Accepted
- Date: 2026-07-16

## Context

The basic design requires controller-relative Wrist Dock placement, absolute
World Pin placement, drag release, two nudge sizes, recenter, and exact pose
readback. The persisted schema historically stored three position values and
three Euler values without defining their axes, units, multiplication order,
tracking origin, or comparison tolerance. Leaving those decisions to the
native adapter would make profile migration and hardware readback ambiguous.

The pinned OpenVR 2.15.6
[`openvr.h`](https://github.com/ValveSoftware/openvr/blob/v2.15.6/headers/openvr.h)
defines `HmdMatrix34_t` as a right-handed, metre-based transform with +X right,
+Y up, and -Z forward. It also distinguishes absolute transforms with an
explicit tracking origin from tracked-device-relative transforms.

## Decision

1. `OverlayTransform.Position` is `[x, y, z]` in metres in its parent space.
   No DirectX left-handed axis conversion is applied.
2. `RotationEuler` is `[pitchX, yawY, rollZ]` in degrees. Conversion to the
   row-major OpenVR 3x4 matrix uses `Rz(roll) * Ry(yaw) * Rx(pitch)`; translation
   occupies the fourth column.
3. Wrist Dock uses `SetOverlayTransformTrackedDeviceRelative` with the selected
   left or right controller. World Pin uses `SetOverlayTransformAbsolute` with
   `TrackingUniverseStanding`. Raw tracking space is never used.
4. Persisted v1 values remain lossless when read as v2 as long as their vectors
   contain three finite numbers. Runtime conversion rejects positions outside
   ±100 m instead of producing a non-finite float matrix.
5. Readback compares Euclidean translation distance and SO(3) angular distance,
   not raw Euler elements. The tolerances are 0.5 mm and 0.1 degree.
6. Nudge changes the parent-space X/Y offset. Small is 5 mm and large is 20 mm.
   Right/up are positive X/Y; left/down are negative X/Y.
7. Drag release changes Wrist Dock to World Pin at 120 mm or more from the dock
   pose. A pinned overlay returns to Wrist Dock at 80 mm or less. The 40 mm gap
   is hysteresis and prevents mode chatter near one threshold.
8. Recenter restores the design default `[0.03, 0.05, -0.08]` metres and
   `[25, 0, 10]` degrees for Wrist Dock. STOP priority over held nudge repeat is
   owned by the later interaction state machine, not by the pose math contract.

## Consequences

- Pure tests can prove axis signs, units, Euler order, nudge tokens, hysteresis,
  and readback tolerance without SteamVR or a controller.
- The native pose adapter only maps a validated `OpenVrMatrix34` and selected
  transform mode to pinned OpenVR calls; it does not invent coordinate rules.
- Hardware validation still must confirm controller model orientation, comfort,
  readability, and round-trip values for each HMD/controller/hand profile.
