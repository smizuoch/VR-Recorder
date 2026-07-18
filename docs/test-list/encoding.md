# Encoder selection test list / エンコーダー選択テストリスト

## 日本語

基本設計書 v0.3 §10.3、§11.2、§18.4、§24に従い、初期化成功ではなく実packet生成をprobe成功条件としてRed–Green–Refactorで実装します。

- [x] NVIDIA adapterでNVENC probeが失敗したらMF softwareへfallbackする
- [x] AMD adapterではAMFを先にprobeする
- [x] Intel adapterではQSVを先にprobeする
- [x] 固定指定が失敗した場合は黙って別encoderを選ばず明示失敗する
- [x] probe後も元のAuto／固定指定をrecording planへ保持し、選択済み実encoderだけからfallback可否を推測しない
- [x] 開始前と録画中のsoftware fallback／part rolloverはAutoだけに許可し、固定指定では元のfailureを報告して予約fileを変更しない
- [x] Autoの開始前software retryはtyped VideoEncoder faultだけに許可し、同じstatus／messageを持つSpout／audio／mux／unknown failureを文字列判定でfallbackしない
- [x] production H.264 factoryがCreate中に失敗した場合はtyped VideoEncoder faultを同期通知し、managed側は後続のgeneric create statusよりそのstatus／message／sourceを優先する
- [x] production routeはNVENCを`h264_nvenc`＋D3D11 NV12、AMFを`h264_amf`＋D3D11 NV12、QSVを`h264_qsv`＋同じD3D11 deviceから派生したQSV frames、MF softwareを`h264_mf`＋system-memory NV12へexact mappingし、全hardware routeでsource／processor／encoderのadapter LUID一致を検証する
- [x] NVIDIA実機でD3D11 texture→NVENC packet→AAC付きfragmented MP4→別oracle decodeを通し、AMF／QSVは同じsession構造とfail-closed probeまで固定して対応GPUの最終HILへ送る
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
- [x] Preserve the original Auto/fixed preference in the recording plan after probing instead of inferring fallback policy from only the selected encoder
- [x] Permit pre-start software retry and in-recording software part rollover only for Auto, reporting the original failure and leaving reserved files unchanged for fixed preferences
- [x] Permit Auto's pre-start software retry only for a typed VideoEncoder fault, never using status/message matching to fall back on Spout, audio, mux, or unknown failures
- [x] When the production H.264 factory fails during Create, synchronously publish a typed VideoEncoder fault and preserve its status, message, and source in managed code instead of replacing it with the subsequent generic create status
- [x] Map production NVENC to `h264_nvenc` plus D3D11 NV12, AMF to `h264_amf` plus D3D11 NV12, QSV to `h264_qsv` plus QSV frames derived from the same D3D11 device, and MF software to `h264_mf` plus system-memory NV12, verifying the source/processor/encoder adapter LUID for every hardware route
- [x] On real NVIDIA hardware, pass D3D11 texture → NVENC packet → AAC fragmented MP4 → independent oracle decode; keep AMF/QSV on the same session structure and fail-closed probe pending final matching-GPU HIL
- [x] Treat a probe that produces no packet as failed
- [x] Pin every encoder probe request to the same adapter LUID as the stabilized Spout sender
- [x] Pass output geometry, frame rate, and the same adapter through the native ABI and encode 16 synthetic frames
- [x] Report native probe success to managed code only when an encoded packet is produced
- [x] Match probe-v2 codec name, hardware mode, adapter, and D3D11/QSV/system-memory input format to the requested kind, publishing no result unless the evidence proves a parseable AU, SPS, PPS, IDR, display dimensions, profile, frame rate, zero B-frames, decode, and hardware same-adapter ownership
