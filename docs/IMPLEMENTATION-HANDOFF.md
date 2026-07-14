# VR-Recorder 実装引き継ぎ書

- 更新日: 2026-07-14
- 対象branch: `main`
- 基準commit: `a54dc68`（この文書を追加するcommitの親）
- 現在の判定: 実装checkpoint。録画可能な配布製品でも、release候補でもない
- 配布方針: unpackaged self-contained `win-x64` payloadで実機検証し、同一payloadの合格後だけMicrosoft Store MSIX候補へ進める

## 1. 最初に読む結論

次回はMSIXやOpenVR overlayから始めない。最初にproduction録画経路を成立させる。

推奨順は次のとおり。

1. encoder方式、late-extradata handshake、Windows dependency／staging contractをADRとRed testで固定する。
2. production audio pumpを1024-frame AAC入力へ固定し、実AAC factoryへ接続する。
3. system-memory NV12からH.264 encoder Port、late extradata、Annex B→AVCC、SPS寸法検証を実装する。
4. 実encoder packetを実fragmented MP4 muxerへ接続し、3秒scratch fileをffprobe／decodeまで通す。
5. 実Spout2 receiverとD3D11 processorを実装する。
6. 既存WASAPI実装をWindows COM failure seamと実device試験で閉じる。
7. 上記を`CreateMediaBackend`、`CreateSpoutSourceBackend`、encoder probe factoryへ接続する。
8. privateなunpackaged self-contained `win-x64` directoryで、desktop操作によるWindows／GPU／VRChat録画をbring-upする。この段階はまだpromotion用の実機合格証拠にしない。
9. OpenVR input／overlay／renderer／haptics／move・pinを実装する。
10. 全機能を含むunpackaged directoryを再生成・再identity化し、Windows／GPU／VRChat／SteamVR／HMDの最終Hardware Validation Payloadとして合格させる。
11. その合格payloadだけを別projectでMSIXへ包む。

この順序は「OpenVRを後回しにしてよい」という意味ではない。OpenVR overlay、Wrist renderer、haptics、move／pinは明確な未実装release gateである。ただし、録画backendとoverlay backendを同時に立ち上げると、Windows／GPU／SteamVRの失敗原因が混ざる。まずdesktop操作で3秒MP4を確定できる状態を作り、その同じ状態機械へOpenVRを接続する。

## 2. 現在までに完了したこと

### 2.1 Native failure-boundary hardening

直近の論理commitは次のとおり。

| Commit | 内容 |
|---|---|
| `d7744ae` | AAC変換chunk境界と192 kbps metadata伝播 |
| `1c63227` | callback stack外へのAbort Join移動 |
| `83c3d66` | media worker graphへのcallback Abort伝播 |
| `a144319` | audio／video／recording sessionのAbort仲裁 |
| `ab6afca` | backend cleanup／stop thread生成失敗 |
| `1a7b00d` | C ABI Start／Stop transaction仲裁 |
| `a05f367` | audio capture worker thread生成失敗 |
| `b33220f` | audio／video encoding workerのAbort、event、統計commit境界 |
| `a54dc68` | Spout workerとvideo session Join helperのthread publication境界 |

現在固定済みの主な不変条件:

- thread生成OOM、internal failure、成功statusなのにnon-joinableを決定注入できる。
- Start中Abortをterminal winnerとして遅延threadへ転送し、publication完了前にcleanupを返さない。
- Abort後に成功復帰したWrite／Pollをpacket、frame、latency、first-packet、Faultedへcommitしない。
- callback内ではlogical `RequestAbort`だけを行い、物理Joinはcallback stack外のcleanup ownerが回収する。
- C ABI Start／Stop、backend stop、inner pipeline Start／Joinの結果を単一terminal winnerへ収束させる。
- Spout SenderLost／FailedとAbortが競合してもAbort結果を維持し、capture Abortを重複しない。

詳細なfailure matrixは[`NATIVE-PIPELINE-FAILURE-MATRIX.md`](NATIVE-PIPELINE-FAILURE-MATRIX.md)を正とする。

### 2.2 直近の検証証拠

`a54dc68`時点で次を確認した。

- CMake full build: 成功
- Linux native CTest: 43/43成功
- `spout_capture_worker`／`video_pipeline_session`: 通常構成で各100回反復成功
- 同2 target: ASan／UBSan＋leak検出構成で各30回反復成功
- `git diff --check`: 成功
- GCC TSan: test process開始前にhost固有の`unexpected memory mapping`で停止。TSan成功は主張しない

managed全件、real FFmpeg 47件、coverageの前回値は[`VALIDATION-REPORT.md`](../VALIDATION-REPORT.md)にある。今回のnative変更後にmanaged coverageとnative coverageは再採取していないため、古い数値を現在値として扱わない。

## 3. 現在の実装とplaceholderの境界

### 3.1 実装済みだがproduction未接続

- portable audio／video capture、normalize、mix、CFR、encoding、mux、recording session state machine
- 実FFmpeg 8.1.2 libavcodec AAC PortとAAC packet encoder
- 実FFmpeg 8.1.2 libavformat fragmented MP4 Port
- AAC descriptorのexact 192000 bit/s伝播、negative priming／edit list境界
- Windows用event-driven WASAPI source
- managed P/Invoke recording／Spout／SteamVR wrappers
- Wrist UIの状態projection、localization、Legal projection、input adapter
- OpenVR action manifestとIndex／Oculus Touch／Viveの録画toggle binding
- unpackaged EXE→MSIXのpromotion policyとfail-closedなidentity比較

既存`ffmpeg_aac_to_fragmented_mp4_integration_tests.cpp`はAAC descriptorとzero-packetのMOV headerを検証しており、実AAC packetをmux／demuxした証拠ではない。Work package A／Cでnonzero packet compositionを追加する。

### 3.2 明示的placeholder

production DLLは現在も次のfactoryをplaceholderへ接続している。

- `src/VRRecorder.Native/src/unavailable_media_backend.cpp`
- `src/VRRecorder.Native/src/unavailable_spout_source_backend.cpp`
- `src/VRRecorder.Native/src/unavailable_encoder_probe_backend.cpp`
- `src/VRRecorder.Native/src/unavailable_steamvr_input_backend.cpp`

