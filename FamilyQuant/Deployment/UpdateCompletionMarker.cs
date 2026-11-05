using System;
using System.IO;

namespace FamilyQuant.Deployment;

// Shared source keeps the standalone Setup and app on the same small file contract.
internal static class UpdateCompletionMarker
{
    internal const string FileName = ".familyquant-update-complete";

    internal static void Write(string directory, string version)
    {
        if (!Version.TryParse(version, out _)) throw new IOException("The installed version is invalid.");
        string path = Path.Combine(directory, FileName);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, version);
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    internal static string? Consume(string directory, string currentVersion)
    {
        string path = Path.Combine(directory, FileName);
        try
        {
            if (!File.Exists(path)) return null;
            string stored = File.ReadAllText(path).Trim();
            File.Delete(path);
            if (!Version.TryParse(stored, out var installed) || !Version.TryParse(currentVersion, out var current)
                || installed != current) return null;
            return "FamilyQuant was updated to " + (installed.Build >= 0 ? installed.ToString(3) : installed.ToString()) + ".";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return null; }
    }
}
