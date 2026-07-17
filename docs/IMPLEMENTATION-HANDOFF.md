# VR-Recorder 実装引き継ぎ書

- 更新日: 2026-07-17
- 対象branch: `main`
- 実装基準commit: `58750cf`（PICO production WASAPI startup stabilization）
- 現在の判定: automated release-payload identity実装中。配布製品でもrelease候補でもない
- 配布方針: unpackaged self-contained `win-x64` payloadで実機検証し、同一payloadの合格後だけMicrosoft Store MSIX候補へ進める

## 1. 最初に読む結論

desktop production録画、production OpenVR overlay、first-run setup 7／8のproduction route、PICO microphoneのdirect captureとMicOnly production mux／oracleは完了した。実機検証は最終Hardware Validationへまとめる。先に自動化可能なrelease payload identity、report schema／matrix validator、settings schema、quality gateを閉じ、payloadをfreezeしてからWindows／GPU／VRChat／SteamVR／HMDを同一identityで一括検証する。独立Legal承認は外部gateとしてfail-closedのまま維持し、自動化可能な実装と直列化しない。

2026-07-16 checkpointでは、公式Spout demo senderの640×360 BGRA8 frameとdefault render endpointのWASAPI loopbackを、production `vrrecorder_native.dll`のSpout2→D3D11 NV12→software `h264_mf`およびAAC→fragmented MP4経路へ通した。共有D3D11 immediate contextには`ID3D11Multithread`保護を有効化し、CFR frame PTSはcodec tickのままlibavcodecへ渡す。実録画はvideo 117 packet／3.900秒／30 fps、audio 193 packet／4.117秒／48 kHz stereo、container 4.133秒となり、別rootのpinned `ffprobe`でpending fileを検証してからだけfinal `.mp4`へrenameされた。PICO `default-capture`のdirect production WASAPI HILでは、session前packetとshort discontinuity後のdevice-position gapを修正し、3秒／48 kHz captureを500 ms間隔で10回連続成功させた。さらに同じPICO endpointをMicOnlyでfull production sessionへ接続し、Spout映像110 H.264 packetとmicrophone音声179 AAC packetをmuxした。pinned oracleはvideo 110 frame、audio 178 decoded frame／182,272 sample、audio presentation 0.000～3.797秒を確認し、検証後だけfinal `.mp4`へ公開した。独立Legal review／canonical registry admission、VRChat sender、device change／privacy、controller HIL、fallback／part rollover、最終payload／MSIXは未完了であり、Release admissionは引き続きfail-closedである。古いcheckpointの「production media未接続」「実Spout／WASAPI未完了」という記述より本段落と2.5節を優先する。

推奨順は次のとおり。

1. manifest v2、PE admission、repository-derived graph、staging CLI、two-phase publish、post-publish sealの自動gateを完成させる。
2. source revision／product versionを含むapplication payload identityと、versioned Hardware Validation report schema／required matrix validatorをRed→Greenで閉じる。
3. settings schema、coverage／mutation、UI Automationのうち実機を要しない範囲を閉じ、全機能を含むunpackaged directoryをfreezeする。
4. freeze済みの同一identityに対してWindows／GPU／VRChat／audio／SteamVR／HMDの最終実機検証を一括実行する。
5. 実在するticket／requester／independent reviewerからLegal approvalを取得し、合格payloadだけを別projectでMSIXへ包む。

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
| `77b7747` | AAC残gateと実装順の引き継ぎ更新 |
| `8cfcb88` | strict Windows runtime staging manifest／path grammar |
| `13d3283` | factory-selection evidenceとactual native binaryの照合 |
| `3c622f4` | ApprovedGraph admission、決定的props、immutable digest publication、失敗注入 |
| `f1aaff0` | Release Appを`ApprovedWindowsRuntime.props`限定へ変更 |
| `409d9b3` | staging対象DLL／EXEのAMD64 PE32+ admissionをrelease plannerへ接続 |
| `7399852` | manifest v2のprofile／RID／declared length／Legal anchorをApp buildまで伝播 |
| `eebf776` | full-production runtime closureをexact target setで固定 |
| `b7e8a63` | staged Windows PEの通常／delay import closureを検証 |
| `c2c46fe` | repository evidenceだけからApprovedReleaseGraphを発行 |
| `bf6a8e7` | fail-closed Windows staging CLIを追加 |
| `af38530` | stagingとself-contained publishを分離したofficial scriptを追加 |
| `a672c42` | Windows publish directoryのcanonical inventoryを生成 |
| `e358204` | non-native asset owner／scope closureとfactory evidence target固定 |
| `cf07e1b` | Windows-only orchestration、ADS拒否、end-to-end staging境界 |
| `3d1a99c` | 共有D3D11 immediate contextのmultithread保護 |
| `1a24950` | software H.264のCFR PTSをcodec tickへ修正 |
| `9aabe3f` | pending録画をfinal公開前に検証 |
| `079b785` | 期待録画長をvideo timelineで検証 |

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
- staging manifestはstrict UTF-8／JSON schema、Windows case-fold path、device name、ADS記法、unknown／duplicate field、source／target親子衝突を拒否する。
- dedicated input rootはmissing／extra／hash／kind／reparse、ApprovedGraph owner／runtime scope、native registry、factory evidenceとactual DLL identityを通過した場合だけimmutable planになる。
- payloadと決定的`ApprovedWindowsRuntime.props`を同じtemporary directoryで生成・再hash・exact inventory検証し、未存在の`windows-runtime-<inventory-sha256>`へ1回だけmoveする。copy／tamper／extra file／commit／cancellation失敗は既存digest directoryを変更しない。
- Release Appは明示したapproved propsだけをevaluation時にimportし、旧`NativeMediaLibraryPath`／`FfprobeExecutablePath`／`FfmpegRuntimeDirectory`をReleaseで拒否する。Debug bring-upだけは旧経路を残す。

詳細なfailure matrixは[`NATIVE-PIPELINE-FAILURE-MATRIX.md`](NATIVE-PIPELINE-FAILURE-MATRIX.md)を正とする。

### 2.2 Windows runtime staging foundation

今回、次の縦切りをRed→Greenで実装した。

1. [`WindowsRuntimeStagingManifestReader`](../src/VRRecorder.Compliance/Staging/WindowsRuntimeStagingManifestReader.cs)がschema v1、`windows-x64`、role／deployment-kind対応、canonical lowercase SHA-256、source／targetのWindows path grammarをstrictに読む。
2. [`WindowsRuntimeStagingAdmissionPlanner`](../src/VRRecorder.Compliance/Staging/WindowsRuntimeStagingAdmissionPlanner.cs)がdedicated input rootのexact inventory、hash、manifest上のdeployment kind、reparse point、ApprovedGraph owner／scope、third-party native registry、first-party `vrrecorder_native.dll`と`native-factory-selection.json`のexact pairを検査する。
3. [`NativeFactorySelectionEvidenceValidator`](../src/VRRecorder.Compliance/Staging/NativeFactorySelectionEvidenceValidator.cs)が4 production family、selection intent marker、DLL filename／length／SHA-256、evidence自体のSHA-256を照合する。
4. [`ImmutableWindowsRuntimeStagingPublisher`](../src/VRRecorder.Compliance/Staging/ImmutableWindowsRuntimeStagingPublisher.cs)がsourceをstable handleでcopyし、destinationをCreateNew／WriteThrough／flush、再hash、length／kind／exact inventoryで再検証する。
5. payloadと[`ApprovedWindowsRuntimePropsGenerator`](../src/VRRecorder.Compliance/Staging/ApprovedWindowsRuntimePropsGenerator.cs)の出力を同じdigest directoryへ入れ、destination不存在の`Directory.Move`だけをcommit pointにする。同一digestの並行winnerは既存directoryを全検証してidempotent成功とし、異なるbyteは上書きしない。
6. Windowsでは`FindFirstStreamW`／`FindNextStreamW`でdefault `::$DATA`以外を拒否する。top-level [`WindowsRuntimeStagingOrchestrator`](../src/VRRecorder.Compliance/Staging/WindowsRuntimeStagingOrchestrator.cs)はWindows以外をfail-closedにし、portable testのGreenをWindows ADS証拠として扱わない。
7. [`VRRecorder.App.csproj`](../src/VRRecorder.App/VRRecorder.App.csproj)はRelease時にabsoluteかつ存在する`ApprovedWindowsRuntimeProps`だけをimportし、import markerとmanifest／inventory SHA-256を検査する。

