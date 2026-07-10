using VRRecorder.Application.Ports;
using VRRecorder.Domain.Audio;

namespace VRRecorder.Application.Audio;

public sealed class AudioSessionService
{
    public const int RequiredSampleRate = 48_000;
    public const int RequiredChannelCount = 2;
    public const int MaxScheduledFrameCount = 4_800;

    private readonly object _gate = new();
    private readonly IAudioEndpointRediscoveryScheduler _rediscovery;
    private readonly IAudioSessionEventSink _events;
    private AudioRouting _routing;
    private AudioRouting? _queuedRouting;
    private AudioRoutingRamp? _activeRamp;
    private int _rampFrameOffset;
    private AudioInputAvailability _availability = AudioInputAvailability.All;
    private long? _nextFrame;

    public AudioSessionService(
        AudioRouting initialRouting,
        IAudioEndpointRediscoveryScheduler rediscovery,
        IAudioSessionEventSink events)
    {
        EnsureDefined(initialRouting, nameof(initialRouting));
        ArgumentNullException.ThrowIfNull(rediscovery);
        ArgumentNullException.ThrowIfNull(events);
        _routing = initialRouting;
        _rediscovery = rediscovery;
        _events = events;
    }

    public void SetRouting(AudioRouting routing)
    {
        EnsureDefined(routing, nameof(routing));
        lock (_gate)
        {
            if (_activeRamp is not null)
            {
                _queuedRouting = routing;
                return;
            }

            BeginRoutingTransition(routing);
        }
    }

    public MixedStereoAudioBuffer Process(ScheduledStereoAudioBuffer buffer)
    {
        ValidateBuffer(buffer);
        List<AudioSessionWarning> warnings = [];
        List<AudioSessionStatus> statuses = [];
        AudioEndpointRediscoveryRequest? rediscoveryRequest;
        MixedStereoAudioBuffer output;

        lock (_gate)
        {
            EnsureScheduledPosition(buffer);
            rediscoveryRequest = ObserveAvailability(
                buffer,
                warnings,
                statuses);
            output = Mix(buffer);
            _nextFrame = checked(
                buffer.StartFrame + buffer.ScheduledFrameCount);
        }

        Publish(warnings, statuses);
        if (rediscoveryRequest is not null)
        {
            ScheduleRediscovery(
                rediscoveryRequest,
                buffer.StartFrame);
        }

        return output;
    }

    private static void ValidateBuffer(ScheduledStereoAudioBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentNullException.ThrowIfNull(buffer.DesktopInterleavedSamples);
        ArgumentNullException.ThrowIfNull(buffer.MicrophoneInterleavedSamples);
        if (buffer.StartFrame < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(buffer),
                buffer.StartFrame,
                "The scheduled start frame cannot be negative.");
        }

        if (buffer.SampleRate != RequiredSampleRate)
        {
            throw new ArgumentOutOfRangeException(
                nameof(buffer),
                buffer.SampleRate,
                "Audio sessions accept only 48 kHz sample buffers.");
        }

