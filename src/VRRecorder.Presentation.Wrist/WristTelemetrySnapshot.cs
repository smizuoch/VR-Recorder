using System.Globalization;
using VRRecorder.Application.Settings;
using VRRecorder.DesignSystem;

namespace VRRecorder.Presentation.Wrist;

public enum WristSignalHealth
{
    NotApplicable,
    Available,
    Degraded,
    Unavailable,
}

public enum WristAlertSeverity
{
    Warning,
    Fault,
}

public sealed record WristAlertSnapshot
{
    public WristAlertSnapshot(
        string semanticId,
        WristAlertSeverity severity,
        LocalizedText message)
    {
        ValidateText(semanticId, nameof(semanticId), 128);
        if (!Enum.IsDefined(severity))
        {
            throw new ArgumentOutOfRangeException(
                nameof(severity),
                severity,
                "The wrist alert severity is not defined.");
        }
        ArgumentNullException.ThrowIfNull(message);
        ValidateText(message.ResourceKey, nameof(message), 256);
        ValidateText(message.Value, nameof(message), 1024);
        SemanticId = semanticId;
        Severity = severity;
        Message = message;
    }

    public string SemanticId { get; }

    public WristAlertSeverity Severity { get; }

    public LocalizedText Message { get; }

    private static void ValidateText(
        string value,
        string parameterName,
        int maximumLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > maximumLength || value.Any(char.IsControl))
        {
            throw new ArgumentException(
                "Wrist alert text is invalid.",
                parameterName);
        }
    }
}

public sealed record WristTelemetrySnapshot
{
    private const int MaximumCanvasDimension = 16_384;
    private const double MaximumFramesPerSecond = 1_000;

    public WristTelemetrySnapshot(
        TimeSpan elapsedRecordingTime,
        int canvasWidth,
        int canvasHeight,
        double targetFramesPerSecond,
        double actualFramesPerSecond,
        WristSignalHealth spoutSignal,
        WristSignalHealth desktopAudioSignal,
        WristSignalHealth microphoneSignal,
        string encoderDisplayName,
        OverlayPlacementMode placementMode,
        IEnumerable<WristAlertSnapshot> alerts)
    {
        if (elapsedRecordingTime < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(elapsedRecordingTime),
                elapsedRecordingTime,
                "Elapsed recording time cannot be negative.");
        }
        ValidateDimension(canvasWidth, nameof(canvasWidth));
        ValidateDimension(canvasHeight, nameof(canvasHeight));
        ValidateFramesPerSecond(
            targetFramesPerSecond,
            allowZero: false,
            nameof(targetFramesPerSecond));
        ValidateFramesPerSecond(
            actualFramesPerSecond,
            allowZero: true,
            nameof(actualFramesPerSecond));
        ValidateHealth(spoutSignal, nameof(spoutSignal));
        ValidateHealth(desktopAudioSignal, nameof(desktopAudioSignal));
        ValidateHealth(microphoneSignal, nameof(microphoneSignal));
        ArgumentException.ThrowIfNullOrWhiteSpace(encoderDisplayName);
        if (encoderDisplayName.Length > 128 ||
            encoderDisplayName.Any(char.IsControl))
        {
            throw new ArgumentException(
                "The encoder display name is invalid.",
                nameof(encoderDisplayName));
        }
        if (!Enum.IsDefined(placementMode))
        {
            throw new ArgumentOutOfRangeException(
                nameof(placementMode),
                placementMode,
                "The overlay placement mode is not defined.");
        }
        ArgumentNullException.ThrowIfNull(alerts);
        var alertArray = alerts.ToArray();
        if (alertArray.Length > 8 || alertArray.Any(alert => alert is null) ||
            alertArray.Select(alert => alert.SemanticId)
                .Distinct(StringComparer.Ordinal)
                .Count() != alertArray.Length)
        {
            throw new ArgumentException(
                "Wrist alerts must be non-null, unique, and limited to eight.",
                nameof(alerts));
        }

        ElapsedRecordingTime = elapsedRecordingTime;
        CanvasWidth = canvasWidth;
        CanvasHeight = canvasHeight;
        TargetFramesPerSecond = targetFramesPerSecond;
        ActualFramesPerSecond = actualFramesPerSecond;
        SpoutSignal = spoutSignal;
        DesktopAudioSignal = desktopAudioSignal;
        MicrophoneSignal = microphoneSignal;
        EncoderDisplayName = encoderDisplayName;
        PlacementMode = placementMode;
        Alerts = Array.AsReadOnly(alertArray);
        ElapsedText = FormatElapsed(elapsedRecordingTime);
        ResolutionText = FormattableString.Invariant(
            $"{canvasWidth}×{canvasHeight}");
        FramesPerSecondText = string.Create(
            CultureInfo.InvariantCulture,
            $"{targetFramesPerSecond:0.##} / {actualFramesPerSecond:0.##} FPS");
    }

    public TimeSpan ElapsedRecordingTime { get; }

    public int CanvasWidth { get; }

    public int CanvasHeight { get; }

    public double TargetFramesPerSecond { get; }

    public double ActualFramesPerSecond { get; }

    public WristSignalHealth SpoutSignal { get; }

    public WristSignalHealth DesktopAudioSignal { get; }

    public WristSignalHealth MicrophoneSignal { get; }

    public string EncoderDisplayName { get; }

    public OverlayPlacementMode PlacementMode { get; }

    public IReadOnlyList<WristAlertSnapshot> Alerts { get; }

    public string ElapsedText { get; }

    public string ResolutionText { get; }

    public string FramesPerSecondText { get; }

    private static string FormatElapsed(TimeSpan elapsed)
    {
        var totalHours = checked((long)elapsed.TotalHours);
        return totalHours == 0
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"{elapsed.Minutes:00}:{elapsed.Seconds:00}")
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{totalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}");
    }

    private static void ValidateDimension(int value, string parameterName)
    {
        if (value < 1 || value > MaximumCanvasDimension)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                $"Canvas dimensions must be between 1 and {MaximumCanvasDimension}.");
        }
    }

    private static void ValidateFramesPerSecond(
        double value,
        bool allowZero,
        string parameterName)
    {
        var minimum = allowZero ? 0 : double.Epsilon;
        if (!double.IsFinite(value) ||
            value < minimum ||
            value > MaximumFramesPerSecond)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "FPS must be finite and within the supported range.");
        }
    }

    private static void ValidateHealth(
        WristSignalHealth health,
        string parameterName)
    {
        if (!Enum.IsDefined(health))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                health,
                "The wrist signal health is not defined.");
        }
    }
}
