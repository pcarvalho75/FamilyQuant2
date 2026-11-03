using System;

namespace FamilyQuant;

/// <summary>Protein-name text for new report, supplement and audit outputs. Removes only the
/// header organism tail, preserving already-clean text, spacing and qualifiers. Stored data and
/// historical export formats are unchanged.</summary>
public static class ProteinNameFormatting
{
    public static string ForNewOutput(string? description)
    {
        string text = description ?? string.Empty;
        int organism = text.IndexOf(" OS=", StringComparison.Ordinal);
        return organism < 0 ? text : text.Substring(0, organism);
    }
}
