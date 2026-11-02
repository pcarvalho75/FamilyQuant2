using FamilyQuant.DeNovo;
using FamilyQuant.Evidence;
using FamilyQuant.Homology;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;

namespace FamilyQuant.Tests
{
    internal enum FamilyWorldVerdict { Pass, Between, Kill }

    /// <summary>
    /// B1, the gate on the family world (docs/ROADMAP.md, W8). Two tests, both through the program's own mapper
    /// at its shipped 80% gate and the production de novo rule.
    ///
    /// Known answers: scans the database search identified and a de novo engine also called at a compatible
    /// precursor (the stored spectrum table: same file, scan, charge and 20-ppm precursor), so the tag carries
    /// the de novo engine's real errors while the family is known from the identified peptide. The family world
    /// loses the sample's own species, and the tag must find its family through other species alone. The same
    /// test without the whole genus is reported beside it, ungated.
    ///
    /// Coverage: the de novo envelope the project's own database leaves unattributed, searched against the whole
    /// family world with reversed copies as decoys.
    ///
    /// The thresholds were fixed in the roadmap before this ran. A KILL is a scientific answer, not a failed check.
    /// </summary>
    internal static class FamilyWorldCheck
    {
        internal const string Argument = "--family-world-check";

        // ROADMAP B1, fixed before the experiment. Never moved to reach a verdict.
        internal const double PassRightRate = 0.95;
        internal const double KillRightRate = 0.85;
        internal const double PassLandedFraction = 0.20;
        internal const double KillLandedFraction = 0.05;
        internal const double PassDecoyRate = 0.05;
        internal const double KillDecoyRate = 0.20;

        private const string UniProt = "https://rest.uniprot.org/";

        /// <summary>B1's settings: no minimum tag length and every family of the world creditable. The product's
        /// defaults are D11's (<see cref="FamilyWorldOptions"/>).</summary>
        private static FamilyWorldOptions B1Options(FamilyWorld world) =>
            new FamilyWorldOptions { MinimumTagLength = 0, CreditableFamilies = world.Families };

        internal static FamilyWorldVerdict Verdict(double rightRate, double landedFraction, double decoyRate)
        {
            if (rightRate < KillRightRate || landedFraction < KillLandedFraction || decoyRate > KillDecoyRate)
                return FamilyWorldVerdict.Kill;
            return rightRate >= PassRightRate && landedFraction >= PassLandedFraction && decoyRate <= PassDecoyRate
                ? FamilyWorldVerdict.Pass : FamilyWorldVerdict.Between;
        }

        internal static void Run(string[] args, Action<string, bool, string?> check)
        {
            try
            {
                int flag = Array.IndexOf(args, Argument);
                string At(int offset, string fallback) => TestArguments.EmptyFallsBack(args, flag, offset, fallback);
                string projectPath = At(1, "C:/Users/paulo/Desktop/FamilyQuant/BrProt_Talk/all+DeepNovo/scientific_results.fqproj");
                string worldPath = At(2, "D:/FamilyQuant-data/out-opus/e16-ana-searched.fcf");
                string outDir = At(3, "D:/FamilyQuant-data/out-opus/b1");
                Directory.CreateDirectory(outDir);
                RunOn(projectPath, worldPath, outDir, check);
            }
            catch (Exception ex) { check("family world check completed", false, ex.GetBaseException().ToString()); }
        }

        private static string N(long v) => v.ToString("N0", CultureInfo.InvariantCulture);
        private static string P(double v) => (100 * v).ToString("F2", CultureInfo.InvariantCulture) + "%";
        private static string R(double v) => v.ToString("R", CultureInfo.InvariantCulture);
        private static string N0(int v) => v.ToString(CultureInfo.InvariantCulture);

        private static void RunOn(string projectPath, string worldPath, string outDir, Action<string, bool, string?> check)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            void Log(string text) => Console.WriteLine("        [" + watch.Elapsed.TotalSeconds.ToString("0", CultureInfo.InvariantCulture) + " s] " + text);
            var summary = new StringBuilder();
            void Line(string key, string value) => summary.Append(key).Append('\t').Append(value).Append('\n');

            var (sample, quant, manifest) = ProjectStore.ReadFqproj(projectPath);
            List<FastaItemExtended> sampleItems = sample.MyExtendedFastaItems ?? new List<FastaItemExtended>();
            List<FastaItemExtended> sampleTargets = sampleItems.Where(i => i != null
                && !SequenceLabels.IsDecoy(i.SequenceIdentifier ?? string.Empty)
                && !(i.SequenceIdentifier ?? string.Empty).StartsWith(SequenceLabels.ContaminantPrefix, StringComparison.Ordinal)).ToList();
            var sampleTaxa = new SortedSet<string>(sampleTargets.Select(i => FamilyWorld.TaxonOf(i.Description)).Where(t => t.Length > 0), StringComparer.Ordinal);
            var sampleAccessions = new HashSet<string>(sampleTargets.Select(i => FamilyWorld.BaseAccession(FamilyNaming.Accession(i.SequenceIdentifier))), StringComparer.Ordinal);
            Log("project: " + N(sampleItems.Count) + " proteins, " + N(sampleTargets.Count) + " targets, taxa " + string.Join(",", sampleTaxa) +
                "; " + N(quant.Count) + " quant rows, " + N(manifest.SpectrumComparisonRows.Count) + " spectrum rows");
            check("family world: the sample's targets name their taxon (OX=)", sampleTaxa.Count > 0 && sampleTargets.All(i => FamilyWorld.TaxonOf(i.Description).Length > 0),
                N(sampleTaxa.Count) + " taxa");

            FamilyClusterFile worldFile = FamilyClusterFile.Load(worldPath);
            List<FamilyWorldProtein> all = FamilyWorld.Collect(worldFile);
            string panelRelease = worldFile.EvidenceSource?.ReferencePanel?.Release ?? string.Empty;
            Log("family world file: " + N(all.Count) + " proteins (" + N(all.Count(p => p.FromPanel)) + " from the panel, release " + panelRelease +
                "), " + N(all.Count(p => p.Families.Count > 0)) + " with a UniProt family");
            Line("project", Path.GetFileName(projectPath));
            Line("family_world_file", Path.GetFileName(worldPath));
            Line("panel_release", panelRelease);
            Line("sample_taxa", string.Join(",", sampleTaxa));
            Line("mapper_min_identity", R(new HomologyOptions().MinIdentity));
            Line("world_proteins", N0(all.Count));

            // Membership from UniProt's taxonomy, because a panel entry carries no header and so no OX=.
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("FamilyQuant");
            TaxonMembers species = TaxonMembers.Union(sampleTaxa.Select(t => Members(client, t, outDir, Log)));
            check("family world: every sample target is in UniProt's list for its taxa",
                sampleAccessions.All(a => species.Accessions.Contains(a) || !FamilyNaming.IsUniProtAccession(a)),
                N(sampleAccessions.Count(a => !species.Accessions.Contains(a))) + " missing");
            if (panelRelease.Length > 0 && species.Release != panelRelease)
                Console.WriteLine("        WARNING: the taxonomy lists are UniProt " + species.Release + ", the panel " + panelRelease);
            Line("taxonomy_release", species.Release);

            List<(string Id, string Name)> genera = sampleTaxa.Select(t => Genus(client, t, outDir, Log)).Where(g => g.Id.Length > 0)
                .Distinct().OrderBy(g => g.Id, StringComparer.Ordinal).ToList();
            TaxonMembers? genus = genera.Count == 0 ? null : TaxonMembers.Union(genera.Select(g => Members(client, g.Id, outDir, Log)));

            List<FamilyWorldProtein> Without(TaxonMembers members, string label)
            {
                bool Out(FamilyWorldProtein p) => members.Organisms.Contains(p.Taxon) || sampleTaxa.Contains(p.Taxon)
                    || members.Accessions.Contains(p.Accession) || sampleAccessions.Contains(p.Accession);
                List<FamilyWorldProtein> kept = all.Where(p => !Out(p)).ToList();
                int byHeader = all.Count(p => members.Organisms.Contains(p.Taxon) || sampleTaxa.Contains(p.Taxon));
                Log(label + " removed: " + N(all.Count - kept.Count) + " proteins (" + N(byHeader) + " by OX=, " +
                    N(all.Count - kept.Count - byHeader) + " by UniProt's list or the sample database, " +
                    N(all.Count(p => p.FromPanel) - kept.Count(p => p.FromPanel)) + " of them panel entries)");
                check("family world: the world without the " + label + " holds none of its proteins",
                    kept.All(p => !members.Accessions.Contains(p.Accession) && !members.Organisms.Contains(p.Taxon)), null);
                return kept;
            }
            List<FamilyWorldProtein> withoutSpecies = Without(species, "species");
            Line("world_without_species", N0(withoutSpecies.Count));

            // ---------------------------------------------------------------- known answers
            var labels = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            foreach (FamilyWorldProtein p in all) if (!labels.ContainsKey(p.Accession)) labels.Add(p.Accession, p.Families);

