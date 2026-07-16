# Settings test list / 設定テストリスト

## 日本語

- [x] schema v2の設計既定値をmissing documentから返す
- [x] 設定をcamelCase／string enumの決定論的JSONとしてatomic保存する
- [x] frame rate、timer、enum、gain、transform、loopback OSC endpointをload時に検証する
- [x] 破損documentをbackupへ退避して既定値で起動する
- [x] schema v1のglobal VR配置を無損失にschema v2 fallbackへ起動時migrationする
- [x] schema v1／v2のJSON Schemaを別identityのembedded resource／offline fileとして同梱する
- [ ] 承認済みのJSON Schema準拠validatorで保存documentを検証する
- [x] `%LocalAppData%\VR-Recorder\settings.json`をWindows Known Folder経由で解決する
- [x] desktop UIからtimer／FPS／解像度方針／encoder／qualityを保存し、次回RECで再読込する
- [x] 保存先変更時は認証済みLegal Bundleのミラー成功後だけ新しいpathを保存する
- [x] audio routing／gainの明示変更だけを保存し、未変更のendpoint IDと同時更新値を保持する
- [x] desktop／microphone endpoint IDをlocalized／accessibleなeditable selectorへ投影し、明示変更だけを三者マージして次回録画へ反映する
- [x] Windows Core Audioからactive render／capture endpointのfriendly nameとopaque IDを列挙し、非activeな保存済み選択も失わずselectorへ統合する
- [x] System／English／Japaneseをoptional schema v2設定として三者マージし、起動時と保存直後にresource dictionaryへ適用する（CLI locale overrideを優先）
- [x] VR hand／Wrist Dock・World PinとOSC discovery／loopback fallbackをlocalized／accessible controlへ投影し、nested同時更新を保持して個別保存する
- [x] tracking system／HMD model／controller input profile／左右のexact keyごとにVR配置を最大64件保存し、未知profileでは移行済みglobal値へfallbackする

## English

- [x] Return the schema-v2 design defaults for a missing document
- [x] Atomically persist deterministic camelCase JSON with string enums
- [x] Validate frame rate, timers, enums, gains, transforms, and loopback OSC endpoints on load
- [x] Move a corrupt document to backup and start with defaults
- [x] Migrate the schema-v1 global VR placement losslessly to the schema-v2 fallback at startup
- [x] Ship the schema-v1 and schema-v2 JSON Schemas as separately identified embedded resources and offline files
- [ ] Validate saved documents with an approved conforming JSON Schema validator
- [x] Resolve `%LocalAppData%\VR-Recorder\settings.json` through the Windows Known Folder API
- [x] Save timer, FPS, resolution policy, encoder, and quality from the desktop UI and reload them for the next REC
- [x] Persist a changed output path only after mirroring the authenticated Legal Bundle successfully
- [x] Save only explicit audio-routing and gain edits while preserving endpoint IDs and concurrent unedited values
- [x] Project desktop/microphone endpoint IDs into localized accessible editable selectors and three-way merge only explicit changes into the next recording
- [x] Enumerate active Windows Core Audio render/capture endpoints by friendly name and opaque ID while retaining inactive persisted selections in the selectors
- [x] Three-way merge System/English/Japanese as an optional schema-v2 setting and apply it to resource dictionaries at startup and immediately after saving, with the CLI locale override taking precedence
- [x] Project VR hand/Wrist Dock/World Pin and OSC discovery/loopback fallback into localized accessible controls and persist each explicit change while preserving concurrent nested updates
- [x] Persist at most 64 VR placements by exact tracking-system/HMD-model/controller-input-profile/hand keys and fall back to the migrated global value for unknown profiles
