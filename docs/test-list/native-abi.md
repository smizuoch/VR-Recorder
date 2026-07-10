# Native ABI test list / Native ABIテストリスト

## 日本語

- [x] 公開headerをC17とC++20の両方でcompileする
- [x] ABI v1のx64 struct sizeと固定幅fieldを検証する
- [x] null／undersized／unsupported ABI入力をhandle生成前に拒否する
- [x] backendのmux成功通知後だけFIRST_VIDEO_PACKET_MUXEDを1回発行する
- [x] trailer／flush／close完了通知後だけSTOPPEDをpacket count付きで1回発行する
- [x] FAULTEDをterminal eventにし、abort後のcallbackを抑止する
- [x] production shared libraryのexportを承認済み6 symbolに限定しlink mapを生成する
- [ ] SteamVR inputのversioned config/state・create/poll/destroy ABIを固定する
- [x] managed bridgeがABI v1 callbackをFIRST／STOPPED／FAULTEDへ変換する
- [ ] Windows x64 DLLをMSVC toolchainでbuildしABIを検証する
- [ ] 承認済みSpout／WASAPI／FFmpeg backendで実際のmux lifecycleを検証する
- [ ] native branch／line coverageのrelease thresholdを適用する

## English

- [x] Compile the public header as both C17 and C++20
- [x] Verify ABI v1 x64 struct sizes and fixed-width fields
- [x] Reject null, undersized, and unsupported-ABI inputs before allocating a handle
- [x] Emit one FIRST_VIDEO_PACKET_MUXED event only after the backend reports a successful mux write
- [x] Emit one STOPPED event with packet counts only after trailer, flush, and close completion
- [x] Make FAULTED terminal and suppress callbacks after abort
- [x] Limit the production shared-library exports to six approved symbols and generate a link map
- [ ] Freeze the versioned SteamVR input config/state and create/poll/destroy ABI
- [x] Translate ABI v1 callbacks into managed FIRST, STOPPED, and FAULTED events
- [ ] Build the Windows x64 DLL with the MSVC toolchain and verify its ABI
- [ ] Verify the real mux lifecycle with approved Spout, WASAPI, and FFmpeg backends
- [ ] Enforce the native branch and line coverage release thresholds
