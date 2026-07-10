namespace VRRecorder.Compliance.Dependencies;

public sealed record RegisteredNuGetPackage(string Id, string Version)
{
    public string Identity => $"{Id}@{Version}";
}
