using FamilyQuant.DeNovo;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FamilyQuant.Tests
{
    internal static class SourceAwareDeNovoTests
    {
        public static void Run(Action<string, bool, string?> check, string workDir)
        {
            string importRoot = Path.Combine(workDir, "source-aware-import");
            Directory.CreateDirectory(importRoot);
            string csv = Path.Combine(importRoot, "source-aware.raw.deepnovo.csv");
            File.WriteAllText(csv,
                "Scan,Peptide,SCORE (%),Length,m/z,z,RT,Area,local confidence (%)\n" +
                "101,PEPTIDEK,77,8,500.25000,2,12.5,1000000,99 99 99 99 99 99 99 99\n");

            string discoveryRoot = Path.Combine(Path.GetTempPath(),
                "FamilyQuant.SourceDiscovery.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(discoveryRoot);
            string discoveryDeepNovo = Path.Combine(discoveryRoot, "fixture.raw.deepnovo.csv");
            File.WriteAllText(discoveryDeepNovo, "fixture\n");
            IReadOnlyList<string> discoveredDeepNovo =
                DeNovoSourceFileDiscovery.RequireFiles(discoveryRoot, DeNovoSource.DeepNovo);
            check("configured-source discovery returns the selected caller's matched files",
                discoveredDeepNovo.SequenceEqual(new[] { Path.GetFullPath(discoveryDeepNovo) },
                    StringComparer.OrdinalIgnoreCase), string.Join("; ", discoveredDeepNovo));
            bool absentConfiguredSourceRejected = false;
            try
            {
                _ = DeNovoSourceFileDiscovery.RequireFiles(discoveryRoot,
                    DeNovoSource.DenovoOnly);
            }
            catch (FileNotFoundException ex)
            {
                absentConfiguredSourceRejected = ex.Message.Contains(
                    "silently omitting a configured caller", StringComparison.Ordinal);
            }
            check("a configured union source with zero matched files fails closed",
                absentConfiguredSourceRejected,
                "the absent PEAKS de-novo-only source was silently omitted");

            PeaksDeNovoRow deep = PeaksCsvReader.Read(csv, DeNovoSource.DeepNovo).Single();
            check("source-aware reader preserves stable DeepNovo identity",
                deep.SourceId == DeNovoSource.DeepNovo.Id && deep.SourceDisplayName == "DeepNovo", null);
            check("source-aware reader calls DeepNovo confidence SCORE, never ALC",
                deep.ConfidenceField == "SCORE (%)" && deep.ConfidenceValue == 77
                && !deep.ConfidenceField.Contains("ALC", StringComparison.OrdinalIgnoreCase), null);
            check("source-aware reader fingerprints and paths the exact input",
                deep.InputFilePath == Path.GetFullPath(csv)
                && deep.InputFileSha256 == FileFingerprint.Sha256(csv)
                && deep.SourceEvidence.Count == 1, null);

            var criteria = new DeNovoFilterCriteria
            {
                MinLength = 7,
                MinAverageLocalConfidence = 80,
                StretchLength = 5,
                StretchFloor = 90,
            };
            check("a DeepNovo SCORE cutoff is independent of the legacy PEAKS ALC cutoff",
                !DeNovoFilter.Passes(deep, criteria)
                && DeNovoFilter.Passes(deep, criteria, minimumConfidence: 75), null);
            check("PTM cleaning rejects malformed or nested annotations instead of leaking a sequence",
                DeNovoReconciler.NormalizeSequenceIgnoringPtm("C(+57.02PEPTIDEK", false) == string.Empty
                && DeNovoReconciler.NormalizeSequenceIgnoringPtm("C([+57.02])PEPTIDEK", false) == string.Empty
                && DeNovoReconciler.NormalizeSequenceIgnoringPtm("C(+57.02]PEPTIDEK", false) == string.Empty
                && !DeNovoReconciler.SequencesEqualIgnoringPtm(
                    "C(+57.02PEPTIDEK", "C(+57.02PEPTIDEK"), null);
            check("PTM cleaning rejects ambiguous, Unicode and punctuated residue strings conservatively",
                DeNovoReconciler.NormalizeSequenceIgnoringPtm("PEPTXDEK", false) == string.Empty
                && DeNovoReconciler.NormalizeSequenceIgnoringPtm("PEPTIDÉK", false) == string.Empty
                && DeNovoReconciler.NormalizeSequenceIgnoringPtm("PEPT*IDEK", false) == string.Empty
                && DeNovoReconciler.NormalizeSequenceIgnoringPtm("K.C(+57.02)EPTIDEK.R", false)
                    == "CEPTIDEK", null);

            var deepCriteria = new DeNovoFilterCriteria
            {
                MinLength = criteria.MinLength,
                MinAverageLocalConfidence = 75,
                StretchLength = criteria.StretchLength,
                StretchFloor = criteria.StretchFloor,
            };
            DeNovoImport uiImport = DeNovoControl.ImportAndFilter(importRoot, deepCriteria,
                DeNovoSource.DeepNovo);
            check("the production De Novo screen importer can select DeepNovo with SCORE semantics",
                uiImport.SourceId == DeNovoSource.DeepNovo.Id
                && uiImport.ConfidenceField == "SCORE (%)"
                && uiImport.RowsKept == 1
                && uiImport.Kept[0].ConfidenceField == "SCORE (%)", null);

            PeaksDeNovoRow peaks = Row(101, "PEPTLDEK", 500.25000, 2,
                DeNovoSource.DeNovoAll, 81, "peaks.csv", "peaks-hash");
            // 9.995 ppm apart but different at five decimals: this catches an exact-rounded-m/z union,
            // which would extract and credit the same physical precursor twice.
            PeaksDeNovoRow deepHigh = Row(101, "ALTERNATE", 500.25500, 2,
                DeNovoSource.DeepNovo, 99, "deep.csv", "deep-hash");
            var selections = new[]
            {
                new DeNovoSourceSelection(DeNovoSource.DeNovoAll, 80, 0),
                new DeNovoSourceSelection(DeNovoSource.DeepNovo, 75, 1),
            };

            List<PeaksDeNovoRow> union = DeNovoUnionResolver.Resolve(new[] { deepHigh, peaks }, selections);
            List<PeaksDeNovoRow> reverseUnion = DeNovoUnionResolver.Resolve(new[] { peaks, deepHigh }, selections);
            check("a shared run/scan/charge/mz precursor produces exactly one union observation",
                union.Count == 1
                && Math.Round(peaks.Mz, DeNovoXicExtractor.MzDecimals)
                    != Math.Round(deepHigh.Mz, DeNovoXicExtractor.MzDecimals), null);
            check("union selection follows explicit source priority, not incomparable ALC/SCORE values",
                union[0].SourceId == DeNovoSource.DeNovoAll.Id
                && reverseUnion[0].SourceId == union[0].SourceId, null);
            check("the selected union observation retains both source sequence claims",
                union[0].SourceEvidence.Count == 2
                && union[0].SourceEvidence.Any(e => e.SourceId == DeNovoSource.DeepNovo.Id)
                && union[0].SourceEvidence.Any(e => e.SourceId == DeNovoSource.DeNovoAll.Id), null);

            DeNovoSourceScoreDistribution peaksScores = DeNovoSourceScoreDistributionStudy.Build(
                new[] { peaks }, selections[0], criteria);
            DeNovoSourceScoreDistribution deepScores = DeNovoSourceScoreDistributionStudy.Build(
                new[] { deepHigh }, selections[1], criteria);
            check("source distributions remain separate and carry exact field labels",
                peaksScores.ConfidenceField == "ALC (%)"
                && deepScores.ConfidenceField == "SCORE (%)"
                && peaksScores.SourceId != deepScores.SourceId, null);

            PeaksDeNovoRow vennPeaksOnly = Row(201, "PEPTIDEK", 600.0, 2,
                DeNovoSource.DenovoOnly, 90, "only.csv", "only-hash");
            PeaksDeNovoRow vennPeaksShared = Row(202, "SHAREDSEQ", 610.0, 2,
                DeNovoSource.DenovoOnly, 91, "only.csv", "only-hash");
            PeaksDeNovoRow vennPeaksRejected = Row(203, "LOWPEAKS", 620.0, 2,
                DeNovoSource.DenovoOnly, 70, "only.csv", "only-hash");
            PeaksDeNovoRow vennDeepShared = Row(202, "SHAREDSEQ", 610.005, 2,
                DeNovoSource.DeepNovo, 92, "deep.csv", "deep-hash");
            PeaksDeNovoRow vennDeepOnly = Row(204, "DEEPNEVA", 630.0, 2,
                DeNovoSource.DeepNovo, 93, "deep.csv", "deep-hash");
            PeaksDeNovoRow vennDeepRejected = Row(205, "LOWDEEP", 640.0, 2,
                DeNovoSource.DeepNovo, 60, "deep.csv", "deep-hash");
            var vennSelections = new[]
            {
                new DeNovoSourceSelection(DeNovoSource.DenovoOnly, 80, 0),
                new DeNovoSourceSelection(DeNovoSource.DeepNovo, 80, 1),
            };
            List<PeaksDeNovoRow> vennFiltered = DeNovoFilter.Apply(
                new[] { vennPeaksOnly, vennPeaksShared, vennPeaksRejected }, criteria, 80)
                .Concat(DeNovoFilter.Apply(
                    new[] { vennDeepShared, vennDeepOnly, vennDeepRejected }, criteria, 80))
                .ToList();
            List<PeaksDeNovoRow> vennUnion = DeNovoUnionResolver.Resolve(
                vennFiltered, vennSelections);
            List<ReconciledRow> vennReconciliation = DeNovoReconciler.Reconcile(vennUnion,
                new[]
                {
                    new PatternLabPsm
                    {
                        FileName = "run.raw.sqt", ScanNumber = 204, ChargeState = 2,
                        PrecursorMz = 630.0, Sequence = "DEEPNEVA", XCorr = 4.0,
                    },
                });
            DeNovoSourceVennSummary sourceVenn = DeNovoSourceVennStudy.Build(
                new[] { vennPeaksOnly, vennPeaksShared, vennPeaksRejected },
                new[] { vennDeepShared, vennDeepOnly, vennDeepRejected }, criteria,
                peaksMinimumConfidence: 80, deepNovoMinimumConfidence: 80,
                unionReconciliation: vennReconciliation);
            check("source Venn tier A uses exact filtered sequence sets before precursor union",
                sourceVenn.FilteredBeforeUnion.PeaksExclusive == 1
                && sourceVenn.FilteredBeforeUnion.Shared == 1
                && sourceVenn.FilteredBeforeUnion.DeepNovoExclusive == 1
                && sourceVenn.FilteredBeforeUnion.Union == 3, null);
            check("source Venn tier B follows carried union-row claim provenance",
                sourceVenn.CarriedForwardClaimProvenance.PeaksExclusive == 1
                && sourceVenn.CarriedForwardClaimProvenance.Shared == 1
                && sourceVenn.CarriedForwardClaimProvenance.DeepNovoExclusive == 0
                && sourceVenn.CarriedForwardClaimProvenance.Union == 2, null);
            check("source Venn stores the cumulative score-length-local filter funnel",
                sourceVenn.PeaksFunnel.RawRows == 3
                && sourceVenn.PeaksFunnel.ScorePassingRows == 2
                && sourceVenn.PeaksFunnel.ScoreAndLengthPassingRows == 2
                && sourceVenn.PeaksFunnel.AllFiltersPassingRows == 2
                && sourceVenn.DeepNovoFunnel.AllFiltersPassingRows == 2, null);
            string sourceVennCsv = DeNovoSourceVennStudy.ToCsv(sourceVenn);
            check("source Venn CSV states both exact grains and avoids separate-variant inference",
                sourceVennCsv.Contains("filtered-before-union")
                && sourceVennCsv.Contains("carried-forward-claim-provenance")
                && sourceVennCsv.Contains("not inferred from separate source-variant totals")
                && sourceVennCsv.Contains("SCORE (%)"), null);
            string ordinaryVennScope = sourceVenn.FilteredBeforeUnion.Scope;
            sourceVenn.FilteredBeforeUnion.Scope = "=HYPERLINK(\"https://invalid\")";
            string formulaSafeVennCsv = DeNovoSourceVennStudy.ToCsv(sourceVenn);
            sourceVenn.FilteredBeforeUnion.Scope = ordinaryVennScope;
            check("source Venn CSV neutralizes spreadsheet formulas in user-derived labels",
                formulaSafeVennCsv.Contains("'=HYPERLINK", StringComparison.Ordinal), null);

            var extracted = new DeNovoXicResult
            {
                RunName = "run",
                ScanNoMS2 = 101,
                PrecursorMz = 500.25,
                Charge = 2,
                Tag = union[0].Peptide,
                IsotopeAreas = new[] { 10.0 },
            };
            List<QuantRow> quant = QuantRow.FromResults("denovo", "run", new[] { extracted });
            QuantRow.AttachDeNovoEvidence(quant, union);

            var patternLab = new PatternLabPsm
            {
                FileName = "run.raw.sqt",
                ScanNumber = 101,
                ChargeState = 2,
                PrecursorMz = 500.25000,
                Sequence = "K.C(+57.02)EPTIDEK.R",
                XCorr = 4.25,
            };
            var identifiedResult = new DeNovoXicResult
            {
                RunName = "run",
                ScanNoMS2 = 101,
                PrecursorMz = 500.25000,
                Charge = 2,
                Tag = patternLab.Sequence,
                IsotopeAreas = new[] { 10.0 },
            };
            List<QuantRow> identified = QuantRow.FromResults("identified", "run",
                new[] { identifiedResult }, familyUserId: 42);
            QuantRow.AttachPatternLabScores(identified, new[] { patternLab });
            quant.AddRange(identified);

            PeaksDeNovoRow incompatible = Row(101, "DIFFERENT", 700.0, 3,
                DeNovoSource.DeepNovo, 88, "deep2.csv", "deep2-hash");
            List<SpectrumComparisonRow> spectrumRows = SpectrumComparisonTable.Build(
                new[] { peaks }, new[] { deepHigh, incompatible }, new[] { patternLab }, quant,
                rawPrecursors: new[]
                {
                    new RawScanPrecursorMetadata
                    {
                        RunName = "run.raw",
                        ScanNumber = 101,
                        Charge = 2,
                        PrecursorMz = 500.25000,
                    },
                },
                perRunTic: new Dictionary<string, double> { ["run.raw"] = 100.0 },
                acceptedDeNovoRows: new[] { peaks, deepHigh });
            SpectrumComparisonRow shared = spectrumRows.Single(r => r.Charge == 2);
            SpectrumComparisonRow incompatibleRow = spectrumRows.Single(r => r.Charge == 3);
            check("spectrum table shares one quant only among compatible precursor alternatives",
                spectrumRows.Count == 2 && shared.PeaksDeNovoCandidateCount == 1
                && shared.DeepNovoCandidateCount == 1 && shared.PatternLabCandidateCount == 1
                && shared.QuantPrimaryAssigned == 10.0 && shared.QuantCandidateCount == 2
                && shared.QuantValuesAgree && incompatibleRow.PrimaryQuantValue == null, null);
            check("spectrum table flags incompatible same-scan hypotheses and never copies their quant",
                shared.SameScanDifferentPrecursorHypothesis
                && incompatibleRow.SameScanDifferentPrecursorHypothesis
                && incompatibleRow.PrecursorAgreement.Contains("SameScanDifferentPrecursorHypothesis")
                && incompatibleRow.JoinStatus.Contains("SourceVsRawPrecursorMismatch")
                && incompatibleRow.DeepNovoAccepted == false
                && incompatibleRow.DeepNovoAcceptedCandidateCount == 0
                && incompatibleRow.PrimaryQuantValue == null, null);
            check("spectrum table carries RAW diagnostics beside source-compatible canonical metadata",
                shared.RawPrecursorCharge == 2 && shared.RawPrecursorMz == 500.25
                && shared.CanonicalCharge == 2 && shared.CanonicalPrecursorMz == 500.25
                && shared.PatternLabCharge == 2
                && shared.DeepNovoCharge == 2 && shared.DeepNovoMatchesRawPrecursor == true, null);
            check("spectrum table carries reproducible TIC denominators and credited-row status",
                shared.RunTic == 100.0 && shared.PooledTic == 100.0
                && shared.QuantCreditedInPrimaryTotals, null);

            var manyFamilyAttribution = new PeptideFamilyAttribution();
            manyFamilyAttribution.IdentifiedBySequence[
                DeNovoReconciler.NormalizeSequence(patternLab.Sequence)] = new[] { 7, 8 };
            manyFamilyAttribution.FamilyDescriptions[7] = "Family seven";
            manyFamilyAttribution.FamilyDescriptions[8] = "Family eight";
            SpectrumComparisonRow manyFamilyRow = SpectrumComparisonTable.Build(
                new[] { peaks }, new[] { deepHigh }, new[] { patternLab }, quant,
                familyAttribution: manyFamilyAttribution,
                acceptedDeNovoRows: new[] { peaks, deepHigh }).Single();
            check("spectrum table retains every many-to-many curated family membership",
                manyFamilyRow.FamilyUserId == null
                && manyFamilyRow.FamilyUserIds == "7;8"
                && manyFamilyRow.FamilyAssignments.Contains("F7: Family seven")
                && manyFamilyRow.FamilyAssignments.Contains("F8: Family eight")
                && manyFamilyRow.FamilyMappingMinimumIdentity == 0
                && manyFamilyRow.FamilyMappingStatus.Contains("no searchable protein rows",
                    StringComparison.Ordinal), null);

            var repeatedAnchors = new[]
            {
                new QuantRow
                {
                    Side = NativeQuantFractions.SideIdentified, Run = "run", Scan = 300,
                    PrecursorMz = 600.0, Charge = 2, Sequence = "PEPTIDEK",
                    IsotopeAreas = new[] { 25.0 }, EnvelopeSum = 25.0,
                },
                new QuantRow
                {
                    Side = NativeQuantFractions.SideIdentified, Run = "run", Scan = 301,
                    PrecursorMz = 600.0, Charge = 2, Sequence = "PEPTIDEK",
                    IsotopeAreas = new[] { 25.0 }, EnvelopeSum = 25.0,
                },
            };
            List<SpectrumComparisonRow> repeatedRows = SpectrumComparisonTable.Build(repeatedAnchors);
            check("bit-identical repeated MS2 anchors credit exactly the deterministic representative scan",
                repeatedRows.Count == 2
                && repeatedRows.Count(r => r.QuantCreditedInPrimaryTotals) == 1
                && repeatedRows.Single(r => r.QuantCreditedInPrimaryTotals).ScanNumber == 300, null);

            // The table must credit quant for EVERY run present, not only the first. An auditor summing
            // the credited rows and their RunTic across the whole table has to reproduce the pooled
            // headline, so a three-run store credits three runs, each with its own RunTic populated.
            var threeRunSequences = new[] { "PEPTIDEAK", "ELVISKR", "SAMPLERK" };
            var threeRunQuant = new List<QuantRow>();
            int threeRunScan = 400;
            foreach (string runName in new[] { "run_01", "run_02", "run_03" })
                foreach (string sequence in threeRunSequences)
                    threeRunQuant.Add(new QuantRow
                    {
                        Side = NativeQuantFractions.SideIdentified, Run = runName, Scan = threeRunScan++,
                        PrecursorMz = 600.0 + sequence.Length, Charge = 2, Sequence = sequence,
                        IsotopeAreas = new[] { 500.0 + sequence.Length, 100.0 }, EnvelopeSum = 900.0 + sequence.Length,
                    });
            var threeRunTic = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["run_01"] = 1_000_000.0, ["run_02"] = 2_000_000.0, ["run_03"] = 3_000_000.0,
            };
            List<SpectrumComparisonRow> threeRunRows = SpectrumComparisonTable.Build(
                Array.Empty<PeaksDeNovoRow>(), Array.Empty<PeaksDeNovoRow>(),
                Array.Empty<PatternLabPsm>(), threeRunQuant, perRunTic: threeRunTic);
            Func<string, int> creditedInRun = runName => threeRunRows.Count(r =>
                r.NormalizedRunName == runName && r.QuantCreditedInPrimaryTotals);
            check("the spectrum table credits quant and populates RunTic for every run, not only run 01",
                creditedInRun("run_01") == 3 && creditedInRun("run_02") == 3 && creditedInRun("run_03") == 3
                && threeRunRows.Where(r => r.NormalizedRunName == "run_02").All(r => r.RunTic == 2_000_000.0)
                && threeRunRows.Where(r => r.NormalizedRunName == "run_03").All(r => r.RunTic == 3_000_000.0)
                && threeRunRows.All(r => r.QuantPrimaryAssigned.HasValue),
                creditedInRun("run_01") + "/" + creditedInRun("run_02") + "/" + creditedInRun("run_03"));
            check("summing the whole table reproduces the pooled credit, not one third of it",
                threeRunRows.Count(r => r.QuantCreditedInPrimaryTotals) == 9
                && threeRunRows.Count(r => r.QuantCreditedInPrimaryTotals)
                    > creditedInRun("run_01"),
                threeRunRows.Count(r => r.QuantCreditedInPrimaryTotals).ToString());
            PeaksDeNovoRow acceptedIdentity = Row(777, "PEPTIDEK", 650.0, 2,
                DeNovoSource.DenovoOnly, 90, "identity.csv", "identity-hash");
            PeaksDeNovoRow rejectedByLocalIdentity = Row(777, "PEPTIDEK", 650.0, 2,
                DeNovoSource.DenovoOnly, 90, "identity.csv", "identity-hash");
            rejectedByLocalIdentity.LocalConfidence = new[] { 99, 99, 99, 99, 89, 99, 99, 99 };
            SpectrumComparisonRow localIdentityRow = SpectrumComparisonTable.Build(
                new[] { acceptedIdentity, rejectedByLocalIdentity }, Array.Empty<PeaksDeNovoRow>(),
                Array.Empty<PatternLabPsm>(), Array.Empty<QuantRow>(),
                acceptedDeNovoRows: new[] { acceptedIdentity }).Single();
            check("accepted-row identity includes the complete local-confidence vector",
                localIdentityRow.PeaksDeNovoCandidateCount == 2
                && localIdentityRow.PeaksDeNovoAcceptedCandidateCount == 1
                && localIdentityRow.PeaksDeNovoAccepted == true, null);

            PeaksDeNovoRow rejectedByLengthIdentity = Row(777, "PEPTIDEK", 650.0, 2,
                DeNovoSource.DenovoOnly, 90, "identity.csv", "identity-hash");
            rejectedByLengthIdentity.Length = 6;
            SpectrumComparisonRow lengthIdentityRow = SpectrumComparisonTable.Build(
                new[] { acceptedIdentity, rejectedByLengthIdentity }, Array.Empty<PeaksDeNovoRow>(),
                Array.Empty<PatternLabPsm>(), Array.Empty<QuantRow>(),
                acceptedDeNovoRows: new[] { acceptedIdentity }).Single();
            check("accepted-row identity includes the reported peptide length",
                lengthIdentityRow.PeaksDeNovoCandidateCount == 2
                && lengthIdentityRow.PeaksDeNovoAcceptedCandidateCount == 1
                && lengthIdentityRow.PeaksDeNovoAccepted == true, null);
            check("spectrum table strips PTM/flanking notation without changing the raw PatternLab sequence",
                shared.PatternLabSequence == patternLab.Sequence
                && shared.PatternLabCleanSequence == "CEPTIDEK", null);
            string spectrumCsv = SpectrumComparisonTable.ToCsv(spectrumRows);
            check("spectrum CSV starts with the requested columns and exposes score/quant semantics",
                spectrumCsv.StartsWith("ScanNumber,FileName,PeaksDeNovoSequence,DeepNovoSequence,PatternLabSequence,PeaksDeNovoScore,DeepNovoScore,PatternLabXScore,PrimaryQuantValue",
                    StringComparison.Ordinal)
                && spectrumCsv.Contains("QuantM0Observed")
                && spectrumCsv.Contains("RawPrecursorCharge")
                && spectrumCsv.Contains("PeaksDeNovoCharge")
                && spectrumCsv.Contains("DeepNovoCharge")
                && spectrumCsv.Contains("PatternLabCharge")
                && spectrumCsv.Contains("PeaksDeNovoAccepted")
                && spectrumCsv.Contains("DeepNovoAccepted")
                && spectrumCsv.Contains("FamilyUserIds")
                && spectrumCsv.Contains("FamilyMappingMinimumIdentity")
                && spectrumCsv.Contains("FamilyMappingStatus"), null);
            string formulaSafeCsv = SpectrumComparisonTable.ToCsv(new[]
            {
                new SpectrumComparisonRow { FileName = "=HYPERLINK(\"https://invalid\")" },
            });
            check("spectrum CSV neutralizes spreadsheet formulas in user-controlled text",
                formulaSafeCsv.Contains("'=HYPERLINK", StringComparison.Ordinal), null);

            string streamedCsvPath = Path.Combine(workDir, "streamed-spectrum-comparison.csv");
            if (File.Exists(streamedCsvPath)) File.Delete(streamedCsvPath);
            bool destinationExistedWhileRowsWereEnumerated = false;
            IEnumerable<SpectrumComparisonRow> ObserveStreaming()
            {
                destinationExistedWhileRowsWereEnumerated = File.Exists(streamedCsvPath);
                foreach (SpectrumComparisonRow row in spectrumRows) yield return row;
            }
            SpectrumComparisonTable.SaveCsv(streamedCsvPath, ObserveStreaming());
            check("spectrum CSV opens its destination before enumerating rows instead of buffering the full file",
                destinationExistedWhileRowsWereEnumerated
                && File.ReadAllText(streamedCsvPath) == spectrumCsv, streamedCsvPath);

            string blockedCsvParent = Path.Combine(workDir, "csv-parent-is-a-file");
            File.WriteAllText(blockedCsvParent, "not a directory");
            var completedDespiteCsvFailure = new FullXicComputeResult
            {
                Rows = quant,
                Manifest = new QuantManifest { IsotopeCount = 1 },
                SpectrumComparisonRows = spectrumRows,
            };
            bool csvWritten = completedDespiteCsvFailure.TryExportSpectrumComparisonCsv(
                Path.Combine(blockedCsvParent, "cannot-write.csv"));
            check("a final automatic CSV path failure preserves the completed quant/project result",
                !csvWritten
                && completedDespiteCsvFailure.Rows.Count == quant.Count
                && completedDespiteCsvFailure.SpectrumComparisonRows.Count == spectrumRows.Count
                && completedDespiteCsvFailure.Manifest.IsotopeCount == 1
                && string.IsNullOrEmpty(completedDespiteCsvFailure.SpectrumComparisonCsvPath), null);
            check("an automatic CSV failure is reported as a recoverable, actionable warning",
                completedDespiteCsvFailure.SpectrumComparisonCsvError.Contains(
                    "Quantitation completed", StringComparison.Ordinal)
                && completedDespiteCsvFailure.SpectrumComparisonCsvError.Contains(
                    "save the .fqproj", StringComparison.Ordinal)
                && completedDespiteCsvFailure.Notes.Any(n => n ==
                    completedDespiteCsvFailure.SpectrumComparisonCsvError),
                completedDespiteCsvFailure.SpectrumComparisonCsvError);

            var exactAccepted = new SpectrumComparisonRow
            {
                FileName = "run.raw",
                NormalizedRunName = "run",
                ScanNumber = 102,
                PrecursorHypothesisIndex = 1,
                Charge = 2,
                PrecursorMz = 555.25,
                PeaksDeNovoSequence = "C(+57.02)EPTIDEK",
                PeaksDeNovoCleanSequence = "CEPTIDEK",
                PeaksDeNovoScore = 88,
                PeaksDeNovoScoreField = "ALC (%)",
                PeaksDeNovoAccepted = true,
                DeepNovoSequence = "CEPTIDEK",
                DeepNovoCleanSequence = "CEPTIDEK",
                DeepNovoScore = 91,
                DeepNovoScoreField = "SCORE (%)",
                DeepNovoAccepted = true,
                PrimaryQuantValue = 123.0,
            };
            DeNovoAcceptedScoreScatter acceptedScatter =
                DeNovoAcceptedScoreScatterStudy.Build(new[] { shared, exactAccepted },
                    new[] { peaksScores, deepScores });
            check("accepted score scatter uses only both-accepted precursor-hypothesis rows",
                acceptedScatter.Total == 2 && acceptedScatter.Matches == 1
                && acceptedScatter.Mismatches == 1, null);
            check("accepted score scatter removes PTM notation but keeps I/L strict",
                acceptedScatter.Points.Single(p => p.ScanNumber == 102).StrictSequenceMatch
                && acceptedScatter.Points.Single(p => p.ScanNumber == 102).PeaksCleanSequence
                    == "CEPTIDEK"
                && !acceptedScatter.Points.Single(p => p.ScanNumber == 101).StrictSequenceMatch,
                null);
            string acceptedScatterCsv = DeNovoAcceptedScoreScatterStudy.ToCsv(acceptedScatter);
            check("accepted score scatter CSV preserves raw/clean provenance and score caveat",
                acceptedScatterCsv.Contains("C(+57.02)EPTIDEK")
                && acceptedScatterCsv.Contains("CEPTIDEK")
                && acceptedScatterCsv.Contains("strict PTM-clean match")
                && acceptedScatterCsv.Contains("not calibrated as equivalent probabilities"), null);
            DeNovoAcceptedScorePoint formulaPoint = acceptedScatter.Points[0];
            string ordinaryScatterFile = formulaPoint.FileName;
            double? ordinaryScatterQuant = formulaPoint.PrimaryQuantValue;
            formulaPoint.FileName = "  =HYPERLINK(\"https://invalid\")";
            formulaPoint.PrimaryQuantValue = -12.5;
            string formulaSafeScatterCsv = DeNovoAcceptedScoreScatterStudy.ToCsv(acceptedScatter);
            formulaPoint.FileName = ordinaryScatterFile;
            formulaPoint.PrimaryQuantValue = ordinaryScatterQuant;
            check("accepted-score CSV neutralizes formulas but preserves real signed numeric cells",
                formulaSafeScatterCsv.Contains("'  =HYPERLINK", StringComparison.Ordinal)
                && formulaSafeScatterCsv.Contains(",-12.5,", StringComparison.Ordinal)
                && !formulaSafeScatterCsv.Contains("'-12.5", StringComparison.Ordinal), null);
            var acceptedScatterBitmap = ChartImageExporter.Render(
                new DeNovoAcceptedScoreScatterChart(acceptedScatter));
            check("accepted score scatter renders at the projector-native 1920x1080 size",
                acceptedScatterBitmap.PixelWidth == 1920
                && acceptedScatterBitmap.PixelHeight == 1080, null);

            List<SpectrumComparisonRow> fallbackRows = SpectrumComparisonTable.Build(
                new[] { peaks }, new[] { deepHigh, incompatible }, new[] { patternLab }, quant,
                acceptedDeNovoRows: new[] { peaks, deepHigh });
            check("without RAW metadata the same reported precursor partition is retained",
                fallbackRows.Count == 2
                && fallbackRows.All(r => !r.RawPrecursorKnown)
                && fallbackRows.All(r => r.PrecursorKeyBasis.Contains("diagnostic only"))
                && fallbackRows.All(r => r.JoinStatus.Contains("not RAW-validated")), null);

            var manifest = new QuantManifest
            {
                FormatVersion = ProjectStore.CurrentFormatVersion,
                IsotopeCount = 1,
                MinPointsForQuant = 3,
                Ppm = 10,
                DeNovoInputs = new List<DeNovoInputManifest>
                {
                    new DeNovoInputManifest
                    {
                        SourceId = DeNovoSource.DeepNovo.Id,
                        SourceDisplayName = DeNovoSource.DeepNovo.DisplayName,
                        ConfidenceField = DeNovoSource.DeepNovo.ConfidenceColumn,
                        MinimumConfidence = 75,
                        SourcePriority = 1,
                        FilePath = deep.InputFilePath,
                        Sha256 = deep.InputFileSha256,
                        RawRows = 1,
                        RowsPassingSourceFilter = 1,
                    },
                },
                DeNovoSourceScoreDistributions = new List<DeNovoSourceScoreDistribution> { deepScores },
                DeNovoUnionResolutionMethod = DeNovoUnionResolver.Method,
                SpectrumComparisonRows = spectrumRows,
                DeNovoSourceVenn = sourceVenn,
                DeNovoAcceptedScoreScatter = acceptedScatter,
            };

            string project = Path.Combine(workDir, "source-aware.fqproj");
            ProjectStore.WriteFqproj(project, new FamilyClusterFile(), quant, manifest);
            var loaded = ProjectStore.ReadFqproj(project);
            check("project quant rows round-trip selected and alternate source evidence",
                loaded.quant.Count == 2
                && loaded.quant.Single(q => q.Side == "denovo").DeNovoEvidence.Count == 2
                && loaded.quant.Single(q => q.Side == "denovo").PrimaryDeNovoEvidence?.SourceId
                    == DeNovoSource.DeNovoAll.Id, null);
            check("project quant rows round-trip PatternLab XCorr as an identification score",
                loaded.quant.Single(q => q.Side == "identified").PatternLabXScore == 4.25, null);
            check("project manifest round-trips the complete compute-time spectrum table",
                loaded.manifest.SpectrumComparisonRows.Count == 2
                && loaded.manifest.SpectrumComparisonRows[0].RawPrecursorKnown
                && loaded.manifest.SpectrumComparisonRows.Any(r => r.PatternLabXScore == 4.25), null);
            check("project manifest round-trips source cutoff, confidence field and fingerprint",
                loaded.manifest.DeNovoInputs.Count == 1
                && loaded.manifest.DeNovoInputs[0].ConfidenceField == "SCORE (%)"
                && loaded.manifest.DeNovoInputs[0].MinimumConfidence == 75
                && loaded.manifest.DeNovoInputs[0].Sha256 == deep.InputFileSha256, null);
            check("project manifest round-trips source-specific distributions without pooling",
                loaded.manifest.DeNovoSourceScoreDistributions.Count == 1
                && loaded.manifest.DeNovoSourceScoreDistributions[0].ConfidenceField == "SCORE (%)"
                && loaded.manifest.DeNovoUnionResolutionMethod == DeNovoUnionResolver.Method, null);
            check("project manifest round-trips exact source Venn regions and funnels",
                loaded.manifest.DeNovoSourceVenn != null
                && loaded.manifest.DeNovoSourceVenn.FilteredBeforeUnion.Union == 3
                && loaded.manifest.DeNovoSourceVenn.CarriedForwardClaimProvenance.Union == 2
                && loaded.manifest.DeNovoSourceVenn.DeepNovoFunnel.RawRows == 3, null);
            check("project manifest round-trips every accepted score/match point",
                loaded.manifest.DeNovoAcceptedScoreScatter != null
                && loaded.manifest.DeNovoAcceptedScoreScatter.Total == 2
                && loaded.manifest.DeNovoAcceptedScoreScatter.Matches == 1
                && loaded.manifest.DeNovoAcceptedScoreScatter.Points.Any(p =>
                    p.PeaksAnnotatedSequence == "C(+57.02)EPTIDEK"), null);
        }

        private static PeaksDeNovoRow Row(int scan, string sequence, double mz, int charge,
                                           DeNovoSource source, double confidence,
                                           string path, string fingerprint)
        {
            var row = new PeaksDeNovoRow
            {
                RunName = "run",
                Scan = scan,
                Peptide = sequence,
                Length = sequence.Length,
                Mz = mz,
                Charge = charge,
                ConfidenceValue = confidence,
                ConfidenceField = source.ConfidenceColumn,
                SourceId = source.Id,
                SourceDisplayName = source.DisplayName,
                InputFilePath = path,
                InputFileSha256 = fingerprint,
                LocalConfidence = Enumerable.Repeat(99, sequence.Length).ToArray(),
            };
            row.SourceEvidence.Add(new DeNovoEvidence
            {
                SourceId = row.SourceId,
                SourceDisplayName = row.SourceDisplayName,
                ConfidenceField = row.ConfidenceField,
                ConfidenceValue = row.ConfidenceValue,
                Sequence = row.Peptide,
                InputFilePath = row.InputFilePath,
                InputFileSha256 = row.InputFileSha256,
            });
            return row;
        }
    }
}
