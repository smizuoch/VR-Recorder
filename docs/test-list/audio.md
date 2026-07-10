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
