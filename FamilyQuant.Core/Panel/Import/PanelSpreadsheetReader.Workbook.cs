using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace FamilyQuant.Panels.Import;

public sealed partial class PanelSpreadsheetReader
{
    public PanelWorkbookRead ReadWorkbook(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var result = new PanelWorkbookRead { SourceName = Path.GetFileName(path) };
        try
        {
            byte[] original = ReadBytes(path);
            result.SourceSha256 = Convert.ToHexString(SHA256.HashData(original));
            using var stream = new MemoryStream(original, writable: false);
            using var document = SpreadsheetDocument.Open(stream, false);
            foreach (var sheet in Sheets(document))
            {
                string name = sheet.Name?.Value ?? string.Empty;
                var source = new PanelWorkbookSheet { SheetName = name, IsHidden = sheet.State?.Value is { } state && state != SheetStateValues.Visible };
                result.Sheets.Add(source);
                source.Preview = ReadPreview(path, name);
                foreach (var problem in source.Preview.Problems) problem.SheetName = name;
                result.Problems.AddRange(source.Preview.Problems);
                if (source.Preview.SourceSha256 != result.SourceSha256)
                    throw new InvalidDataException("The workbook changed while its sheets were being read. Read it again.");
                try
                {
                    var grid = Grid.Load(document, name);
                    source.HasFilterHeader = grid.FilterRow > 0 || grid.TableHeaderRow > 0;
                    source.SourceRows = grid.Rows.Select(row => new PanelPreviewRow
                    {
                        RowNumber = row,
                        Cells = Enumerable.Range(1, grid.Width).Select(column => new PanelPreviewCell
                        {
                            Column = column, ColumnLetter = ColumnLetter(column), Text = grid.At(row, column),
                            Fill = grid.FillAt(row, column), MergeRange = grid.MergeAt(row, column)
                        }).Where(cell => cell.Text.Length > 0 || cell.Fill.Length > 0).ToList()
                    }).Where(row => row.Cells.Count > 0).ToList();
                    if (source.Preview.ProposedHeaderRow > 0)
                    {
                        source.Table = ReadTable(path, name, source.Preview.ProposedHeaderRow);
                        foreach (var problem in source.Table.Problems) problem.SheetName = name;
                        result.Problems.AddRange(source.Table.Problems);
                        if (source.Table.SourceSha256 != result.SourceSha256)
                            throw new InvalidDataException("The workbook changed while its sheets were being read. Read it again.");
                    }
                }
                catch (Exception error) when (IsReadFailure(error))
                {
                    source.IsUncertain = true;
                    result.Problems.Add(new PanelImportProblem { Kind = PanelImportProblemKind.FileUnreadable,
                        Severity = PanelImportSeverity.Blocking, SheetName = name, Message = Describe(error) });
                }
            }
            if (Convert.ToHexString(SHA256.HashData(ReadBytes(path))) != result.SourceSha256)
                throw new InvalidDataException("The workbook changed while its sheets were being read. Read it again.");
        }
        catch (Exception error) when (IsReadFailure(error))
        {
            result.Problems.Add(new PanelImportProblem { Kind = PanelImportProblemKind.FileUnreadable,
                Severity = PanelImportSeverity.Blocking, Message = Describe(error) });
        }
        return result;
    }
}