いずれも意図的に`VRREC_STATUS_BACKEND_UNAVAILABLE`を返す。`VRRECORDER_ENABLE_FFMPEG_ADAPTERS=ON`はpinned SDKを検証・importするが、現時点の`src/VRRecorder.Native/CMakeLists.txt`は実AAC／libavformat sourceをproduction DLLへ追加しておらず、production compositionを成立させない。

したがって、portable unit testや隔離した実FFmpeg testがGreenでも、実アプリの録画成功を意味しない。

### 3.3 OpenVR／Wristの現在地

実装済み:

- action manifestの録画、mic、overlay表示、recenter、haptic action定義
- controller別recommended bindingのうち録画toggle。mic／overlay表示／recenter／hapticはmanifest定義済みだが、同梱bindingと実runtime検証は未完了
- install rootからのabsolute manifest path解決
- native digital-state ABIとmanaged async stream
- Wrist状態／Legal UIのViewModel相当projection

未実装:

- OpenVR SDK／`openvr_api` runtimeのpinned dependency、Legal admission、application `.vrmanifest`登録
- OpenVR SDKを呼ぶnative `SteamVrInputBackend`
- `SetActionManifestPath`、action set／action handle取得、`UpdateActionState`、実digital polling
- process単位のsingle OpenVR runtime owner。Appは現在recordとmic用に別々の`NativeSteamVrInputRuntime`を生成する
- `IVROverlay`の初期化、作成、表示、texture更新、event polling、破棄
- elapsed／resolution／FPS／signalを含むWrist snapshot、layout／hit-test、1024×512 BGRA renderer、glyph／icon atlas
- controller-relative Wrist Dock、absolute World Pin、pose readback
- drag、nudge、recenter、dock／pin commandのruntime適用
- haptic action handleと録画開始／停止／fault pulse
- first-run routerの`WristOverlayPlacement` production route
- 実SteamVR／HMD／controller試験

`Presentation.Wrist`があることと、VR内に描画できることは別である。現在はprojection contractまでであり、renderer／OpenVR resource ownershipは存在しない。

## 4. 未完了release gateの実装段階

この表のP0／P1／P2はfailure severityではなく、依存関係に基づく実装段階を表す。failure severityと個別境界の正本は`NATIVE-PIPELINE-FAILURE-MATRIX.md`である。P0は最初のdesktop EXE bring-upに必要、P1は最終Hardware Validation Payloadの合格前に必要、P2は合格payloadをMSIXへ昇格した後に必要、という意味である。P1／P2もreleaseを阻止するため、省略可能という意味ではない。

| 段階 | Gate | 現在の証拠 | 閉じるまでできないこと |
|---|---|---|---|
| P0 | production AAC接続 | 実AAC Portは隔離testのみ | 実録画backend |
| P0 | H.264 encoder／AVCC契約 | Port／factory／converter未実装 | MP4 video stream |
| P0 | hardware encoder identity方針 | DomainはNVENC／AMF／QSV、pinned FFmpegは`h264_mf`だけ | 正しいprobe／表示／fallback |
| P0 | 実encoder→mux composition | zero-packet headerと個別Portまで | 3秒MP4のdecode証明 |
| P0 | Spout2 receiver | C ABIとpumpのみ、factory placeholder | VRChat frame取得 |
| P0 | D3D11 processor | planとadapterのみ、GPU実体なし | RGBA shared texture→NV12 |
| P0 | WASAPI Windows contract | 実sourceはあるがCOM注入・実機証拠なし | Windows音声release gate |
| P0 | production media factory | `CreateMediaBackend`がplaceholder | EXEで録画 |
| P1 | encoder fallback／part rollover | 現実装はterminal Abort | 基本設計§11.2適合 |
| P1 | unpackaged hardware payload | promotion policyのみ | 実機証拠のidentity固定 |
| P1 | Windows hardware E2E | 未実施 | MSIX候補への昇格 |
| P1 | OpenVR input／overlay一式 | manifest／projectionのみ | Wrist操作・表示 |
| P1 | first-run setup 7／8 | Port境界のみ | setup完走 |
| P1 | coverage／mutation／UI Automation | 90%未達／未測定 | release quality gate |
| P1 | final Legal Bundle／承認 | candidate台帳、承認済みnative 0 | 配布・署名 |
| P2 | MSIX／Store submission | policyのみ、packaging projectなし | Store提出 |

## 5. 次回からのTDD実装順

各work packageは、Red commitまたは少なくともRedが実際に失敗することを確認してからGreenへ進める。複数外部adapterを一度に実装しない。

### Work package 0: Windows dependencyとencoder identityの確定

Work package AのRedはLinuxのcontract-test SDKで先に書けるが、Windows Greenにはactual SDKが必要である。並行して次を固定する。

1. 公式FFmpeg 8.1.2 sourceから、`PinnedFFmpeg.cmake`のexact configure contractに一致するWindows x64 shared SDKを再現生成する。
2. header、MSVC import library、major付きDLL、configure evidence、source archive、build recipe、全SHA-256を一つのcandidate evidenceにする。
3. pinned SDKは`--disable-programs`なので`ffprobe.exe`を生成しない。Appは起動時にffprobeを必須とするため、同一source identityからの別tool buildまたは別の再現可能な供給経路を定義し、version／source／binary hash／Legalを固定する。
4. production SDKはdecoderもprogramも含まないため、H.264／AACのdecode oracleをtest-only dependencyとして別にpinし、source／version／hashをCI evidenceへ残す。test oracleをproduction payloadへ混ぜない。
5. 採用するSpout2／OpenVRのexact tag／commit、source archive、license text、binary／source build方式を固定する。
6. missing file、version drift、unexpected library、GPL／nonfree／version3、未知external dependencyをそれぞれRedにする。
7. actual binaryがない段階でcanonical registryへplaceholderを登録しない。
8. CMakeでportable placeholder variantとproduction adapter variantをexactly-one選択にする。両方またはどちらも選ばれない構成はconfigure時に拒否する。
9. `VRRECORDER_ENABLE_FFMPEG_ADAPTERS=ON`なのに`unavailable_media_backend.cpp`が選ばれる現在の曖昧さをなくし、C ABI smokeをplaceholder variantとproduction variantに分ける。
10. Release publishでは`vrrecorder_native.dll`と`ffprobe.exe`だけでなく、versioned FFmpeg 4 DLL、採用するSpout2／OpenVR runtime、manifest／bindingsをapproved staging manifestからexactly取り込む。PATH上のDLLへ依存しない。

