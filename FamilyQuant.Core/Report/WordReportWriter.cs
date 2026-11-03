using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using FamilyQuant.Operations;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using W = DocumentFormat.OpenXml.Wordprocessing;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using P = DocumentFormat.OpenXml.Drawing.Pictures;

namespace FamilyQuant.Report;

/// <summary>Writes the report model without recalculating science or re-encoding PNGs.</summary>
public static class WordReportWriter
{
    private const int PageWidth = 9360;
    private static readonly DateTimeOffset ZipDate = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly XNamespace PackageR = "http://schemas.openxmlformats.org/package/2006/relationships";

    public static byte[] BuildBytes(ReportDocument document, ReportSelection? selection = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var section in document.Sections)
        {
            RequireId(section.Id);
            foreach (var block in section.Blocks) RequireId(block.Id);
        }
        void RequireId(string id)
        {
            if (string.IsNullOrWhiteSpace(id) || !ids.Add(id))
                throw new ArgumentException("Report section and block ids must be nonempty and unique.", nameof(document));
        }
        var excluded = new HashSet<string>(selection?.ExcludedIds ?? Array.Empty<string>(), StringComparer.Ordinal);
        var sections = document.Sections.Where(s => !excluded.Contains(s.Id)).ToList();
        using var buffer = new MemoryStream();
        using (var word = WordprocessingDocument.Create(buffer, WordprocessingDocumentType.Document, true))
        {
            var main = word.AddMainDocumentPart();
            var body = new W.Body();
            main.Document = new W.Document(body);
            var styles = main.AddNewPart<StyleDefinitionsPart>("rIdStyles");
            styles.Styles = Styles();
            uint imageId = 0;
            for (int index = 0; index < sections.Count; index++)
            {
                var section = sections[index];
                if (index > 0) body.Append(new W.Paragraph(new W.Run(new W.Break { Type = W.BreakValues.Page })));
                string bookmark = "section_" + index.ToString(CultureInfo.InvariantCulture);
                var heading = Paragraph(section.Id == ReportSectionIds.Title ? document.Title : section.Title,
                    section.Id == ReportSectionIds.Title ? "Title" : "Heading1");
                heading.InsertAfter(new W.BookmarkStart { Id = index.ToString(CultureInfo.InvariantCulture), Name = bookmark }, heading.ParagraphProperties!);
                heading.Append(new W.BookmarkEnd { Id = index.ToString(CultureInfo.InvariantCulture) });
                body.Append(heading);
                if (section.Id == ReportSectionIds.Title)
                {
                    if (document.Authors.Length > 0) body.Append(Paragraph(document.Authors));
                    body.Append(Paragraph("FamilyQuant " + document.ApplicationVersion));
                }
                if (section.Id == ReportSectionIds.Contents)
                {
                    for (int other = 0; other < sections.Count; other++)
                    {
                        if (sections[other].Id is ReportSectionIds.Title or ReportSectionIds.Contents) continue;
                        body.Append(new W.Paragraph(new W.Hyperlink(new W.Run(new W.Text(sections[other].Title)))
                        { Anchor = "section_" + other.ToString(CultureInfo.InvariantCulture) }));
                    }
                }
                foreach (var block in section.Blocks.Where(b => !excluded.Contains(b.Id)))
                {
                    switch (block)
                    {
                        case ReportParagraph paragraph:
                            foreach (string line in Lines(paragraph.Text)) body.Append(Paragraph(line));
                            break;
                        case ReportTable table:
                            if (table.Columns.Count == 0 || table.Rows.Any(r => r.Count != table.Columns.Count))
                                throw new ArgumentException("Report tables require columns and equally sized rows.", nameof(document));
                            body.Append(Paragraph(table.Title, "Heading2"));
                            body.Append(Table(table));
                            foreach (string line in Lines(table.Caption)) body.Append(Paragraph(line, "Caption"));
                            break;
                        case ReportFigure figure:
                            if (figure.PixelWidth <= 0 || figure.PixelHeight <= 0 || figure.PngBytes.Length < 8
                                || !figure.PngBytes.Take(8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
                                throw new ArgumentException("Report figures require PNG bytes and positive pixel dimensions.", nameof(document));
                            body.Append(Paragraph(figure.Title, "Heading2"));
                            string relation = "rIdImage" + (++imageId).ToString(CultureInfo.InvariantCulture);
                            var imagePart = main.AddImagePart(ImagePartType.Png, relation);
                            using (var png = new MemoryStream(figure.PngBytes, false)) imagePart.FeedData(png);
                            body.Append(Image(figure, relation, imageId));
                            foreach (string line in Lines(figure.Caption)) body.Append(Paragraph(line, "Caption"));
                            break;
                        default: throw new ArgumentException("Unsupported report block.", nameof(document));
                    }
                }
            }
            var footer = main.AddNewPart<FooterPart>("rIdFooter");
            footer.Footer = new W.Footer(new W.Paragraph(new W.ParagraphProperties(new W.Justification { Val = W.JustificationValues.Center }),
                new W.Run(new W.Text("Page ")), new W.SimpleField { Instruction = "PAGE" }));
            body.Append(new W.SectionProperties(
                new W.FooterReference { Type = W.HeaderFooterValues.Default, Id = "rIdFooter" },
                new W.PageSize { Width = 12240, Height = 15840 },
                new W.PageMargin { Top = 1440, Bottom = 1440, Left = 1440, Right = 1440, Header = 720, Footer = 720, Gutter = 0 }));
        }
        return DeterministicPackage(buffer.ToArray(), document);
    }

    public static void Write(string path, ReportDocument document, ReportSelection? selection = null, OperationContext? operation = null)
    {
        operation?.Report("report", "Preparing report document");
        byte[] bytes = BuildBytes(document, selection);
        operation?.ThrowIfCancellationRequested();
        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        string temp = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temp, bytes);
            void Publish() => File.Move(temp, fullPath, true);
            if (operation == null) Publish(); else operation.Commit(Publish);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    private static IEnumerable<string> Lines(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
    private static W.Paragraph Paragraph(string text, string? style = null)
    {
        var properties = new W.ParagraphProperties();
        if (style != null) properties.ParagraphStyleId = new W.ParagraphStyleId { Val = style };
        return new(properties, new W.Run(new W.Text(text) { Space = SpaceProcessingModeValues.Preserve }));
    }

    private static W.Styles Styles()
    {
        W.Style Style(string id, string name, string size, bool bold = false)
        {
            var paragraph = new W.StyleParagraphProperties(new W.KeepNext(), new W.SpacingBetweenLines { Before = "180", After = "100" });
            if (id is "Heading1" or "Heading2") paragraph.Append(new W.OutlineLevel { Val = id == "Heading1" ? 0 : 1 });
            var run = new W.StyleRunProperties(new W.RunFonts { Ascii = "Calibri", HighAnsi = "Calibri" });
            if (bold) run.Append(new W.Bold());
            run.Append(new W.FontSize { Val = size });
            return new(new W.StyleName { Val = name }, paragraph, run) { Type = W.StyleValues.Paragraph, StyleId = id };
        }
        return new W.Styles(
            new W.DocDefaults(new W.RunPropertiesDefault(new W.RunPropertiesBaseStyle(
                new W.RunFonts { Ascii = "Calibri", HighAnsi = "Calibri" }, new W.FontSize { Val = "22" },
                new W.Languages { Val = "en-US" })),
                new W.ParagraphPropertiesDefault(new W.ParagraphPropertiesBaseStyle(new W.SpacingBetweenLines { After = "120" }))),
            Style("Title", "Title", "40", true), Style("Heading1", "heading 1", "30", true),
            Style("Heading2", "heading 2", "24", true), Style("Caption", "caption", "20"));
    }

    private static W.Table Table(ReportTable data)
    {
        int[] widths = Enumerable.Range(0, data.Columns.Count)
            .Select(index => PageWidth / data.Columns.Count + (index < PageWidth % data.Columns.Count ? 1 : 0)).ToArray();
        var result = new W.Table(new W.TableProperties(new W.TableWidth { Width = PageWidth.ToString(CultureInfo.InvariantCulture), Type = W.TableWidthUnitValues.Dxa },
            new W.TableLayout { Type = W.TableLayoutValues.Fixed }),
            new W.TableGrid(widths.Select(width => new W.GridColumn { Width = width.ToString(CultureInfo.InvariantCulture) })));
        W.TableRow Row(IReadOnlyList<string> cells, bool header)
        {
            var row = new W.TableRow();
            if (header) row.Append(new W.TableRowProperties(new W.TableHeader()));
            for (int index = 0; index < cells.Count; index++)
            {
                string text = cells[index];
                var cell = new W.TableCell(new W.TableCellProperties(new W.TableCellWidth { Width = widths[index].ToString(CultureInfo.InvariantCulture), Type = W.TableWidthUnitValues.Dxa }));
                foreach (string line in Lines(text))
                {
                    var paragraph = Paragraph(line);
                    if (header) paragraph.GetFirstChild<W.Run>()!.PrependChild(new W.RunProperties(new W.Bold()));
                    else if (double.TryParse(line, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                        paragraph.ParagraphProperties!.Append(new W.Justification { Val = W.JustificationValues.Right });
                    cell.Append(paragraph);
                }
                row.Append(cell);
            }
            return row;
        }
        result.Append(Row(data.Columns, true));
        foreach (var row in data.Rows) result.Append(Row(row, false));
        return result;
    }

    private static W.Paragraph Image(ReportFigure figure, string relation, uint id)
    {
        long cx = PageWidth * 635L;
        long cy = (long)Math.Round(cx * ((double)figure.PixelHeight / figure.PixelWidth));
        const long maxHeight = 7200L * 635;
        if (cy > maxHeight) { cx = cx * maxHeight / cy; cy = maxHeight; }
        return new W.Paragraph(new W.ParagraphProperties(new W.KeepNext()), new W.Run(new W.Drawing(new DW.Inline(
            new DW.Extent { Cx = cx, Cy = cy }, new DW.DocProperties { Id = id, Name = figure.Title, Description = figure.Caption },
            new A.Graphic(new A.GraphicData(new P.Picture(
                new P.NonVisualPictureProperties(new P.NonVisualDrawingProperties { Id = id, Name = figure.Id }, new P.NonVisualPictureDrawingProperties()),
                new P.BlipFill(new A.Blip { Embed = relation }, new A.Stretch(new A.FillRectangle())),
                new P.ShapeProperties(new A.Transform2D(new A.Offset { X = 0, Y = 0 }, new A.Extents { Cx = cx, Cy = cy }),
                    new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle })))
            { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" })))));
    }

    // OPC can choose a random root relationship id. Canonicalize that id and metadata, then
    // repack sorted entries with fixed ZIP dates so culture and wall-clock time cannot alter bytes.
    private static byte[] DeterministicPackage(byte[] original, ReportDocument document)
    {
        using var source = new ZipArchive(new MemoryStream(original));
        var files = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var entry in source.Entries)
        {
            using var input = entry.Open(); using var memory = new MemoryStream(); input.CopyTo(memory);
            files.Add(entry.FullName, memory.ToArray());
        }
        XDocument Xml(string name)
        {
            using var input = new MemoryStream(files[name]);
            return XDocument.Load(input);
        }
        void Put(string name, XDocument xml) => files[name] = Encoding.UTF8.GetBytes(xml.ToString(SaveOptions.DisableFormatting));
        var rels = Xml("_rels/.rels");
        var links = rels.Root!.Elements().OrderBy(e => (string?)e.Attribute("Target"), StringComparer.Ordinal).ToList();
        rels.Root.RemoveNodes();
        int count = 0;
        foreach (var link in links) { link.SetAttributeValue("Id", "rId" + (++count).ToString(CultureInfo.InvariantCulture)); rels.Root.Add(link); }
        rels.Root.Add(new XElement(PackageR + "Relationship", new XAttribute("Id", "rIdCore"),
            new XAttribute("Type", "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties"), new XAttribute("Target", "docProps/core.xml")));
        Put("_rels/.rels", rels);
        XNamespace cp = "http://schemas.openxmlformats.org/package/2006/metadata/core-properties";
        XNamespace dc = "http://purl.org/dc/elements/1.1/";
        XNamespace dt = "http://purl.org/dc/terms/";
        XNamespace xsi = "http://www.w3.org/2001/XMLSchema-instance";
        string Date(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        Put("docProps/core.xml", new XDocument(new XElement(cp + "coreProperties", new XAttribute(XNamespace.Xmlns + "dcterms", dt),
            new XElement(dc + "title", document.Title), new XElement(dc + "creator", document.Authors),
            new XElement(cp + "lastModifiedBy", "FamilyQuant " + document.ApplicationVersion),
            new XElement(dt + "created", new XAttribute(xsi + "type", "dcterms:W3CDTF"), Date(document.CreatedUtc)),
            new XElement(dt + "modified", new XAttribute(xsi + "type", "dcterms:W3CDTF"), Date(document.ModifiedUtc)))));
        var types = Xml("[Content_Types].xml");
        types.Root!.Add(new XElement(types.Root.Name.Namespace + "Override", new XAttribute("PartName", "/docProps/core.xml"),
            new XAttribute("ContentType", "application/vnd.openxmlformats-package.core-properties+xml")));
        Put("[Content_Types].xml", types);
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            foreach (var file in files)
            {
                var entry = zip.CreateEntry(file.Key, CompressionLevel.Optimal); entry.LastWriteTime = ZipDate;
                using var content = entry.Open(); content.Write(file.Value);
            }
        }
        return output.ToArray();
    }
}
