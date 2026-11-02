using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace FamilyQuant;

/// <summary>The unchanged four-column homology assignment export, shared by Results and legacy callers.</summary>
public static class HomologyCsv
{
    public static void Write(string path, DeNovoHomology homology)
    {
        ArgumentNullException.ThrowIfNull(homology);
        var csv = new StringBuilder();
        csv.AppendLine("peptide,family_ids,protein_ids,ambiguous");
        foreach (var assignment in homology.Assignments)
            csv.AppendLine(string.Join(",", Field(assignment.Peptide),
                Field(string.Join(";", assignment.FamilyIds.Select(id => id.ToString(CultureInfo.InvariantCulture)))),
                Field(string.Join(";", assignment.ProteinIds)), assignment.IsAmbiguous ? "true" : "false"));
        File.WriteAllText(path, csv.ToString());
    }
    private static string Field(string? value) => CsvFormatting.AlwaysQuoted(value);
}
