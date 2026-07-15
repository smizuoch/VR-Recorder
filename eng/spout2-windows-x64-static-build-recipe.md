# Spout2 2.007.017 Windows x64 static SDK recipe

This recipe prepares the exact official Spout2 SDK inputs used by VR Recorder. It does not rebuild or modify Spout2 binaries.

## Pinned upstream inputs

- Release/tag: `2.007.017`
- Source commit: `f49e2f469f8cb25f559a6eaa61a3f5b8173fc100`
- Official binary asset: `Spout-SDK-binaries_2-007-017_1.zip`
- Binary asset URL: `https://github.com/leadedge/Spout2/releases/download/2.007.017/Spout-SDK-binaries_2-007-017_1.zip`
- Binary asset length: `3472666`
- Binary asset SHA-256: `695f20e3505fa0da51b2eb959af359f5d9e2c914bb9676e9118d19f6a5424bf4`
- Source archive URL: `https://github.com/leadedge/Spout2/archive/f49e2f469f8cb25f559a6eaa61a3f5b8173fc100.tar.gz`
- Source archive length: `4920448`
- Source archive SHA-256: `9d93cadc7fea63d3e8b26384da8f8f23982a06a07adb0363d75630a99ab1f8f1`

The source archive is retained for the source offer and license review. The binary asset supplies the official `MD` build. Only the x64 DirectX 11 static receiver surface is admitted.

## Preparation

Run from PowerShell 7 on Windows:

```powershell
pwsh -NoProfile -File eng/prepare-spout2-windows-sdk.ps1 `
  -SdkRoot C:\VRRecorderDependencies\spout2-2.007.017-windows-msvc-x64-static
```

The script downloads both pinned inputs when they are absent, verifies length and SHA-256 before extraction, copies the exact eight `SpoutDX` headers plus `SpoutDX_static.lib` and `Spout_static.lib` from the official `MD` directory, retains both upstream archives and the BSD-2-Clause license, and writes `share/vrrecorder/spout2-sdk-evidence.json`.

Re-running against an existing SDK validates every admitted byte and does not overwrite it. Preparation uses a sibling owned work directory and commits by renaming the completed directory only after validation.

## Link contract

Consumers import `Spout2::SpoutDX_static`. It links `Spout2::Spout_static` and the exact Windows system libraries declared by upstream:

`opengl32`, `kernel32`, `user32`, `gdi32`, `winspool`, `comdlg32`, `comctl32`, `advapi32`, `shell32`, `ole32`, `oleaut32`, `uuid`, `odbc32`, `odbccp32`, `d3d9`, `d3d11`, `dxgi`, `version`, and `winmm`.

No Spout DLL is staged. Spout SDK C++ types remain inside the private native adapter and do not cross the VR Recorder C ABI or domain boundaries.
