# Video geometry test list / 映像ジオメトリテストリスト

## 日本語

基本設計書 v0.3 §10.2、§10.5、§24の解像度・縦横・Contain規則をRed–Green–Refactorで実装します。

- [x] `height > width`をPortraitと判定する
- [x] `width >= height`をLandscapeと判定する
- [ ] 1921×1081を4:2:0用に1922×1082へ最大1 px padする
- [ ] 偶数寸法にはpadしない
- [ ] SingleFileFitで1920×1080へ1080×1920を歪みなくContainする

## English

The resolution, orientation, and Contain rules from Basic Design v0.3 §§10.2, 10.5, and 24 are implemented with Red–Green–Refactor.

- [x] Classify `height > width` as Portrait
- [x] Classify `width >= height` as Landscape
- [ ] Pad 1921×1081 by at most one pixel to 1922×1082 for 4:2:0
- [ ] Do not pad even dimensions
- [ ] Contain 1080×1920 without distortion in a 1920×1080 SingleFileFit canvas
