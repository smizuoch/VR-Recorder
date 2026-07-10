# SBOM output directory / SBOM出力ディレクトリ

## 日本語

Release時に`manifest.spdx.json`を生成します。正規出力はSPDX JSONで、最終staging directory、第三者registry、rights ledger、link/import inventory、Material Symbols asset manifestと照合します。

`UNKNOWN`、`NOASSERTION`、`NONE`、未登録file、notice差分、schema違反、Material Symbols hash不一致が1件でもある場合は署名・公開を停止します。このexampleには架空の完全SBOMを置かず、実buildの検出結果だけを生成対象とします。

## English

Generate `manifest.spdx.json` at release time. SPDX JSON is the canonical output and is reconciled with the final staging directory, third-party registry, rights ledger, link/import inventory, and Material Symbols asset manifest.

Signing and publication stop when any `UNKNOWN`, `NOASSERTION`, `NONE`, unregistered file, notice mismatch, schema violation, or Material Symbols hash mismatch remains. This example intentionally contains no fictional complete SBOM; only actual build-discovery results may populate it.
