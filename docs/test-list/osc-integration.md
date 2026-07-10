# OSC integration test list / OSC結合テストリスト

## 日本語

- [ ] 実loopback UDPでAcquireをMode=2→Streaming=true、Restoreを逆順に送る
- [ ] 初回echo欠落時だけ200 ms後に1回再送する
- [ ] 2回とも未確認なら明示的confirmation failureにする
- [ ] cancellationでreceive/retryを即時停止する
- [ ] malformed packet、wrong source、stale echoを確認扱いしない
- [ ] OSCQueryで複数VRChat候補を発見し、選択前は送信しない

## English

- [ ] Send Acquire as Mode=2→Streaming=true and Restore in reverse over real loopback UDP
- [ ] Retry exactly once after 200 ms only when the first echo is missing
- [ ] Return an explicit confirmation failure after two unconfirmed attempts
- [ ] Stop receive/retry promptly on cancellation
- [ ] Reject malformed packets, wrong sources, and stale echoes as confirmations
- [ ] Discover multiple VRChat targets with OSCQuery and send nothing before selection
