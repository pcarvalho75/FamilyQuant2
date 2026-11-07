using FamilyQuant.Evidence.Alignment;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace FamilyQuant;

/// <summary>Align selected: opens a read-only alignment of the highlighted protein rows beside their
/// reviewed relatives. Nothing in the proposal or its ticks changes.</summary>
public partial class SuperClusterPreviewWindow
{
    private Func<IReadOnlyList<int>, AlignmentCandidateSet>? alignmentSource;
    private bool alignButtonHooked;

    /// <summary>Chooses the sequences to offer for the highlighted protein indices, first highlighted first.
    /// Without it the Align selected button stays disabled.</summary>
    public Func<IReadOnlyList<int>, AlignmentCandidateSet>? AlignmentSource
    {
        get => alignmentSource;
        set { alignmentSource = value; UpdateAlignButton(); }
    }

    /// <summary>The last alignment window this review opened, while it is open.</summary>
    public AlignmentWindow? OpenAlignmentWindow { get; private set; }

    private void AlignSelected_Loaded(object sender, RoutedEventArgs e)
    {
        if (alignButtonHooked) return;
        alignButtonHooked = true;
        evidenceGrid.SelectionChanged += (_, _) => UpdateAlignButton();
        UpdateAlignButton();
    }

    private void UpdateAlignButton()
    {
        alignSelectedButton.IsEnabled = alignmentSource != null && evidenceGrid.SelectedItems.Count > 0;
    }

    /// <summary>The highlighted rows in the order they were highlighted.</summary>
    internal IReadOnlyList<int> HighlightedProteinIndices() =>
        evidenceGrid.SelectedItems.OfType<ReviewProteinRow>().Select(r => r.ProteinIndex).Distinct().ToArray();

    private void AlignSelected_Click(object sender, RoutedEventArgs e) => OpenAlignment();

    /// <summary>Opens the alignment window for the highlighted rows; null when nothing can be aligned.</summary>
    internal AlignmentWindow? OpenAlignment()
    {
        var highlighted = HighlightedProteinIndices();
        if (alignmentSource == null || highlighted.Count == 0) return null;
        AlignmentWindow window;
        try { window = new AlignmentWindow(new AlignmentViewModel(alignmentSource(highlighted))) { Owner = this }; }
        catch (ArgumentException ex)
        {
            ShellMessages.Report(this, "Nothing could be aligned: " + ex.Message, "Align selected", MessageBoxButton.OK, MessageBoxImage.Information);
            return null;
        }
        window.Closed += (_, _) => { if (ReferenceEquals(OpenAlignmentWindow, window)) OpenAlignmentWindow = null; };
        OpenAlignmentWindow = window;
        window.Show();
        return window;
    }
}
