namespace VRRecorder.Compliance.Runtime;

public sealed record AuthenticatedLegalBundleAnchor
{
    public AuthenticatedLegalBundleAnchor(
        string bundleId,
        string manifestSha256)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bundleId);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestSha256);
        if (manifestSha256.Length != 64 || manifestSha256.Any(character =>
                character is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
        {
            throw new ArgumentException(
                "The authenticated manifest digest must be 64 lowercase hexadecimal characters.",
                nameof(manifestSha256));
        }

        BundleId = bundleId;
        ManifestSha256 = manifestSha256;
    }

    public string BundleId { get; }

    public string ManifestSha256 { get; }
}
