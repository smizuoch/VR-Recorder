using VRRecorder.Application.Ports;

namespace VRRecorder.Application.Haptics;

public sealed class WristHapticFeedbackCoordinator
{
    private readonly object _gate = new();
    private readonly IWristHapticOutput _output;
    private readonly WristHapticFeedbackOptions _options;
    private long _lastRevision = -1;

    public WristHapticFeedbackCoordinator(
        IWristHapticOutput output,
        WristHapticFeedbackOptions options)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(options);
        _output = output;
        _options = options;
    }

    public async Task<WristHapticFeedbackResult> PublishAsync(
        long revision,
        WristHapticFeedbackKind kind,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(revision);
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "Unknown haptic feedback kind.");
        }

        lock (_gate)
        {
            if (revision <= _lastRevision)
            {
                return new WristHapticFeedbackResult.Ignored(revision);
            }

            _lastRevision = revision;
        }

        if (!_options.IsEnabled)
        {
            return new WristHapticFeedbackResult.Disabled(revision);
        }

        var pattern = CreatePattern(kind, _options);
        try
        {
            await _output
                .PlayAsync(pattern, cancellationToken)
                .ConfigureAwait(false);
            return new WristHapticFeedbackResult.Delivered(revision);
        }
        catch (Exception exception)
        {
            return new WristHapticFeedbackResult.Failed(
                revision,
                exception);
        }
    }

    private static WristHapticPattern CreatePattern(
        WristHapticFeedbackKind kind,
        WristHapticFeedbackOptions options) =>
        kind switch
        {
            WristHapticFeedbackKind.RecordingStarted => new(
                TimeSpan.FromMilliseconds(30),
                pulseCount: 1,
                options.FrequencyHertz,
                options.Amplitude),
            WristHapticFeedbackKind.RecordingStopped => new(
                TimeSpan.FromMilliseconds(20),
                pulseCount: 2,
                options.FrequencyHertz,
                options.Amplitude),
            WristHapticFeedbackKind.Fault => new(
                TimeSpan.FromMilliseconds(80),
                pulseCount: 1,
                options.FrequencyHertz,
                options.Amplitude),
            _ => throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "Unknown haptic feedback kind."),
        };
}