ここでencoder名称の仕様もADRで確定する。現在のDomain／UIは`Nvenc`、`Amf`、`Qsv`を公開する一方、pinned FFmpeg allowlistは`h264_mf`だけを有効化している。次のどちらかを明示的に選ぶ。

- vendor固有FFmpeg encoderを使うなら、SDK configure contract、headers、license／redistribution、D3D11 frame経路を追加審査する。
- `h264_mf`のhardware MFTを使うなら、利用者向け名称と`EncoderKind`を「NVENC／AMF／QSV」と断定せず、実際のMFT identity／adapter bindingをprobeして表現を改訂する。

現状のままvendor名を表示して`h264_mf`を呼ぶ実装はしない。probe成功条件は初期化ではなく、選択したSpout adapter上で実packetが出ることとする。

### Work package A: production AAC pipeline

Work package 0のADR／dependency contract Redを先に固定し、その判断と並行して最初のproduction codeをここから再開する。

#### Red

1. production audio input windowが常に1024 stereo frameであること。
2. active中は1024未満をencodeせず、Finish時だけsmall-last-frameを渡すこと。
3. native AAC factoryがexact 48 kHz／stereo／AAC-LC／192000 bit/s descriptorを返すこと。
4. zero packet bufferingを成功packetとして数えないこと。
5. factory／Encode／Finish失敗時にpartial batchをmuxへ公開しないこと。
6. Encode／Finish中Abortがpacket、statistics、first-packetをcommitしないこと。
7. production CMake targetがpinned FFmpeg以外を探索・linkしないこと。

#### Green

- `ffmpeg_aac_packet_encoder.cpp`をproduction compositionから生成するfactoryを追加する。
- `StereoAudioPipelineAdapter`へ1024-frame契約を渡す。
- muxing sinkへ実AAC descriptorとpacket batchを接続する。
- `VRRECORDER_ENABLE_FFMPEG_ADAPTERS=ON`時だけproduction DLLへ必要source／import targetを追加する。

#### 完了条件

- portable fake test、real libavcodec test、既存header test、新しいnonzero AAC packet→MOV mux／demux testがすべてGreen。
- Windows MSVC＋pinned SDKでproduction DLLがlinkする。
- placeholder以外のaudio compositionをfactory testから生成できる。

### Work package B: H.264 encoder Portとbitstream契約

最初はsystem-memoryのsynthetic NV12 frameでsoftware encoder経路を成立させる。D3D11 texture直結とhardware adapter bindingはWork package Dの後に追加する。これによりcodec／timestamp／AVCCの失敗とGPU共有resourceの失敗を分離する。

#### Red

1. `h264_mf` software context設定とopened-context readback。Work package 0でhardware方式を確定した後、同じcontractへhardware contextを追加する。
2. system-memory frame、後続のD3D11 frame、global-headerの各入力契約。
3. open直後にextradataがないlate-extradata経路。
4. Annex B SPS／PPS／IDRからavcCとlength-prefixed AUへの変換。
5. malformed NAL、missing SPS／PPS、同一IDで内容が競合するparameter set、length overflowをfail-closedで拒否する。同一bytesの反復はdedupeし、複数SPS／PPSはavcCのcount／size上限内で決定的に扱う。
6. SPS width／heightとrecording canvas不一致の拒否。
7. send／receiveのEAGAIN、0／複数packet、drain EOF、Abort。
8. side-data-only packetと未知packet flagを黙ってdropしないこと。

#### Green

- AACと同じportable encoder state machineへH.264実Portを接続する。
- software fallbackは基本設計どおり`h264_mf`の`hw_encoding=0`を使う。
- AVCC-only mux Portへ渡す前に明示変換／検証する。
- Annex B parser／SPS・PPS抽出／avcC builderはFFmpeg context ownershipから独立したportable moduleにする。

#### 完了条件

- synthetic frameから実H.264 packetを生成し、decoderで寸法／frame count／presentation startを確認する。
- Finish後frame、Abort後packet、invalid extradataを拒否する。

### Work package C: real encoder→fragmented MP4 composition

#### 先に固定するdescriptor-readiness state machine

現在の`MediaRecordingPipeline`はstream descriptorを構築時に受け取り、workerより先にmux headerを開始する。一方、`h264_mf`はOpen直後にSPS／PPS extradataを返さず、最初の実frameをencodeして初めて得られる場合がある。この順序を曖昧にしたまま実adapterを接続しない。

推奨状態は`Created -> Priming -> HeaderReady -> DrainingPreHeader -> Running -> Finishing`とし、次を一つの`PreHeaderCoordinator` ownerで行う。

1. Record Start時に単一monotonic capture epochを確定し、audio／video timestampを同じepochへ正規化する。
2. `Priming`でaudio capture／encode workerとvideo priming workerを開始するが、両streamのpacketはmuxへ渡さず、stream別のbounded queueへ所有して保持する。session Start成功とRunning eventはまだ公開しない。
3. 最初の実video frameを同一production encoder contextでencodeし、context extradata、または最初のAnnex B SPS／PPS＋IDRからavcCを導出して寸法とprofileを検証する。
4. 既知のaudio descriptorと確定したvideo descriptorでmux headerを1回だけ開始する。
5. `DrainingPreHeader`へ遷移し、drain中に到着したpacketも同じownerへ追加しながら、保留A/V packetをDTS順にexactly once submitする。
6. queueが空になった一点でlive submitへ切り替えて`Running`を公開する。header失敗、descriptor不成立、worker開始失敗、queueのtime／bytes／packet上限超過、Abortでは両queueを破棄してpending fileを公開しない。

throwaway encoder contextから取得したextradataを本番contextへ流用しない。descriptorは実packetを生成する同一contextから得る。

#### Red

