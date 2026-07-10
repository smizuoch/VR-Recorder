# VR-Recorder Material Design 3 / Material Symbols 適合設計

- 文書版: 1.2
- 対象設計: VR-Recorder基本設計書 v0.3
- 仕様確認日: 2026-07-10
- 対象surface: WPF desktop、SteamVR/OpenVR wrist overlay
- M3 source inventory: `ui-template/m3-source-inventory.example.yml`
- 適合profile: `ui-template/m3-conformance-profile.example.yml`
- design token契約: `ui-template/design-tokens.example.json`
- icon台帳: `ui-template/material-symbols-manifest.example.yml`

## 1. 適合の意味

VR-Recorderは、release時点の公式Material Design 3（M3）navigationから発見した全foundation、style、component、XR項目をversion付きsource inventoryへ取り込み、100%を適用性分類する。そのうち製品へ適用可能な全screen、component、stateをaccessibility、interaction、adaptive layout、bidirectionality、motion、XR guidanceへ対応付ける。

「すべてに準拠」は、M3サイトの全componentを製品へ無理に配置することではない。release時に固定したM3 catalogの各項目を、必ず次のいずれかへ分類することを意味する。

1. **Applicable**: 製品へ適用し、design evidence、実装evidence、automated testまたはmanual XR reviewを記録する。
2. **NotApplicable**: 製品に該当しない理由、代替手段、必要なADRを記録する。
3. **Deferred**: 将来適用するownerとtarget releaseを記録する。ただし出荷機能に関係するDeferredは0件とする。

未分類、根拠のない非適用、未検証の適用項目、出荷機能に関するDeferred、未解決deviationが1件でもあれば署名・公開しない。M3サイトは更新され得るため、承認済み更新PRでreleaseごとにURL／title inventoryとhashを固定し、前回snapshotとの差分をreviewする。

`M3 conformance`はGoogleによる認証、承認、提携または公式性を意味しない。Google製品UI、logo、brand color、product icon、M3サイトの画面・文章・design kitを製品assetとして複製せず、公開された設計原則をVR-Recorder独自UIへ実装する。公式navigationに追加・削除・renameがあった場合は、profileを再分類するまで通常releaseを停止する。

## 2. Material Symbolsの採用

### 2.1 採用範囲

- Icon set: **Material Symbols**
- 標準style: **Rounded**
- 候補確認: `fonts.google.com/icons`
- 正規取得元: `google/material-design-icons` official repository
- License: **Apache-2.0**
- 初期releaseの配布形態: allowlistした公式SVGだけを自己ホスト
- Runtime network: 禁止
- Material Symbols font binary: 初期releaseでは同梱しない
- 第三者npm mirror: 禁止

`fonts.google.com/icons`は検索・意味確認に使う。実際のrelease inputは、review済みの公式repositoryの完全commit、source path、SHA-256で固定する。ブラウザーから都度取得した「latest」を直接buildへ使用しない。

### 2.2 標準取得フロー

1. `fonts.google.com/icons`で意味と視認性を確認する。
2. 公式repositoryの完全commitを固定する。
3. `material-symbols-manifest.yml`へsemantic ID、upstream name、codepoint、style、source path、source SHA-256、用途、surface、RTL挙動、resource keyを登録する。
4. allowlist分の公式SVGだけを取得する。
5. 原則として公式SVGをbyte-identicalに保存し、source/output SHA-256が一致することを検証する。
6. WPF Path、最適化SVG、texture atlas、raster image等へ変換する場合は、Apache-2.0上のmodified artifactとして `modified: true`、変更表示、tool/version、再現可能recipe、source/output hashを必須にする。
7. Apache-2.0全文、上流のcopyright／attribution、NOTICE有無、使用icon一覧、固定commit、改変情報をLegal Bundleへ自動生成する。
8. registry、rights ledger、SBOM、THIRD-PARTY-NOTICES、SteamVR Legal UIの入力を同時更新する。
9. license/rights reviewer承認前はmerge、署名、公開しない。

### 2.3 Apache-2.0対応

