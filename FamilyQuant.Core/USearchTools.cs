using PatternTools.FastaTools;
using FamilyQuant.Operations;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Security.Cryptography;
using System.Threading;

namespace FamilyQuant
{
    public class USearchTools
    {
        public const string ExhaustiveClusteringMethod =
            "USEARCH cluster_fast plus allpairs_global single-linkage connected components";
        public const string FastClusteringMethod = "USEARCH cluster_fast greedy centroid clustering";
        private static readonly TimeSpan ProcessTimeout = TimeSpan.FromMinutes(30);
        // Below this the identity/coverage comparisons treat two values as equal, so a boundary case
        // (alnlen exactly on the coverage threshold, id exactly on the identity) is kept rather than
        // dropped by float noise. USEARCH reports id and lengths as short decimals/integers, so the
        // slack only ever matters at the exact edge.
        private const double GateEpsilon = 1e-9;

        /// <summary>
        /// One accepted pairwise alignment from <c>usearch -allpairs_global</c>: the two sequence
        /// identifiers, the alignment identity as a PERCENTAGE (0-100), the aligned length, and the two
        /// sequence lengths. The exhaustive second pass reads these to decide which first-pass families
        /// to merge. It carries no dependence on the clustering identity, so it is aligned once per FASTA
        /// and reused across an identity sweep.
        /// </summary>
        public readonly struct PairwiseEdge
        {
            public string QueryId { get; }
            public string TargetId { get; }
            public double IdentityPercent { get; }
            public int AlignmentLength { get; }
            public int QueryLength { get; }
            public int TargetLength { get; }

            public PairwiseEdge(string queryId, string targetId, double identityPercent,
                                int alignmentLength, int queryLength, int targetLength)
            {
                QueryId = queryId;
                TargetId = targetId;
                IdentityPercent = identityPercent;
                AlignmentLength = alignmentLength;
                QueryLength = queryLength;
                TargetLength = targetLength;
            }
        }

        // Immutable on-disk pairwise tables per FASTA, keyed by full path and content SHA-256. Edges do
        // not depend on the clustering identity, and GenerateFCF is called once per identity across a
        // whole sweep or a Divide-and-Conquer search, possibly from several background threads at once,
        // so the O(N^2) alignment must run ONCE per FASTA. A failed or empty alignment is never left in
        // the memo (see GetOrAlignPairwiseEdges), so it cannot answer forever from a poisoned result.
        private static readonly ConcurrentDictionary<string, Lazy<PairwiseEdgeFile?>> PairwiseEdgeCache = new();
        private static readonly ConcurrentBag<string> OwnedPairwiseFiles = new();

        static USearchTools()
        {
            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                foreach (string path in OwnedPairwiseFiles) TryDelete(path);
            };
        }

        private sealed record PairwiseEdgeFile(string Path, long Count, bool FractionalIdentity)
        {
            public IEnumerable<PairwiseEdge> Read(OperationContext? operation) =>
                ReadPairwiseEdges(Path, FractionalIdentity, operation);
        }

        // How many times the all-pairs alignment has actually been executed. A test seam: it proves the
        // memo above serves a repeated call without re-aligning, and that a changed FASTA re-aligns.
        private static long pairwiseAlignmentRuns;
        public static long PairwiseAlignmentRunCount => Interlocked.Read(ref pairwiseAlignmentRuns);

