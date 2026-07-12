# Audio test list / 音声テストリスト

## 日本語

- [x] 切断した音声inputだけを無音としscheduled sample countを保つ
- [x] desktop audio切断時はdefault render endpointを5秒間再探索する
基本設計書 v0.3 §12、§18.4、§24の48 kHz mix、routing、click防止、無音継続規則をRed–Green–Refactorで実装します。

- [x] Mic OFFは10 msでmic gainだけを0へランプする
- [x] Mic ONは10 msでmic gainを復帰させる
- [x] Mutedはdesktop／mic双方の寄与を0にする
- [x] Mutedでも無音AAC用のsample timelineを維持する
- [x] audio device loss時は該当入力だけを無音化する
- [x] 48 kHz stereo interleaved frame契約で両channelへ同じ10 ms gain rampを適用する
- [x] routing変更または早期device復帰時は現在の可聴gainから新しいrampを開始する
- [x] warning／status sink障害で連続audio timelineを中断しない
- [x] native device loss／recoveryを入力種別と48 kHz frame位置付きの型付きeventへ変換する
- [x] desktop／microphoneの喪失／復旧を独立に保持し、一方の復旧で他方のunavailable状態を解除しない
- [x] 診断／presentation observerの障害で録画sessionを停止しない
- [x] bounded callback queueの飽和時も入力別の最新availabilityへ収束する
- [x] float／PCM16／packed PCM24／PCM32とmono／stereo／multichannelを48 kHz stereoへ正規化する
- [x] packetをまたぐsample-rate変換phaseとtimestamp-error／discontinuity epochを保持する
- [x] event-driven WASAPI sourceがloopback／microphone、QPC、silent／loss flag、同一thread release、Abortを扱う
- [x] replacement endpointのStart失敗後もtimelineを破棄せず再接続を継続する
- [x] desktop／microphoneを同一48 kHz frame windowでmixし、片側lossだけを無音化する
- [x] 二入力のframe skewをmix前に拒否し、blocked readをAbortで解除する
- [x] WASAPI Start／Readを同一専用threadで実行し、初期化失敗時とAbort時にjoinする
- [x] microphone初期化失敗時は開始済みdesktop workerをrollbackし、部分開始sessionを残さない
- [x] mixed PCMの開始frameと固定sample数をencoder Portへ渡し、buffering中もsilent timelineを維持する
- [x] capture timeline overrunとmixed-window underrunをinput role／正確な48 kHz frame付きでnative media event sinkへ通知する
- [x] encoder失敗時は未mux frame／packetを成功統計へ加算しない
- [x] device loss／recoveryを入力roleと正確な48 kHz frameでproduction MediaEventへ伝播する
- [x] graceful stopではcapture解除後にencoderをflushし、Abort／encoder failureではflushせず両方を停止する
- [x] capture成功後だけencodingを開始し、pipeline単位でrouting／stop／最終統計を扱う
- [x] AAC-LC、48 kHz、stereo、192 kbpsと内部Float32 interleaved入力をnative encoder設定として固定する

## English

- [x] Replace only a disconnected audio input with silence while preserving the scheduled sample count
- [x] Search for the default render endpoint for five seconds after desktop-audio loss
The 48 kHz mixing, routing, click-prevention, and silence-continuity rules from Basic Design v0.3 §§12, 18.4, and 24 are implemented with Red–Green–Refactor.

- [x] Ramp only microphone gain to zero over 10 ms for Mic Off
- [x] Restore microphone gain over 10 ms for Mic On
- [x] Remove both desktop and microphone contributions when Muted
- [x] Preserve the sample timeline for silent AAC while Muted
- [x] Silence only the affected input when an audio device is lost
- [x] Apply one 10 ms gain ramp to both channels of the 48 kHz interleaved-stereo frame contract
- [x] Restart a ramp from the current audible gain after routing changes or early device recovery
- [x] Keep the continuous audio timeline when warning or status sinks fail
- [x] Translate native device-loss/recovery callbacks into typed events carrying the input kind and 48 kHz frame position
- [x] Track desktop/microphone loss and recovery independently without clearing the other input's unavailable state
- [x] Keep the recording session running when diagnostics or presentation observers fail
- [x] Converge to each input's latest availability when the bounded callback queue is saturated
- [x] Normalize float, PCM16, packed PCM24, PCM32, mono, stereo, and multichannel capture into 48 kHz stereo
- [x] Preserve sample-rate conversion phase and timestamp-error/discontinuity epochs across packets
- [x] Cover loopback/microphone, QPC, silent/loss flags, same-thread release, and abort in the event-driven WASAPI source
- [x] Keep the timeline recoverable after a replacement endpoint fails to start
- [x] Mix desktop and microphone over the same 48 kHz frame window while silencing only a lost side
- [x] Reject dual-input frame skew before mixing and release blocked reads on abort
- [x] Run WASAPI Start/Read on one dedicated thread and join it after initialization failure or abort
- [x] Roll back a started desktop worker when microphone initialization fails, leaving no partially started session
- [x] Submit positioned, fixed-size mixed PCM windows to the encoder port while preserving silent timelines during buffering
- [x] Notify the native media-event sink of capture-timeline overruns and mixed-window underruns with the input role and exact 48 kHz frame
- [x] Do not count unmuxed frames or packets as successful after an encoder failure
- [x] Propagate device loss/recovery into production media events with the input role and exact 48 kHz frame
- [x] Flush the encoder after releasing capture only on graceful stop, and stop both sides without flushing on abort or encoder failure
- [x] Start encoding only after capture succeeds and manage routing, stop, and final statistics at the pipeline-session level
- [x] Fix AAC-LC, 48 kHz, stereo, 192 kbps, and internal interleaved Float32 input in the native encoder configuration
