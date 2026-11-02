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
        var row = inventory.Observations.Single(observation => observation.Id == "FQ15-155754DC695E");
        var input = new LedgerPauloRulings.Input(2, "synthetic guard", row.Id, "Analytics", "Results", "Native ruling fixture only.");
        var accepted = LedgerPauloRulings.Evaluate(input, row, LedgerRulingLiveText.Find);
        Check("a real compiled current caption can support recorded Rule 1", accepted.Closed && accepted.Rule == "1");
        Check("test-only strings cannot serve as current program text", !LedgerRulingLiveText.Find("Synthetic ruling sentinel 710928399 - absent from installed code").Found);
        Check("empty new text cannot close a ruling row", !LedgerPauloRulings.Evaluate(input with { NewText = "" }, row, LedgerRulingLiveText.Find).Closed);
        Check("bare newline cannot close a ruling row", !LedgerPauloRulings.Evaluate(input with { NewText = "\r\n" }, row, LedgerRulingLiveText.Find).Closed);
        Check("capture placeholder cannot close a ruling row", !LedgerPauloRulings.Evaluate(input with { NewText = "<No exact current value captured>" }, row, LedgerRulingLiveText.Find).Closed);
        Check("identical wording stays with the text tool", !LedgerPauloRulings.Evaluate(input with { NewText = "Analytics" }, row, LedgerRulingLiveText.Find).Closed);
        Check("invented new wording remains open", !LedgerPauloRulings.Evaluate(input with { NewText = "Uncompiled ruling sentinel 31892117" }, row, LedgerRulingLiveText.Find).Closed);
        foreach (string kind in new[] { "format/export", "export/storage fact", "handler", "handler definition", "control", "shortcut" })
            Check("ruling cannot replace " + kind + " execution", !LedgerPauloRulings.Evaluate(input, row with { Kind = kind }, LedgerRulingLiveText.Find).Closed);
        // Frozen App.xaml:46 sets only Foreground. The first Style also has a
        // qualified template setter, deliberately outside the narrow policy.
        var style = inventory.Observations.Single(observation => observation.Id == "FQ15-B073CC492890");
        var styleInput = new LedgerPauloRulings.Input(2, "synthetic guard", style.Id, style.Detail, "Current theme", "Synthetic style fixture.");
        Check("appearance ruling accepts only a supported style/setter anchor", LedgerPauloRulings.Evaluate(styleInput, style, _ => new(false, "", "", "")).Rule == "2");
        var behaviorSetter = style with { Detail = "Setter.Value = False", Source = "FamilyQuant/ClusterGenerationControl.xaml", Line = 1 };
        Check("appearance ruling cannot waive an unsupported setter", !LedgerPauloRulings.Evaluate(styleInput with { Id = behaviorSetter.Id, OldText = behaviorSetter.Detail }, behaviorSetter,
            _ => new(false, "", "", "")).Closed);

        string folder = Path.Combine(output, "guard-fixtures"); Directory.CreateDirectory(folder);
        string tsv = Path.Combine(folder, "input.tsv"), rules = Path.Combine(folder, "rules.md"), packetFile = Path.Combine(folder, "packet.json");
        File.WriteAllText(tsv, "lane\trow_id\told_text\tnew_text\treason\nfixture\t" + row.Id + "\tAnalytics\tResults\tSynthetic guard only\n");
        File.Copy(LedgerPauloRulings.RulesPath, rules);
        var packet = new LedgerPauloRulings.Packet(1, LedgerPauloRulings.Command, tsv, rules, FeatureLedgerEvidence.Hash(tsv), LedgerPauloRulings.RulesSha256, new() { accepted });
        FeatureLedgerEvidence.WriteJson(packetFile, packet);
        var source = new FeatureLedgerEvidence.Implementation("FamilyQuant/AnalyticsControl.xaml", "Results caption", FeatureLedgerEvidence.Hash(Path.Combine(root, "FamilyQuant/AnalyticsControl.xaml")));
        string rule = File.ReadAllLines(rules).First(line => line.StartsWith("Rule 1,", StringComparison.Ordinal));
        var proof = new FeatureLedgerEvidence.Proof("fixture", "ruling", "Rule 1", FeatureLedgerEvidence.RulingAssertion(rule, accepted), packetFile, "Paulo");
        var receipt = new FeatureLedgerEvidence.Row(row.Id, new() { source }, new() { proof }, Compared: FeatureLedgerEvidence.RulingComparison(accepted));
        FeatureLedgerEvidence.Run Run() => new("fixture", "FamilyQuant.Tests.exe --ledger-apply-rulings synthetic fixture", timestamp, new string('a', 40),
            new() { new(tsv, FeatureLedgerEvidence.Hash(tsv)), new(rules, FeatureLedgerEvidence.Hash(rules)), new(packetFile, FeatureLedgerEvidence.Hash(packetFile)) }, root);
        bool Valid(FeatureLedgerEvidence.Row candidate, string step = LedgerPauloRulings.Step, FeatureLedgerEvidence.Run? run = null)
        {
            var result = FeatureLedgerEvidence.Load(root, inventory, new[] { new FeatureLedgerEvidence.Register(1, step, FeatureLedger.ReferenceCommit,
                new() { run ?? Run() }, new() { candidate }) }, Array.Empty<FeatureLedgerEvidence.KindReview>());
            return result.IsVerified(candidate.ObservationId, "evidence:" + step + ":" + candidate.ObservationId);
        }
        Check("dedicated recorded ruling receipt is accepted", Valid(receipt));
        Check("another register cannot borrow the ruling proof kind", !Valid(receipt, "unrelated"));
        Check("a ruling proof must cite the rule", !Valid(receipt with { Proofs = new() { proof with { Target = "" } } }));
        Check("a ruling proof cannot alter old or new text in its assertion", !Valid(receipt with { Proofs = new() { proof with { Assertion = "Paulo accepted it" } } }));
        Check("a ruling proof cannot omit the listed text comparison", !Valid(receipt with { Compared = "" }));
        Check("a ruling proof requires Paulo's named decision", !Valid(receipt with { Proofs = new() { proof with { InspectedBy = "" } } }));
        Check("a ruling proof requires the dedicated command", !Valid(receipt, run: Run() with { Command = "some other checker" }));
        FeatureLedgerEvidence.WriteJson(packetFile, packet with { Decisions = new() { accepted with { NewText = "" } } });
        Check("forged empty text in a hashed ruling packet still cannot close", !Valid(receipt));
        FeatureLedgerEvidence.WriteJson(packetFile, packet);
        File.WriteAllText(tsv, "lane\trow_id\told_text\tnew_text\treason\nfixture\t" + row.Id + "\tAnalytics\tSome other text\tNot this decision\n");
        FeatureLedgerEvidence.WriteJson(packetFile, packet with { TsvSha256 = FeatureLedgerEvidence.Hash(tsv) });
        Check("a row/text pair absent from the pinned TSV cannot close", !Valid(receipt));
    }
}
