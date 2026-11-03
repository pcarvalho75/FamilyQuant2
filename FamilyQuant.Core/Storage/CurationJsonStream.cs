using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace FamilyQuant;

public partial class FamilyClusterFile
{
    // Same metadata/options/order as ToJsonText, with UTF-8 written incrementally to the destination.
    internal void WriteJsonUtf8(Stream destination) => JsonSerializer.Serialize(destination, this, CurationJsonOptions);

    internal string JsonContentSha256()
    {
        using var hash = SHA256.Create();
        using var stream = new CryptoStream(Stream.Null, hash, CryptoStreamMode.Write);
        WriteJsonUtf8(stream); stream.FlushFinalBlock();
        return Convert.ToHexString(hash.Hash!);
    }
}
