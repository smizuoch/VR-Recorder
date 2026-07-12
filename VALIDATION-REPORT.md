# Validation report / 検証報告

## 日本語

### 現在の判定

実装進行中であり、release適格ではありません。2026-07-12現在、Linux／WSL2で実行できるmanagedテスト、Windows x64向けWPF cross-build、Linux native ABIを検証しています。Windows上でのWPF実行、Spout2、D3D11、WASAPI、FFmpeg、OpenVR、実VRChat、Windows 10／11、GPU／HMDの検証は未実施です。

desktop production compositionはP/Invoke Spout source、encoder probe、native recording engine、OSC、storage、Legal mirror、runtime fault stop、SteamVR inputまで配線済みです。stale CameraLease／未確定録画の起動時回復、複数VRChatの厳密選択、VRChat service単位のSpout sender前回選択と曖昧時のdesktop prompt、録画設定UI、4状態tray、保存path／カメラ復元警告、bounded callback queueとsession-scoped UIを備えた音声device喪失／復旧の非terminal通知、media profile／最終native統計を含むprivacy-safe診断bundleの明示exportも配線済みです。録画中のMic／Muteはnative ABIからdesktop／wrist／SteamVR入力まで復元可能な状態を保って配線済みです。native内部にはPCM／floatとsample rate／channel差を48 kHz stereoへ正規化する処理、event-driven WASAPI loopback／microphone source、packet境界／gap／device loss／recovery／Abortを扱うcapture timelineと再探索runner、WASAPIのStart／Readを同一専用threadへ固定して同期初期化とRAII joinを行うworker、desktop／microphoneの開始rollbackと同一frame window mixを所有するcapture sessionがあります。ただしこれらをencoder／muxerへ接続するproduction native media backendとSteamVR backendは意図的に`BACKEND_UNAVAILABLE`を返します。承認済みWindows x64 native DLLとffprobeもRelease入力として未提供です。したがって、現状は設計契約と境界実装を検証する開発checkpointであり、録画可能な製品ではありません。

第三者台帳の現在のentryはtest-only NuGet依存のcandidateです。version、NuGet content hash、package archive SHA-256、上流commit、license全文hashを固定していますが、全entryの`approval.status`は`pending-independent-review`です。native link／runtime-load manifestは現行のfirst-party、Windows system、toolchain call siteを照合し、未登録追加をcandidate gateで拒否します。最終staging gateはPE内容、所有者、runtime scope、台帳schema／承認、component version／source commit、binary／source archive hashをLegal Bundle生成前に照合し、standalone native componentをTXT noticeとSPDXへ含めます。ただし承認済み第三者native componentはまだ0件です。署名・公開・end-user Legal Bundleの承認を意味しません。

### 自動検証

2026-07-12に次を実行し、成功を確認しました。

```text
dotnet test tests/VRRecorder.Domain.Tests/VRRecorder.Domain.Tests.csproj --no-restore
dotnet test tests/VRRecorder.Application.Tests/VRRecorder.Application.Tests.csproj --no-restore
dotnet test tests/VRRecorder.Compliance.Tests/VRRecorder.Compliance.Tests.csproj --no-restore
dotnet test tests/VRRecorder.Presentation.Tests/VRRecorder.Presentation.Tests.csproj --no-restore
dotnet test tests/VRRecorder.IntegrationTests/VRRecorder.IntegrationTests.csproj --no-restore
dotnet build src/VRRecorder.App/VRRecorder.App.csproj --no-restore
dotnet format VR-Recorder.sln --no-restore --verify-no-changes --verbosity minimal
make -C tests/VRRecorder.Native.Tests test
```

- managed: 857件成功、失敗0、skip 0
  - Domain 90
  - Application 226
  - Compliance 181
  - Presentation 85
  - Integration 275
- WPF `win-x64` cross-build: warning 0、error 0
- native Make ABI／mixer／timeline／capture-pump／WASAPI factory／worker／stereo session contract: 成功
- native公開symbol allowlist: 17/17一致
- format/analyzer: 差分なし

