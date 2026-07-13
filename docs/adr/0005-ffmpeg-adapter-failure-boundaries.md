# ADR-0005: FFmpeg adapterをportable state machineと実API薄層に分離する

- Status: Accepted
- Date: 2026-07-13

## Context

FFmpegのencode APIは、1回の入力から0個以上のpacketを返す。`avcodec_send_frame`が`EAGAIN`を返した場合は入力が消費されていないため、出力をreceiveしてから同じ入力を再送しなければならない。drainはNULL frameを送った後、`avcodec_receive_packet`が`AVERROR_EOF`を返すまで続け、drain受理後の`EAGAIN`は契約違反である。

また、成功した`AVPacket`は参照count付きbufferとside dataを所有する。domain packetへcopyした後は、成功経路だけでなく検証失敗・確保失敗でも必ずunrefする必要がある。

AACでは`AVCodecContext.initial_padding`がoutput packet timestampへ反映される。input PTSが0でも先頭packetのPTS／DTSは`-initial_padding`になり得るため、「全packet timestampは非負」という一般化は正しくない。MOV muxerは負の先頭PTSをedit listでpresentation time 0へ切り詰める。`AV_PKT_DATA_SKIP_SAMPLES`はu32le×2とreason byte×2の10 byte layoutだが、pinned 8.1.2のMOV muxerはこのside dataを参照しない。したがって渡すだけで末尾paddingが反映されるとは扱わない。

mux側では`avformat_write_header`が`AVStream.time_base`を変更し得る。packetはheader後に読み戻した実time baseへ変換し、`av_interleaved_write_frame`へ渡す必要がある。reference-counted packetの所有権は成功／失敗にかかわらず同APIへ移るので、error後に同じpacketを再送しない。FFmpegの整数rescaleはoverflow回避と規定の丸めを含むため、独自実装を複製しない。

通常のportable buildはlibavcodec／libavformatを要求しない。一方、production adapter用のWindows buildは公式FFmpeg 8.1.2だけを受け入れ、不完全または法務条件不明のSDKをconfigure時点で拒否する必要がある。

## Decision

### Pinned dependency boundary

`VRRECORDER_ENABLE_FFMPEG_ADAPTERS`はWindows x64専用のopt-inとし、`VRRECORDER_FFMPEG_ROOT`以外のsystem pathを探索しない。受理するSDKは公式release 8.1.2、tag `n8.1.2`、commit `38b88335f99e76ed89ff3c93f877fdefce736c13`、公式tar.xz SHA-256 `464beb5e7bf0c311e68b45ae2f04e9cc2af88851abb4082231742a74d97b524c`へ固定する。

SDK検査は4 libraryの完全version、public header、MSVC import library、major付きshared DLL、build evidenceを照合する。build evidenceのconfigure引数はshared／最小library／native AAC／`h264_mf`／D3D11VA／MP4／file protocolのallowlistと完全一致させ、GPL、nonfree、version3、`--enable-lib*`、autodetectを拒否する。IAMFとx86 assemblyは明示無効化し、MP4が不可避にselectするAC-3 parserと`aac_adtstoasc`／`vp9_superframe` bitstream filterも完全一致で固定する。actual Windows DLLとhashが得られるまではcanonical第三者台帳やnative-link manifestへplaceholderを登録しない。

Linux上の実API契約テストは`eng/build-ffmpeg-contract-test-sdk.sh <absolute-sdk-path>`で同じ公式tarballから隔離SDKを作り、`VRRECORDER_FFMPEG_CONTRACT_TEST_ROOT`へ渡した場合だけ追加する。このSDKはlibavcodec／libavformat Portのcontract test専用で、Windows配布artifactや法務承認の代替にしない。

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
- `AV_PKT_DATA_SKIP_SAMPLES`はaudio packet上のexact 10 byteだけを`EncodedPacketSideDataKind::SkipSamples`としてtyped・ownedで実Port境界まで保持する。duplicate、video上、wrong-size、empty side-data-only packet、未知side dataは拒否する。pinned MOVへ明示変換する処理がない段階では、実Portが未消費side dataを成功扱いせずfail-closed拒否する。

### Mux time-base boundary

`FfmpegFragmentedMp4Muxer`は、FFmpeg型を公開しない`FfmpegMuxerPort`を使う。

