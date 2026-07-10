namespace VRRecorder.Compliance.Dependencies;

public sealed record RegisteredNuGetPackage(
    string Id,
    string Version,
    string LicenseConcluded)
{
    public string Identity => $"{Id}@{Version}";
}
