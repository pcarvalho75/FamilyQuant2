using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace FamilyQuant.Ai;

public sealed class AiKeyStore
{
    private const int MaximumBytes = 8192;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private readonly string path;

    public AiKeyStore(string? path = null) => this.path = Path.GetFullPath(path
        ?? Path.Combine(ApplicationPaths.LocalDataDirectory, "ai-assistant-key.dat"));

    public string? Read()
    {
        if (!File.Exists(path)) return null;
        byte[] encrypted = File.ReadAllBytes(path);
        byte[] plain = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
        try { return Normalize(Utf8.GetString(plain)); }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    public void Save(string key)
    {
        string normalized = Normalize(key);
        byte[] plain = Utf8.GetBytes(normalized);
        byte[] encrypted;
        try { encrypted = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser); }
        finally { CryptographicOperations.ZeroMemory(plain); }
        string directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        string staging = Path.Combine(directory, ".ai-key-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllBytes(staging, encrypted);
            File.Move(staging, path, true);
        }
        finally { if (File.Exists(staging)) File.Delete(staging); }
    }

    public void Import(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > MaximumBytes) throw new ArgumentException("The key file is too large.", nameof(path));
        using var reader = new StreamReader(stream, Utf8, true);
        string key = reader.ReadToEnd();
        if (Utf8.GetByteCount(key) > MaximumBytes) throw new ArgumentException("The key file is too large.", nameof(path));
        Save(key);
    }

    public void Remove() { if (File.Exists(path)) File.Delete(path); }

    private static string Normalize(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        string value = key.Trim();
        if (value.Length == 0 || value.Any(char.IsWhiteSpace) || value.Any(char.IsControl) || Utf8.GetByteCount(value) > MaximumBytes)
            throw new ArgumentException("Enter one nonempty key on a single line.", nameof(key));
        return value;
    }
}
