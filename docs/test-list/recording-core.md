# Recording Core test list / Recording Coreテストリスト

## 日本語

基本設計書 v0.3 §24のうち、Recording Coreの最初の縦切りで扱うテストリストです。各項目を1件ずつRed–Green–Refactorで実装します。

- [x] ReadyでStartRequestedを受けるとArmingになる
- [x] Recorder ActorがArming中に受けた重複StartRequestedは無視される
- [x] Desktop／Inputの2回目RECはArming／Countdownをキャンセルして元のReady／NoSignalへ戻す
- [x] StableSignal前にはRecordingEngine.Startを呼ばない
- [x] SignalTimeoutではファイルを作らない
- [x] Countdown中のCancelでencoderを開始しない
- [x] 開始キャンセルではCameraLeaseを復元・削除してcamera gatewayを1回だけ破棄する
- [x] snapshot失敗、NoSignal、録画完了でも所有したcamera gatewayを1回だけ破棄する
- [x] NoSignalからprocess再起動なしで再試行できる
- [x] Recording中のStopRequestedを2回受けてもStopAsyncは1回だけ
- [x] 通常終了とComplianceFaultを型付き停止理由で区別し、競合時は最初の理由で1回だけ安全停止する

## English

This is the first vertical-slice test list for Recording Core, derived from Basic Design v0.3 §24. Each item is implemented one at a time using Red–Green–Refactor.

- [x] StartRequested transitions Ready to Arming
- [x] The Recorder Actor ignores a duplicate StartRequested while Arming
- [x] A second desktop/input REC cancels Arming/Countdown and restores the original Ready/NoSignal state
- [x] RecordingEngine.Start is not called before StableSignal
- [x] SignalTimeout creates no file
- [x] Cancel during Countdown does not start the encoder
- [x] Start cancellation restores/deletes the CameraLease and disposes the camera gateway exactly once
- [x] Snapshot failure, NoSignal, and recording completion also dispose the owned camera gateway exactly once
- [x] NoSignal can be retried without restarting the process
- [x] Two StopRequested events while Recording call StopAsync only once
- [x] Normal shutdown and ComplianceFault keep distinct typed stop reasons and a race safely stops once with the first reason