            var pairs = new SortedDictionary<(string Tag, string Identified), KnownPair>();
            foreach (SpectrumComparisonRow row in manifest.SpectrumComparisonRows)
            {
                string identified = DeNovoReconciler.NormalizeSequence(row.PatternLabCleanSequence ?? string.Empty);
                if (identified.Length == 0) continue;
                void Add(string source, string clean, bool? accepted)
                {
                    string tag = DeNovoReconciler.NormalizeSequence(clean ?? string.Empty);
                    if (tag.Length == 0 || accepted != true) return;
                    if (!pairs.TryGetValue((tag, identified), out KnownPair? pair))
                        pairs[(tag, identified)] = pair = new KnownPair { Tag = tag, Identified = identified };
                    pair.Calls++;
                    pair.Sources.Add(source);
                }
                Add("PEAKS", row.PeaksDeNovoCleanSequence, row.PeaksDeNovoAccepted);
                Add("DeepNovo", row.DeepNovoCleanSequence, row.DeepNovoAccepted);
            }
            Log("known-answer pairs (accepted de novo call, identified scan, compatible precursor): " + N(pairs.Count) +
                " distinct, " + N(pairs.Values.Sum(p => p.Calls)) + " calls");

            // The family of an identified peptide: the UniProt families of the database proteins that hold it,
            // found by the production identified rule (the proteins tied at the best score).
            HomologyDatabase sampleDatabase = HomologyDatabase.Build(sampleItems);
            var identifiedHits = HomologyMapper.Search(pairs.Values.Select(p => p.Identified).Distinct(StringComparer.Ordinal), sampleDatabase,
                    new HomologyOptions { TopHitsPerPeptide = int.MaxValue, BestScoreOnly = true })
                .Where(r => !r.IsRejected).ToDictionary(r => r.NormalizedPeptide, StringComparer.Ordinal);
            foreach (KnownPair pair in pairs.Values)
            {
                if (!identifiedHits.TryGetValue(pair.Identified, out HomologyResult? r) || r.Hits.Count == 0 || r.Hits.Any(h => h.IsDecoy))
                {
                    pair.Exclusion = "no target holds the identified peptide";
                    continue;
                }
                if (r.Hits.Any(h => h.ProteinId.StartsWith(SequenceLabels.ContaminantPrefix, StringComparison.Ordinal)))
                {
                    pair.Exclusion = "a contaminant holds the identified peptide";
                    continue;
                }
                foreach (HomologyHit hit in r.Hits)
                {
                    string accession = FamilyWorld.BaseAccession(FamilyNaming.Accession(hit.ProteinId));
                    if (labels.TryGetValue(accession, out IReadOnlyList<string>? families) && families.Count > 0) pair.Known.UnionWith(families);
                    else pair.UnlabelledHolders++;
                }
                if (pair.Known.Count == 0) pair.Exclusion = "no protein holding it has a UniProt family";
            }
            List<KnownPair> known = pairs.Values.Where(p => p.Exclusion.Length == 0).ToList();
            foreach (var group in pairs.Values.Where(p => p.Exclusion.Length > 0).GroupBy(p => p.Exclusion).OrderBy(g => g.Key, StringComparer.Ordinal))
                Log("  excluded, " + group.Key + ": " + N(group.Count()));
            Log("known answers with a family: " + N(known.Count) + " pairs, " + N(known.Select(p => p.Identified).Distinct().Count()) + " identified peptides");
            Line("known_pairs_all", N0(pairs.Count));
            Line("known_pairs", N0(known.Count));

            // The families the world was gathered for: the panel's, read from the file. D11 credits only these.
            IReadOnlyList<string> scopeFamilies = worldFile.EvidenceSource?.ReferencePanel?.Families ?? new List<string>();
            FamilyWorld knownWorld = FamilyWorld.Build(withoutSpecies, scopeFamilies);
            var speciesRun = RunKnown("species", "", pairs.Values.ToList(), known, knownWorld, outDir, Log, Line);
            double rightRate = speciesRun.RightRate;
            if (genus != null)
            {
                Console.WriteLine();
                Line("genus", string.Join(",", genera.Select(g => g.Id + " " + g.Name)));
                List<FamilyWorldProtein> withoutGenus = Without(genus, "genus " + string.Join(", ", genera.Select(g => g.Name)));
                Line("world_without_genus", N0(withoutGenus.Count));
                RunKnown("genus (ungated)", "_genus", pairs.Values.ToList(), known, FamilyWorld.Build(withoutGenus, scopeFamilies), outDir, Log, Line);
            }
            else Console.WriteLine("        no genus above the sample's taxa in UniProt's lineage; the genus run is skipped");

            // ---------------------------------------------------------------- coverage
            Console.WriteLine();
            PeptideFamilyAttribution attribution = FamilyEnvelopeQuant.BuildAttributionFromCuration(quant, sample);
            FamilyEnvelopeQuantResult first = FamilyEnvelopeQuant.BuildFromAttribution(quant, attribution);
            Log("first pass (the project's own database): unattributed de novo reference-calibrated envelope " +
                first.UnattributedDeNovoEnvelopeReferenceCalibrated.ToString("E6", CultureInfo.InvariantCulture) + " = " +
                P(first.MeasuredDeNovoEnvelopeReferenceCalibrated <= 0 ? 0 : first.UnattributedDeNovoEnvelopeReferenceCalibrated / first.MeasuredDeNovoEnvelopeReferenceCalibrated) +
                " of the de novo envelope, " + N(first.UnattributedDeNovoPeptides) + " peptides");

