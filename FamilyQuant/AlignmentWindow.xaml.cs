using FamilyQuant.Evidence.Alignment;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace FamilyQuant;

/// <summary>Align selected: a read-only view of the highlighted proteins beside their reviewed relatives.</summary>
public partial class AlignmentWindow : Window
{
    /// <summary>Tests read the copied text here instead of the system clipboard.</summary>
    public static Action<string>? ClipboardOverride { get; set; }

    public AlignmentViewModel Model { get; }

    public AlignmentWindow(AlignmentViewModel model)
    {
        Model = model ?? throw new ArgumentNullException(nameof(model));
        InitializeComponent();
        DataContext = model;
    }

    private void CopyFasta_Click(object sender, RoutedEventArgs e) => Copy(Model.FastaText, "The aligned sequences were copied as FASTA.");
    private void CopyText_Click(object sender, RoutedEventArgs e) => Copy(Model.AlignedText, "The aligned text was copied.");
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Copy(string text, string done)
    {
        if (text.Length == 0) return;
        if (ClipboardOverride != null) { ClipboardOverride(text); Model.Tell(done); return; }
        try { Clipboard.SetText(text); Model.Tell(done); }
        catch (ExternalException) { Model.Tell("Nothing was copied: another program is holding the clipboard. Try again in a moment."); }
    }
}

