namespace VRRecorder.Domain.Audio;

public sealed class AudioRoutingRamp
{
    private readonly AudioGains _start;
    private readonly AudioGains _end;

    private AudioRoutingRamp(
        AudioGains start,
        AudioGains end,
        int lengthSamples)
    {
        _start = start;
        _end = end;
        LengthSamples = lengthSamples;
    }

    public int LengthSamples { get; }

    public static AudioRoutingRamp Create(
        AudioRouting from,
        AudioRouting to,
        int sampleRate) =>
        Create(GainsFor(from), GainsFor(to), sampleRate);

    public static AudioRoutingRamp Create(
        AudioGains from,
        AudioGains to,
        int sampleRate)
    {
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sampleRate),
                sampleRate,
                "Sample rate must be positive.");
        }

        EnsureFinite(from, nameof(from));
        EnsureFinite(to, nameof(to));
        return new AudioRoutingRamp(
            from,
            to,
            Math.Max(1, sampleRate / 100));
    }

    public AudioGains AtSample(int sampleOffset)
    {
        if (sampleOffset < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sampleOffset),
                sampleOffset,
                "Sample offset cannot be negative.");
        }

        var position = Math.Min(sampleOffset, LengthSamples);
        var progress = (double)position / LengthSamples;
        return new AudioGains(
            Interpolate(_start.Desktop, _end.Desktop, progress),
            Interpolate(_start.Microphone, _end.Microphone, progress));
    }

    private static AudioGains GainsFor(AudioRouting routing) => routing switch
    {
        AudioRouting.Mixed => new AudioGains(1, 1),
        AudioRouting.DesktopOnly => new AudioGains(1, 0),
        AudioRouting.MicOnly => new AudioGains(0, 1),
        AudioRouting.Muted => new AudioGains(0, 0),
        _ => throw new ArgumentOutOfRangeException(
            nameof(routing),
            routing,
            "Unknown audio routing."),
    };

    private static double Interpolate(double start, double end, double progress) =>
        start + ((end - start) * progress);

    private static void EnsureFinite(AudioGains gains, string parameterName)
    {
        if (!double.IsFinite(gains.Desktop) ||
            !double.IsFinite(gains.Microphone))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                gains,
                "Audio gains must be finite.");
        }
    }
}
