# ADR-0005: FFmpeg adapterをportable state machineと実API薄層に分離する

- Status: Accepted
- Date: 2026-07-13

## Context

FFmpegのencode APIは、1回の入力から0個以上のpacketを返す。`avcodec_send_frame`が`EAGAIN`を返した場合は入力が消費されていないため、出力をreceiveしてから同じ入力を再送しなければならない。drainはNULL frameを送った後、`avcodec_receive_packet`が`AVERROR_EOF`を返すまで続け、drain受理後の`EAGAIN`は契約違反である。

また、成功した`AVPacket`は参照count付きbufferとside dataを所有する。domain packetへcopyした後は、成功経路だけでなく検証失敗・確保失敗でも必ずunrefする必要がある。

AACでは`AVCodecContext.initial_padding`がoutput packet timestampへ反映される。input PTSが0でも先頭packetのPTS／DTSは`-initial_padding`になり得るため、「全packet timestampは非負」という一般化は正しくない。MOV muxerは負の先頭PTSをedit listでpresentation time 0へ切り詰める。`AV_PKT_DATA_SKIP_SAMPLES`はu32le×2とreason byte×2の10 byte layoutであり、FFmpeg encoderが生成するpacket side dataも10 byteである。

mux側では`avformat_write_header`が`AVStream.time_base`を変更し得る。packetはheader後に読み戻した実time baseへ変換し、`av_interleaved_write_frame`へ渡す必要がある。FFmpegの整数rescaleはoverflow回避と規定の丸めを含むため、独自実装を複製しない。

現在のLinux開発環境にはlibavcodec／libavformat開発libraryを固定していない。一方で、状態遷移と失敗境界は実backendの導入前にportable testで固定できる。

## Decision

### Encoder state machine

既存の`PacketVideoEncoder`／`PacketAudioEncoder`公開契約は維持し、その内部に`FfmpegEncoderStateMachine`を置く。

- `SendPreparedFrame`成功後はreceiveを`EAGAIN`まで反復し、0 packetも正常とする。
- sendが`EAGAIN`ならpending packetをreceiveし、`EAGAIN`到達後に同じprepared frameを1回再送する。
- sendとreceiveの同時`EAGAIN`、receive前drainの予期しないEOF、再送後の再`EAGAIN`はbusy loopせずterminal faultにする。
- drain NULLが受理された後はEOFだけを正常完了とし、`EAGAIN`／一般errorでは部分batchを破棄してAbortする。
- 成功receiveごとにRAIIでunrefし、payload、timestamp、対応済みside dataをowned packetへcopyする。
- malformed packet、unknown timestamp、`PTS < DTS`、非正duration、timestamp＋duration overflow、corrupt flag、未知side data、OOMはfail-closedとする。video timestampは非負を維持する。
- AAC packetはencoder primingによる負のPTS／DTSを改変せず保持する。stream descriptorは`frame_size`と`initial_padding_samples`を所有し、coordinatorはpaddingをmicrosecondsへ切り上げた下限より古いaudio timestampを拒否する。
- A/V診断は負epochのAAC priming packetをmuxから除去せず、drift sampleとしてだけ無視する。presentation time 0以降のaudio packetから通常のdrift観測を始める。
- `AV_PKT_DATA_SKIP_SAMPLES`はaudio packet上のexact 10 byteだけを`EncodedPacketSideDataKind::SkipSamples`としてtyped・ownedでmux境界まで保持する。duplicate、video上、wrong-size、empty side-data-only packet、未知side dataは拒否する。

### Mux time-base boundary

`FfmpegFragmentedMp4Muxer`は、FFmpeg型を公開しない`FfmpegMuxerPort`を使う。

