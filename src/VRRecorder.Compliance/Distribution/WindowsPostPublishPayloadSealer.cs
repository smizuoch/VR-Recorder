using System.Collections.ObjectModel;
using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using VRRecorder.Compliance.Runtime;
using VRRecorder.Compliance.Staging;

namespace VRRecorder.Compliance.Distribution;

public sealed class SealedWindowsApplicationPayload
{
    internal SealedWindowsApplicationPayload(
        WindowsPublishDirectoryInventory inventory,
        ManagedApplicationBuildIdentity buildIdentity,
        string runtimeIdentifier,
        string legalBundleId,
        string legalManifestSha256)
    {
        RootDirectory = inventory.RootDirectory;
        EntryPoint = inventory.EntryPoint;
        ProductVersion = buildIdentity.ProductVersion;
        SourceRevision = buildIdentity.SourceRevision;
        RuntimeIdentifier = runtimeIdentifier;
        EntryPointSha256 = inventory.EntryPointSha256;
        InventorySha256 = inventory.InventorySha256;
        LegalBundleId = legalBundleId;
        LegalManifestSha256 = legalManifestSha256;
        Files = new ReadOnlyCollection<StagedPayloadFile>(
            inventory.Files.ToArray());
    }

    public string RootDirectory { get; }

    public string EntryPoint { get; }

    public string ProductVersion { get; }

    public string SourceRevision { get; }

    public string RuntimeIdentifier { get; }

    public string EntryPointSha256 { get; }

    public string InventorySha256 { get; }

    public string LegalBundleId { get; }

    public string LegalManifestSha256 { get; }

    public IReadOnlyList<StagedPayloadFile> Files { get; }
}

public sealed record WindowsPostPublishPayloadSealResult(
    SealedWindowsApplicationPayload? Payload,
    IReadOnlyList<ComplianceIssue> Issues)
{
    public bool IsSealed => Payload is not null && Issues.Count == 0;
}

internal interface IWindowsPublishLegalBundleVerifier
{
    Task<LegalBundleVerification> VerifyAsync(
        string publishRoot,
        AuthenticatedLegalBundleAnchor anchor,
        CancellationToken cancellationToken);
}

internal sealed class WindowsPublishLegalBundleVerifier
    : IWindowsPublishLegalBundleVerifier
{
    public Task<LegalBundleVerification> VerifyAsync(
        string publishRoot,
        AuthenticatedLegalBundleAnchor anchor,
        CancellationToken cancellationToken) =>
        new AuthenticatedLegalBundleVerifier(new FixedAnchorSource(anchor))
            .VerifyAsync(
                publishRoot,
                LegalBundleVerificationScope.InstallRoot,
                cancellationToken);

    private sealed class FixedAnchorSource(
        AuthenticatedLegalBundleAnchor anchor)
        : IAuthenticatedLegalBundleAnchorSource
    {
        public ValueTask<AuthenticatedLegalBundleAnchor> GetAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(anchor);
        }
    }
}

public sealed class WindowsPostPublishPayloadSealer
{
    private const string PropsFileName = "ApprovedWindowsRuntime.props";
    private const string PayloadDirectoryName = "payload";
    private const string PayloadPrefix = "$(MSBuildThisFileDirectory)payload/";
    private const string Profile =
        "full-production-hardware-validation-v1";
    private const string RuntimeIdentifier = "win-x64";
    private const int MaximumPropsBytes = 4 * 1024 * 1024;
    private static readonly string[] PropertyNames =
    [
        "VRRecorderApprovedWindowsRuntimeImported",
        "VRRecorderApprovedWindowsRuntimeManifestSha256",
        "VRRecorderApprovedWindowsRuntimeInventorySha256",
        "VRRecorderApprovedWindowsRuntimeProfile",
        "VRRecorderApprovedWindowsRuntimeIdentifier",
        "VRRecorderApprovedLegalBundleId",
        "VRRecorderApprovedLegalManifestSha256",
        "LegalBundleId",
        "LegalManifestSha256",
    ];
    private readonly IWindowsPublishLegalBundleVerifier _legalVerifier;
    private readonly WindowsPublishDirectoryInventoryReader _publishReader;
    private readonly IStagingInventoryReader _stagingReader;
    private readonly IWindowsRuntimeFileSemanticsVerifier _fileSemantics;

