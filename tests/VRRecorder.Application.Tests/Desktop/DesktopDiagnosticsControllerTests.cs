using VRRecorder.Application.Desktop;
using VRRecorder.Application.Diagnostics;
using VRRecorder.Application.Ports;

namespace VRRecorder.Application.Tests.Desktop;

public sealed class DesktopDiagnosticsControllerTests
{
    [Fact]
    public void NullExporterIsRejected()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new DesktopDiagnosticsController(null!));
    }

    [Fact]
    public async Task RelativeDestinationIsRejectedBeforeExport()
    {
        var exporter = new ControllableDiagnosticBundleExporter();
        var controller = new DesktopDiagnosticsController(exporter);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            controller.ExportAsync(
                "diagnostics.zip",
                CancellationToken.None));

        Assert.Equal(0, exporter.CallCount);
        Assert.Equal(0, controller.State.Revision);
        Assert.Equal(DesktopDiagnosticsStatus.Idle, controller.State.Status);
    }

    [Fact]
    public async Task PreCanceledRequestDoesNotStartOrChangeState()
    {
        var exporter = new ControllableDiagnosticBundleExporter();
        var controller = new DesktopDiagnosticsController(exporter);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            controller.ExportAsync(
                AbsolutePath("canceled-before-start.zip"),
                cancellation.Token));

        Assert.Equal(0, exporter.CallCount);
        Assert.Equal(0, controller.State.Revision);
        Assert.Equal(DesktopDiagnosticsStatus.Idle, controller.State.Status);
        Assert.Null(controller.State.LastExport);
    }

    [Fact]
    public async Task ExportsOnlyAfterExplicitRequestAndPublishesSuccess()
    {
        var exporter = new ControllableDiagnosticBundleExporter();
        var controller = new DesktopDiagnosticsController(exporter);
        var destination = AbsolutePath("diagnostics.zip");

        Assert.Equal(0, exporter.CallCount);
        Assert.Equal(DesktopDiagnosticsStatus.Idle, controller.State.Status);

        var export = controller.ExportAsync(
            destination,
            CancellationToken.None);
        await exporter.WaitUntilCalledAsync();

        Assert.Equal(DesktopDiagnosticsStatus.Exporting, controller.State.Status);
        Assert.Equal(1, controller.State.Revision);
        exporter.Complete(new DiagnosticBundleExport(destination, 3));
        await export;

        Assert.Equal([destination], exporter.Destinations);
        Assert.Equal(DesktopDiagnosticsStatus.Exported, controller.State.Status);
        Assert.Equal(2, controller.State.Revision);
        Assert.Equal(destination, controller.State.LastExport?.BundlePath);
        Assert.Equal(3, controller.State.LastExport?.EventCount);
    }

    [Fact]
    public async Task CancellationReturnsToIdleWithoutPublishingSuccess()
    {
        var exporter = new ControllableDiagnosticBundleExporter();
        var controller = new DesktopDiagnosticsController(exporter);
        using var cancellation = new CancellationTokenSource();

        var export = controller.ExportAsync(
            AbsolutePath("canceled.zip"),
            cancellation.Token);
        await exporter.WaitUntilCalledAsync();
        cancellation.Cancel();
        exporter.Cancel(cancellation.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => export);
        Assert.Equal(DesktopDiagnosticsStatus.Idle, controller.State.Status);
        Assert.Null(controller.State.LastExport);
    }

    [Fact]
    public async Task ConcurrentExportIsRejectedWithoutReplacingActiveState()
    {
        var exporter = new ControllableDiagnosticBundleExporter
        {
            FailSecondCall = true,
        };
        var controller = new DesktopDiagnosticsController(exporter);
        var firstDestination = AbsolutePath("first.zip");
        var firstExport = controller.ExportAsync(
            firstDestination,
            CancellationToken.None);
        await exporter.WaitUntilCalledAsync();

        var failure = await Record.ExceptionAsync(() =>
            controller.ExportAsync(
                AbsolutePath("second.zip"),
                CancellationToken.None));

        Assert.IsType<InvalidOperationException>(failure);
        Assert.Equal(1, exporter.CallCount);
        Assert.Equal(1, controller.State.Revision);
        Assert.Equal(DesktopDiagnosticsStatus.Exporting, controller.State.Status);
        Assert.Null(controller.State.LastExport);

        exporter.Complete(new DiagnosticBundleExport(firstDestination, 1));
        await firstExport;
    }

    [Fact]
    public async Task NullExporterResultFailsAndClearsActiveOperation()
    {
        var exporter = new ControllableDiagnosticBundleExporter();
        var controller = new DesktopDiagnosticsController(exporter);
        var export = controller.ExportAsync(
            AbsolutePath("null-result.zip"),
            CancellationToken.None);
        await exporter.WaitUntilCalledAsync();
        exporter.Complete(null!);

        await Assert.ThrowsAsync<ArgumentNullException>(() => export);

        Assert.Equal(DesktopDiagnosticsStatus.Failed, controller.State.Status);
        Assert.Equal(2, controller.State.Revision);
        Assert.Null(controller.State.LastExport);
    }

    [Fact]
    public async Task FailureIsRetryableAndNeverReusesEarlierSuccess()
    {
        var exporter = new ControllableDiagnosticBundleExporter();
        var controller = new DesktopDiagnosticsController(exporter);
        var first = AbsolutePath("first.zip");
        var firstExport = controller.ExportAsync(first, CancellationToken.None);
        await exporter.WaitUntilCalledAsync();
        exporter.Complete(new DiagnosticBundleExport(first, 1));
        await firstExport;
        exporter.ResetCallSignal();

        var failedExport = controller.ExportAsync(
            AbsolutePath("failed.zip"),
            CancellationToken.None);
        await exporter.WaitUntilCalledAsync();
        exporter.Fail(new IOException("destination unavailable"));

        await Assert.ThrowsAsync<IOException>(() => failedExport);
        Assert.Equal(DesktopDiagnosticsStatus.Failed, controller.State.Status);
        Assert.Null(controller.State.LastExport);
    }

    private static string AbsolutePath(string name) => Path.Combine(
        Path.GetTempPath(),
        "vr-recorder-desktop-diagnostics-tests",
        name);

    private sealed class ControllableDiagnosticBundleExporter
        : IDiagnosticBundleExporter
    {
        private TaskCompletionSource<DiagnosticBundleExport> _completion =
            CreateCompletion();
        private TaskCompletionSource _called = CreateSignal();

        public int CallCount { get; private set; }

        public bool FailSecondCall { get; init; }

        public List<string> Destinations { get; } = [];

        public Task<DiagnosticBundleExport> ExportAsync(
            string destinationPath,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            Destinations.Add(destinationPath);
            if (FailSecondCall && CallCount > 1)
            {
                return Task.FromException<DiagnosticBundleExport>(
                    new IOException("Unexpected concurrent export."));
            }

            _called.TrySetResult();
            return _completion.Task;
        }

        public Task WaitUntilCalledAsync() => _called.Task;

        public void Complete(DiagnosticBundleExport result) =>
            _completion.TrySetResult(result);

        public void Cancel(CancellationToken cancellationToken) =>
            _completion.TrySetCanceled(cancellationToken);

        public void Fail(Exception exception) =>
            _completion.TrySetException(exception);

        public void ResetCallSignal()
        {
            _completion = CreateCompletion();
            _called = CreateSignal();
        }

        private static TaskCompletionSource<DiagnosticBundleExport>
            CreateCompletion() => new(
                TaskCreationOptions.RunContinuationsAsynchronously);

        private static TaskCompletionSource CreateSignal() => new(
            TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
