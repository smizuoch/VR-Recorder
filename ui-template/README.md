# UI template / UIテンプレート

## 日本語

このフォルダーには、Material Symbolsの固定取得、Material Design 3適合、localizationを実装するための設計時exampleがあります。`<...>` placeholderはrelease前に全て固定値へ置換し、残存時はCIを失敗させます。

- `material-symbols-manifest.example.yml`: 使用icon、公式upstream commit/path/hash、改変、RTL、accessible labelのallowlist
- `design-tokens.example.json`: desktop／wrist共通のM3 semantic token契約。具体theme値はrelease時に生成・固定
- `m3-source-inventory.example.yml`: 公式M3 navigationの全項目を収集し、追加・削除・rename・未分類を検出する台帳
- `m3-conformance-profile.example.yml`: foundation／style／component／accessibility／XRの適用性とrelease gate
- `m3-conformance-matrix.example.yml`: source inventoryの全項目とprofile判定・evidenceを結ぶmachine-readable台帳
- `localization-contract.example.yml`: 日本語／英語、fallback、pseudo-locale、RTL、README言語の契約
- `locales/*.example.json`: 主要操作の日本語／英語resource catalog例

この設計packageにはMaterial SymbolsのSVGまたはfont binaryを含めません。実装時は公式repositoryの固定commitからallowlist分だけを取得し、Apache-2.0対応物を自動生成します。

## English

This folder contains design-time examples for pinned Material Symbols acquisition, Material Design 3 conformance, and localization. Every `<...>` placeholder must be replaced with a pinned release value; CI fails when any placeholder remains.

- `material-symbols-manifest.example.yml`: Allowlist for selected icons, official upstream commit/path/hash, modifications, RTL, and accessible labels
- `design-tokens.example.json`: Shared M3 semantic-token contract for desktop and wrist surfaces; concrete theme values are generated and pinned at release time
- `m3-source-inventory.example.yml`: Inventory that collects every official M3 navigation entry and detects additions, removals, renames, and unclassified entries
- `m3-conformance-profile.example.yml`: Applicability and release gates for foundations, styles, components, accessibility, and XR
- `m3-conformance-matrix.example.yml`: Machine-readable registry joining every source-inventory item to its profile decision and evidence
- `localization-contract.example.yml`: Japanese/English, fallback, pseudo-locale, RTL, and README-language contract
- `locales/*.example.json`: Japanese and English resource-catalog examples for primary operations

This design package contains no Material Symbols SVG or font binary. Implementation must acquire only allowlisted assets from a pinned official-repository commit and generate the Apache-2.0 compliance material automatically.
