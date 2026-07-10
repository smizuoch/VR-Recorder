# Legal artifact test list / 法務成果物テストリスト

## 日本語

基本設計書 v0.3 §17、§18.4、§24に従い、単一台帳から通知、license payload、SPDX SBOM、integrity manifestを決定論的に生成します。release生成は独立承認済みcomponentだけを受け入れます。

- [x] direct NuGet追加を完全なmetadata／license全文とともにNoticesへ自動追記する
- [x] transitive NuGet追加もNoticesとSPDX SBOMへ自動追記する
- [x] final stagingの未登録native DLLがpackage生成を停止する
- [x] MIT componentのcopyright notice欠落が生成を停止する
- [x] 未承認componentが1件でもあればrelease artifact生成を拒否する
- [x] 同一入力からbyte-for-byte同一の成果物を生成する
- [x] 生成済みNoticesの手編集を再生成差分で検出する
- [x] SBOMのUNKNOWN／NOASSERTION／NONEを拒否する
- [x] 全payload fileを列挙する決定論的な`LEGAL-MANIFEST.sha256`を生成する
- [x] 外部resource／JavaScriptなしで目次・検索案内・license全文を持つHTML Noticesを生成する

## English

Following Basic Design v0.3 §§17, 18.4, and 24, notices, license payloads, the SPDX SBOM, and the integrity manifest are generated deterministically from the canonical registry. Release generation accepts only independently approved components.

- [x] Add a direct NuGet dependency to Notices with complete metadata and full license text
- [x] Add a transitive NuGet dependency to both Notices and the SPDX SBOM
- [x] Block package generation for an unregistered native DLL in final staging
- [x] Block generation when an MIT component lacks its copyright notice
- [x] Reject release-artifact generation when any component is unapproved
- [x] Produce byte-for-byte identical artifacts from identical inputs
- [x] Detect manual edits to generated Notices through regeneration diff
- [x] Reject UNKNOWN, NOASSERTION, and NONE in the SBOM
- [x] Generate a deterministic `LEGAL-MANIFEST.sha256` covering every payload file
- [x] Generate HTML Notices with contents, search guidance, and full license text without external resources or JavaScript
