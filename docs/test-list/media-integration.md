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
- [x] versioned native Spout sourceからsender snapshotとframe observationをUTF-8 buffer sizing付きで取得する
- [x] managed P/Invoke sourceを既存の3 frame／300 ms stability gatewayへ接続する
- [x] native pollのcaller cancellationとDisposeを短いpoll sliceで収束させる
- [x] forced encoder終了時はpendingを保持しSavedを発行しない
- [x] native音声device eventを入力別の型付きwarning／recoveryへ変換し、pending Stopを完了せずobserver障害でも録画を中断しない
- [x] native callbackを診断I/O／presentation処理から分離し、停止時はproducer停止後にqueueをdrainする
- [x] first packet確定後のmedia profileとgraceful stop後のnative最終統計を録画結果を変えず診断へ発行する
- [x] A/V packetのPTS/DTSを保持してstream別DTSを検証し、1秒以降のkeyframeまたは2秒上限でfragmentを確定する
- [x] graceful finishだけが最終fragment、trailer、file flushを順に実行し、Abortではtrailerを書かない

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
- [x] Read sender snapshots and frame observations from a versioned native Spout source with UTF-8 buffer sizing
- [x] Connect the managed P/Invoke source to the existing three-frame/300-ms stability gateway
- [x] Converge caller cancellation and Dispose around native polling through short poll slices
- [x] Preserve pending output and suppress Saved after forced encoder termination
- [x] Translate native audio-device events into input-specific typed warnings/recoveries without completing a pending Stop or interrupting recording when observers fail
- [x] Isolate native callbacks from diagnostics I/O/presentation work and drain queues after stopping producers
- [x] Publish the committed media profile and final native statistics without changing recording start/stop outcomes
- [x] Preserve A/V packet PTS/DTS, validate DTS per stream, and close fragments at a keyframe after one second or at the hard two-second limit
- [x] Run final-fragment, trailer, and file flush in order only for graceful finish, never writing a trailer after abort