            // The same walk BuildFromPeptides makes, so the residual is the production one to the last bit.
            List<NativePeptideQuant> peptides = FamilyEnvelopeQuant.PreparePeptides(quant);
            Dictionary<string, List<NativePeptideQuant>> identifiedByKey = peptides
                .Where(p => string.Equals(p.Side, NativeQuantFractions.SideIdentified, StringComparison.Ordinal))
                .GroupBy(NativeQuantFractions.CrossSideKey, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
            var residual = new List<(NativePeptideQuant Peptide, string Sequence)>();
            double residualEnvelope = 0;
            foreach (NativePeptideQuant p in peptides)
            {
                if (string.Equals(p.Side, NativeQuantFractions.SideIdentified, StringComparison.Ordinal)) continue;
                if (identifiedByKey.TryGetValue(NativeQuantFractions.CrossSideKey(p), out List<NativePeptideQuant>? candidates)
                    && candidates.Any(q => NativeQuantFractions.IsCrossSideEquivalent(q, p))) continue;
                string normalized = DeNovoReconciler.NormalizeSequence(p.Sequence ?? string.Empty);
                if (attribution.Resolve(NativeQuantFractions.SideDeNovo, normalized).Count > 0) continue;
                residual.Add((p, normalized));
                residualEnvelope += p.ReferenceCalibratedEnvelope;
            }
            check("family world: the residual walked here is the production unattributed de novo envelope, bit for bit",
                residualEnvelope.Equals(first.UnattributedDeNovoEnvelopeReferenceCalibrated) && residual.Count == first.UnattributedDeNovoPeptides,
                R(residualEnvelope) + " vs " + R(first.UnattributedDeNovoEnvelopeReferenceCalibrated));

            // The quantitation seam must hand the second pass exactly this residual, in this order.
            FamilyPeptideAttribution firstPass = FamilyEnvelopeQuant.AttributePeptides(peptides, attribution);
            check("family world: the quantitation seam's unattributed de novo peptides are the walked residual, in order",
                firstPass.UnattributedDeNovoPeptides.Count == residual.Count
                && firstPass.UnattributedDeNovoPeptides.Select((p, i) => ReferenceEquals(p, residual[i].Peptide)).All(same => same),
                N(firstPass.UnattributedDeNovoPeptides.Count) + " vs " + N(residual.Count));

            // B1's coverage through the product pass itself (FamilyWorldPass), under B1's settings; the tally below is
            // the checker's own and must agree with it to the last bit.
            FamilyWorld coverageWorld = FamilyWorld.Build(all, scopeFamilies);
            Log("whole world: " + N(coverageWorld.Proteins.Count) + " targets and as many reversed copies; searching " +
                N(residual.Select(r => r.Sequence).Where(s => s.Length > 0).Distinct(StringComparer.Ordinal).Count()) + " sequences under B1's settings");
            FamilyWorldPassResult b1Pass = FamilyWorldPass.Run(firstPass, coverageWorld, B1Options(coverageWorld));
            Log("residual searched");
            Dictionary<string, FamilyWorldCredit> residualCredit = b1Pass.Sequences.ToDictionary(s => s.Sequence, s => s.Credit, StringComparer.Ordinal);
            FamilyWorldCredit CreditOf(string sequence) => residualCredit[sequence];

            var envelopeBy = new Dictionary<FamilyWorldOutcome, double>();
            var peptidesBy = new Dictionary<FamilyWorldOutcome, int>();
            var coverageBy = new SortedDictionary<string, CoverageTally>(StringComparer.OrdinalIgnoreCase);
            CoverageTally Cover(string f) => coverageBy.TryGetValue(f, out CoverageTally? t) ? t : coverageBy[f] = new CoverageTally();
            var bySequence = new SortedDictionary<string, (double Envelope, int Peptides)>(StringComparer.Ordinal);
            double creditedOneFamily = 0;
            foreach (var (p, sequence) in residual)
            {
                FamilyWorldCredit credit = CreditOf(sequence);
                envelopeBy[credit.Outcome] = envelopeBy.GetValueOrDefault(credit.Outcome) + p.ReferenceCalibratedEnvelope;
                peptidesBy[credit.Outcome] = peptidesBy.GetValueOrDefault(credit.Outcome) + 1;
                if (credit.Families.Count == 1) creditedOneFamily += p.ReferenceCalibratedEnvelope;
                foreach (string f in credit.Families)
                {
                    CoverageTally t = Cover(f);
                    t.Envelope += p.ReferenceCalibratedEnvelope;
                    t.Peptides++;
                    if (credit.Families.Count == 1) t.UniqueEnvelope += p.ReferenceCalibratedEnvelope;
                }
                foreach (string f in credit.DecoySourceFamilies) Cover(f).DecoyEnvelope += p.ReferenceCalibratedEnvelope;
                var s = bySequence.GetValueOrDefault(sequence);
                bySequence[sequence] = (s.Envelope + p.ReferenceCalibratedEnvelope, s.Peptides + 1);
            }
            double credited = envelopeBy.GetValueOrDefault(FamilyWorldOutcome.Credited);
            double decoy = envelopeBy.GetValueOrDefault(FamilyWorldOutcome.Decoy);
            double landedFraction = residualEnvelope <= 0 ? 0 : credited / residualEnvelope;
            double decoyRate = credited <= 0 ? double.PositiveInfinity : decoy / credited;
            double closure = envelopeBy.Values.Sum();
            check("family world: the outcomes close on the residual",
                Math.Abs(closure - residualEnvelope) <= 1e-9 * Math.Max(1, residualEnvelope) && peptidesBy.Values.Sum() == residual.Count, R(closure));
            check("family world: the product pass gives B1's checker the same numbers, bit for bit",
                b1Pass.ResidualEnvelope.Equals(residualEnvelope) && b1Pass.ResidualPeptides == residual.Count
                && Enum.GetValues<FamilyWorldOutcome>().All(o => b1Pass.Envelope(o).Equals(envelopeBy.GetValueOrDefault(o))
                    && b1Pass.Peptides(o) == peptidesBy.GetValueOrDefault(o))
                && b1Pass.Families.Count == coverageBy.Count
                && b1Pass.Families.All(f => coverageBy.TryGetValue(f.Family, out CoverageTally? t) && t.Envelope.Equals(f.CreditedEnvelope)
                    && t.UniqueEnvelope.Equals(f.UniqueEnvelope) && t.Peptides == f.Peptides && t.DecoyEnvelope.Equals(f.DecoyEnvelope)),
                N(b1Pass.Families.Count) + " families");

            Console.WriteLine("        COVERAGE (the unattributed de novo reference-calibrated envelope, " + N(residual.Count) + " peptides)");
            foreach (FamilyWorldOutcome o in Enum.GetValues<FamilyWorldOutcome>())
                Console.WriteLine("          " + o + ": " + P(residualEnvelope <= 0 ? 0 : envelopeBy.GetValueOrDefault(o) / residualEnvelope) +
                    " of the residual, " + N(peptidesBy.GetValueOrDefault(o)) + " peptides");
            Console.WriteLine("          landed on a family: " + P(landedFraction) + "; decoys as a share of it: " + P(decoyRate) +
                " (as a share of every target landing: " + P(decoy / Math.Max(double.Epsilon, credited + envelopeBy.GetValueOrDefault(FamilyWorldOutcome.LandedNoFamily))) + ")");
            Console.WriteLine("          landed on exactly one family: " + P(residualEnvelope <= 0 ? 0 : creditedOneFamily / residualEnvelope) +
                "; on several: " + P(residualEnvelope <= 0 ? 0 : (credited - creditedOneFamily) / residualEnvelope));
            Console.WriteLine("          by family (credited envelope, count in every family; unique; decoys won against its reversed copies):");
            foreach (var (family, t) in coverageBy.OrderByDescending(kv => kv.Value.Envelope).ThenBy(kv => kv.Key, StringComparer.Ordinal).Take(25))
                Console.WriteLine("            " + family + ": " + P(t.Envelope / residualEnvelope) + ", unique " + P(t.UniqueEnvelope / residualEnvelope) +
                    ", " + N(t.Peptides) + " peptides, decoys " + P(t.DecoyEnvelope / residualEnvelope));

            FamilyWorldVerdict verdict = Verdict(rightRate, landedFraction, decoyRate);
            Console.WriteLine();
            Console.WriteLine("        B1 VERDICT: " + verdict.ToString().ToUpperInvariant() + " (right " + P(rightRate) + ", landed " + P(landedFraction) +
                ", decoys " + P(decoyRate) + "; PASS needs >= " + P(PassRightRate) + ", >= " + P(PassLandedFraction) + ", <= " + P(PassDecoyRate) +
                "; KILL at < " + P(KillRightRate) + ", < " + P(KillLandedFraction) + " or > " + P(KillDecoyRate) + ")");

            // ---------------------------------------------------------------- files
            Line("residual_envelope_reference_calibrated", R(residualEnvelope));
            Line("residual_peptides", N0(residual.Count));
            foreach (FamilyWorldOutcome o in Enum.GetValues<FamilyWorldOutcome>()) Line("residual_" + o + "_envelope", R(envelopeBy.GetValueOrDefault(o)));
            Line("residual_credited_one_family_envelope", R(creditedOneFamily));
            Line("landed_fraction", R(landedFraction));
            Line("decoy_rate", R(decoyRate));
            Line("right_rate", R(rightRate));
            Line("verdict", verdict.ToString().ToUpperInvariant());

            var coverageCsv = new StringBuilder("family,credited_envelope,unique_envelope,peptides,decoy_envelope,share_of_residual\n");
            foreach (var (family, t) in coverageBy)
                coverageCsv.Append(Csv(family, R(t.Envelope), R(t.UniqueEnvelope), N0(t.Peptides), R(t.DecoyEnvelope), R(t.Envelope / residualEnvelope)));
            Write(outDir, "family_world_coverage_by_family.csv", coverageCsv.ToString());

            var sequenceCsv = new StringBuilder("sequence,peptides,envelope,outcome,credited_families,decoy_source_families\n");
            foreach (var (sequence, s) in bySequence)
            {
                FamilyWorldCredit credit = CreditOf(sequence);
                sequenceCsv.Append(Csv(sequence, N0(s.Peptides), R(s.Envelope), credit.Outcome.ToString(), FamilyWorld.Join(credit.Families),
                    FamilyWorld.Join(credit.DecoySourceFamilies)));
            }
            Write(outDir, "family_world_coverage_sequences.csv", sequenceCsv.ToString());

            // ---------------------------------------------------------------- gate study
            // BETWEEN asks for a study of the gate before building. One candidate rule, measured and never applied: a
            // minimum tag length for the cross-species pass. A shorter tag is simply not searched, so every row reads off
            // the searches above. The known answers are split in two before anything is looked at, by a hash of the
            // identified peptide (all tags of one peptide stay together; the outcome plays no part): the first half makes
            // the table and the second is reported beside it. The verdict above stays the only one; the choice is Paulo's.
            Dictionary<string, FamilyWorldCredit> tagCredits = speciesRun.Credits;
            int HalfOf(KnownPair p) => System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(p.Identified))[0] & 1;
            bool StudyLanded(KnownPair p) => tagCredits[p.Tag].Outcome == FamilyWorldOutcome.Credited;
            bool StudyRight(KnownPair p) => FamilyWorld.IsRight(tagCredits[p.Tag], p.Known);
            List<KnownPair>[] halves = { known.Where(p => HalfOf(p) == 0).ToList(), known.Where(p => HalfOf(p) == 1).ToList() };
            check("family world study: the two halves partition the known answers and share no identified peptide",
                halves[0].Count + halves[1].Count == known.Count
                && !halves[0].Select(p => p.Identified).Intersect(halves[1].Select(p => p.Identified), StringComparer.Ordinal).Any(),
                N(halves[0].Count) + " + " + N(halves[1].Count));
            SortedSet<string> knownFamilies = FamilyWorld.NewFamilySet();
            foreach (KnownPair p in known) knownFamilies.UnionWith(p.Known);
            var panelFamilies = new HashSet<string>(worldFile.EvidenceSource?.ReferencePanel?.Families ?? new List<string>(), StringComparer.OrdinalIgnoreCase);

