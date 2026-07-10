# Audio test list / 音声テストリスト

## 日本語

基本設計書 v0.3 §12、§18.4、§24の48 kHz mix、routing、click防止、無音継続規則をRed–Green–Refactorで実装します。

- [x] Mic OFFは10 msでmic gainだけを0へランプする
- [ ] Mic ONは10 msでmic gainを復帰させる
- [ ] Mutedはdesktop／mic双方の寄与を0にする
- [ ] Mutedでも無音AAC用のsample timelineを維持する
- [ ] audio device loss時は該当入力だけを無音化する

## English

The 48 kHz mixing, routing, click-prevention, and silence-continuity rules from Basic Design v0.3 §§12, 18.4, and 24 are implemented with Red–Green–Refactor.

- [x] Ramp only microphone gain to zero over 10 ms for Mic Off
- [ ] Restore microphone gain over 10 ms for Mic On
- [ ] Remove both desktop and microphone contributions when Muted
- [ ] Preserve the sample timeline for silent AAC while Muted
- [ ] Silence only the affected input when an audio device is lost