        if (buffer.ScheduledFrameCount <= 0 ||
            buffer.ScheduledFrameCount > MaxScheduledFrameCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(buffer),
                buffer.ScheduledFrameCount,
                "The scheduled frame count must be between 1 and 4,800.");
        }

        if (!Enum.IsDefined(buffer.Availability))
        {
            throw new ArgumentOutOfRangeException(
                nameof(buffer),
                buffer.Availability,
                "Unknown audio input availability.");
        }

        _ = checked(buffer.StartFrame + buffer.ScheduledFrameCount);
        ValidateInput(
            buffer.DesktopInterleavedSamples,
            buffer.ScheduledFrameCount,
            buffer.Availability.HasFlag(AudioInputAvailability.Desktop),
            nameof(buffer.DesktopInterleavedSamples));
        ValidateInput(
            buffer.MicrophoneInterleavedSamples,
            buffer.ScheduledFrameCount,
            buffer.Availability.HasFlag(AudioInputAvailability.Microphone),
            nameof(buffer.MicrophoneInterleavedSamples));
    }

    private static void ValidateInput(
        float[] samples,
        int scheduledFrameCount,
        bool available,
        string parameterName)
    {
        var expectedLength = available
            ? checked(scheduledFrameCount * RequiredChannelCount)
            : 0;
        if (samples.Length != expectedLength)
        {
            throw new ArgumentException(
                "Audio input availability and scheduled buffer length disagree.",
                parameterName);
        }

        if (samples.Any(sample => !float.IsFinite(sample)))
        {
            throw new ArgumentException(
                "Audio input samples must be finite.",
                parameterName);
        }
    }

    private void EnsureScheduledPosition(ScheduledStereoAudioBuffer buffer)
    {
        if (_nextFrame is { } expected && buffer.StartFrame != expected)
        {
            throw new ArgumentException(
                $"Expected scheduled frame {expected}, but received " +
                $"{buffer.StartFrame}.",
                nameof(buffer));
        }
    }

    private AudioEndpointRediscoveryRequest? ObserveAvailability(
        ScheduledStereoAudioBuffer buffer,
        List<AudioSessionWarning> warnings,
        List<AudioSessionStatus> statuses)
    {
        var previous = _availability;
        var current = buffer.Availability;
        foreach (var input in Enum.GetValues<AudioInput>())
        {
            var wasAvailable = IsAvailable(previous, input);
            var isAvailable = IsAvailable(current, input);
            if (wasAvailable && !isAvailable)
            {
                warnings.Add(new AudioSessionWarning(
                    AudioSessionWarningKind.InputUnavailable,
                    input,
                    buffer.StartFrame));
            }
            else if (!wasAvailable && isAvailable)
            {
                statuses.Add(new AudioSessionStatus(
                    AudioSessionStatusKind.InputRecovered,
                    input,
                    buffer.StartFrame));
            }
        }

        if (previous != current && _activeRamp is null)
        {
            var from = EffectiveRouting(_routing, previous);
            var to = EffectiveRouting(_routing, current);
            if (from != to)
            {
                _activeRamp = AudioRoutingRamp.Create(
                    from,
                    to,
                    RequiredSampleRate);
                _rampFrameOffset = 0;
            }
        }

        _availability = current;
        var desktopWasAvailable = previous.HasFlag(
            AudioInputAvailability.Desktop);
        var desktopIsAvailable = current.HasFlag(
            AudioInputAvailability.Desktop);
        return desktopWasAvailable && !desktopIsAvailable
            ? AudioDeviceRecoveryPolicy.ForDesktopLoss()
            : null;
    }

    private MixedStereoAudioBuffer Mix(ScheduledStereoAudioBuffer buffer)
    {
        float[] samples;
        if (_activeRamp is { } ramp)
        {
            samples = AudioMixer.MixInterleaved(
                buffer.DesktopInterleavedSamples,
                buffer.MicrophoneInterleavedSamples,
                buffer.ScheduledFrameCount,
                RequiredChannelCount,
                ramp,
                _rampFrameOffset,
                buffer.Availability);
            AdvanceRamp(buffer.ScheduledFrameCount);
        }
        else
        {
            var steadyRamp = AudioRoutingRamp.Create(
                _routing,
                _routing,
                RequiredSampleRate);
            samples = AudioMixer.Mix(
                buffer.DesktopInterleavedSamples,
                buffer.MicrophoneInterleavedSamples,
                checked(buffer.ScheduledFrameCount * RequiredChannelCount),
                steadyRamp.AtSample(0),
                buffer.Availability);
        }

        return new MixedStereoAudioBuffer(
            buffer.StartFrame,
            RequiredSampleRate,
            RequiredChannelCount,
            samples);
    }

    private void AdvanceRamp(int scheduledFrameCount)
    {
        var ramp = _activeRamp!;
        if (scheduledFrameCount < ramp.LengthSamples - _rampFrameOffset)
        {
            _rampFrameOffset += scheduledFrameCount;
            return;
        }

        _activeRamp = null;
        _rampFrameOffset = 0;
        if (_queuedRouting is { } queued)
        {
            _queuedRouting = null;
            BeginRoutingTransition(queued);
        }
    }

    private void BeginRoutingTransition(AudioRouting routing)
    {
        if (routing == _routing)
        {
            return;
        }

        _activeRamp = AudioRoutingRamp.Create(
            EffectiveRouting(_routing, _availability),
            EffectiveRouting(routing, _availability),
            RequiredSampleRate);
        _rampFrameOffset = 0;
        _routing = routing;
    }

    private void ScheduleRediscovery(
        AudioEndpointRediscoveryRequest request,
        long samplePosition)
    {
        try
        {
            _rediscovery.Schedule(request);
            Publish(new AudioSessionStatus(
                AudioSessionStatusKind.EndpointRediscoveryScheduled,
                request.Input,
                samplePosition,
                request.Budget));
        }
        catch (Exception failure)
        {
            Publish(new AudioSessionWarning(
                AudioSessionWarningKind.EndpointRediscoveryFailed,
                request.Input,
                samplePosition,
                failure));
        }
    }

    private void Publish(
        IEnumerable<AudioSessionWarning> warnings,
        IEnumerable<AudioSessionStatus> statuses)
    {
        foreach (var warning in warnings)
        {
            Publish(warning);
        }

        foreach (var status in statuses)
        {
            Publish(status);
        }
    }

    private void Publish(AudioSessionWarning warning)
    {
        try
        {
            _events.Publish(warning);
        }
        catch (Exception)
        {
            // Diagnostic sinks cannot interrupt the constant audio timeline.
        }
    }

    private void Publish(AudioSessionStatus status)
    {
        try
        {
            _events.Publish(status);
        }
        catch (Exception)
        {
            // Diagnostic sinks cannot interrupt the constant audio timeline.
        }
    }

    private static bool IsAvailable(
        AudioInputAvailability availability,
        AudioInput input) => input switch
    {
        AudioInput.Desktop => availability.HasFlag(AudioInputAvailability.Desktop),
        AudioInput.Microphone => availability.HasFlag(
            AudioInputAvailability.Microphone),
        _ => throw new ArgumentOutOfRangeException(
            nameof(input),
            input,
            "Unknown audio input."),
    };

    private static AudioRouting EffectiveRouting(
        AudioRouting routing,
        AudioInputAvailability availability)
    {
        var desktop = routing is AudioRouting.Mixed or AudioRouting.DesktopOnly &&
                      availability.HasFlag(AudioInputAvailability.Desktop);
        var microphone = routing is AudioRouting.Mixed or AudioRouting.MicOnly &&
                         availability.HasFlag(AudioInputAvailability.Microphone);
        return (desktop, microphone) switch
        {
            (true, true) => AudioRouting.Mixed,
            (true, false) => AudioRouting.DesktopOnly,
            (false, true) => AudioRouting.MicOnly,
            _ => AudioRouting.Muted,
        };
    }

    private static void EnsureDefined(AudioRouting routing, string parameterName)
    {
        if (!Enum.IsDefined(routing))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                routing,
                "Unknown audio routing.");
        }
    }
}