このfoundationがまだ証明しないことも明確にする。

- schema v2は`full-production-hardware-validation-v1`、`win-x64`、全entryのdeclared byte length、Legal Bundle ID／manifest SHAを固定し、Release App buildでもstager生成値との一致を要求する。first-party native／factory evidence、FFmpeg 8 runtime majorの4 DLL＋ffprobe、OpenVR runtime／application manifest／action manifest／3 bindingsをexact closureにし、static-link Spout2とOS／driver提供encoder以外のruntime追加を拒否する。
- DLL／EXEはactual bytesからAMD64 PE32+、subsystem、entrypoint、通常／delay import tableまでparseする。読み取ったimportはmanifest内でexactly one staged DLLへ解決するか、明示allowlistのWindows system／API-set DLLである場合だけ認め、ambient PATH上の未登録DLLへは解決しない。
- actual production registryにはnative artifactが0件で、componentは独立承認待ちである。synthetic first-party fixtureのGreenをactual FFmpeg／OpenVR／Spout admissionと呼ばない。
- repository registry／license bytes／approval requester・reviewerから`NormalizedComponentGraph`を構築し、release eligibilityがGreenの場合だけ`ApprovedReleaseGraph`を発行するproduction trust-sourceと外部CLIを実装した。`publish-windows-hardware-validation.ps1`はCLI stagingとRelease self-contained `win-x64` publishを別invocationにし、callerから手書きpropsを受け取らず、props filenameとcontent-addressed directory名をApp buildでも照合する。現canonical registryはpending independent reviewのため実行時にffmpeg／openvr／spout2でfail-closedになる。
- post-publish sealerはexisting canonical root、normalized root-relative GUI AMD64 PE entrypoint、全fileのcanonical path／length／SHA-256／kind、Windows case-fold duplicate、reparse／ADS semanticsを再scanする。propsに固定した各runtimeのhash／length／kindとpublish結果を照合し、stagingとpublishを同時に改変した場合も拒否する。`VRRecorder.App.dll`をloadせずmanaged PE metadataからrepository source revision／RID／pinned product versionを読み、authenticated Legal catalogのproduct versionと一致した後だけinventory／EXE／build／Legal identityを発行する。全publish PEのimport再照合は次の単位に残る。
- official publish scriptはclean repositoryの`HEAD`をsource revisionとしてAppへ埋め、staging CLIが返したexact propsだけでpublishする。publish後に同じReleaseToolのsealer commandを実行し、payload外へschema v1の決定的identity JSONをCreateNew発行する。callerからproduct version／source revision／手書きprops／identity文字列は受け取らない。
- identity readerはschema v1のunknown／duplicate field、version／revision／RID／Legal anchor、canonical file path／sort／Windows case-fold重複、entrypoint membership／hash／kindをstrictに検証し、全fileからinventory digestを再計算する。report validatorはこのreaderが発行したinternal identityだけを入力にする。
- Hardware Validation report schema v1とreaderはpayload identity document hash、run ID／runner ID／UTC timestamp、Windows build、GPU／driver、encoder mode／API／実名、SteamVR／HMD／左右controller、case status、artifact path／length／hash／kindだけを受理する。unknown／duplicate field、unknown status、duplicate run／case／artifactを拒否し、serialized `Passed` propertyはschemaに存在しない。必須matrixとartifact実bytesからの合否導出は次の単位で行う。
- `Directory.Move`成功をcommit pointにしているが、power-loss durabilityや既存非空directoryのatomic replacementは主張しない。既存directoryは置換せずcontent-addressedに並置する。
- Windows ADS実テストはWindows上でだけ有効であり、今回のLinux runではP/Invoke callsite registrationとportable injection境界までの証拠である。

### 2.3 直近の検証証拠

`cf07e1b`後の最終回帰で次を確認した。

- portable CMake full build: 成功
- portable Linux native CTest: 45/45成功
- exact FFmpeg 8.1.2 contract-test SDK構成のfull build: 成功
- real FFmpegを含むLinux native CTest: 50/50成功
- managed tests: Domain 90/90、Application 282/282、Compliance 347/347、Presentation 90/90、Integration 308/308成功
- Compliance内訳の再確認: Staging filter 143/143、Distribution policy filter 18/18成功。後者はpolicy比較であり、実HIL report schemaの証拠ではない
- App Debug build: warning 0／error 0
- `dotnet format VR-Recorder.sln --verify-no-changes`: 成功
- 実AAC→実fragmented MOV integrationのASan／UBSan 10回連続成功は`18b8b03`時点の既存証拠であり、今回は再実行していない
- final packet endが3秒となるcanonical timestamp、nonempty `mdat`、encoder payloadとの完全一致、AAC bitrate metadataを確認
- owned audio graphは1024 input frameから2 real AAC packetを書き、Stop前write 0、Stop後write 2、trailer後flush 1、close 1、`mfra`生成を確認
- `git diff --check`: 成功
- GCC TSan: test process開始前にhost固有の`unexpected memory mapping`で停止。TSan成功は主張しない
- full-production factory configureは未配置の`production_media_backend.cpp`を検出して意図どおり失敗する。full-production binary成功は主張しない

coverage／mutationは今回再採取していない。前回値は[`VALIDATION-REPORT.md`](../VALIDATION-REPORT.md)にあるが、古い数値を現在値として扱わない。Windows MSVC production-link、Windows ADS HIL、別processの実MOV demux／decode、Windows／GPU／SteamVR実機は未検証である。

### 2.4 2026-07-15停止checkpoint

この節は`cf07e1b`時点の記述より新しい。矛盾する場合は、この節と[`WINDOWS-IMPLEMENTATION-HANDOFF.md`](WINDOWS-IMPLEMENTATION-HANDOFF.md)を優先する。

今回までに追加した主な論理単位:

| Commit | 完了した単位 |
|---|---|
| `7b72463`, `c2aaf2f` | FFmpeg 8.1.2由来の別process AAC demux／decode oracleと、3秒／48 kHz入力に対するMP4上の`presented_sample_count == 144000` |
| `a2bb170`, `6130ee3`〜`d53f636` | Annex B→AVCC、SPS表示寸法／crop、parameter-set IDと複数SPS／PPS、oversize拒否 |
| `16bb704`〜`7305762`, `f9c207a` | software `h264_mf(hw_encoding=0)`構成、system-memory NV12 frame、packet／descriptor生成、production H.264 attachment契約 |
| `ed1cf3a`〜`213019f` | H.264 late descriptor待ち、bounded pre-header queue、atomic cutover、producer completion／Abort仲裁、muxing sink composition |
| `3cada0a`〜`29b0dc0` | scheduled frame→software H.264、video surface ownership、D3D11 processor failure分離、owned NV12変換、keyed mutex surface同期 |
| `2f7987c` | llvm-mingw UCRTによるportable Windows native graph |
| `262bc57`〜`c01e623` | structured encoder probe ABI／evidence、deterministic NV12 probe、software FFmpeg probe sessionとfactory composition |
| `63cec76`, `a220f51` | Windows production FFmpeg SDK evidence schema v3、固定MSVC／Windows SDK、公式source、upstream patch、recipe、4 DLL＋4 import libraryの実length／SHA-256 |
| `c1ff027` | CRLF環境でもfactory selection intentをactual file bytesのSHA-256へ結合 |
| `b1e07eb` | productionとは別rootのWindows test-only FFmpeg oracle SDKを再現し、AAC／H.264 decoder、MOV demuxer、file protocol、ffprobeだけを許可する契約 |

