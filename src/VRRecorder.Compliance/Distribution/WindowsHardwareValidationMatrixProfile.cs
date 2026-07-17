namespace VRRecorder.Compliance.Distribution;

internal static class WindowsHardwareValidationMatrixProfile
{
    public static IReadOnlyList<string> RequiredCaseIds { get; } =
        Array.AsReadOnly(new[]
        {
            "launch-first-run-legal",
            "vrchat-spout-recording",
            "spout-demo-recording",
            "recording-stop-finalize",
            "ffprobe-video-audio-decode",
            "player-playback",
            "presentation-timing-av-offset",
            "sender-loss",
            "audio-device-loss",
            "gpu-encoder-unavailable-fallback",
            "disk-full",
            "forced-abort",
            "failed-output-not-published",
            "camera-pending-legal-recovery",
            "openvr-overlay-controller",
            "audio-default-desktop",
            "audio-explicit-endpoint",
            "audio-mic-on",
            "audio-mic-off",
            "audio-mute-all",
            "video-landscape-720p-30",
            "video-landscape-1080p-60",
            "video-landscape-4k-120",
            "video-portrait-720p-30",
            "video-portrait-1080p-90",
            "video-portrait-4k-120",
        });
}
