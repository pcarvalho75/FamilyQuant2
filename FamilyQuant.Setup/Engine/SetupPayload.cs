using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace FamilyQuant.Setup.Engine;

public sealed class SetupPayload(PayloadManifest manifest, Func<Stream> openZip)
{
    public PayloadManifest Manifest { get; } = manifest;
    public void Extract(string stage, IProgress<SetupProgress>? progress, CancellationToken token)
    {
        ValidateManifest(Manifest.Files);
        using var input = openZip();
        string zipHash = Convert.ToHexString(SHA256.HashData(input));
        if (!zipHash.Equals(Manifest.ZipSha256, StringComparison.OrdinalIgnoreCase)) throw new IOException("The embedded package checksum differs.");
        input.Position = 0;
        using var archive = new ZipArchive(input, ZipArchiveMode.Read);
        var expected = Manifest.Files.ToDictionary(f => f.Path, StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int completed = 0;
        foreach (var entry in archive.Entries)
        {
            token.ThrowIfCancellationRequested();
            if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000 || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
                throw new IOException("The package contains a file link.");
            if (entry.FullName.EndsWith('/')) { _ = SetupPaths.FileIn(stage, entry.FullName.TrimEnd('/')); continue; }
            string path = SetupPaths.FileIn(stage, entry.FullName);
            if (!seen.Add(entry.FullName) || !expected.TryGetValue(entry.FullName, out var file) || entry.Length != file.Length)
                throw new IOException("The package contents differ from its file list.");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using (var source = entry.Open())
            using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                byte[] buffer = new byte[81920]; int read; long total = 0;
                while ((read = source.Read(buffer)) > 0)
                {
                    token.ThrowIfCancellationRequested(); total = checked(total + read);
                    if (total > file.Length) throw new IOException("The package expands beyond its recorded size.");
                    output.Write(buffer, 0, read);
                }
                if (total != file.Length) throw new IOException("A package file is incomplete.");
            }
            if (!SetupProduct.Hash(path).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase)) throw new IOException("A package file checksum differs.");
            progress?.Report(new("Preparing " + file.Path, ++completed, expected.Count));
        }
        if (seen.Count != expected.Count) throw new IOException("The package is missing a recorded file.");
    }
    public static void ValidateManifest(IReadOnlyList<PayloadFile> files)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long total = 0;
        foreach (var file in files)
        {
            _ = SetupPaths.FileIn(Path.Combine(Path.GetTempPath(), "package-validation"), file.Path);
            if (!names.Add(file.Path) || file.Path.Equals(SetupProduct.Manifest, StringComparison.OrdinalIgnoreCase)
                || file.Path.Equals(SetupProduct.Uninstaller, StringComparison.OrdinalIgnoreCase)
                || file.Length < 0 || file.Sha256.Length != 64 || !file.Sha256.All(Uri.IsHexDigit))
                throw new IOException("The package file list is invalid.");
            total = checked(total + file.Length);
        }
        if (total > 8L * 1024 * 1024 * 1024 || !names.Contains(SetupProduct.AppExe)) throw new IOException("The package is not a supported application payload.");
    }
    public static SetupPayload Embedded()
    {
        var assembly = typeof(SetupPayload).Assembly;
        using var metadata = assembly.GetManifestResourceStream("FamilyQuant.Setup.Payload.json")
            ?? throw new IOException("This setup build has no embedded application package.");
        var manifest = JsonSerializer.Deserialize<PayloadManifest>(metadata, SetupProduct.Json)
            ?? throw new IOException("The embedded package metadata is invalid.");
        if (manifest.Schema != 1 || manifest.ProductId != SetupProduct.Id || !Version.TryParse(manifest.Version, out _))
            throw new IOException("The embedded package identity is invalid.");
        return new(manifest, () => assembly.GetManifestResourceStream("FamilyQuant.Setup.Payload.zip")
            ?? throw new IOException("The embedded package is missing."));
    }
}
