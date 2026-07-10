using VRRecorder.Application.Ports;
using VRRecorder.Domain.Storage;

namespace VRRecorder.Application.Tests.TestDoubles;

internal sealed class StubStorageSpaceProbe : IStorageSpaceProbe
{
    private readonly StorageSpace _space;

    public StubStorageSpaceProbe(StorageSpace space)
    {
        _space = space;
    }

    public int CallCount { get; private set; }

    public OutputPath? RequestedOutputPath { get; private set; }

    public Task<StorageSpace> MeasureAsync(
        OutputPath outputPath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CallCount++;
        RequestedOutputPath = outputPath;
        return Task.FromResult(_space);
    }
}
