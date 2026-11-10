using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace FamilyQuant.Report;

public static class HtmlReportCoverageBuilder
{
    public static IReadOnlyList<HtmlReportCoverage> Build(ReportDocument document, FamilyClusterFile project,
        QuantManifest sourceStudies, IReadOnlyCollection<int> sourceUserIds,
        Func<string, HtmlReportReference, string, double?, string> addField,
        ReportSelection? selection = null, string? ownerSectionId = null)
    {
        ArgumentNullException.ThrowIfNull(document); ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(sourceStudies); ArgumentNullException.ThrowIfNull(sourceUserIds); ArgumentNullException.ThrowIfNull(addField);
        var excluded = new HashSet<string>(selection?.ExcludedIds ?? Array.Empty<string>(), StringComparer.Ordinal);
        if (ownerSectionId != null && excluded.Contains(ownerSectionId)) return Array.Empty<HtmlReportCoverage>();
        var figures = document.Sections.Where(s => !excluded.Contains(s.Id)).SelectMany(s => s.Blocks.OfType<ReportFigure>())
            .Where(f => !excluded.Contains(f.Id)).ToArray();
        var localization = sourceStudies.FamilyPeptideLocalization;
        if (localization != null)
        {
            FamilyStudyMembership.RequireCurrent(project, sourceStudies);
            foreach (var family in localization.Families)
                if (family.Proteins.Any(p => p.Rank < 1 || !double.IsFinite(p.CreditedSignal) || p.CreditedSignal < 0)
                    || family.Proteins.Select(p => p.Rank).Distinct().Count() != family.Proteins.Count
                    || family.Proteins.Select(p => p.ProteinIdentifier).Distinct(StringComparer.Ordinal).Count() != family.Proteins.Count)
                    throw new InvalidDataException("Stored coverage ranking is invalid.");
            var known = localization.Families.SelectMany(f => f.Proteins.Select(p => (f.FamilyUserId, p.ProteinIdentifier))).ToHashSet();
            if (localization.Segments.Any(s => !known.Contains((s.FamilyUserId, s.ProteinIdentifier))))
                throw new InvalidDataException("Stored coverage segment has no ranked protein.");
        }
        var result = new List<HtmlReportCoverage>();
        foreach (var figure in figures)
        {
            int? familyId = sourceUserIds.Distinct().Where(id => figure.Id.StartsWith("figure:protein_coverage_" + I(id) + "_", StringComparison.Ordinal))
                .Select(id => (int?)id).SingleOrDefault();
            if (!familyId.HasValue) continue;
            string title = addField(figure.Title, new() { Kind = "figure-title", Figure = figure.Id }, "", null);
            string caption = addField(figure.Caption, new() { Kind = "figure-caption", Figure = figure.Id }, "", null);
            var fallback = new HtmlReportCoverage { Title = title, Caption = caption, SourceFigureId = figure.Id,
                Png = "data:image/png;base64," + Convert.ToBase64String(figure.PngBytes) };
            int fi = localization?.Families.FindIndex(f => f.FamilyUserId == familyId.Value) ?? -1;
            if (fi < 0) { result.Add(fallback); continue; }
            var ranking = localization!.Families[fi];
            int pi = ranking.Proteins.FindIndex(p => FigureId(familyId.Value, p.ProteinIdentifier) == figure.Id);
            if (pi < 0) { result.Add(fallback); continue; }
            var ranked = ranking.Proteins[pi];
            var candidates = project.MyExtendedFastaItems.Select((p, i) => (Item: p, Index: i))
                .Where(p => p.Item.UserID == familyId.Value && p.Item.SequenceIdentifier == ranked.ProteinIdentifier).ToArray();
            if (candidates.Length != 1 || string.IsNullOrEmpty(candidates[0].Item.Sequence)) throw new InvalidDataException("Coverage protein is absent or ambiguous.");
            var (protein, proteinIndex) = candidates[0];
            string sha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(protein.Sequence.ToUpperInvariant())));
            if (ranked.ProteinLength != protein.Sequence.Length || !sha.Equals(ranked.ProteinSequenceSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Stored localization sequence differs from the coverage protein.");
            string rankPath = "/manifest/FamilyPeptideLocalization/Families/" + I(fi) + "/Proteins/" + I(pi);
            var peptideSpans = new List<BoundSpan>(); var domainSpans = new List<BoundSpan>();
            foreach (var (segment, index) in localization.Segments.Select((s, i) => (s, i)).Where(p => p.s.FamilyUserId == familyId.Value && p.s.ProteinIdentifier == ranked.ProteinIdentifier))
            {
                if (string.IsNullOrWhiteSpace(segment.NormalizedSequence) || !double.IsFinite(segment.Signal) || segment.Signal < 0
                    || !Enum.IsDefined(segment.MatchKind) || segment.Side is not ("identified" or "denovo"))
                    throw new InvalidDataException("Stored peptide coverage metadata is invalid.");
                int priorEnd = 0;
                for (int j = 0; j < segment.Intervals.Count; j++)
                {
                    var span = segment.Intervals[j]; Validate(span.Start, span.End, ranked.ProteinLength);
                    if (span.Start <= priorEnd) throw new InvalidDataException("Stored peptide intervals overlap or are unordered.");
                    priorEnd = span.End;
                    string path = "/manifest/FamilyPeptideLocalization/Segments/" + I(index);
                    peptideSpans.Add(new(span.Start, span.End, segment.NormalizedSequence, path + "/NormalizedSequence", null,
                        path + "/Intervals/" + I(j), segment.MatchKind.ToString()));
                }
            }
            var positions = protein.Evidence?.Positions;
            string positionPath = "/project/MyExtendedFastaItems/" + I(proteinIndex) + "/Evidence/Positions";
            if (positions != null && positions.SequenceLength == ranked.ProteinLength && sha.Equals(positions.SequenceSha256, StringComparison.OrdinalIgnoreCase))
            {
                for (int d = 0; d < positions.Pfam.Count; d++)
                {
                    var domain = positions.Pfam[d]; string path = positionPath + "/Pfam/" + I(d);
                    string label = (domain.Name.Length > 0 ? domain.Name + " " : "") + domain.Accession;
                    if (domain.Fragments.Count == 0)
                    { Validate(domain.Start, domain.End, ranked.ProteinLength); domainSpans.Add(new(domain.Start, domain.End, label, path, "domain-label", path, "domain")); }
                    else for (int j = 0; j < domain.Fragments.Count; j++)
                    {
                        var span = domain.Fragments[j]; Validate(span.Start, span.End, ranked.ProteinLength);
                        domainSpans.Add(new(span.Start, span.End, label, path, "domain-label", path + "/Fragments/" + I(j), "domain"));
                    }
                }
                for (int f = 0; f < positions.Features.Count; f++)
                {
                    var feature = positions.Features[f];
                    if (!feature.Start.HasValue || !feature.End.HasValue || !string.Equals(feature.StartModifier, "EXACT", StringComparison.OrdinalIgnoreCase)
                        || !string.Equals(feature.EndModifier, "EXACT", StringComparison.OrdinalIgnoreCase)) continue;
                    Validate(feature.Start.Value, feature.End.Value, ranked.ProteinLength);
                    string path = positionPath + "/Features/" + I(f);
                    domainSpans.Add(new(feature.Start.Value, feature.End.Value, feature.Type + (feature.Description.Length > 0 ? ": " + feature.Description : ""), path, "feature-label", path, "feature"));
                }
            }
            if (peptideSpans.Count == 0 && domainSpans.Count == 0) { result.Add(fallback); continue; }
            result.Add(fallback with
            {
                Png = null, Length = ranked.ProteinLength,
                Protein = addField(ranked.ProteinIdentifier, Stored(rankPath + "/ProteinIdentifier"), "", null),
                LengthLabel = addField(I(ranked.ProteinLength) + " residues", Stored(rankPath + "/ProteinLength", "0' residues'"), "", ranked.ProteinLength),
                Peptides = peptideSpans.Select(Bind).ToArray(), Domains = domainSpans.Select(Bind).ToArray(),
            });
        }
        return result;

        HtmlCoverageSpan Bind(BoundSpan span) => new(span.Start, span.End,
            addField(span.Label, Stored(span.LabelPath, span.LabelFormat), "", null), span.Kind,
            addField(I(span.Start) + "-" + I(span.End), Stored(span.RangePath, "range"), "", null));
    }

    private sealed record BoundSpan(int Start, int End, string Label, string LabelPath, string? LabelFormat, string RangePath, string Kind);
    private static string I(int value) => value.ToString(CultureInfo.InvariantCulture);
    private static HtmlReportReference Stored(string pointer, string? format = null) => new() { Kind = "stored-value", Property = pointer, Format = format };
    public static string FigureId(int family, string protein) => "figure:protein_coverage_" + I(family) + "_"
        + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(protein))).ToLowerInvariant() + ".png";
    private static void Validate(int start, int end, int length)
    { if (start < 1 || end < start || end > length) throw new InvalidDataException("Stored coverage coordinates are outside the canonical sequence."); }
}
