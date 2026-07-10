using System.Collections.Concurrent;

namespace VRRecorder.Infrastructure.Osc;

public sealed class WindowsDnsSdApi : IWindowsDnsSdApi
{
    private const uint ErrorSuccess = 0;
    private const uint ErrorCancelled = 1223;
    private static readonly TimeSpan DefaultBrowseDuration =
        TimeSpan.FromMilliseconds(500);
    private readonly IWindowsDnsServiceNativeApi _native;
    private readonly TimeSpan _browseDuration;

    public WindowsDnsSdApi()
        : this(
            new ShellWindowsDnsServiceNativeApi(),
            DefaultBrowseDuration)
    {
    }

    public WindowsDnsSdApi(
        IWindowsDnsServiceNativeApi native,
        TimeSpan browseDuration)
    {
        ArgumentNullException.ThrowIfNull(native);
        if (browseDuration < TimeSpan.Zero ||
            browseDuration == Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(
                nameof(browseDuration),
                browseDuration,
                "The DNS-SD browse duration must be finite and non-negative.");
        }

        _native = native;
        _browseDuration = browseDuration;
    }

    public bool IsSupported => _native.IsSupported;

    public async Task<IReadOnlyList<string>> BrowseAsync(
        string queryName,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queryName);
        var results = new ConcurrentDictionary<string, byte>(
            StringComparer.Ordinal);
        var terminal = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var operation = _native.StartBrowse(
            queryName,
            (status, serviceInstanceNames) =>
            {
                if (status == ErrorSuccess)
                {
                    foreach (var serviceInstanceName in serviceInstanceNames)
                    {
                        if (!string.IsNullOrWhiteSpace(serviceInstanceName))
                        {
                            results.TryAdd(serviceInstanceName, 0);
                        }
                    }

                    return;
                }

                if (status == ErrorCancelled)
                {
                    terminal.TrySetResult();
                    return;
                }

                terminal.TrySetException(CreateFailure("browse", status));
            });
        try
        {
            var collectionWindow = Task.Delay(
                _browseDuration,
                cancellationToken);
            var completed = await Task
                .WhenAny(collectionWindow, terminal.Task)
                .ConfigureAwait(false);
            await completed.ConfigureAwait(false);
        }
        finally
        {
            operation.Cancel();
            await terminal.Task.ConfigureAwait(false);
        }

        return results.Keys.Order(StringComparer.Ordinal).ToArray();
    }

    public async Task<WindowsDnsSdResolvedService?> ResolveAsync(
        string serviceInstanceName,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceInstanceName);
        var completion = new TaskCompletionSource<WindowsDnsSdResolvedService?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var operation = _native.StartResolve(
            serviceInstanceName,
            (status, resolved) =>
            {
                if (status == ErrorSuccess)
                {
                    completion.TrySetResult(resolved);
                }
                else if (status == ErrorCancelled)
                {
                    completion.TrySetCanceled(cancellationToken);
                }
                else
                {
                    completion.TrySetException(CreateFailure("resolve", status));
                }
            });
        using var cancellationRegistration = cancellationToken.Register(() =>
        {
            try
            {
                operation.Cancel();
                completion.TrySetCanceled(cancellationToken);
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        });
        return await completion.Task.ConfigureAwait(false);
    }

    private static WindowsDnsSdException CreateFailure(
        string operation,
        uint status) =>
        new(operation, status);
}
