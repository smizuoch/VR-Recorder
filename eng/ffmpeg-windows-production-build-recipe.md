# FFmpeg 8.1.2 Windows production SDK build recipe

This recipe builds the private candidate SDK used by VR Recorder. It does not
admit the result to a Release payload; repository Legal and artifact admission
remain separate gates.

## Pinned inputs

- Source: `ffmpeg-8.1.2.tar.xz`
- Source SHA-256: `464beb5e7bf0c311e68b45ae2f04e9cc2af88851abb4082231742a74d97b524c`
- Source patch: `patches/ffmpeg-8.1.2/0001-configure-redo-enabling-cbs-in-lavf.patch`
- Source patch SHA-256: `c8aca5fee1f02dbd1a1623de0333013e0c41fb691adf0ede3d4479ee32ac41c0`
- Upstream patch commit: `cec19d7ddf725896dfbf79a4c308550d83eab5ec`
- Upstream review: `https://code.ffmpeg.org/FFmpeg/FFmpeg/pulls/23039`
- Compiler: MSVC `19.44.35228`, x64 host and target
- Windows SDK: `10.0.26100.0`
- POSIX build environment: MSYS2 with GNU Make
- nv-codec-headers tag/commit: `n13.0.19.0` / `e844e5b26f46bb77479f063029595293aa8f812d`
- AMD AMF tag/commit: `v1.5.2` / `eadd00804d5f7e5cd8c85d540073198312870776`
- Intel VPL tag/commit: `v2.17.0` / `d77f9195cf495b937631607333288fd917ae8939`
- Intel VPL is built as a shared MSVC x64 dispatcher. Its `libvpl.dll` is
  copied into the FFmpeg SDK and is an explicit runtime dependency.

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
--enable-swresample
--enable-d3d11va
--enable-mediafoundation
--enable-ffnvcodec
--enable-nvenc
--enable-amf
--enable-libvpl
--enable-encoder=aac
--enable-encoder=h264_mf
--enable-encoder=h264_nvenc
--enable-encoder=h264_amf
--enable-encoder=h264_qsv
--enable-muxer=mp4
--enable-protocol=file
```

Run from PowerShell 7:

```powershell
pwsh -File eng/build-ffmpeg-windows-production-sdk.ps1 `
  -SdkRoot C:\absolute\private\ffmpeg-8.1.2-windows-msvc-x64-hardware `
  -NvCodecHeadersRoot C:\absolute\sources\nv-codec-headers-n13.0.19.0 `
  -AmfRoot C:\absolute\sources\AMF-v1.5.2 `
  -LibvplRoot C:\absolute\sources\libvpl-v2.17.0
```

The builder verifies the selected component macros, absence of programs and
decoders, the three vendor source commits, source and backport-patch bytes,
compiler/SDK versions, output filenames, and every artifact length/SHA-256
before returning the SDK root. The
patch is the upstream fix for an empty `cbs_type_table` in minimal mov/mp4
builds, which can trigger an MSVC internal compiler error.
