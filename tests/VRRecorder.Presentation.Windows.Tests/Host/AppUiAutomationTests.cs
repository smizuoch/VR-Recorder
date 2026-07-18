using System.Windows.Automation;
using System.Windows.Threading;
using VRRecorder.App;
using VRRecorder.Application.Desktop;
using VRRecorder.Application.Diagnostics;
using VRRecorder.Application.Ports;
using VRRecorder.Application.Setup;

namespace VRRecorder.Presentation.Windows.Tests.Host;

[CollectionDefinition(CollectionName, DisableParallelization = true)]
public sealed class UiAutomationSerialGroup
{
    public const string CollectionName = "Windows UI Automation";
}

[Collection(UiAutomationSerialGroup.CollectionName)]
public sealed class AppUiAutomationTests
{
    [Fact]
    public async Task DiagnosticsSaveDialogAndFirstRunExposeKeyboardScreenReaderUi()
    {
        using var host = new WindowHost();
        var diagnostics = host.WaitForWindow(
            "VR-Recorder diagnostic bundle export");
        Assert.Equal(ControlType.Window, diagnostics.Current.ControlType);

        var export = FindRequired(
            diagnostics,
            ControlType.Button,
            "Export privacy-safe diagnostic bundle");
        Assert.True(export.Current.IsKeyboardFocusable);
        Assert.NotEmpty(export.Current.HelpText);
        Assert.True(export.TryGetCurrentPattern(
            InvokePattern.Pattern,
            out var exportPattern));
        export.SetFocus();
        Assert.IsType<InvokePattern>(exportPattern);
        var saveDialogLifetime = host.OpenSaveDialogAsync();

        var saveDialog = WaitForTopLevelWindow(
            "Save diagnostic bundle",
            diagnostics.Current.ProcessId);
        Assert.Equal(ControlType.Window, saveDialog.Current.ControlType);
        var cancel = FindRequiredByAutomationId(
            saveDialog,
            ControlType.Button,
            "2");
        Assert.True(cancel.Current.IsKeyboardFocusable);
        Assert.True(cancel.TryGetCurrentPattern(
            InvokePattern.Pattern,
            out var cancelPattern));
        ((InvokePattern)cancelPattern).Invoke();
        await saveDialogLifetime.WaitAsync(TimeSpan.FromSeconds(10));

        var closeDiagnostics = FindRequired(
            diagnostics,
            ControlType.Button,
            "Close diagnostics");
        Assert.True(closeDiagnostics.TryGetCurrentPattern(
            InvokePattern.Pattern,
            out var closeDiagnosticsPattern));
        ((InvokePattern)closeDiagnosticsPattern).Invoke();

        var firstRun = host.WaitForWindow("VR-Recorder first-run setup");
        Assert.Equal(ControlType.Window, firstRun.Current.ControlType);
        foreach (var name in new[]
                 {
                     "Open recording settings",
                     "Verify the current setup check",
                     "Close first-run setup",
                 })
        {
            var button = FindRequired(firstRun, ControlType.Button, name);
            Assert.True(button.Current.IsKeyboardFocusable);
            Assert.NotEmpty(button.Current.HelpText);
            Assert.True(button.TryGetCurrentPattern(
                InvokePattern.Pattern,
                out _));
        }

        var closeSetup = FindRequired(
            firstRun,
            ControlType.Button,
            "Close first-run setup");
        Assert.True(closeSetup.TryGetCurrentPattern(
            InvokePattern.Pattern,
            out var closeSetupPattern));
        ((InvokePattern)closeSetupPattern).Invoke();
        host.WaitForExit();
    }

    private static AutomationElement FindRequired(
        AutomationElement root,
        ControlType controlType,
        string name) => Retry(() => root.FindFirst(
            TreeScope.Descendants,
            new AndCondition(
                new PropertyCondition(
                    AutomationElement.ControlTypeProperty,
                    controlType),
                new PropertyCondition(
                    AutomationElement.NameProperty,
                    name)))) ?? throw new InvalidOperationException(
                $"UI Automation could not find {controlType.ProgrammaticName} '{name}'.");

    private static AutomationElement FindRequiredByAutomationId(
        AutomationElement root,
        ControlType controlType,
        string automationId) => Retry(() => root.FindFirst(
            TreeScope.Descendants,
            new AndCondition(
                new PropertyCondition(
                    AutomationElement.ControlTypeProperty,
                    controlType),
                new PropertyCondition(
                    AutomationElement.AutomationIdProperty,
                    automationId)))) ?? throw new InvalidOperationException(
                $"UI Automation could not find automation ID '{automationId}'.");

    private static AutomationElement WaitForTopLevelWindow(
        string name,
        int processId)
    {
        var found = Retry(() => AutomationElement.RootElement.FindFirst(
            TreeScope.Descendants,
            new AndCondition(
                new PropertyCondition(
                    AutomationElement.ControlTypeProperty,
                    ControlType.Window),
                new PropertyCondition(
                    AutomationElement.NameProperty,
                    name),
                new PropertyCondition(
                    AutomationElement.ProcessIdProperty,
                    processId))));
        if (found is not null)
        {
            return found;
        }
        var visible = AutomationElement.RootElement.FindAll(
                TreeScope.Children,
                Condition.TrueCondition)
            .Cast<AutomationElement>()
            .Select(element =>
                $"{element.Current.ControlType.ProgrammaticName}|" +
                $"{element.Current.ProcessId}|{element.Current.Name}")
            .ToArray();
        throw new InvalidOperationException(
            $"UI Automation could not find top-level window '{name}'. " +
            $"Visible roots: {string.Join("; ", visible)}");
    }

