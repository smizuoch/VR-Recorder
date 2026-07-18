# Video geometry test list / 映像ジオメトリテストリスト

## 日本語

基本設計書 v0.3 §10.2、§10.5、§24の解像度・縦横・Contain規則をRed–Green–Refactorで実装します。

- [x] `height > width`をPortraitと判定する
- [x] `width >= height`をLandscapeと判定する
- [x] 1921×1081を4:2:0用に1922×1082へ最大1 px padする
- [x] 偶数寸法にはpadしない
- [x] SingleFileFitで1920×1080へ1080×1920を歪みなくContainする
- [x] 録画中に幅・高さ・pixel format・surface generationが変わったframeを隔離し、同じ新signatureがsource timestampで500 ms以上継続した場合だけ安定変更として1回通知する
- [x] 安定前に元のgeometryへ戻るか別signatureへ変わった場合は候補をresetし、変更frameをschedulerへ流さない
- [x] SingleFileFitは固定canvasのlayout更新成功をnative captureへacknowledgeしてから新geometryのframeを再開する
- [x] ExactFollowSegmentsはpart001を入力と同じ偶数寸法で開始し、安定変更ごとに同じ録画handle／audio routing／encoder選択を保ったpart002、part003へ移行する
- [x] ExactFollowSegmentsの各partは入力と同じ寸法・無paddingとし、H.264 4:2:0で厳密一致できない奇数寸法はfail-closedで拒否する
- [x] 次partの最初のvideo packetがmuxされるまで前partをfinal公開せず、開始失敗時に前partの停止結果を保持する
- [ ] 実VRChat senderの解像度／縦横／pixel format切替から生成した各fMP4 partを、freeze済みpayloadのoracleと実GPUで検証する

## English

The resolution, orientation, and Contain rules from Basic Design v0.3 §§10.2, 10.5, and 24 are implemented with Red–Green–Refactor.

- [x] Classify `height > width` as Portrait
- [x] Classify `width >= height` as Landscape
- [x] Pad 1921×1081 by at most one pixel to 1922×1082 for 4:2:0
- [x] Do not pad even dimensions
- [x] Contain 1080×1920 without distortion in a 1920×1080 SingleFileFit canvas
- [x] Quarantine in-recording frames whose width, height, pixel format, or surface generation changes and emit one stable-change notification only after the same new signature persists for at least 500 ms of source timestamps
- [x] Reset an unstable candidate when the original geometry returns or another signature appears, without submitting changed frames to the scheduler
- [x] Resume new-geometry frames in SingleFileFit only after the fixed-canvas layout update succeeds and native capture is acknowledged
- [x] Start ExactFollowSegments part 001 at the input's exact even dimensions and retain the same recording handle, audio routing, and encoder selection across stable transitions to parts 002 and 003
- [x] Keep every ExactFollowSegments part unpadded and identical to the input dimensions, failing closed on odd dimensions that cannot be represented exactly by the H.264 4:2:0 path
- [x] Do not publish the previous part until the next part muxes its first video packet, and preserve the previous stop result when the next part cannot start
- [ ] Validate every fMP4 part produced by actual VRChat sender resolution, orientation, or pixel-format changes with the frozen payload's oracle and a real GPU
