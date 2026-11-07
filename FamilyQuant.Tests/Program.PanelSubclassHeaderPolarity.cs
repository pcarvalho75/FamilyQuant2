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
        var vocabulary = new[] { new FamilyPanelDomain { Accession = "PF01234", Name = "Anchor domain" } };
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
        var absentColumns = new[]
        {
            ("English adjective", "Absent domains", "absent Anchor domain"),
            ("English without", "Domains without", "without Anchor domain"),
            ("English no", "No domains", "no Anchor domain"),
            ("English not", "Not domains", "not Anchor domain"),
            ("Portuguese adjective", "Dom\u00ednios ausentes", "ausente Anchor domain"),
            ("Portuguese sem", "Sem dom\u00ednios", "sem Anchor domain"),
            ("Spanish adjective", "Dominios ausentes", "ausentes Anchor domain"),
            ("Spanish sin", "Sin dominios", "sin Anchor domain"),
            ("French adjective", "Domaines absents", "absents Anchor domain"),
            ("French sans", "Sans domaines", "sans Anchor domain"),
            ("German ohne", "Ohne Dom\u00e4nen", "ohne Anchor domain"),
            ("Leading minus", "- domains", "- Anchor domain"),
        };
        foreach (var (label, header, agreeingCell) in absentColumns)
        {
            var plain = Propose((header, "Anchor domain"));
            var agreed = Propose((header, agreeingCell));
            Check("Z16a12 " + label + " header gives a bare cell absent polarity", plain.Rules is [{ Accession: "PF01234", Kind: PanelSubclassRuleKind.AbsentPfam, IsUncertain: false }]
                && !plain.IsUncertain);
            Check("Z16a12 " + label + " agreeing header and cell remain certain", agreed.Rules is [{ Kind: PanelSubclassRuleKind.AbsentPfam, IsUncertain: false }] && !agreed.IsUncertain);
        }
        foreach (string header in new[] { "Required domains", "Dom\u00ednios Pfam exigidos", "Dominios requeridos" })
        {
            var plain = Propose((header, "Anchor domain"));
            var agreed = Propose((header, "+ Anchor domain"));
            Check("Z16a12 required header and agreeing plus keep required polarity", plain.Rules is [{ Kind: PanelSubclassRuleKind.RequiredPfam, IsUncertain: false }]
                && agreed.Rules is [{ Kind: PanelSubclassRuleKind.RequiredPfam, IsUncertain: false }]);
            var conflict = Propose((header, "without Anchor domain"));
            Check("Z16a12 required header versus absent cell requires review", conflict.IsUncertain && conflict.Rules.Single().IsUncertain
                && conflict.Rules.Single().Reason.Contains("header", StringComparison.OrdinalIgnoreCase)
                && conflict.Rules.Single().Reason.Contains("cell", StringComparison.OrdinalIgnoreCase));
            Check("Z16a12 a newly conflicted rule cannot inherit its previous accepted identifier", conflict.Rules.Single().RuleId != plain.Rules.Single().RuleId);
        }
        var opposite = Propose(("Absent domains", "+ Anchor domain"));
        Check("Z16a12 absent header versus explicit required cell is uncertain", opposite.IsUncertain && opposite.Rules.Single().IsUncertain);
        var mixedHeader = Propose(("Required or absent domains", "Anchor domain"));
        Check("Z16a12 a contradictory header stays uncertain", mixedHeader.IsUncertain && mixedHeader.Rules.Single().IsUncertain);
        foreach (bool reversed in new[] { false, true })
        {
            var fields = new[] { ("Required domains", "Anchor domain"), ("Other required domains", "without Anchor domain") };
            var merged = Propose(reversed ? fields.Reverse().ToArray() : fields);
            Check("Z16a12 merging repeated rules keeps conflict uncertainty in either column order", merged.Rules.Count == 1 && merged.Rules.Single().IsUncertain && merged.IsUncertain);
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
            && withoutDomain.Rules.Single(rule => rule.Accession == "PF00413").Kind == PanelSubclassRuleKind.RequiredPfam
            && !withDomain.IsUncertain && !withoutDomain.IsUncertain);
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
