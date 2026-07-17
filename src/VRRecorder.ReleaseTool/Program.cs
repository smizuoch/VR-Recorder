namespace VRRecorder.ReleaseTool;

internal static class Program
{
    public static async Task<int> Main(string[] args) =>
        await ReleaseToolApplication.RunAsync(
            args,
            Console.Out,
            Console.Error,
            new WindowsRuntimeStagingRunner(),
            CancellationToken.None);
}
