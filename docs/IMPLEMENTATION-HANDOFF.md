# VR-Recorder 実装引き継ぎ書

- 更新日: 2026-07-14
- 対象branch: `main`
- 実装基準commit: `18b8b03`（production AAC接続構造契約と実AAC→fragmented MOVのin-process境界）
- 現在の判定: 実装checkpoint。録画可能な配布製品でも、release候補でもない
- 配布方針: unpackaged self-contained `win-x64` payloadで実機検証し、同一payloadの合格後だけMicrosoft Store MSIX候補へ進める

## 1. 最初に読む結論

次回はMSIXやOpenVR overlayから始めない。最初にproduction録画経路を成立させる。

推奨順は次のとおり。

1. strict runtime staging manifest／transactional copy／factory-evidence consumptionをRedから実装する。
2. production SDKと同じFFmpeg 8.1.2 source identityからtest-onlyの別process demux／decode oracleを再現buildし、AACのdecoded frame数を固定する。oracleをproduction payloadへ混ぜない。
3. accepted済みの[`ADR-0006`](adr/0006-encoder-identity-preheader-and-production-factory-contract.md)を変更せず、actual Windows FFmpeg／vendor SDK／ffprobe／decode oracleのbuild evidence、hash、Legal admissionを揃え、AACをproduction media variantへ接続する。
4. system-memory NV12からH.264 software encoder Port、late extradata、Annex B→AVCC、crop-aware SPS寸法検証を実装する。
5. software H.264とAACのA/V packetを`PreHeaderCoordinator`から実fragmented MP4 muxerへ接続し、3秒scratch fileをffprobe／decodeまで通す。
6. synthetic D3D11 textureからNV12を生成する実D3D11 processorを実装する。
7. processor成立後にNVENC／AMF／QSVのsame-adapter D3D11経路とstructured probe evidenceを実装する。
8. 実Spout2 receiverを実装し、確定済みD3D11 processor経路へ接続する。
9. 既存WASAPI実装をWindows COM failure seamと実device試験で閉じる。
10. 上記を`CreateMediaBackend`、`CreateSpoutSourceBackend`、encoder probe factoryへ接続する。
11. privateなunpackaged self-contained `win-x64` directoryで、desktop操作によるWindows／GPU／VRChat録画をbring-upする。この段階はまだpromotion用の実機合格証拠にしない。
12. OpenVR input／overlay／renderer／haptics／move・pinを実装する。
13. 全機能を含むunpackaged directoryを再生成・再identity化し、Windows／GPU／VRChat／SteamVR／HMDの最終Hardware Validation Payloadとして合格させる。
14. その合格payloadだけを別projectでMSIXへ包む。

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
| `f8b5915` | 実装引き継ぎとrelease gateの初版 |
| `12ec6ec` | Release outputへのpinned FFmpeg runtime DLL明示staging |
| `202eb74` | 4 factory familyのexactly-one variant selector |
| `a573e83` | factory selection intentをactual native binary hashへ結合 |
| `00a07ea` | AAC pump windowをmux descriptorの`frame_size`へ一元化 |
| `c794817` | 実AAC composition、Stop flush、未開始worker破棄境界 |
| `179ce3b` | AAC compositionの未知failure-point拒否を追試 |
| `18b8b03` | production AAC source-plan契約、実AAC packet→実fragmented MOV、Stop drain／trailer境界 |

現在固定済みの主な不変条件:

