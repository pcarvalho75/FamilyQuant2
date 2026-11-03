using FamilyQuant.Evidence;
using FamilyQuant.Evidence.Positions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static int DomainPositionsLive(string[] args)
    {
        if (args.Length != 4) throw new ArgumentException("Supply input FCF, new output directory and comma-separated accessions.");
        string input = Path.GetFullPath(args[1]), output = Path.GetFullPath(args[2]);
        if (Directory.Exists(output) || output.StartsWith(Path.GetDirectoryName(input)! + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Use a new output directory outside the input folder.");
        string inputHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(input)));
        var file = FamilyClusterFile.Load(input);
        var selected = args[3].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.Ordinal);
        if (selected.Count == 0 || selected.Count > 50) throw new ArgumentException("Select between 1 and 50 validation accessions.");
        file.MyExtendedFastaItems = file.MyExtendedFastaItems.Where(p => !SequenceLabels.IsDecoy(p.SequenceIdentifier)
            && selected.Contains(FamilyNaming.Accession(p.SequenceIdentifier).Split('-')[0])).ToList();
        if (!selected.SetEquals(file.MyExtendedFastaItems.Select(p => FamilyNaming.Accession(p.SequenceIdentifier).Split('-')[0])))
            throw new InvalidDataException("Every selected accession must occur in the input database.");
        var timer = Stopwatch.StartNew();
        var result = EvidenceFetcher.FetchAsync(file, positionAccessions: selected).GetAwaiter().GetResult();
        Check("J4a live: every requested Ana-source protein has fetched coordinates", result.PositionFound == selected.Count);
        Check("J4a live: source sequences agree with the actual database proteins", file.MyExtendedFastaItems.All(p => p.Evidence?.Positions is { } positions
            && ProteinPositionFetcher.MatchesSequence(positions, p.Sequence)));
        Directory.CreateDirectory(output);
        string project = Path.Combine(output, "positions.fqproj");
        ProjectStore.WriteFqproj(project, file, Array.Empty<QuantRow>(), new QuantManifest());
        var reopened = ProjectStore.ReadFqproj(project).fcf;
        Check("J4a live: saved project reopens offline with identical complete evidence", file.ToJsonText() == reopened.ToJsonText());
        Check("J4a live: original input remains byte-identical", inputHash == Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(input)))) ;
        File.WriteAllText(Path.Combine(output, "validation.json"), JsonSerializer.Serialize(new
        {
            Input = input, InputSha256 = inputHash, Project = project, result.PositionAsked, result.PositionFound,
            result.PositionElapsedSeconds, ElapsedSeconds = timer.Elapsed.TotalSeconds,
            Proteins = file.MyExtendedFastaItems.Select(p => new { p.SequenceIdentifier, p.Evidence }),
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }
}