実際に作成・検証したWindows SDK:

- production root: `C:\Users\massu\VRRecorderDependencies\ffmpeg-8.1.2-windows-msvc-x64`
  - 公式`ffmpeg-8.1.2.tar.xz`、SHA-256 `464beb5e7bf0c311e68b45ae2f04e9cc2af88851abb4082231742a74d97b524c`
  - MSVC `19.44.35228`、Windows SDK `10.0.26100.0`、shared／LGPL-2.1-or-later
  - `aac`／`h264_mf` encoder、MP4 muxer、file protocolを持ち、decoderとprogramを持たない
  - exact `avcodec-62.dll`、`avformat-62.dll`、`avutil-60.dll`、`swresample-6.dll`と対応する4 MSVC import library
  - canonical builder／recipeは[`build-ffmpeg-windows-production-sdk.ps1`](../eng/build-ffmpeg-windows-production-sdk.ps1)と[`ffmpeg-windows-production-build-recipe.md`](../eng/ffmpeg-windows-production-build-recipe.md)
- test-only oracle root: `C:\Users\massu\VRRecorderDependencies\ffmpeg-8.1.2-windows-msvc-x64-oracle`
  - productionと同じ公式source identity、MSVC、Windows SDKを使用する別build root
  - AAC／H.264 decoder、MOV demuxer、file protocol、`ffprobe.exe`だけを持ち、encoder／muxer／`ffmpeg.exe`／`ffplay.exe`を持たない
  - oracle executable専用directoryへoracle版`avformat`／`avcodec`／`avutil` DLLだけをcopyし、同名production DLLとのruntime衝突を防ぐ
  - canonical builder／recipeは[`build-ffmpeg-windows-oracle-sdk.ps1`](../eng/build-ffmpeg-windows-oracle-sdk.ps1)と[`ffmpeg-windows-oracle-build-recipe.md`](../eng/ffmpeg-windows-oracle-build-recipe.md)

停止直前の検証証拠:

- WSL Linux native full buildとCTest: 67/67成功
- .NET SDK: `10.0.301`
- Compliance: 349/349成功
- Visual Studio 2022 MSVC `19.44.35228`＋actual production／oracle SDKのRelease全target compile／link: 成功
- llvm-mingw `20260407` UCRT portable Windows native graph: build成功
- oracle出力directoryの3 DLLはoracle SDKのSHA-256と一致し、同名production SDK DLLとは3件すべて不一致であることを確認
- PowerShell builder parse、builderによる既存SDK再検証、`git diff --check`: 成功
- Windowsのunsigned test executableはSmart App Controlに阻止されるため実行していない。Smart App Controlを無効化せず、MSVC compile／linkと、署名不要のcontract／Linux実行で閉じた。Windows実行成功は主張しない

このcheckpointで意図的に未完了のもの:

- FFmpeg componentのindependent Legal review、approval ticket／requester／reviewer、canonical registry／ApprovedGraph admission。自己承認やplaceholder値は追加していない
- actual SDK rootはlocal build artifactであり、approved Release payloadやcanonical repository inputではない
- main `vrrecorder_native.dll`へのAAC 3 source、H.264 source、`FFmpeg::*` linkのproduction attachment
- `production_media_backend.cpp`、`production_encoder_probe_backend.cpp`、`spout2_source_backend.cpp`、`openvr_steamvr_input_backend.cpp`。production factoryは引き続きfail-closed placeholderである
- software H.264＋AACの3秒Windows fragmented MP4、Windows別process decode、Windows D3D11／WASAPI／Spout／SteamVR実機
- full-production staging manifest v2、PE admission、repository-derived graph builder、staging CLI、post-publish sealer。Windows引き継ぎの順序どおり、3秒production MP4より先には開始しない

次回の最初の実装単位はW0の残りである。FFmpegのactual source／recipe／binary hash、LGPL notice、source offerをcandidate evidenceへ結合し、独立review未完了なら必ずfail-closedにする。実在するapproval ticket／requester／reviewerが揃うまではW0 GreenやLegal承認済みと呼ばず、production media attachmentへ進まない。

### 2.5 2026-07-16 desktop production録画checkpoint

この節は2.4節より新しい。矛盾する場合は、本節と1節を優先する。

今回までに追加した主な論理単位:

| Commit | 完了した単位 |
|---|---|
| `b1a7eec` | actual Windows `h264_mf`＋AACの3秒fragmented MP4と別process oracle |
| `3d1a99c` | Spout capture threadとencode threadが共有するD3D11 immediate contextのmultithread保護 |
| `1a24950` | CFR frame PTSをmicrosecondではなくcodec tickとしてlibavcodecへ渡すtimestamp修正 |
| `9aabe3f` | pending fileを検証し、成功後だけfinal pathへ原子的renameするpublication順序 |
| `079b785` | audioがcontainer末尾を延長してもvideo timelineを期待録画長と照合するvalidator |
| `58750cf` | PICO WASAPIのsession前packetとshort discontinuity後device-position gapを安全に処理するstartup修正 |

実機で確認したproduction経路:

- 公式Spout Windows OpenGL sender、640×360 BGRA8、NVIDIA GeForce RTX 5070 Ti上の実shared textureを使用した。
- production `vrrecorder_native.dll`からSpout snapshot／frameを取得し、D3D11 video processor、owned NV12 readback、software `h264_mf`、AAC-LC、fragmented MP4までを同一recording sessionで通した。
- default render endpointのWASAPI loopbackへ実音声を流し、video 117 packet、audio 193 packetを生成した。
- PICO `default-capture`では、session開始前の初回packetをRelease後に読み飛ばし、discontinuity付き96-frame packet直後の384 device-frame gapを無音欠損区間として保持した。production WASAPI sourceから3秒／48 kHzを500 ms間隔で10回連続captureした。
- 公式Spout senderとPICO `default-capture`をMicOnly routingのfull production sessionへ接続し、video 110 packet、audio 179 packetのfragmented MP4を生成した。
- MicOnly fileのpinned oracleはH.264 640×360／30 fpsを110 frame、AAC 48 kHz stereoを178 decoded frame／182,272 sampleとしてerrorなしで復号した。audio presentationは0.000～3.797秒、video presentationは0.166～3.799秒である。
- pinned oracleはH.264 640×360／30 fps／3.900秒、AAC 48 kHz stereo／4.117秒、container 4.133秒を独立processで確認した。
- `.recording.mp4`をoracleで検証してから`real-spout-3s.mp4`へrenameし、成功後にpending pathが残らないことを確認した。
- MicOnly fileも`.recording.mp4`をoracle検証後に`real-spout-mic-3s.mp4`へrenameし、pending pathが残らないこととSHA-256 `CEA6EF21397DC6AFAC10D70FE309758C9C8EB50C55624E29EDEA8AEDB3A26F4F`を確認した。
- validator／finalizer関連Integration test 12/12、実機release smoke、`git diff --check`がGreenである。

未完了:

- VRChat実sender、default device change、unplug、privacy denial、無音・underrun長時間試験。
- NVENC／AMF／QSV、software fallback、part rollover。
- production AppからのSteamVR overlay表示、controller input、move／pin、実HMD readability／STOP到達性。
- full-production manifest v2以降のrelease staging、最終Hardware Validation Payload、Legal approval、MSIX。

次の開始点はOpenVR production compositionである。既存のruntime owner、lifecycle、D3D11 texture presenter、event、pose、recenter、hapticを作り直さず、production Appから実SteamVRへ接続して最初の未接続点だけをTDDで直す。

## 3. 現在の実装とplaceholderの境界

### 3.1 実装済みのproduction境界

