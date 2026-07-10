# Storage integration test list / Storage結合テストリスト

## 日本語

- [x] 同一directoryのpending fileをflush・renameし、再open検証後だけSavedを発行する
- [x] final名collision時に既存fileを保持し、`_002`へ保存する
- [x] invalid finalを実在するrecovery pathへ隔離し、Savedを発行しない
- [x] rename失敗時のpending fileを削除せずrecoveryへ移す
- [ ] 起動時にstaleな`.recording.mp4`を検出して回復する

## English

- [x] Flush and rename a same-directory pending file, then publish Saved only after reopening it
- [x] Preserve an existing final file and save a collision as `_002`
- [x] Move an invalid final file to a real recovery path without publishing Saved
- [x] Preserve and recover a pending file after rename failure
- [ ] Discover and recover stale `.recording.mp4` files at startup
