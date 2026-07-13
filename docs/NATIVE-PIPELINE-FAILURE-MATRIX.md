# Native録画パイプライン失敗境界マトリクス

更新日: 2026-07-13

## 目的

native録画パイプラインの失敗テストを、見つかった不具合ごとに場当たり的に追加するのではなく、外部APIの所有権契約と内部state machineから体系的に生成する。この文書は実装済みの自動テストと、実backend導入時にRedから開始する残課題を分離する。

## 根拠とする外部契約

- FFmpeg send/receive API: `avcodec_send_frame(NULL)`でdrainへ入り、`avcodec_receive_packet()`を`AVERROR_EOF`まで呼ぶ。drain後の追加frameは拒否される。<https://www.ffmpeg.org/doxygen/trunk/group__lavc__encdec.html>
- FFmpeg muxing example: trailerはcodec contextや出力fileを閉じる前に書く。<https://ffmpeg.org/doxygen/trunk/doc_2examples_2muxing_8c-example.html>
- FFmpeg muxing contract: header後に実stream time baseが変わり得るため、packet timestampはheader後の`AVStream.time_base`へrescaleする。A/V interleaveには`av_interleaved_write_frame`を使い、同一contextで`av_write_frame`と混在させない。<https://ffmpeg.org/doxygen/trunk/group__lavf__encoding.html>
- FFmpeg MOV/MP4 fragmentation: 複数fragment条件はどれかを満たすとcutし、`min_frag_duration`は全条件の下限となる。`frag_custom`の手動cutは`av_write_frame(ctx, NULL)`であり、今回は使用しない。<https://ffmpeg.org/ffmpeg-formats.html#Fragmentation>
- FFmpeg AAC priming: audio encoderはoutput PTSから`initial_padding`を減算するため、先頭packetは負のPTS／DTSになり得る。MOV edit listは負の先頭PTSをpresentation time 0から除去する。<https://www.ffmpeg.org/doxygen/trunk/encode_8c_source.html> <https://ffmpeg.org/doxygen/trunk/movenc_8c_source.html>
- FFmpeg SkipSamples: layoutはu32leのstart/end sample数と2 reason byteの計10 byteで、MOV muxerは少なくとも10 byteある場合だけ解釈する。<https://ffmpeg.org/doxygen/trunk/packet_8h_source.html> <https://ffmpeg.org/doxygen/trunk/movenc_8c_source.html>
- Media Foundation media sinks: archive sinkは最後のsample後にFinalizeし、未Finalizeのfileは不正な構造になり得る。<https://learn.microsoft.com/en-us/windows/win32/medfound/media-sinks>
- DXGI keyed mutex: Acquire成功後はReleaseが必須。`WAIT_TIMEOUT`は再試行可能だが、`WAIT_ABANDONED`はsurfaceとmutexの再作成が必要。<https://learn.microsoft.com/en-us/windows/win32/api/dxgi/nf-dxgi-idxgikeyedmutex-acquiresync>
- WASAPI capture: GetBufferとReleaseBufferは同一threadで交互に呼び、packet全体または0 frameを解放する。<https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-iaudiocaptureclient-releasebuffer>

## テスト生成規則

各公開境界について、次の組合せを最低1回ずつ検証する。

1. 呼出し前のstate: 未開始、active、stop要求中、finished、aborted。
2. 失敗位置: 入力検証、確保、capture、processing、encode、packet batch、mux、fragment、trailer、flush、callback。
3. 競合相手: Start、Write/Poll/Mix、RequestStop、Finish、Join、Abort。
4. 観測対象: 戻り値、出力初期化、下流call回数、Abort回数、resource release、統計、event、後続call拒否。
5. overflow/異常値: 0、最大値、加算overflow、NaN/Infinity、非単調timestamp、未知enum、null handle。

競合テストはcondition variable、promise、またはchild processを使い、通常の` sleep`で順序を推測しない。失敗後は「戻り値が失敗」だけでなく、下流mutationがないこと、Abortがexactly onceであること、後続操作がterminal拒否されることまで確認する。

同期callbackへ外部実装を呼ぶ前に、state／mux／monitor mutexを解放する。callback内Abortや統計readbackは再入可能とし、watchdog付きRedで自己deadlockがないことを固定する。

## 2026-07-13実装済み