1. headerを書く。
2. 成功後にvideo／audioの実stream time baseを値としてreadbackする。
3. canonical microsecondsのPTS／DTS／durationを、実portの`RescalePacketTimestamps`へ渡す。
4. rescale後はvideoの非負値、audioのdefined signed timestamp、`PTS >= DTS`、正duration、timestamp＋duration overflow、stream別strict DTSをwrite前に検査する。
5. callerのowned packetを変更せず、rescale済みtimingとともにinterleaved-write薄層へ渡す。

実FFmpeg portは`av_packet_rescale_ts`を直接使う。portable層に`__int128`、floating-point、FFmpeg数学関数の写経を置かない。`INT64_MIN`化、durationの0化、end-time overflow、丸め後DTS衝突はwrite前に拒否する。trailer＋AVIO flush＋closeがAPI上成功した後のAbortは確定済み出力を無効化しないno-opとする。これは電源断耐性の証明ではない。

### Concrete libavformat boundary

初期`LibavformatFragmentedMp4MuxerPort`は公式8.1.2の`libavformat`／`libavcodec`／`libavutil`完全runtime identityを要求し、検証済みAVCC length-prefixed H.264とraw AAC-LC 48 kHz stereoだけを受理する。avcCはSPS／PPS長、NAL type、forbidden bit、profile／compatibility／levelを検査し、packetはVCLを必須として初期closed-GOP契約のIDRとkey-frame flagを一致させる。pinned MOV内部のAnnex B変換はavcC生成errorを握り潰し得るため、Annex Bは明示converterを実装するまでheader前に拒否する。

fragment optionは`frag_keyframe+empty_moov+delay_moov+default_base_moof`、`frag_duration=2000000`、`min_frag_duration=1000000`、`use_editlist=1`、`avoid_negative_ts=disabled`を完全指定する。これによりAAC先頭`-1024` sampleをzero shiftせず、MOV `elst`の`media_time=1024`としてpresentation 0から除去する。8.1.2 MOVが無視するSkipSamplesをpacketへ付けるだけの実装は行わず、明示的な末尾padding変換が入るまで実Portで拒否する。

canonical packetからrefcounted `AVPacket`を新規作成し、実`av_interleaved_write_frame`へ1回だけ渡す。戻り値が成功でも失敗でもpacketはblankでなければならず、error後のretryは禁止する。正常終了はtrailer、AVIO flush error読出し、closeの順とし、flushが失敗してもcloseをexactly once実行して先行errorを保持する。Abortはtrailer／rename／unlinkを行わずcloseだけを実行し、失敗した`.recording.mp4`を起動時回復用に残す。ここでいうflush／close成功はAPI完了を意味し、電源断耐性の`fsync`を主張しない。

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
- 実libavformat PortはLinuxのpinned contract SDKで検証済みだが、Windows production library、実codec frame preparation、factory／media backendへの接続は未実装であり、本ADRだけでは録画可能性を主張しない。
- exact SDK契約のGreenは依存の取り違え防止だけを証明し、actual Windows build、binary hash、corresponding source、独立法務reviewを代替しない。

## Sources

- FFmpeg 8.1.2 release and library versions: <https://www.ffmpeg.org/download.html>
- FFmpeg LGPL compliance checklist: <https://www.ffmpeg.org/legal.html>
- FFmpeg MediaFoundation encoders: <https://www.ffmpeg.org/ffmpeg-codecs.html#MediaFoundation>
- FFmpeg send/receive API: <https://www.ffmpeg.org/doxygen/trunk/group__lavc__encdec.html>
- FFmpeg packet ownership and side data: <https://ffmpeg.org/doxygen/trunk/structAVPacket.html>
- FFmpeg audio initial padding: <https://www.ffmpeg.org/doxygen/trunk/structAVCodecContext.html>
- FFmpeg SkipSamples layout: <https://ffmpeg.org/doxygen/trunk/packet_8h_source.html>
- FFmpeg encoder timestamp and side-data construction: <https://www.ffmpeg.org/doxygen/trunk/encode_8c_source.html>
- FFmpeg MOV edit-list implementation and absence of SkipSamples consumption in pinned 8.1.2: <https://git.ffmpeg.org/gitweb/ffmpeg.git/blob/n8.1.2:/libavformat/movenc.c>
- FFmpeg packet rescaling: <https://www.ffmpeg.org/doxygen/trunk/packet_8c_source.html>
- FFmpeg integer rescaling: <https://ffmpeg.org/doxygen/trunk/mathematics_8c_source.html>
- FFmpeg muxing contract: <https://ffmpeg.org/doxygen/trunk/group__lavf__encoding.html>
