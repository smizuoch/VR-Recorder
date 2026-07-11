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
- [x] lifecycle→runtime→hostのrevisioned状態をWPF Dispatcher経由で順序を保って表示する
- [x] Arming／CountdownはCANCEL、SignalLostはSTOP、NoSignalは再試行、terminal stateは無効として表示する
- [x] x64 WPF hostをLinuxからcross-buildする
- [x] 認証済みLegal Bundleを手首UIの一覧・詳細・全文pageとしてoffline表示する
- [x] Legal全文のpage移動ごとに再検証し、改ざん時は表示済みtextを消去する
- [x] Legalの全viewでcanonical 64 dp STOPを1操作に固定する
- [x] desktop／trayへ保存済みMP4 pathとカメラ復元警告を別通知として表示する
- [x] trayをReady／Recording／Warning／Faultの4状態へ同期し、権利確認前は録画操作を無効にする
- [x] 診断bundleを明示操作だけでexportするlocalized／accessible windowを提供する
- [x] desktop／trayへ入力別の音声喪失／復旧を非terminal通知として表示し、両入力同時喪失を保持する
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
- [x] Render revisioned lifecycle→runtime→host state in order through the WPF Dispatcher
- [x] Show CANCEL for Arming/Countdown, STOP for SignalLost, Retry for NoSignal, and disable terminal states
- [x] Cross-build the x64 WPF host on Linux
- [x] Show an authenticated Legal Bundle offline as wrist list, detail, and full-text pages
- [x] Reverify each legal-text navigation and clear previously visible text after tampering
- [x] Keep the canonical 64 dp one-operation STOP fixed on every Legal view
- [x] Show the saved MP4 path and camera-restore warning as separate desktop/tray notifications
- [x] Synchronize the tray with Ready/Recording/Warning/Fault and disable recording actions before rights acknowledgement
- [x] Provide a localized and accessible window that exports diagnostics only through an explicit action
- [x] Show input-specific audio loss/recovery as nonterminal desktop/tray notifications while preserving simultaneous loss of both inputs
- [ ] Run WPF host UI Automation on Windows
