# Native録画パイプライン失敗境界マトリクス

更新日: 2026-07-13

## 目的

native録画パイプラインの失敗テストを、見つかった不具合ごとに場当たり的に追加するのではなく、外部APIの所有権契約と内部state machineから体系的に生成する。この文書は実装済みの自動テストと、実backend導入時にRedから開始する残課題を分離する。

## 根拠とする外部契約

- FFmpeg send/receive API: `avcodec_send_frame(NULL)`でdrainへ入り、`avcodec_receive_packet()`を`AVERROR_EOF`まで呼ぶ。drain後の追加frameは拒否される。<https://www.ffmpeg.org/doxygen/trunk/group__lavc__encdec.html>
- FFmpeg muxing example: trailerはcodec contextや出力fileを閉じる前に書く。<https://ffmpeg.org/doxygen/trunk/doc_2examples_2muxing_8c-example.html>
- FFmpeg muxing contract: header後に実stream time baseが変わり得るため、packet timestampはheader後の`AVStream.time_base`へrescaleする。A/V interleaveには`av_interleaved_write_frame`を使い、同一contextで`av_write_frame`と混在させない。reference-counted packetの所有権は成功／失敗のどちらでもこの呼出しへ移るため、retryしてはならない。<https://ffmpeg.org/doxygen/trunk/group__lavf__encoding.html>
- FFmpeg MOV/MP4 fragmentation: 複数fragment条件はどれかを満たすとcutし、`min_frag_duration`は全条件の下限となる。`frag_custom`の手動cutは`av_write_frame(ctx, NULL)`であり、今回は使用しない。<https://ffmpeg.org/ffmpeg-formats.html#Fragmentation>
- FFmpeg AAC priming: audio encoderはoutput PTSから`initial_padding`を減算するため、先頭packetは負のPTS／DTSになり得る。pinned 8.1.2のfragmented MOVは`delay_moov`なしだと既定edit listを無効化して負timestampをzero shiftするため、`delay_moov`、`use_editlist=1`、`avoid_negative_ts=disabled`を明示する。<https://www.ffmpeg.org/doxygen/trunk/encode_8c_source.html> <https://git.ffmpeg.org/gitweb/ffmpeg.git/blob/n8.1.2:/libavformat/movenc.c>
- FFmpeg native AAC: pinned 8.1.2の`aac` encoderはFLTP、delay、small-last-frameを公開し、固定frame未満の末尾入力をzero paddingせず受理する。<https://github.com/FFmpeg/FFmpeg/blob/n8.1.2/libavcodec/aacenc.c> <https://github.com/FFmpeg/FFmpeg/blob/n8.1.2/libavcodec/encode.c>
- FFmpeg codec parameters／MOV bitrate: `avcodec_parameters_from_context`はopened contextの`bit_rate`／`frame_size`／`initial_padding`等をcopyし、fragmented MOVはheader時にpacket平均を持たないためcodec-parameter bitrateを`esds`／`btrt` metadataのfallbackに使う。<https://github.com/FFmpeg/FFmpeg/blob/n8.1.2/libavcodec/codec_par.c#L138-L195> <https://github.com/FFmpeg/FFmpeg/blob/n8.1.2/libavformat/movenc.c#L717-L800>
- FFmpeg sample conversion／FIFO: `swr_get_out_samples`は次の`convert`出力上限であり、終了時はNULL input／0 input countでflushする。planar sampleはchannel別planeとして`AVAudioFifo`へwrite/readする。<https://github.com/FFmpeg/FFmpeg/blob/n8.1.2/libswresample/swresample.h#L296-L315> <https://github.com/FFmpeg/FFmpeg/blob/n8.1.2/libswresample/swresample.h#L474-L489> <https://github.com/FFmpeg/FFmpeg/blob/n8.1.2/libswresample/swresample.c#L887-L906> <https://github.com/FFmpeg/FFmpeg/blob/n8.1.2/libavutil/audio_fifo.c#L119-L142> <https://github.com/FFmpeg/FFmpeg/blob/n8.1.2/libavutil/samplefmt.c#L121-L150>
- FFmpeg SkipSamples: layoutはu32leのstart/end sample数と2 reason byteの計10 byteである。ただしpinned 8.1.2のMOV muxerは`AV_PKT_DATA_SKIP_SAMPLES`を参照しないため、side dataを渡すだけでは末尾paddingを表現できない。明示変換を実装するまでは実MOV Portでfail-closed拒否する。<https://ffmpeg.org/doxygen/trunk/packet_8h_source.html> <https://git.ffmpeg.org/gitweb/ffmpeg.git/blob/n8.1.2:/libavformat/movenc.c>
- FFmpeg H.264 MOV handling: avcC extradataならlength-prefixed packetをそのまま書き、Annex BならMOV内部で変換する。ただしpinned 8.1.2はavcC生成errorを呼出し元へ返さない箇所があるため、初期実Portは検証済みAVCCだけを受理し、Annex B変換を別gateにする。<https://git.ffmpeg.org/gitweb/ffmpeg.git/blob/n8.1.2:/libavformat/movenc.c>
- FFmpeg Media Foundation H.264: `h264_mf`は`MF_MT_MPEG_SEQUENCE_HEADER` blobをextradataへcopyするが、Microsoftはこの属性をstart-code付きsequence headerと定義する。機種によってopen後もextradataが得られずfirst frameへprependされるため、Annex B SPS/PPS→avcC、AU length-prefix変換とlate-extradata state machineを別gateにする。<https://github.com/FFmpeg/FFmpeg/blob/n8.1.2/libavcodec/mfenc.c#L200-L218> <https://github.com/FFmpeg/FFmpeg/blob/n8.1.2/libavcodec/mfenc.c#L1284-L1305> <https://learn.microsoft.com/windows/desktop/medfound/mf-mt-mpeg-sequence-header-attribute>
- Media Foundation media sinks: archive sinkは最後のsample後にFinalizeし、未Finalizeのfileは不正な構造になり得る。<https://learn.microsoft.com/en-us/windows/win32/medfound/media-sinks>
- DXGI keyed mutex: Acquire成功後はReleaseが必須。`WAIT_TIMEOUT`は再試行可能だが、`WAIT_ABANDONED`はsurfaceとmutexの再作成が必要。<https://learn.microsoft.com/en-us/windows/win32/api/dxgi/nf-dxgi-idxgikeyedmutex-acquiresync>
- WASAPI capture: GetBufferとReleaseBufferは同一threadで交互に呼び、packet全体または0 frameを解放する。<https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-iaudiocaptureclient-releasebuffer>
- C++ thread join: `join()`は対象threadの完了までblockし、自分自身のthreadをjoinする要求は`resource_deadlock_would_occur`になり得る。callback stackではpeer joinを行わず、別のcleanup ownerへ移す。<https://eel.is/c++draft/thread.thread.member>
- C++ thread construction: 新しいthreadを開始できない場合は`std::system_error`を送出し、resource不足またはprocessのthread上限超過は`resource_unavailable_try_again`となる。production factoryではこの失敗を`INTERNAL_ERROR`、allocation失敗を`OUT_OF_MEMORY`へ変換する。<https://eel.is/c++draft/thread.thread.constr>
- C++ condition variable: predicate付きwaitはmutexを解放してblockし、通知後に再取得してpredicateを再評価する。request publicationとcleanup completionの待機はこの契約に従う。<https://eel.is/c++draft/thread.condition>
- C++ atomic wait/notify: atomic waitはpollingせず値の変更を待ち、`notify_all`はeligible waiterを解除する。Start／Join phaseのpublication後に通知し、wait側はphaseを再読込する。<https://eel.is/c++draft/atomics.wait>

