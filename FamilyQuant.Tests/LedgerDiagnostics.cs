using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant.Tests;

internal static class LedgerDiagnostics
{
    internal static string Capped(IEnumerable<string> messages, int maximum = 5, string separator = "; ")
    {
        ArgumentNullException.ThrowIfNull(messages);
        if (maximum < 1) throw new ArgumentOutOfRangeException(nameof(maximum));
        var all = messages.ToArray();
        return FormattableString.Invariant($"showing the first {Math.Min(maximum, all.Length)} of {all.Length}: ")
            + string.Join(separator, all.Take(maximum));
    }
}
