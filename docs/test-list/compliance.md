# Compliance test list / コンプライアンステストリスト

## 日本語

基本設計書 v0.3 §17、§18.4、§24から導出したfail-closed法務ゲートのテストリストです。

- [x] 未登録の推移NuGet依存を拒否する
- [x] 登録versionとlock fileのversion差分を拒否する
- [ ] UNKNOWN／NOASSERTION／NONE licenseを拒否する
- [ ] license全文またはcopyright表示の欠落を拒否する
- [ ] license fileのSHA-256不一致を拒否する
- [ ] lock fileの直接・推移依存が全件登録済みなら成功する

## English

This fail-closed legal-gate test list is derived from Basic Design v0.3 §§17, 18.4, and 24.

- [x] Reject an unregistered transitive NuGet dependency
- [x] Reject a version mismatch between the registry and lock file
- [ ] Reject UNKNOWN, NOASSERTION, and NONE licenses
- [ ] Reject a missing full license text or copyright notice
- [ ] Reject a license-file SHA-256 mismatch
- [ ] Accept a lock file only when every direct and transitive dependency is registered