## テスト生成規則

各公開境界について、次の組合せを最低1回ずつ検証する。

1. 呼出し前のstate: 未開始、active、stop要求中、finished、aborted。
2. 失敗位置: 入力検証、確保、capture、processing、encode、packet batch、mux、fragment、trailer、flush、callback。
3. 競合相手: Start、Write/Poll/Mix、RequestStop、Finish、Join、Abort。
4. 観測対象: 戻り値、出力初期化、下流call回数、Abort回数、resource release、統計、event、後続call拒否。
5. overflow/異常値: 0、最大値、加算overflow、NaN/Infinity、非単調timestamp、未知enum、null handle。

競合テストはcondition variable、promise、またはchild processを使い、通常の` sleep`で順序を推測しない。失敗後は「戻り値が失敗」だけでなく、下流mutationがないこと、Abortがexactly onceであること、後続操作がterminal拒否されることまで確認する。

同期callbackごとに再入契約を明示する。coordinator observerはcoordinatorおよび経由時のShared state lock外で呼び、batch順序用submit／operation gateは保持する。observerからcoordinator／Shared Abortを許可し、recursive Submit／Finishは契約外とする。production drift callbackはshared-finalizationのreturn後かつmonitor state lock外で呼び、許可するAbort／statistics readbackをwatchdog付きRedで固定する。

