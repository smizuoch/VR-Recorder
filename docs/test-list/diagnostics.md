# Diagnostics test list / 診断テストリスト

## 日本語

- [x] 診断bundleはユーザーの明示export操作後だけ生成する
- [x] JSONLは既知eventと許可fieldだけを再投影し、未知event／fieldを除外する
- [x] bundleへ映像、音声、認証情報、file path、ユーザー名、world名、OSC avatar値を含めない
- [x] 入力logのsymlink／reparse pointを拒否し、失敗時にpartial ZIPを残さない
- [x] ZIP entry名、順序、時刻、UTF-8 JSON Linesを決定論的に生成する
- [x] desktop UIからlocalizedかつaccessibleな明示操作で保存先を選択する
- [x] export成功、取消、失敗を別状態として扱い、以前の成功pathを失敗後に再表示しない
- [x] audio入力の警告／状態を許可済みinput・kind・frame位置・再探索時間・failure typeだけへ再投影し、endpoint情報とmessageを除外する
- [ ] app version、OS、GPU、driver、encoder、映像geometry／FPSを構造化eventへ記録する
- [ ] frame drop／duplicate、encode latency、A/V sync、audio underrun／overrunを記録する
- [ ] OSC capability／書込み結果とfinalization失敗／隔離をprivacy-safeに記録する
- [ ] Windows UI Automationでdiagnostics windowとSave dialogのkeyboard／screen-reader操作を検証する

## English

- [x] Generate a diagnostic bundle only after an explicit user export action
- [x] Reproject only known JSONL events and approved fields, excluding unknown events and fields
- [x] Exclude video, audio, credentials, file paths, user names, world names, and OSC avatar values
- [x] Reject linked/reparse-point log inputs and leave no partial ZIP after failure
- [x] Generate deterministic ZIP entry names, order, timestamps, and UTF-8 JSON Lines
- [x] Choose the destination through an explicit localized and accessible desktop action
- [x] Keep export success, cancellation, and failure distinct without reusing an earlier successful path after failure
- [x] Reproject audio-input warnings/statuses to allowlisted input, kind, frame position, rediscovery budget, and failure type fields while excluding endpoint data and messages
- [ ] Record app version, OS, GPU, driver, encoder, video geometry, and FPS as structured events
- [ ] Record frame drops/duplicates, encode latency, A/V sync, and audio underruns/overruns
- [ ] Record OSC capability/write outcomes and finalization failure/quarantine without private data
- [ ] Validate keyboard and screen-reader operation of the diagnostics window and Save dialog with Windows UI Automation
