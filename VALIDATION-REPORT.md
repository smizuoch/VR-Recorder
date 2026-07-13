# Validation report / 検証報告

## 日本語

### 現在の判定

実装進行中であり、release適格ではありません。2026-07-13現在、Linux／WSL2で実行できるmanagedテスト、Windows x64向けWPF cross-build、Linux native ABIを検証しています。Windows上でのWPF実行、Spout2、D3D11、WASAPI、FFmpeg、OpenVR、実VRChat、Windows 10／11、GPU／HMDの検証は未実施です。

desktop production compositionはP/Invoke Spout source、encoder probe、native recording engine、OSC、storage、Legal mirror、runtime fault stop、SteamVR inputまで配線済みです。stale CameraLease／未確定録画の起動時回復、複数VRChatの厳密選択、VRChat service単位のSpout sender前回選択と曖昧時のdesktop prompt、録画設定UI、4状態tray、保存path／カメラ復元警告、bounded callback queueとsession-scoped UIを備えた音声device喪失／復旧の非terminal通知、media profile／最終native統計を含むprivacy-safe診断bundleの明示exportも配線済みです。録画中のMic／Muteはnative ABIからdesktop／wrist／SteamVR入力まで復元可能な状態を保って配線済みです。native内部にはPCM／floatとsample rate／channel差を48 kHz stereoへ正規化する処理、event-driven WASAPI loopback／microphone source、packet境界／gap／device loss／recovery／Abortを扱うcapture timelineと再探索runner、WASAPIのStart／Readを同一専用threadへ固定して同期初期化とRAII joinを行うworker、desktop／microphoneの開始rollbackと同一frame window mixを所有するcapture sessionがあります。ただしこれらをencoder／muxerへ接続するproduction native media backendとSteamVR backendは意図的に`BACKEND_UNAVAILABLE`を返します。承認済みWindows x64 native DLLとffprobeもRelease入力として未提供です。したがって、現状は設計契約と境界実装を検証する開発checkpointであり、録画可能な製品ではありません。

第三者台帳の現在のentryはtest-only NuGet依存のcandidateです。version、NuGet content hash、package archive SHA-256、上流commit、license全文hashを固定していますが、全entryの`approval.status`は`pending-independent-review`です。native link／runtime-load manifestは現行のfirst-party、Windows system、toolchain call siteを照合し、未登録追加をcandidate gateで拒否します。最終staging gateはPE内容、所有者、runtime scope、台帳schema／承認、component version／source commit、binary／source archive hashをLegal Bundle生成前に照合し、standalone native componentをTXT noticeとSPDXへ含めます。ただし承認済み第三者native componentはまだ0件です。署名・公開・end-user Legal Bundleの承認を意味しません。

### 自動検証

2026-07-13に次を実行し、成功を確認しました。

```text
dotnet test tests/VRRecorder.Domain.Tests/VRRecorder.Domain.Tests.csproj --no-restore
dotnet test tests/VRRecorder.Application.Tests/VRRecorder.Application.Tests.csproj --no-restore
dotnet test tests/VRRecorder.Compliance.Tests/VRRecorder.Compliance.Tests.csproj --no-restore
dotnet test tests/VRRecorder.Presentation.Tests/VRRecorder.Presentation.Tests.csproj --no-restore
dotnet test tests/VRRecorder.IntegrationTests/VRRecorder.IntegrationTests.csproj --no-restore
dotnet build src/VRRecorder.App/VRRecorder.App.csproj --no-restore
dotnet format VR-Recorder.sln --no-restore --verify-no-changes --verbosity minimal
make -C tests/VRRecorder.Native.Tests test
make -C tests/VRRecorder.Native.Tests coverage-gate
cmake -S . -B build/cmake-validation
cmake --build build/cmake-validation --parallel
ctest --test-dir build/cmake-validation --output-on-failure
```

- managed: 956件成功、失敗0、skip 0
  - Domain 90
  - Application 282
  - Compliance 186
  - Presentation 90
  - Integration 308
- WPF `win-x64` cross-build: warning 0、error 0
- native Make ABI／audio pipeline／availability-event／Spout capture worker／video CFR／encoding-worker contract: 成功
- native公開symbol allowlist: 17/17一致
- CMake 3.28.3 configure／全target build／CTest: 39/39成功（公開symbol 17/17とCMake build contractを含む）
- format/analyzer: 差分なし
- GCC標準gcov JSONを112 artifactから収集・mergeし、compiler生成`throw` edgeを除いたfirst-party nativeのline／source branch各90%を独立判定する`coverage-gate` target: 実測line 86.02%（2856/3320）／branch 71.58%（1667/2329）のため設計thresholdどおり非0終了

CMake／CTestは現在のnative graphに対して再実行済みです。Linux GCCでの成功証拠であり、Windows MSVC workflowはrepositoryにありますが、この報告ではevent-driven WASAPI sourceのMSVC compileまたはWindows実行成功を主張しません。

### 直前checkpointの結合テスト単独coverage

設計書18.5に従い、`VRRecorder.IntegrationTests`だけを実行してCoberturaを収集した直前checkpointの値です。runtime Mic／Mute、capture timeline／pump／normalizer／WASAPI factory／dual-input mix、Spout sender選択、native staging／Legal Bundle admission境界を含む858件の回帰は成功していますが、coverageは追加後に再収集していないため、次の表を現在値として扱いません。

