using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FamilyQuant.DatabaseUpdate
{
    /// <summary>What happened to one protein when the project's database was replaced by a newer file
    /// (docs/design/DATABASE-UPDATE.md, section 3).</summary>
    public enum DatabaseUpdateKind
    {
        Unchanged,
        HeaderChanged,
        SequenceChanged,
        NewAccessionSameSequence,
        ReplacedByLonger,
        Removed,
        Added,
        Ambiguous,
    }

    /// <summary>The matching step whose test paired a protein, or found it ambiguous.</summary>
    public enum DatabaseUpdateStep { Key, RepeatedKey, SameSequence, Containment }

    /// <summary>How an added protein received its family (section 4); None before placement.</summary>
    public enum DatabaseUpdatePlacementKind { None, NearestKeptNeighbour, FollowedAddedRelative, NewFamily }

    /// <summary>One database file: its name, its SHA-256 and its record count.</summary>
    public sealed class DatabaseSourceSummary
    {
        public string FileName { get; set; } = string.Empty;
        public string Sha256 { get; set; } = string.Empty;
        public int SequenceCount { get; set; }
    }

    /// <summary>Counts over target rows, or over decoy rows in <see cref="DatabaseUpdateRecord.DecoyCounts"/>.
    /// The placement and apply counts stay zero until those stages ran.</summary>
    public sealed class DatabaseUpdateCounts
    {
        public int Unchanged { get; set; }
        public int HeaderChanged { get; set; }
        public int SequenceChanged { get; set; }
        public int NewAccessionSameSequence { get; set; }
        public int ReplacedByLonger { get; set; }
        public int Removed { get; set; }
        public int Added { get; set; }
        public int AmbiguousCases { get; set; }
        public int AmbiguousOldRows { get; set; }
        public int AmbiguousNewRecords { get; set; }
        public int AddedJoinedFamilies { get; set; }
        public int AddedJoinedLockedFamilies { get; set; }
        public int AddedInNewFamilies { get; set; }
        public int NewFamilies { get; set; }
        public int AddedCloseToSeveralFamilies { get; set; }
        public int FamiliesEmptied { get; set; }
        public int NamesKept { get; set; }
    }

    /// <summary>One protein that is not unchanged. Old fields describe the project row, new fields the
    /// record of the new file; a removed protein has no new side and an added one no old side.</summary>
    public sealed class DatabaseUpdateRow
    {
        public DatabaseUpdateKind Kind { get; set; }
        public DatabaseUpdateStep? Step { get; set; }
        public bool Picked { get; set; }
        public int? AmbiguousCase { get; set; }
        public bool Decoy { get; set; }
        public bool Contaminant { get; set; }
        public int? OldIndex { get; set; }
        public int? NewIndex { get; set; }
        public string OldIdentifier { get; set; } = string.Empty;
        public string NewIdentifier { get; set; } = string.Empty;
        public int? FamilyUserId { get; set; }
        public int? FamilySearchId { get; set; }
        public string FamilyName { get; set; } = string.Empty;
        public DatabaseUpdatePlacementKind Placement { get; set; }
        public string PlacedBy { get; set; } = string.Empty;
        public double? PlacedByIdentityPercent { get; set; }
        public int? PlacedByAlignmentLength { get; set; }
        public bool JoinedLockedFamily { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<int>? OtherLinkedFamilies { get; set; }
        public string Note { get; set; } = string.Empty;
    }

    /// <summary>A family whose shown name would change (section 4) and the user's Keep the old name choice.</summary>
    public sealed class DatabaseUpdateNameChange
    {
        public int FamilyUserId { get; set; }
        public string OldName { get; set; } = string.Empty;
        public string NewName { get; set; } = string.Empty;
        public bool OldNameFromAnnotation { get; set; }
        /// <summary>Identifiers of surviving members whose different User Annotation keeping would replace.</summary>
        public List<string> DifferingAnnotationMembers { get; set; } = new();
        public bool KeepByDefault { get; set; }
        public bool Kept { get; set; }
    }

    /// <summary>The persisted account of one database update, kept with the project (section 8).</summary>
    public sealed class DatabaseUpdateRecord
    {
        public DateTimeOffset UpdatedAtUtc { get; set; }
        public string ProgramVersion { get; set; } = string.Empty;
        public string Method { get; set; } = string.Empty;
        public DatabaseSourceSummary Previous { get; set; } = new();
        public DatabaseSourceSummary New { get; set; } = new();
        public int ClusteringIdentity { get; set; }
        public bool ExhaustivePlacement { get; set; }
        public DatabaseUpdateCounts Counts { get; set; } = new();
        public DatabaseUpdateCounts DecoyCounts { get; set; } = new();
        public List<DatabaseUpdateRow> Rows { get; set; } = new();
        public List<DatabaseUpdateNameChange> NameChanges { get; set; } = new();
        /// <summary>Identifiers whose User Annotation a kept family name was written into.</summary>
        public List<string> AnnotationsWritten { get; set; } = new();
    }

    /// <summary>One pairing of a project row with a record of the new file.</summary>
    public sealed record DatabaseUpdateMatch(int OldIndex, int NewIndex, DatabaseUpdateKind Kind, DatabaseUpdateStep Step, bool Picked);

    /// <summary>A case the matching could not decide: every candidate on both sides, and the step that found it.
    /// Case numbers are stable for the same project and file, so a pick refers to one case.</summary>
    public sealed record DatabaseUpdateAmbiguity(int Case, DatabaseUpdateStep Step, IReadOnlyList<int> OldIndices,
        IReadOnlyList<int> NewIndices, string Reason);

    /// <summary>The user's choice of one pair inside an ambiguous case.</summary>
    public sealed record DatabaseUpdatePick(int Case, int OldIndex, int NewIndex);
}

namespace FamilyQuant
{
    public partial class FamilyClusterFile
    {
        /// <summary>Every database update applied to this project, oldest first. Absent in projects never updated,
        /// so their files keep their bytes.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<DatabaseUpdate.DatabaseUpdateRecord>? DatabaseUpdates { get; set; }
    }
}
