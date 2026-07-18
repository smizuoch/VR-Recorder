using System.Collections.Concurrent;
using VRRecorder.Application.Audio;
using VRRecorder.Application.Ports;
using VRRecorder.Application.Recording;
using VRRecorder.Application.Settings;
using VRRecorder.Domain.Audio;
using VRRecorder.Domain.Encoding;
using VRRecorder.Domain.Timing;
using VRRecorder.Domain.Video;

namespace VRRecorder.Infrastructure.Media;

public sealed class NativeRecordingEngine
    : IRecordingEngine, IRecordingAudioRoutingGateway
{
    private readonly INativeRecordingBackend _backend;
    private readonly IMonotonicClock _clock;
    private readonly IAudioSessionEventSink _audioEvents;
    private readonly IRecordingMediaEventSink _mediaEvents;
    private readonly IRecordingEnvironmentSource? _environmentSource;
    private readonly INativeRecordingRuntimeFaultSink _runtimeFaults;
    private readonly IRecordingPartRollover? _partRollover;
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
        IRecordingPartRollover partRollover)
        : this(
            backend,
            clock,
            runtimeFaults,
            NullAudioSessionEventSink.Instance,
            NullRecordingMediaEventSink.Instance,
            environmentSource: null,
            partRollover)
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
        : this(
            backend,
            clock,
            runtimeFaults,
            audioEvents,
            mediaEvents,
            environmentSource: null)
    {
    }

    public NativeRecordingEngine(
        INativeRecordingBackend backend,
        IMonotonicClock clock,
        INativeRecordingRuntimeFaultSink runtimeFaults,
        IAudioSessionEventSink audioEvents,
        IRecordingMediaEventSink mediaEvents,
        IRecordingEnvironmentSource? environmentSource,
        IRecordingPartRollover? partRollover = null)
    {
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(runtimeFaults);
        ArgumentNullException.ThrowIfNull(audioEvents);
        ArgumentNullException.ThrowIfNull(mediaEvents);
        _backend = backend;
        _clock = clock;
        _runtimeFaults = runtimeFaults;
        _partRollover = partRollover;
        _audioEvents = audioEvents;
        _mediaEvents = mediaEvents;
        _environmentSource = environmentSource;
    }

    public async Task<RecordingHandle> StartAsync(
        RecordingPlan plan,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return await StartCoreAsync(
                plan,
                allowSoftwareFallback: AllowsSoftwareFallback(plan),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<RecordingHandle> StartCoreAsync(
        RecordingPlan plan,
        bool allowSoftwareFallback,
        CancellationToken cancellationToken)
    {
        var firstPacket = new TaskCompletionSource<MonotonicTimestamp>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var runtimeFaultContext = new RuntimeFaultContext(_runtimeFaults);
        var rolloverContext = new RolloverRequestContext(
            this,
            runtimeFaultContext);
        var geometryContext = new VideoGeometryRequestContext(
            this,
            runtimeFaultContext);
        var callbacks = CreateCallbacks(
            firstPacket,
            runtimeFaultContext,
            rolloverContext.Report,
            geometryContext.Report);
        INativeRecordingSession? session = null;
        ActiveSession? activeSession = null;
        try
        {
            session = await _backend
                .OpenAsync(
                    plan,
                    callbacks,
                    cancellationToken)
                .ConfigureAwait(false);
            ArgumentException.ThrowIfNullOrWhiteSpace(session.Id);
            activeSession = new ActiveSession(
                session,
                plan,
                runtimeFaultContext);
            if (!_sessions.TryAdd(session.Id, activeSession))
            {
                throw new InvalidOperationException(
                    $"Native recording session {session.Id} already exists.");
            }

            var committedAt = await firstPacket.Task
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            var handle = new RecordingHandle(session.Id, committedAt);
            runtimeFaultContext.Activate(handle);
            rolloverContext.Activate(handle, activeSession, session);
            geometryContext.Activate(handle, activeSession, session);
            PublishMediaBestEffort(plan);
            return handle;
        }
        catch (Exception exception)
        {
            if (session is not null)
            {
                _sessions.TryRemove(session.Id, out _);
                await session
                    .AbortAsync(CancellationToken.None)
                    .ConfigureAwait(false);
            }

            if (allowSoftwareFallback &&
                _partRollover is not null &&
                exception is NativeRecordingException nativeException &&
                nativeException.Fault.Source ==
                    NativeRecordingFaultSource.VideoEncoder &&
                !cancellationToken.IsCancellationRequested)
            {
                var fallbackPlan = await _partRollover
                    .PrepareSoftwareStartRetryAsync(
                        plan,
                        cancellationToken)
                    .ConfigureAwait(false);
                return await StartCoreAsync(
                        fallbackPlan,
                        allowSoftwareFallback: false,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

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
                if (activeSession.TerminalStopResult is { } terminalResult)
                {
                    _sessions.TryRemove(handle.Id, out _);
                    return terminalResult;
                }

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
            activeSession.AudioRouting = routing;
        }
        finally
        {
            activeSession.StopGate.Release();
        }
    }

    private static InvalidOperationException InactiveSession(
        RecordingHandle handle) => new(
        $"Native recording session {handle.Id} is not active.");

    private NativeRecordingCallbacks CreateCallbacks(
        TaskCompletionSource<MonotonicTimestamp> firstPacket,
        RuntimeFaultContext runtimeFaultContext,
        Action<NativeRecordingFault> videoEncoderFailed,
        Action<VideoGeometry> videoGeometryStable) => new(
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
        AvDrift: PublishAvDriftBestEffort,
        AudioBufferHealth: PublishAudioBufferHealthBestEffort,
        VideoEncoderFailed: videoEncoderFailed,
        VideoGeometryStable: videoGeometryStable);

    private void ScheduleRollover(
        RecordingHandle handle,
        ActiveSession activeSession,
        INativeRecordingSession sourceSession,
        NativeRecordingFault fault)
    {
        if (!ReferenceEquals(activeSession.Session, sourceSession))
        {
            return;
        }
        if (_partRollover is null ||
            !AllowsSoftwareFallback(activeSession.Plan))
        {
            activeSession.RuntimeFaults.Report(fault);
            return;
        }

        if (!activeSession.TryBeginRollover())
        {
            return;
        }

        _ = RolloverAsync(handle, activeSession, sourceSession, fault);
    }

    private async Task RolloverAsync(
        RecordingHandle handle,
        ActiveSession activeSession,
        INativeRecordingSession sourceSession,
        NativeRecordingFault sourceFault)
    {
        INativeRecordingSession? nextSession = null;
        var nextSessionCommitted = false;
        await activeSession.StopGate.WaitAsync(CancellationToken.None)
            .ConfigureAwait(false);
        try
        {
            if (!_sessions.TryGetValue(handle.Id, out var currentSession) ||
                !ReferenceEquals(activeSession, currentSession) ||
                !ReferenceEquals(activeSession.Session, sourceSession))
            {
                return;
            }

            var stopped = await activeSession.Session
                .StopAsync(CancellationToken.None)
                .ConfigureAwait(false);
            activeSession.TerminalStopResult = stopped;
            if (stopped.Statistics is { } statistics)
            {
                PublishMediaBestEffort(statistics);
            }

            var nextSegmentNumber = checked(activeSession.SegmentNumber + 1);
            var nextPlan = await _partRollover!
                .ReserveNextSoftwarePartAsync(
                    activeSession.Plan,
                    nextSegmentNumber,
                    activeSession.AudioRouting,
                    CancellationToken.None)
                .ConfigureAwait(false);
            if (nextPlan.Encoder !=
                EncoderKind.MediaFoundationSoftware)
            {
                throw new InvalidOperationException(
                    "A recording rollover must use the software encoder.");
            }

            var firstPacket = new TaskCompletionSource<MonotonicTimestamp>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var rolloverContext = new RolloverRequestContext(
                this,
                activeSession.RuntimeFaults);
            var geometryContext = new VideoGeometryRequestContext(
                this,
                activeSession.RuntimeFaults);
            var callbacks = CreateCallbacks(
                firstPacket,
                activeSession.RuntimeFaults,
                rolloverContext.Report,
                geometryContext.Report);
            nextSession = await _backend
                .OpenAsync(nextPlan, callbacks, CancellationToken.None)
                .ConfigureAwait(false);
            ArgumentException.ThrowIfNullOrWhiteSpace(nextSession.Id);
            _ = await firstPacket.Task
                .WaitAsync(TimeSpan.FromSeconds(10))
                .ConfigureAwait(false);

            activeSession.Session = nextSession;
            activeSession.Plan = nextPlan;
            activeSession.SegmentNumber = nextSegmentNumber;
            activeSession.TerminalStopResult = null;
            nextSessionCommitted = true;
            rolloverContext.Activate(handle, activeSession, nextSession);
            geometryContext.Activate(handle, activeSession, nextSession);
            PublishMediaBestEffort(nextPlan);
            await _partRollover
                .FinalizeIntermediatePartAsync(
                    stopped,
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            if (nextSession is not null && !nextSessionCommitted)
            {
                try
                {
                    await nextSession
                        .AbortAsync(CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // Preserve the rollover failure that owns this transition.
                }
            }

            activeSession.RuntimeFaults.Report(new NativeRecordingFault(
                sourceFault.Status,
                $"Software encoder rollover failed ({exception.GetType().Name})."));
        }
        finally
        {
            activeSession.EndRollover();
            activeSession.StopGate.Release();
        }
    }

    private static bool AllowsSoftwareFallback(RecordingPlan plan) =>
        plan.EncoderPreference == EncoderPreference.Auto &&
        plan.Encoder != EncoderKind.MediaFoundationSoftware;

    private void ScheduleVideoGeometryChange(
        RecordingHandle handle,
        ActiveSession activeSession,
        INativeRecordingSession sourceSession,
        VideoGeometry geometry)
    {
        if (!ReferenceEquals(activeSession.Session, sourceSession) ||
            !activeSession.TryBeginGeometryChange())
        {
            return;
        }

        _ = ApplyVideoGeometryChangeAsync(
            handle,
            activeSession,
            sourceSession,
            geometry);
    }

    private async Task ApplyVideoGeometryChangeAsync(
        RecordingHandle handle,
        ActiveSession activeSession,
        INativeRecordingSession sourceSession,
        VideoGeometry geometry)
    {
        await activeSession.StopGate.WaitAsync(CancellationToken.None)
            .ConfigureAwait(false);
        try
        {
            if (!_sessions.TryGetValue(handle.Id, out var currentSession) ||
                !ReferenceEquals(activeSession, currentSession) ||
                !ReferenceEquals(activeSession.Session, sourceSession))
            {
                return;
            }

            var nextSignal = activeSession.Plan.Signal.WithGeometry(geometry);
            switch (activeSession.Plan.VideoLayout.Policy)
            {
                case ResolutionChangePolicy.SingleFileFit:
                    await UpdateSingleFileGeometryAsync(
                            activeSession,
                            sourceSession,
                            nextSignal)
                        .ConfigureAwait(false);
                    break;
                case ResolutionChangePolicy.ExactFollowSegments:
                    await RolloverExactGeometryAsync(
                            handle,
                            activeSession,
                            sourceSession,
                            nextSignal)
                        .ConfigureAwait(false);
                    break;
                default:
                    throw new InvalidOperationException(
                        "The active resolution change policy is undefined.");
            }
        }
        catch (Exception exception)
        {
            activeSession.RuntimeFaults.Report(new NativeRecordingFault(
                (int)Native.NativeStatus.InternalError,
                $"Video geometry transition failed ({exception.GetType().Name})."));
        }
        finally
        {
            activeSession.EndGeometryChange();
            activeSession.StopGate.Release();
        }
    }

    private void PublishUpdatedPlan(ActiveSession activeSession)
    {
        PublishMediaBestEffort(activeSession.Plan);
    }

    private async Task UpdateSingleFileGeometryAsync(
        ActiveSession activeSession,
        INativeRecordingSession sourceSession,
        StableVideoSignal nextSignal)
    {
        var layout = activeSession.Plan.VideoLayout.ApplyStableSignal(
            nextSignal);
        await sourceSession.UpdateVideoLayoutAsync(
                layout,
                CancellationToken.None)
            .ConfigureAwait(false);
        activeSession.Plan = activeSession.Plan with
        {
            Signal = nextSignal,
            Media = activeSession.Plan.Media.WithVideoSource(nextSignal),
        };
        PublishUpdatedPlan(activeSession);
    }

    private async Task RolloverExactGeometryAsync(
        RecordingHandle handle,
        ActiveSession activeSession,
        INativeRecordingSession sourceSession,
        StableVideoSignal nextSignal)
    {
        if (_partRollover is null)
        {
            throw new InvalidOperationException(
                "Exact-follow recording requires a part rollover service.");
        }

        _ = RecordingVideoLayoutSession.StartExactSegment(nextSignal);
        INativeRecordingSession? nextSession = null;
        var nextSessionCommitted = false;
        try
        {
            var currentPlan = activeSession.Plan;
            var stopped = await sourceSession
                .StopAsync(CancellationToken.None)
                .ConfigureAwait(false);
            activeSession.TerminalStopResult = stopped;
            if (stopped.Statistics is { } statistics)
            {
                PublishMediaBestEffort(statistics);
            }

            var nextSegmentNumber = checked(activeSession.SegmentNumber + 1);
            var nextPlan = await _partRollover
                .ReserveNextExactPartAsync(
                    currentPlan,
                    nextSignal,
                    nextSegmentNumber,
                    activeSession.AudioRouting,
                    CancellationToken.None)
                .ConfigureAwait(false);
            if (nextPlan.VideoLayout.Policy !=
                    ResolutionChangePolicy.ExactFollowSegments ||
                nextPlan.Signal != nextSignal ||
                nextPlan.Encoder != currentPlan.Encoder)
            {
                throw new InvalidOperationException(
                    "An exact-follow rollover returned a mismatched plan.");
            }

            var firstPacket = new TaskCompletionSource<MonotonicTimestamp>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var rolloverContext = new RolloverRequestContext(
                this,
                activeSession.RuntimeFaults);
            var geometryContext = new VideoGeometryRequestContext(
                this,
                activeSession.RuntimeFaults);
            var callbacks = CreateCallbacks(
                firstPacket,
                activeSession.RuntimeFaults,
                rolloverContext.Report,
                geometryContext.Report);
            nextSession = await _backend
                .OpenAsync(nextPlan, callbacks, CancellationToken.None)
                .ConfigureAwait(false);
            ArgumentException.ThrowIfNullOrWhiteSpace(nextSession.Id);
            _ = await firstPacket.Task
                .WaitAsync(TimeSpan.FromSeconds(10))
                .ConfigureAwait(false);

            activeSession.Session = nextSession;
            activeSession.Plan = nextPlan;
            activeSession.SegmentNumber = nextSegmentNumber;
            activeSession.TerminalStopResult = null;
            nextSessionCommitted = true;
            rolloverContext.Activate(handle, activeSession, nextSession);
            geometryContext.Activate(handle, activeSession, nextSession);
            PublishUpdatedPlan(activeSession);
            await _partRollover
                .FinalizeIntermediatePartAsync(
                    stopped,
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch
        {
            if (nextSession is not null && !nextSessionCommitted)
            {
                try
                {
                    await nextSession
                        .AbortAsync(CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // Preserve the exact-follow transition failure.
                }
            }

            throw;
        }
    }

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
            if (_environmentSource is not null)
            {
                _mediaEvents.Publish(
                    _environmentSource.Capture(plan.Signal));
            }
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

    private void PublishAudioBufferHealthBestEffort(
        RecordingAudioBufferHealthEvent health)
    {
        try
        {
            _mediaEvents.Publish(health);
        }
        catch (Exception)
        {
            // Diagnostics cannot change a native callback outcome.
        }
    }

    private sealed class ActiveSession
    {
        private int _rolloverStarted;
        private int _geometryChangeStarted;

        public ActiveSession(
            INativeRecordingSession session,
            RecordingPlan plan,
            RuntimeFaultContext runtimeFaults)
        {
            Session = session;
            Plan = plan;
            RuntimeFaults = runtimeFaults;
            AudioRouting = plan.Media.AudioRouting;
        }

        public INativeRecordingSession Session { get; set; }

        public RecordingPlan Plan { get; set; }

        public RuntimeFaultContext RuntimeFaults { get; }

        public AudioRouting AudioRouting { get; set; }

        public int SegmentNumber { get; set; } = 1;

        public RecordingStopResult? TerminalStopResult { get; set; }

        public SemaphoreSlim StopGate { get; } = new(1, 1);

        public bool TryBeginRollover() =>
            Interlocked.CompareExchange(ref _rolloverStarted, 1, 0) == 0;

        public void EndRollover() =>
            Interlocked.Exchange(ref _rolloverStarted, 0);

        public bool TryBeginGeometryChange() =>
            Interlocked.CompareExchange(
                ref _geometryChangeStarted,
                1,
                0) == 0;

        public void EndGeometryChange() =>
            Interlocked.Exchange(ref _geometryChangeStarted, 0);
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

    private sealed class RolloverRequestContext(
        NativeRecordingEngine owner,
        RuntimeFaultContext runtimeFaults)
    {
        private readonly object _gate = new();
        private RecordingHandle? _handle;
        private ActiveSession? _activeSession;
        private INativeRecordingSession? _sourceSession;
        private NativeRecordingFault? _pendingFault;

        public void Activate(
            RecordingHandle handle,
            ActiveSession activeSession,
            INativeRecordingSession sourceSession)
        {
            NativeRecordingFault? pending;
            lock (_gate)
            {
                _handle = handle;
                _activeSession = activeSession;
                _sourceSession = sourceSession;
                pending = _pendingFault;
                _pendingFault = null;
            }

            if (pending is not null)
            {
                owner.ScheduleRollover(
                    handle,
                    activeSession,
                    sourceSession,
                    pending);
            }
        }

        public void Report(NativeRecordingFault fault)
        {
            RecordingHandle? handle;
            ActiveSession? activeSession;
            INativeRecordingSession? sourceSession;
            lock (_gate)
            {
                handle = _handle;
                activeSession = _activeSession;
                sourceSession = _sourceSession;
                if (handle is null || activeSession is null ||
                    sourceSession is null)
                {
                    _pendingFault ??= fault;
                    return;
                }
            }

            try
            {
                owner.ScheduleRollover(
                    handle,
                    activeSession,
                    sourceSession,
                    fault);
            }
            catch (Exception)
            {
                runtimeFaults.Report(fault);
            }
        }
    }

    private sealed class VideoGeometryRequestContext(
        NativeRecordingEngine owner,
        RuntimeFaultContext runtimeFaults)
    {
        private readonly object _gate = new();
        private RecordingHandle? _handle;
        private ActiveSession? _activeSession;
        private INativeRecordingSession? _sourceSession;
        private VideoGeometry? _pendingGeometry;

        public void Activate(
            RecordingHandle handle,
            ActiveSession activeSession,
            INativeRecordingSession sourceSession)
        {
            VideoGeometry? pending;
            lock (_gate)
            {
                _handle = handle;
                _activeSession = activeSession;
                _sourceSession = sourceSession;
                pending = _pendingGeometry;
                _pendingGeometry = null;
            }

            if (pending is not null)
            {
                owner.ScheduleVideoGeometryChange(
                    handle,
                    activeSession,
                    sourceSession,
                    pending);
            }
        }

        public void Report(VideoGeometry geometry)
        {
            RecordingHandle? handle;
            ActiveSession? activeSession;
            INativeRecordingSession? sourceSession;
            lock (_gate)
            {
                handle = _handle;
                activeSession = _activeSession;
                sourceSession = _sourceSession;
                if (handle is null || activeSession is null ||
                    sourceSession is null)
                {
                    _pendingGeometry ??= geometry;
                    return;
                }
            }

            try
            {
                owner.ScheduleVideoGeometryChange(
                    handle,
                    activeSession,
                    sourceSession,
                    geometry);
            }
            catch (Exception exception)
            {
                runtimeFaults.Report(new NativeRecordingFault(
                    (int)Native.NativeStatus.InternalError,
                    $"Video geometry callback failed ({exception.GetType().Name})."));
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