- portable audio／video capture、normalize、mix、CFR、encoding、mux、recording session state machine
- 実FFmpeg 8.1.2 libavcodec AAC PortとAAC packet encoder
- 実AAC encoder、`MuxingAudioEncoderSink`、`StereoAudioPipelineSession`を安全な破棄順で所有する`FfmpegAacAudioPipeline`
- 実FFmpeg 8.1.2 libavformat fragmented MP4 Port
- AAC descriptorのexact 192000 bit/s伝播、negative priming／edit list境界、descriptor由来1024-frame pump window
- portable H.264 Annex B parser、SPS／PPS／IDR→avcC、length-prefixed AU、SPS crop-aware表示寸法検証
- software `h264_mf(hw_encoding=0)`構成、system-memory NV12 frame、H.264 packet encoder、first-IDR／opened-context descriptor生成
- bounded `PreHeaderCoordinator`、producer completion、H.264 first-descriptor packetとaudio／video muxing sinkのatomic cutover
- Windows D3D11 video processor Port、owned NV12 frame、keyed-mutex surface、device／surface failure identity
- structured encoder probe evidence、deterministic synthetic NV12、software FFmpeg probe sessionとproduction probe factory
- actual Windows production／test-only oracle FFmpeg SDKの再現builder、recipe、toolchain／artifact hash契約。Legal approval／Release stagingは未完了
- 4 factory familyのconfigure-time exactly-one selectorと、native binaryに結合されたfactory-selection evidence
- Windows用event-driven WASAPI source
- managed P/Invoke recording／Spout／SteamVR wrappers
- Wrist UIの状態projection、localization、Legal projection、input adapter
- OpenVR action manifestとIndex／Oculus Touch／Viveの録画toggle binding
- unpackaged EXE→MSIXのpromotion policyとfail-closedなidentity比較
- strict Windows runtime manifest、ApprovedGraph／registry／factory-evidence admission、immutable publication、決定的props、Release props-only import。ただしproduction artifact／CLI／最終publish sealerは未接続

`ffmpeg_aac_audio_pipeline_tests.cpp`はreal AAC packetがcompositionから同期`RecordingSubmission` fakeへ到達し、Stop時を含めfakeが`Written`として受理したpacket数とpipeline統計が一致することを検証する。`ffmpeg_aac_to_fragmented_mp4_integration_tests.cpp`はさらに、3秒分のreal AAC packetを実libavformatへ渡し、top-level MOV box、`mdat`全payload、bitrate metadataを検証する。owned graphではcapture→AAC→submission→実muxを接続し、Stop／JoinによるFinish drain、trailer、flush、closeまで通す。

ただし、これは同一process内の構造契約である。別processのpinned demux／decode oracle、track/sample tableの解釈、AAC-LC／48 kHz／stereoのdemux結果、MP4上のpresented sample数は`7b72463`以降のoracle testで固定した。raw AAC decoder出力はAAC frame単位のpaddingを含み得るため、`final_packet_end_microseconds == 3'000'000`やraw decoded sample数だけをpresentation長の証拠として扱わない。ここをWork package CのA/V release gate完了と混同しない。

### 3.2 明示的なportable／fail-closed variant

次の`UNAVAILABLE` factoryはportable buildと依存不足時のfail-closed variantとして残している。

- `src/VRRecorder.Native/src/unavailable_media_backend.cpp`
- `src/VRRecorder.Native/src/unavailable_spout_source_backend.cpp`
- `src/VRRecorder.Native/src/unavailable_encoder_probe_backend.cpp`
- `src/VRRecorder.Native/src/unavailable_steamvr_input_backend.cpp`

いずれも意図的に`VRREC_STATUS_BACKEND_UNAVAILABLE`を返す。一方、`production_media_backend.cpp`、`production_encoder_probe_backend.cpp`、`spout2_source_backend.cpp`、`openvr_steamvr_input_backend.cpp`は実在し、full-production buildではpinned FFmpeg／Spout2／OpenVRのcanonical targetとともにactual DLLへlinkされる。media／Spout production variantは2.5節の実録画で動作確認済みである。

factory selection evidenceが証明するのはactual binaryへlinkされたvariantとbinary identityまでである。録画HILがGreenでも、OpenVR production App composition、VRChat、audio failure matrix、Legal／Release stagingまで自動的に証明されるわけではない。

### 3.3 OpenVR／Wristの現在地

実装済み:

