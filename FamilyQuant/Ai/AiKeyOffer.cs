using System;
using System.Collections.Generic;
using System.IO;

namespace FamilyQuant.Ai;

public sealed class AiKeyOffer(AiKeyStore store)
{
    private readonly HashSet<string> offered = new(StringComparer.OrdinalIgnoreCase);

    public string? EnsureKey(string? projectPath, Func<bool> offerImport)
    {
        ArgumentNullException.ThrowIfNull(offerImport);
        string? current = store.Read();
        if (current != null || string.IsNullOrWhiteSpace(projectPath)) return current;
        string? folder = Path.GetDirectoryName(Path.GetFullPath(projectPath));
        if (folder == null) return null;
        string path = Path.Combine(folder, "FamilyQuant AI key.txt");
        if (!File.Exists(path) || !offered.Add(path)) return null;
        if (!offerImport()) return null;
        store.Import(path);
        return store.Read();
    }
}
