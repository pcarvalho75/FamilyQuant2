using System.Collections.Generic;

namespace FamilyQuant.Panels;

/// <summary>User data defining the families to measure and the text to print. Keys are user supplied,
/// stable and case sensitive; names and source decision labels never act as automatic rules.</summary>
public sealed partial class FamilyPanel
{
    public string Name { get; set; } = string.Empty;
    public List<FamilyPanelFamily> Families { get; set; } = new();
    public List<FamilyPanelDecisionMapping> DecisionMappings { get; set; } = new();
    public List<FamilyPanelText> Levels { get; set; } = new();
    public List<FamilyPanelText> TextFields { get; set; } = new();
    public FamilyPanelImport? Import { get; set; }
    public List<FamilyPanelEvidenceLink> EvidenceLinks { get; set; } = new();
}

public enum FamilyPanelDecision { Quantify, ReportUnderOthers, NotAFamily, MergeIntoAnotherFamily }

public sealed class FamilyPanelFamily
{
    public string Key { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    /// <summary>Additional names, for example one named field for each language.</summary>
    public List<FamilyPanelText> Names { get; set; } = new();
    public string SourceDecision { get; set; } = string.Empty;
    public FamilyPanelDecision Decision { get; set; }
    public string MergeTargetFamilyKey { get; set; } = string.Empty;
    public List<FamilyPanelDomain> Pfam { get; set; } = new();
    public List<FamilyPanelSubclass> Subclasses { get; set; } = new();
    /// <summary>Original named text, including literature abundance, notes, sources and levels.
    /// Values are printed verbatim, never parsed as measured quantities or classification rules.</summary>
    public List<FamilyPanelText> TextFields { get; set; } = new();
}

public sealed class FamilyPanelText
{
    public string Name { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
}

public sealed class FamilyPanelDomain
{
    public string Accession { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

/// <summary>One user rule: all required domains present and all absent domains absent, or an exact
/// source subfamily match. Missing domain evidence cannot establish a negative-only rule.</summary>
public sealed class FamilyPanelSubclass
{
    public string Key { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public List<string> RequiredPfam { get; set; } = new();
    public List<string> AbsentPfam { get; set; } = new();
    public List<string> UniProtSubfamilies { get; set; } = new();
    /// <summary>Optional key in the same family's subclass list to which this part is reported.</summary>
    public string ReportAsSubclassKey { get; set; } = string.Empty;
    public List<FamilyPanelText> TextFields { get; set; } = new();
}

public sealed class FamilyPanelDecisionMapping
{
    public string SourceValue { get; set; } = string.Empty;
    public FamilyPanelDecision Decision { get; set; }
    public string MergeTargetFamilyKey { get; set; } = string.Empty;
}

/// <summary>Replay information for a user-confirmed import. ColumnHeaders maps importer field keys
/// to the user's actual headers. FamilySheet and the other sheet names are user data.</summary>
public sealed partial class FamilyPanelImport
{
    public string SourceName { get; set; } = string.Empty;
    public string SourceSha256 { get; set; } = string.Empty;
    public string FamilySheet { get; set; } = string.Empty;
    public int HeaderRowNumber { get; set; } = 1;
    public string SubclassSheet { get; set; } = string.Empty;
    public string LevelSheet { get; set; } = string.Empty;
    public Dictionary<string, string> ColumnHeaders { get; set; } = new();
}
