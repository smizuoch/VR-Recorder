namespace VRRecorder.Infrastructure.Media;

public sealed class RecordingRuntimeResourceLifetime : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly IDisposable[] _resources;
    private Task? _disposeTask;

    public RecordingRuntimeResourceLifetime(params IDisposable[] resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        if (resources.Any(resource => resource is null))
        {
            throw new ArgumentException(
                "Runtime resources cannot contain null.",
                nameof(resources));
        }

        if (resources.Distinct(ReferenceEqualityComparer.Instance).Count() !=
            resources.Length)
        {
            throw new ArgumentException(
                "A runtime resource cannot be owned more than once.",
                nameof(resources));
        }

        _resources = [.. resources];
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            return new ValueTask(_disposeTask ??= DisposeCoreAsync());
        }
    }

    private async Task DisposeCoreAsync()
    {
        await Task.Yield();
        List<Exception>? failures = null;
        for (var index = _resources.Length - 1; index >= 0; index--)
        {
            try
            {
                _resources[index].Dispose();
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }
        }

        if (failures is not null)
        {
            throw new AggregateException(
                "One or more recording runtime resources failed to dispose.",
                failures);
        }
    }
}
