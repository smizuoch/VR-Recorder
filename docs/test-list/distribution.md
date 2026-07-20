# Windows distribution test list

## Hardware Validation Payload

- [x] promotion入力のentrypoint種別としてunpackaged `.exe`だけを許可する
- [x] Hardware Validation Payloadを一般公開可能と判定しない
- [x] MSIXをunpackaged実機検証targetとして受理しない
- [x] self-contained `win-x64` publish directoryを再現可能に生成する
- [x] existing root directoryとroot内normalized relative entrypointを読み取り、traversal／reparse／inventory非所属を拒否する
- [x] publish directory全fileのpath／length／SHA-256／kindからcanonical inventory digestを生成する
- [x] strict `WindowsRuntimeStagingManifest` v2でprofile／RID／Legal anchor／source／target、role、component、platform、deployment kind、length／SHA-256を読み、unknown／duplicate field、absolute／traversal／ADS記法／device name、Windows case-fold duplicate／親子衝突を拒否する
- [x] dedicated input rootのmissing／extra file、hash、kind、reparse point、ApprovedGraph owner／runtime scope、native registryをfail-closed検査する
- [x] factory-selection evidenceのproduction 4 family、binary filename／length／SHA-256、intent marker、evidence SHA-256をactual staged input bytesと照合する
- [x] sibling temporary directoryへCreateNew copy・再hash・length／kind／exact inventory検証し、mid-copy／tamper／extra／commit／cancellation失敗時に既存immutable payloadを維持する
- [x] stagerが決定的`ApprovedWindowsRuntime.props`を生成し、Release Appは明示されたpropsだけをimportしてsource／target一覧を配置する
- [x] Releaseで`NativeMediaLibraryPath`／`FfprobeExecutablePath`／`FfmpegRuntimeDirectory`／`OpenVrRuntimeLibraryPath`／`MsvcRuntimeDirectory`の直接指定を拒否する
- [x] manifest v2のfull-production profile／RID／declared length／Legal anchorでfirst-party native、FFmpeg 4 DLL、`libvpl.dll`、ffprobe、OpenVR runtime／application manifest／action manifest／bindings、app-local MSVC CRT 4 DLL、Spout／encoder runtimeのrequired closureとruntime majorを固定する
- [x] canonical repository evidenceからApprovedGraphを発行するbuilder、external staging CLI、two-invocation publish scriptを実装し、手書きprops／別digest directory差替えを拒否する
- [x] DLL／EXEを実PE bytesとしてparseし、PE32+／AMD64 machine／subsystem／entrypoint／import closureを検証して拡張子だけのkind判定をrelease gateにしない
- [x] app-local MSVC CRTをMicrosoft署名済みVC143 x64 package、公式redistribution list／Runtime license、実4 DLLのlength／SHA-256へ結合したpending candidateとして固定し、developmentだけ許可してRelease admissionを閉じる
- [ ] FFmpeg／JsonSchema.Net／Spout2／OpenVR／libvpl／app-local MSVC CRTを独立reviewし、実在するticket／requester／reviewer付きでcanonical registryをApprovedGraphへadmitする
- [ ] ApprovedGraphから認証済みLegal Bundle／`LEGAL-MANIFEST.sha256`を生成し、clean canonical application source revisionを確定する
- [ ] authenticated Legal Bundle admission、ambient PATH排除、手動copy回避、publish後の全managed／native／self-contained .NET／asset／Legal inventory sealerを通す
- [ ] Windows上でalternate data stream／reparse HILを実行し、portable injection testだけで合格にしない
- [x] Legal Bundle ID／manifest hashをpost-publish payload identityへ取り込み、認証失敗／identity差替え時はidentityを発行しない
- [ ] 実Windows／GPU／VRChat／SteamVR／HMD試験reportをschema検証してpayload identityへ結び付ける
- [x] 必須matrix caseの完全性と結果から合否を導出し、自己申告`Passed`を入力にしない
- [x] application source／DLL／asset／manifest／Legal変更でinner identityが変わる場合に以前の実機証拠を無効化する

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
- [x] Windows Application Packaging Projectとmanifestを追加する
- [x] packaging-only revisionが合格済みimmutable application artifactを再buildせず参照し、App `ProjectReference`等の再build経路を拒否してouter identityを別追跡する
- [x] manifestのidentity／version／x64／Windows.Desktop／entry point／mediumIL／full-trust／runFullTrust宣言を検証する
- [x] local sideload certificate subjectとmanifest Publisherをordinal完全一致で検証し、package hash／証明書thumbprint／SignTool versionへ結合する
- [x] MSIXを展開し、inner payload inventoryを実機検証済みpayloadと照合する

## Microsoft Store Submission

- [x] ephemeral local certificate、SignTool verify、sideload lifecycle、packaged UIA、WACK、Defender／Legal／SBOM scanを一時秘密鍵非公開のself-hosted workflowとして実装する
- [x] packaged実機再試験reportをexact MSIX hash、必須8 case、OS／GPU／driver／SteamVR／HMD、各artifact SHA-256へ結合し、missing／failed／tamperを拒否する
- [x] WACK XMLのoverall／nested resultをparseしてfail／not-run／inapplicableを拒否し、tool非対応waiverにはversion／理由／独立承認／合格済みPartner Center flightを必須にする
- [x] exact MSIX、Legal manifest、SPDX SBOM、private-key混入、Defender package／expanded tree scanを同じpreflight gateへ結合する
- [x] exact package、certification report、flight reportのSHA-256をPartner Center evidenceへ結合し、全preflight合格後だけpublic-release gateを許可する
- [ ] local test certificateでsignし、sideload install／launch／uninstallする
- [ ] package install rootのread-only／working directory差を含む回帰を通す
- [ ] settings、diagnostics、録画出力、Legal表示、SteamVR登録をpackaged実行で通す
- [ ] Spout2、WASAPI、OpenVR、VRChat、HMDをpackaged実行で再検証する
- [ ] latest available WACKをlocal実行し、XMLの失敗／未実行／inapplicable testをparseする。tool非対応時はversion／理由付きwaiverとflight証拠を必須にする
- [ ] MSIX最終payloadとLegal Bundle／SBOMを再scanする
- [ ] exact `.msixupload`に対するPartner Center certification成功を記録する
- [ ] flightで許容されたWACK failureも一般公開前に解消し、検証後だけStore公開を許可する
