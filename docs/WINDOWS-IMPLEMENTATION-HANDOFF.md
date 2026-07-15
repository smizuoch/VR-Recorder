# VR-Recorder Windows実装引き継ぎ書

- 作成日: 2026-07-15
- 対象branch: `main`
- Windows開始baseline commit: `a2bb170` (`feat: convert Annex B H264 to avcC`)
- 現在の判定: 実装checkpoint。録画可能な配布製品でもrelease候補でもない
- 原則: t-wada式TDDで **Red → Green → Refactor → 回帰確認 → 1論理単位ごとにcommit**

## 1. Windowsで最初にやること

Windowsでは **MSIX、OpenVR overlay、Spout2実receiverから始めない**。
まず、Linuxでは完了できない actual Windows artifact 境界を閉じる。

推奨順は次のとおり。

1. **Windows baselineを再確認する**
   - `main` が `a2bb170` 以降であることを確認する。
   - portable Windows x64 build / CTestを先に通す。
2. **actual Windows FFmpeg 8.1.2 SDK evidenceを作る**
   - production SDKはdecoder/programなし、LGPL-only、shared、MSVC x64。
   - `aac` と `h264_mf` を含める。
   - production SDKへ test-only oracle / decoder / `ffprobe.exe` を混ぜない。
3. **test-only demux/decode oracle / ffprobe相当artifactを別buildする**
   - production SDKと同じFFmpeg 8.1.2 source identityを使う。
   - MOV demux、AAC decoder、H.264 decoder、ffprobe相当を持つ。
   - Release payload / staging manifest / production DLLへ混入させない。
4. **Legal / hash / build evidenceを揃える**
   - source archive SHA-256
   - build recipe
   - configure arguments
   - import library / DLL / tool executable hash
   - license / notice / source offer情報
   - placeholder hashや仮approvalは禁止。
5. **production media variantへAAC sourceを接続する**
   - actual artifact evidenceが揃った後だけ行う。
   - `src/VRRecorder.Native/CMakeLists.txt` にliteralでsource/linkを記述する。
   - `FFmpegContractTest::*`、ambient `find_package`、unversioned library fallbackは禁止。
6. **H.264 software encoder Portへ進む**
   - `h264_mf(hw_encoding=0)` を最初の実H.264経路にする。
   - NVENC / AMF / QSVはD3D11 processor成立後まで実装しない。
7. **PreHeaderCoordinatorへ進む**
   - H.264 late extradataを同一production encoder contextから確定する。
   - throwaway contextのextradataを本番contextへ流用しない。

## 2. 現在までに完了済みの差分

Windows側で前提にしてよい最新commitは次の3つ。

| Commit | 内容 |
|---|---|
| `7b72463` | FFmpeg 8.1.2 source identityからtest-only out-of-process AAC demux/decode oracleを追加 |
| `c2aaf2f` | MP4 fragmentの`trun` duration + edit listからAAC presented sample countを検証 |
| `a2bb170` | portable Annex B H.264 SPS/PPS/IDR → avcC + length-prefixed AU converterを追加 |

重要な境界:

- AAC raw decoder出力はAAC frame単位のpaddingを含み得る。
- 3秒 / 48 kHz入力の長さ証拠は raw decoded sample数ではなく、MP4上の `presented_sample_count == 144000` を使う。
- portable H.264 converterは実装済みだが、`h264_mf` encoder Portは未実装。
- `ProductionMediaAacAttachment.cmake` はsource planとcanonical targetを検査するだけで、main DLLを変更しない。
- actual Windows FFmpeg / OpenVR / Spout2 artifactはまだ承認済みruntimeとして登録されていない。

## 3. Windows環境の前提

推奨環境:

- Windows 11 x64
- Visual Studio 2022 + MSVC x64 toolchain
- Windows SDK
- CMake 3.28系以上
- .NET SDK 10系（`global.json`準拠）
- Git
- PowerShell 7またはWindows PowerShell
- 作業pathは空白・非ASCII・長すぎるpathを避ける

推奨artifact root例:

```powershell
$ArtifactRoot = "C:\vrrecorder-artifacts"
$FfmpegProdRoot = "$ArtifactRoot\ffmpeg-8.1.2-msvc-prod"
$FfmpegOracleRoot = "$ArtifactRoot\ffmpeg-8.1.2-msvc-oracle"
$BuildRoot = "build\native-windows-x64-ffmpeg"
```

## 4. 最初の確認コマンド

PowerShellで実行する。

```powershell
git status --short
git log --oneline -8
```

期待:

- `git status --short` が空。
- `a2bb170 feat: convert Annex B H264 to avcC` が履歴にある。

