using System.Runtime.InteropServices;
using VRRecorder.Infrastructure.SteamVr;

namespace VRRecorder.IntegrationTests.SteamVr;

public sealed class NativeSteamVrOverlayLifecycleTests
{
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
