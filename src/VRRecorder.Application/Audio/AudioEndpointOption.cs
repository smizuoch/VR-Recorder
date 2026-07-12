namespace VRRecorder.Application.Audio;

public sealed record AudioEndpointOption
{
    private const int MaximumTextLength = 4096;

    public AudioEndpointOption(string id, string displayName)
    {
        EnsureText(id, nameof(id));
        EnsureText(displayName, nameof(displayName));
        Id = id;
        DisplayName = displayName;
    }

    public string Id { get; }

    public string DisplayName { get; }

    private static void EnsureText(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > MaximumTextLength || value.Any(char.IsControl))
        {
            throw new ArgumentException(
                "Audio endpoint text is invalid.",
                parameterName);
        }
    }
}
