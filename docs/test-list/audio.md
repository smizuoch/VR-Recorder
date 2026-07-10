# Audio test list / 音声テストリスト

## 日本語

- [x] 切断した音声inputだけを無音としscheduled sample countを保つ
基本設計書 v0.3 §12、§18.4、§24の48 kHz mix、routing、click防止、無音継続規則をRed–Green–Refactorで実装します。

- [x] Mic OFFは10 msでmic gainだけを0へランプする
- [x] Mic ONは10 msでmic gainを復帰させる
- [x] Mutedはdesktop／mic双方の寄与を0にする
- [x] Mutedでも無音AAC用のsample timelineを維持する
- [ ] audio device loss時は該当入力だけを無音化する

## English

- [x] Replace only a disconnected audio input with silence while preserving the scheduled sample count
The 48 kHz mixing, routing, click-prevention, and silence-continuity rules from Basic Design v0.3 §§12, 18.4, and 24 are implemented with Red–Green–Refactor.

- [x] Ramp only microphone gain to zero over 10 ms for Mic Off
- [x] Restore microphone gain over 10 ms for Mic On
- [x] Remove both desktop and microphone contributions when Muted
- [x] Preserve the sample timeline for silent AAC while Muted
- [ ] Silence only the affected input when an audio device is lost