        public static string Align(List<FastaItemExtended> fastaSequences, OperationContext? operation = null)
        {
            operation?.ThrowIfCancellationRequested();
            string usearchExe = GetUSearchPath();

            string tempDir = ApplicationPaths.ScratchDirectory;
            Directory.CreateDirectory(tempDir);

            // Unique filenames so a leftover/locked file from a previous (possibly crashed) run can't
            // collide with this one or be read back as a stale result.
            string token = Guid.NewGuid().ToString("N");
            string fileFasta = Path.Combine(tempDir, "align_" + token + ".fasta");
            string alnFile = Path.Combine(tempDir, "align_" + token + ".aln");

            try
            {
                using (StreamWriter sw = new StreamWriter(fileFasta))
                {
                    foreach (FastaItemExtended f in fastaSequences)
                    {
                        operation?.ThrowIfCancellationRequested();
                        sw.WriteLine(">" + f.SequenceIdentifier);
                        sw.WriteLine(f.Sequence);
                    }
                }

                // Quote every path: the temp folder lives under the user profile and frequently
                // contains spaces (e.g. "C:\Users\John Doe\..."), which silently broke the old
                // unquoted command line and was the most likely cause of the alignment crash.
                string arguments = $"-allpairs_global \"{fileFasta}\" -alnout \"{alnFile}\" --acceptall";

                RunUSearch(usearchExe, arguments, tempDir, operation);

                if (!File.Exists(alnFile))
                {
                    throw new InvalidOperationException(
                        "USEARCH finished without producing an alignment file. This usually means the temp " +
                        "path contains characters USEARCH cannot handle, or the run was blocked by security software.");
                }

                return File.ReadAllText(alnFile);
            }
            finally
            {
                TryDelete(fileFasta);
                TryDelete(alnFile);
            }
        }