            Console.WriteLine();
            Console.WriteLine("        GATE STUDY, measured and not applied: a minimum tag length for the cross-species pass (species out)");
            Console.WriteLine("          halves by a hash of the identified peptide: table half " + N(halves[0].Count) + " pairs (" +
                N(halves[0].Select(p => p.Identified).Distinct().Count()) + " peptides), held-out half " + N(halves[1].Count) + " pairs (" +
                N(halves[1].Select(p => p.Identified).Distinct().Count()) + " peptides)");
            Console.WriteLine("          bars as fixed: right >= 95% pass, < 85% kill; landed >= 20% pass, < 5% kill; decoys <= 5% pass, > 20% kill");
            Console.WriteLine("          minimum | table half: landed, right | held-out half: landed, right | residual landed | decoys of landed | credits to families with no known answer");
            var studyCsv = new StringBuilder("minimum_tag_length,table_landed,table_right,table_right_rate,heldout_landed,heldout_right,heldout_right_rate," +
                "residual_landed_fraction,residual_decoy_rate,unknown_family_credits,unknown_families,unknown_family_credits_outside_panel_families\n");
            var studyFamilyCsv = new StringBuilder("minimum_tag_length,half,family,known_pairs,landed,right,credited,credited_true\n");
            var familyRates = new SortedDictionary<string, string?[]>(StringComparer.OrdinalIgnoreCase);
            const int FirstMinimum = 7, LastMinimum = 15;
            for (int minimum = FirstMinimum; minimum <= LastMinimum; minimum++)
            {
                var landedRight = new (int Landed, int Right)[2];
                for (int h = 0; h < 2; h++)
                {
                    List<KnownPair> kept = halves[h].Where(p => p.Tag.Length >= minimum).ToList();
                    landedRight[h] = (kept.Count(StudyLanded), kept.Count(StudyRight));
                    var tally = new SortedDictionary<string, FamilyTally>(StringComparer.OrdinalIgnoreCase);
                    FamilyTally TallyOf(string f) => tally.TryGetValue(f, out FamilyTally? t) ? t : tally[f] = new FamilyTally();
                    foreach (KnownPair pair in kept)
                    {
                        FamilyWorldCredit credit = tagCredits[pair.Tag];
                        bool isRight = StudyRight(pair);
                        foreach (string f in pair.Known)
                        {
                            FamilyTally t = TallyOf(f);
                            t.Known++;
                            if (credit.Outcome == FamilyWorldOutcome.Credited) t.KnownLanded++;
                            if (isRight) t.KnownRight++;
                        }
                        foreach (string f in credit.Families)
                        {
                            FamilyTally t = TallyOf(f);
                            t.Credited++;
                            if (pair.Known.Contains(f)) t.CreditedTrue++;
                        }
                    }
                    foreach (var (family, t) in tally)
                    {
                        studyFamilyCsv.Append(Csv(N0(minimum), h == 0 ? "table" : "heldout", family, N0(t.Known), N0(t.KnownLanded),
                            N0(t.KnownRight), N0(t.Credited), N0(t.CreditedTrue)));
                        if (!knownFamilies.Contains(family)) continue;
                        if (!familyRates.TryGetValue(family, out string?[]? cells))
                            familyRates[family] = cells = new string?[2 * (LastMinimum - FirstMinimum + 1)];
                        cells[2 * (minimum - FirstMinimum) + h] = t.KnownLanded == 0 ? null
                            : (100.0 * t.KnownRight / t.KnownLanded).ToString("F0", CultureInfo.InvariantCulture);
                    }
                }

                // The families no known answer vouches for: every credit to them is wrong by construction.
                List<string> foreign = known.Where(p => p.Tag.Length >= minimum && StudyLanded(p))
                    .SelectMany(p => tagCredits[p.Tag].Families).Where(f => !knownFamilies.Contains(f)).ToList();
                int foreignFamilies = foreign.Distinct(StringComparer.OrdinalIgnoreCase).Count();
                int foreignOutsidePanel = foreign.Count(f => !panelFamilies.Contains(f));
                double landedEnvelope = 0, decoyEnvelope = 0;
                foreach (var (peptide, sequence) in residual)
                {
                    if (sequence.Length < minimum) continue;
                    FamilyWorldOutcome o = CreditOf(sequence).Outcome;
                    if (o == FamilyWorldOutcome.Credited) landedEnvelope += peptide.ReferenceCalibratedEnvelope;
                    else if (o == FamilyWorldOutcome.Decoy) decoyEnvelope += peptide.ReferenceCalibratedEnvelope;
                }
                static double RateOf((int Landed, int Right) x) => x.Landed == 0 ? 0 : x.Right / (double)x.Landed;
                double studyLanded = residualEnvelope <= 0 ? 0 : landedEnvelope / residualEnvelope;
                double studyDecoys = landedEnvelope <= 0 ? double.PositiveInfinity : decoyEnvelope / landedEnvelope;
                Console.WriteLine("          " + minimum.ToString(CultureInfo.InvariantCulture).PadLeft(7) +
                    " | " + N(landedRight[0].Landed) + ", " + P(RateOf(landedRight[0])) +
                    " | " + N(landedRight[1].Landed) + ", " + P(RateOf(landedRight[1])) +
                    " | " + P(studyLanded) + " | " + P(studyDecoys) + " | " + N(foreign.Count) + " to " + N(foreignFamilies) +
                    " families, " + N(foreignOutsidePanel) + " of them outside the panel's families");
                studyCsv.Append(Csv(N0(minimum), N0(landedRight[0].Landed), N0(landedRight[0].Right), R(RateOf(landedRight[0])),
                    N0(landedRight[1].Landed), N0(landedRight[1].Right), R(RateOf(landedRight[1])), R(studyLanded), R(studyDecoys),
                    N0(foreign.Count), N0(foreignFamilies), N0(foreignOutsidePanel)));
            }

            Console.WriteLine("          by family with at least 10 known pairs, right among landed in %, table half/held-out half (- = none landed):");
            Console.WriteLine("            " + "minimum tag length".PadRight(44) + string.Concat(Enumerable.Range(FirstMinimum, LastMinimum - FirstMinimum + 1)
                .Select(m => m.ToString(CultureInfo.InvariantCulture).PadLeft(9))));
            foreach (var (family, cells) in familyRates
                         .Select(kv => (Family: kv.Key, Cells: kv.Value, Known: known.Count(p => p.Known.Contains(kv.Key))))
                         .Where(x => x.Known >= 10)
                         .OrderByDescending(x => x.Known).ThenBy(x => x.Family, StringComparer.Ordinal)
                         .Select(x => (x.Family, x.Cells)))
            {
                string name = family.Length > 42 ? family.Substring(0, 42) : family;
                Console.WriteLine("            " + name.PadRight(44) + string.Concat(Enumerable.Range(0, LastMinimum - FirstMinimum + 1)
                    .Select(i => ((cells[2 * i] ?? "-") + "/" + (cells[2 * i + 1] ?? "-")).PadLeft(9))));
            }
            Write(outDir, "family_world_gate_study.csv", studyCsv.ToString());
            Write(outDir, "family_world_gate_study_by_family.csv", studyFamilyCsv.ToString());

            // ---------------------------------------------------------------- the product pass under D11
            // What B5 ships: a tag needs at least 9 residues and only the families in scope can be credited. The
            // coverage is the product pass itself. The known answers are credited again under the same rule from the
            // searches above, because a search does not depend on the rule that reads it.
            var d11 = new FamilyWorldOptions();
            Console.WriteLine();
            Log("product pass (D11): tags of at least " + N(d11.MinimumTagLength) + " residues, " + N(coverageWorld.ScopeFamilies.Count) +
                " families in scope; searching");
            FamilyWorldPassResult pass = FamilyWorldPass.Run(firstPass, coverageWorld, d11);
            Log("product pass searched");
            Console.WriteLine("        PRODUCT PASS (D11) on the unattributed de novo envelope (" + FamilyWorldPass.RuleText(pass) + ")");
            foreach (FamilyWorldOutcome o in Enum.GetValues<FamilyWorldOutcome>())
                Console.WriteLine("          " + o + ": " + P(pass.ResidualEnvelope <= 0 ? 0 : pass.Envelope(o) / pass.ResidualEnvelope) +
                    " of the residual, " + N(pass.Peptides(o)) + " peptides");
            Console.WriteLine("          landed on a family in scope: " + P(pass.LandedFraction) + "; decoys as a share of it: " + P(pass.DecoyRate) +
                "; shared between families: " + P(pass.SharedEnvelope / pass.ResidualEnvelope) + "; credited although an out-of-scope family also matched: " +
                P(pass.AlsoOutOfScopeEnvelope / pass.ResidualEnvelope));
            Console.WriteLine("          by family (its own peptides; every peptide reaching it; decoys won against its reversed copies):");
            foreach (FamilyWorldFamilyRow f in pass.Families.Take(20))
                Console.WriteLine("            " + f.Family + ": " + P(f.UniqueEnvelope / pass.ResidualEnvelope) + "; " + P(f.CreditedEnvelope / pass.ResidualEnvelope) +
                    "; " + N(f.Peptides) + " peptides; decoys " + P(f.DecoyEnvelope / pass.ResidualEnvelope));

