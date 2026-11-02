using FamilyQuant.Operations;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace FamilyQuant
{
    internal static class ProjectArchiveEntries
    {
        internal static void WriteTextEntry(ZipArchive zip, string name, string content, CompressionLevel level, OperationContext? operation = null)
        {
            operation?.ThrowIfCancellationRequested();
            ZipArchiveEntry entry = zip.CreateEntry(name, level);
            using Stream stream = entry.Open();
            using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            writer.Write(content);
        }

        internal static string? ReadTextEntry(ZipArchive zip, string name, OperationContext? operation = null)
        {
            operation?.ThrowIfCancellationRequested();
            ZipArchiveEntry? entry = zip.GetEntry(name);
            if (entry == null) return null;

            using Stream stream = entry.Open();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd();
        }

    }
}
