# OpenVR 2.15.6 Windows x64 SDK preparation recipe

This recipe prepares the exact official OpenVR application SDK used by VR Recorder. It does not rebuild or modify Valve binaries.

## Pinned upstream input

- Release/tag: `v2.15.6`
- Source commit: `0924064316de3effbcd1acf1e309182a2deb1c05`
- Annotated tag object: `41bc3825fd35b04047610c86fee26fb33b017b29`
- Published: `2026-03-27T22:11:11Z`
- Archive URL: `https://codeload.github.com/ValveSoftware/openvr/tar.gz/refs/tags/v2.15.6`
- Archive length: `154998016`
- Archive SHA-256: `e184cb625010fab7043a9d5e1e000fdeb3067a152bb3169ef53f64dfac37164c`

The tag archive contains the application header, the official x64 MSVC import library, the official signed runtime DLL and detached signature, and the BSD-3-Clause license. The complete archive is retained as the source and binary offer input.

## Preparation

Run from PowerShell 7 on Windows:

```powershell
pwsh -NoProfile -File eng/prepare-openvr-windows-sdk.ps1 `
  -SdkRoot C:\VRRecorderDependencies\openvr-2.15.6-windows-x64
```

The script downloads the pinned tag archive when needed, verifies its length and SHA-256 before extraction, and copies only:

- `headers/openvr.h` to `include/openvr.h`
- `lib/win64/openvr_api.lib` to `lib/openvr_api.lib`
- `bin/win64/openvr_api.dll` to `bin/openvr_api.dll`
- `bin/win64/openvr_api.dll.sig` to `bin/openvr_api.dll.sig`
- `LICENSE` to `share/vrrecorder/licenses/OpenVR-LICENSE.txt`

It also retains the full archive, this recipe, and a byte-bound SDK evidence document. Preparation occurs in a sibling work directory and publishes by one rename only after every file and the exact inventory validate. Re-running against an existing destination performs validation without overwriting it.

## Link and runtime contract

Consumers import only `OpenVR::openvr_api`. The C++ SDK types remain private to the native adapter and never cross the versioned VR Recorder C ABI. The official `openvr_api.dll` is staged beside `vrrecorder_native.dll`; no Steam installation path, ambient `PATH`, or SteamVR-private copy may satisfy the production dependency.

The component remains candidate evidence pending an independent Legal review. SDK preparation and successful linking are not release admission.
