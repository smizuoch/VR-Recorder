# Third-Party Notices自動追記機構 — 実装契約

## 目的

依存を「追加したが通知へ書き忘れた」という人手依存を排除します。自動追記は
自動承認ではありません。候補生成は自動、ライセンス判断は記録されたレビュー、
未知または不整合はfail-closedです。

## 入力

1. `packages.lock.json` と `project.assets.json`
2. NuGet package内の`.nuspec`、SPDX license expression、埋込みlicense file
3. CMake File API codemodel、package-manager lock/status、linker map
4. EXE/DLLのPE import table
5. `runtime-load-manifest.yml`
6. source-copy/header-only検査とcopyright header scan
7. `rights-ledger.yml`
8. installerへ渡す最終staging directory
9. `ui/material-symbols.yml`、固定upstream commit、icon source/output hash、M3 source inventory／conformance profile／design token contract
10. 承認済み`third-party-registry.yml`と`license-policy.yml`

## 正規化と生成

入力をPackage URL、version/commit、hash、scope、linkage、SPDX expressionで
正規化したComponent Graphへ統合し、次を同時生成します。

- `THIRD-PARTY-NOTICES.txt` / `.html`
- component別license全文
- `THIRD-PARTY-COMPONENTS.json`（SteamVR/desktop UI入力）
- SPDX SBOM
- LGPL等のsource情報、patch、build recipe参照
- asset attribution
- `MATERIAL-SYMBOLS-MANIFEST.json`
- `M3-SOURCE-INVENTORY.json`
- `M3-CONFORMANCE-REPORT.json`
- `LEGAL-MANIFEST.sha256`

`THIRD-PARTY-COMPONENTS.json` release出力はschema v3とし、`bundleId`をSPDX `documentNamespace`と一致させます。component固有copyrightと、manifest登録済みの`license`／`notice`／`copyright`／`attribution`／`asset-manifest`参照を生成します。licenseはcomponentごとにexactly one、同一path衝突は禁止し、`asset-manifest`は`MATERIAL-SYMBOLS-MANIFEST.json`だけを指します。catalog自体もmanifestのhash対象に含めます。manifest bytesの期待SHA-256は署名済みresourceまたは認証済みrelease metadataでout-of-bandに保持し、schema v1、v2、未知schemaはreleaseで拒否します。

## CIゲート

以下は警告ではなくエラーです。

- dependency graph、PE import、runtime-load manifest、staging inventoryのいずれかに未登録itemがある
- license expressionまたは全文が不明／欠落している
- component固有copyright noticeが欠落している
- policyで禁止または未承認のlicenseがある
- FFmpeg binary、commit、source archive、configure log、build recipeが対応しない
- `--enable-gpl`、`--enable-nonfree`または未承認external codecが有効である
- rights ledgerにないfont、image、audio、logo、shader、documentが同梱される
- Material Symbolsがofficial repositoryの固定commitに対応しない、allowlist外icon、hash不一致、改変表示欠落がある
- runtimeがGoogle Fonts CDNまたは未承認の外部font／icon endpointへ接続する
- Google logo、Google product icon、第三者logoが一般UI iconとして混入する
- M3 source inventoryが100%分類されていない、公式navigation差分が未review、またはtoken／component／contrast／target／accessible name／tooltip／drag代替／localization gateが失敗する
- M3適合reportに未解決deviation、unclassified entry、出荷機能に関するDeferredが残る
- recording semantic roleがerror roleへaliasされる、または正常な録画状態にerror roleを使用する
- generator実行後にrepository差分が残る
- notice、UI resource、install folder、録画先Legal Bundle、SBOMのbundle ID/hashが一致しない
- template placeholder（`<...>`）がrelease bundleに残る

## 表示と保存

同一の生成manifestからSteamVR手首UI、desktop UI、インストール先、
`<RecordingOutput>\VR-Recorder-Legal\<ProductVersion>\`を構築します。
手首UIでは一覧・詳細・license全文をオフラインで閲覧し、desktopまたはlicense
folderを開けます。録画中もSTOPは常に1操作で到達可能にします。Material SymbolsのApache-2.0、固定commit、使用icon一覧、改変情報も同じmanifestから表示します。

## Merge / Release条件

Security reviewerとlicense/rights reviewerを分離し、自己承認を禁止します。
生成差分と高リスク差分（license変更、static化、source-copy化、GPL/nonfree化、
新しいlogo/font/media）を承認したcommitだけを署名・公開できます。


## Material Symbols更新契約

Material Symbols更新は通常buildから分離した明示的PRでだけ行います。update toolはofficial repository、full commit、source path、license text、NOTICE有無、各source hashを固定し、allowlist分だけを変換します。変換後hashと変更表示を生成し、registry、rights ledger、notice、SBOM、UI catalogを同時更新します。自動更新botによる無審査mergeは禁止します。

RuntimeではGoogle Fonts API/CDNを使用しません。network integration testは`fonts.googleapis.com`、`fonts.gstatic.com`および未承認font/icon endpointへのrequestをrelease failureとします。

## Material Design 3 / localization gate

`M3SourceInventoryUpdater`は承認済みupdate PRで公式navigationのURL／title inventoryを再生成し、追加・削除・rename差分をreview対象にします。`M3ConformanceValidator`はsource inventory coverage 100%、unclassified 0、semantic token、recording/error role分離、component role、全interaction state、contrast、target geometry、drag代替、tooltip、accessible name、focus order、Japanese／English、pseudo-locale 200%、RTL、high contrast、Reduce Motionを検証します。READMEは日本語と英語のsection parityを検証し、未解決deviationが1件でもある場合はsigned packageを生成しません。