portable Windows baseline:

```powershell
cmake -S . -B build/native-windows-x64 -A x64 -DBUILD_TESTING=ON
cmake --build build/native-windows-x64 --config Release --parallel
ctest --test-dir build/native-windows-x64 -C Release --output-on-failure
```

このbaselineが通らない場合、FFmpeg actual artifact作業へ進まない。

## 5. Work package W0: actual Windows FFmpeg artifact evidence

### 目的

production DLLへFFmpegをlinkする前に、actual Windows x64 artifactを固定する。

### Red

次を個別に失敗させるtest / CMake contract / compliance testを先に追加する。

- FFmpeg versionが8.1.2でない。
- source archive SHA-256が固定値でない。
- configure argumentsが契約と違う。
- GPL / nonfree / version3 / unexpected external libraryが有効。
- unversioned DLL / import libraryを参照する。
- `FFmpegContractTest::*` をproduction linkへ使う。
- ambient PATH / system FFmpegへfallbackする。
- production SDKへdecoder / oracle / `ffprobe.exe` が混入する。
- build evidence JSON、source archive、build recipe、runtime DLL hashのいずれかが欠落する。
- Legal admissionなしでApprovedGraphへ入る。

### Green

- production SDKは次のような用途に限定する。
  - `avcodec-62.dll`
  - `avutil-60.dll`
  - `swresample-6.dll`
  - 後続mux接続時は `avformat-62.dll`
  - MSVC import libraries
  - public headers
- test-only oracle SDKは別rootに置く。
- production payload / staging対象へoracleを入れない。
- hash / source / recipe / license evidenceを同じcommit系列で登録する。

### Commit粒度

推奨commit:

```text
build: admit pinned Windows FFmpeg SDK evidence
```

## 6. Work package W1: production AAC variant接続

### 前提

W0がGreenになるまで着手しない。

### Red

- `VRRECORDER_MEDIA_FACTORY_VARIANT=PRODUCTION` かつ `VRRECORDER_ENABLE_FFMPEG_ADAPTERS=ON` で、actual pinned SDKなしならconfigure失敗。
- `FFmpegContractTest::*` を指定したらconfigure失敗。
- alias target、non-imported target、unversioned pathならconfigure失敗。
- portable `UNAVAILABLE` variantにAAC sourceが混入したら失敗。
- production sourceとplaceholder sourceを同時linkしたら失敗。

### Green

`src/VRRecorder.Native/CMakeLists.txt` のWindows production blockへ、literalで書く。

- source:
  - `src/ffmpeg_aac_audio_pipeline.cpp`
  - `src/ffmpeg_aac_packet_encoder.cpp`
  - `src/ffmpeg_libavcodec_encoder_port.cpp`
- link:
  - `FFmpeg::avcodec`
  - `FFmpeg::avutil`
  - `FFmpeg::swresample`

注意:

- helper関数内の`${links}`展開でRepositoryNativeLinkVerifierを回避しない。
- actual SDK evidence / Legal admission / staging manifestを同じcommit系列で揃える。

### 確認コマンド例

```powershell
$env:VRRECORDER_FFMPEG_ROOT = $FfmpegProdRoot
cmake -S . -B $BuildRoot -A x64 `
  -DBUILD_TESTING=ON `
  -DVRRECORDER_ENABLE_FFMPEG_ADAPTERS=ON `
  -DVRRECORDER_FFMPEG_ROOT="$env:VRRECORDER_FFMPEG_ROOT" `
  -DVRRECORDER_MEDIA_FACTORY_VARIANT=PRODUCTION
cmake --build $BuildRoot --config Release --parallel
ctest --test-dir $BuildRoot -C Release --output-on-failure
```

### Commit粒度

```text
feat: attach AAC composition to Windows production media variant
```

## 7. Work package W2: H.264 software encoder Port

### 前提

- W0のactual FFmpeg SDKに `h264_mf` が含まれている。
- `a2bb170` のportable Annex B → avcC converterを作り直さない。

### Red

次を先にtest化する。

- `h264_mf` を名前で開く。
- `hw_encoding=0` を明示し、opened context readbackでsoftware modeを確認する。
- NV12 system-memory frame入力を受ける。
- open直後extradataが空でも失敗にせず、最初の実packetでSPS/PPS/IDRを取得する。
- Annex B packetをavcC + length-prefixed AUへ変換する。
- SPS寸法がrecording canvasと違えば拒否する。
- malformed NAL / missing SPS / missing PPS / conflicting parameter set / overflowをfail-closed拒否する。
- EAGAIN、0 packet、複数packet、drain EOF、AbortをAAC state machine同等に扱う。

