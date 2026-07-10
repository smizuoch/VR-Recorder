# Encoder selection test list / エンコーダー選択テストリスト

## 日本語

基本設計書 v0.3 §10.3、§11.2、§18.4、§24に従い、初期化成功ではなく実packet生成をprobe成功条件としてRed–Green–Refactorで実装します。

- [x] NVIDIA adapterでNVENC probeが失敗したらMF softwareへfallbackする
- [ ] AMD adapterではAMFを先にprobeする
- [ ] Intel adapterではQSVを先にprobeする
- [ ] 固定指定が失敗した場合は黙って別encoderを選ばず明示失敗する
- [x] packetを生成しないprobeは失敗として扱う

## English

Following Basic Design v0.3 §§10.3, 11.2, 18.4, and 24, probe success requires an actual encoded packet rather than initialization alone. The behavior is implemented with Red–Green–Refactor.

- [x] Fall back to MF software when NVENC probing fails on an NVIDIA adapter
- [ ] Probe AMF first on an AMD adapter
- [ ] Probe QSV first on an Intel adapter
- [ ] Fail explicitly instead of silently choosing another encoder when a fixed preference fails
- [x] Treat a probe that produces no packet as failed
