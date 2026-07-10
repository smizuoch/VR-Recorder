using System.Net;
using VRRecorder.Infrastructure.Osc;

namespace VRRecorder.IntegrationTests.Osc;

public sealed class WindowsDnsSdApiTests
{
    [Fact]
    public async Task BrowseCancellationKeepsCallbackAliveUntilTerminalCallback()
    {
        var native = new ControllableWindowsDnsServiceNativeApi();
        var api = new WindowsDnsSdApi(native, TimeSpan.FromMinutes(1));
        using var cancellation = new CancellationTokenSource();

        var browsing = api.BrowseAsync(
            "_oscjson._tcp.local",
            cancellation.Token);
        await native.BrowseStarted.Task;
        await cancellation.CancelAsync();
        await native.BrowseOperation.CancelCalled.Task;

        Assert.False(native.BrowseOperation.IsDisposed);

        native.CompleteBrowseCancellation();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => browsing);
        Assert.True(native.BrowseOperation.IsDisposed);
        Assert.Equal(1, native.BrowseOperation.CancelCallCount);
    }

    [Fact]
    public async Task ResolveMapsHostPortAddressesAndTxtBeforeDisposal()
    {
        var native = new ControllableWindowsDnsServiceNativeApi();
        var api = new WindowsDnsSdApi(native, TimeSpan.Zero);
        var serviceId = "VRChat-Client-alpha._oscjson._tcp.local.";
        var expected = new WindowsDnsSdResolvedService(
            serviceId,
            "alpha-host.local.",
            [IPAddress.Loopback, IPAddress.IPv6Loopback],
            19001,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["name"] = "alpha",
                ["txtvers"] = "1",
            });

        var resolving = api.ResolveAsync(serviceId, CancellationToken.None);
        await native.ResolveStarted.Task;

        Assert.False(native.ResolveOperation.IsDisposed);

        native.CompleteResolve(expected);
        var actual = await resolving;

        Assert.NotNull(actual);
        Assert.Equal(expected.ServiceInstanceName, actual.ServiceInstanceName);
        Assert.Equal(expected.HostName, actual.HostName);
        Assert.Equal(expected.Addresses, actual.Addresses);
        Assert.Equal(expected.Port, actual.Port);
        Assert.Equal(expected.TextProperties, actual.TextProperties);
        Assert.True(native.ResolveOperation.IsDisposed);
    }

    [Fact]
    public async Task ResolveCancellationDoesNotRequireNativeCallback()
    {
        var native = new ControllableWindowsDnsServiceNativeApi();
        var api = new WindowsDnsSdApi(native, TimeSpan.Zero);
        using var cancellation = new CancellationTokenSource();

        var resolving = api.ResolveAsync(
            "VRChat-Client-alpha._oscjson._tcp.local.",
            cancellation.Token);
        await native.ResolveStarted.Task;

        await cancellation.CancelAsync();
        var completed = await Task.WhenAny(
            resolving,
            Task.Delay(TimeSpan.FromSeconds(1)));
        if (completed != resolving)
        {
            native.CompleteResolveCancellation();
        }

        Assert.Same(resolving, completed);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => resolving);
        Assert.Equal(1, native.ResolveOperation.CancelCallCount);
        Assert.True(native.ResolveOperation.IsDisposed);
    }

    private sealed class ControllableWindowsDnsServiceNativeApi
        : IWindowsDnsServiceNativeApi
    {
        private WindowsDnsServiceBrowseCallback? _browseCallback;
        private WindowsDnsServiceResolveCallback? _resolveCallback;

        public bool IsSupported => true;

        public TaskCompletionSource BrowseStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ResolveStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public ControllableOperation BrowseOperation { get; private set; } =
            null!;

        public ControllableOperation ResolveOperation { get; private set; } =
            null!;

        public IWindowsDnsServiceOperation StartBrowse(
            string queryName,
            WindowsDnsServiceBrowseCallback callback)
        {
            _browseCallback = callback;
            BrowseOperation = new ControllableOperation();
            callback(
                status: 0,
                [
                    "VRChat-Client-zeta._oscjson._tcp.local.",
                    "VRChat-Client-alpha._oscjson._tcp.local.",
                    "VRChat-Client-zeta._oscjson._tcp.local.",
                ]);
            BrowseStarted.TrySetResult();
            return BrowseOperation;
        }

        public IWindowsDnsServiceOperation StartResolve(
            string serviceInstanceName,
            WindowsDnsServiceResolveCallback callback)
        {
            _resolveCallback = callback;
            ResolveOperation = new ControllableOperation();
            ResolveStarted.TrySetResult();
            return ResolveOperation;
        }

        public void CompleteBrowseCancellation() =>
            (_browseCallback ?? throw new InvalidOperationException(
                "Browse was not started."))(
                status: 1223,
                []);

        public void CompleteResolve(WindowsDnsSdResolvedService service) =>
            (_resolveCallback ?? throw new InvalidOperationException(
                "Resolve was not started."))(
                status: 0,
                service);

        public void CompleteResolveCancellation() =>
            (_resolveCallback ?? throw new InvalidOperationException(
                "Resolve was not started."))(
                status: 1223,
                null);
    }

    private sealed class ControllableOperation : IWindowsDnsServiceOperation
    {
        public TaskCompletionSource CancelCalled { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public int CancelCallCount { get; private set; }

        public bool IsDisposed { get; private set; }

        public void Cancel()
        {
            CancelCallCount++;
            CancelCalled.TrySetResult();
        }

        public void Dispose() => IsDisposed = true;
    }
}
