namespace VRRecorder.Application.Settings;

public static class VrOverlayPlacementProfiles
{
    public static VrOverlayPlacement Resolve(
        VrSettings settings,
        VrDeviceProfile device,
        VrHand hand)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(settings.PlacementProfiles);

        var profile = settings.PlacementProfiles.FirstOrDefault(candidate =>
            candidate.Hand == hand && DeviceEquals(candidate.Device, device));
        return profile is null
            ? new VrOverlayPlacement(
                settings.PlacementMode,
                settings.Transform)
            : new VrOverlayPlacement(
                profile.PlacementMode,
                profile.Transform);
    }

    public static VrSettings Upsert(
        VrSettings settings,
        VrOverlayPlacementProfile profile)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(profile.Device);
        ArgumentNullException.ThrowIfNull(settings.PlacementProfiles);

        var updated = settings.PlacementProfiles.ToList();
        var existingIndex = updated.FindIndex(candidate =>
            candidate.Hand == profile.Hand &&
            DeviceEquals(candidate.Device, profile.Device));
        if (existingIndex >= 0)
        {
            updated[existingIndex] = profile;
        }
        else
        {
            updated.Add(profile);
        }

        return settings with { PlacementProfiles = updated };
    }

    private static bool DeviceEquals(
        VrDeviceProfile left,
        VrDeviceProfile right) =>
        string.Equals(
            left.TrackingSystemName,
            right.TrackingSystemName,
            StringComparison.Ordinal) &&
        string.Equals(
            left.HmdModelNumber,
            right.HmdModelNumber,
            StringComparison.Ordinal) &&
        string.Equals(
            left.ControllerInputProfilePath,
            right.ControllerInputProfilePath,
            StringComparison.Ordinal);
}
