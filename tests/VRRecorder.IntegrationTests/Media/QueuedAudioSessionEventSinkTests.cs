using VRRecorder.Application.Audio;
using VRRecorder.Application.Ports;
using VRRecorder.Domain.Audio;
using VRRecorder.Infrastructure.Media;

namespace VRRecorder.IntegrationTests.Media;

public sealed class QueuedAudioSessionEventSinkTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task PublishDoesNotWaitAndBoundedBacklogKeepsNewestEvents()
    {
        var observer = new BlockingAudioSessionEventSink();
        using var sink = new QueuedAudioSessionEventSink(
            observer,
            capacity: 2);
        var first = Warning(AudioInput.Desktop, framePosition: 100);
        var second = Warning(AudioInput.Microphone, framePosition: 200);
        var third = Recovered(AudioInput.Desktop, framePosition: 300);
        var fourth = Recovered(AudioInput.Microphone, framePosition: 400);

        var publishing = Task.Run(() => sink.Publish(first));
        try
        {
            await observer.FirstDeliveryStarted.Task.WaitAsync(TestTimeout);
            await publishing.WaitAsync(TestTimeout);
            sink.Publish(second);
            sink.Publish(third);
            sink.Publish(fourth);
        }
        finally
        {
            observer.ReleaseFirstDelivery.Set();
        }

        sink.Dispose();

        Assert.Equal([first, third, fourth], observer.Events);
    }

    [Fact]
    public void ObserverFailureDoesNotStopDeliveryAndDisposeIsIdempotent()
    {
        var observer = new ThrowingFirstAudioSessionEventSink();
        var sink = new QueuedAudioSessionEventSink(observer);
        var warning = Warning(AudioInput.Desktop, framePosition: 100);
        var recovered = Recovered(AudioInput.Desktop, framePosition: 200);

        sink.Publish(warning);
        sink.Publish(recovered);
        sink.Dispose();
        sink.Dispose();
        sink.Publish(Warning(AudioInput.Microphone, framePosition: 300));

        Assert.Equal([warning, recovered], observer.Events);
    }

    private static AudioSessionWarning Warning(
        AudioInput input,
        long framePosition) =>
        new(
            AudioSessionWarningKind.InputUnavailable,
            input,
            framePosition);

    private static AudioSessionStatus Recovered(
        AudioInput input,
        long framePosition) =>
        new(
            AudioSessionStatusKind.InputRecovered,
            input,
            framePosition);

    private sealed class BlockingAudioSessionEventSink
        : IAudioSessionEventSink
    {
        private readonly Lock _gate = new();
        private int _deliveryCount;

        public List<object> Events { get; } = [];

        public TaskCompletionSource FirstDeliveryStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public ManualResetEventSlim ReleaseFirstDelivery { get; } = new(false);

        public void Publish(AudioSessionWarning warning) => Deliver(warning);

        public void Publish(AudioSessionStatus status) => Deliver(status);

        private void Deliver(object audioEvent)
        {
            lock (_gate)
            {
                Events.Add(audioEvent);
            }

            if (Interlocked.Increment(ref _deliveryCount) == 1)
            {
                FirstDeliveryStarted.TrySetResult();
                ReleaseFirstDelivery.Wait(TestTimeout);
            }
        }
    }

    private sealed class ThrowingFirstAudioSessionEventSink
        : IAudioSessionEventSink
    {
        private int _deliveryCount;

        public List<object> Events { get; } = [];

        public void Publish(AudioSessionWarning warning) => Deliver(warning);

        public void Publish(AudioSessionStatus status) => Deliver(status);

        private void Deliver(object audioEvent)
        {
            Events.Add(audioEvent);
            if (Interlocked.Increment(ref _deliveryCount) == 1)
            {
                throw new IOException("diagnostic storage unavailable");
            }
        }
    }
}
