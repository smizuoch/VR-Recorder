using System.Runtime.InteropServices;
using VRRecorder.DesignSystem;
using VRRecorder.Infrastructure.SteamVr;

namespace VRRecorder.IntegrationTests.SteamVr;

public sealed class NativeSteamVrInputRuntimeTests
{
    [Fact]
    public async Task PollsVersionedNativeStateAndDestroysInputOnCancellation()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var install = TemporaryInstall.Create();
        using var controls = new NativeSteamVrFixtureControls(FixturePath());
        var runtime = new NativeSteamVrInputRuntime(
            FixturePath(),
            install.Path,
            pollInterval: TimeSpan.FromMilliseconds(12));
        using var cancellation = new CancellationTokenSource();
        await using var enumerator = runtime
            .ObserveDigitalActionAsync(
                RecordingInputContract.SteamVrToggleActionPath,
                cancellation.Token)
            .GetAsyncEnumerator(cancellation.Token);

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal(
            new SteamVrDigitalActionState(false, false, false),
            enumerator.Current);
        Assert.True(controls.IsInputActive());
        Assert.Equal(
            Path.Combine(install.Path, "OpenVr", "actions.json"),
            controls.ManifestPath());
        Assert.Equal("/actions/vrrecorder", controls.ActionSetPath());
        Assert.Equal(
            RecordingInputContract.SteamVrToggleActionPath,
            controls.ActionPath());

        controls.SetDigitalState(
            isActive: true,
            state: true,
            changed: true);
        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal(
            new SteamVrDigitalActionState(true, true, true),
            enumerator.Current);
        Assert.Equal(2u, controls.PollCount());

        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await enumerator.MoveNextAsync().AsTask());
        Assert.False(controls.IsInputActive());
    }

    private static string FixturePath() => Path.Combine(
        FindRepositoryRoot(),
        "tests",
        "VRRecorder.Native.Tests",
        "build",
        "libvrrecorder_native_test.so");

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "VR-Recorder.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException(
            "The VR-Recorder repository root was not found.");
    }

    private sealed class NativeSteamVrFixtureControls : IDisposable
    {
        private readonly nint _library;
        private readonly SetDigitalStateDelegate _setDigitalState;
        private readonly ByteResultDelegate _isInputActive;
        private readonly PointerResultDelegate _manifestPath;
        private readonly PointerResultDelegate _actionSetPath;
        private readonly PointerResultDelegate _actionPath;
        private readonly UInt32ResultDelegate _pollCount;

        public NativeSteamVrFixtureControls(string path)
        {
            _library = NativeLibrary.Load(path);
            _setDigitalState = Resolve<SetDigitalStateDelegate>(
                "vrrec_test_set_steamvr_digital_state");
            _isInputActive = Resolve<ByteResultDelegate>(
                "vrrec_test_steamvr_input_active");
            _manifestPath = Resolve<PointerResultDelegate>(
                "vrrec_test_steamvr_manifest_path");
            _actionSetPath = Resolve<PointerResultDelegate>(
                "vrrec_test_steamvr_action_set_path");
            _actionPath = Resolve<PointerResultDelegate>(
                "vrrec_test_steamvr_action_path");
            _pollCount = Resolve<UInt32ResultDelegate>(
                "vrrec_test_steamvr_poll_count");
        }

        public void SetDigitalState(bool isActive, bool state, bool changed) =>
            _setDigitalState(
                isActive ? (byte)1 : (byte)0,
                state ? (byte)1 : (byte)0,
                changed ? (byte)1 : (byte)0);

        public bool IsInputActive() => _isInputActive() != 0;

        public string ManifestPath() => ReadUtf8(_manifestPath());

        public string ActionSetPath() => ReadUtf8(_actionSetPath());

        public string ActionPath() => ReadUtf8(_actionPath());

        public uint PollCount() => _pollCount();

        public void Dispose() => NativeLibrary.Free(_library);

        private TDelegate Resolve<TDelegate>(string exportName)
            where TDelegate : Delegate =>
            Marshal.GetDelegateForFunctionPointer<TDelegate>(
                NativeLibrary.GetExport(_library, exportName));

        private static string ReadUtf8(nint value) =>
            Marshal.PtrToStringUTF8(value) ??
            throw new InvalidOperationException(
                "The native fixture returned an empty UTF-8 string.");

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void SetDigitalStateDelegate(
            byte isActive,
            byte state,
            byte changed);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate byte ByteResultDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate nint PointerResultDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate uint UInt32ResultDelegate();
    }

    private sealed class TemporaryInstall : IDisposable
    {
        private TemporaryInstall(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TemporaryInstall Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"vr-recorder-native-steamvr-{Guid.NewGuid():N}");
            var openVrPath = System.IO.Path.Combine(path, "OpenVr");
            Directory.CreateDirectory(openVrPath);
            File.WriteAllText(
                System.IO.Path.Combine(openVrPath, "actions.json"),
                "{}");
            return new TemporaryInstall(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