| Assembly | Line | Branch |
|---|---:|---:|
| 全体 | 71.82% (10660/14842) | 56.94% (2131/3742) |
| Application | 71.45% | 53.14% |
| Compliance | 63.27% | 50.63% |
| DesignSystem | 40.00% | 12.50% |
| Domain | 73.22% | 60.73% |
| Infrastructure.Media | 82.89% | 70.18% |
| Infrastructure.Osc | 83.26% | 66.52% |
| Infrastructure.SteamVr | 79.61% | 70.00% |
| Infrastructure.Storage | 85.16% | 70.86% |
| Presentation.Wrist | 73.75% | 63.15% |

全体および各主要assemblyのline／branch 90%ゲートは未達です。native coverageは測定済みですがline／branchとも90%未達です。mutation score 75%、Windows UI Automation、hardware-in-the-loopは未測定です。

### このcheckpointで検証済みの主な境界

- 録画状態遷移、countdown／auto-stop、signal監視、容量低下停止、同一file確定
- 停止要求のないvideo CFR clock abortを障害として通知し、encoder sinkを確実にabortする資源解放境界
- 停止要求のないaudio mix source abortでcapture failureを確定し、sourceとencoder sinkの双方を確実にabortする資源解放境界
- CameraLease所有権、部分取得rollback、stale leaseのowned Streaming復旧
- OSCQuery target解決、UDP write確認、SteamVR Input Action ABI
- SingleFileFit contain計算とruntime layout更新、native最終statistics取得
- strict Legal catalog v3生成／認証読取、型付きlegal document、tamper／symlink／UTF-8 fail-closed
- production wall／monotonic clockとcancel可能なcountdown adapter
- session固有の寸法／FPS／packet数に基づくffprobe検証、native runtime encoder faultからpending保持／camera復元までの停止経路
- stale CameraLease／未確定録画の起動時回復、複数VRChat候補の厳密選択
- concrete desktop production composition、Release media入力gate、Ready後SteamVR input、録画を維持する4状態tray操作
- 保存pathとカメラ復元警告の分離通知、persisted録画／audio／出力設定、保存先Legal mirror
- 明示操作だけで生成し、未知field・media・認証情報・private値・symlinkを除外するatomic診断bundle
- app／OS build／GPU vendor／driver metadataを列挙値と数値versionへ制限し、未知fieldと自由文字列を除外する診断bundle sanitizer
- first packet確定後にapp version／OS build／process architecture／canonical PCI GPU model／driverをbest-effort構造化eventへ発行するproduction環境sourceとdesktop composition
- finalization／validation失敗後のquarantine reasonを録画結果へ影響しないbest-effort eventとして発行し、path／例外messageを除外するproduction診断経路
- OSCQuery capabilityと確認済みcamera writeの成功／失敗をbest-effort固定語eventとして発行し、service ID／endpoint／avatar値を除外するproduction診断経路
- capture timeline overrunとmixed-window underrunをinput role／正確な48 kHz frame付きで検出し、48-byte C ABI event、typed P/Invoke callback、bounded structured log、privacy-safe bundleへ伝播するaudio health経路
- Windows Core Audio MMDevice COMからactive render／capture endpointのfriendly nameとopaque IDを列挙し、非activeな保存済み選択を保持してlocalized／accessible selectorへ統合、明示変更だけを同時更新へ三者マージして次回native startへ渡すsettings経路
- `ole32.dll` COM解放call siteをWindowsSystem integrityとしてruntime-load manifestへ明示登録し、第三者配布物へ誤分類しないadmission
- System／English／Japaneseのoptional schema v1 localeを同時更新へ三者マージし、CLI override優先で起動時に、保存成功後は即時にWPF string／layout resourceへ適用するsettings経路
- VR hand／Wrist Dock・World PinとOSC auto discovery／loopback fallback host・portをlocalized／accessible settings controlへ投影し、overlay transformやportの同時更新を保持して個別保存する経路（OpenVR／fallback snapshot backendへのruntime適用は未実装）
- 設計上の12初回setup項目をversion付き固定順序として扱い、厳格JSONをatomic保存して完了済みprefixから再開し、破損／順序外完了／旧versionの完了誤認をfail-safeで拒否するApp構成済み経路（画面／実機probeは未実装）
- 初回setupの現在項目を12個の個別localized resource、1-origin step番号、全体件数、進捗率へ投影する英日／200% pseudo／high-contrast／accessible WPF画面。録画権利認可より先に未完了を表示し、閉じても録画を無効のまま保ち、画面自身には完了APIを持たせない（7・8番目のprobe接続は未実装）
- 現在の初回setup項目をexact probe routerへ渡し、成功時だけ順序付き進捗をatomic保存するverification経路。SteamVR App ID 250820のWindows Steam登録 `Installed=1`、または既存のloopback／identity／camera capability検証済みOSCQuery discoveryに1件以上のVRChat候補がある場合だけ先頭2項目を進める（Windows実行と残り10 probeは未検証／未実装）
- 単一の検証済みVRChat候補だけについて現在camera modeをOSCQuery snapshotから読み、同一modeのUDP writeを既存の200 ms×2回echoで確認して状態を変えずにcamera endpoint項目を進めるprobe。候補0／複数、mode不明、echo失敗はfail-safeで未完了を保つ
- Windows microphone consent registryが明示`Allow`で、保存済みopaque capture IDがactive microphone catalogにordinal完全一致する場合だけprivacy／device項目を進めるprobe。`default-capture`、inactive device、拒否／未知／読取失敗はfail-safeで未完了を保つ
- 保存済みencoder preferenceをnative合成frame probeへ渡し、実packet生成時だけself-test項目を進めるprobe。native DLLは検証時だけloadし、結果／失敗後に必ず解放する（AutoはGPU signal未確定のためMedia Foundation softwareを検証）
- packaged manifestを使うnative SteamVR runtimeで録画／microphone toggle actionの初回stateを各2秒以内に取得し、両方がactiveの場合だけbinding項目を進めるprobe。manifest存在だけ、inactive、timeout、backend unavailableは未完了を保つ
- 保存済み録画出力先へauthenticated Legal Bundleをversion付きでmirrorし、install root／staging／既存mirrorのhash、identity、unexpected payload、symlinkをfail-closed再検証できた場合だけ9番目を進めるprobe（固定順序のため7・8番目実装後に到達）
- authenticated catalogの全component／全typed legal documentをinstall rootからofflineで読み、component/reference identityが一致し全文が非空の場合だけ10番目を進めるprobe。license、notice、copyright、attribution、asset manifestを同じfail-closed readerで検証する
- authenticated asset manifestからMaterial Symbols schema 2／承認済みrelease status／component identityと、M3 report schema 2／evaluated／release eligible／coverage 100%／unclassified・deferred・unresolved各0を確認した場合だけ12番目を進めるprobe。design-time exampleは明示拒否する
- authenticated localization contractで英日、pseudo 200%、RTL、resource parity、drag代替、keyboard／controller／ray parityを確認し、release-eligible M3 reportにtooltip、accessible name coverage 100%、英日、pseudo、RTL、high contrastのrequired checkが揃う場合だけ11番目を進めるprobe
- 手首overlay配置について、保存済みmode／transformをruntimeへ適用したreadback、実visible、ユーザーの可読性／操作非干渉確認が全て揃い、vectorを含め設定と完全一致する場合だけ成功するApplication probe境界（production OpenVR verifier未実装のためApp routerは未接続でfail-safe未完了）
- 3秒試験録画について、要求3秒、実duration 3–4秒、MP4確定、validatorによるvideo／audio両stream、実playback開始の全証拠が揃う場合だけ成功するApplication probe境界（production recording／playback verifier未実装のためApp routerは未接続でfail-safe未完了）
- modalな初回setup画面から英日／pseudo／accessibleなSettingsを子dialogとして開き、mic device／encoder／OSC等を保存して同じsetup項目へ戻り再検証できる導線（進捗を直接完了する経路は持たない）
- desktop／microphoneのdevice loss／recoveryを48 kHz frame位置付きで伝える非terminal native ABI、型付きmanaged bridge、callback時刻を保つbounded診断queue、session-scoped desktop／tray fan-out
- first packet確定後のencoder／GPU vendor／geometry／FPSと、graceful stop後のdrop／duplicate／encode latency／A/V offsetをprivate identityなしで記録・再投影する経路
- 録画中Mic／Muteの復元可能なcontrol state、FIFO更新とstop barrier、17番目のnative routing export、desktop／wrist／SteamVR Micの共有command経路
- 可変packetを48 kHz絶対frameへ再構成し、gap／device lossを無音化し、正確なrecovery frameとclock epoch、Abort wakeを保つnative capture timeline
- PCM16／packed PCM24／PCM32／float、mono／stereo／speaker-mask付きmultichannel、sample rate差を48 kHz stereoへ正規化し、packet間のrational phaseとtimestamp-error／discontinuity epochを保つportable normalizer
- event callback、loopback／capture endpoint、device position／QPC、silent／discontinuity／timestamp-error flag、同一threadのGetBuffer／ReleaseBuffer、冪等Abortを扱うWindows WASAPI source境界
- clock付き48 kHz stereo packet、WASAPI silent相当の明示frame数、正確なdevice-loss frame、失敗したreplacement後の再試行、最大5秒のdefault endpoint再探索、待機中Abortを扱うcapture pump／runner
- WASAPI Start／Readを同一専用threadで実行し、初期結果を同期返却して失敗／Abort／destructorで必ずjoinするcapture worker
- desktop／microphone workerをrollback可能に開始し、timelineを同一48 kHz frame windowで読み、片側切断時だけを無音化し、skewを拒否してmixerへ固定sample数を渡すstereo capture session
- mixed PCMの開始frameと固定sample数をencoder Portへ渡し、buffering時の0 packetと実mux packet数を分離するaudio encoding pump
- dedicated threadでaudio encoding pumpを連続実行し、graceful stopだけをflushし、Abort／encoder failureではcaptureとencoderを中断するworker
- capture初期化後だけencodingを開始し、live routing、冪等stop、rollback、最終frame／packet統計を統合するaudio pipeline session
- 各CFR tickで最新source frameを採用し、中間frame dropと直前frame duplicateを個別集計するnative video scheduler
- scheduled frameをvideo encoder Portへ渡し、buffering、first mux packet、runtime failure、最新／最大latencyを集計するpump
- dedicated CFR clock threadでvideo pumpを実行し、first packet callback、graceful flush、Abort、runtime faultをMediaEventへ統合するworker
- selected Spout senderのframe metadataだけを検証してCFR schedulerへ渡し、timeout、sender loss、Abortを分離するcapture pump
- bounded poll timeoutでSpout pumpを専用thread実行し、timeout継続、sender loss、terminal failure、Abort/joinを管理するworker
- capture先行開始、encoder開始失敗時rollback、capture先行停止、capture／encoder相互runtime failure時のpeer Abort、sender loss MediaEvent fault、最終video統計を統合するpipeline session
- encoder開始rollbackでcaptureをAbort／Joinし、forced Abortでもcapture／encoding両workerをjoinしてcontrol call復帰時のthread quiescenceを保証するvideo pipeline lifetime境界
- Adapter LUID／実寸／pixel format／native handleを持つGPU surfaceの共有所有権をSpout captureからCFR outputへ伝播し、texture descriptorとframe metadataの不一致を拒否する境界
- shared GPU surfaceをbounded timeoutでAcquireし、encoder writeの成功／失敗後に必ずReleaseし、一時timeoutを次tickへ継続、同期failureをMediaEvent faultへ変換するencoding境界
- texture実寸の奇数辺を最大1px正規化し、cropなしのSingleFileFit配置、RGBA channel swap、NV12出力をD3D11 processor向け不変planへ変換するportable計算境界（GPU変換実体は未実装）
- processorへsource surface／処理planを渡し、出力Adapter LUID／寸法／NV12／native handleをfail-closed検証してencoderへ転送し、processing／encoding failureとAbort順序を分離するadapter（D3D11 processor実体は未実装）
- C ABI live video layoutを固定canvas／bounds／even寸法で検証してmutex保護し、次frameの明示destinationへ適用、source寸法不一致をprocessor前に拒否するlayout controller（GPU processor実体は未実装）
- 偶数NV12入力、HighからMainへのcapability降格、品質優先VBR、2秒GOP、`width*height*fps*0.14`の8–80 Mbps clampと1.5倍maxrateを整数安全に導出するH.264設定境界
- AAC-LC、48 kHz、stereo、192 kbpsと既存mixerのFloat32 interleaved source形式を明示し、backend固有sample変換をencoder adapterへ隔離する音声設定境界
- A/V packetのPTS／DTS／duration／keyframeを直列化し、stream別DTS単調性、1秒以降keyframe優先／2秒上限fragment、graceful fragment→trailer→file flush、Abort時trailer禁止を保証するfMP4 mux coordinator
- video encoderのbuffering／実packet batchを分離して共通fMP4 timelineへ投入し、mux failureをencoder failureと区別して未commit統計を除外し双方をAbortするsink adapter
- AAC encoderのbuffering／実packet batchを分離して同じfMP4 timelineへaudio packetを投入し、mux failureをaudio pump／workerまで独立伝播してcapture／encoder／muxerを停止するsink adapter
- video／audio flush packet投入後のstream完了をbarrier化し、両encoder成功後だけ最終fragment／trailer／file flushを一度実行し、重複完了／flush後packet／片側failureをfail-closed処理する共有mux finalization session
- mux成功後の最新video／audio PTS差を80 ms閾値で監視し、excursion単位の再arm可能なprivacy-safe eventと最新／最大drift／event数を記録するA/V sync monitor（診断observer結果は録画成否へ影響しない）
- ABI構造体サイズと17 exportsを変えずにA/V drift eventをnative callbackからtyped P/Invoke callback、best-effort media event sink、privacy-safe structured logへ伝播する経路
- encoder probeのDispose開始flagを呼出しthreadで同期確定し、in-flight native結果を`ObjectDisposedException`へ収束させてlibrary解放完了を`IAsyncDisposable`で待つlifetime境界
- A/V sync monitorの非負video／audio PTSとabsolute driftをprivacy-safe native MediaEventへ正確に転送し、C ABI drift event経路へ接続するadapter
- muxer、A/V sync monitor、MediaEvent adapter、共有dual-stream finalizationを安全なlifetime順序で所有し、成功packetのdrift観測と両encoder完了後のfinalizeを自動接続するnative composition boundary
- 映像停止→音声終了→両stream joinの順序を固定し、開始rollbackと片側failure時のpeer／共有mux Abort、両側成功時だけの最終packet統計通知を保証するmedia recording session境界
- 既存のvideo／stereo audio pipelineへpoll timeout、endpoint、session QPC、encoding frame windowを固定して渡し、終了理由を安定C ABI statusへ、packet統計を共通recording sessionへ変換するconfigured stream adapters
- configured video／audio adaptersとrecording coordinatorをlifetime-safeに所有し、Start／live routing／graceful Stop／video・audio統計を単一APIへ集約するmedia recording pipeline composition（backend実体は未実装）
- recording pipelineをC ABI MediaBackendへ変換し、video layout／audio routing、ABI統計、冪等な非同期stop workerとAbort／destructorでの確実なjoinを提供するadapter
- 非同期stop Joinとforced Abortのterminal状態をatomicに仲裁し、各stream join後もAbortを再検査して、Abort先行時にStopped／Saved相当eventを発行しないrecording session境界
- Start／RequestStop中のAbort先行を各blocking stream call後に再検査し、開始済みstreamをAbort／Joinして、未開始audio／残りのgraceful sequenceをskipするrecording session境界
- mux成功packetの最新A/V offsetを`audio PTS - video PTS`の符号付き値としてmonitorからmux／recording pipeline／C ABI最終統計まで伝播する経路（閾値event用absolute driftとは分離）
- device loss／recoveryの入力roleと正確な48 kHz frameをpumpからsession経由でproduction MediaEventへ変換するadapter
- 複数の安定Spout senderをpoll順で即決せず、VRChat service単位の前回選択を優先し、曖昧時だけaccessible desktop promptで選択・atomic保存する経路
- CMake link入力とNativeLibrary／LibraryImport call siteをfirst-party／Windows system／toolchain／third-party provenanceおよびintegrity policyへ照合するcandidate gate
- 最終stagingのPE内容、first-party allowlist、runtime scope、台帳schema／承認／version／source commit、binary／source hashを照合し、standalone native componentを全Legal Bundle indexへ反映するfail-closed gate