1. Open成功時のempty extradataは`Priming`へ入り、header開始前は両streamのmux submitが0件である。
2. video descriptor確定前に到着したAAC packetを共通epochのbounded queueへ保持し、header後にvideoとDTS順でflushする。
3. 最初のSPS／PPS＋IDRからavcCを確定し、header成功後に保留packetをexactly once送る。
4. header失敗では両queueを破棄し、submit、packet statistics、first-packet event、Running event、final fileをcommitしない。
5. Priming／drain中Abortはterminal winnerとなり、遅延encode成功、header成功、drain完了をcommitしない。
6. EAGAIN、最初の0／複数packet、SPS／PPSのみ、IDR欠落、audio／video個別buffer上限超過、Finish-before-readyを決定的に処理する。
7. drain中にpacketが追加される競合でも順序欠落／二重送信なくlive modeへ一度だけ切り替える。
8. audio／video worker生成失敗ではpeerをAbort／Joinし、muxをAbort／closeしてpending fileを公開しない。
9. video／audio実packetから3秒以上のscratch fMP4を作る。
10. ffprobeでH.264、AAC-LC、48 kHz stereo、192 kbps、指定FPS／寸法を確認する。
11. decode後のvideo presentation start、audio priming、末尾sample数を確認する。
12. first-packet前の失敗file、header失敗file、trailer／flush失敗fileをpublishしない。
13. packet漏れ、二重送信、A/V batch interleave破壊がない。
14. `Finish`後Writeと失敗後retryを拒否する。

#### Green

- `MediaMuxPipeline`、両muxing sink、shared finalizationへ実Portを接続する。
- nativeはtrailer／flush／closeの結果を確定してmanagedへ返すところまでを所有する。
- managedの`RecordingFileFinalizationUseCase`がffprobe検証を行い、その成功後だけ`SameDirectoryAtomicRecordingFileFinalizer`でpending `.recording.mp4`をfinal pathへrenameする。nativeからrenameしない。

#### 完了条件

- ffprobeだけでなく実decodeを通す。
- test fileを人が再生できることは実機gateで別に確認する。

### Work package D: D3D11 processor

実adapterを書く前に`video_surface.hpp`の契約を広げる。現在の`VideoSurfaceAcquireResult::{Acquired, Timeout, Failed}`と`void ReleaseFromRead()`では、`WAIT_ABANDONED`、device removed／reset、`ReleaseSync`失敗を現在の呼出しへ返せない。また現在の`VideoEncodingPump`は`sink_.Write()`でencode／muxした後にsource surfaceをReleaseするため、戻り値を追加するだけではRelease失敗時のpacket commitを取り消せない。

#### Red

1. acquire resultに少なくとも`Acquired`、`Timeout`、`Abandoned`、`DeviceLost`、一般failureを表現し、recreate可能性とterminal faultをownerが決める。
2. `ReleaseFromRead()`はstatusを返し、`ReleaseSync`失敗を成功callとして返さない。
3. keyed mutex `WAIT_TIMEOUT`はbounded retry／frame drop、`WAIT_ABANDONED`はsurface破棄・再作成またはterminal faultにする。
4. Acquire成功後はprocessing成功／失敗／Abortの全経路でReleaseを1回行う。processing成功後のRelease失敗ではencoder／muxをまだ呼ばず、当該frame、event、statisticsをcommitせずrecordingをAbortする。
5. device removed／reset、adapter LUID変化、texture recreationを区別し、旧surfaceを再利用しない。
6. recreation中Abort、replacement生成失敗、旧／新resourceのexactly-once releaseを検査する。
7. odd source寸法、BGRA／RGBA channel order、SingleFileFit、NV12 output descriptorを検証する。
8. output handle／format／dimensions／LUID不一致をencoder前に拒否する。

#### Green

- Win32／D3D11 COM呼出しを薄いPortへ分離し、fake HRESULTとresourceを注入できるようにする。
- portable `VideoProcessingPlan`と`VideoProcessingEncoderSink`を再利用する。
- callを`Acquire source -> Process/copy into owned output -> Release source -> Encode output -> mux commit`へ分割する。Release成功前にpacketを不可逆commitしない。
- surfaceを再作成する所有者をSpout receiver、processor、encoderのいずれか一つに固定し、generation IDで旧frameを拒否する。

#### 完了条件

- synthetic D3D11 textureでGPU変換結果をreadback検証する。
- NVIDIA／AMD／Intelの実adapterでresource leak／device lossを確認する。

### Work package E: Spout2 receiver

#### Red

1. sender 0／1／複数、選択sender消失、再出現。
2. texture handle／寸法／format再作成。
3. adapter LUID変更。
4. poll timeoutとAbort、Poll中Abort後のlate SenderLost。
5. receiver、shared handle、texture、keyed mutexのexactly-once release。
6. 古いsurfaceをCFRへpushしない。

#### Green

- `SpoutSourceBackend`と`SpoutCaptureSource`の実adapterを追加する。
- Spout2 SDKの型をC ABI／Domainへ漏らさない。
- 実dependencyが得られるまで台帳へplaceholder hashを入れない。

#### 完了条件

- scripted sender test、実Spout demo sender、実VRChat senderの順に通す。

### Work package F: WASAPI seamと実機検証

既存`wasapi_audio_capture_source.cpp`を捨てず、外部COM境界を薄く切り出す。

#### Red

1. GetBuffer成功後の全分岐でReleaseBufferがpacket全体または0 frame。
2. GetBuffer／ReleaseBufferとservice releaseが同じcapture thread。
3. empty、silent、discontinuity、timestamp error、device invalidated。
4. default endpoint変更、replacement失敗後の再試行、最大5秒の再探索。
5. Abort中wait解除とCOM／event handleのexactly-once cleanup。

#### 完了条件

- Windows fake COM seamの決定的test。
- 実loopback＋microphoneで3秒capture、device抜去／privacy拒否／default切替。

### Work package G: production backend composition

#### Red