- thread生成OOM、internal failure、成功statusなのにnon-joinableを決定注入できる。
- Start中Abortをterminal winnerとして遅延threadへ転送し、publication完了前にcleanupを返さない。
- Abort後に成功復帰したWrite／Pollをpacket、frame、latency、first-packet、Faultedへcommitしない。
- callback内ではlogical `RequestAbort`だけを行い、物理Joinはcallback stack外のcleanup ownerが回収する。
- C ABI Start／Stop、backend stop、inner pipeline Start／Joinの結果を単一terminal winnerへ収束させる。
- Spout SenderLost／FailedとAbortが競合してもAbort結果を維持し、capture Abortを重複しない。
- portable／productionのmedia、encoder probe、Spout、SteamVR factory sourceをfamilyごとにexactly one選び、未知variant、source欠落、incomplete full-productionをconfigure時に拒否する。
- configure intentのSHA-256 markerをnative binaryへ埋め、actual binaryのlength／SHA-256と一致するevidenceだけを生成する。validな別intentへの差替えも拒否する。
- audio encoding windowはAAC descriptorの`frame_size`だけから導出し、callerが別の2048等を渡すsplit-brainを作れない。
- `FfmpegAacAudioPipeline`はencoder→muxing sink→sessionを順に所有し、逆順のsession→sink→encoderで破棄する。未開始破棄はcapture／submissionへ副作用を出さず、active破棄はAbort／Joinを完了してからencoderを解放する。
- production AACの将来source planはexact 3 sourceだけを返し、UNAVAILABLE、production adapter opt-in無効、canonical target欠落、test-only target、alias、source欠落ではfail-closedになる。main DLLはまだ変更しない。
- 実AAC encoderのnonzero packetを実libavformat MOVへ渡したとき、全`mdat` payloadはencoder出力と順序・重複を含め完全一致する。Stop前はwrite 0、Stop／Join後だけdrain packet、trailer、flush、closeを各契約どおりcommitする。

詳細なfailure matrixは[`NATIVE-PIPELINE-FAILURE-MATRIX.md`](NATIVE-PIPELINE-FAILURE-MATRIX.md)を正とする。

### 2.2 直近の検証証拠

`18b8b03`作成時点で次を確認した。

- portable CMake full build: 成功
- portable Linux native CTest: 45/45成功
- exact FFmpeg 8.1.2 contract-test SDK構成のfull build: 成功
- real FFmpegを含むLinux native CTest: 50/50成功
- Compliance test: 205/205成功
- 実AAC→実fragmented MOV integrationはASan／UBSanで10回連続成功
- final packet endが3秒となるcanonical timestamp、nonempty `mdat`、encoder payloadとの完全一致、AAC bitrate metadataを確認
- owned audio graphは1024 input frameから2 real AAC packetを書き、Stop前write 0、Stop後write 2、trailer後flush 1、close 1、`mfra`生成を確認
- `git diff --check`: 成功
- GCC TSan: test process開始前にhost固有の`unexpected memory mapping`で停止。TSan成功は主張しない

Compliance以外のmanaged全件とcoverageは今回再採取していない。前回値は[`VALIDATION-REPORT.md`](../VALIDATION-REPORT.md)にあるが、古い数値を現在値として扱わない。Windows MSVC production-link、別processの実MOV demux／decode、Windows／GPU／SteamVR実機は未検証である。

## 3. 現在の実装とplaceholderの境界

### 3.1 実装済みだがproduction未接続

- portable audio／video capture、normalize、mix、CFR、encoding、mux、recording session state machine
- 実FFmpeg 8.1.2 libavcodec AAC PortとAAC packet encoder
- 実AAC encoder、`MuxingAudioEncoderSink`、`StereoAudioPipelineSession`を安全な破棄順で所有する`FfmpegAacAudioPipeline`
- 実FFmpeg 8.1.2 libavformat fragmented MP4 Port
- AAC descriptorのexact 192000 bit/s伝播、negative priming／edit list境界、descriptor由来1024-frame pump window
- 4 factory familyのconfigure-time exactly-one selectorと、native binaryに結合されたfactory-selection evidence
- Windows用event-driven WASAPI source
- managed P/Invoke recording／Spout／SteamVR wrappers
- Wrist UIの状態projection、localization、Legal projection、input adapter
- OpenVR action manifestとIndex／Oculus Touch／Viveの録画toggle binding
- unpackaged EXE→MSIXのpromotion policyとfail-closedなidentity比較

`ffmpeg_aac_audio_pipeline_tests.cpp`はreal AAC packetがcompositionから同期`RecordingSubmission` fakeへ到達し、Stop時を含めfakeが`Written`として受理したpacket数とpipeline統計が一致することを検証する。`ffmpeg_aac_to_fragmented_mp4_integration_tests.cpp`はさらに、3秒分のreal AAC packetを実libavformatへ渡し、top-level MOV box、`mdat`全payload、bitrate metadataを検証する。owned graphではcapture→AAC→submission→実muxを接続し、Stop／JoinによるFinish drain、trailer、flush、closeまで通す。

