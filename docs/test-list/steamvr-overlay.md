# SteamVR wrist overlay test list / SteamVR手首overlayテストリスト

## 日本語

- [x] lifecycleをrenderer／pose／input／hapticから独立したPortにする
- [x] 0.18～0.32 mの有限幅だけを許可し、hidden状態でoverlayを作成する
- [x] Show／Hideの成功後だけvisibilityをcommitし、同一操作をidempotentにする
- [x] 初期化失敗をrollbackし、Close／destructorでHide後にDestroyをexactly once行う
- [x] process-wide OpenVR ownerへlifecycle Portを接続し、実`IVROverlay` APIを呼ぶ
- [x] versioned C ABIでstable key／name／manifest path／幅と40-byte BGRA frameを検証し、lifecycle／textureを所有・破棄する
- [x] managed SafeHandleでoverlayとnative DLLの寿命を揃え、Close／Disposeを冪等にする
- [x] 1024×512座標、stable element ID、2倍density、RTL、最小target、disabled hit-testをpure layoutで固定する
- [x] 解決済みtheme／raster assetだけを受け取るBGRA compositorをgolden hash、英日、200%、RTL、high contrast、missing assetで固定する
- [x] elapsed／resolution／target・actual FPS／Spout・audio・mic health／alert／placementを検証済みWrist snapshotへ保持する
- [x] 初回／revision変化を即時、Recording／SignalLostだけを100 ms周期にするpure update policyを固定する
- [x] BGRA frame検証、texture-set commit、冪等Clear、Clear→Hide→Destroyを独立native texture Portで固定し、process-wide OpenVR ownerへ接続する
- [x] HMDのDXGI adapter上でD3D11 BGRA textureを所有・再利用し、実RowPitchでuploadして`SetOverlayTexture(TextureType_DirectX)`へ渡す
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
- [x] Validate the stable key, name, manifest path, width, and 40-byte BGRA frame at the versioned C ABI and own/destroy the lifecycle and texture
- [x] Align overlay and native DLL lifetimes with a managed SafeHandle and make Close/Dispose idempotent
- [x] Fix 1024x512 coordinates, stable element IDs, 2x density, RTL, minimum targets, and disabled hit-testing in a pure layout
- [x] Fix the resolved-theme/raster-asset-only BGRA compositor with a golden hash, English/Japanese, 200%, RTL, high contrast, and missing-asset tests
- [x] Carry elapsed time, resolution, target/actual FPS, Spout/audio/mic health, alerts, and placement in a validated wrist snapshot
- [x] Fix a pure update policy that renders first/new revisions immediately and only Recording/SignalLost at 100 ms intervals
- [x] Fix BGRA validation, texture-set commit, idempotent Clear, and Clear→Hide→Destroy in an independent native texture Port connected to the process-wide OpenVR owner
- [x] Own and reuse a D3D11 BGRA texture on the HMD DXGI adapter, upload with the actual row pitch, and pass it to `SetOverlayTexture(TextureType_DirectX)`
- [ ] Update a 1024×512 BGRA texture on state changes and at 10 Hz while recording
- [ ] Hit-test mouse/ray events and dispatch shared application commands
- [ ] Apply Wrist Dock, World Pin, drag, nudge, and recenter to runtime transforms
- [ ] Emit recording-start, recording-stop, and fault haptic pulses
- [ ] Verify lifecycle, visibility, and reconnection with real SteamVR, HMD, and controllers
