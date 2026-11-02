using System;
using System.IO;
using System.Security.Cryptography;

namespace FamilyQuant.Tests;

// BCL-only so ReferenceGoldenHost can link the same byte/case contract.
internal static class TestFileHash
{
    internal static string Upper(string path)
    {
        using FileStream input = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(input));
    }
}