ただし、これは同一process内の構造契約である。別processのpinned demux／decode oracle、track/sample tableの解釈、AAC-LC／48 kHz／stereoのdemux結果、decoded末尾sample数はまだ証明していない。`final_packet_end_microseconds == 3'000'000`もcanonical packet timelineの証拠であり、decode後の長さの証拠ではない。ここをWork package A／Cのrelease gate完了と混同しない。

### 3.2 明示的placeholder

production DLLは現在も次のfactoryをplaceholderへ接続している。

- `src/VRRecorder.Native/src/unavailable_media_backend.cpp`
- `src/VRRecorder.Native/src/unavailable_spout_source_backend.cpp`
- `src/VRRecorder.Native/src/unavailable_encoder_probe_backend.cpp`
- `src/VRRecorder.Native/src/unavailable_steamvr_input_backend.cpp`

いずれも意図的に`VRREC_STATUS_BACKEND_UNAVAILABLE`を返す。`VRRECORDER_ENABLE_FFMPEG_ADAPTERS=ON`はpinned SDKを検証・importするが、現時点の`src/VRRecorder.Native/CMakeLists.txt`は実AAC／libavformat sourceをproduction DLLへ追加しておらず、production compositionを成立させない。`ProductionMediaAacAttachment.cmake`は将来追加するexact 3 sourceとcanonical imported targetの構造を検証するread-only planであり、targetを変更しない。fixtureのimport targetが通ることはactual artifactのversion、location、provenanceを証明しない。

factory selectorは既に`UNAVAILABLE`／`PRODUCTION`をfamily別に選べるが、将来の`production_media_backend.cpp`、`production_encoder_probe_backend.cpp`、`spout2_source_backend.cpp`、`openvr_steamvr_input_backend.cpp`はまだ存在しない。そのため`PRODUCTION`選択はsource欠落として意図的にconfigure失敗する。selection evidenceが証明するのは「どのsourceがactual binaryへlinkされたか」までであり、そのsourceが実packetを生成できることはcomposition testとHILで別に証明する。

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
| P0 | production AAC接続 | real encoder→sink→実MOVとStop drain／trailerはin-process Green。main DLL link／pinned demux・decode／末尾sampleは未完了 | 実録画backend |
| P0 | H.264 encoder／AVCC契約 | Port／factory／converter未実装 | MP4 video stream |
| P0 | hardware encoder identity／probe | ADRでNVENC=`h264_nvenc`、AMF=`h264_amf`、QSV=`h264_qsv`、software=`h264_mf(hw_encoding=0)`を確定。実SDK／structured probeは未実装 | 正しいprobe／表示／fallback |
| P0 | 実encoder→mux composition | audioのnonzero packet→実MOVはGreen。実H.264 packet、A/V composition、別process decodeは未完了 | 3秒A/V MP4のdecode証明 |
| P0 | approved runtime staging | FFmpeg 4 DLLの直接stagingはあるが、hash／registry／exact inventory／transactional copyは未実装 | reproducible Release payload |
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

### Work package 0: Windows dependencyとencoder identity（policy／selector完了、actual artifact未完了）

完了済み:

1. [`ADR-0006`](adr/0006-encoder-identity-preheader-and-production-factory-contract.md)でpublic encoder kindと実FFmpeg codec、same-adapter LUID、QSV mapping、packet-based probe、late extradata、factory evidenceを確定した。
2. media／encoder probe／Spout／SteamVRの4 familyを`UNAVAILABLE`／`PRODUCTION`からexactly one選ぶ。未知値、source欠落、pinned FFmpeg欠落、full-production不完全をconfigure時に拒否する。
3. selection intent digestをnative binaryへ埋め、actual binary filename／length／SHA-256へ結合したevidenceだけを生成する。validな別intentへの差替えもtestで拒否する。
4. App Release outputへFFmpeg 4 DLLを明示配置する入口は追加した。ただし存在確認だけであり、最終approved stagingではない。

次に行うartifact phase:

