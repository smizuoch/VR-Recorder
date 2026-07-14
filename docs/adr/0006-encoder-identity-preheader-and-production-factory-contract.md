# ADR-0006: vendor encoder identity、late extradata、production factoryを分離して固定する

- Status: Accepted
- Date: 2026-07-14

## Context

公開仕様、Domain、設定UI、native ABI、診断logは`Nvenc`、`Amf`、`Qsv`、`MediaFoundationSoftware`を別のencoderとして扱う。一方、現在のpinned FFmpeg 8.1.2 SDK contractはnative AACと`h264_mf`だけを有効にし、production DLLはmedia、encoder probe、Spout、SteamVRの4 factoryすべてを`unavailable_*`へ接続している。

FFmpeg 8.1.2のMedia Foundation encoderはhardware指定時に`MFTEnumEx`でMFTを列挙する。特定adapterを列挙する`MFTEnum2`／`MFT_ENUM_ADAPTER_LUID`を使わないため、Spout textureと同じadapterのhardware MFTを選んだことを証明できない。D3D managerを後から渡すことは、選択済みMFTのidentity証明にはならない。

また`h264_mf`はglobal header指定時にもextradataがopen直後に得られない場合がある。pinned source自身が最大約70 ms待っても取得できず、SPS／PPSがframeへprependされるencoderがあることを明記している。現在の録画開始順はcomplete descriptorでmux headerを書いてからvideo／audio workerを開始するため、このlate extradataを扱えない。

依存物の面でも、FFmpeg 4 DLLをRelease outputへ明示配置するだけでは十分ではない。actual binary、source、build recipe、license、hashが承認されたruntime closureと、production factoryが本当に選択されたことを一つのpayload証拠へ結び付ける必要がある。

## Decision

### 1. 公開encoder名と実backendを一致させる

次の対応を固定する。

| Public kind | FFmpeg encoder | Hardware |
|---|---|---:|
| `Nvenc` | `h264_nvenc` | yes |
| `Amf` | `h264_amf` | yes |
| `Qsv` | `h264_qsv` | yes |
| `MediaFoundationSoftware` | `h264_mf` with `hw_encoding=0` | no |

- `Auto`はSpout sourceのadapter LUIDからvendorを特定し、そのvendor backendだけを最初にprobeする。失敗時だけMedia Foundation softwareへ進み、別vendor hardwareへ横滑りしない。
- 利用者がvendor backendを固定指定した場合は、失敗しても黙ってsoftwareへfallbackしない。
- requested preferenceではなく、実際にpacketを生成したbackend identityをUI、診断、recording evidenceへ保存する。
- `h264_mf(hw_encoding=1)`をNVENC／AMF／QSVと表示して使わない。

将来dependency削減のためMedia Foundation hardwareへ統一する場合は、公開kindを`MediaFoundationHardware`へ変更し、adapter-awareな`MFTEnum2`実装またはFFmpeg patch、MFT identity evidenceを別ADRで承認する。

### 2. same-adapterはLUIDとdevice ownershipで証明する

production hardware経路は次を満たす。

1. Spout senderから得たnon-zero adapter LUIDと一致するDXGI adapterを一意に取得する。
2. そのadapterから録画sessionが所有する一つの`ID3D11Device`を作る。
3. 同deviceをFFmpegのD3D11 hardware-device contextへ明示設定する。文字列adapter indexやprocess default adapterへ任せない。
4. processorは同device上のNV12 frameを作る。
5. NVENCとAMFは同一D3D11 deviceのsurfaceを各vendor sessionへ渡す。QSVは同deviceをchild deviceとしてderiveしたQSV `AVHWFramesContext`へNV12 frameをmapし、`AV_PIX_FMT_QSV` frameとしてencoderへ渡す。`h264_qsv`へ`AV_PIX_FMT_D3D11`を直接渡さない。
6. source、processor、frame、encoder selectionのLUIDをopen時とframe投入前に照合する。

device removed／reset、sender texture再作成、LUID変更時は旧surface、processor、encoder context、probe cacheを再利用しない。Media Foundation software encoderはsystem-memory NV12を受けるが、source／processor adapter identityの証拠は別に保持する。

### 3. probe成功は実H.264 packetの検証で決める

初期化成功だけではprobe成功にしない。既存ABIの16 synthetic frameを使い、次をすべて満たしたときだけ`PacketProduced`とする。

- non-empty packetが得られる。
- H.264 access unitをparseできる。
- SPS、PPS、IDRが揃う。
- SPSのcoded pictureとcrop／conformance windowを検証し、crop適用後のdisplay dimensionsが要求値と一致する。1080pのcoded heightが1088になり得ることを許容する。
- profile、pixel format、fps、B-frame 0のproduction optionをreadbackできる。
- hardware backendではsource／processor／encoderのadapter LUIDが一致する。software backendではencoderをsystem-memoryとして識別し、source／processor LUIDとの一致だけを検証する。

