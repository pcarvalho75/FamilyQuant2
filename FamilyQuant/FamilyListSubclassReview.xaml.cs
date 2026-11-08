using FamilyQuant.Panels.Import;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;

namespace FamilyQuant;

public partial class FamilyListSubclassReview : UserControl
{
    public static readonly DependencyProperty SubclassChoicesProperty = DependencyProperty.Register(nameof(SubclassChoices),
        typeof(IEnumerable<ImportSubclassChoice>), typeof(FamilyListSubclassReview), new PropertyMetadata(null, ChoicesChanged));
    public static readonly DependencyProperty SelectedSubclassProperty = DependencyProperty.Register(nameof(SelectedSubclass),
        typeof(ImportSubclassChoice), typeof(FamilyListSubclassReview), new FrameworkPropertyMetadata(null,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, e) => ((FamilyListSubclassReview)d).SetValue(HasSelectionKey, e.NewValue != null)));
    public static readonly DependencyProperty FamilyKeysProperty = DependencyProperty.Register(nameof(FamilyKeys),
        typeof(IReadOnlyCollection<string>), typeof(FamilyListSubclassReview), new PropertyMetadata(null,
            (d, e) => ((FamilyListSubclassReview)d).SetValue(HasFamilyKeysKey, e.NewValue is IReadOnlyCollection<string> { Count: > 0 })));
    public static readonly DependencyProperty ShowIntroductionProperty = DependencyProperty.Register(nameof(ShowIntroduction),
        typeof(bool), typeof(FamilyListSubclassReview), new PropertyMetadata(true));
    private static readonly DependencyPropertyKey ViewKey = DependencyProperty.RegisterReadOnly(nameof(View), typeof(ICollectionView),
        typeof(FamilyListSubclassReview), new PropertyMetadata(null));
    private static readonly DependencyPropertyKey HasSelectionKey = DependencyProperty.RegisterReadOnly(nameof(HasSelection), typeof(bool),
        typeof(FamilyListSubclassReview), new PropertyMetadata(false));
    private static readonly DependencyPropertyKey HasFamilyKeysKey = DependencyProperty.RegisterReadOnly(nameof(HasFamilyKeys), typeof(bool),
        typeof(FamilyListSubclassReview), new PropertyMetadata(false));
    private static readonly DependencyPropertyKey DraftHintKey = DependencyProperty.RegisterReadOnly(nameof(DraftHint), typeof(string),
        typeof(FamilyListSubclassReview), new PropertyMetadata("No choices have been confirmed."));
    private static readonly DependencyPropertyKey ValidationStatusKey = DependencyProperty.RegisterReadOnly(nameof(ValidationStatus), typeof(string),
        typeof(FamilyListSubclassReview), new PropertyMetadata("Check the draft before Accept and import; the project is unchanged."));
    private static readonly DependencyPropertyKey UnmatchedProblemTextKey = DependencyProperty.RegisterReadOnly(nameof(UnmatchedProblemText), typeof(string),
        typeof(FamilyListSubclassReview), new PropertyMetadata(""));
    public static readonly DependencyProperty ViewProperty = ViewKey.DependencyProperty;
    public static readonly DependencyProperty HasSelectionProperty = HasSelectionKey.DependencyProperty;
    public static readonly DependencyProperty HasFamilyKeysProperty = HasFamilyKeysKey.DependencyProperty;
    public static readonly DependencyProperty DraftHintProperty = DraftHintKey.DependencyProperty;
    public static readonly DependencyProperty ValidationStatusProperty = ValidationStatusKey.DependencyProperty;
    public static readonly DependencyProperty UnmatchedProblemTextProperty = UnmatchedProblemTextKey.DependencyProperty;
    public IEnumerable<ImportSubclassChoice>? SubclassChoices { get => (IEnumerable<ImportSubclassChoice>?)GetValue(SubclassChoicesProperty); set => SetValue(SubclassChoicesProperty, value); }
    public ImportSubclassChoice? SelectedSubclass { get => (ImportSubclassChoice?)GetValue(SelectedSubclassProperty); set => SetValue(SelectedSubclassProperty, value); }
    public IReadOnlyCollection<string>? FamilyKeys { get => (IReadOnlyCollection<string>?)GetValue(FamilyKeysProperty); set => SetValue(FamilyKeysProperty, value); }
    public bool ShowIntroduction { get => (bool)GetValue(ShowIntroductionProperty); set => SetValue(ShowIntroductionProperty, value); }
    public ICollectionView? View => (ICollectionView?)GetValue(ViewKey.DependencyProperty);
    public bool HasSelection => (bool)GetValue(HasSelectionKey.DependencyProperty);
    public bool HasFamilyKeys => (bool)GetValue(HasFamilyKeysKey.DependencyProperty);
    public string DraftHint => (string)GetValue(DraftHintKey.DependencyProperty);
    public string ValidationStatus => (string)GetValue(ValidationStatusKey.DependencyProperty);
    public string UnmatchedProblemText => (string)GetValue(UnmatchedProblemTextKey.DependencyProperty);
    private List<ImportSubclassChoice> rows = new();
    private bool showingProblems;

    public FamilyListSubclassReview()
    {
        InitializeComponent();
        // Preserve the host's existing context without replacing its DataContext.
        SetBinding(SubclassChoicesProperty, new Binding(nameof(SubclassChoices)));
        SetBinding(SelectedSubclassProperty, new Binding(nameof(SelectedSubclass)) { Mode = BindingMode.TwoWay });
        Loaded += (_, _) => EnsureSelection();
    }
    private static void ChoicesChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var control = (FamilyListSubclassReview)sender;
        if (args.OldValue is INotifyCollectionChanged old) CollectionChangedEventManager.RemoveHandler(old, control.CollectionChanged);
        if (args.NewValue is INotifyCollectionChanged next) CollectionChangedEventManager.AddHandler(next, control.CollectionChanged);
        control.RefreshRows();
    }
    private void CollectionChanged(object? sender, NotifyCollectionChangedEventArgs args) => RefreshRows();
    private void RefreshRows()
    {
        foreach (var row in rows) PropertyChangedEventManager.RemoveHandler(row, RowChanged, "");
        rows = SubclassChoices?.ToList() ?? new();
        foreach (var row in rows) PropertyChangedEventManager.AddHandler(row, RowChanged, "");
        var view = new ListCollectionView(rows);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ImportSubclassChoice.SourceFamily)));
        SetValue(ViewKey, view);
        if (IsLoaded) EnsureSelection();
        SetValue(DraftHintKey, rows.Any(row => row.HasDraftSelection)
            ? "Initial ticks come from the current summary draft. Review them here; only Accept and import confirms the choices."
            : "Nothing is selected automatically. Include only the subclasses and rules you want; the project stays unchanged until import.");
    }
    private void EnsureSelection()
    {
        // Wait until host bindings have transferred their initial selection before supplying a default.
        if (SelectedSubclass != null && rows.Contains(SelectedSubclass)) return;
        SetCurrentValue(SelectedSubclassProperty, rows.FirstOrDefault());
        GetBindingExpression(SelectedSubclassProperty)?.UpdateSource();
    }
    private void DraftField_Loaded(object sender, RoutedEventArgs args)
    {
        if (sender is not TextBox field) return;
        field.ApplyTemplate();
        foreach (var clear in Descendants(field).OfType<Button>().Where(button => button.Name == "DeleteButton"))
        {
            ToolTipService.SetShowOnDisabled(clear, true);
            if (field.IsReadOnly)
            {
                clear.ToolTip = "Source text is read only here; use the separate draft fields to edit your choices. This source value is never cleared or changed.";
                clear.IsEnabled = false; clear.Visibility = Visibility.Collapsed;
            }
            else clear.ToolTip = "Clears this draft field without changing the workbook or project; an empty field keeps its proposed value. Available while the draft can be edited, before import and outside a running operation.";
        }
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        if (root is not Visual) yield break;
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, index))) yield return child;
    }
    private void RowChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (!showingProblems && args.PropertyName is not (nameof(ImportSubclassChoice.ProblemText) or nameof(ImportSubclassChoice.HasProblems)))
            SetValue(ValidationStatusKey, "Draft choices changed. Check choices again to update the item messages.");
    }
    public IReadOnlyList<PanelSubclassAcceptance> CaptureAcceptances() => ImportSubclassChoice.Capture(rows);
    public PanelSubclassBuildResult CheckChoices()
    {
        var missing = rows.Where(row => row.SourceProposal is not PanelSubclassProposal).ToList();
        PanelSubclassBuildResult result;
        if (missing.Count > 0)
            result = new() { Problems = missing.Select(row => new PanelSubclassImportProblem
                { ProposalId = row.ProposalId, Message = "Source information is unavailable; refresh the subclass proposals." }).ToList() };
        else result = PanelSubclassImport.BuildDefinitions(rows.Select(row => (PanelSubclassProposal)row.SourceProposal!).ToArray(),
            CaptureAcceptances(), FamilyKeys?.ToArray() ?? Array.Empty<string>());
        ShowProblems(result.Problems);
        SetValue(ValidationStatusKey, result.CanBuild ? "Choices checked. Nothing is imported until Accept and import."
            : "Review the messages on the affected subclass or rule, check the draft again, then Accept and import.");
        return result;
    }
    public void ShowProblems(IEnumerable<PanelSubclassImportProblem> problems)
    {
        ArgumentNullException.ThrowIfNull(problems);
        showingProblems = true;
        try
        {
            foreach (var row in rows) { row.Problems.Clear(); foreach (var rule in row.Rules) rule.Problems.Clear(); }
            var unmatched = new List<string>();
            foreach (var problem in problems)
            {
                var affected = rows.Where(row => row.ProposalId == problem.ProposalId).ToList();
                if (affected.Count == 0) { unmatched.Add(problem.ProposalId + (problem.RuleId.Length > 0 ? " / " + problem.RuleId : "") + ": " + problem.Message); continue; }
                foreach (var row in affected)
                {
                    var rules = row.Rules.Where(rule => rule.RuleId == problem.RuleId).ToList();
                    if (problem.RuleId.Length > 0 && rules.Count > 0) foreach (var rule in rules) rule.Problems.Add(problem.Message);
                    else row.Problems.Add((problem.RuleId.Length > 0 ? problem.RuleId + ": " : "") + problem.Message);
                }
            }
            SetValue(UnmatchedProblemTextKey, string.Join("\n", unmatched));
            var first = rows.FirstOrDefault(row => row.HasProblems);
            if (IsLoaded && first != null)
            {
                SetCurrentValue(SelectedSubclassProperty, first);
                GetBindingExpression(SelectedSubclassProperty)?.UpdateSource();
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
                {
                    var message = Descendants(this).OfType<FrameworkElement>().FirstOrDefault(element =>
                        element.Name == "ProposalProblem" && element.DataContext is ImportSubclassChoice { Problems.Count: > 0 }
                        || element.Name == "RuleProblem" && element.DataContext is ImportSubclassRuleChoice { HasProblems: true });
                    message?.BringIntoView();
                }));
            }
        }
        finally { showingProblems = false; }
    }
    private void Check_Click(object sender, RoutedEventArgs args) => CheckChoices();
    private void RejectSubclass_Click(object sender, RoutedEventArgs args) => (sender as FrameworkElement)?.DataContext.AsChoice()?.Reject();
    private void RejectRule_Click(object sender, RoutedEventArgs args) { if ((sender as FrameworkElement)?.DataContext is ImportSubclassRuleChoice rule) rule.Reject(); }
}

internal static class SubclassReviewContext
{
    internal static ImportSubclassChoice? AsChoice(this object? value) => value as ImportSubclassChoice;
}
