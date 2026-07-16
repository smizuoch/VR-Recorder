# SteamVR input test list / SteamVR Inputテストリスト

## 日本語

- [x] `toggle_recording`をmandatory boolean actionとして日本語・英語で定義する
- [x] Index／Oculus Touch／Vive controller向けrecommended bindingを同梱する
- [x] 全controllerで録画に加えてmicとrecenterを別の非System buttonへ割り当てる
- [x] System buttonをdefault bindingに使用しない
- [x] activeなdigital actionのrising edgeだけを共通`ToggleRecording` commandへdispatchする
- [x] install rootから存在するabsolute action manifest pathを解決する
- [x] WPF publish payloadへaction manifestとbindingsを同梱する
- [x] native ABIのdigital stateをmanaged async streamへ変換しcancellationでdestroyする
- [x] OpenVR `SetActionManifestPath`へinstall directory内のabsolute pathを登録する
- [x] stable app keyとcurrent install rootだけを許可するapplication `.vrmanifest`を同梱・検証する
- [x] `IVRApplications`へcurrent `.vrmanifest`をruntime generationごとにtemporary登録する
- [x] process-wide 1本の最大90 Hz poll loopから全digital actionへ同じrevisionをfan-outする
- [x] `recenter_overlay`のactive rising edgeを同じlazy runtimeからproduction placement coordinatorへ渡し、App終了時にoverlay lifecycleまで破棄する
- [x] first-run binding確認ではrecord／mic／recenterの全actionがactiveであることを要求する
- [ ] 実SteamVR runtimeでbinding読込みとcontroller再割当を検証する
- [x] desktop click／keyboard／wrist rayと同じapplication dispatcherを実行する

## English

- [x] Define `toggle_recording` as a mandatory boolean action in Japanese and English
- [x] Ship recommended bindings for Index, Oculus Touch, and Vive controllers
- [x] Bind microphone and recenter to distinct non-System buttons alongside recording on every controller
- [x] Use no System button in default bindings
- [x] Dispatch only active digital-action rising edges to the shared `ToggleRecording` command
- [x] Resolve an existing absolute action-manifest path from the install root
- [x] Include the action manifest and bindings in the WPF publish payload
- [x] Convert native digital state into a managed async stream and destroy it on cancellation
- [x] Register an install-directory absolute path through OpenVR `SetActionManifestPath`
- [x] Ship and validate an application `.vrmanifest` restricted to the stable app key and current install root
- [x] Temporarily register the current `.vrmanifest` through `IVRApplications` once per runtime generation
- [x] Fan out one revision from a process-wide poll loop capped at 90 Hz to every digital action
- [x] Route active `recenter_overlay` rising edges from the same lazy runtime to the production placement coordinator and dispose the overlay lifecycle at App shutdown
- [x] Require recording, microphone, and recenter actions all to be active in first-run binding verification
- [ ] Verify binding load and controller rebinding with a real SteamVR runtime
- [x] Execute the same application dispatcher used by desktop click, keyboard, and wrist ray
