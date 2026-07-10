# Storage integration test list / Storage結合テストリスト

## 日本語

- [ ] 同一directoryのpending fileをflush・renameし、再open検証後だけSavedを発行する
- [ ] final名collision時に既存fileを保持し、`_002`へ保存する
- [ ] invalid finalを実在するrecovery pathへ隔離し、Savedを発行しない
- [ ] rename失敗時のpending fileを削除せずrecoveryへ移す
- [ ] 起動時にstaleな`.recording.mp4`を検出して回復する

## English

- [ ] Flush and rename a same-directory pending file, then publish Saved only after reopening it
- [ ] Preserve an existing final file and save a collision as `_002`
- [ ] Move an invalid final file to a real recovery path without publishing Saved
- [ ] Preserve and recover a pending file after rename failure
- [ ] Discover and recover stale `.recording.mp4` files at startup
