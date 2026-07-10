# Native ABI test list / Native ABIテストリスト

## 日本語

- [x] 公開headerをC17とC++20の両方でcompileする
- [x] CMake／CTestのWindows x64 build graphとMSVC CI契約を固定する
- [x] ABI v1のx64 struct sizeと固定幅fieldを検証する
- [x] null／undersized／unsupported ABI入力をhandle生成前に拒否する
- [x] backendのmux成功通知後だけFIRST_VIDEO_PACKET_MUXEDを1回発行する
- [x] trailer／flush／close完了通知後だけSTOPPEDをpacket count付きで1回発行する
- [x] FAULTEDをterminal eventにし、abort後のcallbackを抑止する
- [x] production shared libraryのexportを承認済み9 symbolに限定しlink mapを生成する
- [x] production placeholder backendがC ABIから明示的にBACKEND_UNAVAILABLEを返す
- [x] SteamVR inputのversioned config/state・create/poll/destroy ABIを固定する
- [x] managed bridgeがABI v1 callbackをFIRST／STOPPED／FAULTEDへ変換する
- [x] 選択済みencoderを固定値でmanagedからnativeへ渡し、不正値を拒否し、旧40 byte configはMF softwareへ既定化する
- [x] source pixel formatと推定source FPSをappend-only session configで渡し、旧160 byte configの既定値も固定する
- [ ] Spout source create/snapshot/poll/destroyをABI v1の固定structとexport allowlistに追加する
- [ ] snapshot/frameのUTF-8 packed bufferに必要sizeを返し、容量不足時にデータを消費しない
- [ ] malformed UTF-8、oversize、不正enum／geometry／FPS／timestampをmanagedへ渡す前に拒否する
- [ ] poll実行中のdestroyを安全に合流し、pointer-to-pointer destroyを冪等にする
- [ ] production placeholder Spout backendは明示的にBACKEND_UNAVAILABLEを返す
- [ ] Windows x64 DLLをMSVC toolchainでbuildしABIを検証する
- [ ] 承認済みSpout／WASAPI／FFmpeg backendで実際のmux lifecycleを検証する
- [ ] native branch／line coverageのrelease thresholdを適用する

## English

- [x] Compile the public header as both C17 and C++20
- [x] Freeze the CMake/CTest Windows x64 build graph and MSVC CI contract
- [x] Verify ABI v1 x64 struct sizes and fixed-width fields
- [x] Reject null, undersized, and unsupported-ABI inputs before allocating a handle
- [x] Emit one FIRST_VIDEO_PACKET_MUXED event only after the backend reports a successful mux write
- [x] Emit one STOPPED event with packet counts only after trailer, flush, and close completion
- [x] Make FAULTED terminal and suppress callbacks after abort
- [x] Limit the production shared-library exports to nine approved symbols and generate a link map
- [x] Return explicit BACKEND_UNAVAILABLE from production placeholder backends through the C ABI
- [x] Freeze the versioned SteamVR input config/state and create/poll/destroy ABI
- [x] Translate ABI v1 callbacks into managed FIRST, STOPPED, and FAULTED events
- [x] Carry the selected encoder through stable managed/native values, reject invalid values, and default legacy 40-byte configs to MF software
- [x] Carry source pixel format and estimated source FPS in an append-only session config while freezing defaults for legacy 160-byte configs
- [ ] Add Spout source create/snapshot/poll/destroy to the ABI-v1 fixed structs and export allowlist
- [ ] Return required sizes for packed UTF-8 snapshot/frame buffers without consuming data when capacity is insufficient
- [ ] Reject malformed UTF-8, oversize data, and invalid enum/geometry/FPS/timestamp values before they reach managed code
- [ ] Safely join destroy with an active poll and make pointer-to-pointer destroy idempotent
- [ ] Return explicit BACKEND_UNAVAILABLE from the production placeholder Spout backend
- [ ] Build the Windows x64 DLL with the MSVC toolchain and verify its ABI
- [ ] Verify the real mux lifecycle with approved Spout, WASAPI, and FFmpeg backends
- [ ] Enforce the native branch and line coverage release thresholds
