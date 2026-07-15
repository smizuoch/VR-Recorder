# ADR-0007: OpenVR application manifestは起動ごとにtemporary登録する

- Status: Accepted
- Date: 2026-07-15

## Context

`actions.json`はinput actionの定義であり、SteamVRがapplicationを識別するapplication manifestではない。VR Recorderにはstable application key、起動EXE、action manifestを結び付ける`.vrmanifest`が別途必要である。

永続登録に絶対パスを保存すると、unpackaged payloadの移動、upgrade、MSIXのversioned install location、uninstall後に旧EXEを指すstale registrationが残り得る。OpenVRのtemporary manifestはSteamVR起動時に自動復元されないため、使用するprocessが現在のpathを毎回登録する必要がある。

## Decision

- application keyは`com.vrrecorder.desktop`で固定する。
- install rootの`OpenVr/steamvr.vrmanifest`をapplication manifestとし、`OpenVr/actions.json`と各controller bindingを同じpayloadに含める。
- manifestは単一applicationだけを定義し、entry pointは`../VRRecorder.App.exe`、action manifestは`actions.json`に固定する。
- process起動時にcurrent install rootを絶対pathへ解決し、manifest、EXE、action manifestの存在と固定契約を検証する。旧version、別directory、path traversal、欠落fileはOpenVRへ渡す前に拒否する。
- process-wide OpenVR ownerが`IVRApplications::AddApplicationManifest(currentManifestPath, true)`を1回だけ実行してから、action manifest登録、input polling、overlay作成を開始する。登録失敗時はSteamVR機能を開始しない。
- OpenVR runtimeを同一process内で再初期化した場合もcurrent manifestを再登録する。SteamVR再起動後の新しいprocess起動でも同じ手順を繰り返す。
- persistent登録は作らず、uninstall時の`RemoveApplicationManifest`に依存しない。upgrade後は新versionのprocessが新しいpackage locationだけをtemporary登録する。
- MSIXでもworking directoryではなく、起動中packageのinstall locationから同じ絶対pathを解決する。

manifest assetとmanaged validation contractに加え、native production adapterは検証済みaction manifestと同じdirectoryのapplication manifestを`IVRApplications`へtemporary登録する。process-wide ownerは同じruntime generation内の登録を1回に集約し、登録失敗時はaction manifest／handle初期化へ進まない。全client解放後の次generationでは再登録する。portable failure testとpinned OpenVR 2.15.6を使うWindows Release linkを実装証拠とし、実SteamVRでの登録／再起動／upgradeはHILとして別に残す。

## TDD sequence

1. application key、entry point、action manifest pathの変更を拒否する。
2. current executableまたはaction manifest欠落を登録前に拒否する。
3. malformed、複数application、unknown／duplicate propertyを拒否する。
4. current absolute manifest pathをtemporary flag付きでexactly once登録する。
5. 登録失敗時にinput／overlay初期化を行わない。
6. runtime再初期化時は再登録し、同じruntime generation内の重複登録は行わない。
7. publish payloadとMSIX展開payloadの両方で、current install locationだけに解決されることを検証する。

## Consequences

- upgrade／uninstall lifecycleからstale persistent pathを排除できる。
- VR Recorderを起動するたびにSteamVRへ登録する小さなruntime costが生じる。
- SteamVRを再起動してVR Recorderを継続利用する場合、OpenVR runtime generationの再初期化と再登録が必要になる。
- manifestを配布し忘れたpayloadはSteamVR統合をfail closedする。

## Sources

- Valve OpenVR API documentation: <https://github.com/ValveSoftware/openvr/wiki/API-Documentation>
- Valve OpenVR `IVRApplications` interface: <https://github.com/ValveSoftware/openvr/blob/v2.15.6/headers/openvr.h>