| 境界 | 注入した失敗・競合 | 固定した不変条件 | 主なテスト |
|---|---|---|---|
| capture normalizer/timeline/mixer/session | invalid format、Abort、inactive | 失敗時に前回のmetadata/span/readを返さない | `audio_capture_normalizer_tests.cpp`, `audio_capture_timeline_tests.cpp`, `audio_mix_coordinator_tests.cpp`, `audio_capture_session_tests.cpp` |
| audio encoding pump | frame重複、range overflow、NaN PCM | encoderを呼ばずCaptureFailed、成功windowだけを連続計上 | `audio_encoding_pump_tests.cpp` |
| audio capture runner/pump | malformed packet、Start中Abort | sourceとtimelineを解放し、Start完了後に復活したsourceも再Abort | `audio_capture_pump_tests.cpp` |
| audio encoding worker | graceful Finish中Abort | AbortをStoppedより優先し、flush packetを統計へ加えない | `audio_encoding_worker_tests.cpp` |
| audio/video muxing sink | encoder failure、mixed-stream batch、Finish後Write/Finish | batch mutation前に拒否、両側Abort、terminal再入力拒否 | `muxing_audio_encoder_sink_tests.cpp`, `muxing_video_encoder_sink_tests.cpp` |
| video processing sink | null native handle、Process中Abort/Finish | processor/encoderへの不正入力を拒否し、終端後に下流Writeしない | `video_processing_encoder_sink_tests.cpp` |
| video clock worker | WaitNext中Abort後のTick | Abort後のtickをencodeせず、fault/first-packetも出さない | `video_encoding_worker_tests.cpp` |
| Spout capture pump | Descriptor検証中Abort | Abortとscheduler Pushを線形化し、Abort後frameを投入しない | `spout_capture_pump_tests.cpp` |
| audio/video pipeline Start | capture/encoding Start中Abort | Start成功として復活せず、開始済みworkerをrollback/join | `audio_pipeline_session_tests.cpp`, `video_pipeline_session_tests.cpp` |
| media recording session | 同時RequestStop/Join | streamごとのStop/Joinを1回だけ実行し、結果/eventを単一commit | `media_recording_session_tests.cpp` |
| fragmented MP4 header/finalization | invalid descriptor、header/Trailer/Flush各失敗 | header前packet拒否、失敗後の後続段階を実行せずAbort exactly once、以後terminal拒否 | `fragmented_mp4_mux_coordinator_tests.cpp` |
| encoded packet ownership | encoder元buffer変更／破棄、empty payload | packetがbyte列を所有し、empty payloadをmux mutation前に拒否 | `fragmented_mp4_mux_coordinator_tests.cpp` |
| pinned FFmpeg SDK admission | root未指定、header／DLL欠落、完全version不一致、GPL configure flag、未知external library | portable buildへ暗黙混入させず、Windows opt-in時だけ公式8.1.2のsource identity、4 shared library、LGPL-only evidenceをconfigure時にfail-closed照合する | `pinned_ffmpeg_contract.cmake` |
| FFmpeg encode/drain portable state machine | send／receiveの`EAGAIN`、0／複数packet、予期しないEOF、drain、部分batch後error、各packet ownership確保位置のOOM、Abort競合 | 同じ`SendPreparedFrame`操作を再試行し、drain受理後はEOFまでreceive、成功packetを全経路でunref、失敗batchを返さずterminal化。同一実`AVFrame` identityはproduction gateへ残す | `ffmpeg_encoder_state_machine_tests.cpp` |
| AAC priming epoch／packet timing | 負の先頭audio PTS／DTS、padding下限超過、timestamp＋duration overflow | `frame_size`／`initial_padding_samples`をheader descriptorへ保持し、bounded negative audio epochをmuxへ改変せず渡す。A/V診断だけはpresentation 0未満を無視する | `ffmpeg_encoder_state_machine_tests.cpp`, `fragmented_mp4_mux_coordinator_tests.cpp`, `media_mux_pipeline_tests.cpp` |
| encoded packet side data | SkipSamples、side-data-only、unknown／wrong-size／duplicate／video side data | AAC上のexact 10-byte SkipSamplesだけをtyped・ownedでmuxまで保持し、表現不能なside dataを黙ってdropしない | `ffmpeg_encoder_state_machine_tests.cpp`, `fragmented_mp4_mux_coordinator_tests.cpp`, `ffmpeg_fragmented_mp4_muxer_tests.cpp` |
| FFmpeg mux portable time-base seam | header失敗／stream別readback失敗、不正actual time base、fake rescale後sentinel／end overflow／duration 0／DTS衝突、interleaved write失敗 | header成功後のvideo／audio実time baseを値保存し、canonical packetを変更せずpostconditionをwrite前に検査、durable finish後Abortをno-op、失敗時Abort exactly once。実rescale算術はproduction gateへ残す | `ffmpeg_fragmented_mp4_muxer_tests.cpp` |
| mux observer／A/V drift callback再入 | observerまたはdrift callback内の統計readback→Abort | packet write順を直列化しつつexternal callback前にcoordinator／finalization／monitor lockを解放し、Abortをexactly onceでterminal化して自己deadlockしない | `fragmented_mp4_mux_coordinator_tests.cpp`, `media_mux_pipeline_tests.cpp` |
| mux/stream start ordering | header失敗、header開始中Abort | mux header→video→audioの順、header未成立時stream Start 0、Abort exactly once | `media_recording_session_tests.cpp` |
| initial H.264 reorder policy | vendor既定B-frame | `maximum_b_frame_count=0`を明示し、現行video非負DTS契約へreorderを混入させない | `video_encoder_config_tests.cpp` |
| C ABI callback | callback内Abort | callback quiescenceを保ちつつ自己deadlockしない | `abi_contract_tests.cpp` |
| production stop worker | Join中Abort | current threadをjoinせず、別thread/destructorでjoinを回収 | `pipeline_media_backend_tests.cpp` |
| session Start milestone | Start中first packet後にStart失敗 | FIRST_VIDEO_PACKETを外へcommitせずrollback | `abi_contract_tests.cpp` |

