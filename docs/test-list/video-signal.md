# Video signal test list / 映像信号テストリスト

## 日本語

基本設計書 v0.3 §4.2、§10.2、§18.4、§24のfresh-frame基準をRed–Green–Refactorで実装します。画素の黒さは信号断条件に使用しません。

- [x] 黒画素でもfresh frameならAvailableを維持する
- [x] 1.5秒fresh frameがなければSignalLostへ遷移する
- [ ] SignalLost後5秒復帰しなければSafeStopを要求する
- [ ] 5秒以内にfresh frameが戻ればAvailableへ復帰する

## English

The fresh-frame rules from Basic Design v0.3 §§4.2, 10.2, 18.4, and 24 are implemented with Red–Green–Refactor. Pixel blackness is never used as a signal-loss condition.

- [x] Keep the signal Available for a fresh black frame
- [x] Enter SignalLost after 1.5 seconds without a fresh frame
- [ ] Request SafeStop after five seconds without recovery from SignalLost
- [ ] Return to Available when a fresh frame arrives within five seconds