probe evidenceはactual backend kind、FFmpeg codec name、hardware/software、adapter LUID、vendor/device ID、driver identity、FFmpeg build identity、opened profile／format／dimensions／fps、packet検証結果を所有する。positive cache keyにもこれらとquality設定を含める。Abort／cancellation winner後の遅延packetを成功として保存しない。

現在の`vrrec_encoder_probe_v1`は`out_packet_produced`だけを返すため、このevidenceを表現できない。既存v1 ABIを破壊せず、size付きresult structとsize-query／caller-owned文字列bufferを使うversioned probe result APIをRedから追加し、managed `IEncoderProbe`もboolean相当ではなくstructured resultを返す。release evidenceは新APIのactual resultだけから作り、requested kindから推測しない。

### 4. late extradataはA/V共通の`PreHeaderCoordinator`が所有する

既存`MediaMuxPipeline`以下は「complete descriptorを受け、header後のpacketだけを書く層」として維持する。empty H.264 extradata拒否を緩めない。

新しい単一ownerを両encoder sinkと`MediaMuxPipeline`の間へ置く。

```text
PacketVideoEncoder -> MuxingVideoEncoderSink --\
                                                 PreHeaderCoordinator
PacketAudioEncoder -> MuxingAudioEncoderSink --/        |
                                                   MediaMuxPipeline
```

状態を次に固定する。

```text
Created -> Priming -> DescriptorReady -> HeaderStarted
        -> DrainingPreHeader -> Running -> Finishing
```

- videoとaudioを別々のbounded owned queueへ保持する。
- video descriptorはpacketを生成する同一production encoder contextから得る。throwaway contextのextradataを本番headerへ流用しない。
- open時extradataが空なら、最初の実frameからSPS／PPS／IDRを取得し、Annex Bからvalidated avcCを構築する。最初のpacketを捨てない。
- descriptor、両producer startup、audio descriptorが揃うまでheaderを書かない。
- header成功直後にmutex下でadmission cutを一度だけ確定する。cut以前のA/V packetをDTS順の一つのmixed batchとしてexactly once drainし、同DTSはvideo、audioの固定順とする。
- cut後のpacketはlive backlogへ置き、pre-header drain成功後に通常経路へ送る。将来packetまで含む無制限global DTS順序はstream watermarkなしに主張しない。
- queue limitはstream別packet数、payload＋side-data bytes、DTS spanで制限する。batch全体をpreflightし、部分追加しない。
- header failure、queue overflow、encoder completion-before-readiness、Abortでは両queueを破棄し、blocked callerを起床し、packet count、first-packet event、drift statistics、final fileをcommitしない。

既存submission Portの`Written`は実mux書込み完了を意味する。この意味を崩さないため、pre-header submissionはowned-copy後もticketが実際にdrainされるまで同期blockする。bufferへ入れただけで`Written`を返す非同期設計は採用しない。

録画sessionは単一のmonotonic capture epochを一度だけsampleし、videoとaudioへ同じ値を渡す。AAC primingだけがepochより負へ伸びる。`PreHeaderCoordinator`はtimestampをshiftせず、既にcanonical microsecondsへ正規化されたpacketだけを扱う。

### 5. production dependencyとruntime stagingはactual artifactで閉じる

Windows pinned FFmpeg buildは最終的にnative AAC、`h264_mf`、`h264_nvenc`、`h264_amf`、`h264_qsv`をexact allowlistにする。`--disable-autodetect`を維持し、採用したNV codec headers、AMF headers、oneVPLをsource commit、archive hash、license、build evidenceへ含める。

現時点の調査候補はnv-codec-headers `n13.0.19.0`、AMF `v1.5.2`、oneVPL `v2.17.0`である。ただしWindows MSVCでFFmpeg 8.1.2の再現build、import table、runtime closure、法務reviewが完了するまではcanonical registryへapproved entryや仮hashを入れない。現在の`PinnedFFmpeg.cmake`を根拠なく候補値へ書き換えない。

NVIDIA／AMD codec runtimeはGPU driverからloadし、vendor driver DLLをpayloadへ同梱しない。oneVPL dispatcherをdynamic linkする場合はdispatcher DLLだけを明示的なpayload artifact、SBOM、Legal Bundleへ登録し、Driver Storeのimplementation runtimeと区別する。

最終staging manifestは少なくともfirst-party native DLL、FFmpeg 4 DLL、ffprobe、OpenVR runtimeとmanifest／bindings、採用方式に応じたSpout artifact、factory-selection evidenceを列挙する。source／target relative path、role、component ID、platform、deployment kind、SHA-256を持たせる。承認状態はmanifest内のbooleanではなく、canonical registry、approved component graph、source/build evidence、actual hashから導出する。

### 6. native factoryはfamilyごとのexactly-one selectorにする

CMakeは次の4 cache enumを持ち、各値を`UNAVAILABLE`または`PRODUCTION`だけに制限する。

- `VRRECORDER_MEDIA_FACTORY_VARIANT`
- `VRRECORDER_ENCODER_PROBE_FACTORY_VARIANT`
- `VRRECORDER_SPOUT_FACTORY_VARIANT`
- `VRRECORDER_STEAMVR_FACTORY_VARIANT`