### 未完了の主要release gate

- 実Spout2／D3D11、承認済みencoder／muxer adapterを持つproduction media backend
- 実OpenVR overlay、Wrist renderer、haptics、move／pin操作
- 初回setup 7・8番目のproduction verifier adapterとWindows上の実装済み10項目の検証実行、VR配置／OSC設定のruntime反映、実アプリのend-to-end録画
- 承認済みMaterial Symbols asset、rights ledger、FFmpeg source offer、最終依存inventory
- Windows 10／11およびNVIDIA／AMD／Intel、HMD／controllerでの実機試験
- coverage／mutation／native coverage／accessibility／localizationの全release gate
- 独立法務review、署名、installer／最終payload再スキャン

## English

### Current verdict

Implementation is in progress and is not release-eligible. As of 2026-07-13, validation covers managed tests runnable on Linux/WSL2, a Windows x64 WPF cross-build, and the Linux native ABI. Running WPF on Windows and validating Spout2, D3D11, WASAPI, FFmpeg, OpenVR, real VRChat, Windows 10/11, GPUs, and HMDs remain outstanding.

The desktop production composition now wires the P/Invoke Spout source, encoder probe, native recording engine, OSC, storage, Legal mirror, runtime-fault stop path, and SteamVR input. Startup recovery for stale CameraLease/unfinalized recordings, exact multi-VRChat selection, service-scoped previous Spout-sender selection with a desktop ambiguity prompt, recording settings UI, the four-state tray, saved-path/camera-restore notifications, nonterminal audio-device loss/recovery notifications with bounded callback queues and session-scoped UI, and explicit privacy-safe diagnostic-bundle export including the media profile/final native statistics are also wired. Live Mic/Mute control now preserves reversible state from the native ABI through desktop, wrist, and SteamVR input. Native internals include PCM/float sample-rate and channel normalization to 48 kHz stereo, event-driven WASAPI loopback/microphone sources, capture timelines and recovery runners covering packet boundaries, gaps, device loss/recovery, and abort, joined workers that keep WASAPI Start/Read on dedicated same threads, and a rollback-safe stereo capture session that feeds aligned desktop/microphone frame windows into the mixer. The production native media backend that connects these boundaries to an encoder/muxer, and the SteamVR backend, still intentionally return `BACKEND_UNAVAILABLE`; approved Windows x64 native-DLL and ffprobe Release inputs have not been supplied. This is therefore a development checkpoint for design contracts and boundary implementations, not a recording-capable product.

