using System.Runtime.InteropServices;
using VRRecorder.Application.Ports;
using VRRecorder.Application.Presentation;
using VRRecorder.Application.Settings;
using VRRecorder.Domain.Recording;
using VRRecorder.Infrastructure.SteamVr;
using VRRecorder.Presentation.Wrist;

namespace VRRecorder.IntegrationTests.SteamVr;

public sealed class NativeSteamVrOverlayLifecycleTests
{
    [Fact]
    public async Task ProductionLifecycleDrivesPlacementCoordinatorPort()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var install = TemporaryInstall.Create();
        using var controls = new NativeSteamVrOverlayFixtureControls(
            FixturePath());
        controls.Reset();
        using var overlay = new NativeSteamVrOverlayLifecycle(
            FixturePath(),
            install.Path);
        IWristOverlayPlacementRuntime runtime = overlay;
        var settings = VRRecorderSettings.CreateDefault() with
        {
            Vr = VRRecorderSettings.CreateDefault().Vr with
            {
                Hand = VrHand.Right,
                PlacementMode = OverlayPlacementMode.WorldPin,
                Transform = new OverlayTransform(
                    [1.25, 1.5, -2],
                    [0, 45, 0]),
            },
        };
        var store = new TrackingSettingsStore(settings);
        using var coordinator = new WristOverlayPlacementCoordinator(
            store,
            runtime);

        var applied = await coordinator.ApplySavedAsync(
            CancellationToken.None);

