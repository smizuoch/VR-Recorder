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
- [x] video encoding thread生成のOOM／internal／non-joinableを終端失敗にし、生成中Abortを優先して遅延threadをjoinし、Faulted／first packet／flush統計とAbort後に成功復帰したWriteのpacket／latencyを抑止する
- [x] first mux packet callbackをwriteまたはflushの最初の一度だけ発行する
- [x] selected Spout sender以外のframeをCFR schedulerへ入れない
- [x] Spout poll timeout、sender loss、Abortを別結果として扱う
- [x] Spout timeout後も専用capture threadを継続し、sender loss／invalid frameでterminal終了する
- [x] Spout capture thread生成のOOM／internal／non-joinableを終端失敗にし、生成中Abortではpublicationを待って遅延runnerのPollを抑止し、Poll中Abort後のSenderLostで結果やsource Abort回数を上書きしない
- [x] captureをencoderより先に開始し、encoder開始失敗時はcaptureをAbort／Joinしてrollbackする
- [x] graceful stopではcaptureを先に中断してからencoderをflushし、sender loss時はencoderをAbortしてfaultを通知する
- [x] encoder／CFR clockが先に失敗した場合は待機中のcaptureをAbortして双方をjoinする
- [x] forced Abortはcapture／encoding双方へ停止signalを送り、両workerをjoinしてから戻る
- [x] Start rollbackと通常Joinの完了前にAbort cleanupを早期復帰させず、RequestStop／JoinとAbortを単一terminal winnerで仲裁する。SenderLost／Failedと競合したAbortはFaultedを抑止し、capture Abortとencoding物理cleanupをexactly once回収する
- [x] video sessionのencoding生成中Abortを実workerへ即時転送し、capture Join補助threadのOOM／internal／non-joinableとpublication中Abortを注入して、開始済みworkerを回収しFaulted／first packet／Stoppedを抑止する
- [x] Spout textureの共有所有権とdescriptorをcaptureからCFR出力へ保持し、metadata不一致を拒否する
- [x] shared surfaceをbounded timeoutでAcquireし、encoder成功／失敗の双方でReleaseし、timeoutと同期failureを分離する
- [x] 奇数textureを右／下へ最大1pxパディングし、整数演算のSingleFileFitで偶数contain配置とNV12処理計画を生成する
- [x] processor出力のAdapter LUID／寸法／NV12／native handleを検証してからencoderへ渡し、processing failureをencoder failureと分離する
- [x] live video layoutを固定canvasに対して検証して原子的に保持し、次frameから明示destinationを使用しつつsource寸法不一致をfail-closedに拒否する
- [x] NV12、High→Main降格、品質優先VBR、2秒GOP、8–80 Mbps target／1.5倍maxrateのH.264設定を導出する

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
- [x] Terminalize video-encoding thread OOM, internal failure, and non-joinable success; let Abort win during launch, join delayed threads, and suppress Faulted, first-packet, flush-stat, and post-Abort successful-Write packet/latency commits
- [x] Emit the first-muxed-packet callback only once, whether produced by a write or final flush
- [x] Never admit a frame from an unselected Spout sender into the CFR scheduler
- [x] Keep Spout poll timeout, sender loss, and abort as distinct outcomes
- [x] Continue the dedicated capture thread after Spout timeouts and terminate on sender loss or invalid frames
- [x] Terminalize Spout-capture thread OOM, internal failure, and non-joinable success; wait for launch publication during Abort, prevent a delayed runner from polling, and keep post-Abort SenderLost from replacing the result or duplicating source Abort
- [x] Start capture before encoding and abort/join capture when encoding cannot start
- [x] On graceful stop, halt capture before flushing the encoder; on sender loss, abort encoding and report a fault
- [x] If the encoder or CFR clock fails first, abort the waiting capture worker and join both sides
- [x] On forced abort, signal both capture and encoding to stop and join both workers before returning
- [x] Do not return Abort cleanup before startup rollback or a concurrent normal Join completes; arbitrate RequestStop/Join with Abort through one terminal winner, suppress Faulted when Abort races SenderLost/Failed, and reclaim capture abort plus physical encoding cleanup exactly once
- [x] Forward Abort immediately to a real encoding worker while video-session launch is blocked; inject OOM, internal failure, non-joinable success, and Abort during capture-Join-helper publication, reclaim started workers, and suppress Faulted, first-packet, and Stopped events
- [x] Retain shared Spout-texture ownership and its descriptor from capture through CFR output, rejecting metadata mismatches
- [x] Acquire shared surfaces with a bounded timeout, release them after both encoder success and failure, and distinguish timeout from synchronization failure
- [x] Pad odd textures by at most one pixel on the right/bottom and use integer SingleFileFit math to produce an even contain placement and NV12 processing plan
- [x] Validate processor-output adapter LUID, dimensions, NV12 format, and native handle before encoding, keeping processing failures distinct from encoder failures
- [x] Validate live video layouts against the fixed canvas, retain them atomically, apply the explicit destination from the next frame, and fail closed on source-dimension mismatch
- [x] Derive H.264 settings for NV12, High-to-Main fallback, quality VBR, a two-second GOP, an 8–80 Mbps target, and a 1.5x maximum rate