The current third-party registry entries are candidates for test-only NuGet dependencies. Versions, NuGet content hashes, package-archive SHA-256 values, upstream commits, and full-license-text hashes are pinned, but every entry has `approval.status` set to `pending-independent-review`. Native link and runtime-load manifests reconcile current first-party, Windows-system, and toolchain call sites and reject unregistered additions at the candidate gate. The final-staging gate now checks PE content, ownership, runtime scope, registry schema/approval, component version/source commit, and binary/source-archive hashes before Legal Bundle generation, and includes standalone native components in text notices and SPDX. There are still zero approved third-party native components. This is not approval for signing, publication, or an end-user Legal Bundle.

### Automated validation

The following commands were run successfully on 2026-07-13:

```text
dotnet test tests/VRRecorder.Domain.Tests/VRRecorder.Domain.Tests.csproj --no-restore
dotnet test tests/VRRecorder.Application.Tests/VRRecorder.Application.Tests.csproj --no-restore
dotnet test tests/VRRecorder.Compliance.Tests/VRRecorder.Compliance.Tests.csproj --no-restore
dotnet test tests/VRRecorder.Presentation.Tests/VRRecorder.Presentation.Tests.csproj --no-restore
dotnet test tests/VRRecorder.IntegrationTests/VRRecorder.IntegrationTests.csproj --no-restore
dotnet build src/VRRecorder.App/VRRecorder.App.csproj --no-restore
dotnet format VR-Recorder.sln --no-restore --verify-no-changes --verbosity minimal
make -C tests/VRRecorder.Native.Tests test
make -C tests/VRRecorder.Native.Tests coverage-gate
cmake -S . -B build/cmake-validation
cmake --build build/cmake-validation --parallel
ctest --test-dir build/cmake-validation --output-on-failure
```

