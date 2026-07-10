# VR-Recorder Legal Bundle template / Legal Bundle設計テンプレート

## 日本語

このフォルダーは、基本設計書 v0.3 の第三者ライセンス・権利保護要件を実装へ移すための**設計時雛形**です。現時点のファイルはrelease通知ではありません。実際の配布物では、固定したversion/commit、最終publish output、完全なtransitive dependency、実link/load情報、Material Symbols allowlist、権利台帳から再生成してください。

### 同梱物

- `third-party-registry.example.yml`: component単位の正規台帳
- `license-policy.example.yml`: fail-closed採用policy
- `rights-ledger.example.yml`: code以外のasset・商標・許諾証跡
- `runtime-load-manifest.example.yml`: delay-load／LoadLibrary対象
- `AUTOMATION-CONTRACT.md`: 自動検出・自動追記・M3／icon CI停止条件
- `THIRD-PARTY-NOTICES.example.txt`: LGPL／MIT／BSD／Apache-2.0全文を含む出力例
- `THIRD-PARTY-COMPONENTS.v2.example.json`: hash cycleを避けたSteamVR／desktop UI用component manifest v2例
- `THIRD-PARTY-COMPONENTS.example.json`: 後方参照用のschema-v1 design-time例（release不可）
- `MATERIAL-SYMBOLS-MANIFEST.example.json`: 使用iconと出所・hash・変換情報のruntime出力例
- `M3-SOURCE-INVENTORY.example.json`: 公式M3 navigation全項目、分類coverage、差分を記録するruntime／Legal UI向け出力例
- `M3-CONFORMANCE-REPORT.example.json`: M3／accessibility／XR release gateの出力例
- `schemas/`: UI用manifest schema
- `LICENSES/`: 初期候補componentのlicense全文
- `SOURCE-OFFERS/`: FFmpeg対応source情報のrelease雛形
- `RIGHTS/`: asset attribution雛形
- `SBOM/`: SPDX出力先

`<...>` placeholderはrelease前に全て機械置換し、残存時はCIを失敗させます。Material Symbolsはofficial repositoryの固定commit、使用icon一覧、source/output SHA-256、改変情報、Apache-2.0全文、NOTICE有無を記録します。runtimeでGoogle Fonts CDNへ接続しません。

生成した同一bundleをinstall folder、`%LOCALAPPDATA%` cache、録画先の`VR-Recorder-Legal`、SteamVR手首UI、desktop UIへ配置します。各経路のbundle IDとSHA-256が異なる場合はreleaseまたは実行時検証を失敗させます。

schema v2ではcomponent catalogが`LEGAL-MANIFEST.sha256`のpathとalgorithmだけを参照し、manifestはcomponent catalogを含む全payloadをhashします。manifest自体のSHA-256は署名済みapplication resourceまたは認証済みrelease metadataでout-of-bandに保持します。

技術的な検査は「未確認のものを出荷しない」ための制御です。特許、商標、録画対象の著作権・肖像・音声・privacy、地域別規制、最終的なlicense解釈は各releaseで専門家reviewを通します。

## English

This folder is a **design-time template** for implementing the third-party licensing and rights-protection requirements in Basic Design v0.3. These files are not production release notices. A real release must regenerate them from pinned versions/commits, the final publish output, the complete transitive dependency graph, actual link/load data, the Material Symbols allowlist, and the approved rights ledger.

### Contents

- `third-party-registry.example.yml`: Canonical component registry
- `license-policy.example.yml`: Fail-closed adoption policy
- `rights-ledger.example.yml`: Non-code assets, trademarks, and permission evidence
- `runtime-load-manifest.example.yml`: Delay-load and LoadLibrary targets
- `AUTOMATION-CONTRACT.md`: Automatic discovery, notice generation, and M3/icon CI gates
- `THIRD-PARTY-NOTICES.example.txt`: Example output with complete LGPL, MIT, BSD, and Apache-2.0 texts
- `THIRD-PARTY-COMPONENTS.v2.example.json`: Cycle-free schema-v2 component manifest for the SteamVR and desktop legal UI
- `THIRD-PARTY-COMPONENTS.example.json`: Legacy schema-v1 design-time example for reference only; not release-valid
- `MATERIAL-SYMBOLS-MANIFEST.example.json`: Runtime-output example for selected icons, provenance, hashes, and conversion data
- `M3-SOURCE-INVENTORY.example.json`: Runtime and Legal UI output example for all official M3 navigation entries, classification coverage, and snapshot differences
- `M3-CONFORMANCE-REPORT.example.json`: Output example for the M3, accessibility, and XR release gate
- `schemas/`: Runtime-manifest schemas
- `LICENSES/`: Complete license texts for initial candidate components
- `SOURCE-OFFERS/`: Release templates for FFmpeg corresponding-source information
- `RIGHTS/`: Asset-attribution templates
- `SBOM/`: SPDX output directory

Every `<...>` placeholder must be replaced before release; CI fails if any remains. Material Symbols must record the pinned official commit, selected-icon inventory, source/output SHA-256 values, modification information, the complete Apache-2.0 text, and upstream NOTICE status. The application must not contact the Google Fonts CDN at runtime.

The same generated bundle is placed in the installation folder, the `%LOCALAPPDATA%` repair cache, the versioned `VR-Recorder-Legal` recording folder, the SteamVR wrist UI, and the desktop UI. Any bundle-ID or SHA-256 mismatch fails release or runtime verification.

In schema v2, the component catalog references only the path and algorithm of `LEGAL-MANIFEST.sha256`, and the manifest hashes every payload including the catalog. The SHA-256 of the manifest itself is held out-of-band in a signed application resource or authenticated release metadata.

Technical checks enforce “do not ship what has not been verified.” Patents, trademarks, rights in recorded content, voice and likeness rights, privacy, regional rules, and final license interpretation still require recorded expert review for every release.