現在の環境には`cmake`がないため、2026-07-12の変更に対してCMake/CTestは再実行していません。2026-07-10のCMake/CTest 4/4は新しいaudio timeline／capture／mix target追加前の結果であり、現在のCMake構成の成功証拠としては扱いません。Windows MSVC workflowはrepositoryにありますが、この報告ではevent-driven WASAPI sourceのMSVC compileまたはWindows実行成功を主張しません。

### 直前checkpointの結合テスト単独coverage

設計書18.5に従い、`VRRecorder.IntegrationTests`だけを実行してCoberturaを収集した直前checkpointの値です。runtime Mic／Mute、capture timeline／pump／normalizer／WASAPI factory／dual-input mix、Spout sender選択、native staging／Legal Bundle admission境界を含む857件の回帰は成功していますが、coverageは追加後に再収集していないため、次の表を現在値として扱いません。

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

全体および各主要assemblyのline／branch 90%ゲートは未達です。mutation score 75%、native line／branch coverage 90%、Windows UI Automation、hardware-in-the-loopも未測定です。

### このcheckpointで検証済みの主な境界

- 録画状態遷移、countdown／auto-stop、signal監視、容量低下停止、同一file確定
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
- desktop／microphoneのdevice loss／recoveryを48 kHz frame位置付きで伝える非terminal native ABI、型付きmanaged bridge、callback時刻を保つbounded診断queue、session-scoped desktop／tray fan-out
- first packet確定後のencoder／GPU vendor／geometry／FPSと、graceful stop後のdrop／duplicate／encode latency／A/V offsetをprivate identityなしで記録・再投影する経路
- 録画中Mic／Muteの復元可能なcontrol state、FIFO更新とstop barrier、17番目のnative routing export、desktop／wrist／SteamVR Micの共有command経路
- 可変packetを48 kHz絶対frameへ再構成し、gap／device lossを無音化し、正確なrecovery frameとclock epoch、Abort wakeを保つnative capture timeline
- PCM16／packed PCM24／PCM32／float、mono／stereo／speaker-mask付きmultichannel、sample rate差を48 kHz stereoへ正規化し、packet間のrational phaseとtimestamp-error／discontinuity epochを保つportable normalizer
- event callback、loopback／capture endpoint、device position／QPC、silent／discontinuity／timestamp-error flag、同一threadのGetBuffer／ReleaseBuffer、冪等Abortを扱うWindows WASAPI source境界
- clock付き48 kHz stereo packet、WASAPI silent相当の明示frame数、正確なdevice-loss frame、失敗したreplacement後の再試行、最大5秒のdefault endpoint再探索、待機中Abortを扱うcapture pump／runner
- WASAPI Start／Readを同一専用threadで実行し、初期結果を同期返却して失敗／Abort／destructorで必ずjoinするcapture worker
- desktop／microphone workerをrollback可能に開始し、timelineを同一48 kHz frame windowで読み、片側切断時だけを無音化し、skewを拒否してmixerへ固定sample数を渡すstereo capture session
- 複数の安定Spout senderをpoll順で即決せず、VRChat service単位の前回選択を優先し、曖昧時だけaccessible desktop promptで選択・atomic保存する経路
- CMake link入力とNativeLibrary／LibraryImport call siteをfirst-party／Windows system／toolchain／third-party provenanceおよびintegrity policyへ照合するcandidate gate
- 最終stagingのPE内容、first-party allowlist、runtime scope、台帳schema／承認／version／source commit、binary／source hashを照合し、standalone native componentを全Legal Bundle indexへ反映するfail-closed gate

### 未完了の主要release gate

- 実Spout2／D3D11、WASAPI capture sessionからencoderへの接続、encoder／muxer backend
- 実OpenVR overlay、Wrist renderer、haptics、move／pin操作
- 初回setup、audio device選択、設定からの言語切替、VR配置／OSC設定のruntime反映、実アプリのend-to-end録画
- app／OS／GPU model／driver、継続A/V閾値、audio underrun／overrun、OSC、finalization失敗を網羅する構造化診断event
- 承認済みMaterial Symbols asset、rights ledger、FFmpeg source offer、最終依存inventory
- Windows 10／11およびNVIDIA／AMD／Intel、HMD／controllerでの実機試験
- coverage／mutation／native coverage／accessibility／localizationの全release gate
- 独立法務review、署名、installer／最終payload再スキャン

