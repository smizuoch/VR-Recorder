namespace VRRecorder.Compliance.Generation;

public sealed record SpdxGenerationContext(
    string ProductName,
    string ProductVersion,
    string DocumentNamespace,
    DateTimeOffset CreatedAtUtc,
    string Creator);
