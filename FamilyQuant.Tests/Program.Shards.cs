using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;

namespace FamilyQuant.Tests;

// The default run as an ordered table, so N processes can each run a share of it at once. Entry i belongs to
// shard i mod N, except that entries of one group (a later entry reads what an earlier one left in a static
// field) all follow the group's first entry. A serial run (no --shard) executes every entry with exactly the
// former calls; a shard only adds the shared WPF Application that an earlier entry would already have created.
internal static partial class Program
{
    private sealed record SuiteEntry(string Section, Action Body, bool CreatesApplication, bool AfterApplication, int Leader);

    private sealed class SuiteTable
    {
        private string section = "";
        private bool applicationCreated;
        private readonly Dictionary<string, int> leaders = new(StringComparer.Ordinal);
        internal List<SuiteEntry> Entries { get; } = new();
        internal void Section(string title) => section = title;

        /// <summary>Adds one entry; entries sharing <paramref name="group"/> run in the shard of the group's first.</summary>
        internal void Add(Action body, string? group = null) => Entries.Add(new(section, body, false, applicationCreated, Leader(group)));

        /// <summary>An entry the serial run preceded with <see cref="EnsureApplication"/>.</summary>
        internal void AddWithApplication(Action body)
        {
            Entries.Add(new(section, body, true, applicationCreated, Entries.Count));
            applicationCreated = true;
        }

        private int Leader(string? group)
        {
            if (group == null) return Entries.Count;
            if (!leaders.TryGetValue(group, out int leader)) leaders[group] = leader = Entries.Count;
            return leader;
        }
    }

    private readonly record struct SuiteShard(int Index, int Count)
    {
        internal const string Flag = "--shard";
        internal bool Runs(SuiteEntry entry) => entry.Leader % Count == Index;
    }

    /// <summary>Removes <c>--shard k/N</c> from the arguments, so the positional data slots keep their positions.
    /// Without it the whole suite runs as one shard.</summary>
    private static SuiteShard TakeSuiteShard(ref string[] args)
    {
        int at = Array.FindIndex(args, a => string.Equals(a, SuiteShard.Flag, StringComparison.OrdinalIgnoreCase));
        if (at < 0) return new(0, 1);
        string value = at + 1 < args.Length ? args[at + 1] : "";
        string[] parts = value.Split('/');
        if (parts.Length != 2 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int index)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int count) || count < 1 || index >= count)
            throw new ArgumentException("Use " + SuiteShard.Flag + " k/N with 0 <= k < N, for example " + SuiteShard.Flag + " 0/4; got '" + value + "'.");
        args = args.Take(at).Concat(args.Skip(at + 2)).ToArray();
        return new(index, count);
    }

    private static void RunSuite(SuiteTable suite, SuiteShard shard)
    {
        if (shard.Count > 1)
            Console.WriteLine("Shard " + shard.Index.ToString(CultureInfo.InvariantCulture) + " of " + shard.Count.ToString(CultureInfo.InvariantCulture)
                + ": " + suite.Entries.Count(shard.Runs).ToString(CultureInfo.InvariantCulture) + " of "
                + suite.Entries.Count.ToString(CultureInfo.InvariantCulture) + " entries.");
        string? shown = null;
        var durations = new List<(string Section, double Seconds)>();
        foreach (SuiteEntry entry in suite.Entries)
        {
            if (!shard.Runs(entry)) continue;
            if (entry.Section != shown) { Section(entry.Section); shown = entry.Section; }
            if (entry.CreatesApplication || shard.Count > 1 && entry.AfterApplication && Application.Current == null) EnsureApplication();
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            entry.Body();
            durations.Add((entry.Section, System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalSeconds));
        }
        // A shard names its three longest entries, so the runner's slowest process can be traced to its cause.
        if (shard.Count > 1)
            Console.WriteLine("Shard " + shard.Index.ToString(CultureInfo.InvariantCulture) + " longest entries: " + string.Join("; ",
                durations.OrderByDescending(d => d.Seconds).Take(3).Select(d => d.Section + " " + d.Seconds.ToString("F0", CultureInfo.InvariantCulture) + " s")));
    }
}