        Assert.Equal(OverlayPlacementMode.WorldPin, applied.PlacementMode);
        var profile = Assert.Single(store.Current.Vr.PlacementProfiles);
        Assert.Equal(VrHand.Right, profile.Hand);
        Assert.Equal(
            "{indexcontroller}/input/index_controller_profile.json",
            profile.Device.ControllerInputProfilePath);
        var readback = overlay.ReadPlacement();
        Assert.Equal(OverlayPlacementMode.WorldPin, readback.PlacementMode);
        Assert.True(WristOverlayPoseContract.MatchesReadback(
            readback.Transform,
            applied.Transform));
        IWristOverlayPlacementVerificationRuntime verification = overlay;
        verification.Show();
        var verifiedReadback = verification.ReadPlacement();
        Assert.True(controls.IsVisible());
        Assert.Equal(
            OverlayPlacementMode.WorldPin,
            verifiedReadback.PlacementMode);
        Assert.True(WristOverlayPoseContract.MatchesReadback(
            verifiedReadback.Transform,
            applied.Transform));
    }

    [Fact]
    public void AppliesAndReadsTypedWristDockAndWorldPinPoses()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var install = TemporaryInstall.Create();
        using var controls = new NativeSteamVrOverlayFixtureControls(
            FixturePath());
        controls.Reset();
        using var overlay = new NativeSteamVrOverlayLifecycle(
            FixturePath(),
            install.Path);
        var dockTransform = new OverlayTransform(
            [-0.03, 0.05, -0.08],
            [25, 0, -10]);

        overlay.ApplyPlacement(
            VrHand.Right,
            OverlayPlacementMode.WristDock,
            dockTransform);
        var dock = overlay.ReadPlacement();

        Assert.Equal(OverlayPlacementMode.WristDock, dock.PlacementMode);
        Assert.Equal(VrHand.Right, dock.DockHand);
        Assert.Null(dock.TrackingOrigin);
        Assert.Equal(
            WristOverlayPoseContract
                .ToOpenVrMatrix34(dockTransform)
                .ToArray(),
            dock.Transform.ToArray());
        Assert.Equal(
            new VrDeviceProfile(
                "lighthouse",
                "Valve Index",
                "{indexcontroller}/input/index_controller_profile.json"),
            overlay.ReadDeviceProfile(VrHand.Right));
        Assert.Equal(
            dock.Transform.ToArray(),
            overlay
                .ConvertPlacement(
                    VrHand.Right,
                    OverlayPlacementMode.WorldPin)
                .ToArray());

        var pinTransform = new OverlayTransform(
            [1.25, 1.5, -2],
            [0, 45, 0]);
        overlay.ApplyPlacement(
            VrHand.Left,
            OverlayPlacementMode.WorldPin,
            pinTransform);
        var pin = overlay.ReadPlacement();

        Assert.Equal(OverlayPlacementMode.WorldPin, pin.PlacementMode);
        Assert.Null(pin.DockHand);
        Assert.Equal(
            WristOverlayTrackingOrigin.Standing,
            pin.TrackingOrigin);
        Assert.Equal(
            WristOverlayPoseContract
                .ToOpenVrMatrix34(pinTransform)
                .ToArray(),
            pin.Transform.ToArray());

        overlay.Close();
        var applyException = Assert.Throws<SteamVrOverlayException>(() =>
            overlay.ApplyPlacement(
                VrHand.Left,
                OverlayPlacementMode.WristDock,
                dockTransform));
        Assert.Equal(3, applyException.Status);
        Assert.Equal("set pose", applyException.Operation);
        var readException = Assert.Throws<SteamVrOverlayException>(
            overlay.ReadPlacement);
        Assert.Equal(3, readException.Status);
        Assert.Equal("get pose", readException.Operation);
        var profileException = Assert.Throws<SteamVrOverlayException>(() =>
            overlay.ReadDeviceProfile(VrHand.Right));
        Assert.Equal(3, profileException.Status);
        Assert.Equal("get device profile size", profileException.Operation);
        var convertException = Assert.Throws<SteamVrOverlayException>(() =>
            overlay.ConvertPlacement(
                VrHand.Right,
                OverlayPlacementMode.WorldPin));
        Assert.Equal(3, convertException.Status);
        Assert.Equal("convert pose", convertException.Operation);
    }

    [Fact]
    public void AdaptsNativeLifecycleToWristPresentationPorts()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var install = TemporaryInstall.Create();
        using var controls = new NativeSteamVrOverlayFixtureControls(
            FixturePath());
        controls.Reset();
        var adapter = new NativeSteamVrWristOverlayAdapter(
            FixturePath(),
            install.Path);
        var frame = RenderFrame(revision: 10);

        adapter.Publish(frame);
        adapter.Show();
        Assert.Equal(1u, controls.TextureUpdateCount());
        Assert.True(controls.IsVisible());
        Assert.Null(adapter.PollPointerEvent());

        controls.PushPointerEvent(
            kind: 2,
            pixelX: 456,
            pixelY: 78,
            button: 1,
            cursorIndex: 3);
        var pointerEvent = adapter.PollPointerEvent();
        Assert.True(pointerEvent.HasValue);
        Assert.Equal(WristPointerEventKind.ButtonDown, pointerEvent.Value.Kind);
        Assert.Equal(456, pointerEvent.Value.PixelX);
        Assert.Equal(78, pointerEvent.Value.PixelY);
        Assert.Equal(WristPointerButton.Primary, pointerEvent.Value.Button);
        Assert.Equal(3u, pointerEvent.Value.CursorIndex);

        foreach (var expected in new[]
                 {
                     (
                         NativeKind: 1u,
                         NativeButton: 0u,
                         Kind: WristPointerEventKind.Move,
                         Button: WristPointerButton.None),
                     (
                         NativeKind: 2u,
                         NativeButton: 2u,
                         Kind: WristPointerEventKind.ButtonDown,
                         Button: WristPointerButton.Secondary),
                     (
                         NativeKind: 3u,
                         NativeButton: 4u,
                         Kind: WristPointerEventKind.ButtonUp,
                         Button: WristPointerButton.Middle),
                 })
        {
            controls.PushPointerEvent(
                expected.NativeKind,
                pixelX: 1,
                pixelY: 2,
                button: expected.NativeButton,
                cursorIndex: 4);
            var mapped = adapter.PollPointerEvent();
            Assert.True(mapped.HasValue);
            Assert.Equal(expected.Kind, mapped.Value.Kind);
            Assert.Equal(expected.Button, mapped.Value.Button);
        }

        adapter.Dispose();
        adapter.Dispose();
        Assert.False(controls.IsActive());
        Assert.Throws<ObjectDisposedException>(() => adapter.Publish(frame));
        Assert.Throws<ObjectDisposedException>(() =>
            adapter.PollPointerEvent());
    }

    [Fact]
    public void AdaptsAndOwnsAnExistingNativeLifecycle()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var install = TemporaryInstall.Create();
        using var controls = new NativeSteamVrOverlayFixtureControls(
            FixturePath());
        controls.Reset();
        using var overlay = new NativeSteamVrOverlayLifecycle(
            FixturePath(),
            install.Path);
        var adapter = new NativeSteamVrWristOverlayAdapter(overlay);

        adapter.Publish(RenderFrame(revision: 11));
        adapter.Show();
        adapter.Dispose();

        Assert.Equal(1u, controls.TextureUpdateCount());
        Assert.Equal(1u, controls.DestroyCount());
        Assert.False(controls.IsActive());
        Assert.Throws<ObjectDisposedException>(overlay.Show);
    }

    [Fact]
    public void PollsTypedPointerEventsOneAtATime()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var install = TemporaryInstall.Create();
        using var controls = new NativeSteamVrOverlayFixtureControls(
            FixturePath());
        controls.Reset();
        using var overlay = new NativeSteamVrOverlayLifecycle(
            FixturePath(),
            install.Path);

        Assert.Null(overlay.PollPointerEvent());
        controls.PushPointerEvent(
            kind: 1,
            pixelX: 10,
            pixelY: 20,
            button: 1,
            cursorIndex: 0);
        var malformedException = Assert.Throws<SteamVrOverlayException>(() =>
            overlay.PollPointerEvent());
        Assert.Equal(6, malformedException.Status);
        Assert.Equal("poll pointer event", malformedException.Operation);

        controls.PushPointerEvent(
            kind: 2,
            pixelX: 123,
            pixelY: 45,
            button: 1,
            cursorIndex: 7);
        var pointerEvent = overlay.PollPointerEvent();
        Assert.True(pointerEvent.HasValue);
        Assert.Equal(
            SteamVrOverlayPointerEventKind.ButtonDown,
            pointerEvent.Value.Kind);
        Assert.Equal(123, pointerEvent.Value.PixelX);
        Assert.Equal(45, pointerEvent.Value.PixelY);
        Assert.Equal(
            SteamVrOverlayPointerButton.Left,
            pointerEvent.Value.Button);
        Assert.Equal(7u, pointerEvent.Value.CursorIndex);
        Assert.Null(overlay.PollPointerEvent());

        overlay.Close();
        var exception = Assert.Throws<SteamVrOverlayException>(() =>
            overlay.PollPointerEvent());
        Assert.Equal(3, exception.Status);
        Assert.Equal("poll pointer event", exception.Operation);

        overlay.Dispose();
        Assert.Throws<ObjectDisposedException>(() =>
            overlay.PollPointerEvent());
    }

    [Fact]
    public void OwnsVersionedNativeOverlayUntilDisposed()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var install = TemporaryInstall.Create();
        using var controls = new NativeSteamVrOverlayFixtureControls(
            FixturePath());
        controls.Reset();

        using (var overlay = new NativeSteamVrOverlayLifecycle(
                   FixturePath(),
                   install.Path))
        {
            Assert.True(controls.IsActive());
            Assert.False(controls.IsVisible());
            Assert.Equal(
                Path.Combine(
                    install.Path,
                    "OpenVr",
                    "steamvr.vrmanifest"),
                controls.ManifestPath());
            Assert.Equal(
                "com.vrrecorder.desktop.wrist",
                controls.OverlayKey());
            Assert.Equal("VR Recorder Wrist", controls.OverlayName());
            Assert.Equal(0.22F, controls.WidthInMeters());

            var backingPixels = new byte[(1024 * 512 * 4) + 2];
            backingPixels[1] = 0x4a;
            backingPixels[^2] = 0x7c;
            var pixels = backingPixels.AsMemory(1, 1024 * 512 * 4);
            Assert.Throws<ArgumentException>(() =>
                overlay.UpdateBgraTexture(
                    ReadOnlyMemory<byte>.Empty,
                    1024,
                    512,
                    4096));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                overlay.UpdateBgraTexture(pixels, 1023, 512, 4096));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                overlay.UpdateBgraTexture(pixels, 1024, 511, 4096));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                overlay.UpdateBgraTexture(pixels, 1024, 512, 4095));
            Assert.Equal(0u, controls.TextureUpdateCount());
            overlay.UpdateBgraTexture(pixels, 1024, 512, 4096);
            overlay.UpdateBgraTexture(pixels, 1024, 512, 4096);
            Assert.Equal(2u, controls.TextureUpdateCount());
            Assert.Equal(0x4a, controls.TextureFirstByte());
            Assert.Equal(0x7c, controls.TextureLastByte());
            overlay.ClearTexture();
            overlay.ClearTexture();
            Assert.Equal(1u, controls.ClearTextureCount());

            overlay.Show();
            overlay.Show();
            Assert.True(controls.IsVisible());
            Assert.Equal(1u, controls.ShowCount());

            overlay.Hide();
            overlay.Hide();
            Assert.False(controls.IsVisible());
            Assert.Equal(1u, controls.HideCount());

            overlay.Close();
            overlay.Close();
            Assert.Equal(1u, controls.CloseCount());
            var exception = Assert.Throws<SteamVrOverlayException>(
                overlay.Show);
            Assert.Equal(3, exception.Status);
            Assert.Equal("show", exception.Operation);
            var updateException = Assert.Throws<SteamVrOverlayException>(() =>
                overlay.UpdateBgraTexture(pixels, 1024, 512, 4096));
            Assert.Equal(3, updateException.Status);
            Assert.Equal("update texture", updateException.Operation);

            overlay.Dispose();
            overlay.Dispose();
            Assert.False(controls.IsActive());
            Assert.Equal(1u, controls.DestroyCount());
            Assert.Throws<ObjectDisposedException>(overlay.Show);
            Assert.Throws<ObjectDisposedException>(() =>
                overlay.UpdateBgraTexture(pixels, 1024, 512, 4096));
        }

        Assert.False(controls.IsActive());
        Assert.Equal(1u, controls.CloseCount());
        Assert.Equal(1u, controls.DestroyCount());
    }

    private static string FixturePath() => Path.Combine(
        FindRepositoryRoot(),
        "tests",
        "VRRecorder.Native.Tests",
        "build",
        "libvrrecorder_native_test.so");

    private static WristTextureFrame RenderFrame(long revision) =>
        new WristTextureRenderer(
                new OnePixelRasterAssets(),
                new WristTextureThemeSet(Theme(10), Theme(80)))
            .Render(
                new WristUiProjector(EnglishUiLocalizer.Instance).Project(
                    new RecorderStatusSnapshot(
                        revision,
                        RecorderState.Ready,
                        RecorderAvailableActions.Start)),
                WristLayoutOptions.Default);

    private static WristTextureTheme Theme(byte seed) => new(
        new WristTexturePalette(
            Opaque(seed, 1),
            Opaque(seed, 2),
            Opaque(seed, 3),
            Opaque(seed, 4),
            Opaque(seed, 5),
            Opaque(seed, 6),
            Opaque(seed, 7),
            Opaque(seed, 8),
            Opaque(seed, 9),
            Opaque(seed, 10),
            Opaque(seed, 11)),
        new WristTextureMetrics(20, 28, 20, 12, 48, 72, 36));

    private static WristBgra32 Opaque(byte seed, byte offset) => new(
        (byte)(seed + offset),
        (byte)(seed + offset + 1),
        (byte)(seed + offset + 2),
        byte.MaxValue);

    private sealed class OnePixelRasterAssets : IWristRasterAssetProvider
    {
        public bool TryRasterizeIcon(
            WristIconRasterRequest request,
            out WristAlphaMask? mask)
        {
            mask = new WristAlphaMask(1, 1, [byte.MaxValue]);
            return true;
        }

        public bool TryRasterizeText(
            WristTextRasterRequest request,
            out WristAlphaMask? mask)
        {
            mask = new WristAlphaMask(1, 1, [byte.MaxValue]);
            return true;
        }
    }

    private sealed class TrackingSettingsStore(VRRecorderSettings initial)
        : ISettingsStore
    {
        public VRRecorderSettings Current { get; private set; } = initial;

        public Task<VRRecorderSettings> LoadAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Current);
        }

        public Task SaveAsync(
            VRRecorderSettings settings,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Current = settings;
            return Task.CompletedTask;
        }
    }

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

    private sealed class NativeSteamVrOverlayFixtureControls : IDisposable
    {
        private readonly nint _library;
        private readonly VoidDelegate _reset;
        private readonly ByteResultDelegate _isActive;
        private readonly ByteResultDelegate _isVisible;
        private readonly PointerResultDelegate _manifestPath;
        private readonly PointerResultDelegate _overlayKey;
        private readonly PointerResultDelegate _overlayName;
        private readonly FloatResultDelegate _widthInMeters;
        private readonly UInt32ResultDelegate _showCount;
        private readonly UInt32ResultDelegate _hideCount;
        private readonly UInt32ResultDelegate _closeCount;
        private readonly UInt32ResultDelegate _destroyCount;
        private readonly UInt32ResultDelegate _textureUpdateCount;
        private readonly UInt32ResultDelegate _clearTextureCount;
        private readonly ByteResultDelegate _textureFirstByte;
        private readonly ByteResultDelegate _textureLastByte;
        private readonly PushPointerEventDelegate _pushPointerEvent;

        public NativeSteamVrOverlayFixtureControls(string path)
        {
            _library = NativeLibrary.Load(path);
            _reset = Resolve<VoidDelegate>("vrrec_test_steamvr_overlay_reset");
            _isActive = Resolve<ByteResultDelegate>(
                "vrrec_test_steamvr_overlay_active");
            _isVisible = Resolve<ByteResultDelegate>(
                "vrrec_test_steamvr_overlay_visible");
            _manifestPath = Resolve<PointerResultDelegate>(
                "vrrec_test_steamvr_overlay_manifest_path");
            _overlayKey = Resolve<PointerResultDelegate>(
                "vrrec_test_steamvr_overlay_key");
            _overlayName = Resolve<PointerResultDelegate>(
                "vrrec_test_steamvr_overlay_name");
            _widthInMeters = Resolve<FloatResultDelegate>(
                "vrrec_test_steamvr_overlay_width_in_meters");
            _showCount = Resolve<UInt32ResultDelegate>(
                "vrrec_test_steamvr_overlay_show_count");
            _hideCount = Resolve<UInt32ResultDelegate>(
                "vrrec_test_steamvr_overlay_hide_count");
            _closeCount = Resolve<UInt32ResultDelegate>(
                "vrrec_test_steamvr_overlay_close_count");
            _destroyCount = Resolve<UInt32ResultDelegate>(
                "vrrec_test_steamvr_overlay_destroy_count");
            _textureUpdateCount = Resolve<UInt32ResultDelegate>(
                "vrrec_test_steamvr_overlay_texture_update_count");
            _clearTextureCount = Resolve<UInt32ResultDelegate>(
                "vrrec_test_steamvr_overlay_clear_texture_count");
            _textureFirstByte = Resolve<ByteResultDelegate>(
                "vrrec_test_steamvr_overlay_texture_first_byte");
            _textureLastByte = Resolve<ByteResultDelegate>(
                "vrrec_test_steamvr_overlay_texture_last_byte");
            _pushPointerEvent = Resolve<PushPointerEventDelegate>(
                "vrrec_test_steamvr_overlay_push_pointer_event");
        }

        public void Reset() => _reset();

        public bool IsActive() => _isActive() != 0;

        public bool IsVisible() => _isVisible() != 0;

        public string ManifestPath() => ReadUtf8(_manifestPath());

        public string OverlayKey() => ReadUtf8(_overlayKey());

        public string OverlayName() => ReadUtf8(_overlayName());

        public float WidthInMeters() => _widthInMeters();

        public uint ShowCount() => _showCount();

        public uint HideCount() => _hideCount();

        public uint CloseCount() => _closeCount();

        public uint DestroyCount() => _destroyCount();

        public uint TextureUpdateCount() => _textureUpdateCount();

        public uint ClearTextureCount() => _clearTextureCount();

        public byte TextureFirstByte() => _textureFirstByte();

        public byte TextureLastByte() => _textureLastByte();

        public void PushPointerEvent(
            uint kind,
            uint pixelX,
            uint pixelY,
            uint button,
            uint cursorIndex) => _pushPointerEvent(
                kind,
                pixelX,
                pixelY,
                button,
                cursorIndex);

        public void Dispose() => NativeLibrary.Free(_library);

        private TDelegate Resolve<TDelegate>(string exportName)
            where TDelegate : Delegate =>
            Marshal.GetDelegateForFunctionPointer<TDelegate>(
                NativeLibrary.GetExport(_library, exportName));

        private static string ReadUtf8(nint value) =>
            Marshal.PtrToStringUTF8(value) ?? string.Empty;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void VoidDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate byte ByteResultDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate nint PointerResultDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate float FloatResultDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate uint UInt32ResultDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void PushPointerEventDelegate(
            uint kind,
            uint pixelX,
            uint pixelY,
            uint button,
            uint cursorIndex);
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
                $"vr-recorder-native-overlay-{Guid.NewGuid():N}");
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