/// <summary>
/// Draws a <see cref="MultipleAlignment"/> as blocks that fit the available width: a ruler of column
/// numbers, then one line per sequence with its name, residues and the residue number reached.
/// Cysteines are marked, residues that differ from the first protein are muted, and stored Pfam spans
/// run as a bar under their residues. Colours come from the theme tokens.
/// </summary>
public sealed class AlignmentView : FrameworkElement
{
    public static readonly DependencyProperty AlignmentProperty = DependencyProperty.Register(nameof(Alignment),
        typeof(MultipleAlignment), typeof(AlignmentView),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    private const double FontSize = 13;
    private const int MaximumNameCharacters = 14;
    private static readonly Typeface Mono = new(new FontFamily("Consolas"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private static readonly Typeface Ui = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private static readonly string[] DomainBrushKeys =
        { "ChartIdentifiedBrush", "ChartDeNovoBrush", "ChartNotQuantifiedBrush", "ChartPeptideLikeBrush", "ChartQuantifiedBrush", "ChartTicBrush" };

    private Layout? layout;

    public MultipleAlignment? Alignment
    {
        get => (MultipleAlignment?)GetValue(AlignmentProperty);
        set => SetValue(AlignmentProperty, value);
    }

    public AlignmentView()
    {
        Loaded += (_, _) => { Themes.ThemeManager.ThemeChanged -= Refresh; Themes.ThemeManager.ThemeChanged += Refresh; };
        Unloaded += (_, _) => Themes.ThemeManager.ThemeChanged -= Refresh;
    }

    private void Refresh(object? sender, EventArgs e) => InvalidateVisual();

    /// <summary>Columns on one line at the measured width; 0 before the first measure.</summary>
    public int ColumnsPerLine => layout?.PerLine ?? 0;

    private sealed record Layout(double CharWidth, double LineHeight, double NameWidth, int PerLine, double LegendHeight,
        List<string> Domains, double BlockHeight, int Blocks, double Width);

    protected override Size MeasureOverride(Size availableSize)
    {
        var alignment = Alignment;
        if (alignment == null || alignment.Rows.Count == 0) { layout = null; return new Size(0, 0); }
        double dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var probe = new FormattedText("M", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, FontSize, TextInk(), dip);
        double charWidth = probe.WidthIncludingTrailingWhitespace, lineHeight = Math.Ceiling(probe.Height * 1.3);
        int nameCharacters = Math.Min(MaximumNameCharacters, alignment.Rows.Max(r => r.Input.Label.Length));
        double nameWidth = (nameCharacters + 2) * charWidth, numberWidth = 7 * charWidth;
        double width = double.IsInfinity(availableSize.Width) ? nameWidth + numberWidth + 60 * charWidth : availableSize.Width;
        int perLine = Math.Max(10, (int)((width - nameWidth - numberWidth) / charWidth) / 10 * 10);
        var domains = alignment.Rows.SelectMany(r => r.Domains).Select(d => d.Accession + " " + d.Name).Distinct(StringComparer.Ordinal).ToList();
        // The legend wraps exactly as OnRender draws it.
        double legendHeight = lineHeight + 2;
        if (domains.Count > 0)
        {
            int lines = 1; double lx = 0;
            foreach (string domain in domains)
            {
                double item = 28 + new FormattedText(domain, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Ui, 12, TextInk(), dip).Width;
                if (lx > 0 && lx + item > width) { lines++; lx = 0; }
                lx += item + 18;
            }
            legendHeight += (lines + 1) * (lineHeight + 2);
        }
        legendHeight += lineHeight * 0.5;
        int blocks = (alignment.Columns + perLine - 1) / perLine;
        double blockHeight = lineHeight * (alignment.Rows.Count + 1) + lineHeight * 0.8;
        layout = new Layout(charWidth, lineHeight, nameWidth, perLine, legendHeight, domains, blockHeight, blocks, width);
        return new Size(double.IsInfinity(availableSize.Width) ? width : Math.Min(width, availableSize.Width),
            legendHeight + blocks * blockHeight);
    }

    private Brush Token(string key) => TryFindResource(key) as Brush ?? TextInk();

    private Brush TextInk() => TryFindResource("TextBrush") as Brush ?? SystemColors.ControlTextBrush;

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var alignment = Alignment;
        if (alignment == null || layout == null) return;
        double dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        Brush text = TextInk(), muted = Token("MutedBrush"), accent = Token("AccentBrush"), mark = Token("SelectionBrush"), edge = Token("EdgeBrush");
        double cw = layout.CharWidth, lh = layout.LineHeight;
        FormattedText Words(string value, Brush brush, double size = 12) =>
            new(value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Ui, size, brush, dip);
        FormattedText Residues(string value, Brush brush) =>
            new(value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, FontSize, brush, dip);

        // Legend.
        double x = 0, y = 0;
        dc.DrawRectangle(mark, null, new Rect(x, y + 1, cw + 2, lh - 2));
        var c = Residues("C", accent); c.SetFontWeight(FontWeights.Bold); dc.DrawText(c, new Point(x + 1, y + (lh - c.Height) / 2));
        x += cw + 8; var legend = Words("cysteine", muted); dc.DrawText(legend, new Point(x, y + (lh - legend.Height) / 2)); x += legend.Width + 18;
        var differs = Residues("K", muted); dc.DrawText(differs, new Point(x, y + (lh - differs.Height) / 2)); x += cw + 6;
        legend = Words("faded: differs from the first protein", muted); dc.DrawText(legend, new Point(x, y + (lh - legend.Height) / 2)); x += legend.Width + 18;
        var gap = Residues("-", muted); dc.DrawText(gap, new Point(x, y + (lh - gap.Height) / 2)); x += cw + 6;
        legend = Words("gap", muted); dc.DrawText(legend, new Point(x, y + (lh - legend.Height) / 2));
        if (layout.Domains.Count > 0)
        {
            y += lh + 2; x = 0;
            legend = Words("Pfam domains, where positions are stored for that exact sequence:", muted);
            dc.DrawText(legend, new Point(0, y + (lh - legend.Height) / 2));
            y += lh + 2;
            for (int d = 0; d < layout.Domains.Count; d++)
            {
                var name = Words(layout.Domains[d], text);
                if (x > 0 && x + 28 + name.Width > layout.Width) { x = 0; y += lh + 2; }
                dc.DrawRoundedRectangle(Token(DomainBrushKeys[d % DomainBrushKeys.Length]), null, new Rect(x, y + lh / 2 - 2, 22, 4), 2, 2);
                dc.DrawText(name, new Point(x + 28, y + (lh - name.Height) / 2));
                x += 28 + name.Width + 18;
            }
        }

        var first = alignment.Rows[0].Gapped;
        var reached = new int[alignment.Rows.Count];
        var thin = new Pen(edge, 1);
        for (int block = 0; block < layout.Blocks; block++)
        {
            int start = block * layout.PerLine, count = Math.Min(layout.PerLine, alignment.Columns - start);
            double top = layout.LegendHeight + block * layout.BlockHeight, left = layout.NameWidth;
            // Ruler: a tick and the column number every ten columns.
            for (int column = start; column < start + count; column++)
            {
                if ((column + 1) % 10 != 0) continue;
                double cx = left + (column - start + 0.5) * cw;
                dc.DrawLine(thin, new Point(cx, top + lh - 4), new Point(cx, top + lh - 1));
                var number = Words((column + 1).ToString(CultureInfo.InvariantCulture), muted, 10.5);
                dc.DrawText(number, new Point(Math.Max(left, cx - number.Width / 2), top + lh - 5 - number.Height));
            }
            for (int r = 0; r < alignment.Rows.Count; r++)
            {
                AlignedRow row = alignment.Rows[r];
                double rowTop = top + lh * (r + 1);
                string label = row.Input.Label.Length > MaximumNameCharacters ? row.Input.Label[..(MaximumNameCharacters - 1)] + "~" : row.Input.Label;
                var name = Residues(label, r == 0 ? accent : text);
                if (r == 0) name.SetFontWeight(FontWeights.Bold);
                dc.DrawText(name, new Point(0, rowTop + (lh - name.Height) / 2));
                string piece = row.Gapped.Substring(start, count);
                for (int i = 0; i < piece.Length; i++)
                    if (char.ToUpperInvariant(piece[i]) == 'C')
                        dc.DrawRectangle(mark, null, new Rect(left + i * cw, rowTop + 1, cw, lh - 2));
                foreach (var (domain, index) in row.Domains.Select((d, i) => (d, i)))
                {
                    int from = Math.Max(start, domain.FirstColumn), to = Math.Min(start + count - 1, domain.LastColumn);
                    if (from > to) continue;
                    int key = layout.Domains.IndexOf(domain.Accession + " " + domain.Name);
                    dc.DrawRectangle(Token(DomainBrushKeys[Math.Max(0, key) % DomainBrushKeys.Length]), null,
                        new Rect(left + (from - start) * cw, rowTop + lh - 3, (to - from + 1) * cw, 2.5));
                }
                var residues = Residues(piece, text);
                for (int i = 0; i < piece.Length;)
                {
                    int kind = Kind(piece[i], r == 0 ? piece[i] : first[start + i]);
                    int run = i;
                    while (run < piece.Length && Kind(piece[run], r == 0 ? piece[run] : first[start + run]) == kind) run++;
                    if (kind == 1) residues.SetForegroundBrush(muted, i, run - i);
                    else if (kind == 2) { residues.SetForegroundBrush(accent, i, run - i); residues.SetFontWeight(FontWeights.Bold, i, run - i); }
                    i = run;
                }
                dc.DrawText(residues, new Point(left, rowTop + (lh - residues.Height) / 2));
                reached[r] += piece.Count(ch => ch != '-');
                var end = Words(reached[r].ToString(CultureInfo.InvariantCulture), muted, 11);
                dc.DrawText(end, new Point(left + count * cw + cw, rowTop + (lh - end.Height) / 2));
            }
        }
    }

    // 0: same as the first protein, 1: a gap or a different residue, 2: a cysteine.
    private static int Kind(char residue, char firstResidue)
    {
        char upper = char.ToUpperInvariant(residue);
        if (upper == 'C') return 2;
        if (residue == '-' || upper != char.ToUpperInvariant(firstResidue)) return 1;
        return 0;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        ToolTip = Describe(e.GetPosition(this));
    }

    /// <summary>The centre of one residue cell as drawn, or null before layout or outside the alignment.</summary>
    public Point? CellCenter(int row, int column)
    {
        var alignment = Alignment;
        if (alignment == null || layout == null || row < 0 || row >= alignment.Rows.Count || column < 0 || column >= alignment.Columns) return null;
        int block = column / layout.PerLine;
        return new Point(layout.NameWidth + (column - block * layout.PerLine + 0.5) * layout.CharWidth,
            layout.LegendHeight + block * layout.BlockHeight + (row + 1.5) * layout.LineHeight);
    }

    /// <summary>The sequence, residue number and column under a point, or null outside the residues.</summary>
    public string? Describe(Point point)
    {
        var alignment = Alignment;
        if (alignment == null || layout == null || point.Y < layout.LegendHeight || point.X < layout.NameWidth) return null;
        int block = (int)((point.Y - layout.LegendHeight) / layout.BlockHeight);
        int line = (int)((point.Y - layout.LegendHeight - block * layout.BlockHeight) / layout.LineHeight) - 1;
        int column = block * layout.PerLine + (int)((point.X - layout.NameWidth) / layout.CharWidth);
        if (block >= layout.Blocks || line < 0 || line >= alignment.Rows.Count || column >= Math.Min(alignment.Columns, (block + 1) * layout.PerLine))
            return null;
        AlignedRow row = alignment.Rows[line];
        char residue = row.Gapped[column];
        string where = "column " + (column + 1).ToString(CultureInfo.CurrentCulture);
        if (residue == '-') return row.Input.Label + ": gap at " + where;
        int number = row.Gapped.Take(column + 1).Count(ch => ch != '-');
        string domains = string.Join(", ", row.Domains.Where(d => d.FirstColumn <= column && column <= d.LastColumn).Select(d => d.Accession + " " + d.Name));
        return row.Input.Label + ": " + residue + number.ToString(CultureInfo.CurrentCulture) + ", " + where
            + (domains.Length > 0 ? "; " + domains : "");
    }
}
