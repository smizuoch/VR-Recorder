using System.Text.Json;

namespace VRRecorder.Infrastructure.SteamVr;

public static class OpenVrApplicationManifest
{
    public const string StableAppKey = "com.vrrecorder.desktop";

    private const string ExpectedSource = "vrrecorder";
    private const string ExpectedExecutablePath = "../VRRecorder.App.exe";
    private const string ExpectedActionManifestPath = "actions.json";

    public static ValidatedOpenVrApplicationManifest ResolveAndValidate(
        string installRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installRoot);
        if (!Path.IsPathFullyQualified(installRoot))
        {
            throw new ArgumentException(
                "The application install root must be absolute.",
                nameof(installRoot));
        }

        var normalizedRoot = Path.GetFullPath(installRoot);
        var openVrDirectory = Path.Combine(normalizedRoot, "OpenVr");
        var manifestPath = Path.Combine(
            openVrDirectory,
            "steamvr.vrmanifest");
        RequireFile(
            manifestPath,
            "The packaged OpenVR application manifest was not found.");

        using var document = ParseManifest(manifestPath);
        var root = document.RootElement;
        RequireObject(root, "The OpenVR application manifest root");
        RequireExactProperties(root, "source", "applications");
        RequireExactString(root, "source", ExpectedSource);

        var applications = RequireProperty(root, "applications");
        if (applications.ValueKind != JsonValueKind.Array ||
            applications.GetArrayLength() != 1)
        {
            throw Invalid("The OpenVR manifest must define exactly one application.");
        }

        var application = applications[0];
        RequireObject(application, "The OpenVR application entry");
        RequireExactProperties(
            application,
            "app_key",
            "launch_type",
            "binary_path_windows",
            "action_manifest_path",
            "is_dashboard_overlay",
            "strings");
        RequireExactString(application, "app_key", StableAppKey);
        RequireExactString(application, "launch_type", "binary");
        RequireExactString(
            application,
            "binary_path_windows",
            ExpectedExecutablePath);
        RequireExactString(
            application,
            "action_manifest_path",
            ExpectedActionManifestPath);

        var isDashboardOverlay = RequireProperty(
            application,
            "is_dashboard_overlay");
        if (isDashboardOverlay.ValueKind is not JsonValueKind.False)
        {
            throw Invalid("The OpenVR application must not be a dashboard overlay.");
        }

        ValidateLocalizedStrings(RequireProperty(application, "strings"));

        var executablePath = Path.GetFullPath(Path.Combine(
            openVrDirectory,
            ExpectedExecutablePath));
        var actionManifestPath = Path.GetFullPath(Path.Combine(
            openVrDirectory,
            ExpectedActionManifestPath));
        if (!string.Equals(
                executablePath,
                Path.Combine(normalizedRoot, "VRRecorder.App.exe"),
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                actionManifestPath,
                Path.Combine(openVrDirectory, "actions.json"),
                StringComparison.OrdinalIgnoreCase))
        {
            throw Invalid("OpenVR manifest paths escaped the current install contract.");
        }

        RequireFile(
            executablePath,
            "The current VR Recorder executable was not found.");
        RequireFile(
            actionManifestPath,
            "The packaged OpenVR action manifest was not found.");

        return new ValidatedOpenVrApplicationManifest(
            manifestPath,
            StableAppKey,
            executablePath,
            actionManifestPath);
    }

    private static JsonDocument ParseManifest(string manifestPath)
    {
        try
        {
            return JsonDocument.Parse(File.ReadAllBytes(manifestPath));
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "The OpenVR application manifest is not valid JSON.",
                exception);
        }
    }

    private static void ValidateLocalizedStrings(JsonElement strings)
    {
        RequireObject(strings, "The OpenVR localized strings");
        RequireExactProperties(strings, "en_us", "ja_jp");
        foreach (var locale in new[] { "en_us", "ja_jp" })
        {
            var localization = RequireProperty(strings, locale);
            RequireObject(localization, $"The OpenVR {locale} strings");
            RequireExactProperties(localization, "name", "description");
            RequireNonBlankString(localization, "name");
            RequireNonBlankString(localization, "description");
        }
    }

    private static void RequireExactProperties(
        JsonElement element,
        params string[] expectedNames)
    {
        var expected = expectedNames.ToHashSet(StringComparer.Ordinal);
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!expected.Contains(property.Name) || !found.Add(property.Name))
            {
                throw Invalid(
                    $"Unexpected or duplicate OpenVR manifest property: {property.Name}.");
            }
        }

        if (!expected.SetEquals(found))
        {
            throw Invalid("The OpenVR manifest is missing a required property.");
        }
    }

    private static JsonElement RequireProperty(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            throw Invalid(
                $"The OpenVR manifest is missing property: {propertyName}.");
        }

        return property;
    }

    private static void RequireExactString(
        JsonElement element,
        string propertyName,
        string expected)
    {
        var value = RequireProperty(element, propertyName);
        if (value.ValueKind != JsonValueKind.String ||
            !string.Equals(value.GetString(), expected, StringComparison.Ordinal))
        {
            throw Invalid(
                $"The OpenVR manifest property {propertyName} is not the expected value.");
        }
    }

    private static void RequireNonBlankString(
        JsonElement element,
        string propertyName)
    {
        var value = RequireProperty(element, propertyName);
        if (value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw Invalid(
                $"The OpenVR manifest property {propertyName} must be a non-blank string.");
        }
    }

    private static void RequireObject(JsonElement element, string description)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw Invalid($"{description} must be a JSON object.");
        }
    }

    private static void RequireFile(string path, string message)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(message, path);
        }
    }

    private static InvalidDataException Invalid(string message) => new(message);
}

public sealed record ValidatedOpenVrApplicationManifest(
    string ManifestPath,
    string AppKey,
    string ExecutablePath,
    string ActionManifestPath);