        private static string GetUSearchPath()
        {
            // Escape hatch for locked-down machines (AppLocker / AV that block execution from
            // user-writable folders): point FAMILYQUANT_USEARCH at a usearch.exe you control.
            string? overridePath = Environment.GetEnvironmentVariable("FAMILYQUANT_USEARCH");
            if (!string.IsNullOrWhiteSpace(overridePath) && File.Exists(overridePath))
            {
                return overridePath;
            }

            byte[] payload = MyResources.usearch11_0_667_win32;

            // Extract to a stable per-user folder rather than %TEMP% (which can be cleaned mid-session).
            string dir = ApplicationPaths.LocalDataDirectory;
            Directory.CreateDirectory(dir);

            string usearchPath = Path.Combine(dir, "usearch11.0.667_win32.exe");

            string payloadHash;
            using (SHA256 sha = SHA256.Create()) payloadHash = Convert.ToHexString(sha.ComputeHash(payload));
            bool executableMatches = false;
            if (File.Exists(usearchPath) && new FileInfo(usearchPath).Length == payload.Length)
            {
                try
                {
                    executableMatches = string.Equals(
                        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(usearchPath))),
                        payloadHash, StringComparison.Ordinal);
                }
                catch { executableMatches = false; }
            }

            // Re-extract if missing or its CONTENT differs from the embedded payload. Equal size is not
            // an integrity check: a modified executable can keep the same length.
            if (!executableMatches)
            {
                try
                {
                    File.WriteAllBytes(usearchPath, payload);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        "Could not write the bundled USEARCH to \"" + usearchPath + "\". Check disk space and " +
                        "that security software is not blocking it, or set FAMILYQUANT_USEARCH to your own " +
                        "usearch.exe.\n\n" + ex.Message, ex);
                }
            }

            return usearchPath;
        }

        /// <summary>
        /// Groups a database into families at a sequence-identity threshold and returns the result.
        ///
        /// The fast first pass (<c>usearch -cluster_fast</c>) is greedy and order-sensitive: two
        /// sequences that align above the threshold can still land in SEPARATE families, because the
        /// second one was compared only against the seed of a family it did not quite match. That leaves
        /// biological holes - full-length homologues split apart.
        ///
        /// With <paramref name="exhaustive"/> on (the default), a second pass closes them: every pair of
        /// sequences is aligned in full (<c>usearch -allpairs_global</c>) and the first-pass families are
        /// single-linked through every alignment that reaches the identity over most of the LONGER
        /// sequence. A pair sharing only a short local motif fails the coverage half of the gate and is
        /// correctly kept apart. This is single linkage on purpose: the identity is the tightness knob,
        /// and every pair at or above it chains its two families together (see PassesExhaustiveGate).
        ///
        /// With <paramref name="exhaustive"/> off, only the fast first pass runs, which is quicker but
        /// leaves the holes. The exhaustive pass runs regardless of database size; the O(N^2) alignment
        /// simply takes longer on a large database, and the caller (the USearch panel) warns once before
        /// starting it rather than capping it here.
        /// </summary>
        public static FamilyClusterFile GenerateFCF(FileInfo fi, int identity, bool exhaustive = true, OperationContext? operation = null)
            => Cluster(fi, identity, exhaustive, operation).Document;

        public static ClusteringResult Cluster(FileInfo fi, int identity, bool exhaustive = true, OperationContext? operation = null)
        {
            operation?.ThrowIfCancellationRequested();
            string usearchExe = GetUSearchPath();

            // Absolute, quoted paths plus an explicit working directory: we no longer mutate the
            // process-wide current directory (so this is now safe to call from a background thread)
            // and we tolerate spaces in the FASTA's folder.
            //
            // The name carries a unique token because two sweeps over the same FASTA (Divide & Conquer
            // and Scan identity range both run many clusterings) would otherwise probe the same identity
            // and collide: one run's cleanup deleted the file the other was about to read, or the parse
            // saw a half-written table and silently produced a family list missing proteins.
            string ucFile = Path.Combine(
                fi.DirectoryName ?? string.Empty,
                Path.GetFileNameWithoutExtension(fi.Name) + "_" + identity + "_" +
                Guid.NewGuid().ToString("N").Substring(0, 8) + ".uc");

            // Force InvariantCulture for the identity fraction. On locales whose decimal separator is a
            // comma (pt-BR, pt-PT, most of Europe) "0.85".ToString() produced "-id 0,85", which USEARCH
            // misparsed - a silent "some users" clustering bug.
            string idValue = Math.Round((double)identity / 100, 2).ToString(CultureInfo.InvariantCulture);
            string arguments = $"-cluster_fast \"{fi.FullName}\" -id {idValue} -uc \"{ucFile}\"";

            try
            {
                RunUSearch(usearchExe, arguments, fi.DirectoryName ?? Environment.CurrentDirectory, operation);

                if (!File.Exists(ucFile))
                {
                    throw new InvalidOperationException("USEARCH produced no cluster (.uc) file for " + fi.Name + ".");
                }

                FastaFileParser ffp = new FastaFileParser() { PreserveHead = true };
                using (StreamReader reader = new StreamReader(fi.FullName))
                {
                    ffp.ParseFile(reader, false);
                }

                List<FastaItemExtended> fastaItemsExtended = ParseClusterFile(ucFile, ffp.MyItems, operation);

                bool usedExhaustive = exhaustive &&
                    ApplyExhaustiveSecondPass(fi, usearchExe, fastaItemsExtended, identity, operation);
                IReadOnlyList<string> warnings = exhaustive && !usedExhaustive
                    ? new[] { "The exhaustive pass produced no alignments; families use the fast first pass." }
                    : Array.Empty<string>();

                var document = new FamilyClusterFile()
                {
                    ClusteringIdentity = identity,
                    SourceSha256 = FileFingerprint.Sha256(fi.FullName),
                    SourceSequenceCount = ffp.MyItems.Count,
                    ClusteringMethod = usedExhaustive ? ExhaustiveClusteringMethod : FastClusteringMethod,
                    MyExtendedFastaItems = fastaItemsExtended
                };
                return new ClusteringResult(document, exhaustive, usedExhaustive, warnings);
            }
            finally
            {
                TryDelete(ucFile);
            }
        }

        /// <summary>
        /// Number of sequences in a FASTA, counted by its '&gt;' header lines without loading the file
        /// into memory. Used to warn before an exhaustive clustering of a very large database.
        /// </summary>
        public static int CountSequences(FileInfo fi, OperationContext? operation = null)
        {
            operation?.ThrowIfCancellationRequested();
            int count = 0;
            foreach (string line in File.ReadLines(fi.FullName))
            {
                operation?.ThrowIfCancellationRequested();
                if (line.Length > 0 && line[0] == '>') count++;
            }
            return count;
        }

        /// <summary>
        /// The coverage-and-identity gate for one pairwise edge. It KEEPS a full-length match at or above
        /// the identity, and EXCLUDES a short local motif: a high-identity hit whose aligned length is
        /// small relative to the longer of the two sequences. Coverage is measured against the LONGER
        /// sequence, so a short fragment aligning perfectly inside a large protein does not merge them.
        /// </summary>
        public static bool PassesExhaustiveGate(PairwiseEdge edge, int identity)
        {
            if (edge.IdentityPercent + GateEpsilon < identity) return false;

            int longer = Math.Max(edge.QueryLength, edge.TargetLength);
            double coverageNeeded = (identity / 100.0) * longer;
            return edge.AlignmentLength + GateEpsilon >= coverageNeeded;
        }

        /// <summary>
        /// Single-linkage merge of the first-pass families over every pairwise edge that passes the gate.
        /// Pure (no I/O), so the gate arithmetic and the union-find are unit-tested directly.
        ///
        /// The result maps each sequence identifier to the id of its final family. The survivor of a
        /// merged component is its smallest member id (via <see cref="WordCluster.BuildStableMapping"/>),
        /// so a family the merge never touched keeps its exact first-pass number. An edge referencing a
        /// sequence that is not in <paramref name="firstPassClusterBySeq"/> is skipped rather than
        /// throwing, so a stray label in the alignment output cannot abort the clustering.
        /// </summary>
        public static Dictionary<string, int> MergeFirstPassClusters(
            IReadOnlyDictionary<string, int> firstPassClusterBySeq,
            IEnumerable<PairwiseEdge> edges,
            int identity, OperationContext? operation = null)
        {
            operation?.ThrowIfCancellationRequested();
            var parent = new Dictionary<int, int>();

            int Find(int x)
            {
                if (!parent.ContainsKey(x)) parent[x] = x;
                while (parent[x] != x)
                {
                    parent[x] = parent[parent[x]];
                    x = parent[x];
                }
                return x;
            }

            void Union(int a, int b)
            {
                int ra = Find(a), rb = Find(b);
                if (ra != rb) parent[Math.Max(ra, rb)] = Math.Min(ra, rb);
            }

            foreach (int cluster in firstPassClusterBySeq.Values) Find(cluster);

            foreach (PairwiseEdge edge in edges)
            {
                operation?.ThrowIfCancellationRequested();
                if (!PassesExhaustiveGate(edge, identity)) continue;
                if (!firstPassClusterBySeq.TryGetValue(edge.QueryId, out int cq)) continue;
                if (!firstPassClusterBySeq.TryGetValue(edge.TargetId, out int ct)) continue;
                Union(cq, ct);
            }

            // Group the first-pass cluster ids by the component they ended up in, then let
            // BuildStableMapping pick the smallest id in each as the survivor.
            var components = new Dictionary<int, List<int>>();
            foreach (int cluster in firstPassClusterBySeq.Values.Distinct())
            {
                int root = Find(cluster);
                if (!components.TryGetValue(root, out List<int>? members))
                {
                    members = new List<int>();
                    components[root] = members;
                }
                members.Add(cluster);
            }

            Dictionary<int, int> clusterToSurvivor = WordCluster.BuildStableMapping(components.Values.ToList());

            var result = new Dictionary<string, int>(firstPassClusterBySeq.Count, StringComparer.Ordinal);
            foreach (var kv in firstPassClusterBySeq)
            {
                result[kv.Key] = clusterToSurvivor[kv.Value];
            }
            return result;
        }

        /// <summary>
        /// Runs the exhaustive second pass over an already-clustered item list, IN PLACE: it aligns every
        /// pair, merges the first-pass families through the gate, writes the merged family id into both
        /// USearchID and UserID of every item, and re-sorts by family id as ParseClusterFile does.
        ///
        /// If the all-pairs alignment fails or produces nothing, this leaves the fast-only grouping
        /// untouched rather than throwing: the caller still gets a usable Family Cluster File.
        /// </summary>
        private static bool ApplyExhaustiveSecondPass(
            FileInfo fi, string usearchExe, List<FastaItemExtended> items, int identity, OperationContext? operation)
        {
            PairwiseEdgeFile? edges = GetOrAlignPairwiseEdges(fi, usearchExe, operation);
            if (edges == null || edges.Count == 0)
            {
                // Never fail silently: the exhaustive pass was requested but the all-pairs alignment
                // produced nothing, so the grouping is the fast first pass only. Say so rather than let
                // the caller believe it got the refined clustering.
                Console.WriteLine("Exhaustive clustering pass produced no pairwise alignments for " +
                    fi.Name + "; kept the fast first-pass clustering for this run.");
                return false;
            }

            var firstPass = new Dictionary<string, int>(items.Count, StringComparer.Ordinal);
            foreach (FastaItemExtended item in items) firstPass[item.SequenceIdentifier] = item.USearchID;

            Dictionary<string, int> merged = MergeFirstPassClusters(firstPass, edges.Read(operation), identity, operation);

            foreach (FastaItemExtended item in items)
            {
                int id = merged[item.SequenceIdentifier];
                item.USearchID = id;
                item.UserID = id;
            }

            items.Sort((a, b) => a.USearchID.CompareTo(b.USearchID));
            return true;
        }

        /// <summary>
        /// The native pairwise table for a FASTA, memoised by full path and content SHA-256. A concurrent
        /// sweep asks for the same FASTA at many identities; only the first request runs the O(N^2)
        /// alignment and the rest read the cache. A FAILED or EMPTY alignment is dropped from the memo
        /// before returning, so it is retried next time instead of being remembered as "no edges".
        /// </summary>
        private static PairwiseEdgeFile? GetOrAlignPairwiseEdges(FileInfo fi, string usearchExe, OperationContext? operation = null)
        {
            operation?.ThrowIfCancellationRequested();
            fi.Refresh();
            string key = fi.FullName + "|" + FileFingerprint.Sha256(fi.FullName);

            Lazy<PairwiseEdgeFile?> lazy = PairwiseEdgeCache.GetOrAdd(
                key,
                _ => new Lazy<PairwiseEdgeFile?>(
                    () => AlignAllPairs(fi, usearchExe, operation),
                    LazyThreadSafetyMode.ExecutionAndPublication));

            PairwiseEdgeFile? edges;
            try { operation?.ThrowIfCancellationRequested(); edges = lazy.Value; operation?.ThrowIfCancellationRequested(); }
            catch (OperationCanceledException)
            {
                PairwiseEdgeCache.TryRemove(new KeyValuePair<string, Lazy<PairwiseEdgeFile?>>(key, lazy));
                throw;
            }
            catch { edges = null; }

            if (edges == null || edges.Count == 0)
            {
                // Only remove OUR lazy: a concurrent writer may already have replaced it with a good one.
                PairwiseEdgeCache.TryRemove(new KeyValuePair<string, Lazy<PairwiseEdgeFile?>>(key, lazy));
                return null;
            }

            if (!File.Exists(edges.Path))
            {
                PairwiseEdgeCache.TryRemove(new KeyValuePair<string, Lazy<PairwiseEdgeFile?>>(key, lazy));
                return GetOrAlignPairwiseEdges(fi, usearchExe, operation);
            }

            return edges;
        }

        /// <summary>
        /// Test seam: the memoised all-pairs edges for a FASTA (aligning once and caching, exactly as the
        /// exhaustive pass does). Returns null when the alignment fails or is empty.
        /// </summary>
        public static IReadOnlyList<PairwiseEdge>? GetPairwiseEdgesForTest(FileInfo fi, OperationContext? operation = null)
        {
            operation?.ThrowIfCancellationRequested();
            return GetOrAlignPairwiseEdges(fi, GetUSearchPath(), operation)?.Read(operation).ToList();
        }

        /// <summary>Streaming parser seam for format/gate tests without running a native alignment.</summary>
        public static IEnumerable<PairwiseEdge> ReadPairwiseEdgesForTest(string path, OperationContext? operation = null)
            => ParsePairwiseEdges(path, operation).Read(operation);

        /// <summary>
        /// Aligns every pair of sequences (<c>usearch -allpairs_global ... -acceptall</c>) and parses the
        /// query/target/id/alnlen/ql/tl table. Any failure returns null (rather than throwing) so the
        /// memo above never caches an exception, and the exhaustive pass falls back to fast-only.
        /// </summary>
        private static PairwiseEdgeFile? AlignAllPairs(FileInfo fi, string usearchExe, OperationContext? operation)
        {
            string tsvFile = Path.Combine(
                ApplicationPaths.ScratchDirectory,
                Path.GetFileNameWithoutExtension(fi.Name) + "_allpairs_" +
                Guid.NewGuid().ToString("N").Substring(0, 8) + ".tsv");

            // -userfields query+target+id+alnlen+ql+tl : query/target labels, identity (0-100), aligned
            // length, and the two full sequence lengths. Quote the FASTA path (spaces in user folders).
            string arguments = $"-allpairs_global \"{fi.FullName}\" -acceptall -userout \"{tsvFile}\" " +
                               "-userfields query+target+id+alnlen+ql+tl";

            Interlocked.Increment(ref pairwiseAlignmentRuns);
            bool retained = false;

            try
            {
                Directory.CreateDirectory(ApplicationPaths.ScratchDirectory);
                RunUSearch(usearchExe, arguments, fi.DirectoryName ?? Environment.CurrentDirectory, operation);

                if (!File.Exists(tsvFile)) return null;

                PairwiseEdgeFile edges = ParsePairwiseEdges(tsvFile, operation);
                if (edges.Count == 0) return null;
                OwnedPairwiseFiles.Add(tsvFile);
                retained = true;
                return edges;
            }
            catch (OperationCanceledException) { throw; }
            catch
            {
                // A blocked/removed USEARCH, a locked temp path, or an unreadable table: fall back to the
                // fast-only clustering for this call rather than failing the whole run.
                return null;
            }
            finally
            {
                if (!retained) TryDelete(tsvFile);
            }
        }

        /// <summary>
        /// Every hit of every query against a database at or above <paramref name="minIdentity"/> (a
        /// fraction), from <c>usearch -search_global</c>, which aligns every query with every target.
        /// The indexed <c>-usearch_global</c> is not used: even with no accept or reject limit its word
        /// index left out pairs depending on the order of the database (one venom query found 76 of the
        /// 175 targets above 50% in file order and 95 in reverse order). Hits are handed to
        /// <paramref name="onHit"/> as they are read, labels up to the first space. A failure throws.
        /// </summary>
        public static void SearchGlobal(string queryFasta, string databaseFasta, double minIdentity,
                                        Action<PairwiseEdge> onHit, OperationContext? operation = null)
        {
            string usearchExe = GetUSearchPath();
            string tempDir = ApplicationPaths.ScratchDirectory;
            Directory.CreateDirectory(tempDir);
            string tsvFile = Path.Combine(tempDir, "global_" + Guid.NewGuid().ToString("N") + ".tsv");
            string arguments = $"-search_global \"{queryFasta}\" -db \"{databaseFasta}\" " +
                               $"-id {minIdentity.ToString("0.######", CultureInfo.InvariantCulture)} " +
                               $"-maxaccepts 0 -maxrejects 0 -userout \"{tsvFile}\" " +
                               "-userfields query+target+id+alnlen+ql+tl";
            try
            {
                RunUSearch(usearchExe, arguments, tempDir, operation);
                if (!File.Exists(tsvFile))
                    throw new InvalidOperationException("USEARCH finished without writing its hit table.");
                foreach (string line in File.ReadLines(tsvFile))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    string[] cols = line.Split('	');
                    if (cols.Length < 6
                        || !double.TryParse(cols[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double id)
                        || !int.TryParse(cols[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int alnlen)
                        || !int.TryParse(cols[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int ql)
                        || !int.TryParse(cols[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out int tl))
                        throw new InvalidDataException("USEARCH wrote a hit line that cannot be read: \"" + line + "\".");
                    // Every reported hit is at or above the threshold, so one at or under 1.0 can only be a
                    // build reporting the fraction; ParsePairwiseEdges guards the same way.
                    if (id <= 1.0 && minIdentity > 0.01) id *= 100.0;
                    onHit(new PairwiseEdge(ExtractIdentifier(cols[0]), ExtractIdentifier(cols[1]), id, alnlen, ql, tl));
                }
            }
            finally
            {
                TryDelete(tsvFile);
            }
        }

        private static PairwiseEdgeFile ParsePairwiseEdges(string tsvFile, OperationContext? operation = null)
        {
            long count = 0;
            double maxId = 0.0;

            foreach (string line in File.ReadLines(tsvFile))
            {
                operation?.ThrowIfCancellationRequested();
                if (!TryReadPairwiseRow(line, false, out PairwiseEdge edge)) continue;
                count++;
                if (edge.IdentityPercent > maxId) maxId = edge.IdentityPercent;
            }

            // Preserve the old whole-column scale guard. The first streaming pass needs constant
            // memory and establishes the scale before any gate is applied in the second pass.
            return new PairwiseEdgeFile(tsvFile, count, maxId <= 1.0 && count > 0);
        }

        private static IEnumerable<PairwiseEdge> ReadPairwiseEdges(string tsvFile, bool fractionalIdentity,
            OperationContext? operation)
        {

            foreach (string line in File.ReadLines(tsvFile))
            {
                operation?.ThrowIfCancellationRequested();
                if (!TryReadPairwiseRow(line, true, out PairwiseEdge edge)) continue;
                yield return fractionalIdentity
                    ? new PairwiseEdge(edge.QueryId, edge.TargetId, edge.IdentityPercent * 100.0,
                        edge.AlignmentLength, edge.QueryLength, edge.TargetLength)
                    : edge;
            }
        }

        private static bool TryReadPairwiseRow(string line, bool readLabels, out PairwiseEdge edge)
        {
            edge = default;
            ReadOnlySpan<char> text = line.AsSpan();
            Span<Range> columns = stackalloc Range[7];
            if (text.Split(columns, '\t') < 6) return false;
            if (!double.TryParse(text[columns[2]], NumberStyles.Float, CultureInfo.InvariantCulture, out double id)
                || !int.TryParse(text[columns[3]], NumberStyles.Integer, CultureInfo.InvariantCulture, out int alnlen)
                || !int.TryParse(text[columns[4]], NumberStyles.Integer, CultureInfo.InvariantCulture, out int ql)
                || !int.TryParse(text[columns[5]], NumberStyles.Integer, CultureInfo.InvariantCulture, out int tl)) return false;
            edge = new PairwiseEdge(readLabels ? PairwiseIdentifier(text[columns[0]]) : string.Empty,
                readLabels ? PairwiseIdentifier(text[columns[1]]) : string.Empty, id, alnlen, ql, tl);
            return true;
        }

        private static string PairwiseIdentifier(ReadOnlySpan<char> label)
        {
            int space = label.IndexOf(' ');
            return (space < 0 ? label : label[..space]).ToString();
        }

        /// <summary>
        /// The identifier part of a USEARCH label: everything up to the first space, matching how
        /// ParseClusterFile splits a .uc label. A decoy label with no space is returned whole.
        /// </summary>
        private static string ExtractIdentifier(string label)
        {
            int firstSpace = label.IndexOf(' ');
            return firstSpace < 0 ? label : label.Substring(0, firstSpace);
        }

        /// <summary>
        /// Runs the USEARCH executable with stdout/stderr captured so failures surface as descriptive
        /// exceptions instead of crashing the caller. Both pipes are drained asynchronously to avoid a
        /// WaitForExit deadlock when USEARCH is chatty, and a failure to launch (blocked/removed exe) is
        /// translated into an actionable message.
        /// </summary>
        private static void RunUSearch(string exePath, string arguments, string workingDirectory, OperationContext? operation = null)
        {
            operation?.Report("usearch", "Running sequence alignment");
            ProcessStartInfo psi = new ProcessStartInfo(exePath, arguments)
            {
                UseShellExecute = false,        // required to redirect output; also avoids a console flash
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = string.IsNullOrEmpty(workingDirectory) ? Environment.CurrentDirectory : workingDirectory
            };

            StringBuilder output = new StringBuilder();
            DataReceivedEventHandler sink = (s, e) =>
            {
                if (e.Data != null)
                {
                    lock (output) output.AppendLine(e.Data);
                }
            };

            try
            {
                using (Process process = new Process { StartInfo = psi })
                {
                    process.OutputDataReceived += sink;
                    process.ErrorDataReceived += sink;

                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    bool exited;
                    if (operation == null) exited = process.WaitForExit(ProcessTimeout);
                    else
                    {
                        var timer = Stopwatch.StartNew();
                        while (!(exited = process.WaitForExit(100)) && timer.Elapsed < ProcessTimeout)
                        {
                            if (!operation.IsCancellationRequested) continue;
                            try { process.Kill(entireProcessTree: true); } catch { }
                            process.WaitForExit();
                            operation.ThrowIfCancellationRequested();
                        }
                    }
                    if (!exited)
                    {
                        try { process.Kill(entireProcessTree: true); } catch { }
                        process.WaitForExit();
                        throw new TimeoutException("USEARCH exceeded the 30-minute safety timeout and was stopped.");
                    }
                    operation?.ThrowIfCancellationRequested();
                    process.WaitForExit();   // ensure asynchronous output readers have drained

                    if (process.ExitCode != 0)
                    {
                        throw new InvalidOperationException(
                            "USEARCH exited with code " + process.ExitCode +
                            (output.Length > 0 ? "\n\n" + output.ToString().Trim() : "."));
                    }
                }
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                throw new InvalidOperationException(
                    "Could not launch USEARCH (\"" + exePath + "\").\n\n" +
                    "It may have been blocked or removed by security software (antivirus / AppLocker), or its " +
                    "folder is not allowed to execute. Set the FAMILYQUANT_USEARCH environment variable to a " +
                    "usearch.exe you control to bypass the bundled copy.\n\nDetails: " + ex.Message, ex);
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch
            {
                // best-effort cleanup; a leftover temp file is harmless
            }
        }

        private static List<FastaItemExtended> ParseClusterFile(string file, List<FastaItem> fastaItems, OperationContext? operation = null)
        {
            // Refuse a repeated identifier instead of quietly keeping the first: the duplicate would be
            // dropped from the .fcf entirely (the .uc dedup below discards it too), so its peptides would
            // never be attributed to any family and the protein count would be silently short.
            var locusSequenceDict = new Dictionary<string, string>();
            foreach (var item in fastaItems)
            {
                operation?.ThrowIfCancellationRequested();
                if (locusSequenceDict.ContainsKey(item.SequenceIdentifier))
                {
                    throw new InvalidOperationException(
                        "The database contains more than one sequence with the identifier \"" +
                        item.SequenceIdentifier + "\". Remove the duplicate before clustering - family " +
                        "membership and peptide attribution would be ambiguous.");
                }

                locusSequenceDict.Add(item.SequenceIdentifier, item.Sequence);
            }

            Dictionary<string, FastaItemExtended> theItems = new Dictionary<string, FastaItemExtended>();

            foreach (string line in File.ReadLines(file))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;   // tolerate blank/trailing lines

                // Plain Split over a fixed single-character delimiter; Regex.Split here cost a full regex
                // match per line for no gain, on files with one line per database sequence.
                string[] cols = line.Split('\t');
                int familyId = int.Parse(cols[1], CultureInfo.InvariantCulture);

                // Column N-2 of a .uc record is the query label: identifier first, then the description.
                string label = cols[cols.Length - 2];
                int firstSpace = label.IndexOf(' ');

                string seqIdentifier = firstSpace < 0 ? label : label.Substring(0, firstSpace);
                string description = firstSpace < 0 ? string.Empty : label.Substring(firstSpace + 1);

                if (theItems.ContainsKey(seqIdentifier)) continue;

                if (!locusSequenceDict.TryGetValue(seqIdentifier, out string? sequence))
                {
                    throw new InvalidOperationException(
                        "USEARCH reported a sequence (\"" + seqIdentifier + "\") that is not in " +
                        "the input database. The FASTA may have been modified while clustering was running.");
                }

                theItems.Add(seqIdentifier, new FastaItemExtended()
                {
                    USearchID = familyId,
                    UserID = familyId,
                    Description = description,
                    SequenceIdentifier = seqIdentifier,
                    Sequence = sequence
                });
            }

            List<FastaItemExtended> items = theItems.Values.ToList();

            items.Sort((a, b) => a.USearchID.CompareTo(b.USearchID));

            return items;
        }
    }
}
