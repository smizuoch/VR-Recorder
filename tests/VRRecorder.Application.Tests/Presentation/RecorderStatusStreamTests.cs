using VRRecorder.Application.Presentation;
using VRRecorder.Domain.Recording;

namespace VRRecorder.Application.Tests.Presentation;

public sealed class RecorderStatusStreamTests
{
    [Theory]
    [InlineData(RecorderState.Booting, RecorderAvailableActions.None)]
    [InlineData(RecorderState.ComplianceFault, RecorderAvailableActions.None)]
    [InlineData(RecorderState.Ready, RecorderAvailableActions.Start)]
    [InlineData(RecorderState.Arming, RecorderAvailableActions.Cancel)]
    [InlineData(RecorderState.Countdown, RecorderAvailableActions.Cancel)]
    [InlineData(RecorderState.Starting, RecorderAvailableActions.None)]
    [InlineData(RecorderState.Recording, RecorderAvailableActions.Stop)]
    [InlineData(RecorderState.SignalLost, RecorderAvailableActions.Stop)]
    [InlineData(RecorderState.Stopping, RecorderAvailableActions.None)]
    [InlineData(RecorderState.NoSignal, RecorderAvailableActions.Retry)]
    [InlineData(RecorderState.Faulted, RecorderAvailableActions.None)]
    public void FactoryProvidesOneAuthoritativeStateToActionMapping(
        RecorderState state,
        RecorderAvailableActions expected)
    {
        var status = RecorderStatusSnapshot.Create(7, state);

        Assert.Equal(7, status.Revision);
        Assert.Equal(state, status.State);
        Assert.Equal(expected, status.AvailableActions);
    }

    [Fact]
    public void StreamReplaysCurrentAndRejectsDuplicateOrOutOfOrderUpdates()
    {
        using var stream = new RecorderStatusHub(
            RecorderStatusSnapshot.Create(4, RecorderState.Arming));
        List<RecorderStatusSnapshot> observed = [];
        using var subscription = stream.Subscribe(observed.Add);

        var duplicate = stream.TryPublish(
            RecorderStatusSnapshot.Create(4, RecorderState.Countdown));
        var stale = stream.TryPublish(
            RecorderStatusSnapshot.Create(3, RecorderState.Ready));
        var next = stream.TryPublish(
            RecorderStatusSnapshot.Create(5, RecorderState.Countdown));

        Assert.False(duplicate);
        Assert.False(stale);
        Assert.True(next);
        Assert.Equal(
            [
                RecorderStatusSnapshot.Create(4, RecorderState.Arming),
                RecorderStatusSnapshot.Create(5, RecorderState.Countdown),
            ],
            observed);
        Assert.Equal(observed[^1], stream.Current);
    }

    [Fact]
    public void ThrowingSubscriberCannotBlockRecordingStatusOrOtherSubscribers()
    {
        using var stream = new RecorderStatusHub(
            RecorderStatusSnapshot.Create(0, RecorderState.Ready));
        using var throwing = stream.Subscribe(_ =>
            throw new InvalidOperationException("subscriber failed"));
        List<RecorderStatusSnapshot> observed = [];
        using var healthy = stream.Subscribe(observed.Add);

        var published = stream.TryPublish(
            RecorderStatusSnapshot.Create(1, RecorderState.Arming));

        Assert.True(published);
        Assert.Equal(RecorderState.Arming, stream.Current.State);
        Assert.Equal(
            [
                RecorderStatusSnapshot.Create(0, RecorderState.Ready),
                RecorderStatusSnapshot.Create(1, RecorderState.Arming),
            ],
            observed);
    }

    [Fact]
    public void UnsubscribeAndDisposePreventLateDelivery()
    {
        var stream = new RecorderStatusHub(
            RecorderStatusSnapshot.Create(0, RecorderState.Ready));
        List<RecorderStatusSnapshot> observed = [];
        var subscription = stream.Subscribe(observed.Add);
        subscription.Dispose();

        Assert.True(stream.TryPublish(
            RecorderStatusSnapshot.Create(1, RecorderState.Arming)));
        stream.Dispose();

        Assert.Single(observed);
        Assert.False(stream.TryPublish(
            RecorderStatusSnapshot.Create(2, RecorderState.Countdown)));
        Assert.Throws<ObjectDisposedException>(() =>
            stream.Subscribe(_ => { }));
    }

