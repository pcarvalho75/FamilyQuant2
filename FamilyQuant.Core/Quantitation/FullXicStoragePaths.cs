using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using FamilyQuant.DeNovo;

namespace FamilyQuant;

public readonly record struct FullXicCacheLocation(string WritePath, string? LegacyReadPath);

/// <summary>Acquisition inputs and mutable computation output use separate folders.</summary>
public sealed class FullXicStoragePaths
{
    private readonly string rawDirectory;
    public string OutputDirectory { get; }
    public string SpectrumComparisonCsvPath => Path.Combine(OutputDirectory, SpectrumComparisonTable.DefaultFileName);

    public FullXicStoragePaths(string rawDirectory, string? cacheDirectory = null)
    {
        this.rawDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rawDirectory));
        string identity = OperatingSystem.IsWindows() ? this.rawDirectory.ToUpperInvariant() : this.rawDirectory;
        string scope = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        string root = string.IsNullOrWhiteSpace(cacheDirectory)
            ? Path.Combine(ApplicationPaths.LocalDataDirectory, FullXicComputer.CacheFolderName)
            : Path.GetFullPath(cacheDirectory);
        OutputDirectory = Path.Combine(root, scope);
        if (IsInRawDirectory(OutputDirectory, this.rawDirectory))
            throw new ArgumentException("Choose a cache/output folder outside the RAW acquisition folder.", nameof(cacheDirectory));
    }

    public FullXicCacheLocation Extraction(string side, string run, string? explicitPath = null) =>
        Location("fullxic_" + SafeFileName(side) + "_" + SafeFileName(run) + ".json", explicitPath);

    public FullXicCacheLocation Detector(string run, string? explicitPath = null) =>
        Location("ms1envelope_" + SafeFileName(run) + ".json", explicitPath);

    private FullXicCacheLocation Location(string name, string? explicitPath)
    {
        if (explicitPath != null && !IsInRawDirectory(explicitPath, rawDirectory)) return new(explicitPath, null);
        return new(Path.Combine(OutputDirectory, name), explicitPath ?? Path.Combine(rawDirectory, FullXicComputer.CacheFolderName, name));
    }

    public static bool IsInRawDirectory(string path, string rawDirectory)
    {
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rawDirectory));
        string output = Path.GetFullPath(path);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        string prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        return string.Equals(output, root, comparison) || output.StartsWith(prefix, comparison);
    }

    private static string SafeFileName(string name)
    {
        var text = new System.Text.StringBuilder(name.Length);
        foreach (char c in name) text.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c);
        return text.Length == 0 ? "run" : text.ToString();
    }
}
