namespace VRRecorder.Compliance.Distribution;

internal sealed record WindowsValidatedPayloadDirectoryVerification(
    IReadOnlyList<ComplianceIssue> Issues)
{
    public bool IsVerified => Issues.Count == 0;
}

internal static class WindowsValidatedPayloadDirectoryVerifier
{
    public static async Task<WindowsValidatedPayloadDirectoryVerification>
        VerifyAsync(
            string payloadRoot,
            WindowsApplicationPayloadIdentityDocument identity,
            CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadRoot);
        ArgumentNullException.ThrowIfNull(identity);
        cancellationToken.ThrowIfCancellationRequested();

        var admission = await new WindowsPublishDirectoryInventoryReader()
            .ReadAsync(payloadRoot, identity.EntryPoint, cancellationToken)
            .ConfigureAwait(false);
        if (!admission.IsAdmitted || admission.Inventory is null)
        {
            return Reject(admission.Issues.Select(issue =>
                new ComplianceIssue(
                    "validated-payload-directory-invalid",
                    $"{issue.Code}:{issue.Subject}")));
        }

        var inventory = admission.Inventory;
        var issues = new List<ComplianceIssue>();
        if (inventory.EntryPoint != identity.EntryPoint)
        {
            issues.Add(new ComplianceIssue(
                "validated-payload-entrypoint-mismatch",
                inventory.EntryPoint));
        }

        if (inventory.EntryPointSha256 !=
            identity.Payload.ApplicationExecutableSha256)
        {
            issues.Add(new ComplianceIssue(
                "validated-payload-executable-mismatch",
                inventory.EntryPoint));
        }

        if (inventory.InventorySha256 !=
                identity.Payload.PayloadInventorySha256 ||
            !FileInventoriesMatch(inventory.Files, identity.Files))
        {
            issues.Add(new ComplianceIssue(
                "validated-payload-inventory-mismatch",
                inventory.RootDirectory));
        }

        return issues.Count == 0
            ? new WindowsValidatedPayloadDirectoryVerification([])
            : Reject(issues);
    }

    private static bool FileInventoriesMatch(
        IReadOnlyList<Staging.StagedPayloadFile> actual,
        IReadOnlyList<Staging.StagedPayloadFile> expected) =>
        actual.Count == expected.Count &&
        actual.Zip(expected).All(pair =>
            pair.First.RelativePath == pair.Second.RelativePath &&
            pair.First.Length == pair.Second.Length &&
            pair.First.Sha256 == pair.Second.Sha256 &&
            pair.First.Kind == pair.Second.Kind);

    private static WindowsValidatedPayloadDirectoryVerification Reject(
        IEnumerable<ComplianceIssue> issues) => new(
        issues.OrderBy(issue => issue.Code, StringComparer.Ordinal)
            .ThenBy(issue => issue.Subject, StringComparer.Ordinal)
            .ToArray());
}
