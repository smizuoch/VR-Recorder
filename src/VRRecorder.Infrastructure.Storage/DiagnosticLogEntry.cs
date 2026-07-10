using System.Collections.ObjectModel;

namespace VRRecorder.Infrastructure.Storage;

public sealed record DiagnosticLogEntry
{
    private const int MaximumFieldCount = 64;
    private const int MaximumFieldValueLength = 4096;

    public DiagnosticLogEntry(
        DateTimeOffset timestampUtc,
        DiagnosticLogLevel level,
        string eventName,
        IReadOnlyDictionary<string, string> fields)
    {
        if (timestampUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "A diagnostic timestamp must be expressed in UTC.",
                nameof(timestampUtc));
        }

        if (!Enum.IsDefined(level))
        {
            throw new ArgumentOutOfRangeException(
                nameof(level),
                level,
                "The diagnostic log level is not supported.");
        }

        EnsureIdentifier(eventName, nameof(eventName));
        ArgumentNullException.ThrowIfNull(fields);
        if (fields.Count > MaximumFieldCount)
        {
            throw new ArgumentException(
                $"A diagnostic event cannot exceed {MaximumFieldCount} fields.",
                nameof(fields));
        }

        var sorted = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var field in fields)
        {
            EnsureIdentifier(field.Key, nameof(fields));
            ArgumentNullException.ThrowIfNull(field.Value);
            if (field.Value.Length > MaximumFieldValueLength)
            {
                throw new ArgumentException(
                    $"A diagnostic field value cannot exceed " +
                    $"{MaximumFieldValueLength} characters.",
                    nameof(fields));
            }

            sorted.Add(field.Key, field.Value);
        }

        TimestampUtc = timestampUtc;
        Level = level;
        EventName = eventName;
        Fields = new ReadOnlyDictionary<string, string>(sorted);
    }

    public DateTimeOffset TimestampUtc { get; }

    public DiagnosticLogLevel Level { get; }

    public string EventName { get; }

    public IReadOnlyDictionary<string, string> Fields { get; }

    private static void EnsureIdentifier(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > 128 ||
            value.Any(character =>
                !char.IsAsciiLetterOrDigit(character) &&
                character is not '.' and not '-' and not '_'))
        {
            throw new ArgumentException(
                "Diagnostic identifiers must use at most 128 ASCII letters, " +
                "digits, dots, hyphens, or underscores.",
                parameterName);
        }
    }
}
