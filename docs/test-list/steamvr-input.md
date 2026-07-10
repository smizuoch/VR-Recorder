# SteamVR input test list / SteamVR Inputテストリスト

## 日本語

- [x] `toggle_recording`をmandatory boolean actionとして日本語・英語で定義する
- [x] Index／Oculus Touch／Vive controller向けrecommended bindingを同梱する
- [x] System buttonをdefault bindingに使用しない
- [x] activeなdigital actionのrising edgeだけを共通`ToggleRecording` commandへdispatchする
- [x] install rootから存在するabsolute action manifest pathを解決する
- [x] WPF publish payloadへaction manifestとbindingsを同梱する
- [x] native ABIのdigital stateをmanaged async streamへ変換しcancellationでdestroyする
- [ ] OpenVR `SetActionManifestPath`へinstall directory内のabsolute pathを登録する
- [ ] 実SteamVR runtimeでbinding読込みとcontroller再割当を検証する
- [ ] desktop click／keyboard／wrist rayと同じapplication dispatcherを実行する

## English

- [x] Define `toggle_recording` as a mandatory boolean action in Japanese and English
- [x] Ship recommended bindings for Index, Oculus Touch, and Vive controllers
- [x] Use no System button in default bindings
- [x] Dispatch only active digital-action rising edges to the shared `ToggleRecording` command
- [x] Resolve an existing absolute action-manifest path from the install root
- [x] Include the action manifest and bindings in the WPF publish payload
- [x] Convert native digital state into a managed async stream and destroy it on cancellation
- [ ] Register an install-directory absolute path through OpenVR `SetActionManifestPath`
- [ ] Verify binding load and controller rebinding with a real SteamVR runtime
- [ ] Execute the same application dispatcher used by desktop click, keyboard, and wrist ray
