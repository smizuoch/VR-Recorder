using System.Collections.Concurrent;
using VRRecorder.Application.Audio;
using VRRecorder.Application.Ports;
using VRRecorder.Application.Recording;
using VRRecorder.Domain.Audio;
using VRRecorder.Domain.Timing;

namespace VRRecorder.Infrastructure.Media;

public sealed class NativeRecordingEngine
    : IRecordingEngine, IRecordingAudioRoutingGateway
{
    private readonly INativeRecordingBackend _backend;
    private readonly IMonotonicClock _clock;
    private readonly IAudioSessionEventSink _audioEvents;
    private readonly IRecordingMediaEventSink _mediaEvents;
    private readonly INativeRecordingRuntimeFaultSink _runtimeFaults;
    private readonly ConcurrentDictionary<string, ActiveSession> _sessions =
        new(StringComparer.Ordinal);

    public NativeRecordingEngine(
        INativeRecordingBackend backend,
        IMonotonicClock clock,
        INativeRecordingRuntimeFaultSink runtimeFaults)
        : this(
            backend,
            clock,
            runtimeFaults,
            NullAudioSessionEventSink.Instance,
            NullRecordingMediaEventSink.Instance)
    {
    }

    public NativeRecordingEngine(
        INativeRecordingBackend backend,
        IMonotonicClock clock,
        INativeRecordingRuntimeFaultSink runtimeFaults,
        IAudioSessionEventSink audioEvents)
        : this(
            backend,
            clock,
            runtimeFaults,
            audioEvents,
            NullRecordingMediaEventSink.Instance)
    {
    }

    public NativeRecordingEngine(
        INativeRecordingBackend backend,
        IMonotonicClock clock,
        INativeRecordingRuntimeFaultSink runtimeFaults,
        IAudioSessionEventSink audioEvents,
        IRecordingMediaEventSink mediaEvents)
    {
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(runtimeFaults);
        ArgumentNullException.ThrowIfNull(audioEvents);
        ArgumentNullException.ThrowIfNull(mediaEvents);
        _backend = backend;
        _clock = clock;
        _runtimeFaults = runtimeFaults;
        _audioEvents = audioEvents;
        _mediaEvents = mediaEvents;
    }

    public async Task<RecordingHandle> StartAsync(
        RecordingPlan plan,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var firstPacket = new TaskCompletionSource<MonotonicTimestamp>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var runtimeFaultContext = new RuntimeFaultContext(_runtimeFaults);
        var callbacks = new NativeRecordingCallbacks(
            FirstVideoPacketMuxed: () =>
                firstPacket.TrySetResult(_clock.Now),
            Faulted: fault =>
            {
                if (!firstPacket.TrySetException(
                        new NativeRecordingException(fault)))
                {
                    runtimeFaultContext.Report(fault);
                }
            },
            AudioWarning: PublishAudioBestEffort,
            AudioStatus: PublishAudioBestEffort,
            AvDrift: PublishAvDriftBestEffort);
        var session = await _backend
            .OpenAsync(
                plan,
                callbacks,
                cancellationToken)
            .ConfigureAwait(false);
        ArgumentException.ThrowIfNullOrWhiteSpace(session.Id);
        var activeSession = new ActiveSession(session);
        if (!_sessions.TryAdd(session.Id, activeSession))
        {
            await session
                .AbortAsync(CancellationToken.None)
                .ConfigureAwait(false);
            throw new InvalidOperationException(
                $"Native recording session {session.Id} already exists.");
        }

        try
        {
            var committedAt = await firstPacket.Task
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            var handle = new RecordingHandle(session.Id, committedAt);
            runtimeFaultContext.Activate(handle);
            PublishMediaBestEffort(plan);
            return handle;
        }
        catch
        {
            _sessions.TryRemove(session.Id, out _);
            await session
                .AbortAsync(CancellationToken.None)
                .ConfigureAwait(false);
            throw;
        }
    }

    private void PublishAudioBestEffort(AudioSessionWarning warning)
    {
        try
        {
            _audioEvents.Publish(warning);
        }
        catch (Exception)
        {
            // Audio observers cannot interrupt the native media timeline.
        }
    }

    private void PublishAudioBestEffort(AudioSessionStatus status)
    {
        try
        {
            _audioEvents.Publish(status);
        }
        catch (Exception)
        {
            // Audio observers cannot interrupt the native media timeline.
        }
    }

    public async Task<RecordingStopResult> StopAsync(
        RecordingHandle handle,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handle);
        if (!_sessions.TryGetValue(handle.Id, out var activeSession))
        {
            throw new InvalidOperationException(
                $"Native recording session {handle.Id} is not active.");
        }

        await activeSession.StopGate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            if (!_sessions.TryGetValue(handle.Id, out var currentSession) ||
                !ReferenceEquals(activeSession, currentSession))
            {
                throw new InvalidOperationException(
                    $"Native recording session {handle.Id} is not active.");
            }

            try
            {
                var result = await activeSession.Session
                    .StopAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (result.Statistics is { } statistics)
                {
                    PublishMediaBestEffort(statistics);
                }

                _sessions.TryRemove(handle.Id, out _);
                return result;
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                _sessions.TryRemove(handle.Id, out _);
                try
                {
                    await activeSession.Session
                        .AbortAsync(CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // Preserve the stop failure that made the session terminal.
                }

                throw;
            }
        }
        finally
        {
            activeSession.StopGate.Release();
        }
    }

    public async Task UpdateAudioRoutingAsync(
        RecordingHandle handle,
        AudioRouting routing,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handle);
        if (!_sessions.TryGetValue(handle.Id, out var activeSession))
        {
            throw InactiveSession(handle);
        }

        await activeSession.StopGate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            if (!_sessions.TryGetValue(handle.Id, out var currentSession) ||
                !ReferenceEquals(activeSession, currentSession))
            {
                throw InactiveSession(handle);
            }

            await activeSession.Session
                .UpdateAudioRoutingAsync(routing, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            activeSession.StopGate.Release();
        }
    }

    private static InvalidOperationException InactiveSession(
        RecordingHandle handle) => new(
        $"Native recording session {handle.Id} is not active.");

    private void PublishMediaBestEffort(RecordingPlan plan)
    {
        try
        {
            var layout = plan.VideoLayout.CurrentLayout;
            _mediaEvents.Publish(new RecordingMediaProfile(
                plan.Signal.Width,
                plan.Signal.Height,
                plan.Signal.PixelFormat,
                plan.Signal.EstimatedSourceFramesPerSecond,
                layout.OutputCanvas.Width,
                layout.OutputCanvas.Height,
                plan.FrameRate.Value,
                plan.Encoder,
                plan.Signal.GpuVendor));
        }
        catch (Exception)
        {
            // Diagnostics cannot change a committed recording start.
        }
    }

    private void PublishMediaBestEffort(
        RecordingSessionStatistics statistics)
    {
        try
        {
            _mediaEvents.Publish(statistics);
        }
        catch (Exception)
        {
            // Diagnostics cannot change a successful native stop.
        }
    }

    private void PublishAvDriftBestEffort(NativeAvDriftEvent drift)
    {
        try
        {
            _mediaEvents.Publish(new RecordingAvDriftEvent(
                drift.VideoPts,
                drift.AudioPts,
                drift.AbsoluteDrift));
        }
        catch (Exception)
        {
            // Diagnostics cannot interrupt an active recording.
        }
    }

    private sealed class ActiveSession
    {
        public ActiveSession(INativeRecordingSession session)
        {
            Session = session;
        }

        public INativeRecordingSession Session { get; }

        public SemaphoreSlim StopGate { get; } = new(1, 1);
    }

    private sealed class NullRecordingMediaEventSink
        : IRecordingMediaEventSink
    {
        public static NullRecordingMediaEventSink Instance { get; } = new();

        public void Publish(RecordingMediaProfile profile)
        {
        }

        public void Publish(RecordingSessionStatistics statistics)
        {
        }
    }

    private sealed class RuntimeFaultContext(
        INativeRecordingRuntimeFaultSink sink)
    {
        private readonly object _gate = new();
        private RecordingHandle? _handle;
        private NativeRecordingFault? _pendingFault;

        public void Activate(RecordingHandle handle)
        {
            NativeRecordingFault? pending;
            lock (_gate)
            {
                _handle = handle;
                pending = _pendingFault;
                _pendingFault = null;
            }

            if (pending is not null)
            {
                ReportBestEffort(handle, pending);
            }
        }

        public void Report(NativeRecordingFault fault)
        {
            RecordingHandle? handle;
            lock (_gate)
            {
                handle = _handle;
                if (handle is null)
                {
                    _pendingFault ??= fault;
                    return;
                }
            }

            ReportBestEffort(handle, fault);
        }

        private void ReportBestEffort(
            RecordingHandle handle,
            NativeRecordingFault fault)
        {
            try
            {
                sink.Report(handle, fault);
            }
            catch (Exception)
            {
                // A callback observer must not escape through the native ABI
                // or replace the encoder failure that owns this session.
            }
        }
    }

    private sealed class NullAudioSessionEventSink : IAudioSessionEventSink
    {
        public static NullAudioSessionEventSink Instance { get; } = new();

        public void Publish(AudioSessionWarning warning)
        {
        }

        public void Publish(AudioSessionStatus status)
        {
        }
    }
}
