using VRRecorder.DesignSystem;

namespace VRRecorder.Presentation.Wrist;

public sealed class JapaneseUiLocalizer : IUiLocalizer
{
    private static readonly Dictionary<string, string> Values =
        new(StringComparer.Ordinal)
        {
            ["recording.start.short"] = "録画",
            ["recording.start.accessible"] = "録画を開始",
            ["recording.stop.short"] = "停止",
            ["recording.stop.accessible"] = "録画を停止",
            ["audio.microphone.on.short"] = "マイク オン",
            ["audio.microphone.on.accessible"] = "マイク オン",
            ["audio.microphone.on.tooltip"] = "マイク音声を録音します",
            ["audio.microphone.off.short"] = "マイク オフ",
            ["audio.microphone.off.accessible"] = "マイク オフ",
            ["audio.microphone.off.tooltip"] =
                "マイク音声を録音しません",
            ["audio.mute-all.short"] = "全音声オフ",
            ["audio.mute-all.accessible"] =
                "デスクトップ音声とマイクをミュート",
            ["audio.mute-all.tooltip"] =
                "すべての録音音声をオフにします",
            ["camera.retry.short"] = "再試行",
            ["camera.retry.accessible"] = "カメラ接続を再試行",
            ["overlay.move.short"] = "移動",
            ["overlay.move.accessible"] = "オーバーレイを移動",
            ["overlay.move.tooltip"] = "位置調整を開きます",
            ["overlay.dock.short"] = "手首",
            ["overlay.dock.accessible"] = "オーバーレイを手首へ固定",
            ["overlay.dock.tooltip"] =
                "オーバーレイを選択中の手首へ取り付けます",
            ["overlay.pin.short"] = "空間固定",
            ["overlay.pin.accessible"] = "オーバーレイを空間へ固定",
            ["overlay.pin.tooltip"] =
                "オーバーレイを現在の空間位置へ固定します",
            ["overlay.nudge.up.short"] = "上",
            ["overlay.nudge.up.accessible"] = "オーバーレイを上へ移動",
            ["overlay.nudge.up.tooltip"] = "オーバーレイを上へ移動します",
            ["overlay.nudge.down.short"] = "下",
            ["overlay.nudge.down.accessible"] = "オーバーレイを下へ移動",
            ["overlay.nudge.down.tooltip"] = "オーバーレイを下へ移動します",
            ["overlay.nudge.left.short"] = "左",
            ["overlay.nudge.left.accessible"] = "オーバーレイを左へ移動",
            ["overlay.nudge.left.tooltip"] = "オーバーレイを左へ移動します",
            ["overlay.nudge.right.short"] = "右",
            ["overlay.nudge.right.accessible"] = "オーバーレイを右へ移動",
            ["overlay.nudge.right.tooltip"] = "オーバーレイを右へ移動します",
            ["overlay.recenter.short"] = "中央",
            ["overlay.recenter.accessible"] = "オーバーレイを中央へ戻す",
            ["overlay.recenter.tooltip"] =
                "オーバーレイを既定位置へ戻します",
            ["common.back.short"] = "戻る",
            ["common.back.accessible"] = "戻る",
            ["common.back.tooltip"] = "前のページへ戻ります",
            ["state.booting.label"] = "起動中",
            ["state.compliance-fault.label"] = "コンプライアンス確認失敗",
            ["state.ready.label"] = "準備完了",
            ["state.arming.label"] = "カメラ接続中",
            ["state.countdown.label"] = "カウントダウン",
            ["state.starting.label"] = "録画開始中",
            ["state.recording.label"] = "録画中",
            ["state.signal-lost.label"] = "カメラ信号喪失",
            ["state.stopping.label"] = "録画保存中",
            ["state.no-signal.label"] = "カメラ信号なし",
            ["state.faulted.label"] = "レコーダーエラー",
            ["telemetry.desktop-audio.unavailable"] =
                "デスクトップ音声を利用できません",
            ["telemetry.microphone.unavailable"] =
                "マイクを利用できません",
            ["legal.title"] = "情報とライセンス",
            ["legal.version.format"] = "製品バージョン {0}",
            ["legal.bundle.format"] = "Legal Bundle 識別子 {0}",
            ["legal.manifest.format"] = "Manifest SHA-256 {0}",
            ["legal.component.accessible.format"] =
                "{0}、バージョン {1}、ライセンス {2} の法的情報を開く",
            ["legal.field.name"] = "名称",
            ["legal.field.version"] = "バージョン",
            ["legal.field.license"] = "ライセンス",
            ["legal.field.usage"] = "用途",
            ["legal.field.linkage"] = "リンク形態",
            ["legal.field.modified"] = "改変",
            ["legal.field.source"] = "ソース情報",
            ["legal.field.copyright"] = "著作権",
            ["legal.document.license"] = "ライセンス",
            ["legal.document.notice"] = "通知",
            ["legal.document.copyright"] = "著作権文書",
            ["legal.document.attribution"] = "帰属表示",
            ["legal.document.asset-manifest"] = "アセットマニフェスト",
            ["legal.document.accessible.format"] = "{1} の {0} を読む",
            ["legal.modified.yes"] = "あり",
            ["legal.modified.no"] = "なし",
            ["legal.back.short"] = "戻る",
            ["legal.back.accessible"] = "第三者コンポーネント一覧へ戻る",
            ["legal.open-license.short"] = "ライセンス",
            ["legal.open-license.accessible"] = "ライセンス全文を読む",
            ["legal.previous-page.short"] = "前へ",
            ["legal.previous-page.accessible"] = "ライセンスの前ページ",
            ["legal.next-page.short"] = "次へ",
            ["legal.next-page.accessible"] = "ライセンスの次ページ",
            ["legal.page.format"] = "{0} / {1} ページ",
            ["legal.unavailable.label"] =
                "認証済みの法的情報を表示できません",
        };

    private JapaneseUiLocalizer()
    {
    }

    public static JapaneseUiLocalizer Instance { get; } = new();

    public IReadOnlyCollection<string> ResourceKeys => Values.Keys;

    public LocalizedText Resolve(string resourceKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceKey);
        return Values.TryGetValue(resourceKey, out var value)
            ? new LocalizedText(resourceKey, value)
            : throw new KeyNotFoundException(
                $"No Japanese UI resource exists for {resourceKey}.");
    }
}
