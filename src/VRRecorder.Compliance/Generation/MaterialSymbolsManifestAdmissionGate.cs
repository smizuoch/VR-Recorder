namespace VRRecorder.Compliance.Generation;

internal static class MaterialSymbolsManifestAdmissionGate
{
    private const string ComponentId = "material-symbols";
    private const string ManifestPath = "MATERIAL-SYMBOLS-MANIFEST.json";
    private const string Repository =
        "https://github.com/google/material-design-icons";
    private const string Browser = "https://fonts.google.com/icons";
    private const string LicenseExpression = "Apache-2.0";
    private const string AcquisitionMode = "approved-update-pr-only";
    private const string DisplayName =
        "Material Symbols (Material Design icons by Google)";
    private const string OutputRoot =
        "src/VRRecorder.DesignSystem/Assets/MaterialSymbols/";
    private static readonly string[] RequiredProhibitedAssetClasses =
    [
        "google-logo",
        "google-g",
        "google-product-icon",
        "third-party-logo",
        "trademark-or-brand-mark",
        "unregistered-symbol",
        "runtime-cdn-font",
    ];
    public static IReadOnlyList<ComplianceIssue> Validate(
        IReadOnlyList<NormalizedComponent> components,
        bool materialComponentRequired)
    {
        ArgumentNullException.ThrowIfNull(components);
        var issues = new List<ComplianceIssue>();
        var materialComponents = components.Where(component =>
                string.Equals(
                    component.Id,
                    ComponentId,
                    StringComparison.Ordinal))
            .ToArray();
        var manifestOwners = components.Where(component =>
                component.LegalFiles.Any(file =>
                    file.Kind == LegalFileKind.AssetManifest))
            .ToArray();

        foreach (var owner in manifestOwners.Where(component =>
                     !string.Equals(
                         component.Id,
                         ComponentId,
                         StringComparison.Ordinal)))
        {
            issues.Add(Issue(
                "material-symbols-manifest-owner-invalid",
                owner.Id));
        }

        if (materialComponents.Length == 0)
        {
            if (materialComponentRequired)
            {
                issues.Add(Issue(
                    "material-symbols-manifest-missing",
                    ComponentId));
            }

            return Order(issues);
        }

        if (materialComponents.Length != 1)
        {
            issues.Add(Issue(
                "material-symbols-component-count-invalid",
                ComponentId));
            return Order(issues);
        }

        var component = materialComponents[0];
        if (!string.Equals(
                component.License.DeclaredExpression,
                LicenseExpression,
                StringComparison.Ordinal) ||
            !string.Equals(
                component.License.ConcludedExpression,
                LicenseExpression,
                StringComparison.Ordinal))
        {
            issues.Add(Issue(
                "material-symbols-license-policy-mismatch",
                ComponentId));
        }

        var manifests = component.LegalFiles.Where(file =>
                file.Kind == LegalFileKind.AssetManifest)
            .ToArray();
        if (manifests.Length == 0)
        {
            issues.Add(Issue(
                "material-symbols-manifest-missing",
                ComponentId));
            return Order(issues);
        }

        if (manifests.Length != 1)
        {
            issues.Add(Issue(
                "material-symbols-manifest-count-invalid",
                ComponentId));
            return Order(issues);
        }

        var manifestFile = manifests[0];
        if (!string.Equals(
                manifestFile.RelativePath,
                ManifestPath,
                StringComparison.Ordinal))
        {
            issues.Add(Issue(
                "material-symbols-manifest-invalid",
                ComponentId));
            return Order(issues);
        }

        if (!MaterialSymbolsManifestReader.TryParse(
                manifestFile.Utf8Content,
                out var manifest))
        {
            issues.Add(Issue(
                "material-symbols-manifest-invalid",
                ComponentId));
            return Order(issues);
        }

        if (!HasValidStructure(manifest))
        {
            issues.Add(Issue(
                "material-symbols-manifest-invalid",
                ComponentId));
            return Order(issues);
        }

        var validatedManifest = manifest!;
        if (ContainsPlaceholder(validatedManifest))
        {
            issues.Add(Issue(
                "material-symbols-placeholder-value",
                ComponentId));
        }

        ValidateSource(component, validatedManifest.Source, issues);
        ValidateLegalDocuments(component, validatedManifest.Source, issues);
        ValidateRightsPolicy(validatedManifest.RightsPolicy, issues);
        ValidateRendering(validatedManifest.Rendering, issues);
        ValidateValidationPolicy(validatedManifest.Validation, issues);
        ValidateIcons(validatedManifest.SelectedIcons, issues);
        return Order(issues);
    }

