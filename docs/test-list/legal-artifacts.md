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
- [x] 生成対象外のfileがLegal Bundle directoryに残っていれば検証を失敗させる
- [x] Legal Bundleを同一volumeのstagingから置換し、失敗時は既存bundleを保持する
- [x] hash cycleのないstrict schema v3 component catalogをmanifest対象として生成する
- [x] schema v3文書参照とout-of-band trust boundaryをtemplate・ADRで固定する
- [x] 認証済みout-of-band digestからmanifestと全payload hashを実行時検証する
- [x] 重複catalog property・未登録file・改ざん・trust anchor欠落をfail-closedで拒否する
- [x] runtime改ざんをComplianceFaultへ写像しREC actionを禁止する
- [x] Release buildでbundle ID・manifest digest・payloadを必須化しdigestを照合する
- [x] 検証済みstagingからbyte-identicalなZIPを一時file経由で安全に確定する
- [x] 選択した録画保存先へ、lifecycle／OSC／camera開始前に認証済みversion付きLegal Bundleをミラーする
- [x] Legal Bundleのミラー失敗・取消では録画開始とcamera writeを行わない
- [x] install rootからはmanifest認証済みLegal fileだけをミラーし、同居するEXE／DLL等をコピーしない
- [x] `CURRENT.txt`、`OPEN-NOTICES.html`、過去version、atomic置換、symlink拒否を保存先連携後も維持する

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
- [x] Fail Legal Bundle directory verification when any unexpected file remains
- [x] Replace a Legal Bundle from same-volume staging while preserving the existing bundle on failure
- [x] Generate a manifest-covered strict schema-v3 component catalog without a hash cycle
- [x] Pin schema-v3 document references and its out-of-band trust boundary in the template and ADR
- [x] Verify the manifest and every payload hash at runtime from an authenticated out-of-band digest
- [x] Fail closed on duplicate catalog properties, unlisted files, tampering, or a missing trust anchor
- [x] Map runtime tampering to ComplianceFault and suppress the REC action
- [x] Require the bundle ID, manifest digest, and payload for Release builds and compare the digest
- [x] Publish a byte-identical ZIP from verified staging through a fail-safe temporary file
- [x] Mirror the authenticated, versioned Legal Bundle to the selected recording output before lifecycle, OSC, or camera startup
- [x] Do not start recording or write camera state when Legal Bundle mirroring fails or is cancelled
- [x] Mirror only manifest-authenticated Legal files from the install root, never colocated EXE, DLL, or other application payloads
- [x] Preserve `CURRENT.txt`, `OPEN-NOTICES.html`, prior versions, atomic replacement, and symlink rejection after output integration
