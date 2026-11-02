using System;
using System.Security.Cryptography;
using System.Text;

namespace FamilyQuant.Evidence;

internal sealed class LengthPrefixedSha256 : IDisposable
{
    private readonly IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

    // Callers retain their existing null policy and field/row traversal order.
    internal void Append(string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        hash.AppendData(BitConverter.GetBytes(bytes.Length));
        hash.AppendData(bytes);
    }

    internal string Finish() => Convert.ToHexString(hash.GetHashAndReset());
    public void Dispose() => hash.Dispose();
}