            double passClosure = pass.Families.Sum(f => f.UniqueEnvelope) + pass.SharedEnvelope
                + Enum.GetValues<FamilyWorldOutcome>().Where(o => o != FamilyWorldOutcome.Credited).Sum(o => pass.Envelope(o));
            check("family world: the product pass's family parts, shared row and other outcomes close on the residual",
                Math.Abs(passClosure - pass.ResidualEnvelope) <= 1e-9 * Math.Max(1, pass.ResidualEnvelope) && pass.ResidualEnvelope.Equals(residualEnvelope),
                R(passClosure) + " vs " + R(pass.ResidualEnvelope));
            check("family world: the product pass credits families in scope only and searches no tag under the minimum",
                pass.Families.All(f => coverageWorld.ScopeFamilies.Contains(f.Family, StringComparer.OrdinalIgnoreCase))
                && pass.Peptides(FamilyWorldOutcome.TooShort) == residual.Count(r => r.Sequence.Length > 0 && r.Sequence.Length < d11.MinimumTagLength),
                N(pass.Families.Count) + " families");
            // With every family creditable, the pass at 9 residues is the study's row at 9: what D11's scope rule
            // takes away is exactly what it moves to "out of scope".
            double study9Landed = 0, study9Decoys = 0;
            foreach (var (peptide, sequence) in residual)
            {
                if (sequence.Length < d11.MinimumTagLength) continue;
                FamilyWorldOutcome o = CreditOf(sequence).Outcome;
                if (o == FamilyWorldOutcome.Credited) study9Landed += peptide.ReferenceCalibratedEnvelope;
                else if (o == FamilyWorldOutcome.Decoy) study9Decoys += peptide.ReferenceCalibratedEnvelope;
            }
            check("family world: the product pass agrees with the study's 9-residue row once the scope rule is undone",
                Math.Abs(pass.Envelope(FamilyWorldOutcome.Credited) + pass.Envelope(FamilyWorldOutcome.OutOfScope) - study9Landed) <= 1e-9 * Math.Max(1, study9Landed)
                && Math.Abs(pass.Envelope(FamilyWorldOutcome.Decoy) - study9Decoys) <= 1e-9 * Math.Max(1, study9Decoys),
                R(pass.Envelope(FamilyWorldOutcome.Credited) + pass.Envelope(FamilyWorldOutcome.OutOfScope)) + " vs " + R(study9Landed));
            Write(outDir, "family_world_pass.csv", FamilyWorldPass.BuildCsv(pass));
            Line("d11_minimum_tag_length", N0(pass.MinimumTagLength));
            Line("d11_families_in_scope", N0(pass.CreditableFamilies.Count));
            Line("d11_world_fingerprint", pass.WorldFingerprint);
            foreach (FamilyWorldOutcome o in Enum.GetValues<FamilyWorldOutcome>()) Line("d11_residual_" + o + "_envelope", R(pass.Envelope(o)));
            Line("d11_shared_envelope", R(pass.SharedEnvelope));
            Line("d11_landed_fraction", R(pass.LandedFraction));
            Line("d11_decoy_rate", R(pass.DecoyRate));

            Dictionary<string, FamilyWorldCredit> d11Credits = speciesRun.Results.ToDictionary(kv => kv.Key,
                kv => knownWorld.Credit(kv.Value, knownWorld.ScopeFamilies, d11.MinimumTagLength), StringComparer.Ordinal);
            bool D11Landed(KnownPair p) => d11Credits[p.Tag].Outcome == FamilyWorldOutcome.Credited;
            bool D11Right(KnownPair p) => FamilyWorld.IsRight(d11Credits[p.Tag], p.Known);
            string D11Rate(IEnumerable<KnownPair> set)
            {
                List<KnownPair> landedSet = set.Where(D11Landed).ToList();
                int rightCount = landedSet.Count(D11Right);
                return N(rightCount) + " of " + N(landedSet.Count) + (landedSet.Count > 0 ? " (" + P(rightCount / (double)landedSet.Count) + ")" : "");
            }
            Console.WriteLine("        KNOWN ANSWERS UNDER D11, world without the species (" + N(known.Count) + " pairs)");
            foreach (FamilyWorldOutcome o in Enum.GetValues<FamilyWorldOutcome>())
                Console.WriteLine("          " + o + ": " + N(known.Count(p => d11Credits[p.Tag].Outcome == o)));
            Console.WriteLine("          right among landed: pooled " + D11Rate(known) + "; table half " + D11Rate(halves[0]) + "; held-out half " + D11Rate(halves[1]));
            List<string> d11Foreign = known.Where(D11Landed).SelectMany(p => d11Credits[p.Tag].Families).Where(f => !knownFamilies.Contains(f)).ToList();
            Console.WriteLine("          credits to families with no known answer: " + N(d11Foreign.Count) + " to " +
                N(d11Foreign.Distinct(StringComparer.OrdinalIgnoreCase).Count()) + " families");
            Console.WriteLine("          by family with at least 10 known pairs (right among landed, pooled):");
            foreach (string family in knownFamilies.Where(f => known.Count(p => p.Known.Contains(f)) >= 10)
                         .OrderByDescending(f => known.Count(p => p.Known.Contains(f))).ThenBy(f => f, StringComparer.Ordinal))
                Console.WriteLine("            " + family + ": " + D11Rate(known.Where(p => p.Known.Contains(family))));
            int d11Landed = known.Count(D11Landed), d11Right = known.Count(D11Right);
            Line("d11_known_landed", N0(d11Landed));
            Line("d11_known_right", N0(d11Right));
            Line("d11_known_right_rate", R(d11Landed == 0 ? 0 : d11Right / (double)d11Landed));
            Line("d11_known_unknown_family_credits", N0(d11Foreign.Count));
            var d11Csv = new StringBuilder("tag,identified,half,known_families,outcome,credited_families,out_of_scope_families,right\n");
            foreach (KnownPair p in known)
            {
                FamilyWorldCredit credit = d11Credits[p.Tag];
                d11Csv.Append(Csv(p.Tag, p.Identified, HalfOf(p) == 0 ? "table" : "heldout", FamilyWorld.Join(p.Known), credit.Outcome.ToString(),
                    FamilyWorld.Join(credit.Families), FamilyWorld.Join(credit.OutOfScopeFamilies), D11Right(p) ? "1" : "0"));
            }
            Write(outDir, "family_world_known_answers_d11.csv", d11Csv.ToString());
            Write(outDir, "family_world_summary.tsv", summary.ToString());
            Log("written to " + outDir);
        }

        /// <summary>One known-answer run against one world, under B1's settings. Returns the right rate among the
        /// landed pairs, the credit of every tag and its search result.</summary>
        private static (double RightRate, Dictionary<string, FamilyWorldCredit> Credits, Dictionary<string, HomologyResult> Results) RunKnown(string label, string suffix, List<KnownPair> pairs, List<KnownPair> known, FamilyWorld world,
            string outDir, Action<string> log, Action<string, string> line)
        {
            List<string> tags = known.Select(p => p.Tag).Distinct(StringComparer.Ordinal).ToList();
            log("world without the " + label + ": " + N(world.Proteins.Count) + " targets and as many reversed copies; searching " + N(tags.Count) + " tags");
            Dictionary<string, HomologyResult> results = HomologyMapper.Search(tags, world.Database, B1Options(world).SearchOptions())
                .ToDictionary(r => r.IsRejected ? r.Peptide : r.NormalizedPeptide, StringComparer.Ordinal);
            Dictionary<string, FamilyWorldCredit> credits = results.ToDictionary(kv => kv.Key,
                kv => world.Credit(kv.Value, world.Families, 0), StringComparer.Ordinal);
            log("tags searched");
            FamilyWorldCredit CreditOf(KnownPair p) => credits[p.Tag];
            bool Landed(KnownPair p) => CreditOf(p).Outcome == FamilyWorldOutcome.Credited;
            bool Right(KnownPair p) => FamilyWorld.IsRight(CreditOf(p), p.Known);

            int landed = known.Count(Landed);
            int right = known.Count(Right);
            double rightRate = landed == 0 ? 0 : right / (double)landed;
            string Rate(IEnumerable<KnownPair> set)
            {
                List<KnownPair> l = set.Where(Landed).ToList();
                int r = l.Count(Right);
                return "right " + N(r) + " of " + N(l.Count) + (l.Count > 0 ? " (" + P(r / (double)l.Count) + ")" : "");
            }

            string prefix = suffix.Length == 0 ? "known_" : "known" + suffix + "_";
            Console.WriteLine();
            Console.WriteLine("        KNOWN ANSWERS, world without the " + label + " (" + N(known.Count) + " pairs)");
            foreach (FamilyWorldOutcome o in Enum.GetValues<FamilyWorldOutcome>())
            {
                int count = known.Count(p => CreditOf(p).Outcome == o);
                Console.WriteLine("          " + o + ": " + N(count));
                line(prefix + o, N0(count));
            }
            Console.WriteLine("          right family among landed: " + N(right) + " of " + N(landed) + " = " + P(rightRate) +
                " (the known family among the credited ones: " + N(known.Count(p => Landed(p) && CreditOf(p).Families.Overlaps(p.Known))) + ")");
            line(prefix + "landed", N0(landed));
            line(prefix + "right", N0(right));
            line(prefix + "right_rate", R(rightRate));

            // Diagnostics, never part of the verdict: the rate weighted by calls, and split by what the tag is.
            int callsLanded = known.Where(Landed).Sum(p => p.Calls);
            int callsRight = known.Where(Right).Sum(p => p.Calls);
            Console.WriteLine("          weighted by calls: right " + N(callsRight) + " of " + N(callsLanded) +
                (callsLanded > 0 ? " (" + P(callsRight / (double)callsLanded) + ")" : ""));
            Console.WriteLine("          tag equal to the identified peptide: " + Rate(known.Where(p => p.Exact)) + "; different: " + Rate(known.Where(p => !p.Exact)));
            Console.WriteLine("          identified peptide also held by a protein with no UniProt family: " + Rate(known.Where(p => p.UnlabelledHolders > 0)) +
                "; not: " + Rate(known.Where(p => p.UnlabelledHolders == 0)));
            Console.WriteLine("          by tag length:");
            foreach (var group in known.GroupBy(p => LengthBin(p.Tag.Length)).OrderBy(g => g.Key.Order))
                Console.WriteLine("            " + group.Key.Name + ": " + N(group.Count()) + " pairs, " + Rate(group));

            // By family, both ways: of the pairs whose known family is F, how many came back right; of the
            // pairs credited to F, how many truly were F. The second is the inflation a conserved family causes.
            var byFamily = new SortedDictionary<string, FamilyTally>(StringComparer.OrdinalIgnoreCase);
            FamilyTally Tally(string f) => byFamily.TryGetValue(f, out FamilyTally? t) ? t : byFamily[f] = new FamilyTally();
            foreach (KnownPair pair in known)
            {
                FamilyWorldCredit credit = CreditOf(pair);
                bool isRight = Right(pair);
                foreach (string f in pair.Known)
                {
                    FamilyTally t = Tally(f);
                    t.Known++;
                    if (credit.Outcome == FamilyWorldOutcome.Credited) t.KnownLanded++;
                    if (isRight) t.KnownRight++;
                    if (credit.Outcome == FamilyWorldOutcome.Decoy) t.KnownDecoy++;
                }
                foreach (string f in credit.Families)
                {
                    FamilyTally t = Tally(f);
                    t.Credited++;
                    if (pair.Known.Contains(f)) t.CreditedTrue++;
                }
            }
            Console.WriteLine("          by family (known: landed, right | credited: of them truly that family), families with a known answer:");
            foreach (var (family, t) in byFamily.Where(kv => kv.Value.Known > 0)
                         .OrderByDescending(kv => kv.Value.Known).ThenBy(kv => kv.Key, StringComparer.Ordinal))
                Console.WriteLine("            " + family + ": known " + N(t.Known) + ", landed " + N(t.KnownLanded) + ", right " + N(t.KnownRight) +
                    (t.KnownLanded > 0 ? " (" + P(t.KnownRight / (double)t.KnownLanded) + ")" : "") + " | credited " + N(t.Credited) +
                    ", true " + N(t.CreditedTrue) + (t.Credited > 0 ? " (" + P(t.CreditedTrue / (double)t.Credited) + ")" : ""));
            List<KeyValuePair<string, FamilyTally>> foreign = byFamily.Where(kv => kv.Value.Known == 0).ToList();
            Console.WriteLine("          families credited with no known answer of their own: " + N(foreign.Count) + ", " +
                N(foreign.Sum(kv => kv.Value.Credited)) + " credits, all wrong");

            var pairsCsv = new StringBuilder("tag,identified,calls,sources,exact,exclusion,known_families,unlabelled_holders,outcome,credited_families,decoy_source_families,right\n");
            foreach (KnownPair p in pairs)
            {
                FamilyWorldCredit? credit = p.Exclusion.Length == 0 ? CreditOf(p) : null;
                pairsCsv.Append(Csv(p.Tag, p.Identified, N0(p.Calls), string.Join("+", p.Sources), p.Exact ? "1" : "0",
                    p.Exclusion, FamilyWorld.Join(p.Known), N0(p.UnlabelledHolders),
                    credit?.Outcome.ToString() ?? string.Empty, FamilyWorld.Join(credit?.Families ?? FamilyWorld.NewFamilySet()),
                    FamilyWorld.Join(credit?.DecoySourceFamilies ?? FamilyWorld.NewFamilySet()), credit == null ? string.Empty : Right(p) ? "1" : "0"));
            }
            Write(outDir, "family_world_known_answers" + suffix + ".csv", pairsCsv.ToString());

            var familyCsv = new StringBuilder("family,known_pairs,known_landed,known_right,known_decoy,credited_pairs,credited_true\n");
            foreach (var (family, t) in byFamily)
                familyCsv.Append(Csv(family, N0(t.Known), N0(t.KnownLanded), N0(t.KnownRight), N0(t.KnownDecoy), N0(t.Credited), N0(t.CreditedTrue)));
            Write(outDir, "family_world_known_by_family" + suffix + ".csv", familyCsv.ToString());
            return (rightRate, credits, results);
        }

