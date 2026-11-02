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
        Check("identical wording stays with the text tool", !Decide(input with { NewText = input.OldText }, row).Closed);
        Check("invented new wording remains open", !Decide(input with { NewText = "Uncompiled ruling sentinel 31892117" }, row).Closed);
        var rejectedInput = LedgerPauloRulings.ReadInputs(LedgerPauloRulings.InputPath).First(candidate => candidate.Id == "FQ15-BEF6F613076C");
        var rejectedRow = inventory.Observations.Single(observation => observation.Id == rejectedInput.Id);
        Check("review-rejected lost button-colour fact cannot close under Rule 3",
            facts[rejectedInput.Id].Verdict == "lost" && !Decide(rejectedInput, rejectedRow).Closed);
        Check("a missing independent verdict cannot close real compiled wording", !LedgerPauloRulings.Evaluate(input, row, LedgerRulingLiveText.Find,
            new Dictionary<string, LedgerPauloRulings.FactCheck>(StringComparer.Ordinal)).Closed);
        var lost = new Dictionary<string, LedgerPauloRulings.FactCheck>(facts, StringComparer.Ordinal) { [row.Id] = new("lost", "Synthetic lost fact.") };
        Check("a lost verdict cannot close real compiled wording", !LedgerPauloRulings.Evaluate(input, row, LedgerRulingLiveText.Find, lost).Closed);
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
        var forgedFacts = new Dictionary<string, LedgerPauloRulings.FactCheck>(facts, StringComparer.Ordinal)
            { [rejectedInput.Id] = new("kept", "Synthetic private forgery, never a reviewer verdict.") };
        var forged = LedgerPauloRulings.Evaluate(rejectedInput, rejectedRow, LedgerRulingLiveText.Find, forgedFacts);
        Check("forgery fixture preserves the formerly accepted compiled Rule 3 witness", forged.Closed && forged.Rule == "3");
        File.WriteAllText(tsv, "lane\trow_id\told_text\tnew_text\treason\nfixture\t" + rejectedInput.Id + "\t" + rejectedInput.OldText + "\t" + rejectedInput.NewText + "\tSynthetic guard\n");
        File.WriteAllText(factFile, "row_id\tverdict\tnote\n" + rejectedInput.Id + "\tkept\t" + forged.FactCheckNote + "\n");
        FeatureLedgerEvidence.WriteJson(packetFile, packet with { Decisions = new() { forged }, TsvSha256 = FeatureLedgerEvidence.Hash(tsv), FactChecksSha256 = FeatureLedgerEvidence.Hash(factFile) });
        string rejectedCurrent = FeatureLedgerEvidence.CurrentPath(root, rejectedRow.Source);
        var rejectedReceipt = new FeatureLedgerEvidence.Row(rejectedRow.Id, new() { new(rejectedCurrent, "Synthetic rejected fact", FeatureLedgerEvidence.Hash(Path.Combine(root, rejectedCurrent))) },
            new() { proof with { Target = "Rule 3", Assertion = FeatureLedgerEvidence.RulingAssertion(File.ReadAllLines(rules)[2], forged) } }, Compared: FeatureLedgerEvidence.RulingComparison(forged));
        Check("a private forged kept verdict cannot override the canonical lost verdict", !Valid(rejectedReceipt));
    }
}
