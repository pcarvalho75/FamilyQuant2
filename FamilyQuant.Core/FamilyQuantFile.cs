using PatternTools.PLP;
using FamilyQuant.Operations;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FamilyQuant
{
    public partial class FamilyClusterFile
    {
        private readonly PatternLabAttribution patternLabAttribution = new PatternLabAttribution();

        public int ClusteringIdentity { get; set; }

        /// <summary>SHA-256 of the FASTA/.T-R content used to create this clustering.</summary>
        public string SourceSha256 { get; set; } = string.Empty;

        /// <summary>Number of FASTA records in the source at clustering time.</summary>
        public int SourceSequenceCount { get; set; }

        /// <summary>Algorithm semantics, not merely the executable name.</summary>
        public string ClusteringMethod { get; set; } = string.Empty;

        /// <summary>
        /// Cosine threshold of the last word-based reclustering applied to these families, or null if the
        /// grouping is still the raw USEARCH one. Persisted, so reopening a file shows how its families
        /// were built instead of silently looking like a plain identity clustering.
        /// </summary>
        public double? WordSimilarity { get; set; }

        /// <summary>Set when the families were merged by keyword rather than by a similarity threshold.</summary>
        public bool IsKeywordSuperCluster { get; set; }

        /// <summary>
        /// Families the user curated BY HAND, as UserIDs. Automatic reclustering must never merge a
        /// locked family into anything: the bench report that forced this was a phospholipase A2
        /// inhibitor separated manually from the toxin and re-merged by the next word reclustering,
        /// legitimately under its own similarity rules, because the two DESCRIPTIONS are nearly
        /// identical - which is exactly why the split had to be manual in the first place.
        /// Persisted with the file, so the protection survives a save and reload.
        /// </summary>
        public HashSet<int> LockedUserIds { get; set; } = new HashSet<int>();

        /// <summary>Where the proteins' domains came from, such as "UniProt 2026_03"; empty until fetched.
        /// Annotation changes between releases, so the file records which one its domains are.</summary>
        public string DomainSource { get; set; } = string.Empty;

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Evidence.EvidenceProvenance? EvidenceSource { get; set; }

        /// <summary>Verified legacy ordered task hashes bound to the same protein multiset on load.
        /// Kept outside stored worlds so their content hashes and result provenance remain unchanged.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Dictionary<string, string>? LegacyTaskSourceFingerprints { get; set; }

        public List<FastaItemExtended> MyExtendedFastaItems { get; set; } = new List<FastaItemExtended>();

        /// <summary>How the current grouping was produced, for display.</summary>
        public string GroupingDescription()
        {
            if (IsKeywordSuperCluster) return "keywords";
            if (WordSimilarity.HasValue) return WordSimilarity.Value.ToString("0.##", CultureInfo.CurrentCulture);
            return "none";
        }

        /// <summary>
        /// The families as they currently stand, on the curated grouping.
        ///
        /// This grouped by USearchID. That field now tracks the last automatic grouping (it moves with
        /// reclustering for unlocked families and diverges from UserID only where a family was
        /// renumbered by hand), so grouping by it would report the automatic grouping rather than the
        /// curated one that CalculateGroupingScore and CalculateFDR are meant to score. The curated
        /// grouping is UserID, so this groups by UserID.
        /// </summary>
        [JsonIgnore]
        public List<IGrouping<int, FastaItemExtended>> GroupedFastaItems
        {
            get
            {
                return (from mf in MyExtendedFastaItems
                        group mf by mf.UserID into thegroups
                        select thegroups).ToList();
            }
        }

        public FamilyClusterFile()
        {

        }

        public void SetLookupDictionary(PatternLabProject MyPLP, OperationContext? operation = null) =>
            patternLabAttribution.SetLookupDictionary(MyPLP, operation);

        public void AttributeXICToGroups(PatternLabProject MyPLP, NormalizationMode mode, SharedPeptidePolicy sharedPolicy,
            OperationContext? operation = null) =>
            patternLabAttribution.AttributeXICToGroups(this, MyPLP, mode, sharedPolicy, operation);

        public GroupingScore CalculateGroupingScore(string decoyTag = SequenceLabels.DecoyPrefix) =>
            FamilyClusterScoring.CalculateGroupingScore(this, decoyTag);

        public double CalculateFDR(string decoyTag = SequenceLabels.DecoyPrefix) =>
            FamilyClusterScoring.CalculateFDR(this, decoyTag);

        public static List<IdentityScanRow> ScanIdentities(string fileName, int from = 95, int to = 10,
            int step = 5, string decoyTag = SequenceLabels.DecoyPrefix, bool exhaustive = true, OperationContext? operation = null) =>
            FamilyClusterScoring.ScanIdentities(fileName, from, to, step, decoyTag, exhaustive, operation);

        public static double DivideAndConquer(string fileName, double acceptableScore = 0.0,
            string decoyTag = SequenceLabels.DecoyPrefix, bool exhaustive = true, OperationContext? operation = null) =>
            FamilyClusterScoring.DivideAndConquer(fileName, acceptableScore, decoyTag, exhaustive, operation);

        public static DivideAndConquerResult DivideAndConquerDetailed(string fileName, double acceptableScore = 0.0,
            string decoyTag = SequenceLabels.DecoyPrefix, bool exhaustive = true, OperationContext? operation = null) =>
            FamilyClusterScoring.DivideAndConquerDetailed(fileName, acceptableScore, decoyTag, exhaustive, operation);

        /// <summary>
        /// Canonical format-1 serialization options for hashes, detached copies and legacy text.
        /// Format-2 file and project writers derive their options from these shared defaults.
        /// </summary>
        public static readonly JsonSerializerOptions CurationJsonOptions = new JsonSerializerOptions
        {
            WriteIndented = false,
        };

        /// <summary>Serialises canonical format-1 content for compatibility text, hashes and copies.
        /// Save and the project container write a format-2 frame around the persisted curation.</summary>
        public string ToJsonText() => JsonSerializer.Serialize(this, CurationJsonOptions);

        public static FamilyClusterFile Load(string fileName, OperationContext? operation = null)
        {
            operation?.Report("document-read", "Opening family document");
            try
            {
                // Hold a read-only handle across a legacy encoding retry as well.
                using var held = File.OpenRead(fileName);
                var document = ReadJsonUtf8(() => File.OpenRead(fileName));
                operation?.ThrowIfCancellationRequested();
                return document;
            }
            catch (InvalidDataException)
            {
                // Re-raise with the file name the caller can act on; the JSON-shaped failures keep their
                // own type so the corrupt-file path still throws loudly.
                throw new InvalidDataException("\"" + fileName + "\" is not a valid Family Cluster File.");
            }
        }

        /// <summary>
        /// Deserialises curation from its JSON text and applies the pre-lock migration. Both
        /// <see cref="Load"/> (reading a .fcf) and the .fqproj container (reading its curation.json entry)
        /// go through here, so a project opened either way arrives with identical families, locks and
        /// flags, migration included.
        /// </summary>
        public static FamilyClusterFile FromJsonText(string theFile)
        {
            if (CurationFormat.Detect(theFile) == CurationFormat.Current)
                return ReadCurationFormat2(() => new MemoryStream(System.Text.Encoding.UTF8.GetBytes(theFile), writable: false));
            FamilyClusterFile loaded = JsonSerializer.Deserialize<FamilyClusterFile>(theFile)
                ?? throw new InvalidDataException("The text is not a valid Family Cluster File.");
            return FinishJsonLoad(loaded, theFile.Contains("\"LockedUserIds\""));
        }

        private static FamilyClusterFile FinishJsonLoad(FamilyClusterFile loaded, bool hasLockField)
        {

            // Migration for files written before curation locks existed - but ONLY for files that
            // never went through a reclustering. In a never-reclustered file the two ids start equal,
            // so a divergent row can only mean the USER typed it: that is curation, and it must
            // arrive protected. Both sides of the split are locked, matching what the live edit
            // handler records.
            //
            // The reclustering condition is not decoration. Build 1.0.8.0 already wrote only UserID
            // and renumbered clusters densely, so a file saved by it AFTER a word reclustering
            // diverges on nearly every row - and inferring curation from that would lock nearly every
            // family, permanently and invisibly, trading the reported defects for a worse one. The
            // final review before deploy caught exactly this. WordSimilarity and
            // IsKeywordSuperCluster are persisted by every build that could recluster, so they are
            // the reliable witnesses. The residual, documented in the release notes: curation typed
            // into a 1.0.8.0 file AFTER a reclustering cannot be told apart from the renumbering and
            // does not arrive locked - re-typing the number once in the current build locks it.
            if (!hasLockField
                && loaded.WordSimilarity == null && !loaded.IsKeywordSuperCluster)
            {
                foreach (var item in loaded.MyExtendedFastaItems)
                {
                    if (item.UserID != item.USearchID)
                    {
                        loaded.LockedUserIds.Add(item.UserID);
                        loaded.LockedUserIds.Add(item.USearchID);
                    }
                }
            }

            Evidence.EvidenceTaskDiscovery.BindLegacySources(loaded);
            return loaded;
        }

        public void Save(string fileName, OperationContext? operation = null)
        {
            // Written to a temporary file and moved into place, so a process killed, a power loss or a
            // full disk mid-write leaves the previous .fcf intact rather than a truncated one that
            // FromJsonText would reject, losing all curation. Mirrors ProjectStore.WriteFqproj. The shared
            // options keep a .fcf and a .fqproj's curation.json byte-identical for the same file.
            operation?.Report("save", "Preparing the family document");
            LastSaveCheck = null;
            var expected = Evidence.SavedFileCheck.Capture(this);
            string temp = fileName + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var destination = File.Create(temp)) WriteCurationFile(destination);
                LastSaveCheck = Evidence.SavedFileCheck.Verify(expected, temp, project: false, operation);
                Evidence.SavedFileCheck.RequirePassed(LastSaveCheck);
                operation?.Report("save", "Publishing the family document", 1, 1);
                void Publish() => File.Move(temp, fileName, overwrite: true);
                if (operation == null) Publish();
                else operation.Commit(Publish);
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            }
        }

    }
}
