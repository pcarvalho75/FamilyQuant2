using FamilyQuant.Evidence;
using FamilyQuant.Examples;
using FamilyQuant.Panels;
using FamilyQuant.Panels.Import;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void PanelSubclassHeaderPolarityTests(string output)
    {
        Section("Z16a12 column and cell polarity in subclass proposals");
        var vocabulary = new[] { new FamilyPanelDomain { Accession = "PF01234", Name = "Anchor domain" },
            new FamilyPanelDomain { Accession = "PF05678", Name = "Tail domain" }, new FamilyPanelDomain { Accession = "PF09090", Name = "Extra domain" } };
        PanelSubclassProposal Propose(params (string Header, string Cell)[] fields)
        {
            var cells = fields.ToDictionary(field => field.Header, field => field.Cell, StringComparer.Ordinal);
            cells["Class"] = "Example class";
            var table = new PanelWorkbookSubclassTable { SheetName = "Rule table", FamilyKeys = new() { "F1" }, ClassColumn = "Class",
                DomainColumns = fields.Select(field => field.Header).ToList(), Table = new() { Headers = cells.Keys.ToList(), Rows = new()
                    { new() { RowNumber = 2, Cells = cells } } } };
            string before = JsonSerializer.Serialize(table);
            var result = PanelSubclassImport.Propose(new[] { table }, new[] { "F1" }, vocabulary, Array.Empty<PanelSubclassExpectedName>()).Proposals.Single();
            if (JsonSerializer.Serialize(table) != before) throw new InvalidOperationException("Subclass proposal changed its source table.");
            return result;
        }
        // Z16a12b replaces the earlier assumption that short prepositions give header polarity.
        foreach (string header in new[] { "Como reconhecer no FamilyQuant", "Dom\u00ednios no grupo" })
        {
            var mixed = Propose((header, "Anchor domain SEM Tail domain e SEM Extra domain"));
            var plus = Propose((header, "Anchor domain + Tail domain + Extra domain"));
            Check("Z16a12b Portuguese preposition does not negate an unmarked first domain", mixed.Rules.Count == 3
                && mixed.Rules.Single(rule => rule.Accession == "PF01234").Kind == PanelSubclassRuleKind.RequiredPfam
                && mixed.Rules.Where(rule => rule.Accession != "PF01234").All(rule => rule.Kind == PanelSubclassRuleKind.AbsentPfam));
            Check("Z16a12b Portuguese preposition leaves a plus architecture entirely required", plus.Rules.Count == 3
                && plus.Rules.All(rule => rule.Kind == PanelSubclassRuleKind.RequiredPfam && !rule.IsUncertain));
        }
        var absentColumns = new[]
        {
            ("English adjective", "Absent domains", "absent Anchor domain"),
            ("Portuguese adjective", "Dom\u00ednios ausentes", "ausente Anchor domain"),
            ("Spanish adjective", "Dominios ausentes", "ausentes Anchor domain"),
            ("French adjective", "Domaines absents", "absents Anchor domain"),
            ("Leading minus", "- domains", "- Anchor domain"),
        };
        foreach (var (label, header, agreeingCell) in absentColumns)
        {
            var plain = Propose((header, "Anchor domain"));
            var agreed = Propose((header, agreeingCell));
            Check("Z16a12b " + label + " header inference is absent but uncertain", plain.Rules is [{ Accession: "PF01234", Kind: PanelSubclassRuleKind.AbsentPfam, IsUncertain: true }]
                && plain.IsUncertain && plain.Rules.Single().Reason.Contains("header", StringComparison.OrdinalIgnoreCase));
            Check("Z16a12 " + label + " agreeing header and cell remain certain", agreed.Rules is [{ Kind: PanelSubclassRuleKind.AbsentPfam, IsUncertain: false }] && !agreed.IsUncertain);
        }
        foreach (string marker in new[] { "no", "not", "sem", "sin", "sans", "ohne", "without" })
        {
            var plain = Propose(("Domains " + marker + " context", "Anchor domain"));
            var explicitCell = Propose(("Domains " + marker + " context", marker + " Anchor domain"));
            Check("Z16a12b short word " + marker + " is cell-only", plain.Rules is [{ Kind: PanelSubclassRuleKind.RequiredPfam, IsUncertain: false }]
                && explicitCell.Rules is [{ Kind: PanelSubclassRuleKind.AbsentPfam, IsUncertain: false }]);
        }
        foreach (string header in new[] { "Required domains", "Dom\u00ednios Pfam exigidos", "Dominios requeridos" })
        {
            var plain = Propose((header, "Anchor domain"));
            var agreed = Propose((header, "+ Anchor domain"));
            Check("Z16a12b required header inference needs review while an explicit plus stays certain", plain.Rules is [{ Kind: PanelSubclassRuleKind.RequiredPfam, IsUncertain: true }]
                && agreed.Rules is [{ Kind: PanelSubclassRuleKind.RequiredPfam, IsUncertain: false }]);
            var conflict = Propose((header, "without Anchor domain"));
            Check("Z16a12b explicit absent cell wins a required header and keeps conflict review", conflict.IsUncertain
                && conflict.Rules is [{ Kind: PanelSubclassRuleKind.AbsentPfam, IsUncertain: true }]
                && conflict.Rules.Single().Reason.Contains("header", StringComparison.OrdinalIgnoreCase)
                && conflict.Rules.Single().Reason.Contains("cell", StringComparison.OrdinalIgnoreCase));
            Check("Z16a12 a newly conflicted rule cannot inherit its previous accepted identifier", conflict.Rules.Single().RuleId != plain.Rules.Single().RuleId);
        }
        var opposite = Propose(("Absent domains", "+ Anchor domain"));
        Check("Z16a12b explicit required cell wins an absent header and keeps conflict review", opposite.IsUncertain
            && opposite.Rules is [{ Kind: PanelSubclassRuleKind.RequiredPfam, IsUncertain: true }]);
        var mixedHeader = Propose(("Required or absent domains", "Anchor domain"));
        Check("Z16a12 a contradictory header stays uncertain", mixedHeader.IsUncertain && mixedHeader.Rules.Single().IsUncertain);
        Check("Z16a12b explicit plus wins even a contradictory header", Propose(("Required or absent domains", "+ Anchor domain")).Rules
            is [{ Kind: PanelSubclassRuleKind.RequiredPfam, IsUncertain: true }]);
        foreach (bool reversed in new[] { false, true })
        {
            var fields = new[] { ("Required domains", "Anchor domain"), ("Other required domains", "+ Anchor domain") };
            var merged = Propose(reversed ? fields.Reverse().ToArray() : fields);
            Check("Z16a12b merging explicit and inferred copies keeps inference uncertainty", merged.Rules.Count == 1 && merged.Rules.Single().IsUncertain && merged.IsUncertain);
        }
        Check("Z16a12 neutral headers keep existing cell semantics and whole-word boundaries", Propose(("Architecture", "without Anchor domain")).Rules is [{ Kind: PanelSubclassRuleKind.AbsentPfam, IsUncertain: false }]
            && Propose(("Nocturnal domain annotation", "Anchor domain")).Rules is [{ Kind: PanelSubclassRuleKind.RequiredPfam, IsUncertain: false }]);
        Check("Z16a12 uncertain conflicts remain unconfirmed without an explicit selection", PanelSubclassImport.BuildDefinitions(new[] { opposite }, Array.Empty<PanelSubclassAcceptance>(), new[] { "F1" })
            is { CanBuild: true, Definitions.Count: 0 });

        string workbook = Path.Combine(output, "shipped-header-polarity.xlsx");
        ExampleFamilyList.Write(workbook);
        string hash = FileFingerprint.Sha256(workbook);
        var map = new FamilyPanelSpreadsheetImporter().ProposeWorkbook(workbook);
        string familyColumn = map.FamilyProposal!.Choices.ColumnHeaders[PanelImportFields.FamilyKey];
        var keys = map.FamilyTable!.Rows.Select(row => row.Cells.GetValueOrDefault(familyColumn, "").Trim()).Distinct(StringComparer.Ordinal).ToArray();
        var knownDomains = new[] { new FamilyPanelDomain { Accession = "PF00413", Name = "Peptidase_M10" }, new FamilyPanelDomain { Accession = "PF00045", Name = "Hemopexin" } };
        var proposals = PanelSubclassImport.Propose(map.SubclassTables, keys, knownDomains, Array.Empty<PanelSubclassExpectedName>()).Proposals;
        var withDomain = proposals.Single(proposal => proposal.FamilyKey == "MMP" && proposal.DisplayName == "Com hemopexina");
        var withoutDomain = proposals.Single(proposal => proposal.FamilyKey == "MMP" && proposal.DisplayName == "Sem hemopexina");
        Check("Z16a12 shipped example proposes opposite rules for its two domain architectures", withDomain.Rules.Single(rule => rule.Accession == "PF00045").Kind == PanelSubclassRuleKind.RequiredPfam
            && withoutDomain.Rules.Single(rule => rule.Accession == "PF00045").Kind == PanelSubclassRuleKind.AbsentPfam
            && withoutDomain.Rules.Single(rule => rule.Accession == "PF00413").Kind == PanelSubclassRuleKind.RequiredPfam);
        Check("Z16a12b shipped header-derived rules require review", withDomain.IsUncertain && withoutDomain.IsUncertain
            && withDomain.Rules.Concat(withoutDomain.Rules).All(rule => rule.IsUncertain && rule.Reason.Contains("header", StringComparison.OrdinalIgnoreCase)));
        var confirmed = PanelSubclassImport.BuildDefinitions(proposals, new[] { ConfirmSubclass(withDomain), ConfirmSubclass(withoutDomain) }, keys);
        var family = new FamilyPanelFamily { Key = "MMP", Subclasses = confirmed.Definitions.Select(item => item.Definition).ToList() };
        ProteinEvidence Facts(params string[] domains) => new() { Fragment = false, Pfam = domains.Select(accession => new EvidenceDomain { Accession = accession }).ToList() };
        Check("Z16a12 real subclass engine executes the shipped opposite architectures", confirmed.CanBuild
            && SubclassAssignment.ClassifyDirect(family, Facts("PF00413", "PF00045")).SubclassKey == withDomain.Key
            && SubclassAssignment.ClassifyDirect(family, Facts("PF00413")).SubclassKey == withoutDomain.Key);
        Check("Z16a12 header interpretation retains exact cells and workbook bytes", withoutDomain.SourceRows.All(row => row.Cells.All(cell => confirmed.Definitions
            .Single(item => item.Definition.Key == withoutDomain.Key).Definition.TextFields.Any(field => field.Text == cell.Value)))
            && FileFingerprint.Sha256(workbook) == hash);
        File.WriteAllText(Path.Combine(output, "header-polarity-proposals.json"), JsonSerializer.Serialize(new[] { withDomain, withoutDomain }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