各familyはexactly one sourceをlinkする。未知値、empty、選択source欠落はconfigure failureにする。media／encoder probeのproduction選択はpinned FFmpeg SDK importを必須とする。

段階的bring-upではfamilyごとのproduction化を許すが、unpackaged hardware-validation payloadは`VRRECORDER_REQUIRE_FULL_PRODUCTION_FACTORIES=ON`を必須とし、4 familyの一つでも`UNAVAILABLE`なら構成を拒否する。configure時のselection intent digestをgenerated C++ sourceとしてnative binary内へ埋め込む。build後のwriterはactual binaryからexact markerを読み戻した場合だけ、DLLのfilename、length、SHA-256へ結び付けた`native-factory-selection-<config>.json`を生成する。validな別intentへの差替え、marker欠落、binary hash不一致を拒否し、後段staging gateはbinary hashと選択sourceを検査する。

selection evidenceが証明するのはlink対象のfamily／sourceとbinary bytesの対応までである。`production_*.cpp`が実adapterを構成することや、実環境でpacketを生成することは、注入可能Portを使うfactory composition test、versioned probe result、hardware validationで別に証明する。production sourceが`unavailable_*`へ委譲する実装をselection filenameだけで合格にしない。

portable C ABI smokeは引き続き`BACKEND_UNAVAILABLE`を要求する。production smokeはexport／argument validationと各factory composition testへ分離し、SteamVR停止中やSpout sender不在という正常な環境依存状態を「production source不在」と混同しない。

## TDD sequence

1. factory enumのempty／unknown／combined値、source欠落、partial full-productionをRedにする。
2. portable targetが4 unavailable sourceだけを一度ずつ選び、full-productionがplaceholderを一つも選ばないことを固定する。既存`BACKEND_UNAVAILABLE` C header smokeはportable variantだけに配線し、productionではexport／argument validation smokeと注入可能Portによる4 factory composition testを別targetにする。
3. vendor encoder allowlistとexternal SDK identityの不足をWindows production configureでRedにする。
4. versioned structured probe result ABI／managed resultを先にRedにし、same-adapter owner、16-frame packet probe、requested／actual identityを固定する。
5. late extradataの両stream buffering、header exactly once、DTS drain、Abort winner、queue overflowを決定的thread testでRedにする。
6. validated runtime-staging manifestのmissing／extra／major drift／hash mismatch／link／path traversal／partial copyをRedにする。
7. actual Windows binary、hardware matrix、ffprobe／decode evidenceが揃った後だけfull-production payloadをGreenにする。

## Consequences

- 公開encoder名と実装が一致し、`h264_mf` hardwareをvendor名で誤表示しない。
- multi-GPU環境でsame-adapterをLUIDとdevice ownershipから検証できる。
- late extradataを扱っても最初のvideo packetや先行audioを失わず、header failure後の偽packet統計を防げる。
- production factory sourceが未実装の間、`PRODUCTION`選択は明示的にconfigure失敗する。portable buildとisolated FFmpeg adapter testは継続できる。
- vendor SDKとruntime stagingの作業は増えるが、actual artifact未確定のplaceholder hashやambient PATH依存をreleaseへ混入させない。

## Sources

- FFmpeg 8.1.2 Media Foundation encoder: <https://github.com/FFmpeg/FFmpeg/blob/n8.1.2/libavcodec/mfenc.c>
- FFmpeg 8.1.2 Media Foundation enumeration: <https://github.com/FFmpeg/FFmpeg/blob/n8.1.2/libavcodec/mf_utils.c>
- FFmpeg 8.1.2 configure dependency graph: <https://github.com/FFmpeg/FFmpeg/blob/n8.1.2/configure>
- FFmpeg 8.1.2 NVENC adapter: <https://github.com/FFmpeg/FFmpeg/blob/n8.1.2/libavcodec/nvenc.c>
- FFmpeg 8.1.2 AMF adapter: <https://github.com/FFmpeg/FFmpeg/blob/n8.1.2/libavcodec/amfenc.c>
- FFmpeg 8.1.2 QSV adapter: <https://github.com/FFmpeg/FFmpeg/blob/n8.1.2/libavcodec/qsvenc.c>
- Microsoft `MFTEnum2`: <https://learn.microsoft.com/en-us/windows/win32/api/mfapi/nf-mfapi-mftenum2>
- Microsoft `MFT_ENUM_ADAPTER_LUID`: <https://learn.microsoft.com/en-us/windows/win32/medfound/mft-enum-adapter-luid>
- NVIDIA NVENC DirectX 11 session: <https://docs.nvidia.com/video-technologies/video-codec-sdk/13.1/nvenc-video-encoder-api-prog-guide/index.html>
- Intel oneVPL hardware acceleration: <https://intel.github.io/libvpl/latest/programming_guide/VPL_prg_hw.html>
- AMD AMF license: <https://github.com/GPUOpen-LibrariesAndSDKs/AMF/blob/master/LICENSE.txt>
- Intel oneVPL license: <https://github.com/intel/libvpl/blob/main/LICENSE>
