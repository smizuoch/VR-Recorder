# Settings test list / 設定テストリスト

## 日本語

- [x] schema v1の設計既定値をmissing documentから返す
- [x] 設定をcamelCase／string enumの決定論的JSONとしてatomic保存する
- [x] frame rate、timer、enum、gain、transform、loopback OSC endpointをload時に検証する
- [x] 破損documentをbackupへ退避して既定値で起動する
- [ ] 旧schemaを起動時にmigrationする
- [x] schema v1のJSON Schemaをembedded resourceとoffline fileで同梱する
- [ ] 承認済みのJSON Schema準拠validatorで保存documentを検証する
- [x] `%LocalAppData%\VR-Recorder\settings.json`をWindows Known Folder経由で解決する
- [x] desktop UIからtimer／FPS／解像度方針／encoder／qualityを保存し、次回RECで再読込する
- [x] 保存先変更時は認証済みLegal Bundleのミラー成功後だけ新しいpathを保存する
- [x] audio routing／gainの明示変更だけを保存し、未変更のendpoint IDと同時更新値を保持する
- [x] desktop／microphone endpoint IDをlocalized／accessibleなeditable selectorへ投影し、明示変更だけを三者マージして次回録画へ反映する

## English

- [x] Return the schema-v1 design defaults for a missing document
- [x] Atomically persist deterministic camelCase JSON with string enums
- [x] Validate frame rate, timers, enums, gains, transforms, and loopback OSC endpoints on load
- [x] Move a corrupt document to backup and start with defaults
- [ ] Migrate older schemas at startup
- [x] Ship the schema-v1 JSON Schema as an embedded resource and offline file
- [ ] Validate saved documents with an approved conforming JSON Schema validator
- [x] Resolve `%LocalAppData%\VR-Recorder\settings.json` through the Windows Known Folder API
- [x] Save timer, FPS, resolution policy, encoder, and quality from the desktop UI and reload them for the next REC
- [x] Persist a changed output path only after mirroring the authenticated Legal Bundle successfully
- [x] Save only explicit audio-routing and gain edits while preserving endpoint IDs and concurrent unedited values
- [x] Project desktop/microphone endpoint IDs into localized accessible editable selectors and three-way merge only explicit changes into the next recording