    private static bool HasValidStructure(
        MaterialSymbolsManifestDocument? manifest) =>
        manifest is not null &&
        manifest.SchemaVersion == 2 &&
        string.Equals(
            manifest.ComponentId,
            ComponentId,
            StringComparison.Ordinal) &&
        string.Equals(
            manifest.DisplayName,
            DisplayName,
            StringComparison.Ordinal) &&
        manifest.Source is not null &&
        manifest.RightsPolicy is not null &&
        manifest.Rendering is not null &&
        manifest.Validation is not null &&
        manifest.SelectedIcons is { Length: > 0 } &&
        manifest.SelectedIcons.All(icon => icon is not null);

    private static void ValidateSource(
        NormalizedComponent component,
        MaterialSymbolsManifestSource source,
        List<ComplianceIssue> issues)
    {
        var validCommit = IsLowerHex(source.Commit, 40) ||
                          IsLowerHex(source.Commit, 64);
        if (!string.Equals(source.Repository, Repository, StringComparison.Ordinal) ||
            !string.Equals(source.Browser, Browser, StringComparison.Ordinal) ||
            !string.Equals(
                source.LicenseExpression,
                LicenseExpression,
                StringComparison.Ordinal) ||
            !string.Equals(
                source.AcquisitionMode,
                AcquisitionMode,
                StringComparison.Ordinal) ||
            source.RuntimeNetworkAllowed ||
            source.NoticeStatus is not ("present" or "absent") ||
            !IsSafeRelativePath(source.CodepointsFile) ||
            !validCommit ||
            !string.Equals(
                component.Version,
                source.Commit,
                StringComparison.Ordinal) ||
            !string.Equals(
                component.SourceInformation,
                $"{Repository}@{source.Commit}",
                StringComparison.Ordinal))
        {
            issues.Add(Issue(
                "material-symbols-source-policy-mismatch",
                ComponentId));
        }
    }

    private static void ValidateLegalDocuments(
        NormalizedComponent component,
        MaterialSymbolsManifestSource source,
        List<ComplianceIssue> issues)
    {
        var licenseMatches = component.LegalFiles.Any(file =>
            file.Kind == LegalFileKind.License &&
            string.Equals(
                file.RelativePath,
                source.LicenseFile,
                StringComparison.Ordinal));
        var attributionMatches = component.LegalFiles.Any(file =>
            file.Kind == LegalFileKind.Attribution &&
            string.Equals(
                file.RelativePath,
                source.AttributionFile,
                StringComparison.Ordinal));
        if (!licenseMatches || !attributionMatches)
        {
            issues.Add(Issue(
                "material-symbols-legal-document-mismatch",
                ComponentId));
        }

        if (string.Equals(
                source.NoticeStatus,
                "present",
                StringComparison.Ordinal) &&
            !component.LegalFiles.Any(file =>
                file.Kind == LegalFileKind.Notice))
        {
            issues.Add(Issue(
                "material-symbols-notice-document-missing",
                ComponentId));
        }
    }

    private static void ValidateRightsPolicy(
        MaterialSymbolsManifestRightsPolicy policy,
        List<ComplianceIssue> issues)
    {
        var prohibited = policy.ProhibitedAssetClasses ?? [];
        var required = RequiredProhibitedAssetClasses.ToHashSet(
            StringComparer.Ordinal);
        if (!string.Equals(
                policy.PermittedAssetClass,
                "general-user-interface-symbol",
                StringComparison.Ordinal) ||
            !policy.AttributionDisplayedInLegalUi ||
            !policy.LicenseTextBundledOffline ||
            !policy.SourceAndOutputHashesRequired ||
            !policy.ModificationRecordRequired ||
            policy.ExclusiveTrademarkUseAllowed ||
            policy.ImplyGoogleAffiliationAllowed ||
            prohibited.Length != required.Count ||
            !required.SetEquals(prohibited))
        {
            issues.Add(Issue(
                "material-symbols-rights-policy-mismatch",
                ComponentId));
        }
    }

