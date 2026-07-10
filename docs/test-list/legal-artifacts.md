# Legal artifact test list / 法務成果物テストリスト

## 日本語

基本設計書 v0.3 §17、§18.4、§24に従い、単一台帳から通知、component manifest、SPDX SBOMを決定論的に生成します。release生成は独立承認済みcomponentだけを受け入れます。

- [x] direct NuGet追加を完全なmetadata／license全文とともにNoticesへ自動追記する
- [x] transitive NuGet追加もNoticesとSPDX SBOMへ自動追記する
- [ ] final stagingの未登録native DLLがpackage生成を停止する
- [ ] MIT componentのcopyright notice欠落が生成を停止する
- [ ] 未承認componentが1件でもあればrelease artifact生成を拒否する
- [ ] 同一入力からbyte-for-byte同一の成果物を生成する
- [ ] 生成済みNoticesの手編集を再生成差分で検出する
- [ ] SBOMのUNKNOWN／NOASSERTION／NONEを拒否する

## English

Following Basic Design v0.3 §§17, 18.4, and 24, notices, the component manifest, and the SPDX SBOM are generated deterministically from the canonical registry. Release generation accepts only independently approved components.

- [x] Add a direct NuGet dependency to Notices with complete metadata and full license text
- [x] Add a transitive NuGet dependency to both Notices and the SPDX SBOM
- [ ] Block package generation for an unregistered native DLL in final staging
- [ ] Block generation when an MIT component lacks its copyright notice
- [ ] Reject release-artifact generation when any component is unapproved
- [ ] Produce byte-for-byte identical artifacts from identical inputs
- [ ] Detect manual edits to generated Notices through regeneration diff
- [ ] Reject UNKNOWN, NOASSERTION, and NONE in the SBOM
