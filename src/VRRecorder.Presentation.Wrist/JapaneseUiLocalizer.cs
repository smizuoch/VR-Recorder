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
            ["camera.retry.short"] = "再試行",
            ["camera.retry.accessible"] = "カメラ接続を再試行",
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
            ["legal.title"] = "情報とライセンス",
            ["legal.version.format"] = "製品バージョン {0}",
            ["legal.component.accessible.format"] =
                "{0}、バージョン {1}、ライセンス {2} の法的情報を開く",
            ["legal.field.name"] = "名称",
            ["legal.field.version"] = "バージョン",
            ["legal.field.license"] = "ライセンス",
            ["legal.field.usage"] = "用途",
            ["legal.field.linkage"] = "リンク形態",
            ["legal.field.modified"] = "改変",
            ["legal.field.source"] = "ソース情報",
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