- Apache License 2.0全文をオフライン同梱する。
- 上流のcopyright、license、attribution noticeを保持する。
- 上流にNOTICEが存在するreleaseでは、必要なNOTICE内容を可読な形で同梱する。
- 改変したfileには、変更したことが分かる明瞭なnoticeを付ける。
- Google商標の許諾がApache-2.0から与えられるとは扱わない。
- `Material Symbols (Material Design icons by Google)`という表示は出所説明に限定し、Googleによる承認を示さない。
- アイコンをVR-Recorderの独占的なlogoまたは商標として登録・主張しない。

### 2.4 禁止事項

- Google Fonts API/CDNからのruntime download
- unpinned URL、可変branch、非公式mirrorからの取得
- Google logo、Google G、Google product icon、第三者logoの採用
- upstream ligature名をaccessible nameや表示文言として露出すること
- license text、upstream notice、modified-file noticeの除去
- hash不一致のassetを「見た目が同じ」という理由で許可すること
- Googleとの提携、認定、承認、公式性を示す表現

## 3. Material Symbols semantic catalog

`material-symbols-manifest.example.yml`を唯一のallowlistとする。UI codeはupstream名を直接参照せず、`recording.start`、`audio.microphone.off`、`legal.license`等のsemantic IDだけを参照する。

この設計版では、録画、停止、マイク、全音声mute、self timer、auto-stop、NO SIGNAL、移動、wrist dock／world pin、設定、Legal、folder、document、language、navigation、close、retry、desktop open、warning、success確認に必要な公式symbolを登録済みである。縦横は入力解像度の数値とportrait／landscapeのlocalized状態名で示し、専用iconへ意味を依存しない。画面設計で参照するiconがmanifestにない状態はCI errorとする。

方向を持つnavigation iconだけをRTLでmirrorする。録画、停止、マイク、警告、著作権等の非方向iconはmirrorしない。

## 4. M3 design token contract

`design-tokens.example.json`は、desktopとwristで共有する意味の単一情報源である。presentation codeでcolor、font size、corner radius、spacing、shadow、duration等を直接記述しない。

| Token category | Contract |
|---|---|
| Color | M3 semantic color roleと承認済みcustom color groupを使用し、light、dark、Windows high contrastを生成・検証する。録画には`recording` group、実障害には`error` groupを使用し、aliasを禁止する |
| Typography | M3 type scaleへproject roleを対応付ける。Windows system fontを参照し、font fileを同梱しない |
| Shape | M3 shape roleだけを使用し、corner radius literalを禁止する |
| Spacing | 4 dp gridのproject tokenを使用する。XRではtarget間隔を安全側へ拡大できる |
| Elevation | interactionとspatial hierarchyの説明にだけ使う |
| Motion | state理解に必要なtransitionだけを使用し、Reduce Motionで除去または短いfadeへ変える |
| State | enabled、hovered、focused、pressed、disabled、selected、dragging、loading、recording、faultを対象componentごとに定義する |
| Target | 48 dp以上、wrist primary 56 dp以上、critical STOP 64 dpを設計基準とする |
| Layout | DPI、text 200%、left/right wrist、seated/standing、safe area、RTLを扱う |

release用themeの具体値は、review済みM3 snapshotから生成してhash固定する。example中のplaceholderをreleaseへ残さない。

## 5. M3 catalog coverage

`m3-source-inventory.example.yml`は公式navigationのURL／title inventory、snapshot digest、追加・削除・rename差分を管理する。`m3-conformance-profile.example.yml`は各entryの適用性、要件、代替、evidence IDを管理する。

- foundation: accessibility、design tokens、layout、interaction、usability、customization、XR design／accessibility
- style: color、typography、icons、shape、elevation、motion
- component family: app bars、badges、bottom sheets、buttons、button groups、cards、carousels、checkboxes、chips、date pickers、dialogs、dividers、FAB、icon buttons、lists、loading/progress indicators、menus、navigation bar/drawer/rail、radio buttons、search、segmented buttons、side sheets、sliders、snackbars、switches、tabs、text fields、time pickers、toolbars、tooltips、およびrelease時に発見した追加項目
- XR mapping: spatial panel、app bar、dialog、navigation、toolbar、およびrelease時に発見した追加項目

全entryを削除せず分類する。NotApplicableには理由と代替、Deferredにはownerとtarget release、Applicableにはdesign／implementation／test evidenceを要求する。classification coverage 100%、Unclassified 0を必須とする。

## 6. Component map

