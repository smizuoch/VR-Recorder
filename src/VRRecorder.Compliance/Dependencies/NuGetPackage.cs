namespace VRRecorder.Compliance.Dependencies;

public sealed record NuGetPackage(string Id, string Version, NuGetDependencyKind Kind)
{
    public string Identity => $"{Id}@{Version}";
}
