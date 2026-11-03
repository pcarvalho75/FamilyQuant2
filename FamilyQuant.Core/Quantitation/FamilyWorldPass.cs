using FamilyQuant.DeNovo;
using FamilyQuant.Evidence;
using FamilyQuant.Homology;
using FamilyQuant.Operations;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FamilyQuant
{
    /// <summary>What one de novo sequence found in the family world.</summary>
    public enum FamilyWorldOutcome
    {
        /// <summary>The sequence could not be read (no standard residue left after normalization).</summary>
        Rejected,
        /// <summary>No protein of the world, target or decoy, passed the identity gate.</summary>
        NotLanded,
        /// <summary>A reversed copy scored at least as well as every target (a tie goes to the decoy).</summary>
        Decoy,
        /// <summary>Targets won, but none of them carries a UniProt family.</summary>
        LandedNoFamily,
        /// <summary>Targets won and at least one carries a family in scope: those families are credited.</summary>
        Credited,
        /// <summary>The tag is shorter than the minimum length, so it was not searched (D11).</summary>
        TooShort,
        /// <summary>Targets won, but every family they carry is out of scope, so none is credited (D11).</summary>
        OutOfScope,
    }

    /// <summary>One protein of the family world. Its UniProt families come from its own stored evidence.</summary>
    public sealed class FamilyWorldProtein
    {
        public string Id { get; init; } = string.Empty;
        public string Accession { get; init; } = string.Empty;
        public string Sequence { get; init; } = string.Empty;
        public IReadOnlyList<string> Families { get; init; } = Array.Empty<string>();
        /// <summary>The NCBI taxon read from the header's OX= (or stored by the fetch), empty when unknown.</summary>
        public string Taxon { get; init; } = string.Empty;
        /// <summary>Every direct taxon when a sequence retains joined source headers.</summary>
        public IReadOnlyList<string> Taxa { get; init; } = Array.Empty<string>();
        /// <summary>The taxa above <see cref="Taxon"/>, when the fetch stored them.</summary>
        public IReadOnlyList<string> LineageTaxa { get; init; } = Array.Empty<string>();
        public bool FromPanel { get; init; }
    }

    /// <summary>The families one sequence was credited to, the matched families that were out of scope, the
    /// families whose reversed copies beat it, and the world proteins it hit.</summary>
    public sealed class FamilyWorldCredit
    {
        public FamilyWorldOutcome Outcome { get; init; }
        /// <summary>The target proteins of the world that won, by identifier (<see cref="FamilyWorld.Protein"/>): the
        /// candidates a database could add (B7's suggested additions).</summary>
        public SortedSet<string> Proteins { get; init; } = new SortedSet<string>(StringComparer.Ordinal);
        public SortedSet<string> Families { get; init; } = FamilyWorld.NewFamilySet();
        public SortedSet<string> OutOfScopeFamilies { get; init; } = FamilyWorld.NewFamilySet();
        public SortedSet<string> DecoySourceFamilies { get; init; } = FamilyWorld.NewFamilySet();
    }

    /// <summary>The rules of the second pass. The defaults are Paulo's decisions D11 (scope) and D22 (length).</summary>
    public sealed class FamilyWorldOptions
    {
        /// <summary>D22 (Paulo, 2026-11-02): by default a tag needs at least 8 residues, replacing D11's 9, after the table
        /// by length on both worlds (B10); a person can choose any of <see cref="LengthChoices"/>. The B1 study is in
        /// docs/vision/B1-GATE.md.</summary>
        public const int DefaultMinimumTagLength = 8;

        /// <summary>B10: the minimum tag lengths a person can choose from; Compute stores the complete table for each.</summary>
        public static IReadOnlyList<int> LengthChoices { get; } = Array.AsReadOnly(new[] { 7, 8, 9, 10, 11, 12, 13, 14, 15 });

        public int MinimumTagLength { get; set; } = DefaultMinimumTagLength;

        /// <summary>The families that may be credited. Null takes the families the world was gathered for, its
        /// scope (D11: only families in scope can be credited).</summary>
        public IReadOnlyCollection<string>? CreditableFamilies { get; set; }

        public double MinIdentity { get; set; } = HomologyOptions.DefaultMinIdentity;

        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public HomologyScienceOptions? Science { get; set; }

        /// <summary>The de novo side of the production attribution: every target past the gate after the
        /// target-decoy competition, with no cut at the mapper's top three.</summary>
        public HomologyOptions SearchOptions()
        {
            var science = Science?.AnyEnabled == true ? Science.Clone() : null;
            if (science != null) science.QueryRole = HomologyQueryRole.DeNovo;
            return new HomologyOptions { MinIdentity = MinIdentity, TopHitsPerPeptide = int.MaxValue, Science = science };
        }

        public FamilyWorldOptions Clone() => new FamilyWorldOptions
        {
            MinimumTagLength = MinimumTagLength, CreditableFamilies = CreditableFamilies, MinIdentity = MinIdentity,
            Science = Science?.AnyEnabled == true ? Science.Clone() : null,
        };
    }

    /// <summary>
    /// The family world: proteins of the families in scope, from every species, each labelled with the UniProt
    /// families of its own evidence, searched with its reversed copies as decoys. A hit credits FAMILIES, never a
    /// protein. Nothing here names a family: the labels and the scope come from the data.
    /// </summary>
    public sealed class FamilyWorld
    {
        private static readonly Regex TaxonField = new Regex(@"(?:^|\s)OX=(\d+)", RegexOptions.CultureInvariant);
        private readonly Dictionary<string, FamilyWorldProtein> byId;

        private FamilyWorld(List<FamilyWorldProtein> proteins, HomologyDatabase database, IEnumerable<string> scopeFamilies,
            string? fingerprint = null)
        {
            Proteins = proteins;
            Database = database;
            byId = proteins.ToDictionary(p => p.Id, StringComparer.Ordinal);
            ScopeFamilies = Sorted(scopeFamilies);
            Families = Sorted(proteins.SelectMany(p => p.Families));
            Fingerprint = fingerprint ?? FingerprintOf(proteins);
        }

        public IReadOnlyList<FamilyWorldProtein> Proteins { get; }
        public HomologyDatabase Database { get; }
        /// <summary>The families the world was gathered for; the default of what may be credited.</summary>
        public IReadOnlyList<string> ScopeFamilies { get; }
        /// <summary>Every family any protein of the world carries.</summary>
        public IReadOnlyList<string> Families { get; }
        /// <summary>The world's identity: the stored snapshot's content hash (B4), or else SHA-256 over every target's
        /// identifier, sequence and families, in identifier order.</summary>
        public string Fingerprint { get; }

        /// <summary>A target protein of the world by identifier, or null.</summary>
        public FamilyWorldProtein? Protein(string id) => byId.TryGetValue(id ?? string.Empty, out FamilyWorldProtein? p) ? p : null;

        public static SortedSet<string> NewFamilySet() => new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The OX= taxon of a FASTA description, or empty.</summary>
        public static string TaxonOf(string? description)
        {
            Match m = TaxonField.Match(description ?? string.Empty);
            return m.Success ? m.Groups[1].Value : string.Empty;
        }

        /// <summary>Every distinct positive OX= identifier of a FASTA description, including joined entries.</summary>
        public static IReadOnlyList<string> TaxaOf(string? description) => TaxonField.Matches(description ?? string.Empty)
            .Cast<Match>().Select(match => int.TryParse(match.Groups[1].Value, NumberStyles.None,
                CultureInfo.InvariantCulture, out int id) && id > 0 ? id.ToString(CultureInfo.InvariantCulture) : string.Empty)
            .Where(id => id.Length > 0).Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToArray();

        /// <summary>An accession without its isoform suffix, so P12345-2 and P12345 are one protein.</summary>
        public static string BaseAccession(string accession)
        {
            int dash = accession.IndexOf('-');
            return dash > 0 ? accession.Substring(0, dash) : accession;
        }

        public static IReadOnlyList<string> FamiliesOf(ProteinEvidence? evidence) => evidence == null
            ? Array.Empty<string>()
            : ReferencePanelFetcher.FamiliesOf(evidence).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ThenBy(f => f, StringComparer.Ordinal).ToArray();

        /// <summary>Every protein of a family-world file before any exclusion: the file's entries (decoys left out,
        /// the world gets its own reversed copies), then each panel entry the file does not already hold.</summary>
        public static List<FamilyWorldProtein> Collect(FamilyClusterFile file)
        {
            if (file == null) throw new ArgumentNullException(nameof(file));
            var proteins = new List<FamilyWorldProtein>();
            var accessions = new HashSet<string>(StringComparer.Ordinal);
            foreach (FastaItemExtended item in file.MyExtendedFastaItems ?? new List<FastaItemExtended>())
            {
                string id = item?.SequenceIdentifier ?? string.Empty;
                if (item == null || id.Length == 0 || SequenceLabels.IsDecoy(id)) continue;
                string accession = BaseAccession(FamilyNaming.Accession(id));
                accessions.Add(accession);
                proteins.Add(new FamilyWorldProtein
                {
                    Id = id,
                    Accession = accession,
                    Sequence = item.Sequence ?? string.Empty,
                    Families = FamiliesOf(item.Evidence),
                    Taxon = TaxonOf(item.Description),
                    Taxa = TaxaOf(item.Description),
                });
            }
            foreach (PanelEntry entry in file.EvidenceSource?.ReferencePanel?.Entries ?? new List<PanelEntry>())
            {
                string accession = BaseAccession(entry.Accession);
                if (!accessions.Add(accession)) continue;
                proteins.Add(new FamilyWorldProtein
                {
                    Id = "panel|" + entry.Accession,
                    Accession = accession,
                    Sequence = entry.Sequence,
                    Families = FamiliesOf(entry.Evidence),
                    FromPanel = true,
                });
            }
            return proteins;
        }

        /// <summary>The world a family-world file stands for until the clade fetch (B4) stores one: its entries and
        /// its stored panel, with the panel's families as the scope.</summary>
        public static FamilyWorld FromFile(FamilyClusterFile file) =>
            Build(Collect(file), file.EvidenceSource?.ReferencePanel?.Families ?? new List<string>());

        /// <summary>
        /// The world B4 stored with the curation: each entry by its stored accession (so hit identifiers are B4's),
        /// with its full family memberships, organism and lineage; the scope is the snapshot's families and the
        /// fingerprint its content hash. The caller verifies the snapshot (<see cref="FamilyWorldFetcher.Stored"/>).
        /// </summary>
        public static FamilyWorld FromSnapshot(FamilyWorldSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            List<FamilyWorldProtein> proteins = snapshot.Entries.Select(e => new FamilyWorldProtein
            {
                Id = e.Accession,
                Accession = BaseAccession(e.Accession),
                Sequence = e.Sequence ?? string.Empty,
                Families = FamiliesOf(e.Evidence),
                Taxon = e.TaxonId > 0 ? e.TaxonId.ToString(CultureInfo.InvariantCulture) : string.Empty,
                Taxa = e.TaxonId > 0 ? new[] { e.TaxonId.ToString(CultureInfo.InvariantCulture) } : Array.Empty<string>(),
                LineageTaxa = e.LineageTaxonIds.Select(t => t.ToString(CultureInfo.InvariantCulture)).ToArray(),
                FromPanel = e.FromReviewedPanel,
            }).ToList();
            return Build(proteins, snapshot.Families, snapshot.ContentSha256);
        }

        /// <summary>Whether a protein belongs to any direct taxon, or any taxon of its lineage.</summary>
        public static bool IsOfTaxa(FamilyWorldProtein protein, ICollection<string> taxa) =>
            (protein.Taxon.Length > 0 && taxa.Contains(protein.Taxon)) || protein.Taxa.Any(taxa.Contains) || protein.LineageTaxa.Any(taxa.Contains);

        /// <summary>The searchable world: the kept proteins and one reversed copy of each.</summary>
        public static FamilyWorld Build(IEnumerable<FamilyWorldProtein> proteins, IEnumerable<string> scopeFamilies) =>
            Build(proteins, scopeFamilies, null);

        private static FamilyWorld Build(IEnumerable<FamilyWorldProtein> proteins, IEnumerable<string> scopeFamilies, string? fingerprint)
        {
            if (proteins == null) throw new ArgumentNullException(nameof(proteins));
            if (scopeFamilies == null) throw new ArgumentNullException(nameof(scopeFamilies));
            List<FamilyWorldProtein> kept = proteins.Where(p => p != null && p.Sequence.Length > 0).ToList();
            var entries = new List<(string Id, string Sequence)>(kept.Count * 2);
            foreach (FamilyWorldProtein p in kept) entries.Add((p.Id, p.Sequence));
            foreach (FamilyWorldProtein p in kept)
            {
                char[] reversed = p.Sequence.ToCharArray();
                Array.Reverse(reversed);
                entries.Add((SequenceLabels.DecoyPrefix + p.Id, new string(reversed)));
            }
            return new FamilyWorld(kept, HomologyDatabase.Build(entries), scopeFamilies, fingerprint);
        }

        /// <summary>
        /// What one search result credits. A tag shorter than <paramref name="minimumTagLength"/> is never credited.
        /// Of the families the winning targets carry, only those in <paramref name="creditable"/> are credited; the
        /// others are kept apart as out of scope.
        /// </summary>
        public FamilyWorldCredit Credit(HomologyResult result, IReadOnlyCollection<string> creditable, int minimumTagLength)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            if (creditable == null) throw new ArgumentNullException(nameof(creditable));
            if (result.IsRejected) return new FamilyWorldCredit { Outcome = FamilyWorldOutcome.Rejected };
            if (result.NormalizedPeptide.Length < minimumTagLength) return new FamilyWorldCredit { Outcome = FamilyWorldOutcome.TooShort };
            if (result.Hits.Count == 0) return new FamilyWorldCredit { Outcome = FamilyWorldOutcome.NotLanded };
            if (result.Hits.All(h => h.IsDecoy))
            {
                SortedSet<string> sources = NewFamilySet();
                foreach (HomologyHit hit in result.Hits)
                    if (byId.TryGetValue(hit.ProteinId.Substring(SequenceLabels.DecoyPrefix.Length), out FamilyWorldProtein? source))
                        sources.UnionWith(source.Families);
                return new FamilyWorldCredit { Outcome = FamilyWorldOutcome.Decoy, DecoySourceFamilies = sources };
            }
            SortedSet<string> matched = NewFamilySet();
            var proteins = new SortedSet<string>(StringComparer.Ordinal);
            foreach (HomologyHit hit in result.Hits)
                if (!hit.IsDecoy && byId.TryGetValue(hit.ProteinId, out FamilyWorldProtein? protein))
                {
                    matched.UnionWith(protein.Families);
                    proteins.Add(protein.Id);
                }
            if (matched.Count == 0) return new FamilyWorldCredit { Outcome = FamilyWorldOutcome.LandedNoFamily, Proteins = proteins };
            var allowed = new HashSet<string>(creditable, StringComparer.OrdinalIgnoreCase);
            SortedSet<string> credited = NewFamilySet();
            SortedSet<string> outOfScope = NewFamilySet();
            foreach (string family in matched) (allowed.Contains(family) ? credited : outOfScope).Add(family);
            return new FamilyWorldCredit
            {
                Outcome = credited.Count == 0 ? FamilyWorldOutcome.OutOfScope : FamilyWorldOutcome.Credited,
                Families = credited,
                OutOfScopeFamilies = outOfScope,
                Proteins = proteins,
            };
        }

        /// <summary>Right only when every credited family is one of the known ones: a tag credited to its own family
        /// and to another one is counted wrong, because the other family would receive its signal.</summary>
        public static bool IsRight(FamilyWorldCredit credit, IReadOnlyCollection<string> known) =>
            credit.Outcome == FamilyWorldOutcome.Credited && known.Count > 0
            && credit.Families.All(f => known.Contains(f, StringComparer.OrdinalIgnoreCase));

        public static string Join(IEnumerable<string> families) => string.Join("; ",
            families.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ThenBy(f => f, StringComparer.Ordinal));

        private static IReadOnlyList<string> Sorted(IEnumerable<string> families) => families
            .Where(f => !string.IsNullOrWhiteSpace(f)).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ThenBy(f => f, StringComparer.Ordinal).ToArray();

        private static string FingerprintOf(IEnumerable<FamilyWorldProtein> proteins)
        {
            var text = new StringBuilder();
            foreach (FamilyWorldProtein p in proteins.OrderBy(p => p.Id, StringComparer.Ordinal))
                text.Append(p.Id.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(p.Id)
                    .Append(p.Sequence.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(p.Sequence)
                    .Append(Join(p.Families).Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(Join(p.Families)).Append('\n');
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
        }
    }

    /// <summary>One family of the second pass: the reference-calibrated de novo envelope credited to it.</summary>
    public sealed class FamilyWorldFamilyRow
    {
        public string Family { get; init; } = string.Empty;
        /// <summary>Every peptide credited to the family, counted in each family it reaches (overlapping).</summary>
        public double CreditedEnvelope { get; set; }
        /// <summary>Peptides credited to this family alone.</summary>
        public double UniqueEnvelope { get; set; }
        /// <summary>Each peptide divided equally among the families it reaches.</summary>
        public double SplitEnvelope { get; set; }
        public int Peptides { get; set; }
        public int UniquePeptides { get; set; }
        /// <summary>Peptides won by the reversed copies of this family's proteins.</summary>
        public double DecoyEnvelope { get; set; }
        public int DecoyPeptides { get; set; }
        /// <summary>The world proteins of this family that the credited tags hit, by identifier.</summary>
        public SortedSet<string> Proteins { get; } = new SortedSet<string>(StringComparer.Ordinal);
    }

    /// <summary>One distinct searched sequence of the residual, for the audit.</summary>
    public sealed class FamilyWorldSequenceRow
    {
        public string Sequence { get; init; } = string.Empty;
        public int Peptides { get; set; }
        public double Envelope { get; set; }
        public FamilyWorldCredit Credit { get; init; } = new FamilyWorldCredit();
    }

    /// <summary>The second attribution pass over the de novo envelope the first pass left unattributed.</summary>
    public sealed class FamilyWorldPassResult
    {
        public string Method { get; init; } = FamilyWorldPass.Method;
        public int MinimumTagLength { get; init; }
        public double MinIdentity { get; init; }
        public IReadOnlyList<string> CreditableFamilies { get; init; } = Array.Empty<string>();
        public string WorldFingerprint { get; init; } = string.Empty;
        public int WorldProteins { get; init; }

        /// <summary>The first pass's unattributed de novo reference-calibrated envelope, the denominator.</summary>
        public double ResidualEnvelope { get; set; }
        public int ResidualPeptides { get; set; }
        public Dictionary<FamilyWorldOutcome, double> EnvelopeByOutcome { get; } = new Dictionary<FamilyWorldOutcome, double>();
        public Dictionary<FamilyWorldOutcome, int> PeptidesByOutcome { get; } = new Dictionary<FamilyWorldOutcome, int>();
        /// <summary>Credited envelope reaching several families: the shared bucket.</summary>
        public double SharedEnvelope { get; set; }
        /// <summary>Credited envelope whose tags also matched a family out of scope.</summary>
        public double AlsoOutOfScopeEnvelope { get; set; }
        public List<FamilyWorldFamilyRow> Families { get; } = new List<FamilyWorldFamilyRow>();
        public List<FamilyWorldSequenceRow> Sequences { get; } = new List<FamilyWorldSequenceRow>();

        public double Envelope(FamilyWorldOutcome outcome) => EnvelopeByOutcome.TryGetValue(outcome, out double v) ? v : 0;
        public int Peptides(FamilyWorldOutcome outcome) => PeptidesByOutcome.TryGetValue(outcome, out int v) ? v : 0;
        /// <summary>The share of the residual credited to a family.</summary>
        public double LandedFraction => ResidualEnvelope <= 0 ? 0 : Envelope(FamilyWorldOutcome.Credited) / ResidualEnvelope;
        /// <summary>Envelope won by reversed copies over the envelope credited to a family.</summary>
        public double DecoyRate
        {
            get
            {
                double credited = Envelope(FamilyWorldOutcome.Credited);
                return credited <= 0 ? double.PositiveInfinity : Envelope(FamilyWorldOutcome.Decoy) / credited;
            }
        }
    }

    /// <summary>
    /// B5, the second attribution pass. The de novo peptides the curated families do not reach
    /// (<see cref="FamilyEnvelopeQuant.AttributePeptides(IReadOnlyList{NativePeptideQuant}, PeptideFamilyAttribution)"/>)
    /// are searched against the family world. A hit credits the UniProt families in scope, never a protein. The
    /// first-pass families, totals and exports are only read; the result is a separate table with its own export.
    /// </summary>
    public static class FamilyWorldPass
    {
        public const string Method =
            "family world pass v1: de novo peptides with no first-pass family; tags of at least the minimum length " +
            "searched against the family world and its reversed copies (gapless identity gate, PAM30MS, every target " +
            "past the gate after target-decoy competition); a hit credits the UniProt families in scope of the " +
            "winning proteins, never a protein; 2026-11-01";

        /// <summary>The pass over a project's quant and curation: the production first pass, then the second.</summary>
        public static FamilyWorldPassResult Run(IEnumerable<QuantRow> rows, FamilyClusterFile curation, FamilyWorld world,
            FamilyWorldOptions? options = null, OperationContext? operation = null)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            if (curation == null) throw new ArgumentNullException(nameof(curation));
            List<QuantRow> rowList = rows.Where(r => r != null).ToList();
            PeptideFamilyAttribution attribution = FamilyEnvelopeQuant.BuildAttributionFromCuration(rowList, curation);
            return Run(FamilyEnvelopeQuant.AttributePeptides(FamilyEnvelopeQuant.PreparePeptides(rowList), attribution),
                world, options, operation);
        }

        public static FamilyWorldPassResult Run(FamilyPeptideAttribution firstPass, FamilyWorld world,
            FamilyWorldOptions? options = null, OperationContext? operation = null) =>
            RunLengths(firstPass, world, options, new[] { (options ?? new FamilyWorldOptions()).MinimumTagLength }, operation)[0];

        /// <summary>
        /// The pass at several minimum tag lengths from ONE world search, at the lowest of them (B10). A tag's search
        /// result depends only on the tag, the world and the mapper options, never on the minimum, so each length's
        /// result is the lowest-length search with the tags under that length set to <see cref="FamilyWorldOutcome.TooShort"/>.
        /// The residual is walked in the same order for every length, so each result equals a pass run at that length
        /// alone, sums included, to the last bit. Results come back in the order of <paramref name="lengths"/>.
        /// </summary>
        public static IReadOnlyList<FamilyWorldPassResult> RunLengths(FamilyPeptideAttribution firstPass, FamilyWorld world,
            FamilyWorldOptions? options, IReadOnlyList<int> lengths, OperationContext? operation = null)
        {
            if (firstPass == null) throw new ArgumentNullException(nameof(firstPass));
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (lengths == null || lengths.Count == 0) throw new ArgumentException("Give at least one minimum tag length.", nameof(lengths));
            options ??= new FamilyWorldOptions();
            if (lengths.Any(l => l < 0)) throw new ArgumentOutOfRangeException(nameof(lengths), "The minimum tag length cannot be negative.");
            IReadOnlyCollection<string> creditable = options.CreditableFamilies ?? world.ScopeFamilies;
            if (creditable.Count == 0)
                throw new InvalidOperationException("The family world records no families in scope, so no family can be credited. " +
                    "Fetch the family world for a confirmed scope first.");
            string[] creditableSorted = creditable.Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ThenBy(f => f, StringComparer.Ordinal).ToArray();

            int lowest = lengths.Min();
            List<(NativePeptideQuant Peptide, string Sequence)> residual = firstPass.UnattributedDeNovoPeptides
                .Select(p => (p, DeNovoReconciler.NormalizeSequence(p.Sequence ?? string.Empty))).ToList();
            List<string> searched = residual.Select(r => r.Sequence).Where(s => s.Length > 0 && s.Length >= lowest)
                .Distinct(StringComparer.Ordinal).ToList();
            var credits = new Dictionary<string, FamilyWorldCredit>(StringComparer.Ordinal);
            foreach (HomologyResult r in HomologySearch.Search(searched, world.Database, options.SearchOptions(), operation,
                progressStage: "world-residual-queries", progressLabel: "Family-world peptides searched", progressBatchSize: 128))
                credits[r.IsRejected ? r.Peptide : r.NormalizedPeptide] = world.Credit(r, creditableSorted, lowest);
            operation?.ThrowIfCancellationRequested();
            return lengths.Select(length => Aggregate(residual, credits, length, creditableSorted, world, options)).ToList();
        }

        private static FamilyWorldPassResult Aggregate(List<(NativePeptideQuant Peptide, string Sequence)> residual,
            Dictionary<string, FamilyWorldCredit> credits, int minimumTagLength, string[] creditable, FamilyWorld world,
            FamilyWorldOptions options)
        {
            var result = new FamilyWorldPassResult
            {
                MinimumTagLength = minimumTagLength,
                MinIdentity = options.MinIdentity,
                CreditableFamilies = creditable,
                WorldFingerprint = world.Fingerprint,
                WorldProteins = world.Proteins.Count,
            };

            FamilyWorldCredit CreditOf(string sequence) => sequence.Length == 0
                ? new FamilyWorldCredit { Outcome = FamilyWorldOutcome.Rejected }
                : sequence.Length < minimumTagLength ? new FamilyWorldCredit { Outcome = FamilyWorldOutcome.TooShort }
                : credits[sequence];

            var byFamily = new Dictionary<string, FamilyWorldFamilyRow>(StringComparer.OrdinalIgnoreCase);
            FamilyWorldFamilyRow Row(string family) => byFamily.TryGetValue(family, out FamilyWorldFamilyRow? row)
                ? row : byFamily[family] = new FamilyWorldFamilyRow { Family = family };
            var bySequence = new Dictionary<string, FamilyWorldSequenceRow>(StringComparer.Ordinal);
            var creditableSet = new HashSet<string>(creditable, StringComparer.OrdinalIgnoreCase);
            foreach (var (peptide, sequence) in residual)
            {
                double envelope = peptide.ReferenceCalibratedEnvelope;
                FamilyWorldCredit credit = CreditOf(sequence);
                result.ResidualEnvelope += envelope;
                result.ResidualPeptides++;
                result.EnvelopeByOutcome[credit.Outcome] = result.Envelope(credit.Outcome) + envelope;
                result.PeptidesByOutcome[credit.Outcome] = result.Peptides(credit.Outcome) + 1;
                if (credit.Outcome == FamilyWorldOutcome.Credited)
                {
                    if (credit.Families.Count > 1) result.SharedEnvelope += envelope;
                    if (credit.OutOfScopeFamilies.Count > 0) result.AlsoOutOfScopeEnvelope += envelope;
                    foreach (string family in credit.Families)
                    {
                        FamilyWorldFamilyRow row = Row(family);
                        row.CreditedEnvelope += envelope;
                        row.SplitEnvelope += envelope / credit.Families.Count;
                        row.Peptides++;
                        foreach (string id in credit.Proteins)
                            if (world.Protein(id)?.Families.Contains(family, StringComparer.OrdinalIgnoreCase) == true) row.Proteins.Add(id);
                        if (credit.Families.Count == 1)
                        {
                            row.UniqueEnvelope += envelope;
                            row.UniquePeptides++;
                        }
                    }
                }
                foreach (string family in credit.DecoySourceFamilies)
                    if (creditableSet.Contains(family))
                    {
                        FamilyWorldFamilyRow row = Row(family);
                        row.DecoyEnvelope += envelope;
                        row.DecoyPeptides++;
                    }
                if (!bySequence.TryGetValue(sequence, out FamilyWorldSequenceRow? s))
                    bySequence[sequence] = s = new FamilyWorldSequenceRow { Sequence = sequence, Credit = credit };
                s.Peptides++;
                s.Envelope += envelope;
            }

            result.Families.AddRange(byFamily.Values.OrderByDescending(f => f.CreditedEnvelope)
                .ThenBy(f => f.Family, StringComparer.OrdinalIgnoreCase).ThenBy(f => f.Family, StringComparer.Ordinal));
            result.Sequences.AddRange(bySequence.Values.OrderBy(s => s.Sequence, StringComparer.Ordinal));
            return result;
        }

        /// <summary>The rules in force, in plain words, as the export and the screen state them (D11).</summary>
        public static string RuleText(FamilyWorldPassResult result) =>
            "second pass on the de novo envelope the curated families leave unattributed; tags of at least " +
            result.MinimumTagLength.ToString(CultureInfo.InvariantCulture) + " residues; " +
            (100 * result.MinIdentity).ToString("0.##", CultureInfo.InvariantCulture) + "% gapless identity; reversed copies as decoys; only families " +
            "in scope can be credited (" + result.CreditableFamilies.Count.ToString(CultureInfo.InvariantCulture) + "); " +
            "a family is credited, never a protein; a family row holds its own peptides and a peptide reaching several " +
            "families is counted once, in the shared row; fractions of the unattributed envelope";

        /// <summary>
        /// The new export. One row per credited family (its unique part, the part split equally, the overlapping
        /// count-in-every-family total and the decoys its reversed copies won), then the rows that close on the
        /// unattributed envelope: unique family parts + shared + every other outcome = 1. Every row states the rules.
        /// </summary>
        public static string BuildCsv(FamilyWorldPassResult result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            string Num(double v) => v.ToString("R", CultureInfo.InvariantCulture);
            string Int(int v) => v.ToString(CultureInfo.InvariantCulture);
            string Fraction(double v) => result.ResidualEnvelope > 0 ? Num(v / result.ResidualEnvelope) : string.Empty;
            string rule = CsvFormatting.AlwaysQuoted(RuleText(result));
            var csv = new StringBuilder();
            csv.Append(string.Join(",", new[]
            {
                "row", "family", "envelope", "fraction_of_unattributed", "split_envelope", "credited_envelope_every_family",
                "peptides", "unique_peptides", "decoy_envelope", "world_proteins_hit", "rule",
            }.Select(CsvFormatting.AlwaysQuoted))).Append('\n');
            foreach (FamilyWorldFamilyRow f in result.Families)
                csv.Append(string.Join(",", CsvFormatting.AlwaysQuoted("family"), CsvFormatting.AlwaysQuoted(f.Family),
                    Num(f.UniqueEnvelope), Fraction(f.UniqueEnvelope), Num(f.SplitEnvelope), Num(f.CreditedEnvelope),
                    Int(f.Peptides), Int(f.UniquePeptides), Num(f.DecoyEnvelope), Int(f.Proteins.Count), rule)).Append('\n');

            void Closing(string label, double envelope, int peptides) =>
                csv.Append(string.Join(",", CsvFormatting.AlwaysQuoted("closing"), CsvFormatting.AlwaysQuoted(label),
                    Num(envelope), Fraction(envelope), string.Empty, string.Empty, Int(peptides), string.Empty, string.Empty, string.Empty, rule)).Append('\n');
            int sharedPeptides = result.Sequences.Where(s => s.Credit.Outcome == FamilyWorldOutcome.Credited && s.Credit.Families.Count > 1)
                .Sum(s => s.Peptides);
            Closing("shared between families", result.SharedEnvelope, sharedPeptides);
            Closing("won by a reversed copy", result.Envelope(FamilyWorldOutcome.Decoy), result.Peptides(FamilyWorldOutcome.Decoy));
            Closing("landed on families out of scope only", result.Envelope(FamilyWorldOutcome.OutOfScope), result.Peptides(FamilyWorldOutcome.OutOfScope));
            Closing("landed on proteins with no family", result.Envelope(FamilyWorldOutcome.LandedNoFamily), result.Peptides(FamilyWorldOutcome.LandedNoFamily));
            Closing("tag shorter than the minimum length", result.Envelope(FamilyWorldOutcome.TooShort), result.Peptides(FamilyWorldOutcome.TooShort));
            Closing("not found in the family world", result.Envelope(FamilyWorldOutcome.NotLanded), result.Peptides(FamilyWorldOutcome.NotLanded));
            Closing("sequence not readable", result.Envelope(FamilyWorldOutcome.Rejected), result.Peptides(FamilyWorldOutcome.Rejected));
            return csv.ToString();
        }
    }
}
