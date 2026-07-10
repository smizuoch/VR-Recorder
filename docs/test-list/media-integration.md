# Media integration test list / Media結合テストリスト

## 日本語

- [ ] host FFmpegで生成した3秒H.264/AAC fMP4をprobe・rename後だけSavedにする
- [ ] native first-packet callback後だけStartAsyncを完了する
- [ ] Stopがtrailer/flush/close後のpending recordingを返す
- [ ] duplicate Stopがstop→finalize→probe→Saved全体で同一Taskを返す
- [ ] forced encoder終了時はpendingを保持しSavedを発行しない

## English

- [ ] Publish Saved only after probing and renaming a 3-second H.264/AAC fMP4 made by host FFmpeg
- [ ] Complete StartAsync only after the native first-packet callback
- [ ] Return the pending recording only after Stop writes the trailer, flushes, and closes
- [ ] Return the same Task for duplicate Stop across stop→finalize→probe→Saved
- [ ] Preserve pending output and suppress Saved after forced encoder termination
