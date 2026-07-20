# VRRecorder Store Packaging

This packaging-only Windows Application Packaging Project intentionally has
no application `ProjectReference`. The checked-in manifest is a template;
`eng/build-store-msix.ps1` injects the real Partner Center identity and copies
an already validated, immutable `win-x64` application payload into the package
layout.

Do not build or publish `VRRecorder.App` from this project. A changed inner
application payload requires a new Hardware Validation Payload and hardware
validation report before another Store packaging candidate can be created.
