namespace VRRecorder.Presentation.Wrist;

public enum WristFlowDirection
{
    LeftToRight,
    RightToLeft,
}

public sealed record WristLayoutOptions
{
    public static WristLayoutOptions Default { get; } = new();

    public WristFlowDirection FlowDirection { get; init; } =
        WristFlowDirection.LeftToRight;

    public double TextScale { get; init; } = 1.0;

    public bool HighContrast { get; init; }
}