    private static AutomationElement? Retry(
        Func<AutomationElement?> find)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        do
        {
            var found = find();
            if (found is not null)
            {
                return found;
            }
            Thread.Sleep(25);
        }
        while (DateTime.UtcNow < deadline);
        return null;
    }

    private sealed class WindowHost : IDisposable
    {
        private readonly AutoResetEvent _windowReady = new(false);
        private readonly Thread _thread;
        private readonly object _gate = new();
        private nint _windowHandle;
        private Exception? _failure;

        public WindowHost()
        {
            _thread = new Thread(Run)
            {
                IsBackground = true,
                Name = "VR Recorder UI Automation WPF host",
            };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
        }

        public AutomationElement WaitForWindow(string expectedName)
        {
            if (!_windowReady.WaitOne(TimeSpan.FromSeconds(10)))
            {
                ThrowFailureOrTimeout(expectedName);
            }
            nint handle;
            lock (_gate)
            {
                if (_failure is not null)
                {
                    throw new InvalidOperationException(
                        "The WPF UI Automation host failed.",
                        _failure);
                }
                handle = _windowHandle;
            }
            var element = AutomationElement.FromHandle(handle);
            Assert.Equal(expectedName, element.Current.Name);
            return element;
        }

        public void WaitForExit()
        {
            Assert.True(
                _thread.Join(TimeSpan.FromSeconds(10)),
                "The WPF UI Automation host did not exit.");
            lock (_gate)
            {
                if (_failure is not null)
                {
                    throw new InvalidOperationException(
                        "The WPF UI Automation host failed.",
                        _failure);
                }
            }
        }

        public Task OpenSaveDialogAsync()
        {
            var completion = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var dispatcher = Dispatcher.FromThread(_thread) ??
                throw new InvalidOperationException(
                    "The WPF UI Automation dispatcher is unavailable.");
            _ = dispatcher.BeginInvoke(() =>
            {
                try
                {
                    var dialog = new Microsoft.Win32.SaveFileDialog
                    {
                        AddExtension = true,
                        CheckPathExists = true,
                        DefaultExt = ".zip",
                        FileName = "VR-Recorder-diagnostics.zip",
                        Filter = "ZIP archive (*.zip)|*.zip",
                        OverwritePrompt = true,
                        Title = "Save diagnostic bundle",
                    };
                    _ = dialog.ShowDialog();
                    completion.TrySetResult();
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
            });
            return completion.Task;
        }

        public void Dispose()
        {
            if (_thread.IsAlive)
            {
                Dispatcher.FromThread(_thread)?.BeginInvokeShutdown(
                    DispatcherPriority.Send);
                _thread.Join(TimeSpan.FromSeconds(5));
            }
            _windowReady.Dispose();
        }

        private void Run()
        {
            try
            {
                var application = new System.Windows.Application
                {
                    ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown,
                };
                foreach (var resource in new[]
                         {
                             "DesignTokens.xaml",
                             "Layout.ltr.xaml",
                             "Strings.en-US.xaml",
                         })
                {
                    application.Resources.MergedDictionaries.Add(
                        new System.Windows.ResourceDictionary
                        {
                            Source = new Uri(
                                $"pack://application:,,,/VRRecorder.App;component/Resources/{resource}"),
                        });
                }

                var diagnostics = new DiagnosticsWindow(
                    new DesktopDiagnosticsController(new RejectingExporter()));
                diagnostics.Closed += (_, _) =>
                    diagnostics.Dispatcher.BeginInvoke(ShowFirstRun);
                Publish(diagnostics);
                diagnostics.Show();
                Dispatcher.Run();
            }
            catch (Exception exception)
            {
                lock (_gate)
                {
                    _failure = exception;
                }
                _windowReady.Set();
            }
        }

        private void ShowFirstRun()
        {
            var setup = new FirstRunSetupController(new EmptySetupStore());
            var window = new FirstRunSetupWindow(
                new FirstRunSetupUiController(setup),
                new FirstRunSetupVerificationController(
                    setup,
                    new SuccessfulProbe()));
            window.Closed += (_, _) =>
                window.Dispatcher.BeginInvokeShutdown(
                    DispatcherPriority.Send);
            Publish(window);
            window.Show();
        }

        private void Publish(System.Windows.Window window)
        {
            window.SourceInitialized += (_, _) =>
            {
                var handle = new System.Windows.Interop.WindowInteropHelper(
                    window).Handle;
                lock (_gate)
                {
                    _windowHandle = handle;
                }
                _windowReady.Set();
            };
        }

        private void ThrowFailureOrTimeout(string expectedName)
        {
            lock (_gate)
            {
                throw new InvalidOperationException(
                    $"The WPF UI Automation host did not show '{expectedName}'.",
                    _failure);
            }
        }
    }

    private sealed class RejectingExporter : IDiagnosticBundleExporter
    {
        public Task<DiagnosticBundleExport> ExportAsync(
            string destinationPath,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                "The Save dialog smoke must cancel before export.");
    }

    private sealed class EmptySetupStore : IFirstRunSetupStore
    {
        public Task<FirstRunSetupProgress?> LoadAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<FirstRunSetupProgress?>(null);

        public Task SaveAsync(
            FirstRunSetupProgress progress,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class SuccessfulProbe : IFirstRunSetupProbe
    {
        public Task<bool> VerifyAsync(
            FirstRunSetupStep setupStep,
            CancellationToken cancellationToken) => Task.FromResult(true);
    }
}
