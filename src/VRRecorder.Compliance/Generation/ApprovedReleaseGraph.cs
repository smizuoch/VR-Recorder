namespace VRRecorder.Compliance.Generation;

public sealed record ApprovedReleaseGraph
{
    internal ApprovedReleaseGraph(NormalizedComponentGraph graph)
    {
        Graph = graph;
    }

    public NormalizedComponentGraph Graph { get; }
}