1. video／audio／muxの生成順とpartial-construction rollback。
2. Spoutまたはaudio片側Start失敗でpeerをAbort／Join。
3. first packet前のhardware encoder失敗では、hardware descriptorで開いたmux／pending fileをAbortして破棄し、同じlogical output reservationに新しいpending fileを作り、software descriptorのheaderから再開始する。opened muxへsoftware packetを混在させない。
4. first packet後のencoder failureで現在partを確定し、次partをsoftwareで開始。
5. native trailer／flush／close失敗、managed ffprobe／atomic rename失敗、disk full、callback Abort。
6. production factoryが成功時にplaceholderを通らず、portable buildだけが`BACKEND_UNAVAILABLE` smokeを満たす。
7. production buildが実factory sourceとplaceholder sourceを同時linkしない。

#### 仕様判断

基本設計§11.2はpart rolloverを要求している。推奨は仕様を維持して実装すること。初回の内部bring-upではterminal Abortを一時的に観測してよいが、Hardware Validation Payloadを「合格」にする前にfallback／rolloverを閉じる。仕様をterminal Abortへ変える場合は、コードだけでなく基本設計、error catalog、test list、UI説明を同じcommit系列で改訂する。

## 6. Unpackaged EXE実機検証

unpackaged実行は2回以上の段階に分かれる。production media backendを初めて動かすbring-up payloadは何度作ってもよいが、OpenVRを含む全release機能が揃うまではpromotion policyへ渡せる合格済みHardware Validation Evidenceを発行しない。MSIX候補が参照するのは、全機能実装後に再生成・再identity化して最終matrixを通したpayloadだけである。

### 6.1 先に実装する生成物

`docs/test-list/distribution.md`の未完了項目を次の順でRedにする。

1. self-contained `win-x64` publish directory generator。
2. existing canonical root directoryとroot内のnormalized relative EXE entrypointを読み取るadmission reader。
3. directory全体のcanonical inventory。
4. 実inventoryからのpayload identity生成。
5. hardware validation report schema／reader。
6. reportとpayload identityの完全一致検査。
7. 必須scenario一覧から合否を導出するvalidator。呼出し側が任意の`Passed` boolを渡せる現在のrecord contractは廃止する。
8. schema version不明、必須case欠落、duplicate case ID、unknown status、失敗case、artifact欠落、payload identity不一致を個別にRedにする。

payloadは`VRRecorder.App.exe`単体ではない。最低限、次を一体としてhash固定する。

- `VRRecorder.App.exe`
- managed assembliesとself-contained .NET runtime
- `vrrecorder_native.dll`
- pinned `avcodec-62.dll`、`avformat-62.dll`、`avutil-60.dll`、`swresample-6.dll`
- `ffprobe.exe`
- OpenVR／Spout2等の承認済みruntime dependency
- OpenVR application manifest、action manifest、bindings
- UI assets
- authenticated Legal Bundle／SBOM／source offer

現在の`VRRecorder.App.csproj`はRelease時に`vrrecorder_native.dll`と`ffprobe.exe`しか必須化していない。上記全runtimeをapproved staging manifestからcopyし、missing、余分なDLL、major不一致をbuild前に拒否する。

canonical inventory entryは少なくともnormalized relative path、byte length、SHA-256、artifact kindを持つ。sort順、path separator、case policy、symlink／reparse point拒否をtestで固定する。Legal Bundle／runtime dependencyを1 byteでも変更した場合は別payload identityとなり、以前の実機証拠を無効にする。

identityは二層に分ける。

- `ApplicationPayloadIdentity`: Hardware Validationを受けるinner application directory。これを生成したapplication source revision、product version、RID、EXE hash、全inventory digest、Legal anchorsを持つ。
- `PackagingCandidateIdentity`: packaging wrapper revision、Partner Center identity、manifest hash、outer package／upload hashと、参照する`ApplicationPayloadIdentity`を持つ。

Hardware Validation合格後にpackaging projectだけを追加／修正してrepository HEADが変わっても、凍結済みapplication artifactを再buildせずそのまま入力にする限りHILを無効にしない。application payloadを生成するsource、build input、inventory bytes、Legal anchorsのいずれかが変わった場合は新しい`ApplicationPayloadIdentity`として全Hardware Validationをやり直す。

payload生成／inventoryのRedは少なくとも次を含める。

- clean Windows checkoutからlocked restore、Release self-contained publish、approved dependency staging、inventory生成までを一つの非対話commandで再現する。
- 同じcommit／toolchain／dependency／Legal入力から2回clean buildし、canonical inventoryが一致する。差が出たfileはtimestamp等を無視して隠さず、reproducibility failureとして特定する。
- `/`区切りのrelative path、`.`／`..`／absolute／ADS拒否、Windows case-fold後の重複拒否、reparse point拒否を固定する。
- root不存在／non-directory、entrypoint absolute／traversal／root外、entrypoint不存在／non-regular／reparse、entrypointがinventoryにない、entrypoint hash不一致を個別に拒否する。
- product version、source revision、RID、EXE hash、inventory digest、Legal Bundle ID／manifest hashをbuild output／approved evidenceから生成し、callerの任意文字列をrelease identityにしない。
- staging manifestにないfile、expected file欠落、runtime major不一致、ambient PATHから解決されたDLLを拒否する。

### 6.2 実機matrix

最低限の合格matrix:

- Windows 11 x64
- Windows 10 22H2 x64
- NVIDIA、AMD、Intelの対象GPU。未保有構成は未検証として明記し、推測で合格にしない
- software H.264 fallback
- SteamVR＋対象HMD／controller
- VRChat実sender、Spout demo sender
- default desktop audio、明示endpoint、mic on／off、mute all
- landscape／portrait、720p／1080p／4K、30 fps。60～120 fpsは対応宣言範囲に応じて追加

各payloadで最低限確認する項目:

1. 起動、first-run、Legal読取。
2. VRChat選択、OSC camera ON、Spout sender安定化。
3. 3秒以上録画、STOP、native trailer／flush／close、managed ffprobe／atomic rename。
4. ffprobe、video decode、audio decode、実player再生。
5. presentation start、duration、A/V offset、drop／duplicate、encode latency。
6. sender loss、audio device loss、GPU encoder unavailable、disk full相当、強制Abort。
7. failed recordingをfinal filenameへ公開しない。
8. camera state、pending recording、Legal mirrorの回復。

