namespace VRRecorder.Compliance.Dependencies;

public enum NativeRuntimeLoadMechanism
{
    NativeLibrary,
    LibraryImport,
    LoadLibrary,
}

public enum NativeRuntimeIntegrity
{
    ReleaseArtifact,
    WindowsSystem,
    RegistrySha256,
}

public sealed record NativeRuntimeLoadObservation(
    string Consumer,
    string FileName,
    NativeRuntimeLoadMechanism Mechanism,
    string Platform);

public sealed record NativeRuntimeLoadAdmission(
    string Consumer,
    string FileName,
    NativeRuntimeLoadMechanism Mechanism,
    string Platform,
    NativeDependencyOrigin Origin,
    NativeRuntimeIntegrity Integrity,
    string? ComponentId);

public sealed record AdmittedNativeRuntimeLoad(
    string Consumer,
    string FileName,
    NativeRuntimeLoadMechanism Mechanism,
    string Platform,
    NativeDependencyOrigin Origin,
    NativeRuntimeIntegrity Integrity,
    string? ComponentId);

public sealed record NativeRuntimeLoadAdmissionReport(
    IReadOnlyList<AdmittedNativeRuntimeLoad> Dependencies,
    IReadOnlyList<ComplianceIssue> Issues);