- action manifestの録画、mic、overlay表示、recenter、haptic action定義
- controller別recommended binding。録画toggleを左右primary button、micを左secondary／grip、recenterを右secondary／gripへ割り当て、System buttonを使わないmachine contractとfirst-runのrecord／mic／recenter active確認を固定した。overlay表示／hapticのbindingと全actionの実runtime検証は未完了
- pinned OpenVR 2.15.6 SDK／runtime identity、candidate Legal evidence、production DLL link。独立Legal approvalとcanonical admissionは未完了
- stable app keyを持つapplication `.vrmanifest`、current install contract、runtime generationごとのtemporary `IVRApplications`登録
- OpenVR SDKを呼ぶnative `SteamVrInputBackend`のinit、action/application manifest、handle取得、`UpdateActionState`、digital polling
- process-wide single runtime ownerと最大90 Hzのsingle poll thread。同じrevisionをrecord／mic／recenter actionへfan-outし、App／first-runは1つのlazy managed runtimeを共有する
- renderer／pose／input／hapticから分離したoverlay lifecycle Port。有限な0.18～0.32 m幅、hidden create、idempotent Show／Hide、失敗rollback、exactly-once Destroyを固定し、process ownerとpinned OpenVRの実`IVROverlay`へ接続済み
- stable key／name、current installのapplication manifest path、有限幅と40-byte BGRA frame descriptorを検証し、Update／Clear／Show／Hide／Close／Destroyの所有権を固定するversioned native C ABI
- current installを解決してC ABIを呼び、overlay SafeHandleをnative DLLより先に破棄するmanaged lifecycle wrapper
- `ReadOnlyMemory<byte>`のarray offsetを保ち、同期C ABI call中だけBGRA bufferをpinしてUpdate／Clearするmanaged lifecycle wrapper。overlay SafeHandleはnative DLLより先に破棄する
- 1024×512／2倍densityのpure Wrist layout。stable element ID、pixel bounds、z-order、64／56 dp target、RTL mirror、disabled非dispatchのray hit-testを固定済み
- 解決済みthemeとallowlist済みraster providerだけを入力にするopaque BGRA compositor。英日、200% text scale、RTL、high contrast、missing asset fail-closed、synthetic golden SHA-256を固定済み
- elapsed、canvas resolution、target／actual FPS、Spout／desktop audio／mic health、warning／fault、placement modeとInvariant表示文字列を保持する検証済みWrist telemetry snapshot
- 初回／revision変化は即時、Recording／SignalLost中だけ100 ms周期とし、publish成功後だけnext cursorを採用できるpure texture update policy
- lifecycle Portと分離したnative texture Port。1024×512 BGRA／stride／bufferを検証し、成功後だけtexture-set、Clear冪等、CloseでClear→Hide→Destroy、Clear失敗時も後段cleanup継続を固定し、input／lifecycleと同じprocess-wide OpenVR ownerのruntime generationへ接続済み
- HMDの`GetDXGIOutputInfo` adapter上で作る1024×512 `B8G8R8A8_UNORM` dynamic texture presenter。実RowPitchでBGRAをuploadし、`TextureType_DirectX`／`ColorSpace_Auto`で`SetOverlayTexture`へsubmitする。未submitの失敗resourceは即解放し、submit済みresourceは`ClearOverlayTexture`成功まで保持する
- renderer／pure update policy／publisherを直列化するmanaged texture update host。初回／revision変化とRecording／SignalLostの100 ms heartbeatだけをpublishし、初回publish→Show成功後だけcursor／visibleをcommitする。publish／Show失敗時は同一revisionを再試行できる
- Wrist pixel座標をz-order済みstable semantic targetへhit-testし、current snapshotのenabled actionとsemantic ID／commandが一致するときだけ既存`IUiCommandDispatcher`へ`WristRay`としてdispatchする入力adapter。miss／stale／重複targetは副作用なく無視する
- lifecycle／textureと分離したnative overlay event Port。input／lifecycle／textureと同じprocess runtime generationへ接続し、実`SetOverlayInputMethod(Mouse)`／1024×512 mouse scale／`PollNextOverlayEvent`を呼ぶ。OpenVRのGL左下座標をtop-left pixelへ上下反転し、Move／ButtonDown／ButtonUpだけをbutton／bounds検証後に返す。configure失敗はDestroy rollback、不正runtime eventはゼロ化してfail-closedにし、32-byte versioned C ABIから1件ずつ公開する
- managed lifecycleの同期pointer event poll。no-eventを`null`、既知kind／button／1024×512範囲だけを型付き値へ変換し、Close／Dispose／不正payloadを既存のlifetime／例外規約へ収束させる
- 表示成功済みsnapshot／layoutだけでpointer eventをhit-testするpure interaction host。primary down／upをcursor別に追跡し、duplicate downと同一revisionの二重commandを抑止し、1 tick最大64件に制限してmove floodから描画を保護する
- native overlay lifecycleを`IWristTexturePublisher`／`IWristPointerEventSource`へ適合するmanaged adapter。rendererのframe metadataをそのままUpdateし、OpenVR Left／Right／Middleをpresentation Primary／Secondary／Middleへ変換し、lifecycleを単一所有・破棄する
- recorder status subscriptionをatomicな最新1件へ畳み、初回即時かつ最大90 Hzでinteraction coordinatorを駆動するone-shot background host。遅いtick後はcatch-up連打せずmonotonic nowから再基準化し、cancel／fault時に必ずunsubscribeする
- settings schema v3。旧v1のglobal hand／mode／transformとv2のdevice別配置を無損失に移行し、OpenVRのtracking system name、HMD model number、controller input profile path、left／rightのexact keyごとに最大64件の配置を保存・選択・置換する。haptics enabled、120 Hz、amplitude 0.65の初期product tokenを保持し、v1／v2／v3 JSON Schemaを別identityで同梱してv3保存値をoffline検証する
- [`ADR-0008`](adr/0008-openvr-overlay-pose-contract.md)のpure pose contract。右手系+X右／+Y上／-Z前、m、pitch-X／yaw-Y／roll-Z degree、`Rz*Ry*Rx`、Standing World Pin、0.5 mm／0.1 degree readback許容差、5／20 mm nudge、120／80 mm detach／reattach hysteresisを固定し、初回setupのreadbackもEuler完全一致ではなく同じ行列許容差で判定する
- lifecycle／texture／eventから分離したnative pose Portと72-byte versioned C ABI。Wrist Dockをleft／right tracked-device-relative、World PinをStanding absolute transformとして実`IVROverlay`へ適用・readbackし、mode／hand／origin、有限値、±100 m、右手系直交回転行列をABI境界とlifecycle境界でfail-closed検証する。同じprocess runtime generationへ接続し、managed lifecycleでは`OverlayTransform`と型付きreadbackへ変換する
- selected handの実controllerとHMDからtracking system name／HMD model number／controller input profile pathを取得するdevice-profile query。同じprocess runtime generationで`GetStringTrackedDeviceProperty`を呼び、40-byte descriptor＋必要サイズ付きpacked UTF-8 C ABIからmanagedのexact `VrDeviceProfile`へ厳格decodeする
- runtime identityからexact profileまたは旧global fallbackを選び、selected handへApplyした後にmode／hand／Standing origin／0.5 mm・0.1 degree matrix readbackを検証してからだけexact profileを保存するproduction placement coordinator。small／large nudgeは現在modeの親空間X／Yへ適用し、recenterは安全な既定Wrist Dockへ戻す。native lifecycleは同じprocess runtimeをこのApplication Portへ公開する
- Appの録画host Ready後に起動する`recenter_overlay` observer。active rising edgeだけを同じlazy input runtimeからproduction placement commandへ渡し、lazy native overlay lifecycleを生成してApply／readback／settings保存まで通す。mic／recenter observerの片方が利用不能でもrecording inputを止めず、App終了時はobserver停止後にcoordinator／overlayを破棄する
- start 30 ms×1、stop 20 ms×2、fault 80 ms×1を生成するpure haptic policy。duration／pulse count／frequency／amplitudeを検証し、disabledと同一／旧revisionを出力せず消費する。controller disconnect等のoutput例外は型付き失敗結果へ隔離し、後続revisionを妨げない
- input／overlayと同じprocess runtime generationを共有するnative haptic Portとproduction backend。`actions.json`から同階層のapplication manifestを一つの共通contractで導出し、temporary登録→action manifest→haptic action／controller input source handleの順で初期化する。正の有限duration、有限な非負frequency、0超1以下amplitudeの単発pulseだけを実`IVRInput::TriggerHapticVibrationAction`へ渡し、途中失敗は取得済みruntimeを解放する。trigger中はruntime lockで最終shutdownと直列化する
- 40-byte haptic configと24-byte単発pulseを持つversioned C ABI。absolute action manifest、stable haptic action、left／right input source、reserved、duration／frequency／amplitudeをhandle生成／backend呼出し前に検証し、create／trigger／destroyとbackend error伝播を32-symbol export allowlistへ追加する
- current installのaction manifestと選択handをC ABI configへ変換し、SafeHandleをnative DLLより先に破棄するmanaged haptic output。Application patternのpulse countをduration間隔の単発ABI callへ展開し、native statusを型付きmanaged例外へ変換する。left／right path、2 pulse、backend failure、Dispose後のnative handle破棄を実fixtureで固定する
- recorder statusをsingle-reader queueへ順序保持して録画threadを塞がず、Recording成功でstart、Stopping後のReadyでstop、SignalLost／NoSignal／Faultedでfaultを発火するhaptic observer。App host Ready後にschema v3のenabled／frequency／amplitude／selected handを読み、実managed native outputへ接続する。output生成／trigger失敗はTrace診断だけへ隔離し、後続transitionと録画結果を壊さない
- first-run setup 7の明示的なVERIFY操作から同じlazy native overlay lifecycleをShowし、保存済みmode／selected hand／tracking origin／poseと実readbackが一致した場合だけ完了するproduction verifier／router接続
- first-run setup 8を通常のproduction recording host／finalizationへ接続するverifier。host初期化後に録画権利確認を先行し、Ready→Recordingからmonotonic 3秒後にStopする。既存ffprobe検証とatomic publishを通ったSaved通知、Ready復帰、Windows既定player起動が揃った場合だけ完了し、cancel／失敗時はbest-effort Stopへ収束する
- native digital-state ABIとmanaged async stream
- Wrist状態／Legal UIのViewModel相当projection

未実装:

- OpenVR candidateの独立Legal approval、canonical native registry admission、最終full-production staging
- overlay表示／hapticのcontroller binding、mic／recenterを含む全actionの実runtime検証
- production telemetryの全項目採取・表示
- 実controllerでのWrist Dock／左右ray／drag／再接続試験

pure renderer、D3D11/OpenVR texture ownership、managed lifecycle、interaction coordinatorに加え、production glyph／native lifecycle adapter／App composition／pose／haptic／first-run verifierとWorld Pinの実HMD表示までGreenである。実controllerを必要とするWrist Dock／左右ray／drag／再接続はcontroller standby中のため未完了として分離する。

## 4. 未完了release gateの実装段階

この表のP0／P1／P2はfailure severityではなく、依存関係に基づく実装段階を表す。failure severityと個別境界の正本は`NATIVE-PIPELINE-FAILURE-MATRIX.md`である。P0は最初のdesktop EXE bring-upに必要、P1は最終Hardware Validation Payloadの合格前に必要、P2は合格payloadをMSIXへ昇格した後に必要、という意味である。P1／P2もreleaseを阻止するため、省略可能という意味ではない。

