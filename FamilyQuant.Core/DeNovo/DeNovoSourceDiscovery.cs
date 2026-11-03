using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FamilyQuant.DeNovo;

/// <summary>A filename-based proposal only; import still validates columns and values. Multiple
/// PEAKS export types are left to the person rather than silently choosing a subset.</summary>
public sealed record DeNovoSourceProposal(IReadOnlyList<DeNovoSource> AvailableSources,
    IReadOnlyList<DeNovoSource> ProposedSources, string Message);

public static class DeNovoSourceDiscovery
{
    public static DeNovoSourceProposal Inspect(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return new(Array.Empty<DeNovoSource>(), Array.Empty<DeNovoSource>(), string.Empty);
        try
        {
            DeNovoSource[] available = DeNovoSource.Presets.Where(source =>
                Directory.EnumerateFiles(directory, source.FileGlob, SearchOption.AllDirectories).Any()).ToArray();
            if (available.Contains(DeNovoSource.DenovoOnly) && available.Contains(DeNovoSource.DeNovoAll))
                return new(available, Array.Empty<DeNovoSource>(), "Both PEAKS export types were found. Choose the source to read.");
            if (available.Length == 0)
                return new(available, available, "No supported de novo exports were found in this folder.");
            return new(available, available, "Found " + string.Join(" + ", available.Select(s => s.DisplayName)) + ".");
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException)
        {
            return new(Array.Empty<DeNovoSource>(), Array.Empty<DeNovoSource>(), "The export folder could not be inspected; the current source was kept.");
        }
    }
}
