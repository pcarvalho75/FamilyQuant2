namespace FamilyQuant.Setup.Engine;

public static class SetupPaths
{
    public static string FullDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || path.StartsWith("\\\\", StringComparison.Ordinal))
            throw new IOException("Choose a full local folder path.");
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (Same(full, Path.GetPathRoot(full)!)) throw new IOException("Choose a program folder, not a drive root.");
        foreach (string part in full[Path.GetPathRoot(full)!.Length..].Split(Path.DirectorySeparatorChar))
            if (part.EndsWith('.') || part.EndsWith(' ')) throw new IOException("Folder names must not end with a dot or space.");
        CheckAncestors(full);
        return full;
    }
    public static bool Same(string left, string right) => string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)), StringComparison.OrdinalIgnoreCase);
    public static bool Within(string root, string path) => Same(root, path) || Path.GetFullPath(path).StartsWith(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    public static void CheckAncestors(string path)
    {
        for (var current = new DirectoryInfo(Path.GetFullPath(path)); current != null; current = current.Parent)
            if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Setup does not follow folder links.");
    }
    public static string FileIn(string root, string relative)
    {
        if (string.IsNullOrEmpty(relative) || relative.Contains('\\') || Path.IsPathRooted(relative))
            throw new IOException("The package has an invalid relative path.");
        foreach (string part in relative.Split('/'))
        {
            if (part.Length == 0 || part is "." or ".." || part.EndsWith('.') || part.EndsWith(' ')
                || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new IOException("The package has an unsafe file name.");
            string name = part.Split('.')[0].ToUpperInvariant();
            if (name is "CON" or "PRN" or "AUX" or "NUL" || (name.Length == 4 && name[..3] is "COM" or "LPT" && name[3] is >= '1' and <= '9'))
                throw new IOException("The package has a reserved file name.");
        }
        string full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!Within(root, full) || Same(root, full)) throw new IOException("The package path leaves its program folder.");
        return full;
    }
    public static IEnumerable<string> Files(string root)
    {
        var pending = new Stack<string>(); pending.Push(root);
        while (pending.Count > 0)
            foreach (string entry in Directory.EnumerateFileSystemEntries(pending.Pop()))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Setup does not follow file or folder links.");
                if ((attributes & FileAttributes.Directory) != 0) pending.Push(entry); else yield return entry;
            }
    }
    public static void DeleteWork(string parent, string path, string prefix)
    {
        string full = FullDirectory(path);
        if (!Same(Path.GetDirectoryName(full)!, parent) || !Path.GetFileName(full).StartsWith(prefix, StringComparison.Ordinal))
            throw new IOException("The temporary setup folder is outside its expected location.");
        if (!Directory.Exists(full)) return;
        _ = Files(full).Count();
        Directory.Delete(full, true);
    }
    public static void RemoveEmptyDirectories(string root, IEnumerable<string> owned)
    {
        if (!Directory.Exists(root)) return;
        foreach (string relative in owned.OrderByDescending(p => p.Length))
        {
            string directory = FileIn(root, relative);
            if (Directory.Exists(directory) && (File.GetAttributes(directory) & FileAttributes.ReparsePoint) == 0
                && !Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
        }
        if (!Directory.EnumerateFileSystemEntries(root).Any()) Directory.Delete(root);
    }
}