- managed: 956 passed, 0 failed, 0 skipped
  - Domain 90
  - Application 282
  - Compliance 186
  - Presentation 90
  - Integration 308
- WPF `win-x64` cross-build: 0 warnings, 0 errors
- native Make ABI/audio-pipeline/availability-event/Spout-capture-worker/video-CFR/encoding-worker contracts: passed
- native public-symbol allowlist: exact 17/17 match
- CMake 3.28.3 configure/full-target build/CTest: 39/39 passed, including the exact 17/17 public-symbol and CMake-build-contract checks
- format/analyzers: no changes required
- A connected `coverage-gate` target that collects and merges 112 standard GCC gcov JSON artifacts, excludes compiler-generated `throw` edges, and independently enforces 90% first-party native line/source-branch thresholds; current measurements are 86.02% lines (2856/3320) and 71.58% branches (1667/2329), so it exits nonzero as designed

CMake/CTest has now been rerun against the current native graph. This is Linux GCC evidence; a Windows MSVC workflow is present in the repository, but this report does not claim that the event-driven WASAPI source has compiled under MSVC or run on Windows.

### Integration-test-only coverage from the preceding checkpoint

Following design section 18.5, Cobertura coverage was collected by running only `VRRecorder.IntegrationTests` at the preceding checkpoint. The 858-test regression including runtime Mic/Mute, capture timeline/pump/normalizer/WASAPI factory/dual-input mix, Spout-sender selection, and native staging/Legal Bundle admission boundaries passes, but coverage has not been recollected after those additions; the following table is not a current measurement.

