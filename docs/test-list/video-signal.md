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
- [x] video workerはgraceful stopだけをflushし、Abort／runtime failureではflushしない
- [x] first mux packet callbackをwriteまたはflushの最初の一度だけ発行する
- [x] selected Spout sender以外のframeをCFR schedulerへ入れない
- [x] Spout poll timeout、sender loss、Abortを別結果として扱う
- [x] Spout timeout後も専用capture threadを継続し、sender loss／invalid frameでterminal終了する
- [x] captureをencoderより先に開始し、encoder開始失敗時はcaptureをrollbackする
- [x] graceful stopではcaptureを先に中断してからencoderをflushし、sender loss時はencoderをAbortしてfaultを通知する
- [x] encoder／CFR clockが先に失敗した場合は待機中のcaptureをAbortして双方をjoinする
- [x] Spout textureの共有所有権とdescriptorをcaptureからCFR出力へ保持し、metadata不一致を拒否する
- [x] shared surfaceをbounded timeoutでAcquireし、encoder成功／失敗の双方でReleaseし、timeoutと同期failureを分離する

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
- [x] Flush the video encoder only for graceful stop, not for abort or runtime failure
- [x] Emit the first-muxed-packet callback only once, whether produced by a write or final flush
- [x] Never admit a frame from an unselected Spout sender into the CFR scheduler
- [x] Keep Spout poll timeout, sender loss, and abort as distinct outcomes
- [x] Continue the dedicated capture thread after Spout timeouts and terminate on sender loss or invalid frames
- [x] Start capture before encoding and roll capture back when encoding cannot start
- [x] On graceful stop, halt capture before flushing the encoder; on sender loss, abort encoding and report a fault
- [x] If the encoder or CFR clock fails first, abort the waiting capture worker and join both sides
- [x] Retain shared Spout-texture ownership and its descriptor from capture through CFR output, rejecting metadata mismatches
- [x] Acquire shared surfaces with a bounded timeout, release them after both encoder success and failure, and distinguish timeout from synchronization failure
