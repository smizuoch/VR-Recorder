namespace VRRecorder.Compliance.Generation;

public sealed record NoticePackage(string Id, string Version)
{
    public string Identity => $"{Id}@{Version}";
}