| Assembly | Line | Branch |
|---|---:|---:|
| Overall | 71.82% (10660/14842) | 56.94% (2131/3742) |
| Application | 71.45% | 53.14% |
| Compliance | 63.27% | 50.63% |
| DesignSystem | 40.00% | 12.50% |
| Domain | 73.22% | 60.73% |
| Infrastructure.Media | 82.89% | 70.18% |
| Infrastructure.Osc | 83.26% | 66.52% |
| Infrastructure.SteamVr | 79.61% | 70.00% |
| Infrastructure.Storage | 85.16% | 70.86% |
| Presentation.Wrist | 73.75% | 63.15% |

The 90% line and branch gates, both overall and per major assembly, are not met. Native coverage has been measured but remains below 90% for both lines and branches. The 75% mutation score, Windows UI Automation, and hardware-in-the-loop gates have not been measured.

### Main boundaries validated at this checkpoint

- Recording state transitions, countdown/auto-stop, signal supervision, low-space stop, and same-file finalization
- Resource release when the video CFR clock aborts without a stop request: report the fault and reliably abort the encoder sink
- Resource release when the audio mix source aborts without a stop request: resolve capture failure and reliably abort both the source and encoder sink
- CameraLease ownership, partial-acquisition rollback, and owned Streaming recovery from stale leases
- OSCQuery target resolution, confirmed UDP writes, and the SteamVR Input Action ABI
- SingleFileFit contain calculation, runtime layout updates, and final native statistics retrieval
- Strict Legal catalog v3 generation/authenticated reading, typed legal documents, and fail-closed tamper/symlink/UTF-8 handling
- Production wall/monotonic clocks and a cancellable countdown adapter
- Session-specific ffprobe validation from geometry/FPS/packet counts, plus the native runtime-encoder fault path through pending preservation and camera restoration
- Startup recovery for stale CameraLease/unfinalized recordings and exact selection among multiple VRChat candidates
- Concrete desktop production composition, Release media-input gates, Ready-gated SteamVR input, and four-state tray controls that preserve recording
- Separate saved-path/camera-restore notifications, persisted recording/audio/output settings, and output-folder Legal mirroring
- Atomic diagnostic bundles generated only by explicit action while excluding unknown fields, media, credentials, private values, and symlinks
- A diagnostic-bundle sanitizer that restricts app/OS-build/GPU-vendor/driver metadata to enumerated values and numeric versions and excludes unknown fields and free-form strings
- A production environment source and desktop composition that emit app version, OS build, process architecture, canonical PCI GPU model, and driver as a best-effort structured event after the first packet is committed
- A production diagnostics path that emits the post-failure finalization/validation quarantine reason without changing recording results and excludes paths and exception messages
- A production diagnostics path that emits OSCQuery capability and confirmed camera-write outcomes as best-effort fixed-term events while excluding service IDs, endpoints, and avatar values
- An audio-health path that detects capture-timeline overruns and mixed-window underruns with the input role and exact 48 kHz frame and propagates them through the 48-byte C ABI event, typed P/Invoke callback, bounded structured log, and privacy-safe bundle
- A settings path that enumerates active render/capture endpoint friendly names and opaque IDs from Windows Core Audio MMDevice COM, retains inactive persisted selections in localized accessible selectors, three-way merges explicit changes over concurrent updates, and supplies them to the next native start
- An admission for the `ole32.dll` COM cleanup call site as WindowsSystem integrity in the runtime-load manifest, without misclassifying it as a redistributed third-party component
- A settings path that three-way merges System/English/Japanese as an optional schema-v1 locale, applies it to WPF string/layout resources at startup with CLI-override precedence, and reapplies it immediately after a successful save
- A settings path that projects VR hand/Wrist Dock/World Pin and OSC auto-discovery/loopback fallback host/ports into localized accessible controls and persists explicit edits while retaining concurrent overlay-transform or port updates (runtime application to OpenVR and a fallback snapshot backend remains outstanding)
- An App-composed path that models the 12 specified first-run setup items as a versioned fixed order, atomically persists strict JSON, resumes from a completed prefix, and fails safe on corruption, out-of-order completion, or completion from an older version (hardware probes remain outstanding)
- An English/Japanese, deterministic 200%-pseudo, high-contrast, accessible WPF window that projects the current first-run item, one-based step number, total count, and overall progress; it appears before rights authorization, keeps recording disabled after close while incomplete, and exposes no self-approval API (probe wiring for items seven and eight remains outstanding)
- A verification path that routes only the current first-run item to its exact probe and atomically saves ordered progress only after success; the first two items advance only for SteamVR App ID 250820 `Installed=1`, or at least one VRChat candidate from the existing OSCQuery discovery after loopback, identity, and camera-capability validation (Windows execution and the remaining 10 probes are unverified/outstanding)
- A camera-endpoint probe that, only for one validated VRChat candidate, reads the current camera mode from the OSCQuery snapshot and writes the same mode through the existing two-attempt 200 ms UDP echo confirmation, avoiding a state change; zero/multiple candidates, unknown mode, or failed echo remain incomplete
- A microphone privacy/device probe that advances only when the Windows microphone consent registration explicitly says `Allow` and the saved opaque capture ID exactly matches an active microphone endpoint; `default-capture`, inactive devices, denied/unknown consent, and read failures remain incomplete
- An encoder self-test probe that passes the saved encoder preference to the native synthetic-frame probe and advances only after an actual packet is produced; the native DLL is loaded only for verification and always released afterward (Auto tests Media Foundation software because no GPU signal is established yet)
- A SteamVR binding probe that uses the packaged manifest through the native runtime and advances only when initial states for both recording and microphone toggle actions arrive within two seconds each and are active; manifest presence alone, inactive actions, timeout, or backend unavailability remain incomplete
- A ninth-item probe that mirrors the authenticated Legal Bundle to the saved recording output under a product-version directory and advances only after fail-closed hash, identity, unexpected-payload, and symlink checks of the install root, staging, and existing mirror (fixed ordering makes it reachable after items seven and eight)
- A tenth-item probe that reads every typed legal document for every authenticated catalog component offline from the install root and advances only when component/reference identity matches and full text is nonempty; licenses, notices, copyrights, attributions, and asset manifests use the same fail-closed reader
- A twelfth-item probe that reads authenticated asset manifests and requires Material Symbols schema 2/approved release status/component identity plus M3 report schema 2/evaluated/release-eligible/100% coverage/zero unclassified, deferred, and unresolved entries; design-time examples are explicitly rejected
- An eleventh-item probe that requires the authenticated localization contract to cover Japanese/English, 200% pseudo locales, RTL, resource parity, drag alternatives, and keyboard/controller/ray parity, plus a release-eligible M3 report containing tooltip, 100% accessible-name coverage, Japanese/English, pseudo, RTL, and high-contrast required checks
- An Application probe boundary for wrist-overlay placement that requires runtime-applied readback of the saved mode/transform, actual visibility, user confirmation of readability and unobstructed interaction, and exact vector equality; because the production OpenVR verifier is absent, it is intentionally not routed and remains fail-safe incomplete
- An Application probe boundary for the three-second test recording that requires an exact three-second request, an observed 3–4 second duration, finalized MP4, validator-confirmed video and audio streams, and actual playback start; because the production recording/playback verifier is absent, it is intentionally not routed and remains fail-safe incomplete
- An English/Japanese/pseudo-localized accessible path from the modal first-run window to a child Settings dialog, allowing microphone, encoder, and OSC changes to be saved before returning to verify the same step, without any direct progress-completion path
- Nonterminal native ABI events, typed managed bridging, callback-time-preserving bounded diagnostics, and session-scoped desktop/tray fan-out for desktop-audio and microphone loss/recovery with 48 kHz frame positions
- Privacy-safe logging and reprojection of the committed encoder/GPU-vendor/geometry/FPS profile and final drop/duplicate/encode-latency/A/V-offset statistics
- Reversible live Mic/Mute control state, FIFO updates with a stop barrier, the seventeenth native routing export, and shared desktop/wrist/SteamVR microphone command paths
- A native capture timeline that reconstructs variable packets on absolute 48 kHz frames, zero-fills gaps/device loss, preserves exact recovery frames and clock epochs, and wakes safely on abort
- A portable normalizer for PCM16, packed PCM24, PCM32, float, mono, stereo, speaker-masked multichannel, and sample-rate differences that retains rational phase across packets and timestamp-error/discontinuity epochs
- A Windows WASAPI source boundary covering event callbacks, loopback/capture endpoints, device position/QPC, silent/discontinuity/timestamp-error flags, same-thread GetBuffer/ReleaseBuffer, and idempotent abort
- Capture pumps/runners covering clocked 48 kHz packets, explicit WASAPI-style silent-frame counts, exact device-loss frames, recovery after a failed replacement, bounded five-second default-endpoint rediscovery, and abortable waits
- Joined capture workers that run WASAPI Start/Read on one dedicated thread, synchronously report initialization, and join on failure, abort, or destruction
- A rollback-safe stereo capture session that starts desktop/microphone workers, reads their timelines over the same 48 kHz frame window, silences only a disconnected side, rejects skew, and passes a fixed sample count into the mixer
- An audio encoding pump that submits positioned mixed PCM windows through an encoder port and distinguishes buffered zero-packet writes from actual muxed-packet counts
- A dedicated encoding worker that flushes only on graceful stop and aborts both capture and encoding on forced abort or encoder failure
- An audio pipeline session that starts encoding only after capture initialization and integrates live routing, idempotent stop, rollback, and final frame/packet statistics
- A native video scheduler that selects the latest source frame at each CFR tick and separately counts discarded intermediate frames and duplicated previous outputs
- A video encoding pump that submits scheduled frames through an encoder port and tracks buffering, first muxed packet, runtime failure, and latest/maximum latency
- A dedicated CFR-clock worker that integrates video pumping, first-packet callbacks, graceful flush, abort, and runtime faults into media events
- A capture pump that validates metadata from only the selected Spout sender, feeds the CFR scheduler, and distinguishes timeout, sender loss, and abort
- A joined Spout capture worker that polls with a bounded timeout and manages timeout continuation, sender loss, terminal failure, and abort
- A video pipeline session that integrates capture-first startup, rollback on encoder startup failure, capture-first shutdown, peer abort for capture/encoder runtime failures, sender-loss media faults, and final video statistics
- A video-pipeline lifetime boundary that aborts and joins capture during encoder-start rollback and joins both capture/encoding workers on forced abort, guaranteeing thread quiescence before the control call returns
- A GPU-surface boundary that carries shared ownership, adapter LUID, actual dimensions, pixel format, and a native handle from Spout capture through CFR output while rejecting texture/frame metadata mismatches
- An encoding boundary that acquires shared GPU surfaces with a bounded timeout, releases them after both successful and failed writes, continues after transient timeouts, and maps synchronization failures to media faults
- A portable planning boundary that normalizes odd texture edges by at most one pixel and produces immutable no-crop SingleFileFit placement, RGBA channel-swap, and NV12-output instructions for a D3D11 processor (the GPU transformation implementation remains outstanding)
- An adapter that passes source surfaces/plans to a processor, fail-closed validates output adapter LUID, dimensions, NV12 format, and native handle before encoding, and separates processing/encoding failures and abort order (the D3D11 processor implementation remains outstanding)
- A layout controller that validates C ABI live-video layouts against fixed canvas/bounds/even dimensions, stores them under a mutex, applies explicit destinations from the next frame, and rejects source-dimension mismatch before processing (the GPU processor implementation remains outstanding)
- An H.264 configuration boundary that safely derives even NV12 input, High-to-Main capability fallback, quality VBR, a two-second GOP, the clamped 8–80 Mbps `width*height*fps*0.14` target, and a 1.5x maximum rate
- An audio configuration boundary that fixes AAC-LC, 48 kHz, stereo, 192 kbps, and the mixer's interleaved Float32 source format while isolating backend-specific sample conversion in encoder adapters
- An fMP4 mux coordinator that serializes A/V packet PTS/DTS/duration/keyframes, enforces per-stream DTS monotonicity, prefers keyframe cuts after one second with a hard two-second fragment limit, orders graceful fragment/trailer/file flush, and forbids trailers after abort
- A video sink adapter that separates encoder buffering from real packet batches, submits those batches to the shared fMP4 timeline, distinguishes mux from encoder failures, excludes uncommitted statistics, and aborts both sides on failure
- An AAC sink adapter that separates encoder buffering from real packet batches, submits audio packets to the same fMP4 timeline, propagates mux failures distinctly through the audio pump/worker, and stops capture, encoder, and muxer
- A shared mux finalization session that barriers stream completion after video/audio flush packets, runs final fragment/trailer/file flush once only after both encoders succeed, and fail-closed rejects duplicate completion, post-flush packets, or either-side failure
- An A/V sync monitor that observes successfully muxed video/audio PTS at an 80 ms threshold, emits rearmable privacy-safe events per excursion, records latest/maximum drift and event count, and never lets diagnostic observer outcomes alter recording success
- An ABI-size- and 17-export-preserving path that propagates A/V drift events from native callbacks through typed P/Invoke callbacks and a best-effort media-event sink into privacy-safe structured logs
- An encoder-probe lifetime boundary that synchronously marks disposal on the caller thread, converges in-flight native results to `ObjectDisposedException`, and exposes library-release completion through `IAsyncDisposable`
- An adapter that forwards nonnegative A/V-monitor video/audio PTS and absolute drift exactly into privacy-safe native media events, connecting the monitor to the C ABI drift-event path
- A native composition boundary that owns the muxer-facing coordinator, A/V sync monitor, MediaEvent adapter, and shared dual-stream finalization in safe lifetime order, automatically connecting successful packets to drift observation and both encoder completions to finalization
- A media recording-session boundary that fixes video-stop, audio-end, and dual-stream-join ordering; rolls back partial starts; aborts the peer and shared mux on either-side failure; and publishes final packet counts only after both streams succeed
- Configured stream adapters that pass poll timeout, endpoints, session QPC, and encoding frame windows into the existing video/stereo-audio pipelines, mapping their completion reasons to stable C ABI statuses and packet statistics to the common recording session
- A lifetime-safe media recording-pipeline composition that owns the configured video/audio adapters and recording coordinator and exposes start, live routing, graceful stop, and video/audio statistics through one API (backend implementations remain outstanding)
- An adapter from the recording pipeline to the C ABI MediaBackend that supplies video layout/audio routing, ABI statistics, an idempotent asynchronous stop worker, and guaranteed joining on abort or destruction
- A recording-session boundary that atomically arbitrates terminal state between asynchronous stop/join and forced abort, rechecks abort after each stream join, and never emits Stopped/Saved-equivalent success when abort wins
- A recording-session boundary that rechecks an abort winner after every blocking stream call in Start/RequestStop, aborts and joins already-started streams, and skips unstarted audio or remaining graceful-stop work
- A path that carries the latest signed A/V offset from successfully muxed packets as `audio PTS - video PTS` through the monitor, mux and recording pipelines into final C ABI statistics, separately from absolute drift used for threshold events
- An adapter that propagates the input role and exact 48 kHz frame of device loss/recovery from capture pumps through the session into production media events
- Deterministic multi-sender Spout selection that prefers the previous VRChat-service-scoped sender and otherwise uses an accessible desktop prompt with atomic persistence
- Candidate gates that reconcile CMake link inputs and NativeLibrary/LibraryImport call sites with first-party, Windows-system, toolchain, or third-party provenance and integrity policies
- A fail-closed final-staging gate that reconciles PE content, the first-party allowlist, runtime scope, registry schema/approval/version/source commit, and binary/source hashes, then includes standalone native components in every Legal Bundle index

### Major outstanding release gates

- Real Spout2/D3D11 and a production media backend with approved encoder/muxer adapters
- Real OpenVR overlay, wrist renderer, haptics, and move/pin controls
- Production verifier adapters for first-run items seven/eight, Windows execution of the 10 implemented checks, runtime VR-placement/OSC settings, and end-to-end recording in the real application
- Approved Material Symbols assets, rights ledger, FFmpeg source offer, and final dependency inventory
- Hardware testing on Windows 10/11, NVIDIA/AMD/Intel, HMDs, and controllers
- All coverage, mutation, native-coverage, accessibility, and localization release gates
- Independent legal review, signing, and installer/final-payload rescanning
