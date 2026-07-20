namespace VRRecorder.Compliance.Distribution;

internal sealed record WindowsStorePackagingInputValidation(
    WindowsApplicationPayloadIdentityDocument? PayloadIdentity,
    HardwareValidationEvidence? HardwareValidation,
    IReadOnlyList<ComplianceIssue> Issues)
{
    public bool IsValidated =>
        PayloadIdentity is not null &&
        HardwareValidation is not null &&
        Issues.Count == 0;
}

internal static class WindowsStorePackagingInputValidator
{
    public static async Task<WindowsStorePackagingInputValidation>
        ValidateAsync(
            string payloadRoot,
            byte[] payloadIdentityContent,
            byte[] hardwareValidationReportContent,
            string hardwareValidationArtifactRoot,
            string candidatePath,
            MicrosoftStoreIdentity storeIdentity,
            CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadRoot);
        ArgumentNullException.ThrowIfNull(payloadIdentityContent);
        ArgumentNullException.ThrowIfNull(hardwareValidationReportContent);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            hardwareValidationArtifactRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(candidatePath);
        ArgumentNullException.ThrowIfNull(storeIdentity);
        cancellationToken.ThrowIfCancellationRequested();

        WindowsApplicationPayloadIdentityDocument identity;
        try
        {
            identity = WindowsApplicationPayloadIdentityReader.Read(
                payloadIdentityContent);
        }
        catch (InvalidDataException)
        {
            return Reject(
                "store-packaging-payload-identity-invalid",
                "payload-identity");
        }

        var payloadValidationTask =
            WindowsValidatedPayloadDirectoryVerifier.VerifyAsync(
                payloadRoot,
                identity,
                cancellationToken);
        var hardwareValidationTask =
            WindowsHardwareValidationEvidenceValidator.ValidateAsync(
                payloadIdentityContent,
                hardwareValidationReportContent,
                hardwareValidationArtifactRoot,
                cancellationToken);
        await Task.WhenAll(payloadValidationTask, hardwareValidationTask)
            .ConfigureAwait(false);

        var payloadValidation = await payloadValidationTask
            .ConfigureAwait(false);
        var hardwareValidation = await hardwareValidationTask
            .ConfigureAwait(false);
        var issues = payloadValidation.Issues
            .Concat(hardwareValidation.Issues)
            .ToList();
        if (hardwareValidation.Evidence is not null)
        {
            var promotion = DistributionPromotionPolicy.Evaluate(
                new DistributionPromotionRequest(
                    DistributionTarget.MicrosoftStorePackagingCandidate,
                    candidatePath,
                    identity.Payload,
                    hardwareValidation.Evidence,
                    storeIdentity));
            issues.AddRange(promotion.Issues);
        }

        if (issues.Count != 0 || hardwareValidation.Evidence is null)
        {
            return Reject(issues);
        }

        return new WindowsStorePackagingInputValidation(
            identity,
            hardwareValidation.Evidence,
            []);
    }

    private static WindowsStorePackagingInputValidation Reject(
        string code,
        string subject) => Reject([new ComplianceIssue(code, subject)]);

    private static WindowsStorePackagingInputValidation Reject(
        IEnumerable<ComplianceIssue> issues) => new(
        null,
        null,
        issues.OrderBy(issue => issue.Code, StringComparer.Ordinal)
            .ThenBy(issue => issue.Subject, StringComparer.Ordinal)
            .ToArray());
}
