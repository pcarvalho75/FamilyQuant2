using FamilyQuant.DeNovo;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace FamilyQuant.Tests
{
    internal static partial class Program
    {
        private static void QuantitationStageTests()
        {
            FamilySubclassStudyTests();
            var rows = new List<QuantRow>
            {
                Qr("identified", "r1", 1, 2, "PEPTLDEK", 10, 30),
                Qr("denovo", "r1", 2, 2, "PEPTIDEK", 50, 100),
                Qr("denovo", "r1", 3, 2, "VVVVVVVK", 5, 20),
                Qr("denovo", "r1", 4, 2, "GGGGGGGK", 3, 9),
                Qr("denovo", "r1", 5, 2, "GGGGGGGK", 7, 14),
                Qr("denovo", "r1", 6, 3, "PEPTIDEK", 4, 12),
                Qr("denovo", "r2", 7, 2, "PEPTIDEK", 6, 18),
                Qr("identified", "r1", 8, 2, "AAAAAAAK", 2, 6),
            };
            rows[1].PrecursorMz = rows[0].PrecursorMz + 1.00335483 / 2;
            rows[3].DeNovoEvidence = new List<DeNovoEvidence>
            {
                new() { SourceId = DeNovoSource.DenovoOnly.Id, Sequence = "GGGGGGGK" },
            };
            rows[4].DeNovoEvidence = new List<DeNovoEvidence>
            {
                new() { SourceId = DeNovoSource.DeepNovo.Id, Sequence = "GGGGGGGK" },
            };
            var mapping = new PeptideFamilyAttribution();
            mapping.IdentifiedBySequence["PEPTLDEK"] = new[] { 1 };
            mapping.DeNovoBySequence["VVVVVVVK"] = new[] { 9, 3, 9 };
            List<NativePeptideQuant> prepared = FamilyEnvelopeQuant.PreparePeptides(rows);
            string before = JsonSerializer.Serialize(prepared);
            FamilyPeptideAttribution assigned = FamilyEnvelopeQuant.AttributePeptides(prepared, mapping);
            Check("quantitation exposes prepared assignments and an unattributed de novo seam",
                assigned.Peptides.Count == 6 && assigned.UnattributedDeNovoPeptides.Count == 3);
            Check("second pass never receives an identified-equivalent de novo isotope hypothesis",
                !assigned.Peptides.Any(p => p.Peptide.RepresentativeScan == 2)
                && assigned.UnattributedDeNovoPeptides.All(p => p.Side == "denovo"));
            Check("second pass keeps another run and charge rather than suppressing by sequence alone",
                assigned.UnattributedDeNovoPeptides.Any(p => p.Run == "r2" && p.Charge == 2)
                && assigned.UnattributedDeNovoPeptides.Any(p => p.Run == "r1" && p.Charge == 3));
            NativePeptideQuant residual = assigned.UnattributedDeNovoPeptides.Single(p => p.Sequence == "GGGGGGGK");
            Check("second pass uses the max-M0 representative once, carrying all source claims",
                residual.RepresentativeScan == 5 && residual.AreaM0 == 7 && residual.EnvelopeSum == 14
                && residual.AnchorCount == 2 && residual.DeNovoEvidence.Count == 2);
            Check("the residual keeps the prepared calibrated peptide without refitting or copying values",
                ReferenceEquals(residual, prepared.Single(p => p.Sequence == "GGGGGGGK"))
                && residual.ReferenceCalibratedEnvelope > 0);
            Check("a shared mapped peptide never reaches the second pass and duplicate family hits count once",
                assigned.Peptides.Single(p => p.Peptide.Sequence == "VVVVVVVK").FamilyUserIds.SequenceEqual(new[] { 9, 3 })
                && assigned.UnattributedDeNovoPeptides.All(p => p.Sequence != "VVVVVVVK"));
            Check("assignment preserves prepared input values, source claims and order",
                JsonSerializer.Serialize(prepared) == before
                && assigned.Peptides.Select(p => p.Peptide).SequenceEqual(prepared.Where(p => p.RepresentativeScan != 2)));

            FamilyEnvelopeQuantResult table = FamilyEnvelopeQuant.BuildFromAttributedPeptides(assigned);
            Check("stage rollup keeps measured, shared and unattributed envelopes separate",
                table.MeasuredIdentifiedEnvelope == 36 && table.UnattributedIdentifiedEnvelope == 6
                && table.MeasuredDeNovoEnvelope == 64 && table.SharedDeNovoEnvelope == 20
                && table.UnattributedDeNovoEnvelope == 44);
            Check("stage rollup keeps count-in-every-family and split readings",
                table.Families.Single(f => f.FamilyUserId == 9).DeNovoEnvelope == 20
                && table.Families.Single(f => f.FamilyUserId == 3).SplitDeNovoEnvelope == 10
                && table.Families.Single(f => f.FamilyUserId == 1).UniqueIdentifiedEnvelope == 30);
            double closed = table.Families.Sum(f => f.UniqueTotalEnvelopeReferenceCalibrated)
                + table.SharedTotalEnvelopeReferenceCalibrated + table.UnattributedTotalEnvelopeReferenceCalibrated;
            Check("stage unique parts plus shared plus residual close on the calibrated measured total",
                Math.Abs(closed - table.MeasuredTotalEnvelopeReferenceCalibrated) < 1e-10);
            string csv = CsvReport.BuildFamilyComposition(table, SharedPeptidePolicy.FamilyUnique,
                table.MeasuredTotalEnvelopeReferenceCalibrated, "fraction of the quantified signal");
            CultureInfo original = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR");
                var again = FamilyEnvelopeQuant.BuildFromAttributedPeptides(
                    FamilyEnvelopeQuant.AttributePeptides(prepared, mapping));
                Check("stage output and CSV are byte-identical on a second run under a decimal-comma culture",
                    JsonSerializer.Serialize(table) == JsonSerializer.Serialize(again)
                    && csv == CsvReport.BuildFamilyComposition(again, SharedPeptidePolicy.FamilyUnique,
                        again.MeasuredTotalEnvelopeReferenceCalibrated, "fraction of the quantified signal"));
            }
            finally { CultureInfo.CurrentCulture = original; }

            var empty = FamilyEnvelopeQuant.AttributePeptides(Array.Empty<NativePeptideQuant>(), mapping);
            Check("an empty assignment has an empty residual and zero measured rollup",
                empty.Peptides.Count == 0 && empty.UnattributedDeNovoPeptides.Count == 0
                && FamilyEnvelopeQuant.BuildFromAttributedPeptides(empty).MeasuredTotalEnvelope == 0);
        }
    }
}