## 実backend導入時の必須Redテスト

以下は抽象境界では代替できず、対応するproduction adapterを追加するcommitで先にRedを書く。

| 優先度 | backend | 必須ケース | 完了条件 |
|---|---|---|---|
| P0 | 実FFmpeg/MF encoder／muxer port | 実`AVERROR` mapping、実`AVFrame` identity／二重消費、AAC frame-size/FIFO・sample変換・drain、codec time base→canonical変換、負のAAC priming PTS／DTS、実`AVPacket` unref、exact 10-byte SkipSamples／discard padding再構築、zero-size／side-data-only packetと`DISCARD`等flagの対応encoder別実測、global header、codecparの`frame_size`／`initial_padding`、extradata padding、header後actual time-base readback、`av_packet_rescale_ts`数値表、fragment option、interleaved ownership、trailer／close error | portable seamだけをproduction Greenと数えず、実AAC先頭packetを失わない、MOV edit list／decode後presentation 0と末尾paddingをffprobe／playbackで証明、packet漏れ・二重送信なし、Finish後frame拒否、header未成立file／失敗fileをpublishしない |
| P0 | D3D11 processor | `WAIT_TIMEOUT`、`WAIT_ABANDONED`、device removed、Release failure | timeoutだけ継続、abandoned/device lossはsurface再作成またはterminal fault、取得済みresourceを必ず解放 |
| P0 | Spout2 receiver | sender消失、texture再作成、adapter LUID変化、poll中Abort | 古いsurfaceをencodeせず、receiver/handleをexactly once解放 |
| P0 | WASAPI COM seam | GetBuffer/ReleaseBuffer各device loss、timestamp error、buffer empty、同一thread確認 | Get/Release対応を崩さず、device-loss frameを確定し、recoveryまたはterminal化 |
| P1 | C ABI lifetime | callback内`destroy`の可否 | 禁止ならheader契約と拒否test、許可なら参照count付きlifetimeとchild-process test |
| P1 | full media backend | video/audio片側Start失敗、片側Finish失敗、callback中Abort、実file close失敗、first-packet後encoder fault時のpart rollover | rollback/join/event/file publicationが本マトリクスと一致し、part確定＋software次partを実装するか製品仕様を明示改訂 |

## 完了判定

抽象pipelineのfailure matrixはLinux CTestで継続検証する。release適格を名乗るには、上表のproduction adapterテスト、Windows MSVC build、Windows 10/11実行、実GPU/HMD/VRChat/SteamVR試験、90% line/branch gateを別途すべて通過させる。
