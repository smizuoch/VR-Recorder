# Presentation test list / Presentationテストリスト

## 日本語

- [x] ReadyはaccessibleなREC actionを1つ表示する
- [x] Recordingは全pageから64 dp STOPへ1操作で到達できる
- [x] NoSignal/Faulted/ComplianceFaultはStartを許可しない
- [x] 全RecorderStateを非色覚cue付きsemantic roleへ投影する
- [x] 日本語・英語resource key/placeholderが一致する
- [x] desktop click・keyboardを共通のcancellation-aware dispatcherへ送る
- [x] wrist rayのenabled actionを共通dispatcherへ送る
- [x] desktop click・keyboard・wrist ray・SteamVR actionが同じcommand IDになる
- [x] x64 WPF hostをLinuxからcross-buildする
- [x] 認証済みLegal Bundleを手首UIの一覧・詳細・全文pageとしてoffline表示する
- [x] Legal全文のpage移動ごとに再検証し、改ざん時は表示済みtextを消去する
- [x] Legalの全viewでcanonical 64 dp STOPを1操作に固定する
- [ ] WindowsでWPF hostのUI Automationを実行する

## English

- [x] Show one accessible REC action in Ready
- [x] Keep a 64 dp one-action STOP reachable from every page while Recording
- [x] Disallow Start in NoSignal, Faulted, and ComplianceFault
- [x] Project every RecorderState to semantic roles with non-color cues
- [x] Keep Japanese/English resource keys and placeholders in parity
- [x] Route desktop click and keyboard through one cancellation-aware dispatcher
- [x] Route enabled wrist-ray actions through the shared dispatcher
- [x] Map desktop click, keyboard, wrist ray, and SteamVR action to one command ID
- [x] Cross-build the x64 WPF host on Linux
- [x] Show an authenticated Legal Bundle offline as wrist list, detail, and full-text pages
- [x] Reverify each legal-text navigation and clear previously visible text after tampering
- [x] Keep the canonical 64 dp one-operation STOP fixed on every Legal view
- [ ] Run WPF host UI Automation on Windows
