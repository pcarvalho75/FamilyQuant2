using FamilyQuant.Evidence;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FamilyQuant.Tests
{
    internal static partial class Program
    {
        // The columns of the CSVs delivered before 2.0 (conferencia_familias_veneno.csv and
        // fora_com_dominio_de_veneno.csv), as the answer key the program's exports are pinned to.
        private static readonly string[] DeliveredAuditColumns =
        {
            "Família", "Número da família", "Família antes", "Código UniProt", "Locus", "Descrição", "Por que entrou",
            "Domínios (Pfam)", "Tamanho (aa)", "Cisteínas", "Revisada", "Fragmento", "Anotada como toxina", "Família no UniProt",
        };
        private static readonly string[] DeliveredLeftOutColumns =
            { "Família possível", "Código UniProt", "Locus", "Descrição", "Tamanho (aa)", "Revisada", "Fragmento" };

        private static void EvidenceExportTests()
        {
            Check("the audit CSV has the delivered audit columns, in their order, with English headers",
                EvidenceExports.AuditColumns.Count == DeliveredAuditColumns.Length
                && EvidenceExports.AuditColumns.SequenceEqual(new[]
                {
                    "Family", "Family number", "Previous family", "Accession", "Locus", "Description", "Reason", "Domains (Pfam)",
                    "Length (aa)", "Cysteines", "Reviewed", "Fragment", "Annotated in scope", "UniProt family",
                }));
            string leftOutHeader = LeftOutList.BuildCsv(Array.Empty<LeftOutRow>()).Split("\r\n")[0];
            Check("the left-out CSV starts with the delivered left-out columns, in their order, then says why",
                leftOutHeader.Split(',').Take(DeliveredLeftOutColumns.Length)
                    .SequenceEqual(new[] { "Family", "Accession", "Locus", "Description", "Length", "Reviewed", "Fragment" })
                && leftOutHeader.EndsWith("MarkerDomain,MarkerName,AssignedInstead,Why", StringComparison.Ordinal));

            var file = new FamilyClusterFile();
            file.MyExtendedFastaItems.AddRange(new[]
            {
                new FastaItemExtended
                {
                    SequenceIdentifier = "tr|A0A901|ONE", Description = "Protein one, with a comma OS=Testus", Sequence = "MCCAAC",
                    Domains = "Alpha; Beta", UserID = 9,
                    Evidence = new ProteinEvidence { Accession = "A0A901", Reviewed = false, Fragment = false, Keywords = new List<string> { "Chosen" },
                        FamilyAnnotation = "alpha family, beta subfamily" },
                },
                new FastaItemExtended
                {
                    SequenceIdentifier = "sp|P90002|TWO", Description = "Protein two (Fragment) OS=Testus", Sequence = "MAAAAAAAAAAAA",
                    UserID = 4, Evidence = new ProteinEvidence { Accession = "P90002", Reviewed = true, Fragment = true, FamilyAnnotation = "alpha family" },
                },
                new FastaItemExtended { SequenceIdentifier = "Reverse_tr|A0A901|ONE", Sequence = "CAACCM", UserID = 9 },
            });
            Check("with no applied recipe the audit CSV is the header alone",
                EvidenceExports.BuildAuditCsv(file) == string.Join(",", EvidenceExports.AuditColumns) + "\r\n");
            file.EvidenceRecipe = new EvidenceRecipe
            {
                Options = new EvidenceClassificationOptions { ScopeTerms = new[] { "Chosen" } },
                Assignments = new List<EvidenceAssignment>
                {
                    new() { ProteinIndex = 1, Identifier = "sp|P90002|TWO", Name = "Alpha", FamilyId = 1001, PreviousFamilyId = 4, Reason = "Reviewed neighbour." },
                    new() { ProteinIndex = 0, Identifier = "tr|A0A901|ONE", Name = "Alpha", FamilyId = 1001, PreviousFamilyId = 9, Reason = "=UniProt family" },
                },
            };
            CultureInfo previous = CultureInfo.CurrentCulture;
            string[] lines;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("pt-BR");
                lines = EvidenceExports.BuildAuditCsv(file).Split("\r\n");
            }
            finally { CultureInfo.CurrentCulture = previous; }
            Check("each placed protein is one RFC 4180 row in invariant numbers, by family number then locus",
                lines.Length == 4
                && lines[1] == "Alpha,1001,4,P90002,sp|P90002|TWO,Protein two (Fragment) OS=Testus,Reviewed neighbour.,,13,0,yes,yes,no,alpha family"
                && lines[2] == "Alpha,1001,9,A0A901,tr|A0A901|ONE,\"Protein one, with a comma OS=Testus\",'=UniProt family,Alpha; Beta,6,3,no,no,yes,\"alpha family, beta subfamily\"",
                string.Join(" | ", lines));
        }
    }
}
