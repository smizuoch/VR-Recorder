using System.Reflection;

namespace VRRecorder.Compliance.Runtime;

public sealed class AssemblyMetadataAuthenticatedLegalBundleAnchorSource
    : IAuthenticatedLegalBundleAnchorSource
{
    public const string BundleIdMetadataKey = "VRRecorder.LegalBundleId";
    public const string ManifestSha256MetadataKey =
        "VRRecorder.LegalManifestSha256";
    private readonly Assembly _assembly;

    public AssemblyMetadataAuthenticatedLegalBundleAnchorSource(
        Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        _assembly = assembly;
    }

    public ValueTask<AuthenticatedLegalBundleAnchor> GetAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var metadata = _assembly
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .ToArray();
            return ValueTask.FromResult(
                new AuthenticatedLegalBundleAnchor(
                    GetExactlyOne(metadata, BundleIdMetadataKey),
                    GetExactlyOne(metadata, ManifestSha256MetadataKey)));
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException)
        {
            return ValueTask.FromException<AuthenticatedLegalBundleAnchor>(
                new AuthenticatedLegalBundleAnchorUnavailableException(
                    "The signed assembly does not contain one valid legal bundle trust anchor.",
                    exception));
        }
    }

    private static string GetExactlyOne(
        IEnumerable<AssemblyMetadataAttribute> metadata,
        string key)
    {
        var values = metadata
            .Where(attribute => string.Equals(
                attribute.Key,
                key,
                StringComparison.Ordinal))
            .Select(attribute => attribute.Value)
            .ToArray();
        if (values.Length != 1 ||
            string.IsNullOrWhiteSpace(values[0]))
        {
            throw new InvalidOperationException(
                $"Assembly metadata {key} must occur exactly once.");
        }

        return values[0]!;
    }
}
