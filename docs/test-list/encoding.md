# Encoder selection test list / エンコーダー選択テストリスト

## 日本語

基本設計書 v0.3 §10.3、§11.2、§18.4、§24に従い、初期化成功ではなく実packet生成をprobe成功条件としてRed–Green–Refactorで実装します。

- [x] NVIDIA adapterでNVENC probeが失敗したらMF softwareへfallbackする
- [x] AMD adapterではAMFを先にprobeする
- [x] Intel adapterではQSVを先にprobeする
- [x] 固定指定が失敗した場合は黙って別encoderを選ばず明示失敗する
- [x] packetを生成しないprobeは失敗として扱う
- [x] 全encoder probe requestを安定化したSpout senderと同じadapter LUIDへ固定する
- [x] 出力寸法・fps・同一adapterをnative ABIへ渡し16合成frameを実encodeする
- [x] 初期化だけでなくpacket生成時だけnative probeを成功としてmanagedへ返す
- [x] native probe v2はcodec名・hardware mode・adapter・D3D11/QSV/system-memory入力形式をrequested kindの固定対応と照合し、parse可能AU、SPS、PPS、IDR、display寸法、profile、fps、B-frame 0、decode、hardware same-adapterの全証拠が揃わなければ結果を公開しない

## English

Following Basic Design v0.3 §§10.3, 11.2, 18.4, and 24, probe success requires an actual encoded packet rather than initialization alone. The behavior is implemented with Red–Green–Refactor.

- [x] Fall back to MF software when NVENC probing fails on an NVIDIA adapter
- [x] Probe AMF first on an AMD adapter
- [x] Probe QSV first on an Intel adapter
- [x] Fail explicitly instead of silently choosing another encoder when a fixed preference fails
- [x] Treat a probe that produces no packet as failed
- [x] Pin every encoder probe request to the same adapter LUID as the stabilized Spout sender
- [x] Pass output geometry, frame rate, and the same adapter through the native ABI and encode 16 synthetic frames
- [x] Report native probe success to managed code only when an encoded packet is produced
- [x] Match probe-v2 codec name, hardware mode, adapter, and D3D11/QSV/system-memory input format to the requested kind, publishing no result unless the evidence proves a parseable AU, SPS, PPS, IDR, display dimensions, profile, frame rate, zero B-frames, decode, and hardware same-adapter ownership
