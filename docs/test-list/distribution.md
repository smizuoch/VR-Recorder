# Windows distribution test list

## Hardware Validation Payload

- [x] promotion入力のentrypoint種別としてunpackaged `.exe`だけを許可する
- [x] Hardware Validation Payloadを一般公開可能と判定しない
- [x] MSIXをunpackaged実機検証targetとして受理しない
- [ ] self-contained `win-x64` publish directoryを再現可能に生成する
- [ ] existing root directoryとroot内normalized relative entrypointを読み取り、traversal／reparse／inventory非所属を拒否する
- [ ] publish directory全fileのpath／length／SHA-256／kindからcanonical inventory digestを生成する
- [x] strict `WindowsRuntimeStagingManifest` v1でsource／target、role、component、platform、deployment kind、SHA-256を読み、unknown／duplicate field、absolute／traversal／ADS記法／device name、Windows case-fold duplicate／親子衝突を拒否する
- [x] dedicated input rootのmissing／extra file、hash、kind、reparse point、ApprovedGraph owner／runtime scope、native registryをfail-closed検査する
- [x] factory-selection evidenceのproduction 4 family、binary filename／length／SHA-256、intent marker、evidence SHA-256をactual staged input bytesと照合する
- [x] sibling temporary directoryへCreateNew copy・再hash・length／kind／exact inventory検証し、mid-copy／tamper／extra／commit／cancellation失敗時に既存immutable payloadを維持する
- [x] stagerが決定的`ApprovedWindowsRuntime.props`を生成し、Release Appは明示されたpropsだけをimportしてsource／target一覧を配置する
- [x] Releaseで`NativeMediaLibraryPath`／`FfprobeExecutablePath`／`FfmpegRuntimeDirectory`の直接指定を拒否する
- [ ] manifest v2のfull-production profile／RID／declared length／Legal anchorでfirst-party native、FFmpeg 4 DLL、ffprobe、OpenVR runtime／application manifest／action manifest／bindings、Spout／encoder runtimeのrequired closureとruntime majorを固定する
- [ ] canonical repository evidenceからApprovedGraphを発行するbuilder、external staging CLI、two-invocation publish scriptを実装し、手書きprops／別digest directory差替えを拒否する
- [ ] DLL／EXEを実PE bytesとしてparseし、PE32+／AMD64 machine／subsystem／entrypoint／import closureを検証して拡張子だけのkind判定をrelease gateにしない
- [ ] authenticated Legal Bundle admission、ambient PATH排除、手動copy回避、publish後の全managed／native／self-contained .NET／asset／Legal inventory sealerを通す
- [ ] Windows上でalternate data stream／reparse HILを実行し、portable injection testだけで合格にしない
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
- [ ] packaging-only revisionが合格済みimmutable application artifactを再buildせず参照し、App `ProjectReference`等の再build経路を拒否してouter identityを別追跡する
- [ ] manifestのidentity／version／x64／Windows.Desktop／entry point／mediumIL／full-trust／runFullTrust宣言を検証する
- [ ] local sideload certificate subjectとmanifest Publisherの一致を検証する
- [ ] MSIXを展開し、inner payload inventoryを実機検証済みpayloadと照合する

## Microsoft Store Submission

- [ ] local test certificateでsignし、sideload install／launch／uninstallする
- [ ] package install rootのread-only／working directory差を含む回帰を通す
- [ ] settings、diagnostics、録画出力、Legal表示、SteamVR登録をpackaged実行で通す
- [ ] Spout2、WASAPI、OpenVR、VRChat、HMDをpackaged実行で再検証する
- [ ] latest available WACKをlocal実行し、XMLの失敗／未実行／inapplicable testをparseする。tool非対応時はversion／理由付きwaiverとflight証拠を必須にする
- [ ] MSIX最終payloadとLegal Bundle／SBOMを再scanする
- [ ] exact `.msixupload`に対するPartner Center certification成功を記録する
- [ ] flightで許容されたWACK failureも一般公開前に解消し、検証後だけStore公開を許可する