## 2026-07-13実装済み

| 境界 | 注入した失敗・競合 | 固定した不変条件 | 主なテスト |
|---|---|---|---|
| capture normalizer/timeline/mixer/session | invalid format、Abort、inactive | 失敗時に前回のmetadata/span/readを返さない | `audio_capture_normalizer_tests.cpp`, `audio_capture_timeline_tests.cpp`, `audio_mix_coordinator_tests.cpp`, `audio_capture_session_tests.cpp` |
| audio encoding pump | frame重複、range overflow、NaN PCM | encoderを呼ばずCaptureFailed、成功windowだけを連続計上 | `audio_encoding_pump_tests.cpp` |
| audio capture runner/pump | malformed packet、Start中Abort | sourceとtimelineを解放し、Start完了後に復活したsourceも再Abort | `audio_capture_pump_tests.cpp` |
| audio/video encoding worker | graceful Finish中Abort、in-flight WriteがAbort後に失敗復帰 | AbortをStopped／Faultedより優先し、flush packet／latency統計、first-packet／fault eventをcommitしない | `audio_encoding_worker_tests.cpp`, `video_encoding_worker_tests.cpp` |
| audio/video muxing sink | encoder／final completion failure、mixed-stream batch、Encode中Abort、Write／Finish／Abort競合 | zero-packet bufferingではmuxを呼ばず、Abort要求をmuxへ先に通知する。WriteとFinishを直列化し、Finish中Abortではcompletion return後もAbortをlogical winnerとして成功packet数を0にする。失敗通知／completion通知／encoder Abortをexactly onceにし、terminal再入力を拒否する | `muxing_audio_encoder_sink_tests.cpp`, `muxing_video_encoder_sink_tests.cpp` |
| common mux batch submission | batch後半のinvalid packet、N回目write failure、A/V同時batch、in-flight drift callback中Finish、callback内sink Abort、未開始submission／completion、重複Start／completion、完了stream packet、invalid producer／stream | sinkは内部finalization concrete型を受け取れず、drift-aware `MediaMuxPipeline` Portへ同期batchを1回だけ渡す。全件preflight後にbatchを非interleaveで書き、失敗batchのprefixをdrift統計へcommitせず、callback Abort後のtail観測とlogical packet countを抑止する。active／pending muxのprotocol違反は同一call内でAbortし、後続completionで成功へ戻さない | `fragmented_mp4_mux_coordinator_tests.cpp`, `shared_mux_finalization_session_tests.cpp`, `media_mux_pipeline_tests.cpp` |
| video processing sink | null native handle、Process中Abort/Finish | processor/encoderへの不正入力を拒否し、終端後に下流Writeしない | `video_processing_encoder_sink_tests.cpp` |
| video clock worker | WaitNext中Abort後のTick | Abort後のtickをencodeせず、fault/first-packetも出さない | `video_encoding_worker_tests.cpp` |
| Spout capture pump | Descriptor検証中Abort | Abortとscheduler Pushを線形化し、Abort後frameを投入しない | `spout_capture_pump_tests.cpp` |
| audio pipeline session lifecycle | capture Start成功／失敗、SetRouting、encoding RequestStop／Join中Abort | StartをNotStarted／Starting／Completed、終端をOpen／AbortRequested／Completedで追跡する。各blocking call後にwinnerを再検査し、Abort先行時はINVALID_STATE／Abortedへ収束する。cleanupはStart rollback完了を待ち、通常Joinと競合してもsource／sinkの物理Abort ownerを必ず一つ確保してからJoin完了を待つ | `audio_pipeline_session_tests.cpp` |
| video pipeline session lifecycle | capture／encoding Start、encoding RequestStop／Join、capture JoinのSenderLost／Failed中Abort | Start rollback完了までcleanupを待つ。Open／AbortRequested／Completedの単一terminal winnerでAbortを優先し、通常Joinとcleanupのどちらかがworkerの物理Abortを回収する。capture Abortをexactly onceにし、SenderLost／CaptureFailedのFaulted通知を抑止する | `video_pipeline_session_tests.cpp` |
| media recording session | 同時RequestStop/Join、RequestAbort publication中のcleanup、Start成功flagとcleanup ownershipの競合 | streamごとのgraceful Stop/Joinを1回だけ実行し、結果/eventを単一commitする。StartをNotStarted／Starting／Completedで追跡し、cleanupはStart内rollback完了まで待つ。cleanupが取得したstarted streamにはlogical RequestAbortを再送してからJoinし、publication競合でworkerを取り残したり早期復帰したりしない | `media_recording_session_tests.cpp` |
| fragmented MP4 header/finalization | invalid descriptor、header/Trailer/Flush各失敗、header／Trailer中Abort | header中Abort後にStart成功へ復活しない。AbortがFinishより先ならtrailerは0回。trailer完了時の再確認でAbortを観測した場合はFlushFileを0回にするが、既に開始したtrailer／flushはrollbackしない。Abort exactly onceで以後terminal拒否する | `fragmented_mp4_mux_coordinator_tests.cpp`, `media_mux_pipeline_tests.cpp` |
| encoded packet ownership | encoder元buffer変更／破棄、empty payload | packetがbyte列を所有し、empty payloadをmux mutation前に拒否 | `fragmented_mp4_mux_coordinator_tests.cpp` |
| pinned FFmpeg SDK admission | root未指定、header／DLL欠落、完全version不一致、GPL configure flag、未知external library／bitstream filter | portable buildへ暗黙混入させず、Windows opt-in時だけ公式8.1.2のsource identity、4 shared library、LGPL-only configureと暗黙select componentの完全evidenceをconfigure時にfail-closed照合する | `pinned_ffmpeg_contract.cmake` |
| FFmpeg encode/drain portable state machine | send／receiveの`EAGAIN`、0／複数packet、予期しないEOF、drain、部分batch後error、各packet ownership確保位置のOOM、Abort競合 | 同じ`SendPreparedFrame`操作を再試行し、drain受理後はEOFまでreceive、成功packetを全経路でunref、失敗batchを返さずterminal化 | `ffmpeg_encoder_state_machine_tests.cpp` |
| 実libavcodec AAC encoder Port | null／未open context、runtime version不一致、Create／frame ref OOM、実send backpressure、borrow中receive、負priming／unknown timestamp、drain EOF、Abort | open済みcontextを全経路で一意所有し、`EAGAIN`中は同一`AVFrame`参照を保持、実`AVPacket`をunrefまでborrow、timestampをcanonical microsecondsへrescaleし、8.1.2以外を拒否する | `ffmpeg_libavcodec_encoder_port_tests.cpp` |
| 実AAC context／frame factory | config／3-library runtime不一致、context／layout／descriptor／swr／FIFO／frame各OOM、packed→planar変換、0／1／1023／1024／1025 sample、1秒入力、2回目変換失敗、最終chunk NaN、flush／drain、N回目途中失敗、Encode／Finish／Abort競合、drain後Abort、rescale overflow | native `aac`のopen後契約とowned ASC／exact 192000 bit/sを固定する。全入力をfinite／timing preflightしてから最大1024 stereo frameずつFLTP FIFOへ変換し、active中は1024 sample、Finish時だけsmall lastを送る。Abort要求はoperation mutexより先にpublishし、chunk／codec／drain後commitでlogical winnerとして再確認する。失敗batchを部分公開せずterminal化する | `ffmpeg_aac_packet_encoder_tests.cpp` |
| AAC priming epoch／packet timing | 負の先頭audio PTS／DTS、padding下限超過、timestamp＋duration overflow | `frame_size`／`initial_padding_samples`をheader descriptorへ保持し、bounded negative audio epochをmuxへ改変せず渡す。A/V診断だけはpresentation 0未満を無視する | `ffmpeg_encoder_state_machine_tests.cpp`, `fragmented_mp4_mux_coordinator_tests.cpp`, `media_mux_pipeline_tests.cpp` |
| encoded packet side data | SkipSamples、side-data-only、unknown／wrong-size／duplicate／video side data | AAC上のexact 10-byte SkipSamplesだけをtyped・ownedで実Port境界まで保持し、表現不能なside dataを黙ってdropしない。pinned MOVで未変換のSkipSamplesは成功扱いにしない | `ffmpeg_encoder_state_machine_tests.cpp`, `fragmented_mp4_mux_coordinator_tests.cpp`, `ffmpeg_fragmented_mp4_muxer_tests.cpp` |
| FFmpeg mux portable time-base seam | header失敗／stream別readback失敗、不正actual time base、fake rescale後sentinel／end overflow／duration 0／DTS衝突、interleaved write失敗 | header成功後のvideo／audio実time baseを値保存し、canonical packetを変更せずpostconditionをwrite前に検査、finish後Abortをno-op、失敗時Abort exactly once。実rescale算術とAPI所有権は別の実Port testで固定する | `ffmpeg_fragmented_mp4_muxer_tests.cpp` |
| 実libavformat fragmented-MP4 Port | runtime 3-library version drift、open／header／packet allocation、malformed avcC／ASC／AVCC／ADTS、AAC bitrate不一致、SkipSamples、header／interleaved write／trailer／flush／close、実`/dev/full`、Abort／destructor | 公式8.1.2だけを受理し、post-header time baseと実`av_packet_rescale_ts`を使用する。AAC descriptorのexact 192000 bit/sをcodec parametersへ設定してheader後に読み戻し、実zero-packet MOVの`esds`／`btrt`へ保持する。AVCCを完全copyしたrefcounted packetは成功／失敗とも1回だけ消費し、負AAC primingを`elst media_time=1024`へ保持する。全失敗でAVIOをexactly once closeし、`.recording.mp4`を削除／rename／publishしない | `ffmpeg_libavformat_fragmented_mp4_muxer_port_tests.cpp`, `ffmpeg_aac_to_fragmented_mp4_integration_tests.cpp` |
| mux observer／A/V drift callback再入 | coordinator observer内coordinator／Shared Abort、drift callback内の統計readback→pipeline／sink Abort | coordinator／Shared state lockを外し、submit／operation gateを保持したままobserver内coordinator／Shared Abortを許可する。production drift callbackはshared-finalizationのreturn後かつmonitor state lock外で、pipeline operation gateを保持したstatistics readback→pipeline／sink Abortを許可する。Abortしたin-flight Submitを`Written`にせずtail観測を止める。recursive Submit／Finishは契約外 | `fragmented_mp4_mux_coordinator_tests.cpp`, `shared_mux_finalization_session_tests.cpp`, `media_mux_pipeline_tests.cpp` |
| full media graph callback Abort | audio callback→video peer待機、video callback→audio peer待機 | callback stackではmuxと両workerへnon-blocking `RequestAbort`だけを伝播し、prestarted cleanup workerが物理Abort／Joinを回収する。外部ownerの`JoinAfterAbort`は全cleanup完了を待ち、Stopped／Faulted／trailer／flushを発生させない | `full_media_graph_abort_tests.cpp` |
| mux/stream start ordering | header失敗、header開始中Abort | mux header→video→audioの順、header未成立時stream Start 0、Abort exactly once | `media_recording_session_tests.cpp` |
| initial H.264 reorder policy | vendor既定B-frame | `maximum_b_frame_count=0`を明示し、現行video非負DTS契約へreorderを混入させない | `video_encoder_config_tests.cpp` |
| C ABI callback lifetime | callback内のsame-session Abort、callback外のAbort／destroy | same-session Abortだけをcallback-safeとし、logical request後に即時復帰する。request_stop／destroyを含む他session APIはcallback内禁止とheaderで固定し、外部Abort／destroyはcallback quiescenceとworker cleanupを待つ | `abi_contract_tests.cpp`, `vrrecorder_native.h` |
| production backend cleanup／stop worker | cleanup／stop thread生成OOM・internal mapping・joinable thread欠落、pipeline Start／RequestStop中Abort、同時stop、stop worker内Abort | injectable thread factoryで生成失敗を戻り値へ変換し、cleanup worker不成立時はpipelineを開始しない。blocking call後にAbort winnerを再検査し、stop生成／pipeline失敗は1回だけcleanupして同時／後続callerへ同じ結果を返す。current threadをjoinせず別thread／destructorで回収する | `pipeline_media_backend_tests.cpp` |
| session Start milestone | Start中first packet後にStart失敗 | FIRST_VIDEO_PACKETを外へcommitせずrollback | `abi_contract_tests.cpp` |

