using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FamilyQuant;

/// <summary>The import chapter, one part at a time, with the picture of its wizard page.</summary>
public sealed class FamilyListImportGuideViewModel : ScreenViewModel
{
    private FamilyListImportGuideSection current;
    private readonly Dictionary<string, ImageSource?> pictures = new(StringComparer.Ordinal);

    public IReadOnlyList<FamilyListImportGuideSection> Sections { get; } = FamilyListImportGuide.Sections;

    public FamilyListImportGuideSection Current
    {
        get => current;
        set
        {
            if (value == null || ReferenceEquals(current, value)) return;
            current = value;
            Changed(); Changed(nameof(Picture)); Changed(nameof(HasPicture)); Changed(nameof(Position)); Changed(nameof(CanNext)); Changed(nameof(CanPrevious));
        }
    }

    public int Index => Sections.ToList().IndexOf(current);
    public bool CanNext => Index < Sections.Count - 1;
    public bool CanPrevious => Index > 0;
    public string Position => "Part " + (Index + 1).ToString(CultureInfo.CurrentCulture) + " of " + Sections.Count.ToString(CultureInfo.CurrentCulture);
    public bool HasPicture => Picture != null;

    /// <summary>The page picture, read once from the program's resources; null for a part without one.</summary>
    public ImageSource? Picture
    {
        get
        {
            if (current.Image.Length == 0) return null;
            if (pictures.TryGetValue(current.Key, out ImageSource? cached)) return cached;
            ImageSource? image = null;
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = FamilyListImportGuide.ImageUri(current);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();
                image = bitmap;
            }
            catch (Exception ex) when (ex is System.IO.IOException or NotSupportedException or UriFormatException) { image = null; }
            pictures[current.Key] = image;
            return image;
        }
    }

    public FamilyListImportGuideViewModel(string? key = null)
    {
        current = Sections.FirstOrDefault(s => s.Key == key) ?? Sections[0];
    }

    public void Next() { if (CanNext) Current = Sections[Index + 1]; }
    public void Previous() { if (CanPrevious) Current = Sections[Index - 1]; }
}

public partial class FamilyListImportGuideWindow : Window
{
    public FamilyListImportGuideViewModel Model { get; }

    public FamilyListImportGuideWindow(string? section = null)
    {
        Model = new FamilyListImportGuideViewModel(section);
        InitializeComponent();
        DataContext = Model;
        Rect area = SystemParameters.WorkArea;
        MaxWidth = area.Width; MaxHeight = area.Height;
        Width = Math.Min(Width, area.Width); Height = Math.Min(Height, area.Height);
        Model.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(FamilyListImportGuideViewModel.Current)) pageScroll.ScrollToTop(); };
    }

    /// <summary>Opens the chapter beside <paramref name="owner"/>, at <paramref name="section"/> when given
    /// (a key such as "matches"), without blocking the window that asked for it.</summary>
    public static FamilyListImportGuideWindow Show(Window? owner, string? section = null)
    {
        var window = new FamilyListImportGuideWindow(section);
        if (owner != null && owner.IsLoaded) window.Owner = owner;
        window.Show();
        return window;
    }

    private void Next_Click(object sender, RoutedEventArgs e) => Model.Next();
    private void Previous_Click(object sender, RoutedEventArgs e) => Model.Previous();
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
