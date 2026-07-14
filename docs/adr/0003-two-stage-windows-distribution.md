# ADR-0003: Windows配布は実機検証payloadからMicrosoft Store MSIXへ昇格する

- Status: Accepted
- Date: 2026-07-13
- Last updated: 2026-07-14

## Context

VR-RecorderはWPF／Win32、D3D11、Spout2、WASAPI、FFmpeg、OpenVRを使う。
production backendの成立性はWindows、GPU、VRChat、SteamVR、HMD上での実行によって確認する必要がある。一方、MSIX化するとinstall rootのread-only化、working directory、AppData／registryの扱い、package identity、full-trust宣言がunpackaged実行と異なる。

MicrosoftのWPF／Win32向け手順では、Windows Application Packaging Projectをdesktop appとは別に追加してMSIXまたはStore向け`.msixupload`を生成する。Store package manifestにはPartner Centerが割り当てる`Identity/Name`、`Identity/Publisher`、`PublisherDisplayName`が必要である。

## Decision

Windows配布を次の3段階に分ける。

### 1. Hardware Validation Payload

- 最初のproduction実機検証は、unpackaged、self-contained、`win-x64`のpublish directoryで行う。
- canonical root directory内の起動entry pointはnormalized relative path `VRRecorder.App.exe`とするが、検証対象はEXE単体ではなく、native DLL、ffprobe、.NET runtime、Legal Bundle、assetを含むdirectory全体とする。entrypointは実inventoryに含まれるregular fileでなければならない。
- このpayloadは内部実機検証専用であり、その合格だけでは一般公開、Microsoft Store提出、release適格を意味しない。
- payload identityに次を固定する。
  - product version
  - source revision
  - runtime identifier
  - application EXE SHA-256
  - path／length／SHA-256／kindを正規化した全payload inventory digest
  - Legal Bundle ID
  - Legal manifest SHA-256
- 実機検証reportはpayload identityへ結び付ける。application payloadを生成するsource revision、version、inventory bytes、Legal Bundleのいずれかが変われば再検証する。
- reportの合否は、versioned schema readerが必須matrix caseの完全性、全case成功、artifact hash、payload identity一致から導出する。呼出し側の自己申告`Passed`はrelease入力にしない。

### 2. Microsoft Store Packaging Candidate

- Hardware Validation Payloadが合格した後にだけMSIX候補を作る。
- WPF app本体へMSIX設定を混在させず、Windows Application Packaging Projectを別projectとして追加する。
- Partner Centerから取得したName、Publisher、PublisherDisplayNameをmanifestへ使用する。placeholder identityをrelease入力にしない。
- 合格済みapplication directoryをimmutable artifactとして入力し、packaging project側のrevisionからapplication payloadを再buildしない。packaging revision／manifest／outer package hashはapplication payload identityとは別に追跡する。
- MSIX展開後のinner application payloadをinventoryし、Hardware Validation Payloadと一致することを確認する。
- MSIXを作成できたことだけでは`PublishEligible`にしない。

### 3. Microsoft Store Submission

Store提出にはPackaging Candidateに加えて次をすべて要求する。

- manifestのidentity、version、architecture、entry point、full-trust宣言のmachine validation
- local test certificateでのsideload install／launch／uninstall検証
- package install rootからの起動、異なるworking directory、read-only package files、settings／log／録画出力、SteamVR manifest登録を含むpackaged固有の回帰
- Spout2、WASAPI、OpenVR、VRChat、HMDを使う実機再試験
- Windows App Certification Kitは任意のlocal preflightとして扱う。実行した場合はreportをparseし、fail／not-runを見過ごさない
- exact uploadに対するPartner Center certification成功とprivate flight検証
- 最終payload、Legal Bundle、SBOMの再scan

Microsoft Store提出用packageの本番署名はStore側へ委ねる。local sideload用秘密鍵をrepositoryやrelease artifactへ含めない。

## TDD sequence

1. Hardware Validation Payloadが常に非公開であることをRedにする。
2. 実機証拠なしのMSIX候補を拒否する。
3. root／relative entrypoint／inventory membership、version、application revision、EXE、全payload inventory、Legal anchorの各不一致を個別のRedにする。
4. Partner Center identity欠落を拒否する。
5. MSIX manifest readerと展開payload照合をRedにする。
6. sideload lifecycle、packaged固有回帰、任意WACK report parserをRedにする。
7. exact uploadとPartner Center certification結果の紐付けをRedにする。
8. 全gateがGreenになったときだけStore submissionを許可する。

## Consequences

- production backendのデバッグは単純なunpackaged実行で先行できる。
- 実機で確認したpayloadとStoreへ包むpayloadの同一性を追跡できる。
- packaging-only commitは凍結済みinner artifactを消費できるが、inner payloadを再build／変更した時点でHardware Validationをやり直す。
- EXE実機合格はMSIX固有挙動やStore認証を代替しないため、MSIX化後の再試験が必要になる。
- 現在のdeterministic ZIPはHardware Validation Payloadの移送候補として扱えるが、MSIXまたは公開releaseとは呼ばない。

## Sources

- Microsoft, Choose a distribution path for your Windows app: <https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/choose-distribution-path>
- Microsoft, Set up your desktop application for MSIX packaging in Visual Studio: <https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-packaging-dot-net>
- Microsoft, View product identity details: <https://learn.microsoft.com/en-us/windows/apps/publish/view-app-identity-details>
- Microsoft, Understanding how packaged desktop apps run on Windows: <https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-behind-the-scenes>
- Microsoft, Package an app using Visual Studio（WACKのdeprecated／optional preflight説明を含む）: <https://learn.microsoft.com/en-us/windows/msix/package/packaging-uwp-apps>