| Use case | M3 pattern | Material Symbol | Additional requirement |
|---|---|---|---|
| Start recording | Large filled icon button | `fiber_manual_record` | Visible `REC`、localized accessible name、haptic |
| Stop recording | Large filled icon button using project recording role | `stop` | Square shape＋localized short label、常時1操作で到達。error roleを使わない |
| Microphone toggle | Filled tonal icon toggle | `mic` / `mic_off` | selected state、tooltip、state announcement |
| Mute all | Icon toggle or switch | `volume_off` | desktop/mic双方の状態を補助表示 |
| Self timer | Segmented button / menu | `timer` | Off/3/5/10を文字・数字で表示 |
| Auto stop | Segmented button / menu | `schedule` | ∞/3/5/10/30/60を数字で表示 |
| Countdown | Circular progress indicator | numeral | cancel actionを明示 |
| No signal | Persistent error card/state | `signal_disconnected` | REC disabled、理由と復旧操作 |
| Move overlay | Drag handle + equivalent buttons/actions | `drag_indicator` | dedicated handleに加え、上下左右nudge、recenter、dock／pinを提供 |
| Pin overlay | Icon toggle | `keep` | Wrist Dock / World Pin stateを明示 |
| Settings | Icon button / list item | `settings` | tooltip、accessible name |
| Legal overview | Top app bar + list | `info`, `description`, `folder_open`, `open_in_new` | offline license全文 |
| Open folder | Text button / list item | `folder_open` | localized label |
| Language | Menu | `language` | language nameを使用し、flag禁止 |
| Status | Status icon + text | `check`, `warning`, `signal_disconnected` | 色だけに依存しない |
| Orientation | Metadata chip | none | 幅×高さの数値と縦／横localized short label。未登録iconを追加しない |

## 7. 言語に依存しにくいUI

「言語非依存」は文字をすべて消すことではない。意味が普遍的なicon、形状、position、state、数値を第一の手掛かりにし、曖昧性とaccessibilityを解消するlocalized textを必ず併用する。

- REC: 円形録画symbol＋`REC` short label＋経過時間
- STOP: 正方形symbol＋localized short label／tooltip
- Mic: `mic` / `mic_off`の形状差＋selected state
- Timer: timer symbol＋数字。単位はlocale resourceで補足
- Auto stop: ∞または数字をprimary informationとする
- Signal: disconnected-signal symbol＋`NO SIGNAL`相当のlocalized label＋REC disabled
- Move/Pin: dedicated drag handleとpin state。通常tap領域と分離し、上下左右nudge、recenter、dock／pinのbutton／actionも提供する
- Warning/Error: shape、icon、label、focus/hapticを併用し、色だけで伝えない
- Language selector: flagではなく`language` iconと各言語の自己表記を使う

icon-only controlには、目的別のlocalized tooltip、accessible name、必要に応じてaccessible descriptionを付ける。icon file名、upstream ligature、codepointを利用者へ表示しない。

## 8. Localization / README

- 初期UI locale: `ja-JP`, `en-US`
- Fallback: `en-US`
- All user-facing UI text: resource key
- Pseudo locale: 200% expansion
- RTL: layoutとdirectional iconをtest
- CJK/system font fallback: Windows system fontを参照し、font binaryを同梱しない
- License text: upstream originalをauthoritativeとし、UI chromeだけをlocalizeする
- README: **日本語と英語だけ**を同一README内に置き、主要headingとrelease情報のparityをCIで検証する

README以外のlicense原文、component名、API名、code、schema keyまで無理に翻訳しない。法的原文は改変せず、必要な説明を日英で別途付ける。

## 9. Accessibility

- Hit target: 48 dp相当以上。SteamVR primary 56 dp、critical STOP 64 dpを基準にする。
- XR physical target: 実寸7～10 mm相当を目標にし、ray jitterと視距離で拡大する。
- Contrast: normal text 4.5:1以上、large text／important non-text UI 3:1以上。
- Color independence: REC、STOP、warning、selected、disabledは2つ以上の視覚的手掛かりを持ち、必要に応じhaptic／announcementを加える。録画のrecording groupと障害のerror groupを分離する。
- Accessible name: interactive element 100%。
- Tooltip: 曖昧なicon-only actionへpurpose-specific tooltipを付ける。
- Focus: keyboard、mouse、controller ray、SteamVR actionで同じlogical orderを保つ。
- Drag alternative: panel移動はdragなしでも、tap可能な上下左右nudge、recenter、dock／pinとSteamVR Input Actionから完了できる。
- Text scaling: 100%、125%、150%、200%でcritical actionをclipしない。
- Motion: Reduce Motionを尊重し、点滅を標準recording cueにしない。
- XR: left/right hand、seated/standing、異なる視距離、視野角、手振れ、色覚差をmanual reviewする。

