using System.Collections.Concurrent;
using VRRecorder.Application.Ports;
using VRRecorder.Application.Recording;
using VRRecorder.Domain.Timing;

namespace VRRecorder.Infrastructure.Media;

public sealed class NativeRecordingEngine : IRecordingEngine
{
    private readonly INativeRecordingBackend _backend;
    private readonly IMonotonicClock _clock;
    private readonly INativeRecordingRuntimeFaultSink _runtimeFaults;
    private readonly ConcurrentDictionary<string, INativeRecordingSession> _sessions =
        new(StringComparer.Ordinal);

    public NativeRecordingEngine(
        INativeRecordingBackend backend,
        IMonotonicClock clock,
        INativeRecordingRuntimeFaultSink runtimeFaults)
    {
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(runtimeFaults);
        _backend = backend;
        _clock = clock;
        _runtimeFaults = runtimeFaults;
    }

    public async Task<RecordingHandle> StartAsync(
        RecordingPlan plan,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var firstPacket = new TaskCompletionSource<MonotonicTimestamp>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var callbacks = new NativeRecordingCallbacks(
            FirstVideoPacketMuxed: () =>
                firstPacket.TrySetResult(_clock.Now),
            Faulted: fault =>
            {
                if (!firstPacket.TrySetException(
                        new NativeRecordingException(fault)))
                {
                    _runtimeFaults.Report(fault);
                }
            });
        var session = await _backend
            .OpenAsync(
                plan,
                callbacks,
                cancellationToken)
            .ConfigureAwait(false);
        ArgumentException.ThrowIfNullOrWhiteSpace(session.Id);
        if (!_sessions.TryAdd(session.Id, session))
        {
            throw new InvalidOperationException(
                $"Native recording session {session.Id} already exists.");
        }

        try
        {
            var committedAt = await firstPacket.Task
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            return new RecordingHandle(session.Id, committedAt);
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

    public Task<RecordingStopResult> StopAsync(
        RecordingHandle handle,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handle);
        if (!_sessions.TryRemove(handle.Id, out var session))
        {
            throw new InvalidOperationException(
                $"Native recording session {handle.Id} is not active.");
        }

        return session.StopAsync(cancellationToken);
    }
}