## 実backend導入時の必須Redテスト

以下は抽象境界では代替できず、対応するproduction adapterを追加するcommitで先にRedを書く。

| 優先度 | backend | 必須ケース | 完了条件 |
|---|---|---|---|
| P0 | 実FFmpeg/MF encoder／muxer composition | production audio pumpの入力windowを1024 frameへ固定してnative AAC factoryへ接続し、zero-size／side-data-only packetと追加flagを実測する。H.264 context／D3D11 frame adapter、global header、late extradata、SPS寸法整合、Annex B→AVCC明示変換、factory／compositionを接続する | 個別libavcodec／libavformat Portやzero-packet headerだけをproduction Greenと数えず、実encoder packetから作る3秒以上のscratch fMP4でMOV edit list／exact 192 kbps metadata、decode後presentation 0／末尾sample数、ffprobe／decode／playbackを証明する。packet漏れ・二重送信なし、Finish後frame拒否、header未成立file／失敗fileをpublishしない |
| P0 | D3D11 processor | `WAIT_TIMEOUT`、`WAIT_ABANDONED`、device removed、Release failure | timeoutだけ継続、abandoned/device lossはsurface再作成またはterminal fault、取得済みresourceを必ず解放 |
| P0 | Spout2 receiver | sender消失、texture再作成、adapter LUID変化、poll中Abort | 古いsurfaceをencodeせず、receiver/handleをexactly once解放 |
| P0 | WASAPI COM seam | GetBuffer/ReleaseBuffer各device loss、timestamp error、buffer empty、同一thread確認 | Get/Release対応を崩さず、device-loss frameを確定し、recoveryまたはterminal化 |
| P1 | full media backend | video/audio片側Start失敗、片側Finish失敗、実file close失敗、first-packet後encoder fault時のpart rollover | rollback/join/event/file publicationが本マトリクスと一致する。part確定＋software次partを実装するか製品仕様を明示改訂 |

## 完了判定

抽象pipelineのfailure matrixはLinux CTestで継続検証する。release適格を名乗るには、上表のproduction adapterテスト、Windows MSVC build、Windows 10/11実行、実GPU/HMD/VRChat/SteamVR試験、90% line/branch gateを別途すべて通過させる。