## 10. Automated release gate

Release pipelineは最低限、次を検証する。

1. 公式M3 source inventoryのclassification coverageが100%、`unclassifiedItems == 0`である。
2. 公式navigationの追加・削除・rename差分がreview済みで、inventory digestが固定されている。
3. Applicable項目にevidenceと合格結果、NotApplicable項目にrationaleとalternative、Deferred項目にownerとtarget releaseがある。
4. 出荷機能に関するDeferredが0、`unverifiedApplicableItems == 0`、`unresolvedDeviations == 0`である。
5. 全UI assetがMaterial Symbols allowlist、third-party registry、rights ledgerに存在する。
6. Material Symbolsの完全commit、source path、codepoint、source/output SHA-256が一致する。
7. 変換assetは`modified: true`、変更表示、tool/version、recipeを持つ。
8. Apache-2.0全文、copyright/attribution、NOTICE判定、component entry、SBOM、usage manifestが存在する。
9. Runtime external font/icon requestが0件である。
10. Direct color、font size、radius、spacing、shadow、motion literalが存在しない。
11. `recording`と`error`が別semantic groupであり、正常な録画状態にerror roleを使用していない。
12. 全interactive componentにM3 role、全state、focus、tooltip、accessible metadataがある。
13. Target、contrast、focus order、drag代替、text scale、Japanese、English、pseudo locale、RTL、high contrast、Reduce Motion testが合格する。
14. READMEの日英section parityが合格する。

## 11. Manual XR review

- ReadyからRECまで1操作
- RecordingからSTOPまで、Legalや設定表示中でも1操作
- Wrist DockとWorld Pinで視認性を維持
- Ray jitterで隣接controlを誤選択しない
- dragを使わずnudge／recenter／dock／pin操作を完了できる
- 片手／左右手／座位／立位で操作可能
- NO SIGNALとLoadingBlackを区別
- 赤を識別しにくい利用者も録画状態を認識可能
- 日本語／英語／pseudo localeでprimary information hierarchyが同一
- Long license textを読んでいてもSTOPへ即時復帰可能
- Panel移動、depth、motionが不快感や過度な視線移動を増やさない

## 12. Deviation policy

M3に定義がないOpenVR固有要素は、最も近いM3 component／XR foundationへ対応付けたADRを作る。安全、comfort、ray操作のためにtargetやspacingを拡大することは認めるが、M3基準より小さくする、contrastを下げる、tooltip／accessible nameを省くことは認めない。drag以外の同等操作を省略することも認めない。

M3 guidanceとWindows accessibility/high-contrast要件が衝突する場合は、利用者安全とplatform accessibilityを優先し、理由、影響範囲、代替、testをdeviation recordへ残す。未解決または未文書化のdeviationがあるreleaseは署名しない。

## 13. Official references

- https://fonts.google.com/icons
- https://developers.google.com/fonts/docs/material_symbols
- https://github.com/google/material-design-icons
- https://github.com/google/material-design-icons/blob/master/LICENSE
- https://m3.material.io/foundations/design-tokens
- https://m3.material.io/styles/icons
- https://m3.material.io/styles/color/roles
- https://m3.material.io/foundations/layout/layout-overview
- https://m3.material.io/foundations/layout/bidirectionality-rtl
- https://m3.material.io/foundations/interaction/inputs
- https://m3.material.io/foundations/interaction/states
- https://m3.material.io/foundations/xr/design/overview
- https://m3.material.io/foundations/xr/design/accessibility
- https://m3.material.io/components/icon-buttons/overview
- https://m3.material.io/components/icon-buttons/accessibility
- https://m3.material.io/components/tooltips/guidelines
- https://about.google/brand-resource-center/brand-elements/
