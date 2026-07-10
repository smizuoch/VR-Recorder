using VRRecorder.Compliance.Staging;

namespace VRRecorder.Compliance.Packaging;

public sealed class ReleasePackageGenerator
{
    private readonly IStagingInventoryReader _inventoryReader;
    private readonly IReleasePackageWriter _packageWriter;

    public ReleasePackageGenerator(
        IStagingInventoryReader inventoryReader,
        IReleasePackageWriter packageWriter)
    {
        ArgumentNullException.ThrowIfNull(inventoryReader);
        ArgumentNullException.ThrowIfNull(packageWriter);
        _inventoryReader = inventoryReader;
        _packageWriter = packageWriter;
    }

    public async Task<PackageGenerationResult> GenerateAsync(
        ReleasePackageRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var inventory = await _inventoryReader
            .ReadAsync(request.StagingDirectory, cancellationToken)
            .ConfigureAwait(false);
        var issues = inventory.ScanIssues
            .Concat(StagingInventoryValidator.Validate(
                inventory.Files,
                request.RegisteredArtifacts))
            .OrderBy(item => item.Code, StringComparer.Ordinal)
            .ThenBy(item => item.Subject, StringComparer.Ordinal)
            .ToArray();
        if (issues.Length != 0)
        {
            return new PackageGenerationResult(false, issues);
        }

        await _packageWriter
            .WriteAsync(request.PackagePath, inventory, cancellationToken)
            .ConfigureAwait(false);
        return new PackageGenerationResult(true, []);
    }
}
