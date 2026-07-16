# SteamVR wrist overlay test list / SteamVR手首overlayテストリスト

## 日本語

- [x] lifecycleをrenderer／pose／input／hapticから独立したPortにする
- [x] 0.18～0.32 mの有限幅だけを許可し、hidden状態でoverlayを作成する
- [x] Show／Hideの成功後だけvisibilityをcommitし、同一操作をidempotentにする
- [x] 初期化失敗をrollbackし、Close／destructorでHide後にDestroyをexactly once行う
- [x] process-wide OpenVR ownerへlifecycle Portを接続し、実`IVROverlay` APIを呼ぶ
- [x] versioned C ABIでstable key／name／manifest path／幅を検証し、lifecycleを所有・破棄する
- [x] managed SafeHandleでoverlayとnative DLLの寿命を揃え、Close／Disposeを冪等にする
- [ ] 1024×512 BGRA textureをstate change時と録画中10 Hzで更新する
- [ ] mouse／ray eventをhit-testして共通application commandへdispatchする
- [ ] Wrist Dock／World Pin／drag／nudge／recenterをruntime transformへ適用する
- [ ] 録画開始／停止／fault haptic pulseを実行する
- [ ] 実SteamVR／HMD／controllerでlifecycle、visibility、再接続を検証する

## English

- [x] Keep lifecycle in a Port independent from renderer, pose, input, and haptics
- [x] Accept only finite widths from 0.18 to 0.32 m and create the overlay hidden
- [x] Commit visibility only after successful Show/Hide and make repeated operations idempotent
- [x] Roll back failed initialization and Hide then Destroy exactly once from Close/destruction
- [x] Connect the lifecycle Port to the process-wide OpenVR owner and call the real `IVROverlay` API
- [x] Validate the stable key, name, manifest path, and width at the versioned C ABI and own/destroy the lifecycle
- [x] Align overlay and native DLL lifetimes with a managed SafeHandle and make Close/Dispose idempotent
- [ ] Update a 1024×512 BGRA texture on state changes and at 10 Hz while recording
- [ ] Hit-test mouse/ray events and dispatch shared application commands
- [ ] Apply Wrist Dock, World Pin, drag, nudge, and recenter to runtime transforms
- [ ] Emit recording-start, recording-stop, and fault haptic pulses
- [ ] Verify lifecycle, visibility, and reconnection with real SteamVR, HMD, and controllers
