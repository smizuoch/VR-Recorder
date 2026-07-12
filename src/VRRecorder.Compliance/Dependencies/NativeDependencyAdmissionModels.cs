namespace VRRecorder.Compliance.Dependencies;

public enum NativeLinkInputKind
{
    StaticLibrary,
    ImportLibrary,
    DynamicLibrary,
    ToolchainTarget,
}

public enum NativeDependencyOrigin
{
    FirstParty,
    WindowsSystem,
    Toolchain,
    ThirdParty,
}

public sealed record NativeLinkObservation(
    string ConsumerTarget,
    string InputIdentity,
    NativeLinkInputKind InputKind,
    string Platform);

public sealed record NativeDependencyAdmission(
    string ConsumerTarget,
    string InputIdentity,
    NativeLinkInputKind InputKind,
    string Platform,
    NativeDependencyOrigin Origin,
    string? ComponentId);

public sealed record AdmittedNativeDependency(
    string ConsumerTarget,
    string InputIdentity,
    NativeLinkInputKind InputKind,
    string Platform,
    NativeDependencyOrigin Origin,
    string? ComponentId);

public sealed record NativeDependencyAdmissionReport(
    IReadOnlyList<AdmittedNativeDependency> Dependencies,
    IReadOnlyList<ComplianceIssue> Issues);
