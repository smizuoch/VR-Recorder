# M3適用性マトリクス

- 対象: VR-Recorder基本設計書 v0.3
- Profile: `ui-template/m3-conformance-profile.example.yml`
- Source inventory: `ui-template/m3-source-inventory.example.yml`
- 仕様確認日: 2026-07-10
- 性質: 設計時example。実releaseでは公式M3 navigationの承認済みsnapshotで再生成する。

## 判定ルール

- **Applicable**: 製品へ適用し、design／implementation／test evidenceを必須にする。
- **NotApplicable**: 該当しない理由と代替手段を必須にする。
- **Deferred**: ownerとtarget releaseを必須にする。ただし出荷機能に関するDeferredは認めない。
- Source inventory coverage 100%、Unclassified 0、unresolved deviations 0が署名条件である。
- これはGoogleによる認証・承認を意味しない。

## Foundation

| ID | Status | Scope | Requirements | Evidence |
|---|---|---|---|---|
| `foundation-accessibility` | Applicable | desktop, wrist | all-controls-have-programmatic-name, critical-status-not-color-only, focus-visible-and-logical, target-size-and-spacing-verified, drag-has-tap-or-action-alternative, high-contrast-and-text-scaling-supported | A11Y-AUTO, A11Y-MANUAL-XR |
| `foundation-design-tokens` | Applicable | desktop, wrist | all-visual-values-through-semantic-tokens, generated-wpf-and-openvr-bindings, token-change-impact-report | TOKEN-LINT, TOKEN-SNAPSHOT |
| `foundation-layout` | Applicable | desktop, wrist | adaptive-desktop-layout, wrist-safe-insets, text-expansion-without-primary-action-loss, ltr-and-rtl-layout, seated-standing-left-right-hand-review | LAYOUT-GOLDEN, XR-LAYOUT-MANUAL |
| `foundation-interaction` | Applicable | desktop, wrist | enabled-hovered-focused-pressed-disabled-selected-states, controller-ray-and-trigger-feedback, steamvr-action-command-equivalence, deterministic-recording-state-machine | STATE-MATRIX, INPUT-CONTRACT |
| `foundation-usability` | Applicable | desktop, wrist | record-and-stop-remain-one-action, no-signal-prevents-empty-recording, undo-or-safe-recovery-where-applicable, concise-primary-surface | USABILITY-TEST, ACCEPTANCE-UI |
| `foundation-customization` | Applicable | desktop, wrist | independent-vr-recorder-branding, no-google-brand-imitation, shared-semantic-system-across-surfaces | BRAND-REVIEW, TOKEN-SNAPSHOT |
| `foundation-xr-design` | Applicable | wrist | spatial-panel-depth-and-legibility, comfortable-viewing-angle-and-distance, controller-ray-error-tolerance, wrist-dock-and-world-pin-modes | XR-LAYOUT-MANUAL, XR-INPUT-MANUAL |
| `foundation-xr-accessibility` | Applicable | wrist | text-and-target-size-for-xr, non-drag-repositioning-path, left-right-hand-parity, haptics-never-sole-information-channel | A11Y-MANUAL-XR, DRAG-ALTERNATIVE-TEST |

## Styles

| ID | Status | Scope | Requirements | Evidence |
|---|---|---|---|---|
| `style-color` | Applicable | desktop, wrist | m3-semantic-roles, project-recording-custom-color-group, error-reserved-for-fault, contrast-verified-for-every-state | COLOR-LINT, CONTRAST-TEST |
| `style-typography` | Applicable | desktop, wrist | mapped-m3-type-scale, tabular-time-and-resolution, cjk-and-mixed-script-rendering, no-redistributed-font-without-rights-entry | TYPE-LINT, I18N-GOLDEN |
| `style-icons` | Applicable | desktop, wrist | approved-material-symbols-only, pinned-upstream-and-hashes, purpose-specific-labels, correct-rtl-mirroring, no-logo-or-product-icon | ICON-MANIFEST, ICON-LINT, LEGAL-BUNDLE |
| `style-shape` | Applicable | desktop, wrist | m3-shape-roles-only, state-shape-remains-recognizable | SHAPE-LINT |
| `style-elevation` | Applicable | desktop, wrist | hierarchy-only, no-decorative-unregistered-shadow | ELEVATION-LINT, XR-LAYOUT-MANUAL |
| `style-motion` | Applicable | desktop, wrist | registered-motion-tokens-only, state-feedback-within-150ms, reduce-motion-alternative, no-flashing-record-cue | MOTION-LINT, REDUCE-MOTION-TEST |

## Component families

