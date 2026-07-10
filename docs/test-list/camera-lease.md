# Camera lease test list / Camera Leaseテストリスト

## 日本語

基本設計書 v0.3 §9.3、§24のCameraLease所有権規則をRed–Green–Refactorで実装します。

- [ ] RecorderがStreamingをfalseからtrueへ変更した場合だけfalseへ復元する
- [ ] 以前からStreaming=trueなら停止時に変更しない
- [ ] 前状態が不明でRecorderが変更した場合は既定policyでfalseへ復元する
- [ ] 「不明時は変更しない」policyでは復元commandを送らない

## English

The CameraLease ownership rules from Basic Design v0.3 §§9.3 and 24 are implemented with Red–Green–Refactor.

- [ ] Restore false only when the recorder changed Streaming from false to true
- [ ] Leave Streaming unchanged when it was already true
- [ ] Restore false by default when the recorder changed an unknown prior state
- [ ] Send no restore command under the leave-unknown-state policy
