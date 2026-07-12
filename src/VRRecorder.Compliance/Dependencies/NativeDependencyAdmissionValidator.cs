namespace VRRecorder.Compliance.Dependencies;

public static class NativeDependencyAdmissionValidator
{
    public static NativeDependencyAdmissionReport Validate(
        IEnumerable<NativeLinkObservation> observations,
        IEnumerable<NativeDependencyAdmission> admissions,
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

        var dependencies = new List<AdmittedNativeDependency>();
        var issues = new List<ComplianceIssue>();
        foreach (var observation in observationArray)
        {
            var matches = admissionArray
                .Where(admission => Matches(observation, admission))
                .ToArray();
            var subject = Subject(observation);
            if (matches.Length == 0)
            {
                issues.Add(new ComplianceIssue(
                    "unregistered-native-link",
                    subject));
                continue;
            }

            if (matches.Length != 1)
            {
                issues.Add(new ComplianceIssue(
                    "ambiguous-native-admission",
                    subject));
                continue;
            }

            var admission = matches[0];
            if (admission.Origin == NativeDependencyOrigin.ThirdParty)
            {
                if (string.IsNullOrWhiteSpace(admission.ComponentId) ||
                    !registeredComponents.Contains(admission.ComponentId))
                {
                    issues.Add(new ComplianceIssue(
                        "unregistered-native-component",
                        subject));
                    continue;
                }
            }
            else if (admission.ComponentId is not null)
            {
                issues.Add(new ComplianceIssue(
                    "invalid-native-component-owner",
                    subject));
                continue;
            }

            dependencies.Add(new AdmittedNativeDependency(
                observation.ConsumerTarget,
                observation.InputIdentity,
                observation.InputKind,
                observation.Platform,
                admission.Origin,
                admission.ComponentId));
        }

        return new NativeDependencyAdmissionReport(dependencies, issues);
    }

    private static bool Matches(
        NativeLinkObservation observation,
        NativeDependencyAdmission admission) =>
        string.Equals(
            observation.ConsumerTarget,
            admission.ConsumerTarget,
            StringComparison.Ordinal) &&
        string.Equals(
            observation.InputIdentity,
            admission.InputIdentity,
            StringComparison.Ordinal) &&
        observation.InputKind == admission.InputKind &&
        string.Equals(
            observation.Platform,
            admission.Platform,
            StringComparison.Ordinal);

    private static string Subject(NativeLinkObservation observation) =>
        $"{observation.ConsumerTarget}:{observation.InputIdentity}";

    private static void ValidateInputs(
        IEnumerable<NativeLinkObservation> observations,
        IEnumerable<NativeDependencyAdmission> admissions,
        IEnumerable<string> registeredComponentIds)
    {
        foreach (var observation in observations)
        {
            ArgumentNullException.ThrowIfNull(observation);
            ArgumentException.ThrowIfNullOrWhiteSpace(
                observation.ConsumerTarget);
            ArgumentException.ThrowIfNullOrWhiteSpace(
                observation.InputIdentity);
            ArgumentException.ThrowIfNullOrWhiteSpace(observation.Platform);
            if (!Enum.IsDefined(observation.InputKind))
            {
                throw new ArgumentOutOfRangeException(nameof(observations));
            }
        }

        foreach (var admission in admissions)
        {
            ArgumentNullException.ThrowIfNull(admission);
            ArgumentException.ThrowIfNullOrWhiteSpace(
                admission.ConsumerTarget);
            ArgumentException.ThrowIfNullOrWhiteSpace(
                admission.InputIdentity);
            ArgumentException.ThrowIfNullOrWhiteSpace(admission.Platform);
            if (!Enum.IsDefined(admission.InputKind) ||
                !Enum.IsDefined(admission.Origin))
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