## English

### Current verdict

Implementation is in progress and is not release-eligible. As of 2026-07-12, validation covers managed tests runnable on Linux/WSL2, a Windows x64 WPF cross-build, and the Linux native ABI. Running WPF on Windows and validating Spout2, D3D11, WASAPI, FFmpeg, OpenVR, real VRChat, Windows 10/11, GPUs, and HMDs remain outstanding.

The desktop production composition now wires the P/Invoke Spout source, encoder probe, native recording engine, OSC, storage, Legal mirror, runtime-fault stop path, and SteamVR input. Startup recovery for stale CameraLease/unfinalized recordings, exact multi-VRChat selection, service-scoped previous Spout-sender selection with a desktop ambiguity prompt, recording settings UI, the four-state tray, saved-path/camera-restore notifications, nonterminal audio-device loss/recovery notifications with bounded callback queues and session-scoped UI, and explicit privacy-safe diagnostic-bundle export including the media profile/final native statistics are also wired. Live Mic/Mute control now preserves reversible state from the native ABI through desktop, wrist, and SteamVR input. Native internals include PCM/float sample-rate and channel normalization to 48 kHz stereo, event-driven WASAPI loopback/microphone sources, capture timelines and recovery runners covering packet boundaries, gaps, device loss/recovery, and abort, joined workers that keep WASAPI Start/Read on dedicated same threads, and a rollback-safe stereo capture session that feeds aligned desktop/microphone frame windows into the mixer. The production native media backend that connects these boundaries to an encoder/muxer, and the SteamVR backend, still intentionally return `BACKEND_UNAVAILABLE`; approved Windows x64 native-DLL and ffprobe Release inputs have not been supplied. This is therefore a development checkpoint for design contracts and boundary implementations, not a recording-capable product.

The current third-party registry entries are candidates for test-only NuGet dependencies. Versions, NuGet content hashes, package-archive SHA-256 values, upstream commits, and full-license-text hashes are pinned, but every entry has `approval.status` set to `pending-independent-review`. Native link and runtime-load manifests reconcile current first-party, Windows-system, and toolchain call sites and reject unregistered additions at the candidate gate. The final-staging gate now checks PE content, ownership, runtime scope, registry schema/approval, component version/source commit, and binary/source-archive hashes before Legal Bundle generation, and includes standalone native components in text notices and SPDX. There are still zero approved third-party native components. This is not approval for signing, publication, or an end-user Legal Bundle.

### Automated validation

The following commands were run successfully on 2026-07-12:

```text
dotnet test tests/VRRecorder.Domain.Tests/VRRecorder.Domain.Tests.csproj --no-restore
dotnet test tests/VRRecorder.Application.Tests/VRRecorder.Application.Tests.csproj --no-restore
dotnet test tests/VRRecorder.Compliance.Tests/VRRecorder.Compliance.Tests.csproj --no-restore
dotnet test tests/VRRecorder.Presentation.Tests/VRRecorder.Presentation.Tests.csproj --no-restore
dotnet test tests/VRRecorder.IntegrationTests/VRRecorder.IntegrationTests.csproj --no-restore
dotnet build src/VRRecorder.App/VRRecorder.App.csproj --no-restore
dotnet format VR-Recorder.sln --no-restore --verify-no-changes --verbosity minimal
make -C tests/VRRecorder.Native.Tests test
```

- managed: 857 passed, 0 failed, 0 skipped
  - Domain 90
  - Application 226
  - Compliance 181
  - Presentation 85
  - Integration 275
- WPF `win-x64` cross-build: 0 warnings, 0 errors
- native Make ABI/mixer/timeline/capture-pump/WASAPI-factory/worker/stereo-session contracts: passed
- native public-symbol allowlist: exact 17/17 match
- format/analyzers: no changes required