    public WindowsPostPublishPayloadSealer()
        : this(new WindowsPublishLegalBundleVerifier())
    {
    }

    internal WindowsPostPublishPayloadSealer(
        IWindowsPublishLegalBundleVerifier legalVerifier)
        : this(
            legalVerifier,
            new WindowsPublishDirectoryInventoryReader(),
            new FileSystemStagingInventoryReader(),
            WindowsRuntimeFileSemanticsVerifier.Instance)
    {
    }

    internal WindowsPostPublishPayloadSealer(
        IWindowsPublishLegalBundleVerifier legalVerifier,
        WindowsPublishDirectoryInventoryReader publishReader,
        IStagingInventoryReader stagingReader,
        IWindowsRuntimeFileSemanticsVerifier fileSemantics)
    {
        ArgumentNullException.ThrowIfNull(legalVerifier);
        ArgumentNullException.ThrowIfNull(publishReader);
        ArgumentNullException.ThrowIfNull(stagingReader);
        ArgumentNullException.ThrowIfNull(fileSemantics);
        _legalVerifier = legalVerifier;
        _publishReader = publishReader;
        _stagingReader = stagingReader;
        _fileSemantics = fileSemantics;
    }

    public async Task<WindowsPostPublishPayloadSealResult> SealAsync(
        string publishRoot,
        string approvedPropsPath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publishRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(approvedPropsPath);
        cancellationToken.ThrowIfCancellationRequested();

        ApprovedProps props;
        try
        {
            props = ReadApprovedProps(approvedPropsPath);
        }
        catch (PropsAdmissionException exception)
        {
            return Reject(exception.Code, exception.Subject);
        }
        catch (Exception exception) when (exception is
            IOException or UnauthorizedAccessException or XmlException or
            InvalidDataException or InvalidOperationException or
            ArgumentException)
        {
            return Reject("invalid-approved-runtime-props", approvedPropsPath);
        }

        var publishAdmission = await _publishReader
            .ReadAsync(
                publishRoot,
                "VRRecorder.App.exe",
                cancellationToken)
            .ConfigureAwait(false);
        if (!publishAdmission.IsAdmitted ||
            publishAdmission.Inventory is null)
        {
            return Reject(publishAdmission.Issues);
        }

        var managedAssembly = publishAdmission.Inventory.Files
            .Where(file => string.Equals(
                file.RelativePath,
                "VRRecorder.App.dll",
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (managedAssembly.Length == 0)
        {
            return Reject(
                "application-build-identity-assembly-missing",
                "VRRecorder.App.dll");
        }

        if (managedAssembly.Length != 1 ||
            managedAssembly[0].RelativePath != "VRRecorder.App.dll")
        {
            return Reject(
                "application-build-identity-assembly-invalid",
                managedAssembly[0].RelativePath);
        }

        var buildIdentityAdmission = ManagedApplicationBuildIdentityReader.Read(
            Path.Combine(
            publishAdmission.Inventory.RootDirectory,
            "VRRecorder.App.dll"));
        if (!buildIdentityAdmission.IsAdmitted ||
            buildIdentityAdmission.Identity is null)
        {
            return Reject(buildIdentityAdmission.Issues);
        }

        var buildIdentity = buildIdentityAdmission.Identity;

        StagingInventory staged;
        try
        {
            staged = await _stagingReader
                .ReadAsync(props.PayloadDirectory, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is
            IOException or UnauthorizedAccessException or
            InvalidOperationException or ArgumentException)
        {
            return Reject(
                "approved-runtime-payload-read-failed",
                props.PayloadDirectory);
        }

        var issues = new List<ComplianceIssue>(staged.ScanIssues);
        var expectedTargets = props.Files.Select(file => file.Target).ToHashSet(
            StringComparer.OrdinalIgnoreCase);
        foreach (var unexpected in staged.Files.Where(file =>
                     !expectedTargets.Contains(file.RelativePath)))
        {
            issues.Add(new ComplianceIssue(
                "approved-runtime-payload-inventory-mismatch",
                unexpected.RelativePath));
        }

        foreach (var expected in props.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = expected.Target;
            var stagedMatches = staged.Files.Where(file => string.Equals(
                    file.RelativePath,
                    target,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (stagedMatches.Length != 1 ||
                !string.Equals(
                    stagedMatches[0].RelativePath,
                    target,
                    StringComparison.Ordinal))
            {
                issues.Add(new ComplianceIssue(
                    "approved-runtime-payload-inventory-mismatch",
                    target));
                continue;
            }

            if (stagedMatches[0].Length != expected.Length ||
                stagedMatches[0].Kind != expected.Kind ||
                !string.Equals(
                    stagedMatches[0].Sha256,
                    expected.Sha256,
                    StringComparison.Ordinal))
            {
                issues.Add(new ComplianceIssue(
                    "approved-runtime-file-mismatch",
                    target));
                continue;
            }

            try
            {
                _fileSemantics.VerifyRegularFile(
                    WindowsRuntimeRelativePath.Resolve(
                        props.PayloadDirectory,
                        target));
            }
            catch (Exception exception) when (exception is
                IOException or UnauthorizedAccessException or
                InvalidDataException or ArgumentException)
            {
                issues.Add(new ComplianceIssue(
                    "approved-runtime-file-semantics-invalid",
                    target));
                continue;
            }

            var publishedMatches = publishAdmission.Inventory.Files
                .Where(file => string.Equals(
                    file.RelativePath,
                    target,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (publishedMatches.Length == 0)
            {
                issues.Add(new ComplianceIssue(
                    "published-runtime-file-missing",
                    target));
                continue;
            }

            if (publishedMatches.Length != 1 ||
                !string.Equals(
                    publishedMatches[0].RelativePath,
                    target,
                    StringComparison.Ordinal) ||
                publishedMatches[0].Length != stagedMatches[0].Length ||
                publishedMatches[0].Kind != stagedMatches[0].Kind ||
                !string.Equals(
                    publishedMatches[0].Sha256,
                    stagedMatches[0].Sha256,
                    StringComparison.Ordinal))
            {
                issues.Add(new ComplianceIssue(
                    "published-runtime-file-mismatch",
                    target));
            }
        }

        if (issues.Count != 0)
        {
            return Reject(issues);
        }

        var anchor = new AuthenticatedLegalBundleAnchor(
            props.LegalBundleId,
            props.LegalManifestSha256);
        LegalBundleVerification legal;
        try
        {
            legal = await _legalVerifier
                .VerifyAsync(
                    publishAdmission.Inventory.RootDirectory,
                    anchor,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is
            IOException or UnauthorizedAccessException or InvalidDataException or
            InvalidOperationException or ArgumentException)
        {
            return Reject(
                "published-legal-bundle-verification-failed",
                publishAdmission.Inventory.RootDirectory);
        }

        if (legal is LegalBundleVerification.Rejected rejected)
        {
            return Reject(rejected.Issues);
        }

        var verified = (LegalBundleVerification.Verified)legal;
        if (!string.Equals(
                verified.Identity.BundleId,
                props.LegalBundleId,
                StringComparison.Ordinal) ||
            !string.Equals(
                verified.Identity.ManifestSha256,
                props.LegalManifestSha256,
                StringComparison.Ordinal))
        {
            return Reject(
                "legal-bundle-identity-mismatch",
                verified.Identity.BundleId);
        }

        if (!string.Equals(
                verified.Identity.ProductVersion,
                buildIdentity.ProductVersion,
                StringComparison.Ordinal))
        {
            return Reject(
                "application-product-version-legal-mismatch",
                verified.Identity.ProductVersion ?? string.Empty);
        }

        return new WindowsPostPublishPayloadSealResult(
            new SealedWindowsApplicationPayload(
                publishAdmission.Inventory,
                buildIdentity,
                props.RuntimeIdentifier,
                props.LegalBundleId,
                props.LegalManifestSha256),
            []);
    }

    private static ApprovedProps ReadApprovedProps(string path)
    {
        if (!Path.IsPathFullyQualified(path) ||
            !string.Equals(
                Path.GetFullPath(path),
                path,
                PathComparison) ||
            !string.Equals(
                Path.GetFileName(path),
                PropsFileName,
                StringComparison.Ordinal) ||
            !File.Exists(path) ||
            new FileInfo(path).Length is <= 0 or > MaximumPropsBytes)
        {
            throw InvalidProps(path);
        }

        WindowsRuntimeFileSemanticsVerifier.Instance.VerifyRegularFile(path);
        var directory = Path.GetDirectoryName(path) ?? throw InvalidProps(path);
        if (!RepositoryEvidenceRoot.TryResolve(directory, out var propsRoot))
        {
            throw InvalidProps(path);
        }

        using var reader = XmlReader.Create(
            path,
            new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                IgnoreComments = false,
                IgnoreProcessingInstructions = false,
                MaxCharactersInDocument = MaximumPropsBytes,
                XmlResolver = null,
            });
        var document = XDocument.Load(reader, LoadOptions.None);
        var root = document.Root;
        if (root is null || root.Name != "Project" ||
            root.Attributes().Any() ||
            root.Elements().Any(element =>
                element.Name != "PropertyGroup" &&
                element.Name != "ItemGroup"))
        {
            throw InvalidProps(path);
        }

        var propertyGroup = root.Elements("PropertyGroup").Single();
        if (propertyGroup.Attributes().Any())
        {
            throw InvalidProps(path);
        }

        var properties = propertyGroup.Elements().ToArray();
        if (properties.Any(property => property.Attributes().Any()) ||
            properties.Select(property => property.Name.LocalName)
                .Distinct(StringComparer.Ordinal).Count() !=
            properties.Length ||
            properties.Length != PropertyNames.Length ||
            PropertyNames.Any(name => properties.All(property =>
                property.Name.LocalName != name)))
        {
            throw InvalidProps(path);
        }

        string Property(string name) => properties.Single(property =>
            property.Name.LocalName == name).Value;
        var inventorySha = Property(
            "VRRecorderApprovedWindowsRuntimeInventorySha256");
        var legalBundleId = Property("VRRecorderApprovedLegalBundleId");
        var legalManifestSha = Property(
            "VRRecorderApprovedLegalManifestSha256");
        if (Property("VRRecorderApprovedWindowsRuntimeImported") != "true" ||
            !IsSha256(Property(
                "VRRecorderApprovedWindowsRuntimeManifestSha256")) ||
            !IsSha256(inventorySha) ||
            Property("VRRecorderApprovedWindowsRuntimeProfile") != Profile ||
            Property("VRRecorderApprovedWindowsRuntimeIdentifier") !=
                RuntimeIdentifier ||
            string.IsNullOrWhiteSpace(legalBundleId) ||
            !IsSha256(legalManifestSha) ||
            Property("LegalBundleId") != legalBundleId ||
            Property("LegalManifestSha256") != legalManifestSha)
        {
            throw InvalidProps(path);
        }

        var expectedDirectory = "windows-runtime-" + inventorySha;
        if (!string.Equals(
                Path.GetFileName(propsRoot),
                expectedDirectory,
                StringComparison.Ordinal))
        {
            throw new PropsAdmissionException(
                "approved-runtime-props-directory-mismatch",
                propsRoot);
        }

        var itemGroup = root.Elements("ItemGroup").Single();
        if (itemGroup.Attributes().Any())
        {
            throw InvalidProps(path);
        }

        var files = new List<ApprovedRuntimeFile>();
        foreach (var content in itemGroup.Elements())
        {
            if (content.Name != "Content" ||
                content.Attributes().Count() != 1 ||
                content.Attribute("Include") is not { } include ||
                !include.Value.StartsWith(PayloadPrefix, StringComparison.Ordinal))
            {
                throw InvalidProps(path);
            }

            var target = WindowsRuntimeRelativePath.RequireCanonical(
                include.Value[PayloadPrefix.Length..],
                "target");
            var children = content.Elements().ToArray();
            if (children.Length != 7 ||
                children.Select(child => child.Name.LocalName)
                    .Distinct(StringComparer.Ordinal).Count() != 7 ||
                children.Single(child => child.Name == "Link").Value != target ||
                children.Single(child => child.Name == "TargetPath").Value !=
                    target ||
                children.Single(child =>
                    child.Name == "CopyToOutputDirectory").Value !=
                    "IfDifferent" ||
                children.Single(child =>
                    child.Name == "CopyToPublishDirectory").Value !=
                    "IfDifferent" ||
                children.Any(child => child.Attributes().Any()))
            {
                throw InvalidProps(path);
            }

            var sha256 = children.Single(child =>
                child.Name == "VRRecorderSha256").Value;
            var lengthText = children.Single(child =>
                child.Name == "VRRecorderLength").Value;
            var kindText = children.Single(child =>
                child.Name == "VRRecorderKind").Value;
            if (!IsSha256(sha256) ||
                !long.TryParse(
                    lengthText,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var length) ||
                length < 0 ||
                !Enum.TryParse<StagedArtifactKind>(
                    kindText,
                    ignoreCase: false,
                    out var kind) ||
                !Enum.IsDefined(kind))
            {
                throw InvalidProps(path);
            }

            files.Add(new ApprovedRuntimeFile(
                target,
                sha256,
                length,
                kind));
        }

        if (files.Count == 0 ||
            files.Select(file => file.Target)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() !=
            files.Count)
        {
            throw InvalidProps(path);
        }

        var payloadDirectory = Path.Combine(propsRoot, PayloadDirectoryName);
        if (!RepositoryEvidenceRoot.TryResolve(
                payloadDirectory,
                out var canonicalPayload))
        {
            throw InvalidProps(path);
        }

        return new ApprovedProps(
            RuntimeIdentifier,
            inventorySha,
            legalBundleId,
            legalManifestSha,
            canonicalPayload,
            files.OrderBy(file => file.Target, StringComparer.Ordinal).ToArray());
    }

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static PropsAdmissionException InvalidProps(string subject) => new(
        "invalid-approved-runtime-props",
        subject);

    private static WindowsPostPublishPayloadSealResult Reject(
        string code,
        string subject) => Reject([new ComplianceIssue(code, subject)]);

    private static WindowsPostPublishPayloadSealResult Reject(
        IEnumerable<ComplianceIssue> issues) => new(
        null,
        issues.OrderBy(issue => issue.Code, StringComparer.Ordinal)
            .ThenBy(issue => issue.Subject, StringComparer.Ordinal)
            .ToArray());

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private sealed record ApprovedProps(
        string RuntimeIdentifier,
        string InventorySha256,
        string LegalBundleId,
        string LegalManifestSha256,
        string PayloadDirectory,
        IReadOnlyList<ApprovedRuntimeFile> Files);

    private sealed record ApprovedRuntimeFile(
        string Target,
        string Sha256,
        long Length,
        StagedArtifactKind Kind);

    private sealed class PropsAdmissionException(
        string code,
        string subject) : Exception
    {
        public string Code { get; } = code;

        public string Subject { get; } = subject;
    }
}
