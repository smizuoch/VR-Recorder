using VRRecorder.Application.Settings;

namespace VRRecorder.Application.Tests.Settings;

public sealed class VrOverlayPlacementProfilesTests
{
    [Fact]
    public void UnknownDeviceProfileFallsBackToMigratedGlobalPlacement()
    {
        var vr = VRRecorderSettings.CreateDefault().Vr with
        {
            Hand = VrHand.Right,
            PlacementMode = OverlayPlacementMode.WorldPin,
            Transform = Transform(1, 2, 3),
        };

        var resolved = VrOverlayPlacementProfiles.Resolve(
            vr,
            Device("lighthouse", "unknown-hmd", "/input/unknown_profile.json"),
            VrHand.Right);

        Assert.Equal(OverlayPlacementMode.WorldPin, resolved.PlacementMode);
        Assert.Equal([1d, 2d, 3d], resolved.Transform.Position);
    }

    [Fact]
    public void ExactHmdControllerAndHandProfileIsSelected()
    {
        var selectedDevice = Device(
            "lighthouse",
            "index-hmd",
            "/input/index_controller_profile.json");
        var vr = VRRecorderSettings.CreateDefault().Vr with
        {
            PlacementProfiles =
            [
                Profile(selectedDevice, VrHand.Left, 10),
                Profile(selectedDevice, VrHand.Right, 20),
                Profile(
                    Device(
                        "lighthouse",
                        "index-hmd",
                        "/input/other_controller_profile.json"),
                    VrHand.Left,
                    30),
                Profile(
                    Device(
                        "other-driver",
                        "index-hmd",
                        "/input/index_controller_profile.json"),
                    VrHand.Left,
                    40),
            ],
        };

        var left = VrOverlayPlacementProfiles.Resolve(
            vr,
            selectedDevice,
            VrHand.Left);
        var right = VrOverlayPlacementProfiles.Resolve(
            vr,
            selectedDevice,
            VrHand.Right);

        Assert.Equal(10, left.Transform.Position[0]);
        Assert.Equal(20, right.Transform.Position[0]);
    }

    [Fact]
    public void UpsertReplacesOnlyTheExactProfileWithoutCreatingDuplicates()
    {
        var selectedDevice = Device(
            "lighthouse",
            "index-hmd",
            "/input/index_controller_profile.json");
        var otherDevice = Device(
            "lighthouse",
            "vive-hmd",
            "/input/vive_controller_profile.json");
        var vr = VRRecorderSettings.CreateDefault().Vr with
        {
            PlacementProfiles =
            [
                Profile(selectedDevice, VrHand.Left, 10),
                Profile(otherDevice, VrHand.Left, 20),
            ],
        };

        var updated = VrOverlayPlacementProfiles.Upsert(
            vr,
            Profile(selectedDevice, VrHand.Left, 99));

        Assert.Equal(2, updated.PlacementProfiles.Count);
        Assert.Equal(
            99,
            VrOverlayPlacementProfiles.Resolve(
                updated,
                selectedDevice,
                VrHand.Left).Transform.Position[0]);
        Assert.Equal(
            20,
            VrOverlayPlacementProfiles.Resolve(
                updated,
                otherDevice,
                VrHand.Left).Transform.Position[0]);
    }

    private static VrDeviceProfile Device(
        string trackingSystem,
        string hmd,
        string controller) =>
        new(trackingSystem, hmd, controller);

    private static VrOverlayPlacementProfile Profile(
        VrDeviceProfile device,
        VrHand hand,
        double marker) =>
        new(
            device,
            hand,
            OverlayPlacementMode.WristDock,
            Transform(marker, 0, 0));

    private static OverlayTransform Transform(double x, double y, double z) =>
        new([x, y, z], [0, 0, 0]);
}
