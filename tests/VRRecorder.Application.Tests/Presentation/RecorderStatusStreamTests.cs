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
}
