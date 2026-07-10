# VR-Recorder

[日本語](#日本語) | [English](#english)

---

## 日本語

### 概要

VR-Recorderは、VRChatのSpout2カメラ出力をWindows上で録画するためのソフトウェア設計です。SteamVRの手首UIまたは割り当てた1つのInput Actionから録画を開始・停止し、映像、デスクトップ音声、マイク音声をMP4へ保存します。

このリポジトリ一式は現時点では**実装前の基本設計とコンプライアンステンプレート**です。完成した実行ファイルではありません。

### 主な設計要件

- Windows 11 x64正式対応、Windows 10 22H2 x64互換対応
- VRChat Camera OSCとSpout2の自動ON/OFF
- 縦横・解像度の自動検出とアスペクト比を保った録画
- 30 fps初期値、将来30～120 fpsを選択可能
- デスクトップ音声＋マイクのmix、Mic OFF、全音声mute
- NVENC／AMF／QSV、利用不能時はsoftware encoderへfallback
- self timerと自動停止
- SteamVR手首UIのREC／STOP／Mic操作、経過時間、NO SIGNAL
- t-wada式TDD、結合テスト単独で行・分岐coverage 90%以上

### UI

- UI iconはGoogleの公式Material Symbolsを使用します。
- Material Symbolsは固定した公式upstream commitから必要な公式SVGだけを自己ホストし、runtimeでGoogle Fonts CDNへ接続しません。この設計package自体にはicon SVGやfont binaryを含めません。
- SVGを変換・最適化・atlas化する場合は改変assetとして、tool/version、recipe、変更表示、source/output hashを必須にします。
- Desktop UIとSteamVR手首UIは、公式Material Design 3 navigationの全項目をreleaseごとにsource inventoryへ取り込み、Applicable／NotApplicable／Deferredへ100%分類します。適用可能なtoken、component、state、accessibility、interaction、RTL、XR guidanceを検証し、未分類項目は出荷を停止します。
- 主要操作はicon-firstで、言語に依存しにくい形状、状態、数値を使用します。曖昧なicon-only controlにはlocalized tooltipとaccessible nameを必ず付けます。Overlay移動にはdrag以外の上下左右nudge、recenter、dock／pin操作も用意します。
- 初期UI languageは日本語と英語です。resource構造は追加言語、RTL、200% text expansionを受け入れられるようにします。READMEの説明は日本語と英語だけで管理します。license原文、API名、schema keyは原文を保持します。

### 第三者ライセンスと権利保護

- FFmpeg、OpenVR、Spout2、OSCQuery、Material Symbols、.NET runtime、全transitive dependencyを自動inventoryします。
- `THIRD-PARTY-NOTICES`、license全文、SPDX SBOM、source情報、asset attributionを単一registryから生成します。
- Material SymbolsはApache License 2.0として、固定commit、使用icon一覧、source／output hash、改変情報とともに収録します。
- 未登録component、未知license、本文欠落、hash不一致、未登録asset、Google／第三者logo、未解決M3 deviationが1件でもあるreleaseは署名・公開しません。
- Legal情報はSteamVR内、desktop UI、install folder、録画保存先の`VR-Recorder-Legal`からoffline閲覧できます。
- 本製品はGoogle、VRChat、Valveその他の第三者による公式製品、提携製品、承認製品ではありません。

### 文書

- [基本設計書 v0.3](VR-Recorder_基本設計書_v0.3.md)
- [Material Design 3 / Material Symbols適合設計](docs/UI-MATERIAL3-CONFORMANCE.md)
- [M3適用性マトリクス](docs/M3-APPLICABILITY-MATRIX.md)
- [第三者通知自動化契約](legal-template/AUTOMATION-CONTRACT.md)
- [Legal Bundleテンプレート](legal-template/README.md)
- [Material Symbols manifest例](ui-template/material-symbols-manifest.example.yml)
- [M3公式source inventory例](ui-template/m3-source-inventory.example.yml)
- [M3適合profile例](ui-template/m3-conformance-profile.example.yml)
- [M3全項目machine-readable matrix例](ui-template/m3-conformance-matrix.example.yml)
- [M3 design token契約例](ui-template/design-tokens.example.json)
- [Localization契約例](ui-template/localization-contract.example.yml)
- [検証報告](VALIDATION-REPORT.md)
- [公式参照先一覧](OFFICIAL-SOURCES.md)

### 状態

設計版0.3です。実装開始時には、外部API、OS support、license、brand guideline、M3 guidanceを再確認し、固定したrelease入力からLegal Bundleと適合reportを生成します。

---

## English

### Overview

VR-Recorder is a Windows software design for recording the VRChat Spout2 camera output. Recording can be started or stopped from a SteamVR wrist surface or one assigned SteamVR Input Action, and the video, desktop audio, and microphone audio are saved as MP4.

This package currently contains a **pre-implementation architecture and compliance template**. It is not a finished executable application.

### Main design requirements

- Official Windows 11 x64 support and Windows 10 22H2 x64 compatibility support
- Automatic control of VRChat Camera OSC and Spout2 streaming
- Automatic portrait/landscape and resolution detection with aspect-ratio-preserving recording
- 30 fps by default, with a future selectable range of 30–120 fps
- Desktop-audio and microphone mixing, microphone off, and mute-all modes
- NVENC, AMF, and QSV with automatic software-encoder fallback
- Self timer and automatic stop duration
- SteamVR wrist UI for REC, STOP, microphone, elapsed time, and NO SIGNAL
- t-wada-style TDD and at least 90% line and branch coverage from the integration-test suite alone

### UI

- UI icons use Google's official Material Symbols.
- Only selected official SVGs from a pinned upstream commit are self-hosted. The application does not contact the Google Fonts CDN at runtime. This design package contains no icon SVG or font binary.
- Any SVG conversion, optimization, or atlas generation is treated as a modified asset and requires the tool/version, recipe, prominent change notice, and source/output hashes.
- For every release, every entry discovered from the official Material Design 3 navigation is added to a source inventory and classified as Applicable, NotApplicable, or Deferred. Every applicable token, component, state, accessibility, interaction, RTL, and XR guideline is verified; unclassified entries block release.
- Primary operations are icon-first and use language-independent shapes, states, and numbers where practical. Every ambiguous icon-only control has a localized tooltip and accessible name. Overlay positioning also provides directional nudge, recenter, dock, and pin controls that do not require dragging.
- The initial UI languages are Japanese and English. The resource architecture supports additional locales, RTL layout, and 200% text expansion. README explanations are maintained only in Japanese and English; original license text, API names, and schema keys remain unchanged.

### Third-party licensing and rights protection

- FFmpeg, OpenVR, Spout2, OSCQuery, Material Symbols, the .NET runtime, and all transitive dependencies are inventoried automatically.
- `THIRD-PARTY-NOTICES`, complete license texts, an SPDX SBOM, source information, and asset attribution are generated from one registry.
- Material Symbols are recorded as an Apache License 2.0 component, including the pinned commit, selected-icon inventory, source/output hashes, and modification information.
- A release is not signed or published when any unregistered component, unknown license, missing license text, hash mismatch, unregistered asset, Google or third-party logo, or unresolved M3 deviation remains.
- Legal information is available offline in SteamVR, the desktop UI, the installation folder, and the `VR-Recorder-Legal` folder beside recordings.
- This product is not an official, affiliated, sponsored, or endorsed product of Google, VRChat, Valve, or any other third party.

### Documents

- [Basic design v0.3](VR-Recorder_基本設計書_v0.3.md)
- [Material Design 3 and Material Symbols conformance design](docs/UI-MATERIAL3-CONFORMANCE.md)
- [M3 applicability matrix](docs/M3-APPLICABILITY-MATRIX.md)
- [Third-party notice automation contract](legal-template/AUTOMATION-CONTRACT.md)
- [Legal Bundle template](legal-template/README.md)
- [Material Symbols manifest example](ui-template/material-symbols-manifest.example.yml)
- [M3 official-source inventory example](ui-template/m3-source-inventory.example.yml)
- [M3 conformance profile example](ui-template/m3-conformance-profile.example.yml)
- [Machine-readable matrix for every M3 item](ui-template/m3-conformance-matrix.example.yml)
- [M3 design-token contract example](ui-template/design-tokens.example.json)
- [Localization contract example](ui-template/localization-contract.example.yml)
- [Validation report](VALIDATION-REPORT.md)
- [Official source index](OFFICIAL-SOURCES.md)

### Status

Design version 0.3. At implementation start, external APIs, OS support, licenses, brand guidance, and M3 guidance must be revalidated, and the Legal Bundle and conformance report must be generated from pinned release inputs.
