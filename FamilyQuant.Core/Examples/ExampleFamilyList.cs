using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FamilyQuant.Examples;

/// <summary>
/// Writes the invented example family list of the import tutorial (Z12p2c): a secreted-protein mixture with
/// general protein families, in six sheets shaped like a real consensus workbook: a title and a subtitle above
/// an accented header, free-text decisions, abbreviation cells coloured by decision, a row naming several
/// families in one cell, a repeated abbreviation, an empty row and line breaks inside cells. Every value is
/// made up and names no organism; the file is rebuilt the same way each time, so nothing binary is shipped.
/// </summary>
public static class ExampleFamilyList
{
    public const string DefaultFileName = "example-family-list.xlsx";
    public const string FamilySheetName = "1. Consenso de famílias";

    /// <summary>The header row of the first sheet: 3 below the title and the subtitle, 1 without them.</summary>
    public static int FamilyHeaderRow(bool titleRow = true) => titleRow ? 3 : 1;

    private const string Title = "Mistura de proteínas secretadas: consenso das famílias";
    private const string Subtitle = "Exemplo fictício para o tutorial do FamilyQuant. Todos os níveis, ocorrências, abundâncias, notas e referências são inventados.";

    private sealed record Page(string Name, bool Titled, int? FillColumn, int? FillKeyColumn, double[] Widths, string[] Headers, string[][] Rows);

    // The colour of an abbreviation cell follows its decision, as in a hand-kept consensus sheet.
    private static readonly Dictionary<string, string> Fills = new(StringComparer.Ordinal)
    {
        ["Núcleo"] = "teal", ["Padrão"] = "blue", ["Opcional"] = "lilac", ["Excluir"] = "grey", ["Excluir (ou só varredura)"] = "grey",
        ["Condicional (só na fração solúvel)"] = "rose", ["Fundir com LEC"] = "mint", ["Revisar"] = "rose",
    };

    private static readonly string[] None = Array.Empty<string>();

