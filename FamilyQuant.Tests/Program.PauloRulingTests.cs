using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void PauloRulingGuardTests(string root, FeatureLedger.Inventory inventory, string output, string timestamp)
    {
        Section("Paulo rulings - narrow policy and receipt boundary");
        var facts = LedgerPauloRulings.ReadFactChecks(LedgerPauloRulings.FactChecksPath);
        var input = LedgerPauloRulings.ReadInputs(LedgerPauloRulings.InputPath).First(candidate => candidate.Id == "FQ15-03078C4C9E10"
            && candidate.NewText == "The number of the family this protein belongs to now.");
        var row = inventory.Observations.Single(observation => observation.Id == input.Id);
        LedgerPauloRulings.Decision Decide(LedgerPauloRulings.Input value, FeatureLedger.Observation observed) =>
            LedgerPauloRulings.Evaluate(value, observed, LedgerRulingLiveText.Find, facts);
        var accepted = Decide(input, row);
        Check("a real compiled current caption can support recorded Rule 1", accepted.Closed && accepted.Rule == "1");
        Check("test-only strings cannot serve as current program text", !LedgerRulingLiveText.Find("Synthetic ruling sentinel 710928399 - absent from installed code").Found);
        Check("empty new text cannot close a ruling row", !Decide(input with { NewText = "" }, row).Closed);
        Check("bare newline cannot close a ruling row", !Decide(input with { NewText = "\r\n" }, row).Closed);
        Check("capture placeholder cannot close a ruling row", !Decide(input with { NewText = "<No exact current value captured>" }, row).Closed);
        // Identical old and new text: Rule 1 only with an independent kept verdict and a live text witness.
        var identical = input with { NewText = input.OldText };
        var identicalKept = Decide(identical, row);
        Check("identical wording with a kept verdict and a live witness closes under Rule 1",
            facts[row.Id].Verdict == "kept" && identicalKept.Closed && identicalKept.Rule == "1" && identicalKept.WitnessTextSha256.Length > 0);
        var identicalUnverified = LedgerPauloRulings.Evaluate(identical, row, LedgerRulingLiveText.Find, new Dictionary<string, LedgerPauloRulings.FactCheck>(StringComparer.Ordinal));
        Check("identical wording without a verdict stays with the text tool",
            !identicalUnverified.Closed && identicalUnverified.Reason == "Identical old/new text belongs to the text tool.");
        var identicalLost = new Dictionary<string, LedgerPauloRulings.FactCheck>(facts, StringComparer.Ordinal) { [row.Id] = new("lost", "Synthetic lost fact.") };
        Check("identical wording with a lost verdict stays with the text tool",
            LedgerPauloRulings.Evaluate(identical, row, LedgerRulingLiveText.Find, identicalLost) is { Closed: false, Reason: "Identical old/new text belongs to the text tool." });
        Check("identical wording with a kept verdict but no live witness stays open",
            !LedgerPauloRulings.Evaluate(identical, row, _ => new(false, "", "", ""), facts).Closed);
        Check("invented new wording remains open", !Decide(input with { NewText = "Uncompiled ruling sentinel 31892117" }, row).Closed);
        var rejectedInput = LedgerPauloRulings.ReadInputs(LedgerPauloRulings.InputPath).First(candidate => candidate.Id == "FQ15-BEF6F613076C");
        var rejectedRow = inventory.Observations.Single(observation => observation.Id == rejectedInput.Id);
        Check("review-rejected lost button-colour fact cannot close under Rule 3",
            facts[rejectedInput.Id].Verdict == "lost" && !Decide(rejectedInput, rejectedRow).Closed);
        Check("a missing independent verdict cannot close real compiled wording", !LedgerPauloRulings.Evaluate(input, row, LedgerRulingLiveText.Find,
            new Dictionary<string, LedgerPauloRulings.FactCheck>(StringComparer.Ordinal)).Closed);
        var lost = new Dictionary<string, LedgerPauloRulings.FactCheck>(facts, StringComparer.Ordinal) { [row.Id] = new("lost", "Synthetic lost fact.") };
        Check("a lost verdict cannot close real compiled wording", !LedgerPauloRulings.Evaluate(input, row, LedgerRulingLiveText.Find, lost).Closed);
        // Rule 4, Paulo's group ruling: an optional pinned TSV; a listed row closes unless an earlier rule closes it or it is a
        // DROP or an execution row. Missing or empty input closes nothing.
        string groupFolder = Path.Combine(output, "group-fixtures"); Directory.CreateDirectory(groupFolder);
        string groupMissing = Path.Combine(groupFolder, "absent.tsv"), groupEmpty = Path.Combine(groupFolder, "empty.tsv"),
            groupHeader = Path.Combine(groupFolder, "header.tsv"), groupBad = Path.Combine(groupFolder, "bad.tsv");
        File.WriteAllText(groupEmpty, ""); File.WriteAllText(groupHeader, "row_id\tgroup\tnote\n");
        File.WriteAllText(groupBad, "row_id\tgroup\tnote\n" + row.Id + "\tkept\tNot a group ruling\n");
        Check("a missing, empty or header-only group ruling holds no rows", LedgerPauloRulings.ReadGroupRulings(groupMissing).Count == 0
            && LedgerPauloRulings.ReadGroupRulings(groupEmpty).Count == 0 && LedgerPauloRulings.ReadGroupRulings(groupHeader).Count == 0);
        bool badRefused = false;
        try { LedgerPauloRulings.ReadGroupRulings(groupBad); } catch (InvalidDataException) { badRefused = true; }
        Check("a group ruling must be fragment, format or superseded", badRefused);
        string groupFormat = Path.Combine(groupFolder, "format.tsv");
        File.WriteAllText(groupFormat, "row_id\tgroup\tnote\n" + row.Id + "\tformat\tN0 counts keep digit grouping.\n");
        Check("a format group ruling is read with its note", LedgerPauloRulings.ReadGroupRulings(groupFormat) is { Count: 1 } formatRead
            && formatRead[row.Id] == new LedgerPauloRulings.GroupRuling("format", "N0 counts keep digit grouping."));
        Check("the pinned rules record Paulo's rule 4 with its fragment, format and superseded groups", File.ReadAllLines(LedgerPauloRulings.RulesPath)
            .Any(line => line.StartsWith("Rule 4, Paulo 2026-11-02:", StringComparison.Ordinal) && line.Contains("Fragment:", StringComparison.Ordinal)
                && line.Contains("Format:", StringComparison.Ordinal) && line.Contains("Superseded:", StringComparison.Ordinal)));
        var unverified = LedgerPauloRulings.ReadInputs(LedgerPauloRulings.InputPath).Select(candidate => (Input: candidate,
                Row: inventory.Observations.FirstOrDefault(observation => observation.Id == candidate.Id)))
            .First(pair => pair.Row != null && Decide(pair.Input, pair.Row).Reason == "Independent fact-check verdict is missing.");
        var fragment = new Dictionary<string, LedgerPauloRulings.GroupRuling>(StringComparer.Ordinal) { [unverified.Input.Id] = new("fragment", "") };
        var superseded = new Dictionary<string, LedgerPauloRulings.GroupRuling>(StringComparer.Ordinal) { [unverified.Input.Id] = new("superseded", "Build help replaces it.") };
        var asFragment = LedgerPauloRulings.Evaluate(unverified.Input, unverified.Row!, LedgerRulingLiveText.Find, facts, fragment);
        Check("a group ruling closes an open wording row under rule 4 as a fragment",
            asFragment.Closed && asFragment.Rule == "4" && asFragment.Reason == "Paulo group ruling: fragment, no user fact of its own");
        var asSuperseded = LedgerPauloRulings.Evaluate(unverified.Input, unverified.Row!, LedgerRulingLiveText.Find, facts, superseded);
        Check("a superseded group ruling carries its note in the reason",
            asSuperseded.Closed && asSuperseded.Rule == "4" && asSuperseded.Reason == "Paulo group ruling: superseded, Build help replaces it.");
        var format = new Dictionary<string, LedgerPauloRulings.GroupRuling>(StringComparer.Ordinal) { [unverified.Input.Id] = new("format", "N0 counts keep digit grouping.") };
        var asFormat = LedgerPauloRulings.Evaluate(unverified.Input, unverified.Row!, LedgerRulingLiveText.Find, facts, format);
        Check("a format group ruling closes an open wording row under rule 4 with the same-precision reason and its note",
            asFormat.Closed && asFormat.Rule == "4" && asFormat.Reason == "Paulo group ruling: format, same precision today, N0 counts keep digit grouping.");
        var bareFormat = LedgerPauloRulings.Evaluate(unverified.Input, unverified.Row!, LedgerRulingLiveText.Find, facts,
            new Dictionary<string, LedgerPauloRulings.GroupRuling>(StringComparer.Ordinal) { [unverified.Input.Id] = new("format", "") });
        Check("a format group ruling without a note gives the bare format reason",
            bareFormat.Closed && bareFormat.Rule == "4" && bareFormat.Reason == "Paulo group ruling: format, same precision today");
        Check("without a group ruling the same row stays open", !Decide(unverified.Input, unverified.Row!).Closed);
        var keptGroups = new Dictionary<string, LedgerPauloRulings.GroupRuling>(StringComparer.Ordinal) { [row.Id] = new("fragment", "") };
        var stillOne = LedgerPauloRulings.Evaluate(input, row, LedgerRulingLiveText.Find, facts, keptGroups);
        Check("a kept-verdict row that closes under rule 1 stays rule 1 when also group-ruled", stillOne.Closed && stillOne.Rule == "1");
        var stillOneFormat = LedgerPauloRulings.Evaluate(input, row, LedgerRulingLiveText.Find, facts,
            new Dictionary<string, LedgerPauloRulings.GroupRuling>(StringComparer.Ordinal) { [row.Id] = new("format", "Synthetic.") });
        Check("a kept-verdict row that closes under rule 1 stays rule 1 when also ruled format", stillOneFormat.Closed && stillOneFormat.Rule == "1");
        foreach (var (group, rulings) in new[] { ("fragment", fragment), ("format", format) })
            foreach (string kind in new[] { "format/export", "export/storage fact" })
                Check("a " + group + " group ruling cannot close a " + kind + " row", !LedgerPauloRulings.Evaluate(unverified.Input, unverified.Row! with { Kind = kind },
                    LedgerRulingLiveText.Find, facts, rulings).Closed);
        var drop = inventory.Observations.First(FeatureLedger.IsApprovedDrop);
        var dropInput = new LedgerPauloRulings.Input(3, "synthetic guard", drop.Id, drop.Detail, drop.Detail, "Synthetic DROP fixture.");
        var dropDecision = LedgerPauloRulings.Evaluate(dropInput, drop, LedgerRulingLiveText.Find, facts,
            new Dictionary<string, LedgerPauloRulings.GroupRuling>(StringComparer.Ordinal) { [drop.Id] = new("superseded", "Synthetic.") });
        Check("an approved DROP stays a DROP under a group ruling", !dropDecision.Closed && dropDecision.Reason == "Approved DROP is not a wording ruling.");
        var dropFormat = LedgerPauloRulings.Evaluate(dropInput, drop, LedgerRulingLiveText.Find, facts,
            new Dictionary<string, LedgerPauloRulings.GroupRuling>(StringComparer.Ordinal) { [drop.Id] = new("format", "Synthetic.") });
        Check("an approved DROP stays a DROP under a format group ruling", !dropFormat.Closed && dropFormat.Reason == "Approved DROP is not a wording ruling.");
        foreach (string kind in new[] { "format/export", "export/storage fact", "handler", "handler definition", "control", "shortcut" })
            Check("ruling cannot replace " + kind + " execution", !Decide(input, row with { Kind = kind }).Closed);
        // Frozen App.xaml:46 sets only Foreground. The first Style also has a
        // qualified template setter, deliberately outside the narrow policy.
        var style = inventory.Observations.Single(observation => observation.Id == "FQ15-B073CC492890");
        var styleInput = new LedgerPauloRulings.Input(2, "synthetic guard", style.Id, style.Detail, "Current theme", "Synthetic style fixture.");
        Check("appearance ruling accepts only a supported style/setter anchor", LedgerPauloRulings.Evaluate(styleInput, style, _ => new(false, "", "", "")).Rule == "2");
        var behaviorSetter = style with { Detail = "Setter.Value = False", Source = "FamilyQuant/ClusterGenerationControl.xaml", Line = 1 };
        Check("appearance ruling cannot waive an unsupported setter", !LedgerPauloRulings.Evaluate(styleInput with { Id = behaviorSetter.Id, OldText = behaviorSetter.Detail }, behaviorSetter,
            _ => new(false, "", "", "")).Closed);

        string folder = Path.Combine(output, "guard-fixtures"); Directory.CreateDirectory(folder);
        string tsv = Path.Combine(folder, "input.tsv"), rules = Path.Combine(folder, "rules.md"), packetFile = Path.Combine(folder, "packet.json"), factFile = Path.Combine(folder, "facts.tsv");
        File.WriteAllText(tsv, "lane\trow_id\told_text\tnew_text\treason\nfixture\t" + row.Id + "\t" + input.OldText + "\t" + input.NewText + "\tSynthetic guard only\n");
        File.Copy(LedgerPauloRulings.RulesPath, rules);
        File.Copy(LedgerPauloRulings.FactChecksPath, factFile);
        var packet = new LedgerPauloRulings.Packet(2, LedgerPauloRulings.Command, tsv, rules, FeatureLedgerEvidence.Hash(tsv), LedgerPauloRulings.RulesSha256,
            new() { accepted }, factFile, FeatureLedgerEvidence.Hash(factFile));
        FeatureLedgerEvidence.WriteJson(packetFile, packet);
        string current = FeatureLedgerEvidence.CurrentPath(root, row.Source);
        var source = new FeatureLedgerEvidence.Implementation(current, "Reviewed current user text", FeatureLedgerEvidence.Hash(Path.Combine(root, current)));
        string rule = File.ReadAllLines(rules).First(line => line.StartsWith("Rule 1,", StringComparison.Ordinal));
        var proof = new FeatureLedgerEvidence.Proof("fixture", "ruling", "Rule 1", FeatureLedgerEvidence.RulingAssertion(rule, accepted), packetFile, "Paulo");
        var receipt = new FeatureLedgerEvidence.Row(row.Id, new() { source }, new() { proof }, Compared: FeatureLedgerEvidence.RulingComparison(accepted));
        FeatureLedgerEvidence.Run Run() => new("fixture", "FamilyQuant.Tests.exe --ledger-apply-rulings synthetic fixture", timestamp, new string('a', 40),
            new() { new(tsv, FeatureLedgerEvidence.Hash(tsv)), new(rules, FeatureLedgerEvidence.Hash(rules)), new(packetFile, FeatureLedgerEvidence.Hash(packetFile)),
                new(factFile, FeatureLedgerEvidence.Hash(factFile)) }, root);
        bool Valid(FeatureLedgerEvidence.Row candidate, string step = LedgerPauloRulings.Step, FeatureLedgerEvidence.Run? run = null)
        {
            var result = FeatureLedgerEvidence.Load(root, inventory, new[] { new FeatureLedgerEvidence.Register(1, step, FeatureLedger.ReferenceCommit,
                new() { run ?? Run() }, new() { candidate }) }, Array.Empty<FeatureLedgerEvidence.KindReview>());
            return result.IsVerified(candidate.ObservationId, "evidence:" + step + ":" + candidate.ObservationId);
        }
        Check("dedicated recorded ruling receipt is accepted", Valid(receipt));
        FeatureLedgerEvidence.WriteJson(packetFile, packet with { SchemaVersion = 1 });
        Check("old ruling packets without fact verdicts no longer qualify", !Valid(receipt));
        FeatureLedgerEvidence.WriteJson(packetFile, packet);
        Check("fact verdicts must be retained and hash-declared", !Valid(receipt, run: Run() with
            { Artifacts = Run().Artifacts.Where(artifact => artifact.Path != factFile).ToList() }));
        File.WriteAllText(factFile, "row_id\tverdict\tnote\n" + row.Id + "\tlost\tSynthetic lost verdict\n");
        FeatureLedgerEvidence.WriteJson(packetFile, packet with { FactChecksSha256 = FeatureLedgerEvidence.Hash(factFile) });
        Check("a hashed lost verdict cannot be reused as kept", !Valid(receipt));
        File.WriteAllText(factFile, "row_id\tverdict\tnote\n");
        FeatureLedgerEvidence.WriteJson(packetFile, packet with { FactChecksSha256 = FeatureLedgerEvidence.Hash(factFile) });
        Check("a hashed verdict file missing this row cannot close it", !Valid(receipt));
        File.Copy(LedgerPauloRulings.FactChecksPath, factFile, true); FeatureLedgerEvidence.WriteJson(packetFile, packet);
        Check("another register cannot borrow the ruling proof kind", !Valid(receipt, "unrelated"));
        Check("a ruling proof must cite the rule", !Valid(receipt with { Proofs = new() { proof with { Target = "" } } }));
        Check("a ruling proof cannot alter old or new text in its assertion", !Valid(receipt with { Proofs = new() { proof with { Assertion = "Paulo accepted it" } } }));
        Check("a ruling proof cannot omit the listed text comparison", !Valid(receipt with { Compared = "" }));
        Check("a ruling proof requires Paulo's named decision", !Valid(receipt with { Proofs = new() { proof with { InspectedBy = "" } } }));
        Check("a ruling proof requires the dedicated command", !Valid(receipt, run: Run() with { Command = "some other checker" }));
        FeatureLedgerEvidence.WriteJson(packetFile, packet with { Decisions = new() { accepted with { NewText = "" } } });
        Check("forged empty text in a hashed ruling packet still cannot close", !Valid(receipt));
        FeatureLedgerEvidence.WriteJson(packetFile, packet);
        File.WriteAllText(tsv, "lane\trow_id\told_text\tnew_text\treason\nfixture\t" + row.Id + "\t" + input.OldText + "\tSome other text\tNot this decision\n");
        FeatureLedgerEvidence.WriteJson(packetFile, packet with { TsvSha256 = FeatureLedgerEvidence.Hash(tsv) });
        Check("a row/text pair absent from the pinned TSV cannot close", !Valid(receipt));
        // The forgery fixture is any listed row whose canonical verdict is lost and whose compiled witness would close it under
        // Rule 1 or 3 if a private verdict said kept (first by ID), so the guard does not lean on one production row.
        Dictionary<string, LedgerPauloRulings.FactCheck> Forge(string id) => new(facts, StringComparer.Ordinal)
            { [id] = new("kept", "Synthetic private forgery, never a reviewer verdict.") };
        var forgery = LedgerPauloRulings.ReadInputs(LedgerPauloRulings.InputPath)
            .Where(candidate => facts.TryGetValue(candidate.Id, out var fact) && fact.Verdict == "lost")
            .OrderBy(candidate => candidate.Id, StringComparer.Ordinal).ThenBy(candidate => candidate.Line)
            .Select(candidate => (Input: candidate, Row: inventory.Observations.FirstOrDefault(observation => observation.Id == candidate.Id)))
            .Where(pair => pair.Row != null)
            .Select(pair => (pair.Input, Row: pair.Row!, Decision: LedgerPauloRulings.Evaluate(pair.Input, pair.Row!, LedgerRulingLiveText.Find, Forge(pair.Input.Id))))
            .FirstOrDefault(candidate => candidate.Decision.Closed && candidate.Decision.Rule is "1" or "3");
        Check("forgery fixture finds a canonically lost row that a forged kept verdict would close under Rule 1 or 3",
            forgery.Row != null && !Decide(forgery.Input, forgery.Row).Closed);
        var forged = forgery.Decision;
        File.WriteAllText(tsv, "lane\trow_id\told_text\tnew_text\treason\nfixture\t" + forgery.Input.Id + "\t" + forgery.Input.OldText.Replace("\n", "\\n")
            + "\t" + forgery.Input.NewText.Replace("\n", "\\n") + "\tSynthetic guard\n");
        Check("the forgery fixture TSV holds exactly the forged row and wording", LedgerPauloRulings.ReadInputs(tsv) is [var only]
            && only.Id == forged.Id && only.OldText == forged.OldText && only.NewText == forged.NewText);
        File.WriteAllText(factFile, "row_id\tverdict\tnote\n" + forgery.Input.Id + "\tkept\t" + forged.FactCheckNote + "\n");
        FeatureLedgerEvidence.WriteJson(packetFile, packet with { Decisions = new() { forged }, TsvSha256 = FeatureLedgerEvidence.Hash(tsv), FactChecksSha256 = FeatureLedgerEvidence.Hash(factFile) });
        string forgedCurrent = FeatureLedgerEvidence.CurrentPath(root, forgery.Row.Source);
        string forgedRule = File.ReadAllLines(rules).First(line => line.StartsWith("Rule " + forged.Rule + ",", StringComparison.Ordinal));
        var rejectedReceipt = new FeatureLedgerEvidence.Row(forgery.Row.Id, new() { new(forgedCurrent, "Synthetic rejected fact", FeatureLedgerEvidence.Hash(Path.Combine(root, forgedCurrent))) },
            new() { proof with { Target = "Rule " + forged.Rule, Assertion = FeatureLedgerEvidence.RulingAssertion(forgedRule, forged) } }, Compared: FeatureLedgerEvidence.RulingComparison(forged));
        Check("a private forged kept verdict cannot override the canonical lost verdict", !Valid(rejectedReceipt));

        // A rule-4 receipt is valid only with its group ruling pinned as an execution artifact.
        string groupFile = Path.Combine(folder, "group.tsv");
        File.WriteAllText(groupFile, "row_id\tgroup\tnote\n" + unverified.Input.Id + "\tfragment\t\n");
        File.WriteAllText(tsv, "lane\trow_id\told_text\tnew_text\treason\nfixture\t" + unverified.Input.Id + "\t" + unverified.Input.OldText.Replace("\n", "\\n")
            + "\t" + unverified.Input.NewText.Replace("\n", "\\n") + "\tSynthetic group guard\n");
        File.Copy(LedgerPauloRulings.FactChecksPath, factFile, true);
        var groupInput = LedgerPauloRulings.ReadInputs(tsv)[0];
        var groupDecision = LedgerPauloRulings.Evaluate(groupInput, unverified.Row!, LedgerRulingLiveText.Find, facts, LedgerPauloRulings.ReadGroupRulings(groupFile));
        var groupPacket = packet with { Decisions = new() { groupDecision }, TsvSha256 = FeatureLedgerEvidence.Hash(tsv), FactChecksSha256 = FeatureLedgerEvidence.Hash(factFile),
            GroupRulingArtifact = groupFile, GroupRulingSha256 = FeatureLedgerEvidence.Hash(groupFile) };
        FeatureLedgerEvidence.WriteJson(packetFile, groupPacket);
        string groupCurrent = FeatureLedgerEvidence.CurrentPath(root, unverified.Row!.Source);
        var groupReceipt = new FeatureLedgerEvidence.Row(unverified.Row.Id, new() { new(groupCurrent, "Paulo group ruling", FeatureLedgerEvidence.Hash(Path.Combine(root, groupCurrent))) },
            new() { proof with { Target = "Rule 4", Assertion = FeatureLedgerEvidence.RulingAssertion(LedgerPauloRulings.GroupRule(groupDecision), groupDecision) } },
            Compared: FeatureLedgerEvidence.RulingComparison(groupDecision));
        FeatureLedgerEvidence.Run GroupRun() => Run() with { Artifacts = Run().Artifacts.Append(new(groupFile, FeatureLedgerEvidence.Hash(groupFile))).ToList() };
        Check("a rule-4 receipt with its group ruling pinned is accepted", groupDecision.Rule == "4" && Valid(groupReceipt, run: GroupRun()));
        Check("a rule-4 receipt without its group ruling declared is refused", !Valid(groupReceipt));
        FeatureLedgerEvidence.WriteJson(packetFile, groupPacket with { GroupRulingSha256 = new string('0', 64) });
        Check("a rule-4 receipt with a different group ruling hash is refused", !Valid(groupReceipt, run: GroupRun()));
        FeatureLedgerEvidence.WriteJson(packetFile, packet);
    }
}
