# Presentation test list / Presentationテストリスト

## 日本語

- [x] ReadyはaccessibleなREC actionを1つ表示する
- [x] Recordingは全pageから64 dp STOPへ1操作で到達できる
- [x] NoSignal/Faulted/ComplianceFaultはStartを許可しない
- [x] 全RecorderStateを非色覚cue付きsemantic roleへ投影する
- [x] 日本語・英語resource key/placeholderが一致する
- [x] desktop click・keyboardを共通のcancellation-aware dispatcherへ送る
- [x] wrist rayのenabled actionを共通dispatcherへ送る
- [ ] desktop click・keyboard・wrist ray・SteamVR actionが同じcommand IDになる
- [x] x64 WPF hostをLinuxからcross-buildする
- [ ] WindowsでWPF hostのUI Automationを実行する

## English

- [x] Show one accessible REC action in Ready
- [x] Keep a 64 dp one-action STOP reachable from every page while Recording
- [x] Disallow Start in NoSignal, Faulted, and ComplianceFault
- [x] Project every RecorderState to semantic roles with non-color cues
- [x] Keep Japanese/English resource keys and placeholders in parity
- [x] Route desktop click and keyboard through one cancellation-aware dispatcher
- [x] Route enabled wrist-ray actions through the shared dispatcher
- [ ] Map desktop click, keyboard, wrist ray, and SteamVR action to one command ID
- [x] Cross-build the x64 WPF host on Linux
- [ ] Run WPF host UI Automation on Windows
