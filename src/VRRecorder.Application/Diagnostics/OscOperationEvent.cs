namespace VRRecorder.Application.Diagnostics;

public sealed record OscOperationEvent
{
    public OscOperationEvent(
        OscOperation operation,
        OscOperationOutcome outcome)
    {
        if (!Enum.IsDefined(operation))
        {
            throw new ArgumentOutOfRangeException(nameof(operation));
        }

        if (!Enum.IsDefined(outcome))
        {
            throw new ArgumentOutOfRangeException(nameof(outcome));
        }

        Operation = operation;
        Outcome = outcome;
    }

    public OscOperation Operation { get; }

    public OscOperationOutcome Outcome { get; }
}
