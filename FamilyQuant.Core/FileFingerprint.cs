using System;
using System.IO;
using System.Security.Cryptography;

namespace FamilyQuant
{
    /// <summary>Content fingerprints for scientific inputs. File names and timestamps are not identities.</summary>
    public static class FileFingerprint
    {
        public static string Sha256(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A file path is required.", nameof(path));
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 1024 * 1024, FileOptions.SequentialScan);
            using SHA256 sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
        }
    }
}