| ID | Status | Used by / Rationale | Alternative / Requirements | Evidence |
|---|---|---|---|---|
| `component-app-bars` | Applicable | wrist-secondary-pages, desktop-settings, legal-viewer | clear-title, back-or-close-action, persistent-stop-while-recording | COMPONENT-GOLDEN, STOP-REACHABILITY |
| `component-badges` | NotApplicable | No unread-count or compact count-badge use case exists in the initial product. | Status is shown with icon, short label, and state description. |  |
| `component-bottom-sheets` | NotApplicable | Bottom-edge mobile gestures do not map safely to the WPF desktop or floating wrist panel. | Use a dialog, menu, or dedicated secondary page. |  |
| `component-buttons` | Applicable | record, stop, cancel-countdown, retry, open-folder | all-states, clear-label-for-critical-actions, minimum-target | COMPONENT-GOLDEN, TARGET-TEST |
| `component-button-groups` | Applicable | self-timer-presets, auto-stop-presets | single-selection-semantics, selected-state-not-color-only | TIMER-COMPONENT-TEST |
| `component-cards` | Applicable | signal-fault, encoder-status, legal-component-summary | clear-hierarchy, correct-container-role | COMPONENT-GOLDEN |
| `component-carousels` | NotApplicable | VR-Recorder does not browse media collections on the primary recording surface. | Use a list or paged legal viewer. |  |
| `component-checkboxes` | Applicable | desktop-advanced-settings | label-associated, indeterminate-only-when-semantic | A11Y-AUTO |
| `component-chips` | Applicable | encoder-status, audio-status, active-mode-status | status-not-color-only, noninteractive-chip-not-focusable | COMPONENT-GOLDEN, A11Y-AUTO |
| `component-date-pickers` | NotApplicable | No calendar-date input is required. | None. |  |
| `component-dialogs` | Applicable | rights-confirmation, destructive-reset, compliance-fault-details | focus-trap, explicit-title, keyboard-controller-dismissal, stop-remains-reachable | DIALOG-A11Y-TEST, STOP-REACHABILITY |
| `component-dividers` | Applicable | settings-sections, legal-list | decorative-divider-not-announced, sufficient-contrast | COMPONENT-GOLDEN |
| `component-fab` | NotApplicable | A floating action button would compete with the fixed, safety-critical REC/STOP region. | Use the dedicated primary recording button. |  |
| `component-icon-buttons` | Applicable | mic, mute, settings, info, pin, close, back, retry | accessible-name, tooltip-when-ambiguous, target-size, all-states | ICON-BUTTON-A11Y, TARGET-TEST |
| `component-lists` | Applicable | settings, third-party-components, licenses | logical-reading-order, virtualization-does-not-break-accessibility | LIST-A11Y-TEST |
| `component-loading-indicators` | Applicable | arming, encoder-probe, legal-load | state-description, no-indefinite-block-without-cancel-or-timeout | LOADING-STATE-TEST |
| `component-menus` | Applicable | language, timer-overflow, advanced-options | keyboard-controller-navigation, selected-value-exposed | MENU-A11Y-TEST |
| `component-navigation-bar` | NotApplicable | The wrist surface has a shallow page stack and the desktop surface uses a compact settings structure. | Use top app bars and lists with Back. |  |
| `component-navigation-drawer` | NotApplicable | A hidden drawer adds discoverability and ray-target costs without enough destinations. | Use a visible settings list. |  |
| `component-navigation-rail` | NotApplicable | The initial desktop information architecture does not require persistent multi-destination navigation. | Use a compact page stack; reconsider through an ADR if destinations grow. |  |
| `component-progress-indicators` | Applicable | countdown, arming, finalize | determinate-when-progress-known, numeric-countdown-visible | COUNTDOWN-TEST, COMPONENT-GOLDEN |
| `component-radio-buttons` | Applicable | encoder-preference, resolution-change-mode | group-name, one-selection, keyboard-controller-operation | A11Y-AUTO |
| `component-search` | NotApplicable | Initial settings and legal lists remain small enough for direct navigation. | Add only after a measured usability need and profile update. |  |
| `component-segmented-buttons` | Applicable | self-timer-presets, auto-stop-presets, wrist-dock-world-pin | selected-state, text-or-number-cue, minimum-target | TIMER-COMPONENT-TEST, TARGET-TEST |
| `component-side-sheets` | NotApplicable | Secondary content is handled as a page or dialog to preserve a stable primary recording region. | Use a dedicated secondary page. |  |
| `component-sliders` | NotApplicable | The initial release uses discrete safe presets rather than imprecise ray-controlled continuous values. | Use segmented presets or numeric stepper controls. |  |
| `component-snackbars` | Applicable | saved-confirmation, nonblocking-device-warning | not-used-for-persistent-fault, accessible-live-region | LIVE-REGION-TEST |
| `component-switches` | Applicable | mic-default, mute-default, reduce-motion, haptics | label-associated, state-programmatically-exposed | A11Y-AUTO |
| `component-tabs` | NotApplicable | Tabs would add navigation density to a small set of hierarchical screens. | Use list navigation and Back. |  |
| `component-text-fields` | Applicable | advanced-numeric-settings, diagnostic-filter | visible-label, validation-message, no-placeholder-only-label | TEXT-FIELD-A11Y |
| `component-time-pickers` | NotApplicable | Recording durations are elapsed-time presets, not time-of-day values. | Use segmented buttons or a menu with seconds. |  |
| `component-toolbars` | Applicable | desktop-main-actions, legal-viewer-actions | logical-order, critical-stop-not-overflowed | STOP-REACHABILITY, COMPONENT-GOLDEN |
| `component-tooltips` | Applicable | all-ambiguous-icon-only-actions | localized-purpose-specific-text, not-sole-accessible-name-source | TOOLTIP-TEST, A11Y-AUTO |