    private static Page[] Pages() => new[]
    {
        new Page(FamilySheetName, true, 0, 5,
            new double[] { 10, 30, 32, 14, 14, 24, 12, 30, 14, 14, 14, 42, 18, 14, 46 },
            new[] { "Sigla", "Nome (português)", "Nome (inglês)", "Nível na literatura (proteômica)", "Nível na literatura (transcriptômica)",
                "Decisão FamilyQuant", "Quantificar à parte? (sim/não)", "Nome de referência cruzada", "Ocorrência na fração solúvel",
                "Ocorrência na fração de membrana", "Abundância típica na mistura", "Domínio(s) Pfam", "Subclasses", "Fontes", "Observações" },
            new[]
            {
                new[] { "TRP", "Serinoprotease tipo tripsina", "Trypsin-like serine protease", "Alto", "Alto", "Núcleo", "sim", "Serine protease 1", "Comum", "Comum", "20 a 35%", "Trypsin (PF00089)", "Tipo tripsina; Tipo quimotripsina", "Ref. 1\nRef. 2", "Família dominante no exemplo." },
                new[] { "MMP", "Metaloproteinase de matriz", "Matrix metalloproteinase", "Alto", "Médio", "Núcleo", "sim", "Interstitial collagenase", "Comum", "Variável", "10 a 20%", "Peptidase_M10 (PF00413); Hemopexin (PF00045); PG_binding_1 (PF01471)", "Com hemopexina; Sem hemopexina", "Ref. 1", "Separar as formas com hemopexina quando o domínio for observado." },
                new[] { "AST", "Metaloprotease tipo astacina", "Astacin-like metalloprotease", "Médio", "Médio", "Núcleo", "sim", "Astacin-like protease", "Comum", "Comum", "5 a 10%", "Astacin (PF01400)", "", "Ref. 2", "" },
                new[] { "PLA", "Fosfolipase A2 secretada", "Secreted phospholipase A2", "Médio", "Alto", "Núcleo", "sim", "Group IIA phospholipase A2", "Comum", "Variável", "5 a 15%", "Phospholip_A2_1 (PF00068)", "Grupo I; Grupo II", "Ref. 2; Ref. 3", "Formas sem atividade catalítica contam junto." },
                new[] { "LEC", "Lectina tipo C", "C-type lectin", "Médio", "Médio", "Padrão", "não", "Galactose-binding lectin", "Comum", "Comum", "1 a 5%", "Lectin_C (PF00059)", "", "Ref. 1", "" },
                new[] { "CLP", "Proteína semelhante a lectina tipo C", "C-type lectin-like protein", "Médio", "Médio", "Fundir com LEC", "não", "Lectin-like protein", "Comum", "Variável", "1 a 5%", "Lectin_C (PF00059)", "Alfa; Beta", "Ref. 3", "Decidido em 3. Divergências: contar junto com LEC." },
                new[] { "GAL", "Galectina", "Galectin", "Baixo", "Médio", "Padrão", "não", "Galectin-1", "Comum", "Comum", "1 a 3%", "Gal-bind_lectin (PF00337)", "", "Ref. 4", "" },
                new[] { "AAO", "L-aminoácido oxidase", "L-amino acid oxidase", "Médio", "Baixo", "Padrão", "sim", "L-amino-acid oxidase", "Comum", "Comum", "1 a 5%", "Amino_oxidase (PF01593); NAD_binding_8 (PF13450)", "", "Ref. 2", "" },
                new[] { "CAP", "Proteína com domínio CAP", "CAP domain protein", "Baixo", "Médio", "Padrão", "sim", "Cysteine-rich secretory protein", "Variável", "Comum", "menos de 2%", "CAP (PF00188); Crisp (PF08562)", "", "Ref. 4", "" },
                new[] { "KZI", "Inibidor tipo Kunitz", "Kunitz-type protease inhibitor", "Médio", "Baixo", "Padrão", "sim", "Kunitz-type inhibitor", "Variável", "Comum", "1 a 3%", "Kunitz_BPTI (PF00014)", "Simples; Duplo", "Ref. 1", "Só os inibidores livres.\nOs domínios Kunitz de proteínas maiores ficam na família da proteína." },
                new[] { "KAZ", "Inibidor tipo Kazal", "Kazal-type serine protease inhibitor", "Baixo", "Baixo", "Opcional", "não", "Kazal-type inhibitor", "Comum", "Comum", "menos de 1%", "Kazal_1 (PF00050)", "", "Ref. 5", "" },
                new[] { "SPN", "Serpina", "Serine protease inhibitor (serpin)", "Baixo", "Traço", "Opcional", "não", "Alpha-1-antiproteinase-like", "Variável", "Variável", "menos de 1%", "Serpin (PF00079)", "", "Ref. 5", "" },
                new[] { "CST", "Cistatina", "Cystatin", "Baixo", "Baixo", "Opcional", "não", "Cystatin", "Comum", "Comum", "menos de 1%", "Cystatin (PF00031)", "", "Ref. 3", "" },
                new[] { "FTN", "Fetuína", "Fetuin, protease inhibitor", "Baixo", "Baixo", "Padrão", "sim", "Fetuin-like inhibitor", "Variável", "Variável", "menos de 1%", "Cystatin (PF00031)", "", "Ref. 7", "Não é enzima, mas importa: quantificar à parte." },
                new[] { "TIL", "Inibidor tipo TIL", "Trypsin inhibitor-like protein", "Traço", "Baixo", "Opcional", "não", "TIL domain inhibitor", "Rara", "Variável", "menos de 0,5%", "TIL (PF01826)", "", "Ref. 6", "" },
                new[] { "NUC", "Desoxirribonuclease I", "Deoxyribonuclease I", "Baixo", "Baixo", "Opcional", "não", "DNase I-like", "Comum", "Comum", "menos de 1%", "Exo_endo_phos (PF03372)", "", "Ref. 3", "" },
                new[] { "RNS", "Ribonuclease", "Ribonuclease A", "Baixo", "Médio", "Condicional (só na fração solúvel)", "não", "Pancreatic-type ribonuclease", "Comum", "Ausente", "menos de 1%", "RnaseA (PF00074)", "", "Ref. 6", "Quantificar só quando a fração solúvel for analisada." },
                new[] { "5NT", "5'-nucleotidase", "5'-nucleotidase", "Baixo", "Baixo", "Opcional", "não", "Ecto-5'-nucleotidase", "Comum", "Comum", "menos de 1%", "Metallophos (PF00149); 5_nucleotid_C (PF02872)", "", "Ref. 3", "" },
                new[] { "PDE", "Fosfodiesterase", "Phosphodiesterase", "Baixo", "Traço", "Opcional", "não", "Ectonucleotide phosphodiesterase", "Comum", "Variável", "menos de 1%", "Phosphodiest (PF01663)", "", "Ref. 3", "" },
                new[] { "LYZ", "Lisozima", "Lysozyme", "Baixo", "Médio", "Padrão", "sim", "Lysozyme C", "Comum", "Rara", "1 a 3%", "Lys (PF00062)", "", "Ref. 4", "" },
                new[] { "NGF", "Fator de crescimento neural", "Nerve growth factor", "Baixo", "Baixo", "Opcional", "não", "Beta-nerve growth factor", "Comum", "Comum", "menos de 1%", "NGF (PF00243)", "", "Ref. 5", "" },
                new[] { "VGF", "Fator de crescimento do endotélio vascular", "Vascular endothelial growth factor", "Baixo", "Traço", "Opcional", "não", "VEGF-like factor", "Variável", "Variável", "menos de 1%", "PDGF (PF00341)", "", "Ref. 5", "" },
                new[] { "NPP", "Peptídeo natriurético", "Natriuretic peptide", "Baixo", "Médio", "Condicional (só na fração solúvel)", "não", "C-type natriuretic peptide", "Variável", "Rara", "menos de 1%", "ANP (PF00212)", "", "Ref. 4", "Quantificar só quando o peptídeo maduro for observado." },
                new[] { "DEF", "Defensina tipo beta", "Beta-defensin", "Traço", "Baixo", "Condicional (só na fração solúvel)", "sim", "Beta-defensin-like peptide", "Variável", "Ausente", "1 a 4%", "Defensin_beta (PF00711)", "", "Ref. 2", "" },
                new[] { "WAP", "Proteína com domínio WAP", "WAP domain protein", "Traço", "Traço", "Excluir", "não", "Whey acidic protein-like", "Ausente", "Rara", "não relatada", "WAP (PF00095)", "", "Ref. 6", "" },
                new[] { "SPY", "Proteína com domínio SPRY", "SPRY domain protein", "Traço", "Traço", "Opcional", "não", "SPRY domain-containing protein", "Rara", "Rara", "menos de 0,5%", "SPRY (PF00622)", "", "Ref. 6", "" },
                new[] { "CTS", "Catepsina", "Cathepsin", "Traço", "Baixo", "Excluir (ou só varredura)", "não", "Cathepsin B", "Rara", "Comum", "não relatada", "Peptidase_C1 (PF00112)", "", "Ref. 5", "" },
                new[] { "LIP", "Lipase", "Lipase", "Traço", "Traço", "Excluir (ou só varredura)", "não", "Pancreatic-type lipase", "Rara", "Desconhecida", "não relatada", "Lipase (PF00151)", "", "Ref. 5", "" },
                new[] { "ECA", "Peptidase semelhante à conversora de angiotensina", "Angiotensin-converting enzyme-like", "Traço", "Traço", "Opcional", "não", "ACE-like peptidase", "Desconhecida", "Rara", "não relatada", "Peptidase_M2 (PF01401)", "", "Ref. 5", "" },
                None,
                new[] { "LEC", "Lectina tipo C ligante de manose", "Mannose-binding C-type lectin", "Traço", "Baixo", "Revisar", "não", "Mannose-binding lectin", "Rara", "Rara", "menos de 0,5%", "Lectin_C (PF00059)", "", "Ref. 3", "Mesma sigla da linha de lectina tipo C acima: confirmar se é outra família." },
                new[] { "GPX", "Glutationa peroxidase", "Glutathione peroxidase", "Traço", "Baixo", "Excluir", "não", "Glutathione peroxidase", "Comum", "Comum", "não relatada", "GSHPx (PF00255)", "", "Ref. 5", "Proteína intracelular." },
                new[] { "PRX", "Peroxirredoxina", "Peroxiredoxin", "Traço", "Baixo", "Excluir", "não", "Peroxiredoxin", "Comum", "Comum", "não relatada", "AhpC-TSA (PF00578)", "", "Ref. 5", "Proteína intracelular." },
                new[] { "CLR", "Calreticulina", "Calreticulin", "Traço", "Baixo", "Excluir", "não", "Calreticulin", "Comum", "Comum", "não relatada", "Calreticulin (PF00262)", "", "Ref. 5", "Proteína intracelular." },
                new[] { "HSP", "Proteína de choque térmico 70", "Heat shock protein 70", "Traço", "Médio", "Excluir", "não", "Heat shock 70 kDa protein", "Comum", "Comum", "não relatada", "HSP70 (PF00012)", "", "Ref. 5", "Proteína intracelular." },
                new[] { "PDI", "Isomerase de dissulfeto de proteína", "Protein disulfide-isomerase", "Traço", "Médio", "Excluir", "não", "Protein disulfide-isomerase", "Comum", "Comum", "não relatada", "Thioredoxin (PF00085)", "", "Ref. 5", "Proteína intracelular." },
                new[] { "ACT", "Actina", "Actin", "Traço", "Baixo", "Excluir", "não", "Actin, cytoplasmic", "Comum", "Comum", "não relatada", "Actin (PF00022)", "", "Ref. 5", "Contaminação celular." },
                new[] { "HIS", "Histonas", "Histones", "Traço", "Traço", "Excluir", "não", "Histone H2A", "Comum", "Comum", "não relatada", "Histone (PF00125)", "", "Ref. 5", "Contaminação celular." },
                new[] { "ALB", "Albumina", "Albumin", "Traço", "Traço", "Excluir", "não", "Serum albumin", "Variável", "Variável", "não relatada", "Serum_albumin (PF00273)", "", "Ref. 5", "Vem do meio na coleta." },
                new[] { "OUTRAS", "Outras raras: hialuronidase (HYA); quitinase (QTN); glutaminil ciclase (GC); aminopeptidase (AP)", "Other rare families: hyaluronidase; chitinase; glutaminyl cyclase; aminopeptidase", "Traço", "Traço", "Opcional", "não", "", "Variável", "Variável", "menos de 0,5% cada", "Glyco_hydro_56 (PF01630); Glyco_hydro_18 (PF00704); Peptidase_M28 (PF04389); Peptidase_M1 (PF01433)", "", "Ref. 5", "Uma linha para quatro famílias raras: separar na importação se quiser quantificar cada uma." },
            }),
        new Page("2. Subclasses", false, null, null,
            new double[] { 12, 22, 46, 30, 46 },
            new[] { "Sigla da família", "Subclasse", "Domínios Pfam exigidos", "Domínios ausentes", "Observações" },
            new[]
            {
                new[] { "MMP", "Com hemopexina", "Peptidase_M10 (PF00413); Hemopexin (PF00045)", "", "" },
                new[] { "MMP", "Sem hemopexina", "Peptidase_M10 (PF00413)", "Hemopexin (PF00045)", "Só o domínio catalítico." },
                new[] { "TRP", "Tipo tripsina", "Trypsin (PF00089)", "", "Corta após lisina ou arginina." },
                new[] { "TRP", "Tipo quimotripsina", "Trypsin (PF00089)", "", "Separar pela sequência, não pelo domínio." },
                new[] { "PLA", "Grupo I", "Phospholip_A2_1 (PF00068)", "", "" },
                new[] { "PLA", "Grupo II", "Phospholip_A2_1 (PF00068)", "", "" },
                new[] { "KZI", "Simples", "Kunitz_BPTI (PF00014)", "", "Um domínio." },
                new[] { "KZI", "Duplo", "Kunitz_BPTI (PF00014)", "", "Dois domínios em sequência." },
                new[] { "CLP", "Alfa", "Lectin_C (PF00059)", "", "" },
                new[] { "CLP", "Beta", "Lectin_C (PF00059)", "", "" },
            }),
        new Page("3. Divergências resolvidas", false, null, null,
            new double[] { 26, 36, 36, 40, 12 },
            new[] { "Tema", "Posição do grupo A", "Posição do grupo B", "Resolução", "Data" },
            new[]
            {
                new[] { "Lectinas e proteínas semelhantes", "Contar como famílias separadas.", "Contar juntas: a diferença é estrutural, não funcional.", "Contar juntas (Fundir com LEC).", "2026-03" },
                new[] { "Inibidores Kunitz em proteínas maiores", "Contar em KZI.", "Contar na família da proteína.", "Na família da proteína; KZI só para os livres.", "2026-04" },
                new[] { "Fosfodiesterase e 5'-nucleotidase", "Uma família.", "Duas famílias.", "Duas famílias.", "2026-04" },
                new[] { "Proteínas intracelulares", "Varredura apenas.", "Excluir.", "Excluir, mas listar em 6. Legenda.", "2026-05" },
            }),
        new Page("4. Regras de quantificação", false, null, null,
            new double[] { 28, 80 },
            new[] { "Regra", "Texto" },
            new[]
            {
                new[] { "Peptídeos compartilhados", "Um peptídeo visto em duas famílias conta como compartilhado e não decide entre elas." },
                new[] { "Famílias condicionais", "Quantificar só quando a fração indicada for analisada." },
                new[] { "Fragmentos", "Fragmentos entram na família da proteína completa mais próxima." },
                new[] { "Proteínas intracelulares", "Ficam fora do total da mistura, mas aparecem na lista de excluídas." },
            }),
        new Page("5. Referências", false, null, null,
            new double[] { 10, 100 },
            new[] { "Código", "Referência (fictícia)" },
            new[]
            {
                new[] { "Ref. 1", "Autora Fictícia A, Autor Fictício B. Proteoma imaginário de uma mistura secretada. Revista Inventada 2025;1:1-10." },
                new[] { "Ref. 2", "Autor Fictício C, et al. Transcriptoma de um tecido que não existe. Anais Imaginários 2024;7:22-31." },
                new[] { "Ref. 3", "Autora Fictícia D. Lectinas e enzimas raras em misturas inventadas. Boletim de Exemplo 2023;3:5-9." },
                new[] { "Ref. 4", "Autor Fictício E, Autora Fictícia F. Peptídeos de uma mistura de mentira. Revista Inventada 2022;9:40-48." },
                new[] { "Ref. 5", "Grupo de Exemplo. Lista de proteínas intracelulares em preparações inventadas. Nota técnica fictícia, 2025." },
                new[] { "Ref. 6", "Autora Fictícia G. Famílias incomuns em misturas fictícias. Anais Imaginários 2021;2:11-19." },
                new[] { "Ref. 7", "Autor Fictício H, et al. Inibidores de protease em uma mistura imaginária. Boletim de Exemplo 2025;5:60-70." },
            }),
        new Page("6. Legenda", false, 0, 0,
            new double[] { 34, 80 },
            new[] { "Decisão (cor da sigla)", "Significado" },
            new[]
            {
                new[] { "Núcleo", "Sempre quantificar; família central da mistura." },
                new[] { "Padrão", "Quantificar." },
                new[] { "Opcional", "Quantificar quando houver identificações suficientes." },
                new[] { "Condicional (só na fração solúvel)", "Quantificar só quando a fração indicada for analisada." },
                new[] { "Fundir com LEC", "Contar junto com a família indicada." },
                new[] { "Revisar", "Decisão pendente; ver Observações." },
                new[] { "Excluir", "Fora do total." },
                new[] { "Excluir (ou só varredura)", "Fora do total; listar se aparecer." },
                None,
                new[] { "Esta planilha", "Exemplo inventado para o tutorial do FamilyQuant. Nenhum valor é real." },
            }),
    };

