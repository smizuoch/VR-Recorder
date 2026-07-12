namespace VRRecorder.Compliance.Dependencies;

public static class NativeRuntimeLoadAdmissionValidator
{
    public static NativeRuntimeLoadAdmissionReport Validate(
        IEnumerable<NativeRuntimeLoadObservation> observations,
        IEnumerable<NativeRuntimeLoadAdmission> admissions,
        IEnumerable<string> registeredComponentIds)
    {
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(admissions);
        ArgumentNullException.ThrowIfNull(registeredComponentIds);
        var observationArray = observations.ToArray();
        var admissionArray = admissions.ToArray();
        var registeredComponents = new HashSet<string>(
            registeredComponentIds,
            StringComparer.Ordinal);
        ValidateInputs(observationArray, admissionArray, registeredComponents);

        var dependencies = new List<AdmittedNativeRuntimeLoad>();
        var issues = new List<ComplianceIssue>();
        foreach (var observation in observationArray)
        {
            var matches = admissionArray
                .Where(admission => Matches(observation, admission))
                .ToArray();
            var subject = $"{observation.Consumer}:{observation.FileName}";
            if (matches.Length == 0)
            {
                issues.Add(new ComplianceIssue(
                    "unregistered-runtime-load",
                    subject));
                continue;
            }

            if (matches.Length != 1)
            {
                issues.Add(new ComplianceIssue(
                    "ambiguous-runtime-load-admission",
                    subject));
                continue;
            }

            var admission = matches[0];
            var issueCode = ValidateOwnership(admission, registeredComponents);
            if (issueCode is not null)
            {
                issues.Add(new ComplianceIssue(issueCode, subject));
                continue;
            }

            dependencies.Add(new AdmittedNativeRuntimeLoad(
                observation.Consumer,
                observation.FileName,
                observation.Mechanism,
                observation.Platform,
                admission.Origin,
                admission.Integrity,
                admission.ComponentId));
        }

        return new NativeRuntimeLoadAdmissionReport(dependencies, issues);
    }

    private static string? ValidateOwnership(
        NativeRuntimeLoadAdmission admission,
        HashSet<string> registeredComponents) =>
        admission.Origin switch
        {
            NativeDependencyOrigin.FirstParty
                when admission.Integrity !=
                     NativeRuntimeIntegrity.ReleaseArtifact ||
                     admission.ComponentId is not null =>
                "invalid-runtime-load-owner",
            NativeDependencyOrigin.WindowsSystem
                when admission.Integrity !=
                     NativeRuntimeIntegrity.WindowsSystem ||
                     admission.ComponentId is not null =>
                "invalid-runtime-load-owner",
            NativeDependencyOrigin.ThirdParty
                when admission.Integrity !=
                     NativeRuntimeIntegrity.RegistrySha256 ||
                     string.IsNullOrWhiteSpace(admission.ComponentId) ||
                     !registeredComponents.Contains(admission.ComponentId) =>
                "unregistered-runtime-component",
            NativeDependencyOrigin.Toolchain =>
                "invalid-runtime-load-owner",
            _ => null,
        };

    private static bool Matches(
        NativeRuntimeLoadObservation observation,
        NativeRuntimeLoadAdmission admission) =>
        string.Equals(
            observation.Consumer,
            admission.Consumer,
            StringComparison.Ordinal) &&
        string.Equals(
            observation.FileName,
            admission.FileName,
            StringComparison.OrdinalIgnoreCase) &&
        observation.Mechanism == admission.Mechanism &&
        string.Equals(
            observation.Platform,
            admission.Platform,
            StringComparison.Ordinal);

    private static void ValidateInputs(
        IEnumerable<NativeRuntimeLoadObservation> observations,
        IEnumerable<NativeRuntimeLoadAdmission> admissions,
        IEnumerable<string> registeredComponentIds)
    {
        foreach (var observation in observations)
        {
            ArgumentNullException.ThrowIfNull(observation);
            ArgumentException.ThrowIfNullOrWhiteSpace(observation.Consumer);
            ArgumentException.ThrowIfNullOrWhiteSpace(observation.FileName);
            ArgumentException.ThrowIfNullOrWhiteSpace(observation.Platform);
            if (!Enum.IsDefined(observation.Mechanism))
            {
                throw new ArgumentOutOfRangeException(nameof(observations));
            }
        }

        foreach (var admission in admissions)
        {
            ArgumentNullException.ThrowIfNull(admission);
            ArgumentException.ThrowIfNullOrWhiteSpace(admission.Consumer);
            ArgumentException.ThrowIfNullOrWhiteSpace(admission.FileName);
            ArgumentException.ThrowIfNullOrWhiteSpace(admission.Platform);
            if (!Enum.IsDefined(admission.Mechanism) ||
                !Enum.IsDefined(admission.Origin) ||
                !Enum.IsDefined(admission.Integrity))
            {
                throw new ArgumentOutOfRangeException(nameof(admissions));
            }
        }

        foreach (var componentId in registeredComponentIds)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(componentId);
        }
    }
}