reportにはschema version、payload identity、run ID、OS build、GPU vendor／device／driverの構造化値、encoder実identity、HMD／controller、test case ID、pass／fail／skip、artifact hashを記録する。ユーザー名、world名、録画内容は記録しない。必須caseの`skip`は合格にしない。

合否はschema readerが、宣言済みmatrix profileに対する必須caseの完全性、全case成功、artifact hash、payload identity一致から導出する。serialized report内の自己申告`Passed`は入力として信用しない。全機能と最終Legal Bundleをfreezeした後に最終payloadを作り、そのidentityでmatrixを完走する。application source／DLL／asset／action・application manifest／Legalを変更してinner inventoryが変わったら再生成・再試験する。

promotion policyへ渡せる型はstrict schema reader／validatorの成功結果だけにし、生JSONやpublic constructorから`ValidatedPayloadIdentity`／`HardwareValidationEvidence`を自由生成できない境界へ狭める。report artifact自体のSHA-256、runner／CI run identity、取得時刻も保持し、どのrunがどのpayloadを検証したか追跡できるようにする。

## 7. OpenVR overlayの実装順

desktop録画が成立した後、次の順でproduction compositionへ接続する。SDK／Legal、manifest contract、single-runtime seam、pure placement state、Wrist layout／golden renderer、haptic policyのRedはmedia作業と独立して先行できるが、media commitへ混ぜない。

1. OpenVR SDK admission、Legal登録、runtime DLL staging。
2. OpenVR application manifest（`.vrmanifest`）とaction manifest／bindingsの完全性contract。
3. process単位のnative OpenVR runtime ownerとsingle poll loop。
4. 実`SteamVrInputBackend`と複数action observerへのfan-out。
5. overlay lifecycle Port。
6. Wrist snapshot拡張、layout／hit-test model、golden renderer。
7. overlay input／ray eventから共通application commandへの接続。
8. Wrist Dock／World Pin／move／recenter。
9. haptics。
10. first-run placement production verifierとroute登録。
11. 実HMD matrix。

`actions.json`はOpenVR application manifestではない。unpackaged install rootを参照する`.vrmanifest`、stable application key、`IVRApplications`でのidempotent登録／更新／解除が別途必要である。temporary登録を起動ごとに行うかpersistent登録をinstall lifecycleで行うかをADR化し、SteamVR再起動、app upgradeでpath変更、uninstall後のstale manifestをRedにする。MSIXではpackage install locationへabsolute pathを再解決し、存在しない旧version pathを残さない。

現在のApp hostは録画actionとmic actionに別々の`NativeSteamVrInputRuntime`を生成する。実OpenVRでは`VR_Init`／`VR_Shutdown`とaction state更新をprocess単位の一ownerへ集約し、一つの`UpdateActionState`／event poll結果をrecord、mic、overlay、placement、haptic consumerへ配る。first-run probeも同時に別runtimeを開かない。

### 7.1 Input Red

- init失敗、runtime再起動、manifest path失敗。
- `SetActionManifestPath`を最初の`UpdateActionState`／event pollより前にexactly once行う。
- action set／digital／haptic handle取得失敗。
- inactive action、rising edge、held state、controller reconnect。
- 一つのpoll sampleをrecord／mic／overlay consumerへ同じrevisionでfan-outし、遅いconsumerがpoll loopを止めない。
- 二重runtime init、consumer追加／削除中のshutdown、subscriber callback中Abort。
- Poll中shutdown、Abort、destroy。
- callback／poll threadからのself-join禁止。

現行controller bindingは`toggle_recording`しか割り当てていない。action manifestにはmic、overlay、recenter、hapticがあるがbindingがなく、`mute_all`とmove／pin／nudge commandはaction path自体がない。操作をoverlay ray eventで提供するかcontroller actionで提供するかを先に決め、全controllerでfirst-runが要求するrecord＋mic、haptic output、STOP到達性をmachine testする。

### 7.2 Overlay lifecycle Red

- create途中失敗のrollback。
- duplicate key／invalid handle。
- Show／Hide／SetTexture失敗。
- runtime restart、tracked device loss、texture device loss。
- appがrecording-readyでなくてもfirst-run placementとComplianceFault時のWrist Legalを表示できる。REC commandのenableだけをReady gateへ結び付ける。
- shutdown順を「texture／event更新停止 -> Hide -> ClearOverlayTexture -> DestroyOverlay -> renderer texture／device解放 -> `VR_Shutdown`」に固定し、各段をexactly onceにする。

### 7.3 Renderer Red

- 1024×512 BGRA、state change＋recording中10 Hz。
- snapshotへelapsed recording time、canvas resolution、target／actual FPS、audio／mic／Spout signal、warning／fault、placement modeを追加する。
- deterministic layout、stable element ID、pixel bounds、z-order、ray hit-test、disabled actionをpure modelで検査する。
- Japanese／English、200%、RTL、high contrast、missing glyph。
- icon allowlist、direct color／font／radius禁止。
- first frame前に未初期化textureを表示しない。
- locale／DPI／state別golden imageとglyph atlas missを検査し、同一snapshotから同一pixel hashを生成する。

### 7.4 Move／Pin Red

- left／right handのcontroller-relative transform。
- absolute World Pin、recenter、small／large nudge。
- drag release thresholdでmode切替。
- saved transformのreadback完全一致。
- STOPがdrag repeatより優先。
- dragなしで全操作へ到達可能。
- first-run routerに`WristOverlayPlacement` routeを登録し、fake evidenceではなく実overlay visibility／mode／pose readback＋user confirmationで完了する。

### 7.5 Haptics Red

- start 30 ms×1、stop 20 ms×2、fault 80 ms×1。
- amplitude／duration validation、disabled設定。
- visual／text eventが常に併存し、haptic失敗が録画結果を壊さない。

OpenVRの`IVROverlay`はoverlay作成、texture、transform、event、visibilityを別APIとして持つため、単一巨大adapterにしない。lifecycle、renderer、pose／transform、input、hapticをPortで分離する。

## 8. MSIXは実機合格後に行う

[`adr/0003-two-stage-windows-distribution.md`](adr/0003-two-stage-windows-distribution.md)を維持する。

### 8.1 Packaging Candidate