1. headerを書く。
2. 成功後にvideo／audioの実stream time baseを値としてreadbackする。
3. canonical microsecondsのPTS／DTS／durationを、実portの`RescalePacketTimestamps`へ渡す。
4. rescale後はvideoの非負値、audioのdefined signed timestamp、`PTS >= DTS`、正duration、timestamp＋duration overflow、stream別strict DTSをwrite前に検査する。
5. callerのowned packetを変更せず、rescale済みtimingとともにinterleaved-write薄層へ渡す。

実FFmpeg portは`av_packet_rescale_ts`を直接使う。portable層に`__int128`、floating-point、FFmpeg数学関数の写経を置かない。`INT64_MIN`化、durationの0化、end-time overflow、丸め後DTS衝突はwrite前に拒否する。durableなtrailer＋file flush後のAbortは確定済み出力を無効化しないno-opとする。

### Callback reentrancy

mux observerとA/V drift event sinkは外部実装を同期呼出しするため、coordinator／shared finalization／monitorのstate mutexを保持したまま呼ばない。packet writeとobserver順序は専用のsubmit mutexで直列化し、state更新後にstate mutexを解放してobserverへ通知する。A/V monitorもsnapshot用の値をlock内で確定し、event callback自体はlock外で呼ぶ。これによりcallback内のstatistics readbackとAbortを許可し、Abortはtrailerを書かずexactly onceでterminal化する。

### Evidence boundary

portable fake testのGreenは、状態機械とcall orderingの証拠であり、production FFmpeg backendの証拠ではない。production adapterのrelease gateには別途、次を要求する。

- pinned FFmpegをlinkしたWindows MSVC build
- 実`AVERROR` mapping、実packet unref／side-data再構築
- `av_packet_rescale_ts`の数値表とheader後time-base readback
- codec parametersの`frame_size`／`initial_padding`、extradata padding、fragment options、interleaved ownership
- 実AACの負の先頭PTS／DTS、MOV edit list、SkipSamples／discard padding、対応encoderが出すzero-size／side-data-only packetとpacket flagの実測
- 実H.264/AAC fMP4生成、ffprobe、decode／playback、graceful/forced failure file publication

## Consequences

- send／receive／drainの分岐は実codecなしでも網羅的に回帰できる。
- rescale算術の互換性はFFmpegへ委譲しつつ、header順序とpostconditionはportable testで固定できる。
- portable seamは同じ`SendPreparedFrame`操作の再試行を固定するが、同一`AVFrame` identityと二重消費防止は実portのRedで証明する。
- SkipSamples以外のpacket side dataが実encoderで観測された場合は黙ってdropせず、新しいtyped ownershipをRedから追加する。
- 同期observer／drift callbackはstate lock外で発火するため、callback内Abortとstatistics readbackを自己deadlockなしで回帰できる。
- production media backend、Windows実FFmpeg port、実codec frame preparationは引き続き未実装であり、本ADRだけでは録画可能性を主張しない。

## Sources

- FFmpeg send/receive API: <https://www.ffmpeg.org/doxygen/trunk/group__lavc__encdec.html>
- FFmpeg packet ownership and side data: <https://ffmpeg.org/doxygen/trunk/structAVPacket.html>
- FFmpeg audio initial padding: <https://www.ffmpeg.org/doxygen/trunk/structAVCodecContext.html>
- FFmpeg SkipSamples layout: <https://ffmpeg.org/doxygen/trunk/packet_8h_source.html>
- FFmpeg encoder timestamp and side-data construction: <https://www.ffmpeg.org/doxygen/trunk/encode_8c_source.html>
- FFmpeg MOV edit-list and SkipSamples handling: <https://ffmpeg.org/doxygen/trunk/movenc_8c_source.html>
- FFmpeg packet rescaling: <https://www.ffmpeg.org/doxygen/trunk/packet_8c_source.html>
- FFmpeg integer rescaling: <https://ffmpeg.org/doxygen/trunk/mathematics_8c_source.html>
- FFmpeg muxing contract: <https://ffmpeg.org/doxygen/trunk/group__lavf__encoding.html>
