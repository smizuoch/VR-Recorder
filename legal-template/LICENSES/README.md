# License text provenance / License本文の出所

## 日本語

このフォルダーのfileはoffline Legal Bundleの形を示す設計時exampleです。production release前に、generatorは各本文とcomponent固有noticeを固定したsource/packageと照合し、SHA-256を保存します。

- `LGPL-2.1-or-later/LICENSE.txt`: GNU LGPL v2.1本文。個別componentが`or later`を許諾するかは配布する正確なsource/buildから別途確認します。
- `MIT/VRC-OSCQuery-LICENSE.txt`: VRC OSCQuery Libraryのlicense notice。
- `BSD-2-Clause/Spout2-LICENSE.txt`: Spout2のlicense notice。
- `BSD-3-Clause/OpenVR-LICENSE.txt`: OpenVRのlicense notice。
- `Apache-2.0/Material-Symbols-LICENSE.txt`: Material Symbols用Apache License 2.0全文。
- `Apache-2.0/Material-Symbols-ATTRIBUTION.txt`: 固定commit、asset manifest、NOTICE有無をrelease時に完成させる出所表示雛形。

同じSPDX identifierでもcomponent固有のcopyright／NOTICEを統合・削除しません。

## English

These files demonstrate the offline Legal Bundle shape. Before a production release, the generator must compare every license text and component-specific notice with the exact pinned source or package and store its SHA-256.

- `LGPL-2.1-or-later/LICENSE.txt`: GNU LGPL v2.1 text. The component-specific “or later” grant must be confirmed separately from the exact source and build being distributed.
- `MIT/VRC-OSCQuery-LICENSE.txt`: VRC OSCQuery Library license notice.
- `BSD-2-Clause/Spout2-LICENSE.txt`: Spout2 license notice.
- `BSD-3-Clause/OpenVR-LICENSE.txt`: OpenVR license notice.
- `Apache-2.0/Material-Symbols-LICENSE.txt`: Complete Apache License 2.0 text for Material Symbols.
- `Apache-2.0/Material-Symbols-ATTRIBUTION.txt`: Source-attribution template whose commit, asset manifest, and NOTICE status must be completed for the release.

Do not collapse or remove component-specific copyright or NOTICE information merely because multiple components share the same SPDX identifier.