    private static void ValidateRendering(
        MaterialSymbolsManifestRendering rendering,
        List<ComplianceIssue> issues)
    {
        if (!string.Equals(
                rendering.Family,
                "Material Symbols Rounded",
                StringComparison.Ordinal) ||
            !string.Equals(rendering.SourceFormat, "svg", StringComparison.Ordinal) ||
            rendering.OutputFormat is not ("optimized-svg" or
                "project-glyph-atlas") ||
            !IsValidAxes(rendering.DefaultAxes) ||
            rendering.PrimaryActionOpticalSize != 48 ||
            string.IsNullOrWhiteSpace(rendering.ConversionTool) ||
            !IsSafeRelativePath(rendering.ConversionRecipe) ||
            !rendering.DeterministicOutputRequired ||
            rendering.ExternalFontFileAtRuntime)
        {
            issues.Add(Issue(
                "material-symbols-rendering-policy-mismatch",
                ComponentId));
        }
    }

    private static void ValidateValidationPolicy(
        MaterialSymbolsManifestValidation validation,
        List<ComplianceIssue> issues)
    {
        if (!validation.FailOnUnknownUpstreamName ||
            !validation.FailOnCodepointMismatch ||
            !validation.FailOnUnregisteredOutputAsset ||
            !validation.FailOnMissingAccessibleNameForInteractiveIcon ||
            !validation.FailOnMissingTooltipForAmbiguousIconOnlyControl ||
            !validation.FailOnIncorrectRtlMirroring ||
            !validation.FailOnSourceOrOutputHashMismatch ||
            !validation.FailOnLicenseOrAttributionMismatch)
        {
            issues.Add(Issue(
                "material-symbols-validation-policy-mismatch",
                ComponentId));
        }
    }