| 段階 | Gate | 現在の証拠 | 閉じるまでできないこと |
|---|---|---|---|
| P0 | production AAC接続 | AAC sourceとcanonical FFmpeg targetはproduction variantへ接続済み。real encoder→実MOV、3秒presented sample、別process decode、full-production DLL linkはGreen。独立Legal admissionは未完了 | Release admission |
| P0 | H.264 encoder／AVCC契約 | software `h264_mf`、NV12、AVCC、production attachment、90-frame synthetic encodeに加え、実Spout capture surfaceから117 packetを生成して別processでprobe済み | hardware encoder拡張 |
| P0 | hardware encoder identity／probe | ADRのcodec identityに加え、structured evidence pipeline、deterministic NV12、software probe sessionはGreen。production probe factoryとNVENC／AMF／QSV実SDK／実機は未完了 | 正しいprobe／表示／fallback |
| P0 | 実encoder→mux composition | 実Spout／WASAPI入力からdesktop loopback版H.264 117 packet＋AAC 193 packet、およびPICO MicOnly版H.264 110 packet＋AAC 179 packetのfMP4を生成し、pending検証後だけfinal公開するdesktop recordingがGreen | VRChat／音声failure matrix |
| P0 | approved runtime staging | manifest v2、full-production closure、PE admission／import closure、repository graph builder、staging／sealer CLI、official publish、post-publish runtime／Legal／build identity JSONは自動test Green。actual canonical registryは独立Legal承認待ちでfail-closed。全publish PE import再照合が未完了 | reproducible full Release payload |
| P0 | Spout2 receiver | production factoryから公式Spout senderのsnapshot／frameを取得し、recording sessionへ接続済み。VRChat senderは未検証 | VRChat frame取得 |
| P0 | D3D11 processor | 実Spout shared textureをGPUでNV12変換・readbackし、共有immediate contextのmultithread保護を実GPU testと録画で確認済み | device lost／長時間HIL |
| P0 | WASAPI Windows contract | default render loopbackから実AAC packetを生成済み。PICO `default-capture`のdirect sourceはstartup discontinuityを含む3秒captureを10回連続で通過し、MicOnly full production muxも179 AAC packet／182,272 decoded sampleをpinned oracleで確認済み。device change／unplug／privacy／長時間underrunは未検証 | Windows音声failure matrix |
| P0 | production media factory | production DLLのSpout→D3D11→H.264、WASAPI→AAC、pre-header→fMP4、managed finalizationまで実録画Green。production App UI／VRChat／Legalは未完了 | product-level bring-up |
| P1 | encoder fallback／part rollover | failure前part保持、hardware failure後のsoftware rollover、hardware開始失敗時のclean software file retryは自動test Green。実hardware HILは最終batchへ保留 | 基本設計§11.2の実機適合 |
| P1 | unpackaged hardware payload | canonical identity発行／strict再読込とversioned report schema／readerはGreen。required matrix／artifact実bytes validatorは未完了 | 実機証拠の合否固定 |
| P1 | Windows hardware E2E | 未実施 | MSIX候補への昇格 |
| P1 | OpenVR input／overlay一式 | runtime owner、native lifecycle／texture／event／pose／haptic、production glyph、App表示接続、Dock／Pin／nudge／recenter、drag release、World Pinでの実HMD表示はGreen。実controllerでのWrist Dock／drag／左右入力、再接続HILが未完了 | Wrist操作・表示 |
| P1 | first-run setup 7／8 | setup 7はproduction overlayのShow＋mode／hand／origin／pose readback、setup 8はproduction 3秒録画＋ffprobe済みSaved＋既定player起動までGreen。実VRChat／controllerを含む全行程HILは未完了 | setup完走 |
| P1 | coverage／mutation／UI Automation | 90%未達／未測定 | release quality gate |
| P1 | final Legal Bundle／承認 | candidate台帳、承認済みnative 0 | 配布・署名 |
| P2 | MSIX／Store submission | policyのみ、packaging projectなし | Store提出 |

## 5. 次回からのTDD実装順

各work packageは、Red commitまたは少なくともRedが実際に失敗することを確認してからGreenへ進める。複数外部adapterを一度に実装しない。

### Work package 0: Windows dependencyとencoder identity（FFmpeg actual artifact完了、Legal／vendor artifact未完了）

完了済み:

1. [`ADR-0006`](adr/0006-encoder-identity-preheader-and-production-factory-contract.md)でpublic encoder kindと実FFmpeg codec、same-adapter LUID、QSV mapping、packet-based probe、late extradata、factory evidenceを確定した。
2. media／encoder probe／Spout／SteamVRの4 familyを`UNAVAILABLE`／`PRODUCTION`からexactly one選ぶ。未知値、source欠落、pinned FFmpeg欠落、full-production不完全をconfigure時に拒否する。
3. selection intent digestをnative binaryへ埋め、actual binary filename／length／SHA-256へ結合したevidenceだけを生成する。validな別intentへの差替えもtestで拒否する。
4. strict staging manifest v1、Windows path grammar、source exact inventory、ApprovedGraph owner／runtime scope、native registry、factory evidence consumerを実装した。
5. immutable digest directoryへcopy／再hash／再inventoryし、copy途中、post-copy tamper、extra file、commit失敗、commit前cancellation、同一digest並行実行を決定注入するtestを実装した。
6. explicit Contentだけを持つ決定的`ApprovedWindowsRuntime.props`を生成し、Release Appをそのprops-only importへ変更した。旧3 direct propertyはDebug bring-upにだけ残る。
7. Windows named data streamを`FindFirstStreamW`で拒否し、production top-level orchestrationをWindows-onlyにした。
8. 公式FFmpeg 8.1.2 sourceからWindows x64 shared production SDKをMSVCで再現生成し、AAC／software `h264_mf`／MP4 muxに限定したLGPL-only構成、4 DLL＋4 import library、source archive、upstream patch、recipe、toolchain、実length／SHA-256をevidence schema v3へ固定した。
9. 同じsource identityからWindows test-only oracle SDKを別rootへ再現生成し、AAC／H.264 decoder、MOV demuxer、file protocol、ffprobeだけを持つことと、production DLLへ混入しないことを固定した。

次に行うartifact phase:

1. actual FFmpeg source／recipe／artifact hash、LGPL notice、source offerをcandidate evidenceへ結合する。実在するapproval ticket／requester／independent reviewerがない状態では`ApprovedReleaseGraph`を発行しない。
2. Legal admissionが実際にGreenになった後だけ、Work package AのAAC 3 sourceとcanonical `FFmpeg::*` targetをproduction media variantへliteralに接続する。
3. software H.264＋AACの3秒Windows fragmented MP4と別process decodeを先に成立させる。
4. Spout2／OpenVRのexact tag／commit、source archive、license、static／dynamic deploymentを確定する。actual binaryがない段階でcanonical registryへplaceholder hashやapproved entryを入れない。
5. 3秒production MP4成立後にschema v2をRedから追加し、`profile=full-production-hardware-validation-v1`、`runtimeIdentifier=win-x64`、declared `length`、Legal Bundle ID／manifest SHA、required role closureを固定する。v1を暗黙拡張せずversionを上げる。
6. PE admission reader、repository-derived graph builder、external staging CLI、two-invocation publish、post-publish sealerをそれぞれ独立したTDD単位で追加する。

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
8. 当時のportable 45/45、exact FFmpeg 50/50、ASan／UBSan repeat 10回に加え、2026-07-15 checkpointではWindows oracle contractを含むnative 67/67までGreenである。

残作業は次の順に行う。

