# Validation report / 検証報告

## 日本語

### 現在の判定

実装進行中であり、release適格ではありません。2026-07-11現在、Linux／WSL2で実行できるmanagedテスト、Windows x64向けWPF cross-build、Linux native ABIを検証しています。Windows上でのWPF実行、Spout2、D3D11、WASAPI、FFmpeg、OpenVR、実VRChat、Windows 10／11、GPU／HMDの検証は未実施です。

desktop production compositionはP/Invoke Spout source、encoder probe、native recording engine、OSC、storage、Legal mirror、runtime fault stop、SteamVR inputまで配線済みです。stale CameraLease／未確定録画の起動時回復、複数VRChatの厳密選択、録画設定UI、4状態tray、保存path／カメラ復元警告、音声device喪失／復旧の非terminal通知、privacy-safe診断bundleの明示exportも配線済みです。ただしproduction native media／SteamVR backend自体は意図的に`BACKEND_UNAVAILABLE`を返します。承認済みWindows x64 native DLLとffprobeもRelease入力として未提供です。したがって、現状は設計契約と境界実装を検証する開発checkpointであり、録画可能な製品ではありません。

第三者台帳の現在のentryはtest-only NuGet依存のcandidateです。version、NuGet content hash、package archive SHA-256、上流commit、license全文hashを固定していますが、全entryの`approval.status`は`pending-independent-review`です。署名・公開・end-user Legal Bundleの承認を意味しません。

### 自動検証

2026-07-11に次を実行し、成功を確認しました。

```text
dotnet restore VR-Recorder.sln --locked-mode
dotnet test VR-Recorder.sln --configuration Release --nologo
dotnet build src/VRRecorder.App/VRRecorder.App.csproj --configuration Debug --runtime win-x64 --no-restore
dotnet format VR-Recorder.sln --verify-no-changes --no-restore
make -C tests/VRRecorder.Native.Tests test
```

- managed: 783件成功、失敗0、skip 0
  - Domain 90
  - Application 195
  - Compliance 170
  - Presentation 78
  - Integration 250
- WPF `win-x64` cross-build: warning 0、error 0
- native Make ABI contract: 成功
- native公開symbol allowlist: 16/16一致
- format/analyzer: 差分なし

現在の環境には`cmake`がないため、2026-07-11の変更に対してCMake/CTestは再実行していません。CMake/CTest 4/4は2026-07-10の直前checkpointで成功しており、Windows MSVC workflowはrepositoryにありますが、この報告ではremote実行成功を主張しません。

### 直前checkpointの結合テスト単独coverage

設計書18.5に従い、`VRRecorder.IntegrationTests`だけを実行してCoberturaを収集した直前checkpointの値です。今回追加した音声device通知を含む783件の回帰は成功していますが、coverageは追加後に再収集していないため、次の表を現在値として扱いません。

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
- desktop／microphoneのdevice loss／recoveryを48 kHz frame位置付きで伝える非terminal native ABI、型付きmanaged bridge、診断／desktop／tray fan-out

### 未完了の主要release gate

- 実Spout2／D3D11／WASAPI／encoder／muxer backend
- 実OpenVR overlay、Wrist renderer、haptics、move／pin操作
- 初回setup、audio device選択、設定からの言語切替、VR配置／OSC設定のruntime反映、実アプリのend-to-end録画
- app／OS／GPU／encoder、frame統計、A/V sync、audio underrun／overrun、OSC、finalization失敗を網羅する構造化診断event
- 承認済みMaterial Symbols asset、rights ledger、FFmpeg source offer、最終依存inventory
- Windows 10／11およびNVIDIA／AMD／Intel、HMD／controllerでの実機試験
- coverage／mutation／native coverage／accessibility／localizationの全release gate
- 独立法務review、署名、installer／最終payload再スキャン

## English

### Current verdict

Implementation is in progress and is not release-eligible. As of 2026-07-11, validation covers managed tests runnable on Linux/WSL2, a Windows x64 WPF cross-build, and the Linux native ABI. Running WPF on Windows and validating Spout2, D3D11, WASAPI, FFmpeg, OpenVR, real VRChat, Windows 10/11, GPUs, and HMDs remain outstanding.

The desktop production composition now wires the P/Invoke Spout source, encoder probe, native recording engine, OSC, storage, Legal mirror, runtime-fault stop path, and SteamVR input. Startup recovery for stale CameraLease/unfinalized recordings, exact multi-VRChat selection, recording settings UI, the four-state tray, saved-path/camera-restore notifications, nonterminal audio-device loss/recovery notifications, and explicit privacy-safe diagnostic-bundle export are also wired. The production native media and SteamVR backends themselves still intentionally return `BACKEND_UNAVAILABLE`, and approved Windows x64 native-DLL and ffprobe Release inputs have not been supplied. This is therefore a development checkpoint for design contracts and boundary implementations, not a recording-capable product.

The current third-party registry entries are candidates for test-only NuGet dependencies. Versions, NuGet content hashes, package-archive SHA-256 values, upstream commits, and full-license-text hashes are pinned, but every entry has `approval.status` set to `pending-independent-review`. This is not approval for signing, publication, or an end-user Legal Bundle.

### Automated validation

The following commands were run successfully on 2026-07-11:

```text
dotnet restore VR-Recorder.sln --locked-mode
dotnet test VR-Recorder.sln --configuration Release --nologo
dotnet build src/VRRecorder.App/VRRecorder.App.csproj --configuration Debug --runtime win-x64 --no-restore
dotnet format VR-Recorder.sln --verify-no-changes --no-restore
make -C tests/VRRecorder.Native.Tests test
```

- managed: 783 passed, 0 failed, 0 skipped
  - Domain 90
  - Application 195
  - Compliance 170
  - Presentation 78
  - Integration 250
- WPF `win-x64` cross-build: 0 warnings, 0 errors
- native Make ABI contract: passed
- native public-symbol allowlist: exact 16/16 match
- format/analyzers: no changes required

`cmake` is unavailable in the current environment, so CMake/CTest was not rerun for the 2026-07-11 changes. CMake/CTest 4/4 passed at the immediately preceding 2026-07-10 checkpoint. A Windows MSVC workflow is present in the repository, but this report does not claim a successful remote run.

### Integration-test-only coverage from the preceding checkpoint

Following design section 18.5, Cobertura coverage was collected by running only `VRRecorder.IntegrationTests` at the preceding checkpoint. The 783-test regression including the new audio-device notifications passes, but coverage has not been recollected after those additions; the following table is not a current measurement.

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
- Nonterminal native ABI events, typed managed bridging, and diagnostics/desktop/tray fan-out for desktop-audio and microphone loss/recovery with 48 kHz frame positions

### Major outstanding release gates

- Real Spout2/D3D11/WASAPI/encoder/muxer backends
- Real OpenVR overlay, wrist renderer, haptics, and move/pin controls
- First-run setup, audio-device selection, settings-driven language switching, runtime VR-placement/OSC settings, and end-to-end recording in the real application
- Complete structured diagnostics for app/OS/GPU/encoder, frame statistics, A/V sync, audio underruns/overruns, OSC, and finalization failures
- Approved Material Symbols assets, rights ledger, FFmpeg source offer, and final dependency inventory
- Hardware testing on Windows 10/11, NVIDIA/AMD/Intel, HMDs, and controllers
- All coverage, mutation, native-coverage, accessibility, and localization release gates
- Independent legal review, signing, and installer/final-payload rescanning
