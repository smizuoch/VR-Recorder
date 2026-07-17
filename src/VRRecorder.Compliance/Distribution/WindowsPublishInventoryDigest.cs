using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using VRRecorder.Compliance.Staging;

namespace VRRecorder.Compliance.Distribution;

internal static class WindowsPublishInventoryDigest
{
    public static string Compute(IEnumerable<StagedPayloadFile> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        var canonicalFiles = files
            .OrderBy(file => file.RelativePath, StringComparer.Ordinal)
            .ToArray();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendField(hash, "VRRECORDER_APPLICATION_PAYLOAD_INVENTORY_V1");
        Span<byte> integer = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64BigEndian(
            integer,
            canonicalFiles.LongLength);
        hash.AppendData(integer);
        foreach (var file in canonicalFiles)
        {
            AppendField(hash, file.RelativePath);
            BinaryPrimitives.WriteInt64BigEndian(integer, file.Length);
            hash.AppendData(integer);
            AppendField(hash, file.Sha256);
            AppendField(hash, file.Kind.ToString());
        }

        return Convert.ToHexString(hash.GetHashAndReset())
            .ToLowerInvariant();
    }

    private static void AppendField(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }
}