    // Light fills for the abbreviation cells, by the colour names above; dark teal for headers. No yellow or gold.
    private static readonly string[] FillNames = { "teal", "blue", "lilac", "grey", "rose", "mint" };
    private static readonly string[] FillArgb = { "FFB9E3E6", "FFCFE0F7", "FFE6D6F2", "FFD9D9D9", "FFF6D3DF", "FFD3F0E0" };
    private const string HeaderArgb = "FF284E5B";
    private const uint BodyStyle = 1, TitleStyle = 2, SubtitleStyle = 3, HeaderStyle = 4, FirstFillStyle = 5;
    private static readonly uint PlainKeyStyle = FirstFillStyle + (uint)FillNames.Length;

    /// <summary>Writes the workbook to <paramref name="path"/>, replacing a file there. With
    /// <paramref name="titleRow"/> false the first sheet starts with its header row (a plain variant for tests).</summary>
    public static void Write(string path, bool titleRow = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var document = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
        WorkbookPart workbook = document.AddWorkbookPart();
        workbook.Workbook = new Workbook();
        var strings = workbook.AddNewPart<SharedStringTablePart>();
        strings.SharedStringTable = new SharedStringTable();
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        int Text(string value)
        {
            if (index.TryGetValue(value, out int at)) return at;
            strings.SharedStringTable.AppendChild(new SharedStringItem(new Text(value) { Space = SpaceProcessingModeValues.Preserve }));
            index[value] = at = index.Count;
            return at;
        }
        workbook.AddNewPart<WorkbookStylesPart>().Stylesheet = Styles();
        var sheets = workbook.Workbook.AppendChild(new Sheets());
        uint sheetId = 1;
        foreach (Page page in Pages())
        {
            var part = workbook.AddNewPart<WorksheetPart>();
            var data = new SheetData();
            var columns = new Columns(page.Widths.Select((w, i) => new Column { Min = (uint)(i + 1), Max = (uint)(i + 1), Width = w, CustomWidth = true }));
            int width = page.Headers.Length;
            uint rowNumber = 1;
            var merges = new List<string>();
            void Add(IReadOnlyList<string> cells, Func<int, uint> style)
            {
                var row = new Row { RowIndex = rowNumber };
                for (int c = 0; c < cells.Count; c++)
                {
                    if (cells[c].Length == 0) continue;
                    row.AppendChild(new Cell
                    {
                        CellReference = Reference(c, rowNumber), DataType = CellValues.SharedString,
                        CellValue = new CellValue(Text(cells[c]).ToString(CultureInfo.InvariantCulture)), StyleIndex = style(c),
                    });
                }
                data.AppendChild(row);
                rowNumber++;
            }
            if (page.Titled && titleRow)
            {
                merges.Add(Reference(0, rowNumber) + ":" + Reference(width - 1, rowNumber));
                Add(new[] { Title }, _ => TitleStyle);
                merges.Add(Reference(0, rowNumber) + ":" + Reference(width - 1, rowNumber));
                Add(new[] { Subtitle }, _ => SubtitleStyle);
            }
            Add(page.Headers, _ => HeaderStyle);
            foreach (string[] cells in page.Rows)
            {
                string key = page.FillKeyColumn is int k && k < cells.Length ? cells[k] : "";
                Add(cells, c => c == page.FillColumn ? KeyStyle(key) : BodyStyle);
            }
            var sheet = new Worksheet(columns, data);
            if (merges.Count > 0) sheet.AppendChild(new MergeCells(merges.Select(m => new MergeCell { Reference = m })));
            part.Worksheet = sheet;
            sheets.AppendChild(new Sheet { Id = workbook.GetIdOfPart(part), SheetId = sheetId++, Name = page.Name });
        }
        strings.SharedStringTable.Count = (uint)index.Count;
        strings.SharedStringTable.UniqueCount = (uint)index.Count;
        workbook.Workbook.Save();
    }

