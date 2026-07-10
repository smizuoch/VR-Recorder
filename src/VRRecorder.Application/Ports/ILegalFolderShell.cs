namespace VRRecorder.Application.Ports;

public interface ILegalFolderShell
{
    Task OpenFolderAsync(
        string folderPath,
        CancellationToken cancellationToken);
}
