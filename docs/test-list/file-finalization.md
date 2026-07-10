# File finalization test list / ファイル確定テストリスト

## 日本語

基本設計書 v0.3 §11.6、§18.4、§24のflush、atomic rename、再検証、recovery規則をRed–Green–Refactorで実装します。

- [x] 最終rename成功前にSavedを発行しない
- [x] 最終fileの再検証成功後だけSavedを発行する
- [x] MP4検証失敗時はSavedを発行せずrecoveryへ隔離する
- [x] temporary fileとfinal fileは同一directory／volumeを使用する
- [x] 同名final fileがある場合は上書きせず開始前に連番を確保する

## English

The flush, atomic-rename, reopen-validation, and recovery rules from Basic Design v0.3 §§11.6, 18.4, and 24 are implemented with Red–Green–Refactor.

- [x] Do not publish Saved before the final rename succeeds
- [x] Publish Saved only after the final file passes reopen validation
- [x] Publish no Saved event and quarantine to recovery when MP4 validation fails
- [x] Keep temporary and final files on the same directory and volume
- [x] Reserve a numbered name before Start instead of overwriting an existing final file