    private static void ValidateIcons(
        MaterialSymbolsManifestIcon[] icons,
        List<ComplianceIssue> issues)
    {
        var semanticIds = new HashSet<string>(StringComparer.Ordinal);
        var outputPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < icons.Length; index++)
        {
            var icon = icons[index];
            var subject = string.IsNullOrWhiteSpace(icon.SemanticId)
                ? $"{ComponentId}:icon-{index}"
                : $"{ComponentId}:{icon.SemanticId}";
            if (!semanticIds.Add(icon.SemanticId) ||
                !outputPaths.Add(icon.OutputPath))
            {
                issues.Add(Issue(
                    "material-symbols-icon-duplicate",
                    subject));
            }

            if (!IsLowerHex(icon.SourceSha256, 64) ||
                !IsLowerHex(icon.OutputSha256, 64))
            {
                issues.Add(Issue(
                    "material-symbols-icon-hash-invalid",
                    subject));
            }

            if (!IsValidSemanticId(icon.SemanticId) ||
                !IsLowerHexCodepoint(icon.Codepoint))
            {
                issues.Add(Issue(
                    "material-symbols-icon-identity-invalid",
                    subject));
            }

            if (!IsSafeRelativePath(icon.SourcePath))
            {
                issues.Add(Issue(
                    "material-symbols-icon-source-path-invalid",
                    subject));
            }

            if (!HasValidSurfaces(icon.Surfaces))
            {
                issues.Add(Issue(
                    "material-symbols-icon-surface-invalid",
                    subject));
            }

            if (!IsApprovedOutputPath(icon.OutputPath))
            {
                issues.Add(Issue(
                    "material-symbols-output-path-invalid",
                    subject));
            }

            if (icon.Modified &&
                string.IsNullOrWhiteSpace(icon.ModificationNotice))
            {
                issues.Add(Issue(
                    "material-symbols-modification-notice-missing",
                    subject));
            }

            if (icon.Interactive &&
                (string.IsNullOrWhiteSpace(icon.AccessibleNameKey) ||
                 string.IsNullOrWhiteSpace(icon.TooltipKey)))
            {
                issues.Add(Issue(
                    "material-symbols-accessibility-metadata-missing",
                    subject));
            }

            if (!icon.Interactive &&
                !HasNonInteractiveAccessibilityMetadata(icon))
            {
                issues.Add(Issue(
                    "material-symbols-accessibility-metadata-missing",
                    subject));
            }

            if (string.IsNullOrWhiteSpace(icon.UpstreamName) ||
                !string.Equals(icon.Style, "rounded", StringComparison.Ordinal) ||
                !HasExactlyOneAxesContract(icon))
            {
                issues.Add(Issue(
                    "material-symbols-manifest-invalid",
                    subject));
            }
        }
    }

    private static bool HasExactlyOneAxesContract(
        MaterialSymbolsManifestIcon icon)
    {
        if ((icon.Axes is null) == (icon.StateVariants is null))
        {
            return false;
        }

        if (icon.Axes is not null)
        {
            return IsValidAxes(icon.Axes);
        }

        return icon.StateVariants is { Count: > 0 } &&
               icon.StateVariants.All(variant =>
                   !string.IsNullOrWhiteSpace(variant.Key) &&
                   IsValidAxes(variant.Value));
    }

    private static bool IsValidAxes(MaterialSymbolsManifestAxes? axes) =>
        axes is not null &&
        axes.Fill is 0 or 1 &&
        axes.Weight is >= 100 and <= 700 &&
        axes.Grade is >= -25 and <= 200 &&
        axes.OpticalSize is >= 20 and <= 48;

    private static bool HasNonInteractiveAccessibilityMetadata(
        MaterialSymbolsManifestIcon icon) =>
        !string.IsNullOrWhiteSpace(icon.AccessibleDescriptionKey) ||
        (!string.IsNullOrWhiteSpace(icon.VisibleLabelKey) &&
         icon.DecorativeWhenVisibleLabelPresent is true);

    private static bool HasValidSurfaces(string[] surfaces)
    {
        if (surfaces.Length == 0 || surfaces.Length > 2)
        {
            return false;
        }

        var distinct = new HashSet<string>(StringComparer.Ordinal);
        return surfaces.All(surface =>
            surface is "wrist" or "desktop" && distinct.Add(surface));
    }

    private static bool IsValidSemanticId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var segments = value.Split('.');
        return segments.All(segment =>
            segment.Length > 0 &&
            segment[0] is >= 'a' and <= 'z' &&
            segment.Skip(1).All(character =>
                character is >= 'a' and <= 'z' or
                    >= 'A' and <= 'Z' or
                    >= '0' and <= '9'));
    }

    private static bool IsLowerHexCodepoint(string value) =>
        value is { Length: >= 4 and <= 6 } &&
        value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f') &&
        int.TryParse(
            value,
            System.Globalization.NumberStyles.HexNumber,
            System.Globalization.CultureInfo.InvariantCulture,
            out var codepoint) &&
        codepoint <= 0x10ffff &&
        codepoint is not (>= 0xd800 and <= 0xdfff);

    private static bool IsSafeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            path.StartsWith('/') ||
            path.Contains('\\') ||
            path.Contains(':'))
        {
            return false;
        }

        var segments = path.Split('/');
        return segments.All(segment =>
            segment.Length > 0 && segment is not "." and not "..");
    }

    private static bool IsApprovedOutputPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            !path.StartsWith(OutputRoot, StringComparison.Ordinal) ||
            path.Length == OutputRoot.Length ||
            Path.IsPathRooted(path))
        {
            return false;
        }

        try
        {
            _ = LegalArtifactPath.Resolve(Environment.CurrentDirectory, path);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool ContainsPlaceholder(
        MaterialSymbolsManifestDocument manifest)
    {
        IEnumerable<string?> values =
        [
            manifest.Source.Commit,
            manifest.Source.CodepointsFile,
            manifest.Source.LicenseFile,
            manifest.Source.AttributionFile,
            manifest.Source.NoticeStatus,
            manifest.Rendering.ConversionTool,
            manifest.Rendering.ConversionRecipe,
            .. manifest.SelectedIcons.SelectMany(icon => new[]
            {
                icon.SemanticId,
                icon.UpstreamName,
                icon.Codepoint,
                icon.SourcePath,
                icon.SourceSha256,
                icon.OutputPath,
                icon.OutputSha256,
                icon.ModificationNotice,
                icon.VisibleLabelKey,
                icon.AccessibleNameKey,
                icon.TooltipKey,
                icon.AccessibleDescriptionKey,
            }),
        ];
        return values.Any(value =>
            value?.Contains('<', StringComparison.Ordinal) == true ||
            value?.Contains('>', StringComparison.Ordinal) == true);
    }

    private static bool IsLowerHex(string value, int length) =>
        value is not null &&
        value.Length == length &&
        value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static ComplianceIssue Issue(string code, string subject) =>
        new(code, subject);

    private static ComplianceIssue[] Order(
        IEnumerable<ComplianceIssue> issues) =>
        issues
            .OrderBy(issue => issue.Code, StringComparer.Ordinal)
            .ThenBy(issue => issue.Subject, StringComparer.Ordinal)
            .ToArray();
}
