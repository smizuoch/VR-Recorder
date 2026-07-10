# Recording timing test list / 録画タイミングテストリスト

## 日本語

基本設計書 v0.3 §7.1、§18、§24のself timerとauto stop規則をRed–Green–Refactorで実装します。

- [x] SelfTimerはOff／3／5／10秒だけを受け入れる
- [ ] Countdown中のCancelでengineを開始しない
- [ ] AutoStopはCountdown開始ではなくFirstPacketCommittedから計測する
- [ ] 3秒のAutoStopでStopRequestedを1回だけ発行する

## English

The self-timer and auto-stop rules from Basic Design v0.3 §§7.1, 18, and 24 are implemented with Red–Green–Refactor.

- [x] SelfTimer accepts only Off, 3, 5, and 10 seconds
- [ ] Cancel during Countdown does not start the engine
- [ ] AutoStop starts at FirstPacketCommitted, not at Countdown
- [ ] A three-second AutoStop issues StopRequested exactly once
