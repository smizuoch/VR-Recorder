# FFmpeg 8.1.2 Windows test-only oracle SDK build recipe

This recipe builds the private demux/decode SDK used only by VR Recorder tests.
It is a separate root from the production encoder/mux SDK and is forbidden from
Release payloads, production native links, and staging manifests.

## Pinned inputs

- Source: `ffmpeg-8.1.2.tar.xz`
- Source SHA-256: `464beb5e7bf0c311e68b45ae2f04e9cc2af88851abb4082231742a74d97b524c`
- Compiler: MSVC `19.44.35228`, x64 host and target
- Windows SDK: `10.0.26100.0`
- POSIX build environment: MSYS2 with GNU Make

## Configure contract

```text
--prefix=<SDK_ROOT>
--toolchain=msvc
--enable-cross-compile
--host-cc=cl.exe
--arch=x86_64
--target-os=win32
--enable-shared
--disable-static
--disable-programs
--disable-doc
--disable-network
--disable-autodetect
--disable-everything
--disable-avdevice
--disable-avfilter
--disable-swscale
--disable-swresample
--disable-iconv
--disable-zlib
--disable-bzlib
--disable-lzma
--disable-debug
--disable-iamf
--disable-x86asm
--enable-avcodec
--enable-avformat
--enable-avutil
--enable-decoder=aac
--enable-decoder=h264
--enable-demuxer=mov
--enable-protocol=file
--enable-ffprobe
```

Run from PowerShell 7:

```powershell
pwsh -File eng/build-ffmpeg-windows-oracle-sdk.ps1 `
  -SdkRoot C:\absolute\private\ffmpeg-8.1.2-windows-msvc-x64-oracle
```

The builder verifies exact decoder, demuxer, protocol, and program sets; source
bytes; compiler/SDK versions; output filenames; and every artifact
length/SHA-256. Smart App Control may prevent local execution of the unsigned
`ffprobe.exe`; do not disable it. Preserve the artifact for the signed Windows
test gate.
