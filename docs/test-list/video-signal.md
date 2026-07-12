# Video signal test list / 映像信号テストリスト

## 日本語

基本設計書 v0.3 §4.2、§10.2、§18.4、§24のfresh-frame基準をRed–Green–Refactorで実装します。画素の黒さは信号断条件に使用しません。

- [x] 黒画素でもfresh frameならAvailableを維持する
- [x] 1.5秒fresh frameがなければSignalLostへ遷移する
- [x] SignalLost後5秒復帰しなければSafeStopを要求する
- [x] 5秒以内にfresh frameが戻ればAvailableへ復帰する
- [x] CFR tickでは最新source frameだけを採用し、中間frameをdropとして集計する
- [x] 新規frameがないtickでは直前frameをduplicateし、最初のframe前は出力しない
- [x] encoder buffering後の最初のmux packetだけを録画開始確定eventとして識別する
- [x] runtime encoder failureのpacket／latencyを成功統計へ加算しない

## English

The fresh-frame rules from Basic Design v0.3 §§4.2, 10.2, 18.4, and 24 are implemented with Red–Green–Refactor. Pixel blackness is never used as a signal-loss condition.

- [x] Keep the signal Available for a fresh black frame
- [x] Enter SignalLost after 1.5 seconds without a fresh frame
- [x] Request SafeStop after five seconds without recovery from SignalLost
- [x] Return to Available when a fresh frame arrives within five seconds
- [x] Select only the latest source frame at each CFR tick and count discarded intermediate frames
- [x] Duplicate the previous frame when a tick has no new input, while producing nothing before the first frame
- [x] Identify only the first muxed packet after encoder buffering as the recording-start commit event
- [x] Do not commit packet or latency statistics from a runtime encoder failure