1. actual FFmpeg componentのcandidate Legal evidenceをsource／recipe／binary identityへ結合し、独立reviewを通す。未reviewのままapprovalを自己申告しない。
2. main `src/VRRecorder.Native/CMakeLists.txt`のWindows production blockへAAC 3 sourceと`FFmpeg::avcodec`、`FFmpeg::avutil`、`FFmpeg::swresample`をliteral記述する。helper内の`target_link_libraries`や`${links}`展開でComplianceのcallsite検出を回避しない。
3. main DLLへのlink追加時はComplianceが要求するruntime artifact、source archive、build recipe、hash、approvalを同じcommit系列で揃える。`FFmpegContractTest::*`、ambient `find_package`、unversioned library、placeholder hashへfallbackしない。
4. Windows MSVC＋actual pinned SDKでproduction DLLをlinkし、test executable横へ`avcodec-62.dll`、`avutil-60.dll`、`swresample-6.dll`をapproved staging経路で配置する。
5. `production_media_backend.cpp`が後続のWork package Cから所有できるAAC composition factory seamを公開し、actual pinned SDKを使うproduction variantのfactory smoke testを通す。ここまでをWork Aの「production AAC component Green」とし、capture／`PreHeaderCoordinator`／muxへの接続はWork package Cの完了条件にする。

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

現在の`VRRecorder.App.csproj`はRelease時の直接property stagingを廃止し、absoluteな`ApprovedWindowsRuntimeProps`、import marker、manifest／inventory SHA-256を必須にした。旧`NativeMediaLibraryPath`、`FfprobeExecutablePath`、`FfmpegRuntimeDirectory`はDebug bring-upだけで使える。stagerはfull-production manifest v2とrepository-derived ApprovedGraphを通したruntimeだけをimmutable digest directoryへcopyし、CLIとofficial scriptがself-contained publishへ渡す。App buildはrepository-pinned product version、clean repository HEAD、RIDをassembly metadataへ埋め、post-publish sealerがruntime byte identity、authenticated Legal anchor、build identityをfinal inventoryへ取り込む。identity JSONはpayload外へ発行されるためsealed inventory自体を変更しない。現canonical registryの独立Legal承認が未完了のため、Release admissionは引き続きfail-closedである。

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

App host、録画、mic、first-run probeはthread-safe lazyな一つのmanaged runtimeを共有する。native process ownerは`VR_Init`／`VR_Shutdown`をruntime generation単位に集約し、最大90 Hzの一つのbackground pollで`UpdateActionState`と全登録digital actionを同じrevisionへ採取する。遅いconsumerは中間revisionをskipして最新snapshotを読むためpoll ownerを停止しない。native overlay lifecycle／event／poseとmanaged pose transportはownerへ接続済みで、production placement coordinatorとrecording-status haptic observerも同じprocess ownerのproduction Portへ接続した。

### 7.1 Input Red

- init失敗、runtime再起動、manifest path失敗。
- `SetActionManifestPath`を最初の`UpdateActionState`／event pollより前にexactly once行う。
- action set／digital／haptic handle取得失敗。
- inactive action、rising edge、held state、controller reconnect。
- 一つのpoll sampleをrecord／mic／overlay consumerへ同じrevisionでfan-outし、遅いconsumerがpoll loopを止めない。
- 二重runtime init、consumer追加／削除中のshutdown、subscriber callback中Abort。
- Poll中shutdown、Abort、destroy。
- callback／poll threadからのself-join禁止。

現行controller bindingは`toggle_recording`に加えてmicとrecenterを全Index／Oculus Touch／Viveへ割り当て、first-runもrecord／mic／recenterのactive stateを要求する。overlay表示とhapticにはまだbindingがなく、`mute_all`とmove／pin／nudge commandはaction path自体がない。操作をoverlay ray eventで提供するかcontroller actionで提供するかを先に決め、全controllerでhaptic outputとSTOP到達性をmachine testする。button component pathは公式OpenVR資料とWindows上のSteamVR driver bindingで照合したが、実controllerでの誤操作性と再割当はHIL未完了である。

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
- first-run routerの`WristOverlayPlacement` routeは、明示的なVERIFY操作をuser confirmationとし、同じlazy production lifecycleのShow成功とmode／selected hand／tracking origin／pose readback一致時だけsetup 7を完了する。

settings schema v3へのmigration、pure pose contract、native pose Port、72-byte pose C ABI、managed adapter、runtime device-profile query、production placement coordinator、物理recenter routeはRed→Green済みである。旧v1のglobal値は未知profile用fallbackとして保持し、v2のdevice別配置も無損失に引き継ぎ、tracking system／HMD model／controller input profile／left・rightのexact keyでprofileを分離した。軸、m／degree、Standing origin、行列順、readback許容差、drag hysteresis、small・large nudge量は[`ADR-0008`](adr/0008-openvr-overlay-pose-contract.md)で固定した。coordinatorは取得済みruntime identityからprofileを選び、Wrist Dock／World Pinを実OpenVRへApplyした後、mode／hand／origin／matrixが一致した場合だけprofileを保存する。small／large nudge、安全な既定Wrist Dockへのrecenter、親空間を再利用しないDock／Pin変換も同じ経路で永続化する。MOVEは16 px未満をPositioning pageへのtap、16 px以上をdragとして分離し、0.22 m／1024 pxでparent-space X／Yへ換算する。releaseでは既定Dockからの距離へ120／80 mm hysteresisを適用し、必要なcontroller-relative／Standing absolute変換後の確定poseだけを保存する。同じpointer drain内にSTOPがあればdrag確定よりSTOPを優先する。World Pinでの日本語Main／Positioning textureは実SteamVR／HMDへ表示済みだが、SteamVRが左右controllerを未接続として返したためWrist Dock、drag、左右ray、再接続の実機確認は残る。move／pin／nudge用action pathは現manifestにないため、overlay rayだけで提供する操作と物理bindingへ割り当てる操作は引き続き分離する。

### 7.5 Haptics Red

- start 30 ms×1、stop 20 ms×2、fault 80 ms×1。
- amplitude／duration validation、disabled設定。
- visual／text eventが常に併存し、haptic失敗が録画結果を壊さない。
- 同じrecording transition revisionを再配信してもpulseを二重発火しない。
- pulse列の途中でcontroller disconnect／runtime shutdownしてもuse-after-free、self-join、録画Abortを起こさない。

durationに加えて初期product tokenを120 Hz／amplitude 0.65、出力先をsettingsで選択中のhandへ固定した。controller不在やtrigger errorは診断だけへ残して録画結果と分離する。実controller HILでtoken調整が必要になった場合もschema v3設定値の変更で対応し、pulse duration／count契約は変えない。

Applicationのpure policyとproduction compositionはRed→Green済みである。start／stop／faultのdurationと回数を固定し、有限な非負frequency、0超1以下のamplitude、正のduration／pulse countをPort境界で検証する。disabledと同一／旧revisionは出力せず、出力失敗を型付き結果として返して後続revisionを継続する。native側も同じprocess runtime generationへ独立haptic Portとproduction backendを追加し、application／action manifest設定、実OpenVR action／input source handle取得、単発trigger、値検証、初期化失敗cleanup、最終shutdownとの直列化を固定した。40-byte config／24-byte pulseのC ABIとexport allowlist、selected handとpulse列を渡すmanaged SafeHandle adapterもGreenである。schema v3はenabled／frequency／amplitudeを保持してv1／v2からmigrationし、Appのsingle-reader observerがRecording成功、停止成功、SignalLost／NoSignal／Faultedを実managed outputへ順序どおり渡す。生成／trigger失敗はTrace診断だけに隔離する。

OpenVRの`IVROverlay`はoverlay作成、texture、transform、event、visibilityを別APIとして持つため、単一巨大adapterにしない。lifecycle、renderer、pose／transform、input、hapticをPortで分離する。

## 8. MSIXは実機合格後に行う

[`adr/0003-two-stage-windows-distribution.md`](adr/0003-two-stage-windows-distribution.md)を維持する。

現在のrepositoryには`.wapproj`、`Package.appxmanifest`、MakeAppx／SignTool／WACK／Partner Center用script、packaging CIがない。MSIXを生成した証拠は0である。先に`PostPublishPayloadSealerTests`でunpackaged directoryのcanonical root、exact `VRRecorder.App.exe`、PE x64、全inventory、version／revision／RID／Legal anchorをactual bytesから導出し、自由なcaller文字列から`ValidatedPayloadIdentity`を作れない境界をGreenにする。

