using VRRecorder.DesignSystem;

namespace VRRecorder.Presentation.Wrist;

public sealed class EnglishUiLocalizer : IUiLocalizer
{
    private static readonly Dictionary<string, string> Values =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["recording.start.short"] = "REC",
            ["recording.start.accessible"] = "Start recording",
            ["recording.stop.short"] = "STOP",
            ["recording.stop.accessible"] = "Stop recording",
            ["audio.microphone.on.short"] = "MIC ON",
            ["audio.microphone.on.accessible"] = "Microphone on",
            ["audio.microphone.on.tooltip"] = "Record microphone audio",
            ["audio.microphone.off.short"] = "MIC OFF",
            ["audio.microphone.off.accessible"] = "Microphone off",
            ["audio.microphone.off.tooltip"] =
                "Do not record microphone audio",
            ["audio.mute-all.short"] = "Mute all",
            ["audio.mute-all.accessible"] =
                "Mute desktop audio and microphone",
            ["audio.mute-all.tooltip"] = "Turn off all recorded audio",
            ["camera.retry.short"] = "RETRY",
            ["camera.retry.accessible"] = "Retry camera connection",
            ["state.booting.label"] = "Starting",
            ["state.compliance-fault.label"] = "Compliance check failed",
            ["state.ready.label"] = "Ready",
            ["state.arming.label"] = "Connecting camera",
            ["state.countdown.label"] = "Countdown",
            ["state.starting.label"] = "Starting recording",
            ["state.recording.label"] = "Recording",
            ["state.signal-lost.label"] = "Camera signal lost",
            ["state.stopping.label"] = "Saving recording",
            ["state.no-signal.label"] = "No camera signal",
            ["state.faulted.label"] = "Recorder error",
            ["legal.title"] = "About & Legal",
            ["legal.version.format"] = "Product version {0}",
            ["legal.bundle.format"] = "Legal Bundle identity {0}",
            ["legal.manifest.format"] = "Manifest SHA-256 {0}",
            ["legal.component.accessible.format"] =
                "Open legal details for {0}, version {1}, license {2}",
            ["legal.field.name"] = "Name",
            ["legal.field.version"] = "Version",
            ["legal.field.license"] = "License",
            ["legal.field.usage"] = "Usage",
            ["legal.field.linkage"] = "Linkage",
            ["legal.field.modified"] = "Modified",
            ["legal.field.source"] = "Source information",
            ["legal.field.copyright"] = "Copyright",
            ["legal.document.license"] = "License",
            ["legal.document.notice"] = "Notice",
            ["legal.document.copyright"] = "Copyright document",
            ["legal.document.attribution"] = "Attribution",
            ["legal.document.asset-manifest"] = "Asset manifest",
            ["legal.document.accessible.format"] = "Read {0} at {1}",
            ["legal.modified.yes"] = "Yes",
            ["legal.modified.no"] = "No",
            ["legal.back.short"] = "BACK",
            ["legal.back.accessible"] = "Back to legal components",
            ["legal.open-license.short"] = "LICENSE",
            ["legal.open-license.accessible"] = "Read full license text",
            ["legal.previous-page.short"] = "PREVIOUS",
            ["legal.previous-page.accessible"] = "Previous license page",
            ["legal.next-page.short"] = "NEXT",
            ["legal.next-page.accessible"] = "Next license page",
            ["legal.page.format"] = "Page {0} of {1}",
            ["legal.unavailable.label"] =
                "Authenticated legal information is unavailable",
        };

    private EnglishUiLocalizer()
    {
    }

    public static EnglishUiLocalizer Instance { get; } = new();

    public IReadOnlyCollection<string> ResourceKeys => Values.Keys;

    public LocalizedText Resolve(string resourceKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceKey);
        return Values.TryGetValue(resourceKey, out var value)
            ? new LocalizedText(resourceKey, value)
            : throw new KeyNotFoundException(
                $"No English UI resource exists for {resourceKey}.");
    }
}
