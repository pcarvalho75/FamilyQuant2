using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace FamilyQuant.Evidence;

public sealed partial class PanelEvidenceMatching
{
    /// <summary>SHA256 of the previous default UTF-8 EvidenceSource JSON, streamed without a whole-part string.</summary>
    public static string FingerprintSource(EvidenceProvenance? source)
    {
        using var hash = SHA256.Create();
        using var stream = new CryptoStream(Stream.Null, hash, CryptoStreamMode.Write);
        JsonSerializer.Serialize(stream, source);
        stream.FlushFinalBlock();
        return System.Convert.ToHexString(hash.Hash!);
    }
}
