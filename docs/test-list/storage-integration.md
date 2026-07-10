# Storage integration test list / Storage結合テストリスト

## 日本語

- [x] 同一directoryのpending fileをflush・renameし、再open検証後だけSavedを発行する
- [x] 開始前のfinal名collision時に既存fileを保持し、`_002`以降をatomicに予約する
- [x] concurrent reservationでもtemporary/final名を同じ連番で一意に確保する
- [x] 予約済みfinal名へ後発collisionが起きた場合は再採番せずpendingをrecoveryへ移す
- [x] invalid finalを実在するrecovery pathへ隔離し、Savedを発行しない
- [x] rename失敗時のpending fileを削除せずrecoveryへ移す
- [x] 起動時にstaleな`.recording.mp4`を検出して回復する
- [x] 選択したoutput filesystemの空き容量を実測する
- [x] 既定保存先をWindows Downloads Known Folder IDで解決する
- [x] CameraLeaseをatomicかつ所有者照合付きで永続化し、破損証拠を保持する

## English

- [x] Flush and rename a same-directory pending file, then publish Saved only after reopening it
- [x] Preserve an existing final file and atomically reserve `_002` or later before Start
- [x] Keep paired temporary/final ordinals unique under concurrent reservation
- [x] Recover the pending file without renumbering after a late collision on the reserved final name
- [x] Move an invalid final file to a real recovery path without publishing Saved
- [x] Preserve and recover a pending file after rename failure
- [x] Discover and recover stale `.recording.mp4` files at startup
- [x] Measure free space on the selected output filesystem
- [x] Resolve the default output through the Windows Downloads Known Folder ID
- [x] Persist CameraLease atomically with owner matching while retaining corrupt evidence
