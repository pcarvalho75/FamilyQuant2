using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FamilyQuant.Report;

public static partial class HtmlReportPayloadBuilder
{
    private sealed partial class Builder
    {
        private Frame? Workbook(params string[] ids)
        {
            foreach (string id in ids) if (!excluded.Contains(id) && workbookFrames.TryGetValue(id, out var frame)) return frame;
            return null;
        }

        private IEnumerable<int> FamilyRows(Frame frame, string id, bool effectiveKey = false)
        {
            string key = id;
            if (effectiveKey && !context.PanelKeys.TryGetValue(id, out key!)) yield break;
            int column = Column(frame, effectiveKey ? "effective_panel_key" : "family_user_id");
            if (column < 0) yield break;
            for (int i = 0; i < frame.Table.Rows.Count; i++) if (frame.Table.Rows[i][column] == key) yield return i;
        }

        private string? NativeCell(Frame frame, int row, string column, string unit = "", double scale = 1,
            string? fractionColumn = null, double? stored = null, bool shortLabel = true)
        {
            int col = Column(frame, column);
            if (col < 0 || frame.Table.Rows[row][col].Length == 0) return null;
            string text = frame.Table.Rows[row][col];
            double? value = null;
            var reference = frame.Reference(row, col);
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double native) && double.IsFinite(native))
            {
                value = stored ?? native * scale;
                if (scale != 1)
                {
                    text = (native * scale).ToString("G6", CultureInfo.InvariantCulture);
                    reference = reference with { Scale = scale, Format = "G6" };
                }
            }
            string id = Add(text, reference, unit, value);
            if (shortLabel && value.HasValue && unit is "%" or "pp" or "fraction")
            {
                HtmlReportReference precision = reference;
                double shortValue = value.Value;
                int fraction = fractionColumn == null ? -1 : Column(frame, fractionColumn);
                if (fraction >= 0 && double.TryParse(frame.Table.Rows[row][fraction], NumberStyles.Float, CultureInfo.InvariantCulture, out double raw))
                { shortValue = raw * 100; precision = frame.Reference(row, fraction) with { Scale = 100 }; }
                AddShort(id, shortValue, precision);
            }
            return id;
        }

        private HtmlReportFamily FamilyDetails(HtmlReportFamily family, FamilyRunShareSummary? summary)
        {
            if (family.IsOthers) return OtherFamilyShares(family);
            family = family with
            {
                IdentifiedOnly = FamilyShareStatistics(family.Id, "IdentifiedOnly"),
                IdentifiedOnlyParts = FamilyShareMeanParts(family.Id, "IdentifiedOnly"),
                IdentifiedAndDeNovo = FamilyShareStatistics(family.Id, "IdentifiedAndDeNovo"),
                IdentifiedAndDeNovoParts = FamilyShareMeanParts(family.Id, "IdentifiedAndDeNovo"),
                DetectorEnvelopes = FamilyShareStatistics(family.Id, "DetectorEnvelopes"),
                DetectorEnvelopesParts = FamilyShareMeanParts(family.Id, "DetectorEnvelopes"),
                Tic = FamilyShareStatistics(family.Id, "Tic"),
                TicParts = FamilyShareMeanParts(family.Id, "Tic"),
            };
            // A selected Results headline is independent of the family-detail checkbox. Do not
            // allocate any detail fields for an excluded family owner, even from global worksheets.
            if (context.FamilySections.TryGetValue(family.Id, out var owner) && !sections.ContainsKey(owner)) return family;
            var runs = new List<HtmlFamilyRun>();
            if (!excluded.Contains("family-run-shares"))
            {
                var frame = context.UsesPanel ? Workbook("export:panel_family_run_shares.csv") : null;
                frame ??= context.IsIdentity ? Workbook("family-run-shares", "export:family_run_shares.csv") : null;
                if (frame != null)
                    foreach (int row in FamilyRows(frame, family.Id))
                    {
                        int run = Column(frame, "run"); if (run < 0) continue;
                        var stored = summary?.Runs.SingleOrDefault(r => r.Run == frame.Table.Rows[row][run]);
                        string name = RunField(frame, row, run);
                        runs.Add(new(name,
                            NativeCell(frame, row, "assigned_signal_pct", "%", fractionColumn: "assigned_signal_fraction", stored: stored?.ShareOfAssignedSignal * 100),
                            NativeCell(frame, row, "measured_peptide_signal_pct", "%", fractionColumn: "measured_peptide_signal_fraction", stored: stored?.ShareOfMeasuredSignal * 100))
                        {
                            IdentifiedOnly = FamilyShareField(family.Id, "IdentifiedOnly", frame.Table.Rows[row][run]),
                            IdentifiedAndDeNovo = FamilyShareField(family.Id, "IdentifiedAndDeNovo", frame.Table.Rows[row][run]),
                            DetectorEnvelopes = FamilyShareField(family.Id, "DetectorEnvelopes", frame.Table.Rows[row][run]),
                            Tic = FamilyShareField(family.Id, "Tic", frame.Table.Rows[row][run]),
                        });
                    }
            }
            var parts = new List<HtmlFamilyPart>();
            if (context.UsesPanel && !context.IsIdentity)
            {
                var frame = Workbook("export:effective_panel_parts.csv");
                if (frame != null)
                    foreach (int row in FamilyRows(frame, family.Id, true))
                    {
                        int name = Column(frame, "part"), kind = Column(frame, "part_kind");
                        if (name < 0 || kind < 0) continue;
                        string partKind = frame.Table.Rows[row][kind];
                        if (excluded.Contains(partKind == "Subclass" ? "subclass-parts" : partKind == "Domain" ? "domain-parts" : "product-parts")) continue;
                        parts.Add(new(partKind, Cell(frame, row, name), NativeCell(frame, row, "within_family_fraction", "%", 100), Unresolved(frame, row)));
                    }
            }
            else
                foreach (var source in new[] { (Id: "subclass-parts", Kind: "Subclass"), (Id: "domain-parts", Kind: "Domain"), (Id: "product-parts", Kind: "Product") })
                {
                    var frame = Workbook(source.Id); if (frame == null) continue;
                    foreach (int row in FamilyRows(frame, family.Id))
                    {
                        int name = Column(frame, "part"); if (name < 0) continue;
                        parts.Add(new(source.Kind, Cell(frame, row, name), NativeCell(frame, row, "within_family_pct", "%", fractionColumn: "within_family_fraction"), Unresolved(frame, row)));
                    }
                }
            string? peptides = null;
            var counts = context.UsesPanel && !context.IsIdentity ? Workbook("export:effective_panel_protein_counts.csv")
                : Workbook("protein-counts", "export:family_protein_counts.csv");
            if (counts != null)
                foreach (int row in FamilyRows(counts, family.Id, context.UsesPanel && !context.IsIdentity))
                { peptides = NativeCell(counts, row, "observed_peptides"); break; }
            string? cv = null;
            var means = context.UsesPanel ? Workbook("export:panel_family_run_means.csv") : Workbook("export:family_run_means.csv");
            if (means != null)
                foreach (int row in FamilyRows(means, family.Id))
                { cv = NativeCell(means, row, "cv_pct", "%", fractionColumn: "cv_ratio", stored: summary?.CoefficientOfVariation * 100); break; }
            var coverage = context.CoverageCatalogProvider == null
                ? context.CoverageProvider?.Invoke(family.Id, Add, selection) ?? Array.Empty<HtmlReportCoverage>()
                : Array.Empty<HtmlReportCoverage>();
            return family with { Runs = runs, Parts = parts, Peptides = peptides, Cv = cv, Coverage = coverage };
        }

        private static bool Unresolved(Frame frame, int row)
        {
            int flag = Column(frame, "unresolved");
            if (flag >= 0) return frame.Table.Rows[row][flag] == "1" || bool.TryParse(frame.Table.Rows[row][flag], out bool value) && value;
            int key = Column(frame, "part_key");
            return key >= 0 && frame.Table.Rows[row][key].Length == 0;
        }
    }
}
