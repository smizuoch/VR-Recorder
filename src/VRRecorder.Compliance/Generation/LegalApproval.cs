namespace VRRecorder.Compliance.Generation;

public sealed record LegalApproval(
    LegalApprovalStatus Status,
    string? TicketId,
    string RequestedBy,
    string? Reviewer);
