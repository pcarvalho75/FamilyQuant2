using System;
using System.Collections.Generic;

namespace FamilyQuant;

/// <summary>Exact multi-pattern containment for database-protein support. The trie follows failure
/// links so the database is scanned once, retaining overlapping and suffix matches. I/L is folded
/// while scanning, matching the sequence identity used by the quantitation pipeline.</summary>
internal sealed class CompositionPeptideIndex
{
    private sealed class Node
    {
        internal readonly Dictionary<char, int> Next = new();
        internal readonly List<int> Outputs = new();
        internal int Failure;
    }

    private readonly List<Node> nodes = new() { new() };

    internal CompositionPeptideIndex(IReadOnlyList<string> peptides)
    {
        for (int i = 0; i < peptides.Count; i++)
        {
            int state = 0;
            foreach (char residue in peptides[i])
            {
                if (!nodes[state].Next.TryGetValue(residue, out int next))
                {
                    next = nodes.Count;
                    nodes[state].Next.Add(residue, next);
                    nodes.Add(new());
                }
                state = next;
            }
            nodes[state].Outputs.Add(i);
        }
        var queue = new Queue<int>();
        foreach (int child in nodes[0].Next.Values) queue.Enqueue(child);
        while (queue.TryDequeue(out int state))
        {
            foreach (var edge in nodes[state].Next)
            {
                int failure = nodes[state].Failure;
                while (failure != 0 && !nodes[failure].Next.ContainsKey(edge.Key)) failure = nodes[failure].Failure;
                if (nodes[failure].Next.TryGetValue(edge.Key, out int next)) failure = next;
                nodes[edge.Value].Failure = failure;
                nodes[edge.Value].Outputs.AddRange(nodes[failure].Outputs);
                queue.Enqueue(edge.Value);
            }
        }
    }

    internal HashSet<int> Match(string sequence)
    {
        var found = new HashSet<int>();
        int state = 0;
        foreach (char letter in sequence)
        {
            char residue = char.ToUpperInvariant(letter);
            if (residue == 'I') residue = 'L';
            while (state != 0 && !nodes[state].Next.ContainsKey(residue)) state = nodes[state].Failure;
            state = nodes[state].Next.TryGetValue(residue, out int next) ? next : 0;
            foreach (int index in nodes[state].Outputs) found.Add(index);
        }
        return found;
    }
}
