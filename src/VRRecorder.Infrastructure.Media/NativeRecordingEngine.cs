using System.Collections.Concurrent;
using VRRecorder.Application.Audio;
using VRRecorder.Application.Ports;
using VRRecorder.Application.Recording;
using VRRecorder.Domain.Timing;

namespace VRRecorder.Infrastructure.Media;

public sealed class NativeRecordingEngine : IRecordingEngine
{
    private readonly INativeRecordingBackend _backend;
    private readonly IMonotonicClock _clock;
    private readonly IAudioSessionEventSink _audioEvents;
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
            NullAudioSessionEventSink.Instance)
    {
    }

    public NativeRecordingEngine(
        INativeRecordingBackend backend,
        IMonotonicClock clock,
        INativeRecordingRuntimeFaultSink runtimeFaults,
        IAudioSessionEventSink audioEvents)
    {
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(runtimeFaults);
        ArgumentNullException.ThrowIfNull(audioEvents);
        _backend = backend;
        _clock = clock;
        _runtimeFaults = runtimeFaults;
        _audioEvents = audioEvents;
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
            AudioStatus: PublishAudioBestEffort);
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

    private sealed class ActiveSession
    {
        public ActiveSession(INativeRecordingSession session)
        {
            Session = session;
        }

        public INativeRecordingSession Session { get; }

        public SemaphoreSlim StopGate { get; } = new(1, 1);
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
