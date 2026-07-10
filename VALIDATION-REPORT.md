# Validation report / 検証報告

## 日本語

### 現在の判定

実装進行中であり、release適格ではありません。2026-07-10現在、Linux／WSL2で実行できるmanagedテスト、Windows x64向けWPF cross-build、Linux native ABI、CMake構成を検証しています。Windows上でのWPF実行、Spout2、D3D11、WASAPI、FFmpeg、OpenVR、実VRChat、Windows 10／11、GPU／HMDの検証は未実施です。

production native media／SteamVR backendは意図的に`BACKEND_UNAVAILABLE`を返し、desktop production compositionも録画不可としてfail closedにします。したがって、現状は設計契約と境界実装を検証する開発checkpointであり、録画可能な製品ではありません。

第三者台帳の現在のentryはtest-only NuGet依存のcandidateです。version、NuGet content hash、package archive SHA-256、上流commit、license全文hashを固定していますが、全entryの`approval.status`は`pending-independent-review`です。署名・公開・end-user Legal Bundleの承認を意味しません。

### 自動検証

2026-07-10に次を実行し、成功を確認しました。

```text
dotnet restore VR-Recorder.sln --locked-mode
dotnet test VR-Recorder.sln --configuration Debug --no-restore
dotnet build src/VRRecorder.App/VRRecorder.App.csproj --configuration Debug --runtime win-x64 --no-restore
dotnet format VR-Recorder.sln --verify-no-changes --no-restore
make -C tests/VRRecorder.Native.Tests clean test
cmake -S . -B <fresh-build> -G Ninja -DBUILD_TESTING=ON
cmake --build <fresh-build>
ctest --test-dir <fresh-build> --output-on-failure
```

- managed: 443件成功、失敗0、skip 0
  - Domain 82
  - Application 67
  - Compliance 88
  - Presentation 55
  - Integration 151
- WPF `win-x64` cross-build: warning 0、error 0
- native Make ABI contract: 成功
- native CMake/CTest: 4/4成功
- native公開symbol allowlist: 11/11一致
- format/analyzer: 差分なし

### 結合テスト単独coverage

設計書18.5に従い、`VRRecorder.IntegrationTests`だけを実行してCoberturaを収集しました。

| Assembly | Line | Branch |
|---|---:|---:|
| 全体 | 76.98% (8150/10587) | 61.17% (1489/2434) |
| Application | 78.98% | 63.11% |
| Compliance | 71.52% | 56.97% |
| DesignSystem | 39.72% | 25.00% |
| Domain | 72.54% | 56.42% |
| Infrastructure.Media | 85.16% | 66.98% |
| Infrastructure.Osc | 83.26% | 66.52% |
| Infrastructure.SteamVr | 79.61% | 70.00% |
| Infrastructure.Storage | 84.81% | 66.66% |
| Presentation.Wrist | 73.61% | 56.66% |

全体および各主要assemblyのline／branch 90%ゲートは未達です。mutation score 75%、native line／branch coverage 90%、Windows UI Automation、hardware-in-the-loopも未測定です。

### このcheckpointで検証済みの主な境界

- 録画状態遷移、countdown／auto-stop、signal監視、容量低下停止、同一file確定
- CameraLease所有権、部分取得rollback、stale leaseのowned Streaming復旧
- OSCQuery target解決、UDP write確認、SteamVR Input Action ABI
- SingleFileFit contain計算とruntime layout更新、native最終statistics取得
- strict Legal catalog v3生成／認証読取、型付きlegal document、tamper／symlink／UTF-8 fail-closed
- production wall／monotonic clockとcancel可能なcountdown adapter

### 未完了の主要release gate

- 実Spout2／D3D11／WASAPI／encoder／muxer backendとproduction composition
- 実OpenVR overlay、Wrist renderer、haptics、move／pin操作
- 初回setup、設定、通知領域、構造化log、実アプリのend-to-end録画
- 承認済みMaterial Symbols asset、rights ledger、FFmpeg source offer、最終依存inventory
- Windows 10／11およびNVIDIA／AMD／Intel、HMD／controllerでの実機試験
- coverage／mutation／native coverage／accessibility／localizationの全release gate
- 独立法務review、署名、installer／最終payload再スキャン

