using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace FamilyQuant.Tests;

/// <summary>Test-side delivery I/O boundary. Every owned write goes through an absolute, non-link staging path.</summary>
internal sealed class DeliveryWorkspace
{
    internal string Root { get; }
    internal List<string> Writes { get; } = new();

    internal DeliveryWorkspace(string root, bool allowNonempty)
    {
        Root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        if (string.Equals(Root, Path.GetPathRoot(Root), StringComparison.OrdinalIgnoreCase))
            throw new IOException("Use a dedicated staging folder, not a drive root.");
        CheckLinks(Root);
        if (File.Exists(Root)) throw new IOException("The staging target is a file.");
        if (!allowNonempty && Directory.Exists(Root) && Directory.EnumerateFileSystemEntries(Root).Any())
            throw new IOException("The staging folder is not empty. Use --allow-nonempty only when intentional.");
        Directory.CreateDirectory(Root);
    }

    internal string PathFor(string relative)
    {
        if (Path.IsPathRooted(relative)) throw new IOException("A staging path must be relative.");
        string path = Path.GetFullPath(Path.Combine(Root, relative));
        string tail = Path.GetRelativePath(Root, path);
        if (tail == ".." || tail.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || Path.IsPathRooted(tail)) throw new IOException("A write would leave the staging folder.");
        CheckLinks(path);
        return path;
    }

    internal void WriteText(string relative, string text)
    {
        string path = PathFor(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text, new UTF8Encoding(false));
        Writes.Add(path);
    }

    internal string Copy(string source, string relative, bool overwrite = false)
    {
        string target = PathFor(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        if (string.Equals(Path.GetFullPath(source), target, StringComparison.OrdinalIgnoreCase)) return target;
        File.Copy(source, target, overwrite);
        Writes.Add(target);
        if (Sha256(source) != Sha256(target)) throw new IOException("A staged copy differs from its input.");
        return target;
    }

    internal void Move(string sourceRelative, string targetRelative, bool overwrite = false)
    {
        string source = PathFor(sourceRelative), target = PathFor(targetRelative);
        File.Move(source, target, overwrite); Writes.Add(target);
    }

    internal void AssertWritesInside()
    {
        foreach (string write in Writes)
            if (PathFor(Path.GetRelativePath(Root, write)) != write) throw new IOException("An owned write escaped staging.");
        var pending = new Stack<string>(); pending.Push(Root);
        while (pending.Count > 0)
            foreach (string entry in Directory.EnumerateFileSystemEntries(pending.Pop()))
            {
                CheckLinks(entry);
                if (Directory.Exists(entry)) pending.Push(entry);
            }
    }

    internal static string Sha256(string path)
    {
        using var input = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(input));
    }

    internal static int ValidateXmlPackage(string path)
    {
        using var package = ZipFile.OpenRead(path);
        if (package.GetEntry("[Content_Types].xml") == null || package.GetEntry("_rels/.rels") == null)
            throw new InvalidDataException("An Office package is missing required root parts.");
        int xml = 0;
        foreach (var entry in package.Entries)
        {
            using var input = entry.Open();
            if (entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) || entry.FullName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase))
            {
                using var reader = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
                _ = XDocument.Load(reader); xml++;
            }
            else input.CopyTo(Stream.Null);
        }
        return xml;
    }

    private static void CheckLinks(string path)
    {
        for (string? current = path; current != null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Staging paths must not cross a junction or symbolic link.");
    }
}
