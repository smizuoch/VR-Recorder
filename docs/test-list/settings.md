# Settings test list / 設定テストリスト

## 日本語

- [x] schema v1の設計既定値をmissing documentから返す
- [x] 設定をcamelCase／string enumの決定論的JSONとしてatomic保存する
- [x] frame rate、timer、enum、gain、transform、loopback OSC endpointをload時に検証する
- [x] 破損documentをbackupへ退避して既定値で起動する
- [ ] 旧schemaを起動時にmigrationする
- [ ] JSON Schemaを同梱し保存documentを検証する
- [x] `%LocalAppData%\VR-Recorder\settings.json`をWindows Known Folder経由で解決する

## English

- [x] Return the schema-v1 design defaults for a missing document
- [x] Atomically persist deterministic camelCase JSON with string enums
- [x] Validate frame rate, timers, enums, gains, transforms, and loopback OSC endpoints on load
- [x] Move a corrupt document to backup and start with defaults
- [ ] Migrate older schemas at startup
- [ ] Ship a JSON Schema and validate saved documents
- [x] Resolve `%LocalAppData%\VR-Recorder\settings.json` through the Windows Known Folder API
