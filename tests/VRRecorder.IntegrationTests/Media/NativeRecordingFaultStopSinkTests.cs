using VRRecorder.Application.Ports;
using VRRecorder.Application.Recording;
using VRRecorder.Domain.Timing;
using VRRecorder.Infrastructure.Media;

namespace VRRecorder.IntegrationTests.Media;

public sealed class NativeRecordingFaultStopSinkTests
{
    [Fact]
    public void ReportBeforeBindingFailsFast()
    {
        var sink = new NativeRecordingFaultStopSink();

        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            sink.Report(CreateHandle(), CreateFault());
        });

        Assert.Equal(
            "The native recording fault stop sink is not bound.",
            exception.Message);
    }

    [Fact]
    public void StopRequestSinkCanBeBoundOnlyOnce()
    {
        var sink = new NativeRecordingFaultStopSink();
        var first = new CapturingStopRequestSink();
        sink.Bind(first);

        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            sink.Bind(new CapturingStopRequestSink());
        });

        Assert.Equal(
            "The native recording fault stop sink is already bound.",
            exception.Message);
    }

    [Fact]
    public async Task DuplicateRuntimeFaultsRequestOneUncancellableEncoderFailureStop()
    {
        var stopRequests = new CapturingStopRequestSink();
        var sink = new NativeRecordingFaultStopSink();
        sink.Bind(stopRequests);
        var handle = CreateHandle();

        sink.Report(handle, CreateFault());
        sink.Report(handle, new NativeRecordingFault(9, "duplicate failure"));
        await sink.WaitForDispatchAsync(handle, CancellationToken.None);

        var dispatch = Assert.Single(stopRequests.Dispatches);
        Assert.Equal(
            new RecordingStopRequest(handle, RecordingStopReason.EncoderFailure),
            dispatch.Request);
        Assert.False(dispatch.CancellationToken.CanBeCanceled);
        Assert.Null(sink.LastFailure);
    }

    [Fact]
    public async Task AsynchronousStopFailureIsObservedWithoutEscapingReport()
    {
        var stopFailure = new IOException("native stop failed");
        var stopRequests = new CapturingStopRequestSink(stopFailure);
        var sink = new NativeRecordingFaultStopSink();
        sink.Bind(stopRequests);
        var handle = CreateHandle();
        var fault = CreateFault();

        var callbackFailure = Record.Exception(() =>
        {
            sink.Report(handle, fault);
        });
        await sink.WaitForDispatchAsync(handle, CancellationToken.None);

        Assert.Null(callbackFailure);
        var observed = Assert.IsType<NativeRecordingFaultStopFailure>(
            sink.LastFailure);
        Assert.Equal(handle, observed.Handle);
        Assert.Equal(fault, observed.Fault);
        Assert.Same(stopFailure, observed.Exception);
    }

    private static RecordingHandle CreateHandle() =>
        new(
            "native-session-001",
            MonotonicTimestamp.FromElapsed(TimeSpan.FromSeconds(3)));

    private static NativeRecordingFault CreateFault() =>
        new(6, "encoder failed while recording");

    private sealed class CapturingStopRequestSink(Exception? failure = null)
        : IStopRequestSink
    {
        public List<(RecordingStopRequest Request, CancellationToken CancellationToken)>
            Dispatches
        { get; } = [];

        public Task RequestStopAsync(
            RecordingStopRequest request,
            CancellationToken cancellationToken)
        {
            Dispatches.Add((request, cancellationToken));
            return failure is null
                ? Task.CompletedTask
                : Task.FromException(failure);
        }
    }
}