### 8.1 Packaging Candidate

1. WPF app本体とは別にWindows Application Packaging Projectを追加する。
2. project／manifest reader／fixture testは先行実装できるが、実candidateは合格済み`ApplicationPayloadIdentity`のimmutable artifactを入力にし、packaging HEADからapplicationを再publishしない。
3. Partner Centerが発行した実`Identity/Name`、`Identity/Publisher`、`PublisherDisplayName`を使用する。
4. x64、`TargetDeviceFamily=Windows.Desktop`、desktop executable／entry point、mediumIL／full-trust、`runFullTrust` restricted capability、versionをmachine validationする。
5. mic captureに必要なcapabilityだけを宣言し、不要なbroad capabilityを追加しない。
6. MSIXを展開し、packaging metadataを除くinner application payloadをunpackaged合格payloadとpath／length／SHA-256／kind単位で照合する。package固有assetも別inventoryへ全件含めてLegal scanする。
7. local sideload certificateのsubjectをmanifest Publisherと完全一致させる。
8. package生成成功だけでは`PublishEligible`にしない。

MSIX側の最初のRedは`MsixPackagingContractTests`とし、凍結済みpayload directory以外の入力、App `ProjectReference`等による再build、placeholder identity、wrong architecture／entrypoint／trust declarationを拒否する。次にMakeAppxで展開したinner payloadのpath／length／SHA-256／kindが凍結済みinventoryと完全一致するtestを追加する。

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
- latest available WACKをlocal preflightとして実行し、HTMLだけでなくXML reportを保存・parseしてfail／not-run／inapplicableをmachine判定する。tool非対応／実行不能は黙ってskipせず、SDK／WACK versionと理由を記録してrelease ownerの明示waiverを要求する。
- final MSIX payload、Legal Bundle、SBOM、source offerを再scan。
- exact `.msixupload`に対するPartner Centerの公式certification成功を必須にする。
- private flightでinstall／upgrade／hardware smokeを再検証後だけStore公開を許可。

秘密鍵をrepositoryやartifactへ入れない。Store提出用packageの本番署名はStore側へ委ねる。

Microsoft公式資料の表現には差がある。Visual Studio packaging手順はWACKをdeprecated／optional local checkと記す一方、現行certification手順はStoreのtechnical complianceにWACKを使い、提出前にlocal WACKを常に実行するよう推奨する。またpackage flightで一部WACK failureが`passing with notes`になっても、一般公開前には修正が必要である。このrepositoryでは保守的に「実行可能なlatest WACKのlocal result＋exact uploadのPartner Center certification」を要求し、local toolが実行不能な場合だけ理由付きwaiverとflight証拠へ置き換える。Partner Center certificationをlocal WACKで代替しない。

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

real FFmpeg contract SDKが`/tmp/vrrecorder-ffmpeg-contract-v2`、別process demux/decode oracle SDKが`/tmp/vrrecorder-ffmpeg-oracle-v1`にある場合:

```bash
VRRECORDER_FFMPEG_CONTRACT_TEST_ROOT=/tmp/vrrecorder-ffmpeg-contract-v2 \
VRRECORDER_FFMPEG_CONTRACT_ORACLE_ROOT=/tmp/vrrecorder-ffmpeg-oracle-v1 \
  cmake --preset native-linux-debug-ffmpeg
cmake --build --preset native-linux-debug-ffmpeg
ctest --preset native-linux-debug-ffmpeg
```

実AAC→実MOVだけをASan／UBSanで再確認する場合:

```bash
cmake -S . -B build/native-linux-debug-ffmpeg-mux-sanitize \
  -DVRRECORDER_FFMPEG_CONTRACT_TEST_ROOT=/tmp/vrrecorder-ffmpeg-contract-v2 \
  -DVRRECORDER_FFMPEG_CONTRACT_ORACLE_ROOT=/tmp/vrrecorder-ffmpeg-oracle-v1 \
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

staging foundationだけを再確認する場合:

```bash
dotnet test tests/VRRecorder.Compliance.Tests/VRRecorder.Compliance.Tests.csproj \
  --no-restore \
  --filter 'FullyQualifiedName~WindowsRuntimeStaging|FullyQualifiedName~ApprovedWindowsRuntimeProps|FullyQualifiedName~NativeFactorySelectionEvidence|FullyQualifiedName~ImmutableWindowsRuntimeStaging'
dotnet test tests/VRRecorder.Presentation.Tests/VRRecorder.Presentation.Tests.csproj \
  --no-restore \
  --filter FullyQualifiedName~ReleaseDesktopPublishImportsOnlyApprovedWindowsRuntimeStaging
```

`--no-restore`はlocked restore済みのworkspaceでだけ使う。Windows Release publishは先にstagerを別invocationで実行し、そのexact `ApprovedWindowsRuntime.props` pathを新しいpublish invocationへ渡す。現状はproduction graph builder／CLIとactual approved artifactsがないため、このend-to-end commandはまだ存在しない。hand-written props、placeholder path、ambient PATHでgateを回避しない。

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
- `ApprovedWindowsRuntime.props`を手書きし、marker／digestだけを合わせてapproved stagingを迂回しない。
- portable Linuxのsymlink／fault-injection GreenをWindows named-stream／reparse HILの成功と呼ばない。
- content-addressed directoryをmutableな`current` directoryで上書きしない。既存非空directoryのatomic replacementを`Directory.Move`へ期待しない。

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
- full-production manifest v2、repository-derived ApprovedGraph、authenticated Legal anchorを通したruntimeだけをimmutable digest directoryからtwo-phase publishする。
- publish後の全managed／native／self-contained .NET／asset／Legal fileを再scanし、missing／extra／hash／length／kind差を0件にする。
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
- sideload／packaged固有回帰／hardware再試験が成功し、latest available WACKのXML reportが合格する。tool非対応時は理由付きwaiverとPartner Center flight証拠を残す。
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
- Windows App Certification Kit local validation: <https://learn.microsoft.com/en-us/windows/uwp/debug-test-perf/windows-app-certification-kit>
- Microsoft Store MSIX certification process: <https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/app-certification-process>
- Microsoft Store package flights: <https://learn.microsoft.com/en-us/windows/apps/publish/package-flights>
- Windows filename／device／case rules: <https://learn.microsoft.com/en-us/windows/win32/fileio/naming-a-file>
- Windows reparse points: <https://learn.microsoft.com/en-us/windows/win32/fileio/reparse-points>
- Windows alternate data stream enumeration: <https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-findfirststreamw>
- .NET `Directory.Move` destination contract: <https://learn.microsoft.com/en-us/dotnet/api/system.io.directory.move>
- MSBuild evaluation／execution and Import timing: <https://learn.microsoft.com/en-us/visualstudio/msbuild/build-process-overview>
- Windows DLL loading security: <https://learn.microsoft.com/en-us/windows/win32/dlls/dynamic-link-library-security>
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

> `docs/IMPLEMENTATION-HANDOFF.md`の1節と「2.5 2026-07-16 desktop production録画checkpoint」を正として、`58750cf`までを実装baselineにして再開して。FFmpeg／Spout2／D3D11とdefault-render／PICO MicOnly WASAPIのdesktop production録画、pending oracle検証、final publicationは作り直さないで。既存OpenVR runtime owner、lifecycle、texture、event、pose、recenter、hapticをproduction Appから実SteamVRへ直接接続し、最初に失敗する未接続点だけをRed→最小Greenにして。production glyph、move／pin、実HMD表示／左右入力／STOP到達性までを縦に完成させ、その後にVRChat sender、audio default切替／抜去／privacy、fallback／part rollover、manifest v2以降のstaging、最終payload、Legal、MSIXへ進めて。Fast loopは変更対象だけ、commit gateは同subsystem、full regressionは縦切りの最後に1回とし、各論理単位で必ずcommitして。
