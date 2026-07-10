# Recording Core test list / Recording Coreテストリスト

## 日本語

基本設計書 v0.3 §24のうち、Recording Coreの最初の縦切りで扱うテストリストです。各項目を1件ずつRed–Green–Refactorで実装します。

- [x] ReadyでStartRequestedを受けるとArmingになる
- [x] Arming中の2回目StartRequestedは無視される
- [x] StableSignal前にはRecordingEngine.Startを呼ばない
- [x] SignalTimeoutではファイルを作らない
- [x] Countdown中のCancelでencoderを開始しない
- [ ] Recording中のStopRequestedを2回受けてもStopAsyncは1回だけ

## English

This is the first vertical-slice test list for Recording Core, derived from Basic Design v0.3 §24. Each item is implemented one at a time using Red–Green–Refactor.

- [x] StartRequested transitions Ready to Arming
- [x] A second StartRequested while Arming is ignored
- [x] RecordingEngine.Start is not called before StableSignal
- [x] SignalTimeout creates no file
- [x] Cancel during Countdown does not start the encoder
- [ ] Two StopRequested events while Recording call StopAsync only once
