using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant.Report;

public static partial class HtmlReportPayloadBuilder
{
    private sealed partial class Builder
    {
        private readonly Dictionary<string, string> runNameFields = new(StringComparer.OrdinalIgnoreCase);

        private string RunField(Frame frame, int row, int column)
        {
            string full = frame.Table.Rows[row][column];
            if (runNameFields.TryGetValue(full, out var existing)) return existing;
            var names = context.StoredStudies?.FamilyRunShares?.Runs.Select(r => r.Run)
                ?? workbookFrames.Values.Where(f => Column(f, "run") >= 0).SelectMany(f => f.Table.Rows.Select(r => r[Column(f, "run")]));
            string[] distinct = names.Append(full).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            string shortText = ShortRunSuffix(full, distinct);
            var reference = frame.Reference(row, column);
            string id = Add(full, reference, "", null);
            string shortId = Add(shortText, reference with { Format = "run-suffix" }, "", null);
            fields[id] = fields[id] with { Short = shortId };
            runNameFields.Add(full, id);
            return id;
        }

        private static string ShortRunSuffix(string name, IReadOnlyList<string> names)
        {
            static IEnumerable<int> Boundaries(string text)
            {
                yield return 0;
                for (int i = 1; i < text.Length; i++)
                    if (!char.IsLetterOrDigit(text[i - 1]) || char.IsLower(text[i - 1]) && char.IsUpper(text[i])
                        || char.IsLetter(text[i - 1]) && char.IsDigit(text[i])) yield return i;
            }
            foreach (int start in Boundaries(name).Reverse())
            {
                string suffix = name.Substring(start);
                if (suffix.Length == 0 || !char.IsLetterOrDigit(suffix[0])) continue;
                bool collision = names.Where(other => !other.Equals(name, StringComparison.OrdinalIgnoreCase)).Any(other =>
                    other.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && Boundaries(other).Contains(other.Length - suffix.Length));
                if (!collision) return suffix;
            }
            return name;
        }
    }
}
