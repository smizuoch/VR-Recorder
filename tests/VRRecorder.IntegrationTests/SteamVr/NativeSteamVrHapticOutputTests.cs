using System.Runtime.InteropServices;
using VRRecorder.Application.Haptics;
using VRRecorder.Application.Settings;
using VRRecorder.Infrastructure.SteamVr;

namespace VRRecorder.IntegrationTests.SteamVr;

public sealed class NativeSteamVrHapticOutputTests
{
    [Theory]
    [InlineData(VrHand.Left, "/user/hand/left")]
    [InlineData(VrHand.Right, "/user/hand/right")]
    public async Task PlaysEveryPulseForSelectedHandAndDestroysHandle(
        VrHand hand,
        string expectedInputSource)
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var install = TemporaryInstall.Create();
        using var controls = new NativeSteamVrHapticFixtureControls(
            FixturePath());
        Assert.False(controls.IsActive());

        using (var output = new NativeSteamVrHapticOutput(
                   FixturePath(),
                   install.Path,
                   hand))
        {
            Assert.True(controls.IsActive());
            Assert.Equal(
                Path.Combine(install.Path, "OpenVr", "actions.json"),
                controls.ManifestPath());
            Assert.Equal(
                "/actions/vrrecorder/out/haptic",
                controls.ActionPath());
            Assert.Equal(expectedInputSource, controls.InputSourcePath());

            await output.PlayAsync(
                new WristHapticPattern(
                    TimeSpan.FromMilliseconds(20),
                    pulseCount: 2,
                    frequencyHertz: 120,
                    amplitude: 0.65f),
                CancellationToken.None);

            Assert.Equal(2u, controls.TriggerCount());
            Assert.Equal(0.02f, controls.LastDurationSeconds(), 3);
            Assert.Equal(120f, controls.LastFrequencyHertz());
            Assert.Equal(0.65f, controls.LastAmplitude());
        }

        Assert.False(controls.IsActive());
    }

    [Fact]
    public async Task NativeTriggerFailureBecomesManagedHapticException()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var install = TemporaryInstall.Create();
        using var controls = new NativeSteamVrHapticFixtureControls(
            FixturePath());
        using var output = new NativeSteamVrHapticOutput(
            FixturePath(),
            install.Path,
            VrHand.Right);
        controls.SetStatus(4);

        var exception = await Assert.ThrowsAsync<SteamVrHapticException>(() =>
            output.PlayAsync(
                new WristHapticPattern(
                    TimeSpan.FromMilliseconds(80),
                    pulseCount: 1,
                    frequencyHertz: 120,
                    amplitude: 0.65f),
                CancellationToken.None));

        Assert.Equal(4, exception.Status);
        Assert.Equal("trigger", exception.Operation);
        Assert.Equal(1u, controls.TriggerCount());
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

    private sealed class NativeSteamVrHapticFixtureControls : IDisposable
    {
        private readonly nint _library;
        private readonly SetStatusDelegate _setStatus;
        private readonly ByteResultDelegate _isActive;
        private readonly PointerResultDelegate _manifestPath;
        private readonly PointerResultDelegate _actionPath;
        private readonly PointerResultDelegate _inputSourcePath;
        private readonly UInt32ResultDelegate _triggerCount;
        private readonly FloatResultDelegate _lastDurationSeconds;
        private readonly FloatResultDelegate _lastFrequencyHertz;
        private readonly FloatResultDelegate _lastAmplitude;

        public NativeSteamVrHapticFixtureControls(string path)
        {
            _library = NativeLibrary.Load(path);
            _setStatus = Resolve<SetStatusDelegate>(
                "vrrec_test_steamvr_haptic_set_status");
            _isActive = Resolve<ByteResultDelegate>(
                "vrrec_test_steamvr_haptic_active");
            _manifestPath = Resolve<PointerResultDelegate>(
                "vrrec_test_steamvr_haptic_manifest_path");
            _actionPath = Resolve<PointerResultDelegate>(
                "vrrec_test_steamvr_haptic_action_path");
            _inputSourcePath = Resolve<PointerResultDelegate>(
                "vrrec_test_steamvr_haptic_input_source_path");
            _triggerCount = Resolve<UInt32ResultDelegate>(
                "vrrec_test_steamvr_haptic_trigger_count");
            _lastDurationSeconds = Resolve<FloatResultDelegate>(
                "vrrec_test_steamvr_haptic_last_duration_seconds");
            _lastFrequencyHertz = Resolve<FloatResultDelegate>(
                "vrrec_test_steamvr_haptic_last_frequency_hertz");
            _lastAmplitude = Resolve<FloatResultDelegate>(
                "vrrec_test_steamvr_haptic_last_amplitude");
        }

        public void SetStatus(int status) => _setStatus(status);

        public bool IsActive() => _isActive() != 0;

        public string ManifestPath() => ReadUtf8(_manifestPath());

        public string ActionPath() => ReadUtf8(_actionPath());

        public string InputSourcePath() => ReadUtf8(_inputSourcePath());

        public uint TriggerCount() => _triggerCount();

        public float LastDurationSeconds() => _lastDurationSeconds();

        public float LastFrequencyHertz() => _lastFrequencyHertz();

        public float LastAmplitude() => _lastAmplitude();

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
        private delegate void SetStatusDelegate(int status);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate byte ByteResultDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate nint PointerResultDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate uint UInt32ResultDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate float FloatResultDelegate();
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
                $"vr-recorder-native-haptic-{Guid.NewGuid():N}");
            var openVrPath = System.IO.Path.Combine(path, "OpenVr");
            Directory.CreateDirectory(openVrPath);
            File.WriteAllText(
                System.IO.Path.Combine(openVrPath, "actions.json"),
                "{}");
            File.Copy(
                System.IO.Path.Combine(
                    FindRepositoryRoot(),
                    "src",
                    "VRRecorder.Infrastructure.SteamVr",
                    "OpenVr",
                    "steamvr.vrmanifest"),
                System.IO.Path.Combine(openVrPath, "steamvr.vrmanifest"));
            File.WriteAllBytes(
                System.IO.Path.Combine(path, "VRRecorder.App.exe"),
                [0x4d, 0x5a]);
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