### Green

- system-memory synthetic NV12 frameからH.264 packetを生成する。
- 最初のIDRからavcC descriptorを確定する。
- 変換後のpacketだけをAVCC-only mux portへ渡す。
- decoder / ffprobe oracleで寸法、frame count、presentation startを検証する。

### 明確な非対象

この段階では以下を実装しない。

- NVENC
- AMF
- QSV
- D3D11 texture input
- same-adapter hardware binding

### Commit粒度

小さく分ける。

```text
test: require software H264 MF context contract
feat: encode synthetic NV12 with software H264
feat: derive H264 descriptor from first IDR packet
```

## 8. Work package W3: PreHeaderCoordinator

### 着手条件

- W1 production AAC componentがGreen。
- W2 software H.264 packet + avcC descriptorがGreen。

### Red

- H.264 descriptor確定前はmux headerを開始しない。
- descriptor確定前のAAC packetをbounded queueへ保持する。
- header後にA/V packetをDTS順でexactly once flushする。
- header失敗、Abort、queue上限超過、worker生成失敗ではpending fileを公開しない。
- throwaway encoder context由来extradataを本番contextへ流用したら失敗。

### Green

状態機械は次で固定する。

```text
Created -> Priming -> HeaderReady -> DrainingPreHeader -> Running -> Finishing
```

完了条件:

- software H.264 + AACで3秒scratch fragmented MP4を作る。
- ffprobeとdecode oracleを通す。
- まだVRChat / Spout / OpenVR実機合格とは呼ばない。

## 9. Windowsでまだ始めないもの

次はまだ始めない。

- MSIX / Store packaging
- OpenVR overlay / wrist renderer
- Spout2 receiver
- NVENC / AMF / QSV
- full-production staging manifest v2
- Hardware Validation Payload合格証跡

理由:

- まずdesktop操作で3秒MP4を確定できるproduction録画経路を作る必要がある。
- GPU / VR / SteamVRの失敗原因を録画backendの失敗原因と混ぜない。
- manifest v2やpost-publish sealerはactual artifact identity確定後に追加する。

## 10. Commit / test運用

各論理単位で必ず行う。

```powershell
git diff --check
# 対象test
ctest --test-dir <build-dir> -C Release -R '<target-test-name>' --output-on-failure
# 影響範囲test
ctest --test-dir <build-dir> -C Release --output-on-failure
# 必要に応じてmanaged
dotnet test tests/VRRecorder.Compliance.Tests/VRRecorder.Compliance.Tests.csproj --configuration Release
```

commit message例:

```text
test: reject ambient FFmpeg production fallback
build: admit pinned Windows FFmpeg SDK evidence
feat: attach AAC composition to Windows production media variant
test: require software H264 MF context contract
feat: encode synthetic NV12 with software H264
```

1つのcommitに混ぜないもの:

- FFmpeg artifact admissionとH.264 encoder実装
- H.264 softwareとNVENC/AMF/QSV
- D3D11 processorとSpout2 receiver
- recording backendとOpenVR overlay
- staging manifest v2とMSIX packaging

## 11. 失敗時の切り分け順

1. portable Windows ABI / CTestが壊れていないか。
2. actual FFmpeg SDK import targetがcanonicalか。
3. DLL / import lib / header / evidence hashが一致しているか。
4. Legal admission / ApprovedGraph由来か。
5. production sourceとplaceholder sourceが同時linkしていないか。
6. `FFmpegContractTest::*` やsystem FFmpegへfallbackしていないか。
7. H.264 packetがAnnex Bのままmuxへ渡っていないか。
8. H.264 descriptorが同一encoder contextの最初の実packetから作られているか。

## 12. Windows担当者への短い開始指示

```text
mainのa2bb170以降をbaselineにして、まずWindows portable Release CTestを通してください。
次にactual FFmpeg 8.1.2 MSVC x64 production SDKと、別rootのtest-only oracle/ffprobe SDKを作り、source/hash/build recipe/Legal evidenceを揃えてください。
actual evidenceが揃うまでmain DLLへFFmpegをlinkしないでください。
Evidenceが揃ったら、production media variantだけにAAC 3 sourceとFFmpeg::avcodec/avutil/swresampleをliteralに接続し、FFmpegContractTestやambient FFmpeg fallbackをRedで拒否してください。
その後、h264_mf(hw_encoding=0) のsoftware H.264 Portをsystem-memory NV12からRed→Greenで実装してください。
MSIX、OpenVR、Spout2、NVENC/AMF/QSVはまだ始めないでください。
```
