# Third-party registry / 第三者コンポーネント台帳

## 日本語

`registry.yml` は基本設計書 v0.3 §17の単一台帳を実装するproduction入力です。外部YAML parser自体を新たな未登録依存にしないため、現在のschemaはYAML 1.2でも有効なJSON互換profileに限定し、`System.Text.Json`で厳格に読み取ります。

現時点の登録対象はテスト基盤の直接・推移NuGet依存です。version、NuGet content hash、package archive SHA-256、上流repository commit、license結論候補、ローカルlicense全文とSHA-256を固定しています。`approval.status` はすべて `pending-independent-review` であり、release承認を意味しません。特に`xunit.abstractions`はNuGet metadataにlicense式とrepository commitがないため、Apache-2.0という結論候補を独立したreviewerが確認するまでrelease gateを通せません。

`RepositoryComplianceVerifier.VerifyCandidateInputs` は開発時のcandidate完全性だけを検査します。署名・公開には、別途、承認者、通知生成、内部依存、最終staging、SBOM、Legal Bundleの全release gateが必要です。

release用の`approval.status=approved`には、実在する`id`、`requestedBy`、独立した`reviewer`を同じentryへ記録します。`requestedBy`と`reviewer`が同一、欠落、またはplaceholderのentryから`ApprovedReleaseGraph`は発行されません。

## English

`registry.yml` is the production input for the canonical registry required by Basic Design v0.3 §17. To avoid introducing a YAML parser as another unregistered dependency, the current schema uses a JSON-compatible YAML 1.2 profile and is read strictly with `System.Text.Json`.

The current entries cover the direct and transitive NuGet dependencies of the test infrastructure. Versions, NuGet content hashes, package-archive SHA-256 values, upstream repository commits, candidate license conclusions, local full license texts, and their SHA-256 values are pinned. Every `approval.status` remains `pending-independent-review`; this is not release approval. In particular, `xunit.abstractions` has neither a license expression nor a repository commit in its NuGet metadata, so its Apache-2.0 candidate conclusion must be confirmed by an independent reviewer before it can pass the release gate.

`RepositoryComplianceVerifier.VerifyCandidateInputs` checks development-candidate completeness only. Signing or publication additionally requires reviewer approval, generated notices, embedded-dependency inventory, final-staging scans, an SBOM, and all Legal Bundle release gates.

An `approval.status` of `approved` for release also records a real `id`, `requestedBy`, and independent `reviewer` in the same entry. No `ApprovedReleaseGraph` is issued when those identities are missing, placeholders, or self-approved.
