# Media integration test list / Media結合テストリスト

## 日本語

- [x] host FFmpegで生成した3秒H.264/AAC fMP4をprobe・rename後だけSavedにする
- [x] native first-packet callback後だけStartAsyncを完了する
- [x] Stopがtrailer/flush/close後のpending recordingを返す
- [x] duplicate Stopがstop→finalize→probe→Saved全体で同一Taskを返す
- [x] first packet後のnative faultをruntime fault sinkへ通知する
- [x] OSCでStreamingを変更する前のsender一覧をbaselineとして保持する
- [x] Start後の新規／更新senderは同一寸法の3 fresh frameが300 ms以上続いたときだけ安定とする
- [x] 選択したsender ID、adapter LUID、pixel format、source FPSをencoder probeとrecording planへ同一値で渡す
- [x] rich video source contractをplaceholderに戻さずnative session configへ渡す
- [ ] versioned native Spout sourceかsender snapshotとframe observationをUTF-8 buffer sizing付きで取得する
- [ ] managed P/Invoke sourceを既存の3 frame／300 ms stability gatewayへ接続する
- [ ] native pollのcaller cancellationとDisposeを短いpoll sliceで収束させる
- [ ] forced encoder終了時はpendingを保持しSavedを発行しない

## English

- [x] Publish Saved only after probing and renaming a 3-second H.264/AAC fMP4 made by host FFmpeg
- [x] Complete StartAsync only after the native first-packet callback
- [x] Return the pending recording only after Stop writes the trailer, flushes, and closes
- [x] Return the same Task for duplicate Stop across stop→finalize→probe→Saved
- [x] Report native faults after the first packet to the runtime fault sink
- [x] Capture the sender-list baseline before changing Streaming through OSC
- [x] Treat a new or updated post-start sender as stable only after three same-size fresh frames spanning at least 300 ms
- [x] Carry the selected sender ID, adapter LUID, pixel format, and source FPS unchanged into the encoder probe and recording plan
- [x] Carry the rich video-source contract into the native session config without reverting to placeholders
- [ ] Read sender snapshots and frame observations from a versioned native Spout source with UTF-8 buffer sizing
- [ ] Connect the managed P/Invoke source to the existing three-frame/300-ms stability gateway
- [ ] Converge caller cancellation and Dispose around native polling through short poll slices
- [ ] Preserve pending output and suppress Saved after forced encoder termination
