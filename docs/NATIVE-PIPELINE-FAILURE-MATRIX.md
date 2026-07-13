# Native録画パイプライン失敗境界マトリクス

更新日: 2026-07-13

## 目的

native録画パイプラインの失敗テストを、見つかった不具合ごとに場当たり的に追加するのではなく、外部APIの所有権契約と内部state machineから体系的に生成する。この文書は実装済みの自動テストと、実backend導入時にRedから開始する残課題を分離する。

## 根拠とする外部契約

- FFmpeg send/receive API: `avcodec_send_frame(NULL)`でdrainへ入り、`avcodec_receive_packet()`を`AVERROR_EOF`まで呼ぶ。drain後の追加frameは拒否される。<https://ffmpeg.org/doxygen/trunk/group__lavc__decoding.html>
- FFmpeg muxing example: trailerはcodec contextや出力fileを閉じる前に書く。<https://ffmpeg.org/doxygen/trunk/doc_2examples_2muxing_8c-example.html>
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
| fragmented MP4 finalization | EndFragment/Trailer/Flush各失敗 | 後続段階を実行せずAbort exactly once、以後terminal拒否 | `fragmented_mp4_mux_coordinator_tests.cpp` |
| C ABI callback | callback内Abort | callback quiescenceを保ちつつ自己deadlockしない | `abi_contract_tests.cpp` |
| production stop worker | Join中Abort | current threadをjoinせず、別thread/destructorでjoinを回収 | `pipeline_media_backend_tests.cpp` |
| session Start milestone | Start中first packet後にStart失敗 | FIRST_VIDEO_PACKETを外へcommitせずrollback | `abi_contract_tests.cpp` |

## 実backend導入時の必須Redテスト

以下は抽象境界では代替できず、対応するproduction adapterを追加するcommitで先にRedを書く。

| 優先度 | backend | 必須ケース | 完了条件 |
|---|---|---|---|
| P0 | FFmpeg/MF encoder | sendの`EAGAIN`、receiveの0/複数packet、drainの`EOF`、flush/trailer error、packet unref | packet漏れ・二重送信なし、Finish後frame拒否、失敗fileをpublishしない |
| P0 | D3D11 processor | `WAIT_TIMEOUT`、`WAIT_ABANDONED`、device removed、Release failure | timeoutだけ継続、abandoned/device lossはsurface再作成またはterminal fault、取得済みresourceを必ず解放 |
| P0 | Spout2 receiver | sender消失、texture再作成、adapter LUID変化、poll中Abort | 古いsurfaceをencodeせず、receiver/handleをexactly once解放 |
| P0 | WASAPI COM seam | GetBuffer/ReleaseBuffer各device loss、timestamp error、buffer empty、同一thread確認 | Get/Release対応を崩さず、device-loss frameを確定し、recoveryまたはterminal化 |
| P1 | C ABI lifetime | callback内`destroy`の可否 | 禁止ならheader契約と拒否test、許可なら参照count付きlifetimeとchild-process test |
| P1 | full media backend | video/audio片側Start失敗、片側Finish失敗、callback中Abort、実file close失敗 | rollback/join/event/file publicationが本マトリクスと一致 |

## 完了判定

抽象pipelineのfailure matrixはLinux CTestで継続検証する。release適格を名乗るには、上表のproduction adapterテスト、Windows MSVC build、Windows 10/11実行、実GPU/HMD/VRChat/SteamVR試験、90% line/branch gateを別途すべて通過させる。
