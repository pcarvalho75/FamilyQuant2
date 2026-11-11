using FamilyQuant.DeNovo;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace FamilyQuant;

/// <summary>Complete immutable chart inputs, including references/tracks not printed in the bounded overview.</summary>
public static class FigureDetailsCsv
{
    public static string Build(IEnumerable<AnalyticsFigure> figures)
    {
        using var text = new StringWriter(CultureInfo.InvariantCulture);
        Write(text, figures); return text.ToString();
    }

    public static void Write(TextWriter output, IEnumerable<AnalyticsFigure> figures)
    {
        output.WriteLine("figure,record_type,basis,family_key,family_label,protein,item_key,item_label,metric,value,unit,source,match_kind,start,end,note");
        foreach (var figure in figures)
        {
            void Row(string type, string basis, string family, string label, string protein = "", string key = "", string item = "",
                string metric = "", double? value = null, string unit = "", string source = "", string kind = "",
                int? start = null, int? end = null, string note = "") => output.WriteLine(string.Join(",", new[]
                { figure.FileName, type, basis, family, label, protein, key, item, metric,
                    value?.ToString("R", CultureInfo.InvariantCulture) ?? "", unit, source, kind,
                    start?.ToString(CultureInfo.InvariantCulture) ?? "", end?.ToString(CultureInfo.InvariantCulture) ?? "", note }
                    .Select(v => SpreadsheetSafeCsv.Encode(v, alwaysQuote: true))));
            FamilyCompositionData? data = figure.Chart switch
            {
                FamilyCompositionChart c => c.Data, FamilySubclassChart c => c.Data,
                FamilyAbundanceDiversityChart c => c.Data, FamilyRingChart c => c.Data, _ => null
            };
            if (data != null)
            {
                foreach (var family in data.Families)
                {
                    Row("family", data.Basis, family.Key, family.Label, metric: "share", value: family.MeanPercent, unit: "%");
                    if (family.SdPercent is double sd) Row("family_statistic", data.Basis, family.Key, family.Label, metric: "sample SD", value: sd, unit: "percentage points");
                    if (data.IsMeanOfRuns) Row("family_statistic", data.Basis, family.Key, family.Label, metric: "available runs", value: family.RunCount, unit: "runs");
                    if (family.GreedyProteinCount is int count) Row("family_statistic", data.Basis, family.Key, family.Label, metric: "proteins needed to explain the peptides (greedy estimate)", value: count, unit: "proteins");
                    if (family.UniqueProteinCount is int unique) Row("family_statistic", data.Basis, family.Key, family.Label, metric: "proteins with a peptide of their own", value: unique, unit: "proteins");
                    if (family.UnexplainedPeptideCount is int unexplained) Row("family_statistic", data.Basis, family.Key, family.Label, metric: "unexplained peptides", value: unexplained, unit: "peptides");
                    foreach (var part in family.Subclasses)
                        Row("subclass", data.Basis, family.Key, family.Label, key: part.Key, item: part.Label, metric: "share", value: part.Percent, unit: "%");
                }
                foreach (string missing in data.UnavailableFamilies)
                    Row("unavailable_family", data.Basis, "", missing, metric: "share", note: "Unavailable, not zero");
            }
            if (figure.Chart is FamilyRingChart ring)
                foreach (var group in ring.Families)
                {
                    Row("ring_group", ring.Data.Basis, "", group.Label, metric: "pooled share", value: group.Percent, unit: "%",
                        note: group.IsUnassigned ? "Outside families and Others" : group.IsOthers ? "Canonical Others membership" : "Listed family");
                    foreach (var member in group.Members) Row("ring_membership", ring.Data.Basis, member.Key, member.Label, item: group.Label);
                }
            if (figure.Chart is ProteinCoverageMapChart coverage)
            {
                var map = coverage.Data; string family = map.FamilyUserId.ToString(CultureInfo.InvariantCulture);
                string basis = map.RankingMethod;
                Row("protein", basis, family, "", map.ProteinId, item: map.Label, metric: "length", value: map.SequenceLength, unit: "residues",
                    note: map.SequenceSha256 + "; " + map.PositionState + "; " + map.PeptideState + "; " + map.Provenance);
                Row("protein", basis, family, "", map.ProteinId, metric: "allocated display signal", value: map.RankingSignal, unit: "area units");
                foreach (var annotation in map.Annotations)
                {
                    if (annotation.Segments.Count == 0) Row("annotation", basis, family, "", map.ProteinId, item: annotation.Label, kind: annotation.Kind.ToString(), note: annotation.Note);
                    foreach (var block in annotation.Segments) Row("annotation", basis, family, "", map.ProteinId, item: annotation.Label,
                        kind: annotation.Kind.ToString(), start: block.Start, end: block.End, note: annotation.Note);
                }
                foreach (var peptide in map.Peptides.Concat(map.UnplacedPeptides))
                {
                    if (peptide.Segments.Count == 0 || map.UnplacedPeptides.Contains(peptide))
                        Row("unplaced_peptide", basis, family, "", map.ProteinId, peptide.Key, peptide.Label, source: peptide.Source.ToString(), kind: peptide.Kind.ToString(), note: map.PeptideState);
                    else foreach (var block in peptide.Segments)
                        Row("peptide", basis, family, "", map.ProteinId, peptide.Key, peptide.Label, source: peptide.Source.ToString(),
                            kind: peptide.Kind.ToString(), start: block.Start, end: block.End);
                }
            }
            if (figure.Chart is FamilySignalOriginChart sources)
                foreach (var row in sources.Rows)
                {
                    string keys = string.Join(";", row.CuratedUserIds.Select(id => id.ToString(CultureInfo.InvariantCulture)));
                    Row("signal_source", "All runs pooled", keys, row.Family, metric: "identified signal", value: row.IdentifiedEnvelope, unit: "area units");
                    Row("signal_source", "All runs pooled", keys, row.Family, metric: "de novo in database", value: row.DatabaseDeNovoEnvelope, unit: "area units");
                    Row("signal_source", "All runs pooled", keys, row.Family, metric: "de novo only in UniProt families", value: row.WorldDeNovoEnvelope, unit: "area units", note: row.WorldEvidence);
                }
        }
    }
}
