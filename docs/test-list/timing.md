# Recording timing test list / 録画タイミングテストリスト

## 日本語

基本設計書 v0.3 §7.1、§18、§24のself timerとauto stop規則をRed–Green–Refactorで実装します。

- [x] SelfTimerはOff／3／5／10秒だけを受け入れる
- [x] RecordingDurationは∞／3／5／10／30／60秒だけを受け入れる
- [x] FirstPacketCommittedの単調時刻へdurationを加えてdeadlineを作る
- [x] Countdown中のCancelでengineを開始しない
- [x] AutoStopはCountdown開始ではなくFirstPacketCommittedから計測する
- [x] 3秒のAutoStopでStopRequestedを1回だけ発行する

## English

The self-timer and auto-stop rules from Basic Design v0.3 §§7.1, 18, and 24 are implemented with Red–Green–Refactor.

- [x] SelfTimer accepts only Off, 3, 5, and 10 seconds
- [x] RecordingDuration accepts only infinite, 3, 5, 10, 30, and 60 seconds
- [x] Build the deadline by adding duration to the monotonic FirstPacketCommitted time
- [x] Cancel during Countdown does not start the engine
- [x] AutoStop starts at FirstPacketCommitted, not at Countdown
- [x] A three-second AutoStop issues StopRequested exactly once