        private static (int Order, string Name) LengthBin(int length) => length switch
        {
            <= 7 => (0, "7 or fewer"),
            8 => (1, "8"),
            9 => (2, "9"),
            10 => (3, "10"),
            <= 12 => (4, "11-12"),
            <= 15 => (5, "13-15"),
            <= 20 => (6, "16-20"),
            _ => (7, "21 or more"),
        };

        private static string Csv(params string[] fields) => string.Join(",", fields.Select(TestCsvFields.Field)) + "\n";

        private static void Write(string outDir, string name, string text) =>
            File.WriteAllText(Path.Combine(outDir, name), text, new UTF8Encoding(false));

        /// <summary>One UniProt REST answer, with its release. Retried on 429 and 5xx only; anything else, or a
        /// missing release, throws: the sample's relatives are never removed from half a list.</summary>
        private static string Get(HttpClient client, string url, out string release)
        {
            HttpResponseMessage? response = null;
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                response?.Dispose();
                response = client.GetAsync(url).GetAwaiter().GetResult();
                int status = (int)response.StatusCode;
                if (response.IsSuccessStatusCode || (status != 429 && status < 500)) break;
                System.Threading.Thread.Sleep(TimeSpan.FromSeconds(attempt));
            }
            using (response)
            {
                if (!response!.IsSuccessStatusCode)
                    throw new InvalidDataException("UniProt refused " + url + " (HTTP " + ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture) + ").");
                release = response.Headers.TryGetValues("x-uniprot-release", out IEnumerable<string>? values) ? values.FirstOrDefault() ?? string.Empty : string.Empty;
                if (release.Length == 0) throw new InvalidDataException("UniProt omitted its release for " + url + ".");
                return response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            }
        }

        /// <summary>The genus above a taxon in UniProt's taxonomy lineage, or empty when there is none. Kept in the
        /// output folder so a rerun answers offline.</summary>
        private static (string Id, string Name) Genus(HttpClient client, string taxon, string outDir, Action<string> log)
        {
            string cache = Path.Combine(outDir, "family_world_lineage_" + taxon + ".tsv");
            if (File.Exists(cache))
            {
                string[] cached = File.ReadAllLines(cache);
                if (cached.Length >= 2 && cached[0].StartsWith("# release\t", StringComparison.Ordinal))
                {
                    string[] fields = cached[1].Split('\t');
                    if (fields.Length == 2) return (fields[0], fields[1]);
                }
            }
            string json = Get(client, UniProt + "taxonomy/" + Uri.EscapeDataString(taxon) + "?format=json", out string release);
            (string Id, string Name) genus = (string.Empty, string.Empty);
            using (System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(json))
            {
                if (doc.RootElement.TryGetProperty("lineage", out System.Text.Json.JsonElement lineage))
                    foreach (System.Text.Json.JsonElement node in lineage.EnumerateArray())
                        if (node.TryGetProperty("rank", out System.Text.Json.JsonElement rank) && rank.GetString() == "genus")
                            genus = (node.GetProperty("taxonId").GetInt64().ToString(CultureInfo.InvariantCulture),
                                     node.GetProperty("scientificName").GetString() ?? string.Empty);
            }
            File.WriteAllText(cache, "# release\t" + release + "\n" + genus.Id + "\t" + genus.Name + "\n", new UTF8Encoding(false));
            log("lineage of taxon " + taxon + ": genus " + (genus.Id.Length == 0 ? "none" : genus.Id + " " + genus.Name) + ", UniProt " + release);
            return genus;
        }

        /// <summary>Every UniProt entry of a taxon, descendants included: its accessions and the organisms they
        /// come from. Kept in the output folder so a rerun answers offline.</summary>
        private static TaxonMembers Members(HttpClient client, string taxon, string outDir, Action<string> log)
        {
            string cache = Path.Combine(outDir, "family_world_taxon_" + taxon + ".tsv");
            var members = new TaxonMembers();
            if (File.Exists(cache))
            {
                string[] cached = File.ReadAllLines(cache);
                if (cached.Length >= 1 && cached[0].StartsWith("# release\t", StringComparison.Ordinal))
                {
                    members.Release = cached[0].Substring("# release\t".Length);
                    foreach (string row in cached.Skip(1)) members.Add(row);
                    log("taxon " + taxon + " read from " + cache + ": " + N(members.Accessions.Count) + " entries, UniProt " + members.Release);
                    return members;
                }
            }
            string tsv = Get(client, UniProt + "uniprotkb/stream?format=tsv&fields=accession,organism_id&query=" +
                Uri.EscapeDataString("(taxonomy_id:" + taxon + ")"), out string release);
            members.Release = release;
            var rows = new SortedSet<string>(StringComparer.Ordinal);
            foreach (string row in tsv.Split('\n').Skip(1))
                if (row.Trim().Length > 0) rows.Add(row.Trim());
            foreach (string row in rows) members.Add(row);
            File.WriteAllText(cache, "# release\t" + release + "\n" + string.Join("\n", rows) + "\n", new UTF8Encoding(false));
            log("taxon " + taxon + " fetched: " + N(members.Accessions.Count) + " entries from " + N(members.Organisms.Count) + " organisms, UniProt " + release);
            return members;
        }

        private sealed class TaxonMembers
        {
            public string Release { get; set; } = string.Empty;
            public HashSet<string> Accessions { get; } = new HashSet<string>(StringComparer.Ordinal);
            public HashSet<string> Organisms { get; } = new HashSet<string>(StringComparer.Ordinal);

            public void Add(string row)
            {
                string[] fields = row.Split('\t');
                if (fields.Length < 2 || fields[0].Length == 0) return;
                Accessions.Add(FamilyWorld.BaseAccession(fields[0]));
                if (fields[1].Length > 0) Organisms.Add(fields[1]);
            }

            public static TaxonMembers Union(IEnumerable<TaxonMembers> parts)
            {
                var union = new TaxonMembers();
                foreach (TaxonMembers part in parts)
                {
                    if (union.Release.Length > 0 && part.Release != union.Release)
                        throw new InvalidDataException("The UniProt release changed between taxonomy lists.");
                    union.Release = part.Release;
                    union.Accessions.UnionWith(part.Accessions);
                    union.Organisms.UnionWith(part.Organisms);
                }
                return union;
            }
        }

        private sealed class KnownPair
        {
            public string Tag { get; init; } = string.Empty;
            public string Identified { get; init; } = string.Empty;
            public int Calls { get; set; }
            public SortedSet<string> Sources { get; } = new SortedSet<string>(StringComparer.Ordinal);
            public SortedSet<string> Known { get; } = FamilyWorld.NewFamilySet();
            public int UnlabelledHolders { get; set; }
            public string Exclusion { get; set; } = string.Empty;
            public bool Exact => string.Equals(Tag, Identified, StringComparison.Ordinal);
        }

        private sealed class FamilyTally
        {
            public int Known, KnownLanded, KnownRight, KnownDecoy, Credited, CreditedTrue;
        }

        private sealed class CoverageTally
        {
            public double Envelope, UniqueEnvelope, DecoyEnvelope;
            public int Peptides;
        }

        // ---------------------------------------------------------------- fixtures, in the ordinary suite

        /// <summary>The crediting rule and the verdict on a hand-made world, so the gate's arithmetic is pinned
        /// without any data file.</summary>
        internal static void RunFixtures(Action<string, bool, string?> check)
        {
            static ProteinEvidence Family(string family) => new ProteinEvidence { Family = family };
            const string alpha = "MKTAYIAKQRQISFVKSHFSRQLEERLGLIEVQAPILSRVGDGTQDNLSGAEKAVQVKVKALPDAQFEVVHSLAKWKRQTLGQHDFSAGEGLYTHMKALRPDEDRLSPLHSVYVDQWDWERVMGDGERQFSTLKSTVEAIWAGIKATEAAVSEEFGLAPFLPDQIHFVHSQELLSRYPDLDAKGRERAIAKDLGAVFLVGIGGKLSDGHRHDVRAPDYDDWETPA";
            const string beta = "GSHMNEQLRASTWCYPKGDVLAEFRHNWTCQPSVDGKLMYETRHAFNSQWGCVPLDKTRENYMWAHGSFIQKDLVCRTEPGYWNHQAMSFDVKLRETCGYPANWHQ";
            const string orphan = "PPGVWQRCYTHNESKDMFAILPWCGQTRNVHYESDKLMPAFWGCQRTVNHSEYDKLIMPGAWFCQRT";
            var file = new FamilyClusterFile();
            file.MyExtendedFastaItems.Add(new FastaItemExtended { SequenceIdentifier = "sp|P10001|ALPHA_ONE", Description = "Alpha OS=One OX=111", Sequence = alpha, Evidence = Family("alpha family") });
            file.MyExtendedFastaItems.Add(new FastaItemExtended { SequenceIdentifier = "sp|P10002|BETA_ONE", Description = "Beta OS=One OX=111", Sequence = beta, Evidence = Family("beta family") });
            file.MyExtendedFastaItems.Add(new FastaItemExtended { SequenceIdentifier = "tr|A0A001|ORPH_ONE", Description = "Unknown OS=One OX=111", Sequence = orphan });
            file.MyExtendedFastaItems.Add(new FastaItemExtended { SequenceIdentifier = "sp|P10003|ALPHA_TWO", Description = "Alpha OS=Two OX=222", Sequence = "WWWW" + alpha.Substring(20, 60) + "WWWW", Evidence = Family("alpha family") });
            file.MyExtendedFastaItems.Add(new FastaItemExtended { SequenceIdentifier = "sp|P10004|DUAL_TWO", Description = "Dual OS=Two OX=222", Sequence = "YYYY" + alpha.Substring(150, 40) + "YYYY", Evidence = Family("gamma family") });
            file.MyExtendedFastaItems.Add(new FastaItemExtended { SequenceIdentifier = SequenceLabels.DecoyPrefix + "sp|P10001|ALPHA_ONE", Sequence = "AAAA" });
            file.EvidenceSource = new EvidenceProvenance
            {
                ReferencePanel = new ReferencePanel
                {
                    Entries =
                    {
                        new PanelEntry { Accession = "P10002", Sequence = beta, Evidence = Family("beta family") },
                        new PanelEntry { Accession = "Q20001", Sequence = "CCCC" + beta.Substring(10, 50) + "CCCC", Evidence = Family("beta family") },
                    },
                },
            };

            List<FamilyWorldProtein> all = FamilyWorld.Collect(file);
            check("family world fixture: the file's decoys are left out and a panel entry the file holds is not repeated",
                all.Count == 6 && all.Count(p => p.FromPanel) == 1 && all.Single(p => p.FromPanel).Accession == "Q20001", all.Count.ToString(CultureInfo.InvariantCulture));
            check("family world fixture: the taxon comes from OX= and a panel entry has none",
                all.Single(p => p.Accession == "P10003").Taxon == "222" && all.Single(p => p.FromPanel).Taxon.Length == 0, null);

            // B1's settings: every family creditable, no minimum length. The scope (alpha only) is D11's.
            FamilyWorld world = FamilyWorld.Build(all, new[] { "alpha family" });
            HomologyResult Find(FamilyWorld w, string tag) => HomologyMapper.SearchOne(tag, w.Database, new FamilyWorldOptions().SearchOptions());
            FamilyWorldCredit Search(FamilyWorld w, string tag) => w.Credit(Find(w, tag), w.Families, 0);
            FamilyWorldCredit D11(FamilyWorld w, string tag) => w.Credit(Find(w, tag), w.ScopeFamilies, FamilyWorldOptions.DefaultMinimumTagLength);
            string alphaTag = alpha.Substring(30, 12);
            string betaTag = beta.Substring(20, 12);
            string orphanTag = orphan.Substring(10, 12);
            string decoyTag = new string(alpha.Substring(100, 12).Reverse().ToArray());
            FamilyWorldCredit a = Search(world, alphaTag);
            check("family world fixture: a tag of an alpha protein credits the alpha family only",
                a.Outcome == FamilyWorldOutcome.Credited && FamilyWorld.Join(a.Families) == "alpha family", a.Outcome + " " + FamilyWorld.Join(a.Families));
            check("family world fixture: a tag of a protein with no family lands without crediting one",
                Search(world, orphanTag).Outcome == FamilyWorldOutcome.LandedNoFamily, null);
            FamilyWorldCredit d = Search(world, decoyTag);
            check("family world fixture: a reversed tag is won by the decoy, which names its source family",
                d.Outcome == FamilyWorldOutcome.Decoy && FamilyWorld.Join(d.DecoySourceFamilies) == "alpha family", d.Outcome.ToString());
            check("family world fixture: a tag found nowhere does not land", Search(world, "DDDDDDDDDDDD").Outcome == FamilyWorldOutcome.NotLanded, null);
            check("family world fixture: a tag the mapper cannot read is rejected, not dropped", Search(world, "PEP7IDE").Outcome == FamilyWorldOutcome.Rejected, null);

            FamilyWorld withoutOne = FamilyWorld.Build(all.Where(p => p.Taxon != "111"), world.ScopeFamilies);
            FamilyWorldCredit cross = Search(withoutOne, alphaTag);
            check("family world fixture: with its own species out, the alpha tag finds alpha through the other species",
                cross.Outcome == FamilyWorldOutcome.Credited && FamilyWorld.Join(cross.Families) == "alpha family", cross.Outcome.ToString());
            check("family world fixture: with its own species out, a tag of that species' orphan does not land",
                Search(withoutOne, orphanTag).Outcome == FamilyWorldOutcome.NotLanded, null);
            check("family world fixture: the beta tag reaches beta through the panel alone", FamilyWorld.Join(Search(withoutOne, betaTag).Families) == "beta family", null);

            var twoFamilies = new FamilyWorldCredit { Outcome = FamilyWorldOutcome.Credited, Families = { "alpha family", "beta family" } };
            check("family world fixture: right means every credited family is known; a second family makes it wrong",
                FamilyWorld.IsRight(a, new[] { "Alpha family" }) && !FamilyWorld.IsRight(twoFamilies, new[] { "alpha family" })
                && FamilyWorld.IsRight(twoFamilies, new[] { "alpha family", "beta family" }) && !FamilyWorld.IsRight(a, Array.Empty<string>()), null);

            // ---- D11: a tag needs 9 residues, and only families in scope are credited (scope here: alpha)
            check("family world fixture (D11): a 9-residue alpha tag is credited, an 8-residue one is too short",
                D11(world, alpha.Substring(40, 9)).Outcome == FamilyWorldOutcome.Credited
                && D11(world, alpha.Substring(60, 8)).Outcome == FamilyWorldOutcome.TooShort, null);
            FamilyWorldCredit outOfScope = D11(world, betaTag);
            check("family world fixture (D11): a tag landing only on a family out of scope credits nothing and names it",
                outOfScope.Outcome == FamilyWorldOutcome.OutOfScope && outOfScope.Families.Count == 0
                && FamilyWorld.Join(outOfScope.OutOfScopeFamilies) == "beta family", outOfScope.Outcome.ToString());
            string dualTag = alpha.Substring(160, 12);
            FamilyWorldCredit dual = D11(world, dualTag);
            check("family world fixture (D11): a tag reaching alpha and an out-of-scope family credits alpha alone and keeps the other apart",
                dual.Outcome == FamilyWorldOutcome.Credited && FamilyWorld.Join(dual.Families) == "alpha family"
                && FamilyWorld.Join(dual.OutOfScopeFamilies) == "gamma family"
                && FamilyWorld.Join(Search(world, dualTag).Families) == "alpha family; gamma family", FamilyWorld.Join(dual.Families));
            check("family world fixture: a world built from a file takes its panel's families as the scope",
                FamilyWorld.FromFile(new FamilyClusterFile { EvidenceSource = new EvidenceProvenance
                    { ReferencePanel = new ReferencePanel { Families = { "beta family", "Alpha family" } } } }).ScopeFamilies.SequenceEqual(new[] { "Alpha family", "beta family" }), null);

            // ---- the pass itself, on quant rows: first pass on the sample's database, second on the world
            const string sampleProtein = "MHHHHHHWWWWKRRRRMMMMNNNNKPPPPQQQQSSSSTTTTK";
            var sample = new FamilyClusterFile();
            sample.MyExtendedFastaItems.Add(new FastaItemExtended { SequenceIdentifier = "sp|S00001|SAMP_ONE", Description = "Sample OS=One OX=111", Sequence = sampleProtein, UserID = 7 });
            int scan = 0;
            QuantRow Row(string side, string sequence, double m0, double envelope) => new QuantRow
            {
                Side = side, Run = "r1", Scan = ++scan, Charge = 2, Sequence = sequence,
                PrecursorMz = Math.Round(500.0 + scan * 1e-3, 5), IsotopeAreas = new[] { m0, 0.0, 0.0, 0.0 }, EnvelopeSum = envelope,
            };
            var rows = new List<QuantRow>
            {
                Row(NativeQuantFractions.SideIdentified, "HHHHHHWWWWK", 10, 30),
                Row(NativeQuantFractions.SideDeNovo, "RRRRMMMMNNNNK", 4, 9),        // first pass: the sample's own family
                Row(NativeQuantFractions.SideDeNovo, alpha.Substring(40, 9), 5, 11), // credited to alpha
                Row(NativeQuantFractions.SideDeNovo, dualTag, 6, 13),                // credited to alpha, gamma kept apart
                Row(NativeQuantFractions.SideDeNovo, alpha.Substring(60, 8), 7, 15), // too short
                Row(NativeQuantFractions.SideDeNovo, betaTag, 8, 17),                // out of scope
                Row(NativeQuantFractions.SideDeNovo, decoyTag, 9, 19),               // won by a reversed copy
                Row(NativeQuantFractions.SideDeNovo, "DDDDDDDDDDDD", 3, 7),          // not in the world
            };
            FamilyWorldPassResult pass = FamilyWorldPass.Run(rows, sample, world);
            List<NativePeptideQuant> prepared = FamilyEnvelopeQuant.PreparePeptides(rows);
            double EnvelopeOf(params string[] sequences) => prepared.Where(p => p.Side == NativeQuantFractions.SideDeNovo
                && sequences.Select(DeNovoReconciler.NormalizeSequence).Contains(DeNovoReconciler.NormalizeSequence(p.Sequence))).Sum(p => p.ReferenceCalibratedEnvelope);
            string[] residualSequences = { alpha.Substring(40, 9), dualTag, alpha.Substring(60, 8), betaTag, decoyTag, "DDDDDDDDDDDD" };
            check("family world pass fixture: the first pass keeps its own family and the second pass gets the other six de novo peptides",
                pass.ResidualPeptides == 6 && Math.Abs(pass.ResidualEnvelope - EnvelopeOf(residualSequences)) < 1e-9,
                pass.ResidualPeptides.ToString(CultureInfo.InvariantCulture));
            check("family world pass fixture: each peptide lands in its outcome under D11",
                pass.Peptides(FamilyWorldOutcome.Credited) == 2 && pass.Peptides(FamilyWorldOutcome.TooShort) == 1
                && pass.Peptides(FamilyWorldOutcome.OutOfScope) == 1 && pass.Peptides(FamilyWorldOutcome.Decoy) == 1
                && pass.Peptides(FamilyWorldOutcome.NotLanded) == 1 && pass.Peptides(FamilyWorldOutcome.LandedNoFamily) == 0, null);
            FamilyWorldFamilyRow alphaRow = pass.Families.Single();
            check("family world pass fixture: alpha holds both credited peptides, its decoy and nothing of beta or gamma",
                alphaRow.Family == "alpha family" && alphaRow.Peptides == 2 && alphaRow.UniquePeptides == 2
                && Math.Abs(alphaRow.UniqueEnvelope - pass.Envelope(FamilyWorldOutcome.Credited)) < 1e-9
                && Math.Abs(alphaRow.DecoyEnvelope - pass.Envelope(FamilyWorldOutcome.Decoy)) < 1e-9
                && Math.Abs(pass.AlsoOutOfScopeEnvelope - EnvelopeOf(dualTag)) < 1e-9, null);
            FamilyWorldSequenceRow dualRow = pass.Sequences.Single(r => r.Sequence == DeNovoReconciler.NormalizeSequence(dualTag));
            check("family world pass fixture: each tag keeps the world proteins it hit, and a family row only its own",
                alphaRow.Proteins.SequenceEqual(new[] { "sp|P10001|ALPHA_ONE", "sp|P10003|ALPHA_TWO" })
                && dualRow.Credit.Proteins.SequenceEqual(new[] { "sp|P10001|ALPHA_ONE", "sp|P10004|DUAL_TWO" })
                && world.Protein("sp|P10004|DUAL_TWO")?.Sequence.Length == 48 && world.Protein("nothing") == null,
                string.Join(" ", alphaRow.Proteins));
            double closed = pass.Families.Sum(f => f.UniqueEnvelope) + pass.SharedEnvelope
                + Enum.GetValues<FamilyWorldOutcome>().Where(o => o != FamilyWorldOutcome.Credited).Sum(o => pass.Envelope(o));
            check("family world pass fixture: family parts, shared row and the other outcomes close on the residual",
                Math.Abs(closed - pass.ResidualEnvelope) < 1e-9 && pass.MinimumTagLength == 9 && pass.CreditableFamilies.SequenceEqual(new[] { "alpha family" }), null);
            string csv = FamilyWorldPass.BuildCsv(pass);
            string[] csvLines = csv.TrimEnd('\n').Split('\n');
            check("family world pass fixture: the export has one family row, seven closing rows and the rules on every row",
                csvLines.Length == 9 && csvLines.Skip(1).All(l => l.Contains("tags of at least 9 residues", StringComparison.Ordinal)
                    && l.Contains("only families in scope can be credited (1)", StringComparison.Ordinal)), csvLines.Length.ToString(CultureInfo.InvariantCulture));
            CultureInfo culture = CultureInfo.CurrentCulture;
            string again, brazil;
            try
            {
                again = FamilyWorldPass.BuildCsv(FamilyWorldPass.Run(rows, sample, world));
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR");
                brazil = FamilyWorldPass.BuildCsv(FamilyWorldPass.Run(rows, sample, world));
            }
            finally { CultureInfo.CurrentCulture = culture; }
            check("family world pass fixture: two runs give the same export, byte for byte, under pt-BR too", again == csv && brazil == csv, null);
            bool refused = false;
            try { FamilyWorldPass.Run(rows, sample, FamilyWorld.Build(all, Array.Empty<string>())); }
            catch (InvalidOperationException) { refused = true; }
            check("family world pass fixture: a world with no families in scope is refused, never credited to every family", refused, null);
            FamilyWorldPassResult b1 = FamilyWorldPass.Run(rows, sample, world, new FamilyWorldOptions { MinimumTagLength = 0, CreditableFamilies = world.Families });
            check("family world pass fixture: with B1's settings the short tag is searched and beta and gamma are credited",
                b1.Peptides(FamilyWorldOutcome.TooShort) == 0 && b1.Peptides(FamilyWorldOutcome.OutOfScope) == 0
                && b1.Families.Select(f => f.Family).OrderBy(f => f, StringComparer.Ordinal).SequenceEqual(new[] { "alpha family", "beta family", "gamma family" }), null);

            check("family world fixture: the verdict at the roadmap's bars",
                Verdict(0.95, 0.20, 0.05) == FamilyWorldVerdict.Pass
                && Verdict(0.9499, 0.50, 0.01) == FamilyWorldVerdict.Between
                && Verdict(0.99, 0.1999, 0.01) == FamilyWorldVerdict.Between
                && Verdict(0.99, 0.50, 0.0501) == FamilyWorldVerdict.Between
                && Verdict(0.8499, 0.50, 0.01) == FamilyWorldVerdict.Kill
                && Verdict(0.99, 0.0499, 0.01) == FamilyWorldVerdict.Kill
                && Verdict(0.99, 0.50, 0.2001) == FamilyWorldVerdict.Kill
                && Verdict(0.85, 0.05, 0.20) == FamilyWorldVerdict.Between, null);
        }
    }
}