    private static uint KeyStyle(string key)
    {
        if (!Fills.TryGetValue(key, out string? fill)) return PlainKeyStyle;
        int at = Array.IndexOf(FillNames, fill);
        return at < 0 ? PlainKeyStyle : FirstFillStyle + (uint)at;
    }

    private static string Reference(int column, uint row)
    {
        string letters = "";
        for (int n = column + 1; n > 0; n = (n - 1) / 26) letters = (char)('A' + (n - 1) % 26) + letters;
        return letters + row.ToString(CultureInfo.InvariantCulture);
    }

    private static Stylesheet Styles()
    {
        Font Face(bool bold = false, bool italic = false, double size = 11, string? argb = null)
        {
            var font = new Font();
            if (bold) font.AppendChild(new Bold());
            if (italic) font.AppendChild(new Italic());
            font.AppendChild(new FontSize { Val = size });
            if (argb != null) font.AppendChild(new Color { Rgb = argb });
            font.AppendChild(new FontName { Val = "Calibri" });
            return font;
        }
        Fill Solid(string argb) => new(new PatternFill(new ForegroundColor { Rgb = argb }, new BackgroundColor { Indexed = 64 })
            { PatternType = PatternValues.Solid });
        var fonts = new Fonts(Face(), Face(bold: true), Face(bold: true, size: 14), Face(bold: true, argb: "FFFFFFFF"),
            Face(italic: true, size: 10, argb: "FF595959"));
        var fills = new Fills(new Fill(new PatternFill { PatternType = PatternValues.None }),
            new Fill(new PatternFill { PatternType = PatternValues.Gray125 }), Solid(HeaderArgb));
        foreach (string argb in FillArgb) fills.AppendChild(Solid(argb));
        CellFormat Format(uint font, uint fill, bool wrap) => new(new Alignment { WrapText = wrap, Vertical = VerticalAlignmentValues.Top })
            { FontId = font, FillId = fill, BorderId = 0, FormatId = 0, ApplyFont = true, ApplyFill = fill != 0, ApplyAlignment = true };
        var formats = new CellFormats(new CellFormat { FontId = 0, FillId = 0, BorderId = 0, FormatId = 0 },
            Format(0, 0, true), Format(2, 0, false), Format(4, 0, false), Format(3, 2, true));
        for (int i = 0; i < FillArgb.Length; i++) formats.AppendChild(Format(1, (uint)(3 + i), false));
        formats.AppendChild(Format(1, 0, false));
        return new Stylesheet(fonts, fills, new Borders(new Border()),
            new CellStyleFormats(new CellFormat { FontId = 0, FillId = 0, BorderId = 0 }), formats);
    }
}