1. 公式FFmpeg 8.1.2 sourceから、`h264_mf`、`h264_nvenc`、`h264_amf`、`h264_qsv`、AACをexact allowlistにしたWindows x64 shared SDKをMSVCで再現生成する。candidateはnv-codec-headers `n13.0.19.0`、AMF `v1.5.2`、oneVPL `v2.17.0`だが、build／import table／license確認前にcanonical採用扱いしない。
2. header、MSVC import library、major付きDLL、configure evidence、source archive、build recipe、全SHA-256を一つのcandidate evidenceにする。missing、version drift、unexpected library、GPL／nonfree／version3、未知external dependencyを個別にRedにする。
3. production SDKは`--disable-programs`／decoderなしなので、同じsource identityから`ffprobe.exe`とtest-only H.264／AAC demux・decode oracleを別buildする。oracleをproduction payloadへ混ぜない。
4. Spout2／OpenVRのexact tag／commit、source archive、license、static／dynamic deploymentを確定する。actual binaryがない段階でcanonical registryへplaceholder hashやapproved entryを入れない。
5. strict `WindowsRuntimeStagingManifest`を実装する。entryはsource／target relative path、role、component ID、platform、kind、SHA-256を持ち、unknown field、traversal、Windows case-fold duplicate、ADS、device name、missing／extra fileを拒否する。
6. dedicated input rootを既存inventory／registry／Legal gateへ通し、sibling staging directoryへcopyしながら再hash、再inventoryする。全検証後だけdirectory renameし、mid-copy／rename／cancellation失敗では旧payloadを完全維持する。
7. stagerが生成した`ApprovedWindowsRuntime.props`だけをRelease Appがimportする。現行の`NativeMediaLibraryPath`、`FfprobeExecutablePath`、`FfmpegRuntimeDirectory`直接入力は最終Release経路から削除する。

vendor driver DLLは同梱しない。oneVPL dispatcherをdynamic linkする場合だけdispatcherをpayload／SBOM／Legalへ明示登録する。probe成功条件は初期化ではなく、選択したSpout adapter上でSPS／PPS／IDRを含む実packetとactual backend identityが得られることである。

### Work package A: production AAC pipeline（一部Green）

完了済み:

1. `MediaRecordingPipeline`から任意のaudio window引数を削除し、mux configurationのAAC descriptor `frame_size`を唯一の情報源にした。
2. `FfmpegAacAudioPipeline` factoryがexact 48 kHz／stereo／AAC-LC／192000 bit/s／1024-frame descriptorと実encoderを同じopened contextから返す。
3. ownerは`encoder_ -> sink_ -> session_`の順に宣言し、active破棄時はsession Abort／Join後にsink／encoderを解放する。未開始破棄はcapture／muxへ偽Abortを通知しない。
4. composition allocation OOM、未知failure point、factory成功、隣接Port未変更、1024-frame固定、最初のzero packet、Stop時Finish packet、`RecordingSubmission` fakeが`Written`として受理したpacket数との統計一致、active destructor Abortをreal FFmpeg testで固定した。実muxのwrite件数はまだこのtestの対象ではない。
5. encoder内部のactive small-frame拒否、Finish small-last、FLTP変換、FIFO／resampler flush、EAGAIN、0／複数packet、partial batch非公開、Encode／Finish／Abort競合は既存testで継続Greenである。
6. `ProductionMediaAacAttachment.cmake`は将来のproduction attachmentをexact 3 sourceに固定した。UNAVAILABLEでは空、production adapter opt-in無効、canonical imported target各欠落、`FFmpegContractTest::*`だけ、alias、empty／relative／nonexistent root、各source欠落ではconfigureをfail-closedにする。main DLLを変更する副作用は持たない。
7. direct integrationは3秒分のreal AAC packetとFinish packetを実libavformatへ書き、全top-level `mdat` payloadがencoder出力の連結byte列と順序・重複を含め完全一致することを確認する。owned graph integrationは1024 input frame、2 real write、Stop前write 0、Stop後trailer／flush／close、AAC priming edit listを確認する。
8. portable 45/45、exact FFmpeg 50/50、ASan／UBSan repeat 10回までGreenである。

残作業は次の順に行う。

