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
