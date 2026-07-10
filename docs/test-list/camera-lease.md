# Camera lease test list / Camera Leaseテストリスト

## 日本語

基本設計書 v0.3 §9.3、§24のCameraLease所有権規則をRed–Green–Refactorで実装します。

- [x] RecorderがStreamingをfalseからtrueへ変更した場合だけfalseへ復元する
- [x] 以前からStreaming=trueなら停止時に変更しない
- [x] 前状態が不明でRecorderが変更した場合は既定policyでfalseへ復元する
- [x] 「不明時は変更しない」policyでは復元commandを送らない
- [x] session／VRChat service／process／UTC時刻と所有変更flagをatomic JSONへ保存する
- [x] 別sessionによるlease上書き・削除、重複／未知JSON、symlinkをfail-closedで拒否する
- [x] 起動時にlive ownerのleaseは変更せず保持する
- [x] stale leaseは所有したStreamingだけを記録された正確なVRChat serviceへfalse復元し、Modeは変更しない
- [x] stale leaseがStreamingを所有していなければcamera書込みなしで証拠を削除する
- [x] 対象欠落・復元失敗時はtyped warningを出し、失敗・cancel時は次回修復用leaseを保持する
- [x] 通常のcamera取得で注入identity sourceからpersist可能なleaseを作る
- [x] process開始時刻でPID再利用を識別し、別ownerをlive扱いしない

## English

The CameraLease ownership rules from Basic Design v0.3 §§9.3 and 24 are implemented with Red–Green–Refactor.

- [x] Restore false only when the recorder changed Streaming from false to true
- [x] Leave Streaming unchanged when it was already true
- [x] Restore false by default when the recorder changed an unknown prior state
- [x] Send no restore command under the leave-unknown-state policy
- [x] Persist session, VRChat service, process, UTC time, and owned-change flags in atomic JSON
- [x] Fail closed on cross-session overwrite/delete, duplicate or unknown JSON, and symlinks
- [x] Preserve a lease owned by a live process during startup
- [x] Restore only stale-lease-owned Streaming to false on its exact VRChat service and never change Mode
- [x] Delete stale evidence without a camera write when the lease owns no Streaming change
- [x] Publish a typed warning when the target or restore is unavailable, and retain repair evidence on failure or cancellation
- [x] Create a persistable lease from an injected identity source during normal camera acquisition
- [x] Distinguish PID reuse by process start time instead of treating another owner as live
