# ADR-0004: native mux入力はcanonical microsecondsとtyped H.264/AAC descriptorを使う

- Status: Accepted
- Date: 2026-07-13

## Context

libavformatは出力streamとcodec parametersを作成した後、packetより先に`avformat_write_header`を必須とする。header処理後の`AVStream.time_base`はcallerが要求した値から変わり得るため、packetのPTS／DTS／durationはheader後に確定した各stream time baseへ変換しなければならない。

従来のnative抽象はpacket metadataとpayloadだけを持ち、H.264／AAC extradata、packet形式、time base、header開始を表現していなかった。またA/V packetの到着時刻からC++ coordinatorが`EndFragment`を呼んでいたため、audioがvideoより先行しただけで誤ったfragment境界を作り得た。

## Decision

### Canonical packet timeline

- native mux境界へ渡す全packetのPTS／DTS／durationは、明示した`1/1,000,000` time baseのmicrosecondsとする。
- FFmpeg encoder adapterは、encoder固有time baseからcanonical microsecondsへpacketを変換する。
- FFmpeg muxer adapterは`avformat_write_header`成功後に確定した各`AVStream.time_base`を保存し、canonical microsecondsからPTS／DTS／durationをそれぞれrescaleしてから書く。
- video timestampは非負とする。audioはAAC `initial_padding`で定まるbounded negative priming epochを保持し、MOV edit listへ渡す。A/V診断はpresentation time 0未満のaudio packetだけを観測対象外にする。
- A/V sync、CFR、診断event、C ABI統計は引き続きmicrosecondsを使用する。

### Typed stream descriptors

header入力をvideo／audioで分ける。

- H.264: time base、偶数width／height、Main／High profile、Annex B／AVCC length-prefixed packet形式、owned codec extradata
- AAC: time base、48 kHz、stereo layout、AAC-LC、encoder open後のframe size／initial padding、raw access unit形式、owned AudioSpecificConfig
- H.264／AAC extradataが空、AACがADTS、未知profile／layout／packet形式、非microsecond time baseの場合はheader backendを呼ばない。
- FFmpeg adapterが`AVCodecParameters::extradata`へコピーするときは`AV_INPUT_BUFFER_PADDING_SIZE`分を追加確保し、paddingをゼロにする。
- AAC priming／final paddingに必要になり得る`AV_PKT_DATA_SKIP_SAMPLES`はaudio-only／exact 10-byteのtyped・owned side dataとしてpacketからmux境界まで保持する。wrong-size、duplicate、video packet上の値は拒否する。
- side-dataだけでpayloadが空の`AVPacket`と、未対応種類のside dataは現在の抽象では表現しない。production adapterは黙ってdropせずfail-closedとし、必要な種類は別TDDで追加する。

### Lifecycle

録画開始順序を次に固定する。

```text
video/audio encoder open and descriptor creation
  -> mux WriteHeader
  -> video worker Start
  -> audio worker Start
```

- Begin前のpacket／Finish、二重Beginを拒否する。
- invalid descriptorはmuxerを変更しない。
- header failureはmuxをAbortして両streamを開始しない。
- header開始中のAbort後にstreamを開始しない。

### Initial H.264 reordering policy

初回production sliceは`maximum_b_frame_count = 0`とする。現在のvideo非負DTS／canonical microsecond契約へvendor既定のB-frameを混入させない。

B-frameを将来有効化する場合は、負の初期DTS、PTS/DTS reorder、CTTS、fragment epoch、A/V sync観測をRedから再設計する。

### Fragmentation

- generic C++ coordinatorから手動`EndFragment`を呼ばない。
- header policyは最小1秒、最大2秒、video keyframe優先に固定する。
- FFmpeg muxerは`movflags +frag_keyframe`、`min_frag_duration=1,000,000`、`frag_duration=2,000,000`へ写像する。
- packetは`av_interleaved_write_frame`へ統一し、A/V interleaveとfragment判断をlibavformatへ任せる。
- `frag_custom`と`av_write_frame(ctx, NULL)`は使わない。

## Consequences

- production FFmpeg muxerはheader後の実stream time baseを必ずreadbackする。
- encoder固有ticksをnative全体へ漏らさず、既存A/V診断を維持できる。
- encoder open／descriptor作成とworker開始を分離するproduction factoryが必要になる。
- fMP4 fragment optionの実設定、packet rescale、interleaved ownershipはFFmpeg adapter testで検証する。

## Sources

- FFmpeg muxing API: <https://ffmpeg.org/doxygen/trunk/group__lavf__encoding.html>
- FFmpeg AVPacket ownership and timestamps: <https://ffmpeg.org/doxygen/trunk/structAVPacket.html>
- FFmpeg MOV/MP4 fragmentation options: <https://ffmpeg.org/ffmpeg-formats.html#Fragmentation>
- FFmpeg AVCodecContext: <https://ffmpeg.org/doxygen/trunk/structAVCodecContext.html>