1. production SDKと同じFFmpeg 8.1.2 source identityから、MOV demuxer、AAC decoder、ffprobeまたは小さい専用oracleを持つtest-only artifactを別buildする。production SDKはdecoderless／`--disable-programs`のまま維持し、oracleをRelease payloadへstageしない。
2. oracleを別processで起動し、AAC-LC、48 kHz、stereo、packet count、bitrate metadata、PTS/DTS、decoded frame数を機械可読結果から検証する。timeout、nonzero exit、signal、malformed／missing field、version drift、余分なstreamも個別にRedにする。
3. `48'000` input frameに対して`decoded_frames == 48'000`を最初のtail Redにする。一時診断では`48'128` frameとなり、末尾128 frameのpaddingが残った。現在の3秒packet-end assertionをdecode長の代用にしない。FFmpeg 8.1.2のMOV muxerはpacketの`AV_PKT_DATA_SKIP_SAMPLES`を消費しないため、side data追加だけをGreenにしない。priming edit list、最終packet duration、containerが表現できるterminal trimを公式sourceとoracle結果から決める。
4. actual Windows SDK evidenceが揃った後だけ、main `src/VRRecorder.Native/CMakeLists.txt`のWindows production blockへAAC 3 sourceと`FFmpeg::avcodec`、`FFmpeg::avutil`、`FFmpeg::swresample`をliteral記述する。helper内の`target_link_libraries`や`${links}`展開でComplianceのcallsite検出を回避しない。
5. main DLLへのlink追加時はComplianceが要求するruntime artifact、source archive、build recipe、hash、approvalを同じcommit系列で揃える。`FFmpegContractTest::*`、ambient `find_package`、unversioned library、placeholder hashへfallbackしない。
6. Windows MSVC＋actual pinned SDKでproduction DLLをlinkし、test executable横へ`avcodec-62.dll`、`avutil-60.dll`、`swresample-6.dll`をapproved staging manifest経由で配置する。
7. `production_media_backend.cpp`が後続のWork package Cから所有できるAAC composition factory seamを公開し、actual pinned SDKを使うproduction variantのfactory smoke testを通す。ここまでをWork Aの「production AAC component Green」とし、capture／`PreHeaderCoordinator`／muxへの接続はWork package Cの完了条件にする。

### Work package B: H.264 encoder Portとbitstream契約

最初はsystem-memoryのsynthetic NV12 frameでsoftware encoder経路を成立させる。D3D11 texture直結とhardware adapter bindingはWork package Dの後に追加する。これによりcodec／timestamp／AVCCの失敗とGPU共有resourceの失敗を分離する。

#### Red

1. `h264_mf(hw_encoding=0)` software context設定とopened-context readback。vendor hardware codecはこのpacket contractを再利用するが、Work package D完了後まで追加しない。
2. system-memory frameとglobal-headerの入力契約。D3D11 frame入力契約はWork package D2で追加する。
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
- この段階のGreenはsoftware H.264だけを指す。NVENC／AMF／QSVの成功を含めない。

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

### Work package D2: vendor hardware encoderとstructured probe

Work package Dのprocessorとsame-adapter resource契約がGreenになった後だけ着手する。software経路で固定したH.264 packet／AVCC契約を変えず、D3D11入力とbackend identityの証拠を追加する。

#### Red

1. `h264_nvenc`、`h264_amf`、`h264_qsv`ごとにrequested kind、actual codec名、hardware flag、adapter LUID、opened pixel formatを一致させ、不一致やsoftware fallbackの偽装を拒否する。
2. processor出力とencoder device/contextが同じadapter LUIDであることを要求し、cross-adapter texture、stale generation、device removedをpacket生成前に拒否する。
3. QSVのderived frames context、NVENC／AMFのD3D11 frame binding、driver／runtime不足、open成功後の最初の実packet失敗を個別に注入する。
4. bool-only probe v1を互換維持しつつ、actual backend／codec／hardware／adapter LUID／driver／opened format／SPS・PPS・IDR検証を返すsize付きstructured probe v2を追加する。
5. probeはcontext openだけで成功にせず、同じadapter上のsynthetic frameからSPS／PPS／IDRを含むpacketを取得してdecodeできた場合だけ成功にする。

#### 完了条件

- NVIDIA／AMD／Intelの各actual adapterでstructured probeと3秒scratch encode／decodeを通し、unsupported環境は誤ったbackend名で成功させず明示的にUnavailableへ分類する。
- Work package Eはこのsame-adapter経路を変更せず、Spout surfaceを入力へ接続するだけにする。

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

