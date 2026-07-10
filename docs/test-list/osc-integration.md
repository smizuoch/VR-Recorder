# OSC integration test list / OSC結合テストリスト

## 日本語

- [x] loopback OSCQuery advertisementのHTTP capabilityを検証しtyped candidateへ変換する
- [x] OSCQuery JSONの重複security propertyを拒否する
- [x] VRChat camera endpointの404をexplicit capability failureとして返す
- [x] OSCQuery deadlineとcaller cancellationを区別する
- [x] 複数targetの選択完了前はgatewayを生成しない
- [x] 実loopback UDPでAcquireをMode=2→Streaming=true、Restoreを逆順に送る
- [x] 初回echo欠落時だけ200 ms後に1回再送する
- [x] 2回とも未確認なら明示的confirmation failureにする
- [ ] cancellationでreceive/retryを即時停止する
- [ ] malformed packet、wrong source、stale echoを確認扱いしない
- [x] 複数候補またはstaleな選択IDでは明示的target選択を要求する
- [x] OSCQueryで複数VRChat候補を発見し、選択前は送信しない

## English

- [x] Validate HTTP capabilities for loopback OSCQuery advertisements and produce typed candidates
- [x] Reject duplicate security properties in OSCQuery JSON
- [x] Return an explicit capability failure for a missing VRChat camera endpoint
- [x] Distinguish the OSCQuery deadline from caller cancellation
- [x] Create no gateway before multiple-target selection completes
- [x] Send Acquire as Mode=2→Streaming=true and Restore in reverse over real loopback UDP
- [x] Retry exactly once after 200 ms only when the first echo is missing
- [x] Return an explicit confirmation failure after two unconfirmed attempts
- [ ] Stop receive/retry promptly on cancellation
- [ ] Reject malformed packets, wrong sources, and stale echoes as confirmations
- [x] Require explicit target selection for multiple candidates or a stale selected service ID
- [x] Discover multiple VRChat targets with OSCQuery and send nothing before selection
