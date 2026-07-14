# Windows distribution test list

## Hardware Validation Payload

- [x] promotion入力のentrypoint種別としてunpackaged `.exe`だけを許可する
- [x] Hardware Validation Payloadを一般公開可能と判定しない
- [x] MSIXをunpackaged実機検証targetとして受理しない
- [ ] self-contained `win-x64` publish directoryを再現可能に生成する
- [ ] existing root directoryとroot内normalized relative entrypointを読み取り、traversal／reparse／inventory非所属を拒否する
- [ ] publish directory全fileのpath／length／SHA-256／kindからcanonical inventory digestを生成する
- [ ] Legal Bundle ID／manifest hashをpayload identityへ取り込む
- [ ] 実Windows／GPU／VRChat／SteamVR／HMD試験reportをschema検証してpayload identityへ結び付ける
- [ ] 必須matrix caseの完全性と結果から合否を導出し、自己申告`Passed`を入力にしない
- [ ] application source／DLL／asset／manifest／Legal変更でinner identityが変わる場合に以前の実機証拠を無効化する

## Microsoft Store Packaging Candidate

- [x] 実機検証証拠なしのMSIX候補を拒否する
- [x] 不合格の実機検証証拠を拒否する
- [x] product version不一致を拒否する
- [x] source revision不一致を拒否する
- [x] application EXE hash不一致を拒否する
- [x] full payload inventory digest不一致を拒否する
- [x] Legal Bundle ID／manifest hash不一致を拒否する
- [x] Partner Center identity欠落を拒否する
- [x] `.msix`／`.msixupload`以外をStore packaging候補として拒否する
- [x] MSIX候補だけではStore公開可能と判定しない
- [x] 自己申告の実機合格を外部release APIにせず、promotion policyをinternal境界に限定する
- [x] placeholder Partner Center identityを拒否する
- [ ] Windows Application Packaging Projectとmanifestを追加する
- [ ] packaging-only revisionが合格済みimmutable application artifactを再buildせず参照し、outer identityを別追跡する
- [ ] manifestのidentity／version／x64／Windows.Desktop／entry point／mediumIL／full-trust／runFullTrust宣言を検証する
- [ ] local sideload certificate subjectとmanifest Publisherの一致を検証する
- [ ] MSIXを展開し、inner payload inventoryを実機検証済みpayloadと照合する

## Microsoft Store Submission

- [ ] local test certificateでsignし、sideload install／launch／uninstallする
- [ ] package install rootのread-only／working directory差を含む回帰を通す
- [ ] settings、diagnostics、録画出力、Legal表示、SteamVR登録をpackaged実行で通す
- [ ] Spout2、WASAPI、OpenVR、VRChat、HMDをpackaged実行で再検証する
- [ ] WACKを任意のlocal preflightとして扱い、実行した場合だけXMLの失敗／未実行testを拒否する
- [ ] MSIX最終payloadとLegal Bundle／SBOMを再scanする
- [ ] exact `.msixupload`に対するPartner Center certification成功を記録する
- [ ] flightで検証後、Store公開を許可する