## XR mappings

| ID | Status | Product element / Rationale | Alternative / Requirements | Evidence |
|---|---|---|---|---|
| `xr-spatial-panel` | Applicable | wrist-overlay-root | comfortable-distance, scalable-size, stable-depth, safe-inset | XR-LAYOUT-MANUAL |
| `xr-app-bar` | Applicable | wrist-secondary-top-bar | back-close, page-title, persistent-recording-state | COMPONENT-GOLDEN |
| `xr-dialog` | Applicable | rights-and-compliance-dialogs | focus, readable-depth, stop-reachability | DIALOG-A11Y-TEST, XR-INPUT-MANUAL |
| `xr-navigation-bar` | NotApplicable | The wrist surface has too few top-level destinations. | Use top app bar plus shallow page stack. |  |
| `xr-navigation-rail` | NotApplicable | A rail consumes excessive width on the compact wrist panel. | Use a visible settings list. |  |
| `xr-toolbar` | Applicable | wrist-legal-actions | large-ray-targets, labels-or-tooltips, stop-not-hidden | TARGET-TEST, STOP-REACHABILITY |

## VR-Recorder product components

| ID | M3 pattern | Semantic role | Symbols | Required cues / alternatives |
|---|---|---|---|---|
| `recording-primary` | filled-icon-button-with-project-large-size-token | recording | recording.start, recording.stop | icon-shape, REC-or-STOP-label, elapsed-time, haptic-on-transition |
| `microphone-toggle` | filled-tonal-icon-toggle-button |  | audio.microphone.on, audio.microphone.off |  |
| `mute-all-toggle` | icon-toggle-button |  | audio.muteAll |  |
| `timer-selector` | segmented-button-or-menu |  | timer.self, timer.autoStop |  |
| `no-signal-state` | persistent-fault-card-or-state-screen | error | signal.none, common.retry |  |
| `overlay-positioning` | drag-handle-plus-equivalent-button-controls |  | overlay.move, overlay.pin | nudge-up, nudge-down, nudge-left, nudge-right, recenter, wrist-dock, world-pin |
| `legal-list` | top-app-bar-plus-list |  | common.info, common.document, common.folder, common.openExternal |  |

## Release validation

- `source-inventory-coverage-100-percent`
- `no-unclassified-source-entry`
- `semantic-token-lint`
- `recording-role-not-error-alias`
- `component-role-lint`
- `all-interaction-states`
- `contrast-test`
- `target-geometry-test`
- `focus-visible-and-order-test`
- `drag-alternative-test`
- `keyboard-controller-ray-parity-test`
- `tooltip-test`
- `accessible-name-test`
- `icon-allowlist-and-codepoint-test`
- `japanese-english-golden-test`
- `pseudo-locale-golden-test`
- `rtl-golden-test`
- `high-contrast-golden-test`
- `reduce-motion-test`
- `stop-reachability-test`
- `manual-xr-review`

## 更新手順

1. 承認済みupdate PRで公式M3 navigationを取得し、URL／titleだけを正規化する。
2. 前回inventoryとの差分を確認し、追加・削除・renameをreviewする。
3. 全entryをApplicable／NotApplicable／Deferredへ分類する。
4. Profileとこの可読マトリクスを同じgeneratorから再生成する。
5. 全evidenceを実行し、coverage 100%、Unclassified 0、出荷機能Deferred 0、deviation 0を確認する。
6. M3本文、図版、スクリーンショットを製品assetへコピーしない。
