using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FamilyQuant.Tests;

internal static partial class FeatureLedgerEvidence
{
    private sealed record RulingContext(LedgerPauloRulings.Packet Packet, List<LedgerPauloRulings.Input> Inputs, string[] Rules,
        Dictionary<string, LedgerPauloRulings.FactCheck> Facts, Dictionary<string, LedgerPauloRulings.FactCheck> CurrentFacts);

    private static void ValidateRuling(string root, FeatureLedger.Inventory inventory, Register register, Row row,
        FeatureLedger.Observation? observation, Proof proof, Run run, string full,
        Dictionary<string, RulingContext> contexts, Func<string, string> resolve, List<string> problems)
    {
        if (register.Step != LedgerPauloRulings.Step || observation == null || row.KindReceiptId != null
            || row.Proofs.Count != 1 || proof.InspectedBy != "Paulo"
            || !Regex.IsMatch(run.Command, @"(?:^|\s)--ledger-apply-rulings(?:\s|$)", RegexOptions.CultureInvariant))
        { problems.Add("Ruling proof is only valid for the dedicated Paulo-rulings producer and one listed observation."); return; }
        string contextKey = register.Step + "\n" + run.Id + "\n" + full;
        if (!contexts.TryGetValue(contextKey, out var context))
        {
            var packet = JsonSerializer.Deserialize<LedgerPauloRulings.Packet>(File.ReadAllText(full), JsonOptions)
                ?? throw new InvalidDataException("Empty ruling decision packet.");
            if (packet.SchemaVersion != 2 || packet.Producer != LedgerPauloRulings.Command
                || packet.RuleSourceSha256 != LedgerPauloRulings.RulesSha256 || packet.Decisions == null
                || string.IsNullOrWhiteSpace(packet.TsvArtifact) || string.IsNullOrWhiteSpace(packet.RulesArtifact)
                || string.IsNullOrWhiteSpace(packet.FactChecksArtifact) || string.IsNullOrWhiteSpace(packet.FactChecksSha256)
                || packet.Decisions.Any(decision => decision == null || decision.OldText == null || decision.NewText == null))
                throw new InvalidDataException("Ruling packet is not from the recorded command/rules.");
            string tsv = resolve(packet.TsvArtifact), rules = resolve(packet.RulesArtifact), facts = resolve(packet.FactChecksArtifact);
            bool Declared(string path, string sha) => run.Artifacts.Count(artifact => resolve(artifact.Path).Equals(path, StringComparison.OrdinalIgnoreCase)
                && artifact.Sha256.Equals(sha, StringComparison.OrdinalIgnoreCase)) == 1 && File.Exists(path) && Hash(path) == sha;
            if (!Declared(tsv, packet.TsvSha256) || !Declared(rules, LedgerPauloRulings.RulesSha256) || !Declared(facts, packet.FactChecksSha256))
                throw new InvalidDataException("Ruling input/rules/fact verdicts are not byte-pinned execution artifacts.");
            var lines = File.ReadAllLines(rules).Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
            if (lines.Length != 3 || !Enumerable.Range(1, 3).All(number => lines[number - 1].StartsWith("Rule " + number + ", Paulo 2026-11-02:", StringComparison.Ordinal)))
                throw new InvalidDataException("The recorded three Paulo rules are required.");
            context = new(packet, LedgerPauloRulings.ReadInputs(tsv), lines, LedgerPauloRulings.ReadFactChecks(facts),
                File.Exists(LedgerPauloRulings.FactChecksPath) ? LedgerPauloRulings.ReadFactChecks(LedgerPauloRulings.FactChecksPath) : new(StringComparer.Ordinal));
            contexts.Add(contextKey, context);
        }
        var decisions = context.Packet.Decisions.Where(decision => decision.Id == observation.Id && decision.Closed).ToArray();
        if (decisions.Length != 1) { problems.Add("Ruling requires exactly one closed decision for this observation."); return; }
        var decision = decisions[0];
        var listed = context.Inputs.Where(input => input.Id == observation.Id && input.OldText == decision.OldText && input.NewText == decision.NewText).ToArray();
        if (listed.Length == 0) { problems.Add("Ruling observation/text pair is not in the command's pinned TSV input."); return; }
        if (observation.Source.EndsWith(".xaml", StringComparison.Ordinal) && (observation.Detail.StartsWith("Style.", StringComparison.Ordinal)
            || observation.Detail.StartsWith("Setter.", StringComparison.Ordinal)))
        {
            string frozen = Path.Combine(LedgerPauloRulings.FrozenRoot, observation.Source);
            if (Hash(frozen) != inventory.Files.Single(file => file.Path == observation.Source).Sha256)
            { problems.Add("Frozen style source differs from inventory."); return; }
        }
        if (decision.Rule is "1" or "3" && (!context.CurrentFacts.TryGetValue(observation.Id, out var currentFact) || currentFact.Verdict != "kept"))
        { problems.Add("Current independent fact verdict is missing or lost; wording ruling stays OPEN."); return; }
        var evaluated = LedgerPauloRulings.Evaluate(listed[0], observation, LedgerRulingLiveText.Find, context.Facts);
        if (!evaluated.Closed || evaluated.Rule != decision.Rule || evaluated.WitnessAssembly != decision.WitnessAssembly
            || evaluated.WitnessSymbol != decision.WitnessSymbol || evaluated.WitnessTextSha256 != decision.WitnessTextSha256 || evaluated.FactCheckNote != decision.FactCheckNote)
        { problems.Add("Ruling policy/live compiled text no longer accepts this exact row and wording."); return; }
        if (!int.TryParse(decision.Rule, out int number) || number is < 1 or > 3 || proof.Target != "Rule " + number
            || proof.Assertion != RulingAssertion(context.Rules[number - 1], decision)
            || row.Compared != RulingComparison(decision))
            problems.Add("Ruling must cite its exact recorded rule, old text and new text.");
    }

    internal static string RulingAssertion(string rule, LedgerPauloRulings.Decision decision) =>
        rule + "\nOld: " + decision.OldText + "\nNew: " + decision.NewText;
    internal static string RulingComparison(LedgerPauloRulings.Decision decision) => "Old: " + decision.OldText + "\nNew: " + decision.NewText;
}
