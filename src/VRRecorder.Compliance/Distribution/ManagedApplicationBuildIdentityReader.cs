using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;

namespace VRRecorder.Compliance.Distribution;

internal sealed record ManagedApplicationBuildIdentity(
    string ProductVersion,
    string SourceRevision,
    string RuntimeIdentifier);

internal sealed record ManagedApplicationBuildIdentityReadResult(
    ManagedApplicationBuildIdentity? Identity,
    IReadOnlyList<ComplianceIssue> Issues)
{
    public bool IsAdmitted => Identity is not null && Issues.Count == 0;
}

internal static class ManagedApplicationBuildIdentityReader
{
    private const string ProductVersionKey = "VRRecorder.ProductVersion";
    private const string SourceRevisionKey = "VRRecorder.SourceRevision";
    private const string RuntimeIdentifierKey =
        "VRRecorder.RuntimeIdentifier";
    private const string RuntimeIdentifier = "win-x64";
    private const int MaximumAssemblyBytes = 512 * 1024 * 1024;
    private static readonly Regex ProductVersionPattern = new(
        "^(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)" +
        "(?:-[0-9A-Za-z-]+(?:\\.[0-9A-Za-z-]+)*)?" +
        "(?:\\+[0-9A-Za-z-]+(?:\\.[0-9A-Za-z-]+)*)?$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly HashSet<string> FrameworkAssemblyNames = new(
        ["System.Runtime", "mscorlib", "System.Private.CoreLib"],
        StringComparer.Ordinal);

    public static ManagedApplicationBuildIdentityReadResult Read(
        string assemblyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyPath);
        var path = Path.GetFullPath(assemblyPath);
        if (!File.Exists(path))
        {
            return Reject("application-build-identity-assembly-missing", path);
        }

        try
        {
            var info = new FileInfo(path);
            if (info.Length is <= 0 or > MaximumAssemblyBytes)
            {
                return Reject(
                    "application-build-identity-assembly-invalid",
                    path);
            }

            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 81920,
                FileOptions.SequentialScan);
            using var pe = new PEReader(
                stream,
                PEStreamOptions.LeaveOpen | PEStreamOptions.PrefetchMetadata);
            if (!pe.HasMetadata)
            {
                return Reject(
                    "application-build-identity-assembly-invalid",
                    path);
            }

            var metadata = pe.GetMetadataReader();
            if (!metadata.IsAssembly)
            {
                return Reject(
                    "application-build-identity-assembly-invalid",
                    path);
            }

            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var handle in metadata
                         .GetAssemblyDefinition()
                         .GetCustomAttributes())
            {
                var attribute = metadata.GetCustomAttribute(handle);
                if (!IsFrameworkAssemblyMetadataAttribute(
                        metadata,
                        attribute.Constructor))
                {
                    continue;
                }

                var blob = metadata.GetBlobReader(attribute.Value);
                if (blob.ReadUInt16() != 1)
                {
                    return InvalidMetadata(path);
                }

                var key = blob.ReadSerializedString();
                var value = blob.ReadSerializedString();
                if (blob.ReadUInt16() != 0 || blob.RemainingBytes != 0 ||
                    key is null || value is null)
                {
                    return InvalidMetadata(path);
                }

                if (key is ProductVersionKey or SourceRevisionKey or
                    RuntimeIdentifierKey && !values.TryAdd(key, value))
                {
                    return InvalidMetadata(path);
                }
            }

            if (values.Count != 3 ||
                !values.TryGetValue(ProductVersionKey, out var productVersion) ||
                !values.TryGetValue(SourceRevisionKey, out var sourceRevision) ||
                !values.TryGetValue(
                    RuntimeIdentifierKey,
                    out var runtimeIdentifier) ||
                productVersion.Length > 64 ||
                !ProductVersionPattern.IsMatch(productVersion) ||
                !IsCanonicalSourceRevision(sourceRevision) ||
                runtimeIdentifier != RuntimeIdentifier)
            {
                return InvalidMetadata(path);
            }

            return new ManagedApplicationBuildIdentityReadResult(
                new ManagedApplicationBuildIdentity(
                    productVersion,
                    sourceRevision,
                    runtimeIdentifier),
                []);
        }
        catch (Exception exception) when (exception is
            IOException or UnauthorizedAccessException or
            BadImageFormatException or InvalidOperationException or
            ArgumentException)
        {
            return Reject(
                "application-build-identity-assembly-invalid",
                path);
        }
    }

    private static bool IsFrameworkAssemblyMetadataAttribute(
        MetadataReader metadata,
        EntityHandle constructor)
    {
        if (constructor.Kind != HandleKind.MemberReference)
        {
            return false;
        }

        var member = metadata.GetMemberReference(
            (MemberReferenceHandle)constructor);
        if (member.Parent.Kind != HandleKind.TypeReference ||
            metadata.GetString(member.Name) != ".ctor")
        {
            return false;
        }

        var type = metadata.GetTypeReference(
            (TypeReferenceHandle)member.Parent);
        if (metadata.GetString(type.Namespace) != "System.Reflection" ||
            metadata.GetString(type.Name) != "AssemblyMetadataAttribute" ||
            type.ResolutionScope.Kind != HandleKind.AssemblyReference)
        {
            return false;
        }

        var assembly = metadata.GetAssemblyReference(
            (AssemblyReferenceHandle)type.ResolutionScope);
        return FrameworkAssemblyNames.Contains(metadata.GetString(assembly.Name));
    }

    private static bool IsCanonicalSourceRevision(string value) =>
        value.Length is 40 or 64 && value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static ManagedApplicationBuildIdentityReadResult InvalidMetadata(
        string path) => Reject(
        "application-build-identity-metadata-invalid",
        path);

    private static ManagedApplicationBuildIdentityReadResult Reject(
        string code,
        string subject) => new(
        null,
        [new ComplianceIssue(code, subject)]);
}