1. WPF app本体とは別にWindows Application Packaging Projectを追加する。
2. project／manifest reader／fixture testは先行実装できるが、実candidateは合格済み`ApplicationPayloadIdentity`のimmutable artifactを入力にし、packaging HEADからapplicationを再publishしない。
3. Partner Centerが発行した実`Identity/Name`、`Identity/Publisher`、`PublisherDisplayName`を使用する。
4. x64、`TargetDeviceFamily=Windows.Desktop`、desktop executable／entry point、mediumIL／full-trust、`runFullTrust` restricted capability、versionをmachine validationする。
5. mic captureに必要なcapabilityだけを宣言し、不要なbroad capabilityを追加しない。
6. MSIXを展開し、packaging metadataを除くinner application payloadをunpackaged合格payloadとpath／length／SHA-256／kind単位で照合する。package固有assetも別inventoryへ全件含めてLegal scanする。
7. local sideload certificateのsubjectをmanifest Publisherと完全一致させる。
8. package生成成功だけでは`PublishEligible`にしない。

Microsoft公式資料も、WPF／Win32をWindows Application Packaging ProjectからMSIX化できること、Store identity値をpackage manifestへ入れること、MSIX install fileが保護された`WindowsApps`配下に置かれることを説明している。したがって、EXE合格をMSIX合格へ読み替えない。

### 8.2 Packaged固有Red

- package install rootがread-only。
- working directoryがinstall rootでない。
- settings／logsが`LocalAppData`へ出る。
- 録画出力がユーザー選択／Downloadsへ出る。
- action manifest pathがpackage install locationのabsolute pathになる。
- `.vrmanifest`登録がinstall／first launch／upgradeで現package pathを指し、uninstall後にstale executable pathを有効な登録として残さない。
- Legal UI／folder mirrorがpackaged pathでも一致する。
- DLL検索がpackage payload外の同名DLLを拾わない。
- install／upgrade／uninstall後に必要なuser dataだけが残る。

### 8.3 Store Submission

- local test certificateでsideload install／launch／uninstall。
- packaged状態でSpout2、WASAPI、OpenVR、VRChat、HMDを再試験。
- WACKはdeprecated／非保守のoptional local preflightとしてだけ扱う。実行した場合はreportを保存・parseし、fail／not-runを見過ごさないが、未実行だけでreleaseを拒否しない。
- final MSIX payload、Legal Bundle、SBOM、source offerを再scan。
- exact `.msixupload`に対するPartner Centerの公式certification成功を必須にする。
- private flightでinstall／upgrade／hardware smokeを再検証後だけStore公開を許可。

秘密鍵をrepositoryやartifactへ入れない。Store提出用packageの本番署名はStore側へ委ねる。Microsoft公式にも、現在のauthoritativeな認証はPartner Center upload後に自動実行されるcertificationであり、local WACKは任意の事前確認と明記されている。

## 9. 横断release gate

### 9.1 Coverage／test quality

- 基本設計はintegration-test単独のfirst-party line／branch各90%以上を要求する。
- 前回記録は全体line 71.82%、branch 56.94%で未達。
- nativeも前回line 88.53%、branch 75.32%で未達。
- mutation score 75%は未測定。
- Windows UI Automation、HIL、WPF実行は未完了。

coverageを上げるためだけの無意味なassertを追加しない。production adapterのRedで未通過branchを先に埋め、各work package完了時に再採取する。

### 9.2 Legal／dependency

- 現在の第三者entryはcandidateであり、独立承認は未完了。
- actual Windows FFmpeg／Spout2／OpenVR／Material Symbols assetが得られるまでplaceholder hashをcanonical registryへ入れない。
- FFmpegはpinned source、configure flags、DLL hash、source offer、LGPL noticeを最終payloadと照合する。
- self-contained .NET runtime、ffprobe、Spout2、OpenVR、全native DLL／assetをregistry、rights ledger、SBOM、source offerへ反映し、最終inventoryの未知fileを0件にする。
- H.264特許、GPU SDK条項、商標、配布地域はOSS licenseとは別に独立reviewする。

### 9.3 CI／Windows

`.github/workflows/native-windows.yml`はWindows MSVCのportable native graphをbuild／CTestするが、production FFmpeg SDK、Spout2、OpenVR、実device、WPF UI Automationを検証しない。

今後追加するworkflowは次を分離する。

- portable Windows compile／ABI
- pinned production dependency build
- production／placeholder exactly-one factory variantとC ABI smoke
- WPF Release publishとapproved runtime staging manifest
- unpackaged payload assembly／inventory
- hardware-lab実行
- MSIX package validation
- Partner Center submission／certification approval

hardware secretやsigning certificateを通常PR workflowへ置かない。

## 10. t-wada式の進め方

各小単位で次を守る。

1. 外部仕様と既存Portの所有権契約を読む。
2. failure matrixへ入力state、失敗位置、競合相手、観測対象を追加する。
3. condition variable／promise／injectable factoryで決定的Redを書く。
4. 旧実装またはplaceholderで実際にRedになることを確認する。
5. Greenに必要な最小のproduction codeを書く。
6. 正常系だけでなく、下流mutation 0、resource release、event、statistics、後続拒否を検査する。
7. 実API contract testを追加する。
8. composition testで隣接Portをつなぐ。
9. sanitizer／repeat／full regressionを通す。
10. 文書とtest listを更新し、1つの論理commitにする。

sleepで競合順序を推測しない。barrier到達を確認した後の短いnegative waitは「まだ返っていないこと」の補助にだけ使う。fake counterも複数threadから触る場合はatomic／mutexで保護する。

## 11. 次回開始時の確認コマンド

```bash
git status --short
git log --oneline -12
cmake --build build/native-linux-debug -j2
ctest --test-dir build/native-linux-debug --output-on-failure
```

real FFmpeg contract SDKが`/tmp/vrrecorder-ffmpeg-contract-v2`にある場合:

```bash
VRRECORDER_FFMPEG_CONTRACT_TEST_ROOT=/tmp/vrrecorder-ffmpeg-contract-v2 \
  cmake --preset native-linux-debug-ffmpeg
cmake --build --preset native-linux-debug-ffmpeg
ctest --preset native-linux-debug-ffmpeg
```

managed baseline:

```bash
dotnet test tests/VRRecorder.Domain.Tests/VRRecorder.Domain.Tests.csproj --no-restore
dotnet test tests/VRRecorder.Application.Tests/VRRecorder.Application.Tests.csproj --no-restore
dotnet test tests/VRRecorder.Compliance.Tests/VRRecorder.Compliance.Tests.csproj --no-restore
dotnet test tests/VRRecorder.Presentation.Tests/VRRecorder.Presentation.Tests.csproj --no-restore
dotnet test tests/VRRecorder.IntegrationTests/VRRecorder.IntegrationTests.csproj --no-restore
dotnet build src/VRRecorder.App/VRRecorder.App.csproj --no-restore
dotnet format VR-Recorder.sln --no-restore --verify-no-changes --verbosity minimal
```

`--no-restore`はlocked restore済みのworkspaceでだけ使う。Windows Release publishはapproved Legal Bundle、native DLL、FFmpeg 4 DLL、ffprobe、Spout2／OpenVR runtime、manifest／bindingsが必須であり、placeholder pathやambient PATHでgateを回避しない。

## 12. やってはいけないこと

- isolated AAC／mux PortのGreenを「production録画完成」と呼ばない。
- `BACKEND_UNAVAILABLE` placeholderを成功fakeへ置き換えてEXEを動いたことにしない。
- 実binary未取得の第三者componentへ仮hash／仮承認を入れない。
- Hardware Validation PayloadをEXE単体、ZIP単体、一般公開releaseとして扱わない。
- unpackaged実機証拠なしにMSIXを作ってrelease進捗と数えない。
- MSIX作成成功をStore公開可能の証拠にしない。
- callback stackでpeer workerをJoinしない。
- failed recordingをfinal `.mp4`としてrenameしない。
- native backendからfinal filenameへrenameしない。ffprobe後のmanaged finalizerだけが公開する。
- throwaway H.264 contextのextradataで本番mux headerを作らない。
- OpenVR runtime／`UpdateActionState` loopをactionごとに複製しない。
- TSanがhost制約で起動しない事実を成功として記録しない。
- coverage値を再採取せず現在値として更新しない。
- OpenVR projection modelを実overlay renderer完成と扱わない。

## 13. 段階別Definition of Done

### Production recording backend DoD

- `CreateMediaBackend`、Spout、encoder probeが実adapterを返す。
- 3秒以上のH.264＋AAC fMP4が実packetから生成される。
- ffprobe／decode／player playbackが成功する。
- Spout／D3D11／WASAPIのresourceとfailure boundaryが閉じる。
- fallback／part rolloverまたは承認済み仕様改訂が完了する。
- failed outputを公開しない。

### Unpackaged hardware payload DoD

- existing canonical root、inventory内relative entrypointを持つreproducible self-contained `win-x64` directory。
- 全payload inventoryとLegal identityがhash固定される。
- Windows／GPU／VRChat／audio／SteamVR matrix reportが同じidentityへ結び付く。
- validatorが全必須caseの存在と成功から合否を導出し、自己申告boolを信用しない。
- まだ一般公開不可である。

### OpenVR DoD

- SDK／runtime／application manifest／action manifest／bindingsがpinned payloadとLegalへ登録される。
- process単位のsingle runtime／poll loopが全action consumerへfan-outする。
- 実input、overlay、renderer、event、pose、move／pin、hapticsが動く。
- Wristとdesktopが同じapplication commandを使う。
- drag代替、left／right、readability、STOP到達性を実HMDで確認する。
- first-run 7がproduction verifierで完了する。

### Microsoft Store MSIX DoD

- 合格済みunpackaged payloadとinner payloadが一致する。
- packaging wrapper revision／manifest／outer hashが、再buildしていないimmutable `ApplicationPayloadIdentity`を参照する。
- Partner Center identityとmanifestが一致する。
- sideload／packaged固有回帰／hardware再試験が成功する。WACKを実行した場合はそのreportも合格する。
- final Legal Bundle／SBOM／source offerがpayloadと一致する。
- independent legal review、Partner Center certification、Store flightが完了する。

## 14. 参照する一次資料

- Microsoft MSIX documentation: <https://learn.microsoft.com/en-us/windows/msix/>
- Build MSIX from source: <https://learn.microsoft.com/en-us/windows/msix/desktop/source-code-overview>
- Package WPF／Win32 with Visual Studio: <https://learn.microsoft.com/en-us/windows/msix/desktop/vs-package-overview>
- Store product identity: <https://learn.microsoft.com/en-us/windows/apps/publish/view-app-identity-details>
- MSIX container and protected install files: <https://learn.microsoft.com/en-us/windows/msix/msix-containerization-overview>
- Packaged desktop runtime behavior: <https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-behind-the-scenes>
- Package with Visual Studio and certification preflight: <https://learn.microsoft.com/en-us/windows/msix/package/packaging-uwp-apps>
- OpenVR `IVROverlay` overview: <https://github.com/ValveSoftware/openvr/wiki/IVROverlay_Overview>
- OpenVR API documentation: <https://github.com/ValveSoftware/openvr/wiki/API-Documentation>
- OpenVR SteamVR Input: <https://github.com/ValveSoftware/openvr/wiki/SteamVR-Input>
- OpenVR action manifest: <https://github.com/ValveSoftware/openvr/wiki/Action-manifest>
- DXGI keyed mutex: <https://learn.microsoft.com/en-us/windows/win32/api/dxgi/nf-dxgi-idxgikeyedmutex-acquiresync>
- WASAPI `IAudioCaptureClient::ReleaseBuffer`: <https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-iaudiocaptureclient-releasebuffer>
- repository内のFFmpeg／C++ thread契約: [`NATIVE-PIPELINE-FAILURE-MATRIX.md`](NATIVE-PIPELINE-FAILURE-MATRIX.md)

## 15. 再開時の最初の一文

次回は次の依頼から始めればよい。

> `docs/IMPLEMENTATION-HANDOFF.md`のWork package 0から再開し、encoder方式・late-extradata handshake・production dependency／staging contractをADRとRedで固定してから、Work package Aのproduction AAC pipelineをGreen、real FFmpeg test、full regression、論理commitまで進めて。
