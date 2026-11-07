using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    // Design-only pages for Z12p2a. No workbook reader, importer or project is used.
    private static readonly string[] ImportMockSteps = { "Welcome", "File and sheet", "Columns", "Special rows", "Evidence matches", "Summary", "Done" };
    private static readonly string[] ImportMockFiles = { "welcome", "file-and-sheet", "columns", "special-rows", "evidence-matches", "summary", "done" };
    private static readonly string[] ImportMockTitles = { "Import your family list", "Choose your spreadsheet", "Choose what each column means",
        "Review special rows", "Match your list to stored evidence", "Review your family list", "Your family list is imported" };
    private static readonly string[] ImportMockExplanations = {
        "Bring a spreadsheet of abbreviations and family names; review every choice before import.",
        "Choose a file and sheet, then check the preview against your spreadsheet.",
        "Check the suggested columns; an example from your sheet appears below each choice.",
        "Resolve each row marked Needs a choice before continuing.",
        "Confirm or skip the suggestions for each family; nothing is matched for you.",
        "Check what will be created; nothing has changed yet.",
        "Open the family checklist to review which proteins belong in each family." };

    private static void ImportWizardMockTests(string? output = null)
    {
        Section("Import wizard design - seven static pages, real tokens and bounded native controls");
        EnsureApplication();
        var previous = Themes.ThemeManager.Current;
        try
        {
            foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
            foreach (var size in new[] { (Width: 1366, Height: 768), (Width: 1920, Height: 1080) })
            foreach (var state in Enumerable.Range(0, ImportMockSteps.Length).Select(p => (Page: p, Variant: ""))
                .Concat(new[] { (Page: 1, Variant: "wide-sheet"), (Page: 2, Variant: "decisions"), (Page: 3, Variant: "duplicate"), (Page: 3, Variant: "empty") }))
            {
                int page = state.Page;
                Themes.ThemeManager.Apply(theme);
                var window = ImportWizardMock(page, size.Width, size.Height, state.Variant);
                try
                {
                    window.Show(); PumpDispatcher();
                    var viewport = (FrameworkElement)window.Content;
                    viewport.Measure(new Size(size.Width, size.Height));
                    viewport.Arrange(new Rect(0, 0, size.Width, size.Height)); viewport.UpdateLayout();
                    string label = theme + " " + size.Width + " " + ImportMockSteps[page] + " " + state.Variant;
                    var footer = (FrameworkElement)window.FindName("mockFooter");
                    var heading = (TextBlock)window.FindName("mockHeading");
                    var forward = (Button)window.FindName("mockForward");
                    Check(label + " keeps the heading and forward action within the screen", heading.IsVisible && forward.IsVisible
                        && footer.TranslatePoint(new Point(), window).Y + footer.ActualHeight <= window.ActualHeight
                        && heading.Text == (state.Variant == "decisions" ? "Review spreadsheet decisions" : ImportMockTitles[page]));
                    Check(label + " has one primary action and explicit mock labelling", WalkTree(window).OfType<Button>()
                        .Count(b => ReferenceEquals(b.Style, window.FindResource("PrimaryButton"))) == 1
                        && WalkTree(window).OfType<TextBlock>().Any(t => t.Text == "Design preview | Fictitious data"));
                    if (output != null)
                    {
                        string folder = Path.Combine(output, theme.ToString().ToLowerInvariant(), size.Width + "x" + size.Height);
                        Directory.CreateDirectory(folder);
                        ScreenHarness.Save(window, Path.Combine(folder, (page + 1).ToString("D2") + "-" + ImportMockFiles[page]
                            + (state.Variant.Length == 0 ? "" : "-" + state.Variant) + ".png"), size.Width, size.Height);
                    }
                }
                finally { window.Close(); PumpDispatcher(); }
            }
        }
        finally { Themes.ThemeManager.Apply(previous); }
        if (output != null) Console.WriteLine("Design mock screens: 44 PNGs; no workbook or project opened; " + Path.GetFullPath(output));
    }

    private static Window ImportWizardMock(int page, int width, int height, string variant = "")
    {
        var window = new Window { Title = "Import your family list - design preview", Width = width, Height = height,
            Left = -30000, Top = -30000, WindowStartupLocation = WindowStartupLocation.Manual,
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowActivated = false, ShowInTaskbar = false };
        NameScope.SetNameScope(window, new NameScope());
        var root = new Grid { ClipToBounds = true };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        root.ColumnDefinitions.Add(new ColumnDefinition());
        var frame = new Border { Padding = new Thickness(24), Child = root, ClipToBounds = true };
        frame.SetResourceReference(Border.BackgroundProperty, "CanvasBrush"); window.Content = frame;
        var rail = new DockPanel();
        var branding = new StackPanel { Margin = new Thickness(0, 0, 0, 32) };
        branding.Children.Add(ImportMockText("FamilyQuant", "PageTitle"));
        branding.Children.Add(ImportMockText("Import family list", "CaptionText"));
        DockPanel.SetDock(branding, Dock.Top); rail.Children.Add(branding);
        var marker = ImportMockText("Design preview | Fictitious data", "CaptionText");
        DockPanel.SetDock(marker, Dock.Bottom); rail.Children.Add(marker);
        var steps = new StackPanel();
        for (int i = 0; i < ImportMockSteps.Length; i++)
        {
            var step = new Border { Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 8), CornerRadius = new CornerRadius(4) };
            step.SetResourceReference(Border.BackgroundProperty, i == page ? "SelectionBrush" : "CanvasBrush");
            var line = new StackPanel();
            line.Children.Add(ImportMockText((i + 1) + "   " + ImportMockSteps[i], i == page ? "SectionTitle" : "BodyText"));
            if (i < page) line.Children.Add(ImportMockText("     Done", "CaptionText"));
            step.Child = line; steps.Children.Add(step);
        }
        rail.Children.Add(steps); root.Children.Add(rail);
        var main = new Grid(); Grid.SetColumn(main, 2); root.Children.Add(main);
        main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        main.RowDefinitions.Add(new RowDefinition());
        main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var head = new StackPanel { Margin = new Thickness(0, 0, 0, 22) };
        head.Children.Add(ImportMockText("Step " + (page + 1) + " of 7", "CaptionText"));
        var heading = ImportMockText(variant == "decisions" ? "Review spreadsheet decisions" : ImportMockTitles[page], "PageTitle"); heading.Name = "mockHeading"; window.RegisterName(heading.Name, heading);
        head.Children.Add(heading);
        head.Children.Add(ImportMockText(variant == "decisions" ? "Check each proposed action and its source rows, then confirm the mapping." : ImportMockExplanations[page], "CaptionText")); main.Children.Add(head);
        var body = ImportMockBody(page, variant); Grid.SetRow(body, 1); main.Children.Add(body);
        var footer = new Grid { Name = "mockFooter", Margin = new Thickness(0, 20, 0, 0) }; window.RegisterName(footer.Name, footer);
        footer.ColumnDefinitions.Add(new ColumnDefinition()); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(ImportMockText(variant == "decisions" ? "13 values need confirmation" : page == 6 ? "Imported | Project not saved yet" : page == 3 ? "2 rows need a choice"
            : page == 4 ? "1 family needs a choice" : page == 5 ? "Ready to import" : "Draft only", "CaptionText"));
        var help = ImportMockButton("Help"); help.HorizontalAlignment = HorizontalAlignment.Left; left.Children.Add(help); footer.Children.Add(left);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        actions.Children.Add(ImportMockButton(page == 6 ? "Close" : "Cancel"));
        var back = ImportMockButton("Back"); back.IsEnabled = page != 0; actions.Children.Add(back);
        var next = ImportMockButton(page == 5 ? "Import" : page == 6 ? "Open family checklist" : "Next", true);
        next.Name = "mockForward"; window.RegisterName(next.Name, next); next.IsEnabled = page != 3 && page != 4 && variant != "decisions"; actions.Children.Add(next);
        Grid.SetColumn(actions, 1); footer.Children.Add(actions); Grid.SetRow(footer, 2); main.Children.Add(footer);
        return window;
    }

    private static FrameworkElement ImportMockBody(int page, string variant)
    {
        if (variant == "decisions") return ImportMockDecisionBody();
        if (variant == "wide-sheet") return ImportMockWideSheetBody();
        if (page == 0)
        {
            var content = new StackPanel();
            content.Children.Add(ImportMockCard("You need", "Excel workbook (.xlsx)", "Abbreviation column", "Family name column"));
            content.Children.Add(ImportMockCard("Kept with your list", "Other-language names", "Notes and original rows"));
            content.Children.Add(ImportMockCard("Imported into", "This project's family panel", "Next, after import: Review protein choices in Panel checklist"));
            var actions = new WrapPanel(); actions.Children.Add(ImportMockButton("Try with the example"));
            actions.Children.Add(ImportMockButton("Save a template")); actions.Children.Add(ImportMockButton("Edit current panel")); content.Children.Add(actions);
            return content;
        }
        if (page == 1)
        {
            var grid = ImportMockRows();
            var controls = new StackPanel();
            var file = new DockPanel(); var browse = ImportMockButton("Browse"); DockPanel.SetDock(browse, Dock.Right); file.Children.Add(browse);
            file.Children.Add(ImportMockField("Workbook", "Example family list.xlsx")); controls.Children.Add(file);
            var options = new UniformGridForMock(2);
            options.Children.Add(ImportMockPicker("Sheet", "Consensus"));
            var header = new StackPanel(); header.Children.Add(ImportMockField("Header row", "3 (found from headers)"));
            var changeHeader = ImportMockButton("Use another header row"); changeHeader.HorizontalAlignment = HorizontalAlignment.Left; header.Children.Add(changeHeader);
            options.Children.Add(header); controls.Children.Add(options);
            var strip = new DockPanel(); strip.Children.Add(ImportMockText("Spreadsheet preview", "SectionTitle"));
            var colours = ImportMockText("Source colours", "CaptionText"); colours.HorizontalAlignment = HorizontalAlignment.Right; strip.Children.Add(colours); controls.Children.Add(strip);
            grid.Children.Add(controls);
            var table = ImportMockTable(new[] { "Row", "A: Sigla", "B: Nome (portugues)", "C: Nome (ingles)", "D: Notes" }, new[] {
                new[] { "1", "Example family list (fictitious)", "", "", "" }, new[] { "2", "Filter: all rows", "", "", "" },
                new[] { "3", "Sigla", "Nome (portugues)", "Nome (ingles)", "Notes" },
                new[] { "4", "AB", "Familia alfa", "Alpha family", "Source note" }, new[] { "5", "BC", "Familia beta", "Beta family", "" },
                new[] { "6", "CD", "Familia gama", "Gamma family", "" },
                new[] { "7", "DE", "Familia delta", "Delta family", "" }, new[] { "8", "EF", "Familia epsilon", "Epsilon family", "" },
                new[] { "9", "FG", "Familia zeta", "Zeta family", "" }, new[] { "10", "GH", "Familia eta", "Eta family", "" },
                new[] { "11", "HI", "Familia theta", "Theta family", "" }, new[] { "12", "IJ", "Familia iota", "Iota family", "" },
                new[] { "13", "JK", "Familia kappa", "Kappa family", "" }, new[] { "14", "KL", "Familia lambda", "Lambda family", "" },
                new[] { "15", "AB", "Outra familia alfa", "Another alpha family", "Repeated abbreviation" },
                new[] { "16", "OTHER", "Familia nu; Familia xi", "Nu family; Xi family", "Two names in one row" }, new[] { "17", "", "", "", "" } });
            ImportMockSourceFills(table);
            Grid.SetRow(table, 1); grid.Children.Add(table); return grid;
        }
        if (page == 2)
        {
            var content = new StackPanel();
            var roles = new UniformGridForMock(2);
            roles.Children.Add(ImportMockMapping("Abbreviation", "Sigla", "AB"));
            roles.Children.Add(ImportMockMapping("Family name", "Nome (ingles)", "Alpha family"));
            roles.Children.Add(ImportMockMapping("Other-language name (optional)", "Nome (portugues)", "Familia alfa"));
            roles.Children.Add(ImportMockMapping("Notes (optional)", "Notes", "Source note"));
            roles.Children.Add(ImportMockMapping("Pfam codes (optional)", "Not used", "Not used"));
            roles.Children.Add(ImportMockMapping("Decision (optional)", "Not used", "Quantify every family"));
            content.Children.Add(roles); content.Children.Add(ImportMockCard("Decision", "Quantify every family")); return content;
        }
        if (page == 3)
        {
            var columns = ImportMockColumns();
            columns.Children.Add(ImportMockTable(new[] { "Row", "Abbreviation", "What needs review", "Status" }, new[] {
                new[] { "16", "OTHER", "Several families", "Needs a choice" }, new[] { "15", "AB", "Repeated abbreviation (row 4)", "Needs a choice" },
                new[] { "17", "", "Empty row", "Skipped" } }));
            var detail = new StackPanel { Margin = new Thickness(20, 0, 0, 0) };
            if (variant == "duplicate")
            {
                detail.Children.Add(ImportMockText("Rows 4 and 15 use AB.", "SectionTitle"));
                detail.Children.Add(ImportMockCard("Original rows", "Row 4: AB | Alpha family", "Row 15: AB | Another alpha family"));
                detail.Children.Add(new RadioButton { Content = "Rename row 15", Margin = new Thickness(4, 8, 4, 8), GroupName = "duplicate" });
                detail.Children.Add(ImportMockField("New abbreviation for row 15", "AB-2"));
                detail.Children.Add(new RadioButton { Content = "Keep row 4 and skip row 15", Margin = new Thickness(4, 8, 4, 12), GroupName = "duplicate" });
                detail.Children.Add(ImportMockButton("Use this choice")); Grid.SetColumn(detail, 1); columns.Children.Add(detail); return columns;
            }
            if (variant == "empty")
            {
                detail.Children.Add(ImportMockText("Row 17 is empty.", "SectionTitle"));
                detail.Children.Add(new CheckBox { Content = "Skip empty row", IsChecked = true, Margin = new Thickness(4, 12, 4, 12) });
                detail.Children.Add(ImportMockButton("Show source row"));
                Grid.SetColumn(detail, 1); columns.Children.Add(detail); return columns;
            }
            detail.Children.Add(ImportMockText("Row 16 lists several families.", "SectionTitle"));
            detail.Children.Add(ImportMockCard("Original row", "OTHER", "Nu family; Xi family"));
            detail.Children.Add(new RadioButton { Content = "One family per name", Margin = new Thickness(4, 8, 4, 8), GroupName = "special" });
            detail.Children.Add(new RadioButton { Content = "One group reported under others", Margin = new Thickness(4, 0, 4, 12), GroupName = "special" });
            detail.Children.Add(ImportMockTable(new[] { "Abbreviation", "Family name" }, new[] { new[] { "OTHER-1", "Nu family" }, new[] { "OTHER-2", "Xi family" } }));
            detail.Children.Add(ImportMockButton("Use this choice")); Grid.SetColumn(detail, 1); columns.Children.Add(detail); return columns;
        }
        if (page == 4)
        {
            var columns = ImportMockColumns();
            columns.Children.Add(ImportMockTable(new[] { "Abbreviation", "Family name", "Choice" }, new[] {
                new[] { "AB", "Alpha family", "Needs a choice" }, new[] { "BC", "Beta family", "Confirmed" }, new[] { "CD", "Gamma family", "Skipped" },
                new[] { "DE to OTHER-2", "11 more fictitious families", "Confirmed or skipped" } }));
            var right = ImportMockRows(); right.Margin = new Thickness(20, 0, 0, 0);
            var title = new StackPanel(); title.Children.Add(ImportMockText("AB | Alpha family", "SectionTitle"));
            title.Children.Add(ImportMockText("Suggestions from stored evidence", "CaptionText")); right.Children.Add(title);
            var suggestions = ImportMockTable(new[] { "Use", "Stored evidence", "Type", "Proteins", "Reviewed" }, new[] {
                new[] { "No", "Invented alpha protein family", "UniProt family", "3", "2" }, new[] { "No", "Invented alpha function", "Keyword", "2", "1" } });
            var checkbox = new FrameworkElementFactory(typeof(CheckBox)); checkbox.SetValue(CheckBox.IsCheckedProperty, false);
            checkbox.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            checkbox.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, "Use this stored-evidence match");
            suggestions.Columns[0] = new DataGridTemplateColumn { Header = "Use", Width = 54, MinWidth = 54,
                CellTemplate = new DataTemplate { VisualTree = checkbox } };
            Grid.SetRow(suggestions, 1); right.Children.Add(suggestions);
            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            var confirmMatches = ImportMockButton("Use selected matches"); confirmMatches.IsEnabled = false;
            actions.Children.Add(confirmMatches); actions.Children.Add(ImportMockButton("Skip this family"));
            Grid.SetRow(actions, 2); right.Children.Add(actions); Grid.SetColumn(right, 1); columns.Children.Add(right); return columns;
        }
        if (page == 5)
        {
            var grid = ImportMockRows();
            var facts = new StackPanel(); facts.Children.Add(ImportMockField("Panel name", "Example family list"));
            facts.Children.Add(ImportMockCard("Families to create: 14", "Rows split: 1 | Abbreviations renamed: 1 | Rows skipped: 1",
                "Evidence matches: 10 confirmed, 4 skipped", "Stored source: Example family list.xlsx | Consensus | Header row 3", "Current panel: None"));
            grid.Children.Add(facts);
            var table = ImportMockTable(new[] { "Abbreviation", "Family name", "Other-language name", "Decision", "Matches", "Notes" }, new[] {
                new[] { "AB", "Alpha family", "Familia alfa", "Quantify", "Confirmed", "Source note" }, new[] { "BC", "Beta family", "Familia beta", "Quantify", "Confirmed", "" },
                new[] { "CD", "Gamma family", "Familia gama", "Quantify", "Skipped", "" },
                new[] { "DE", "Delta family", "Familia delta", "Quantify", "Confirmed", "" }, new[] { "EF", "Epsilon family", "Familia epsilon", "Quantify", "Confirmed", "" },
                new[] { "FG", "Zeta family", "Familia zeta", "Quantify", "Confirmed", "" }, new[] { "GH", "Eta family", "Familia eta", "Quantify", "Confirmed", "" },
                new[] { "HI", "Theta family", "Familia theta", "Quantify", "Confirmed", "" }, new[] { "IJ", "Iota family", "Familia iota", "Quantify", "Confirmed", "" },
                new[] { "JK", "Kappa family", "Familia kappa", "Quantify", "Confirmed", "" }, new[] { "KL", "Lambda family", "Familia lambda", "Quantify", "Skipped", "" },
                new[] { "AB-2", "Another alpha family", "Outra familia alfa", "Quantify", "Confirmed", "Row 15 renamed" },
                new[] { "OTHER-1", "Nu family", "Familia nu", "Quantify", "Skipped", "From row 16" }, new[] { "OTHER-2", "Xi family", "Familia xi", "Quantify", "Skipped", "From row 16" } });
            Grid.SetRow(table, 1); grid.Children.Add(table); return grid;
        }
        var done = new StackPanel();
        done.Children.Add(ImportMockCard("14 families imported", "Panel: Example family list", "Workbook unchanged", "Protein grouping unchanged", "Project not saved yet"));
        done.Children.Add(ImportMockButton("Undo import")); return done;
    }

    private static FrameworkElement ImportMockDecisionBody()
    {
        var grid = ImportMockRows();
        grid.Children.Add(ImportMockText("Decision column: DECISAO FamilyQuant | 13 values need confirmation", "SectionTitle"));
        var rows = new[] {
            new[] { "Núcleo", "2 to 10", "Quantify", "" }, new[] { "Padrão", "11 to 17", "Quantify", "" },
            new[] { "Opcional", "18 to 29", "Quantify", "" }, new[] { "Excluir", "30, 31", "Not a family", "" },
            new[] { "Excluir (ou revisão)", "32 to 34", "Not a family", "" },
            new[] { "Núcleo (grupo A) / Condicional (grupo B)", "35", "Quantify", "" },
            new[] { "Núcleo (como parte de AB)", "36", "Quantify", "" },
            new[] { "Condicional (grupo A)", "37", "Quantify", "" }, new[] { "Condicional (grupo B)", "38", "Quantify", "" },
            new[] { "Condicional (grupo C)", "39", "Quantify", "" }, new[] { "Condicional", "40", "Quantify", "" },
            new[] { "Opcional (lista de revisão)", "41", "Quantify", "" }, new[] { "Fundir em AB", "42", "Merge into family", "AB" } };
        var table = ImportMockTable(new[] { "Source value", "Source rows", "Use as (proposed)", "Merge into abbreviation" }, rows);
        table.IsReadOnly = false;
        table.Columns[0].IsReadOnly = table.Columns[1].IsReadOnly = true;
        table.Columns[2] = new DataGridComboBoxColumn { Header = "Use as (proposed)", Width = new DataGridLength(1.4, DataGridLengthUnitType.Star),
            SelectedItemBinding = new Binding("[2]"), ItemsSource = new[] { "Quantify", "Report under others", "Not a family", "Merge into family" } };
        Grid.SetRow(table, 1); grid.Children.Add(table);
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        actions.Children.Add(ImportMockButton("Back to columns")); actions.Children.Add(ImportMockButton("Confirm these actions"));
        Grid.SetRow(actions, 2); grid.Children.Add(actions); return grid;
    }

    private static FrameworkElement ImportMockWideSheetBody()
    {
        var grid = ImportMockRows();
        var controls = new StackPanel(); controls.Children.Add(ImportMockField("Workbook", "Detailed fictitious family list.xlsx"));
        var row = new UniformGridForMock(3);
        var sheet = new StackPanel { Margin = new Thickness(0, 0, 12, 10) }; sheet.Children.Add(ImportMockText("Sheet", "SectionTitle"));
        sheet.Children.Add(new ComboBox { ItemsSource = new[] { "1. Lista de famílias", "2. Subclasses de exemplo", "3. Decisões resolvidas",
            "4. Regras de quantificação", "5. Referências", "6. Legenda" }, SelectedIndex = 0, MinHeight = 34 });
        row.Children.Add(sheet); row.Children.Add(ImportMockField("Header row", "1 (found from headers)"));
        row.Children.Add(ImportMockPicker("Other sheets", "Ignore the 5 other sheets")); controls.Children.Add(row);
        controls.Children.Add(ImportMockText("Spreadsheet preview | 41 fictitious rows | 15 columns | Source colours", "SectionTitle")); grid.Children.Add(controls);
        var decisions = Enumerable.Repeat("Núcleo", 9).Concat(Enumerable.Repeat("Padrão", 7)).Concat(Enumerable.Repeat("Opcional", 12))
            .Concat(Enumerable.Repeat("Excluir", 2)).Concat(Enumerable.Repeat("Excluir (ou revisão)", 3))
            .Concat(new[] { "Núcleo (grupo A) / Condicional (grupo B)", "Núcleo (como parte de AB)", "Condicional (grupo A)",
                "Condicional (grupo B)", "Condicional (grupo C)", "Condicional", "Opcional (lista de revisão)", "Fundir em AB" }).ToArray();
        var rows = Enumerable.Range(2, 41).Select(i => new[] { i.ToString(), i == 42 ? "OTHER" : i == 2 ? "AB" : "F" + (i - 1).ToString("D2"),
            "Família de exemplo " + (i - 1), i == 42 ? "Nu family; Xi family" : "Example family " + (i - 1), "Nível A", "Nível B",
            decisions[i - 2], "Sim", "Example name",
            "Source text", "Source text", "Recorded source text", "Example binding (PF12345); Example domain (PF23456)", "Source subclass notes",
            "Fictitious source", "A long fictitious note retained in full, with accents and original line breaks. The complete text is available in cell details." });
        var table = ImportMockTable(new[] { "Row", "Sigla", "Nome (português)", "Nome (inglês)", "Nível A", "Nível B", "DECISAO FamilyQuant", "Na lista inicial?",
            "Nome de conferência", "Ocorrência A", "Ocorrência B", "Abundância anotada", "Domínio(s) Pfam", "Subclasses", "Fontes", "Notas e decisões" }, rows);
        foreach (var column in table.Columns) column.Width = column.Header.ToString() == "Row" ? 54 : 170;
        ImportMockSourceFills(table);
        Grid.SetRow(table, 1); grid.Children.Add(table); return grid;
    }

    private static void ImportMockSourceFills(DataGrid table)
    {
        var fillStyle = new Style(typeof(Border));
        foreach (var row in new[] { (Row: "4", Token: "SelectionBrush"), (Row: "5", Token: "HoverBrush"), (Row: "6", Token: "SelectionBrush") })
        {
            var trigger = new DataTrigger { Binding = new Binding("[0]"), Value = row.Row };
            trigger.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension(row.Token))); fillStyle.Triggers.Add(trigger);
        }
        var fill = new FrameworkElementFactory(typeof(Border)); fill.SetValue(FrameworkElement.StyleProperty, fillStyle);
        var text = new FrameworkElementFactory(typeof(TextBlock)); text.SetBinding(TextBlock.TextProperty, new Binding("[1]"));
        text.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap); text.SetValue(FrameworkElement.MarginProperty, new Thickness(4, 2, 4, 2));
        fill.AppendChild(text); ((DataGridTemplateColumn)table.Columns[1]).CellTemplate = new DataTemplate { VisualTree = fill };
    }

    private static TextBlock ImportMockText(string text, string role = "BodyText")
    {
        var result = new TextBlock { Text = text, Margin = new Thickness(0, 2, 0, 4) };
        result.SetResourceReference(FrameworkElement.StyleProperty, role); return result;
    }
    private static Button ImportMockButton(string text, bool primary = false)
    {
        var button = new Button { Content = text, MinHeight = 36, MinWidth = 86 };
        if (primary) button.SetResourceReference(FrameworkElement.StyleProperty, "PrimaryButton");
        return button;
    }
    private static Border ImportMockCard(string heading, params string[] lines)
    {
        var content = new StackPanel(); content.Children.Add(ImportMockText(heading, "SectionTitle"));
        foreach (string line in lines) content.Children.Add(ImportMockText(line));
        var border = new Border { Child = content, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1) };
        border.SetResourceReference(Border.PaddingProperty, "PanelPadding"); border.SetResourceReference(FrameworkElement.MarginProperty, "PanelMargin");
        border.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush"); border.SetResourceReference(Border.BorderBrushProperty, "EdgeBrush"); return border;
    }
    private static FrameworkElement ImportMockField(string label, string text)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 12, 10) }; stack.Children.Add(ImportMockText(label, "SectionTitle"));
        stack.Children.Add(new TextBox { Text = text, IsReadOnly = true, MinHeight = 34 }); return stack;
    }
    private static FrameworkElement ImportMockPicker(string label, string value)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 12, 10) }; stack.Children.Add(ImportMockText(label, "SectionTitle"));
        stack.Children.Add(new ComboBox { ItemsSource = new[] { value }, SelectedIndex = 0, MinHeight = 34 }); return stack;
    }
    private static FrameworkElement ImportMockMapping(string label, string value, string example)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 12, 12) }; stack.Children.Add(ImportMockPicker(label, value));
        stack.Children.Add(ImportMockText("Example: " + example, "CaptionText")); return stack;
    }
    private static DataGrid ImportMockTable(string[] headers, IEnumerable<string[]> rows)
    {
        var grid = new DataGrid { AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false,
            IsReadOnly = true, HeadersVisibility = DataGridHeadersVisibility.Column, MinHeight = 140, Margin = new Thickness(0, 6, 0, 0) };
        for (int i = 0; i < headers.Length; i++)
        {
            var value = new FrameworkElementFactory(typeof(TextBlock)); value.SetBinding(TextBlock.TextProperty, new Binding("[" + i + "]"));
            value.SetBinding(FrameworkElement.ToolTipProperty, new Binding("[" + i + "]"));
            value.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap); value.SetValue(FrameworkElement.MarginProperty, new Thickness(4, 2, 4, 2));
            grid.Columns.Add(new DataGridTemplateColumn { Header = headers[i], CellTemplate = new DataTemplate { VisualTree = value },
                Width = new DataGridLength(i == 0 ? 0.65 : 1.4, DataGridLengthUnitType.Star), MinWidth = i == 0 ? 50 : 90 });
        }
        grid.ItemsSource = rows.ToArray(); return grid;
    }
    private static Grid ImportMockRows()
    {
        var grid = new Grid(); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition()); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); return grid;
    }
    private static Grid ImportMockColumns()
    {
        var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition()); return grid;
    }
    private sealed class UniformGridForMock : System.Windows.Controls.Primitives.UniformGrid
    {
        public UniformGridForMock(int columns) { Columns = columns; }
    }
}
