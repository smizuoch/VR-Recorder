namespace VRRecorder.Infrastructure.Media;

public sealed record WindowsAudioEndpoint
{
    public WindowsAudioEndpoint(string id, string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        if (id.Any(char.IsControl) || displayName.Any(char.IsControl))
        {
            throw new ArgumentException(
                "Windows audio endpoint text cannot contain control characters.");
        }

        Id = id;
        DisplayName = displayName;
    }

    public string Id { get; }

    public string DisplayName { get; }
}