`cmake` is unavailable in the current environment, so CMake/CTest was not rerun for the 2026-07-12 changes. The 2026-07-10 CMake/CTest 4/4 result predates the new audio timeline/capture/mix targets and is not treated as evidence for the current CMake graph. A Windows MSVC workflow is present in the repository, but this report does not claim that the event-driven WASAPI source has compiled under MSVC or run on Windows.

### Integration-test-only coverage from the preceding checkpoint

Following design section 18.5, Cobertura coverage was collected by running only `VRRecorder.IntegrationTests` at the preceding checkpoint. The 857-test regression including runtime Mic/Mute, capture timeline/pump/normalizer/WASAPI factory/dual-input mix, Spout-sender selection, and native staging/Legal Bundle admission boundaries passes, but coverage has not been recollected after those additions; the following table is not a current measurement.

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

The 90% line and branch gates, both overall and per major assembly, are not met. The 75% mutation score, 90% native line and branch coverage, Windows UI Automation, and hardware-in-the-loop gates have not been measured.

### Main boundaries validated at this checkpoint

- Recording state transitions, countdown/auto-stop, signal supervision, low-space stop, and same-file finalization
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
- Nonterminal native ABI events, typed managed bridging, callback-time-preserving bounded diagnostics, and session-scoped desktop/tray fan-out for desktop-audio and microphone loss/recovery with 48 kHz frame positions
- Privacy-safe logging and reprojection of the committed encoder/GPU-vendor/geometry/FPS profile and final drop/duplicate/encode-latency/A/V-offset statistics
- Reversible live Mic/Mute control state, FIFO updates with a stop barrier, the seventeenth native routing export, and shared desktop/wrist/SteamVR microphone command paths
- A native capture timeline that reconstructs variable packets on absolute 48 kHz frames, zero-fills gaps/device loss, preserves exact recovery frames and clock epochs, and wakes safely on abort
- A portable normalizer for PCM16, packed PCM24, PCM32, float, mono, stereo, speaker-masked multichannel, and sample-rate differences that retains rational phase across packets and timestamp-error/discontinuity epochs
- A Windows WASAPI source boundary covering event callbacks, loopback/capture endpoints, device position/QPC, silent/discontinuity/timestamp-error flags, same-thread GetBuffer/ReleaseBuffer, and idempotent abort
- Capture pumps/runners covering clocked 48 kHz packets, explicit WASAPI-style silent-frame counts, exact device-loss frames, recovery after a failed replacement, bounded five-second default-endpoint rediscovery, and abortable waits
- Joined capture workers that run WASAPI Start/Read on one dedicated thread, synchronously report initialization, and join on failure, abort, or destruction
- A rollback-safe stereo capture session that starts desktop/microphone workers, reads their timelines over the same 48 kHz frame window, silences only a disconnected side, rejects skew, and passes a fixed sample count into the mixer
- Deterministic multi-sender Spout selection that prefers the previous VRChat-service-scoped sender and otherwise uses an accessible desktop prompt with atomic persistence
- Candidate gates that reconcile CMake link inputs and NativeLibrary/LibraryImport call sites with first-party, Windows-system, toolchain, or third-party provenance and integrity policies
- A fail-closed final-staging gate that reconciles PE content, the first-party allowlist, runtime scope, registry schema/approval/version/source commit, and binary/source hashes, then includes standalone native components in every Legal Bundle index

### Major outstanding release gates

- Real Spout2/D3D11, connection of the WASAPI capture session to encoding, and encoder/muxer backends
- Real OpenVR overlay, wrist renderer, haptics, and move/pin controls
- First-run setup, audio-device selection, settings-driven language switching, runtime VR-placement/OSC settings, and end-to-end recording in the real application
- Complete structured diagnostics for app/OS/GPU model/driver, continuous A/V thresholds, audio underruns/overruns, OSC, and finalization failures
- Approved Material Symbols assets, rights ledger, FFmpeg source offer, and final dependency inventory
- Hardware testing on Windows 10/11, NVIDIA/AMD/Intel, HMDs, and controllers
- All coverage, mutation, native-coverage, accessibility, and localization release gates
- Independent legal review, signing, and installer/final-payload rescanning
