using VRRecorder.Infrastructure.Media;

namespace VRRecorder.IntegrationTests.Media;

public sealed class RecordingRuntimeResourceLifetimeTests
{
    [Fact]
    public async Task ConcurrentDisposalReleasesResourcesOnceInReverseOrder()
    {
        var events = new List<string>();
        var first = new TrackingDisposable("first", events);
        var second = new TrackingDisposable("second", events);
        var third = new TrackingDisposable("third", events);
        var lifetime = new RecordingRuntimeResourceLifetime(
            first,
            second,
            third);

        var firstDisposal = lifetime.DisposeAsync().AsTask();
        var secondDisposal = lifetime.DisposeAsync().AsTask();
        await Task.WhenAll(firstDisposal, secondDisposal);

        Assert.Same(firstDisposal, secondDisposal);
        Assert.Equal(["third", "second", "first"], events);
        Assert.Equal(1, first.DisposeCallCount);
        Assert.Equal(1, second.DisposeCallCount);
        Assert.Equal(1, third.DisposeCallCount);
    }

    [Fact]
    public async Task DisposalContinuesAndReportsEveryResourceFailure()
    {
        var events = new List<string>();
        var firstFailure = new IOException("first failed");
        var thirdFailure = new InvalidOperationException("third failed");
        var lifetime = new RecordingRuntimeResourceLifetime(
            new TrackingDisposable("first", events, firstFailure),
            new TrackingDisposable("second", events),
            new TrackingDisposable("third", events, thirdFailure));

        var failure = await Assert.ThrowsAsync<AggregateException>(async () =>
            await lifetime.DisposeAsync());

        Assert.Equal(["third", "second", "first"], events);
        Assert.Equal([thirdFailure, firstFailure], failure.InnerExceptions);
        var repeated = await Assert.ThrowsAsync<AggregateException>(async () =>
            await lifetime.DisposeAsync());
        Assert.Same(failure, repeated);
        Assert.Equal(["third", "second", "first"], events);
    }

    private sealed class TrackingDisposable(
        string name,
        List<string> events,
        Exception? failure = null) : IDisposable
    {
        public int DisposeCallCount { get; private set; }

        public void Dispose()
        {
            DisposeCallCount++;
            events.Add(name);
            if (failure is not null)
            {
                throw failure;
            }
        }
    }
}