現在の`VRRecorder.App.csproj`はRelease時に`vrrecorder_native.dll`、`ffprobe.exe`、FFmpeg 4 DLLを直接propertyからstagingする。しかしhash、registry approval、source／build evidence、extra file、Spout／OpenVR runtime closureを検査しない。上記全runtimeをapproved staging manifestからcopyし、missing、余分なDLL、hash／major不一致をbuild前に拒否する。

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
- portable factory selector／binary evidence回帰と、actual production sourceを使うproduction C ABI／composition smoke
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

実AAC→実MOVだけをASan／UBSanで再確認する場合:

```bash
cmake -S . -B build/native-linux-debug-ffmpeg-mux-sanitize \
  -DVRRECORDER_FFMPEG_CONTRACT_TEST_ROOT=/tmp/vrrecorder-ffmpeg-contract-v2 \
  -DCMAKE_BUILD_TYPE=Debug \
  -DCMAKE_CXX_FLAGS='-fsanitize=address,undefined -fno-omit-frame-pointer' \
  -DCMAKE_EXE_LINKER_FLAGS='-fsanitize=address,undefined'
cmake --build build/native-linux-debug-ffmpeg-mux-sanitize \
  --target vrrecorder_native_ffmpeg_aac_to_fragmented_mp4_integration -j2
ASAN_OPTIONS=detect_leaks=1:halt_on_error=1 \
UBSAN_OPTIONS=halt_on_error=1:print_stacktrace=1 \
  ctest --test-dir build/native-linux-debug-ffmpeg-mux-sanitize \
    -R '^vrrecorder_native_ffmpeg_aac_to_fragmented_mp4_integration$' \
    --output-on-failure --repeat until-fail:10
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
- in-processの`mdat` byte一致を、別process demux／decodeやsample-accurate tailの証拠と呼ばない。
- `AV_PKT_DATA_SKIP_SAMPLES`を付けただけでFFmpeg 8.1.2 MOVの末尾trimが成立したと仮定しない。
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
- FFmpeg 8.1.2 source archive: <https://ffmpeg.org/releases/ffmpeg-8.1.2.tar.xz>
- FFmpeg send／receive drain contract: <https://github.com/FFmpeg/FFmpeg/blob/n8.1.2/libavcodec/avcodec.h#L90-L147>
- FFmpeg MOV muxer implementation: <https://github.com/FFmpeg/FFmpeg/blob/n8.1.2/libavformat/movenc.c>
- FFmpeg MOV／MP4 muxer options: <https://ffmpeg.org/ffmpeg-formats.html#mov_002c-mp4_002c-ismv>
- OpenVR `IVROverlay` overview: <https://github.com/ValveSoftware/openvr/wiki/IVROverlay_Overview>
- OpenVR API documentation: <https://github.com/ValveSoftware/openvr/wiki/API-Documentation>
- OpenVR SteamVR Input: <https://github.com/ValveSoftware/openvr/wiki/SteamVR-Input>
- OpenVR action manifest: <https://github.com/ValveSoftware/openvr/wiki/Action-manifest>
- DXGI keyed mutex: <https://learn.microsoft.com/en-us/windows/win32/api/dxgi/nf-dxgi-idxgikeyedmutex-acquiresync>
- WASAPI `IAudioCaptureClient::ReleaseBuffer`: <https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-iaudiocaptureclient-releasebuffer>
- repository内のFFmpeg／C++ thread契約: [`NATIVE-PIPELINE-FAILURE-MATRIX.md`](NATIVE-PIPELINE-FAILURE-MATRIX.md)

## 15. 再開時の最初の一文

次回は次の依頼から始めればよい。

> `docs/IMPLEMENTATION-HANDOFF.md`とADR-0006を正として、まずWork package 0のstrict runtime staging manifest／transactional copyをRedから進めて。次に同じFFmpeg 8.1.2 source identityから別processのtest-only demux／decode oracleを作り、`48'000` inputに対するdecoded frame完全一致をRedにしてAAC末尾paddingを閉じて。actual Windows artifact／Legal admission前にmain DLLへFFmpegをlinkせず、その後Work package Bのportable Annex B／SPS crop／avcCへ進み、各論理単位でreal test、full regression、commitまで行って。