## English

### Current verdict

Implementation is in progress and is not release-eligible. As of 2026-07-10, validation covers managed tests runnable on Linux/WSL2, a Windows x64 WPF cross-build, the Linux native ABI, and the CMake configuration. Running WPF on Windows and validating Spout2, D3D11, WASAPI, FFmpeg, OpenVR, real VRChat, Windows 10/11, GPUs, and HMDs remain outstanding.

The production native media and SteamVR backends intentionally return `BACKEND_UNAVAILABLE`, and the desktop production composition fails closed as recording-unavailable. This is therefore a development checkpoint for design contracts and boundary implementations, not a recording-capable product.

The current third-party registry entries are candidates for test-only NuGet dependencies. Versions, NuGet content hashes, package-archive SHA-256 values, upstream commits, and full-license-text hashes are pinned, but every entry has `approval.status` set to `pending-independent-review`. This is not approval for signing, publication, or an end-user Legal Bundle.

### Automated validation

The following commands were run successfully on 2026-07-10:

```text
dotnet restore VR-Recorder.sln --locked-mode
dotnet test VR-Recorder.sln --configuration Debug --no-restore
dotnet build src/VRRecorder.App/VRRecorder.App.csproj --configuration Debug --runtime win-x64 --no-restore
dotnet format VR-Recorder.sln --verify-no-changes --no-restore
make -C tests/VRRecorder.Native.Tests clean test
cmake -S . -B <fresh-build> -G Ninja -DBUILD_TESTING=ON
cmake --build <fresh-build>
ctest --test-dir <fresh-build> --output-on-failure
```

- managed: 443 passed, 0 failed, 0 skipped
  - Domain 82
  - Application 67
  - Compliance 88
  - Presentation 55
  - Integration 151
- WPF `win-x64` cross-build: 0 warnings, 0 errors
- native Make ABI contract: passed
- native CMake/CTest: 4/4 passed
- native public-symbol allowlist: exact 11/11 match
- format/analyzers: no changes required

### Integration-test-only coverage

Following design section 18.5, Cobertura coverage was collected by running only `VRRecorder.IntegrationTests`.

| Assembly | Line | Branch |
|---|---:|---:|
| Overall | 76.98% (8150/10587) | 61.17% (1489/2434) |
| Application | 78.98% | 63.11% |
| Compliance | 71.52% | 56.97% |
| DesignSystem | 39.72% | 25.00% |
| Domain | 72.54% | 56.42% |
| Infrastructure.Media | 85.16% | 66.98% |
| Infrastructure.Osc | 83.26% | 66.52% |
| Infrastructure.SteamVr | 79.61% | 70.00% |
| Infrastructure.Storage | 84.81% | 66.66% |
| Presentation.Wrist | 73.61% | 56.66% |

The 90% line and branch gates, both overall and per major assembly, are not met. The 75% mutation score, 90% native line and branch coverage, Windows UI Automation, and hardware-in-the-loop gates have not been measured.

### Main boundaries validated at this checkpoint

- Recording state transitions, countdown/auto-stop, signal supervision, low-space stop, and same-file finalization
- CameraLease ownership, partial-acquisition rollback, and owned Streaming recovery from stale leases
- OSCQuery target resolution, confirmed UDP writes, and the SteamVR Input Action ABI
- SingleFileFit contain calculation, runtime layout updates, and final native statistics retrieval
- Strict Legal catalog v3 generation/authenticated reading, typed legal documents, and fail-closed tamper/symlink/UTF-8 handling
- Production wall/monotonic clocks and a cancellable countdown adapter

### Major outstanding release gates

- Real Spout2/D3D11/WASAPI/encoder/muxer backends and production composition
- Real OpenVR overlay, wrist renderer, haptics, and move/pin controls
- First-run setup, settings, notification area, structured logs, and end-to-end recording in the real application
- Approved Material Symbols assets, rights ledger, FFmpeg source offer, and final dependency inventory
- Hardware testing on Windows 10/11, NVIDIA/AMD/Intel, HMDs, and controllers
- All coverage, mutation, native-coverage, accessibility, and localization release gates
- Independent legal review, signing, and installer/final-payload rescanning