    [Fact]
    public async Task ConcurrentInitialReplayAndPublishAreQueuedInRevisionOrder()
    {
        using var stream = new RecorderStatusHub(
            RecorderStatusSnapshot.Create(0, RecorderState.Ready));
        var initialEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseInitial = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        List<long> revisions = [];
        var activeCallbacks = 0;
        var maximumActiveCallbacks = 0;

        var subscribing = Task.Run(() => stream.Subscribe(status =>
        {
            var active = Interlocked.Increment(ref activeCallbacks);
            InterlockedExtensions.Max(ref maximumActiveCallbacks, active);
            lock (revisions)
            {
                revisions.Add(status.Revision);
            }

            try
            {
                if (status.Revision == 0)
                {
                    initialEntered.TrySetResult();
                    releaseInitial.Task.GetAwaiter().GetResult();
                }
            }
            finally
            {
                Interlocked.Decrement(ref activeCallbacks);
            }
        }));
        await initialEntered.Task.WaitAsync(TimeSpan.FromSeconds(1));

        var publishing = Task.Run(() => stream.TryPublish(
            RecorderStatusSnapshot.Create(1, RecorderState.Arming)));
        await publishing.WaitAsync(TimeSpan.FromSeconds(1));
        releaseInitial.TrySetResult();
        using var subscription = await subscribing.WaitAsync(
            TimeSpan.FromSeconds(1));

        Assert.True(await publishing);
        Assert.Equal([0, 1], revisions);
        Assert.Equal(1, maximumActiveCallbacks);
    }

    [Fact]
    public async Task UnsubscribeWaitsForInflightCallbackAndBarsLaterDelivery()
    {
        using var stream = new RecorderStatusHub(
            RecorderStatusSnapshot.Create(0, RecorderState.Ready));
        var callbackEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCallback = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var callCount = 0;
        var subscription = stream.Subscribe(status =>
        {
            Interlocked.Increment(ref callCount);
            if (status.Revision == 1)
            {
                callbackEntered.TrySetResult();
                releaseCallback.Task.GetAwaiter().GetResult();
            }
        });
        var publishing = Task.Run(() => stream.TryPublish(
            RecorderStatusSnapshot.Create(1, RecorderState.Arming)));
        await callbackEntered.Task.WaitAsync(TimeSpan.FromSeconds(1));

        var unsubscribeAttempted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var unsubscribing = Task.Run(() =>
        {
            unsubscribeAttempted.TrySetResult();
            subscription.Dispose();
        });
        await unsubscribeAttempted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.False(unsubscribing.IsCompleted);
        releaseCallback.TrySetResult();
        await Task.WhenAll(publishing, unsubscribing).WaitAsync(
            TimeSpan.FromSeconds(1));
        Assert.True(stream.TryPublish(
            RecorderStatusSnapshot.Create(2, RecorderState.Countdown)));
        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task HubDisposeWaitsForInflightCallbackAndBarsLaterDelivery()
    {
        var stream = new RecorderStatusHub(
            RecorderStatusSnapshot.Create(0, RecorderState.Ready));
        var callbackEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCallback = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = stream.Subscribe(status =>
        {
            if (status.Revision == 1)
            {
                callbackEntered.TrySetResult();
                releaseCallback.Task.GetAwaiter().GetResult();
            }
        });
        var publishing = Task.Run(() => stream.TryPublish(
            RecorderStatusSnapshot.Create(1, RecorderState.Arming)));
        await callbackEntered.Task.WaitAsync(TimeSpan.FromSeconds(1));

        var disposeAttempted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var disposing = Task.Run(() =>
        {
            disposeAttempted.TrySetResult();
            stream.Dispose();
        });
        await disposeAttempted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.False(disposing.IsCompleted);
        releaseCallback.TrySetResult();
        await Task.WhenAll(publishing, disposing).WaitAsync(
            TimeSpan.FromSeconds(1));
        Assert.False(stream.TryPublish(
            RecorderStatusSnapshot.Create(2, RecorderState.Countdown)));
    }

    [Fact]
    public async Task SubscriberCanReenterPublishAndUnsubscribeWithoutDeadlock()
    {
        using var stream = new RecorderStatusHub(
            RecorderStatusSnapshot.Create(0, RecorderState.Ready));
        List<long> revisions = [];
        IDisposable? subscription = null;
        subscription = stream.Subscribe(status =>
        {
            revisions.Add(status.Revision);
            if (status.Revision == 1)
            {
                Assert.True(stream.TryPublish(
                    RecorderStatusSnapshot.Create(2, RecorderState.Countdown)));
                subscription!.Dispose();
            }
        });

        var publishing = Task.Run(() => stream.TryPublish(
            RecorderStatusSnapshot.Create(1, RecorderState.Arming)));

        Assert.True(await publishing.WaitAsync(TimeSpan.FromSeconds(1)));
        Assert.Equal([0, 1], revisions);
        Assert.Equal(2, stream.Current.Revision);
    }

    private static class InterlockedExtensions
    {
        public static void Max(ref int target, int candidate)
        {
            var current = Volatile.Read(ref target);
            while (candidate > current)
            {
                var observed = Interlocked.CompareExchange(
                    ref target,
                    candidate,
                    current);
                if (observed == current)
                {
                    return;
                }

                current = observed;
            }
        }
    }
}
