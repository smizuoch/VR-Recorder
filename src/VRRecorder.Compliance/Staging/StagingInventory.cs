namespace VRRecorder.Compliance.Staging;

public sealed record StagingInventory(
    IReadOnlyList<StagedPayloadFile> Files,
    IReadOnlyList<ComplianceIssue> ScanIssues);
