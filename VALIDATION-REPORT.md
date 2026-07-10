# Validation report / 検証報告

## 日本語

### 現在の判定

実装進行中であり、release適格ではありません。Linux／WSL2上で実行可能なmanaged Domain、Application、Complianceの範囲だけを検証しています。WPF、Spout2、D3D11、WASAPI、FFmpeg、OpenVR、実VRChat、Windows 10／11、GPU／HMDの検証は未実施です。

第三者台帳の現在のentryはtest-only NuGet依存のcandidateです。version、NuGet content hash、package archive SHA-256、上流commit、license全文hashを固定していますが、全entryの`approval.status`は`pending-independent-review`です。署名・公開・end-user Legal Bundleの承認を意味しません。

### 自動検証

2026-07-10時点で次を実行し、成功を確認しています。

```text
dotnet restore VR-Recorder.sln --locked-mode
dotnet test VR-Recorder.sln --configuration Debug --no-restore
dotnet format VR-Recorder.sln --verify-no-changes --no-restore
```

テストは、状態遷移、signal-before-start、signal timeout、countdown cancel、CameraLease所有権、映像orientation／padding／Contain、およびNuGet／license candidate fail-closed検証を対象にします。設計書の全受入シナリオ、90% integration coverage、75% mutation score、native coverageを満たしたという意味ではありません。

## English

### Current verdict

Implementation is in progress and is not release-eligible. Validation currently covers only the managed Domain, Application, and Compliance code that can run on Linux/WSL2. WPF, Spout2, D3D11, WASAPI, FFmpeg, OpenVR, real VRChat, Windows 10/11, GPU, and HMD validation have not been performed.

The current third-party registry entries are candidates for test-only NuGet dependencies. Versions, NuGet content hashes, package-archive SHA-256 values, upstream commits, and full-license-text hashes are pinned, but every entry has `approval.status` set to `pending-independent-review`. This is not approval for signing, publication, or an end-user Legal Bundle.

### Automated validation

The following commands were run successfully as of 2026-07-10:

```text
dotnet restore VR-Recorder.sln --locked-mode
dotnet test VR-Recorder.sln --configuration Debug --no-restore
dotnet format VR-Recorder.sln --verify-no-changes --no-restore
```

Tests cover state transitions, signal-before-start, signal timeout, countdown cancellation, CameraLease ownership, video orientation/padding/Contain, and fail-closed NuGet/license candidate checks. This does not claim completion of every acceptance scenario, 90% integration coverage, a 75% mutation score, or native coverage required by the design.
