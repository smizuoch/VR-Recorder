namespace VRRecorder.Infrastructure.Storage;

public interface IWindowsKnownFolderPathApi
{
    string GetPath(Guid folderId);
}
