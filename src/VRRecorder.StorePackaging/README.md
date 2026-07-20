# VRRecorder Store Packaging

This packaging-only Windows Application Packaging Project intentionally has
no application `ProjectReference`. The checked-in manifest is a template;
`eng/build-store-msix.ps1` injects the real Partner Center identity and copies
an already validated, immutable `win-x64` application payload into the package
layout.

Do not build or publish `VRRecorder.App` from this project. A changed inner
application payload requires a new Hardware Validation Payload and hardware
validation report before another Store packaging candidate can be created.

After packaging, `.github/workflows/store-release-preflight.yml` consumes the
exact candidate plus packaged-hardware evidence and runs scratch signing,
sideload/UI Automation, WACK, and final payload scan. The separate
`store-public-release-gate.yml` accepts only hash-bound Partner Center
certification and private-flight reports. Neither workflow stores a PFX or
changes the unsigned Store candidate. The public gate targets the protected
`store-production` GitHub Environment, which must have independent required
reviewers configured in repository settings.
