using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace FamilyQuant.Themes;

/// <summary>Keeps the native disabled table template on the application's palette.</summary>
public static class DisabledTableAppearance
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(DisabledTableAppearance), new PropertyMetadata(false, Changed));
    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State", typeof(State), typeof(DisabledTableAppearance));
    public static bool GetEnabled(DependencyObject target) => (bool)target.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject target, bool value) => target.SetValue(EnabledProperty, value);

    private static void Changed(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not DataGrid grid) return;
        (grid.GetValue(StateProperty) as State)?.Dispose();
        grid.ClearValue(StateProperty);
        if ((bool)args.NewValue) grid.SetValue(StateProperty, new State(grid));
    }

    private sealed class State : IDisposable
    {
        private readonly DataGrid grid;
        private Border? paintedRoot;
        private bool attached, scopedText;
        private readonly Dictionary<string, (bool HadValue, object? Value)> previousBrushes = new();
        private static readonly (string Key, string Token)[] DisabledBrushes =
        [ ("TextBrush", "MutedBrush"), ("SelectionBrush", "SurfaceBrush"), ("HoverBrush", "SurfaceBrush"),
          ("DataGridRowSelectedBackgroundThemeBrush", "SurfaceBrush"), ("DataGridRowSelectedForegroundThemeBrush", "MutedBrush") ];
        private BindingBase? previousForegroundBinding;
        private object? previousForeground;
        private bool foregroundOverridden;

        public State(DataGrid grid)
        {
            this.grid = grid; grid.Loaded += Loaded; grid.Unloaded += Unloaded;
            if (grid.IsLoaded) Attach();
        }
        private void Loaded(object sender, RoutedEventArgs args) => Attach();
        private void Unloaded(object sender, RoutedEventArgs args) => Detach();
        private void Attach()
        {
            if (attached) return;
            attached = true; grid.IsEnabledChanged += EnabledChanged; ThemeManager.ThemeChanged += ThemeChanged;
            Update();
        }
        private void EnabledChanged(object sender, DependencyPropertyChangedEventArgs args) => Update();
        private void ThemeChanged(object? sender, EventArgs args) => Update();
        private void Update()
        {
            if (grid.IsEnabled) { Restore(); return; }
            grid.ApplyTemplate();
            var root = VisualTreeHelper.GetChildrenCount(grid) > 0 ? VisualTreeHelper.GetChild(grid, 0) as Border : null;
            if (!ReferenceEquals(root, paintedRoot))
            {
                RestoreRoot();
                // A caller's explicit local brush takes precedence; the native defect is a template value.
                if (root != null && root.ReadLocalValue(Border.BackgroundProperty) == DependencyProperty.UnsetValue)
                { paintedRoot = root; root.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush"); }
            }
            if (!scopedText)
            {
                foreach (var entry in DisabledBrushes)
                {
                    bool local = false;
                    foreach (object key in grid.Resources.Keys) if (Equals(key, entry.Key)) { local = true; break; }
                    previousBrushes[entry.Key] = (local, local ? grid.Resources[entry.Key] : null);
                }
                previousForegroundBinding = BindingOperations.GetBindingBase(grid, Control.ForegroundProperty);
                previousForeground = grid.ReadLocalValue(Control.ForegroundProperty);
                foregroundOverridden = previousForegroundBinding != null || previousForeground == DependencyProperty.UnsetValue
                    || previousForeground is Brush || previousForeground == null;
                scopedText = true;
            }
            foreach (var entry in DisabledBrushes) grid.Resources[entry.Key] = grid.FindResource(entry.Token);
            // Existing dynamic TextBrush references follow the scoped resource without being replaced.
            if (foregroundOverridden) grid.SetResourceReference(Control.ForegroundProperty, "MutedBrush");
        }
        private void RestoreRoot()
        {
            paintedRoot?.ClearValue(Border.BackgroundProperty); paintedRoot = null;
        }
        private void Restore()
        {
            RestoreRoot();
            if (!scopedText) return;
            foreach (var entry in previousBrushes)
                if (entry.Value.HadValue) grid.Resources[entry.Key] = entry.Value.Value;
                else grid.Resources.Remove(entry.Key);
            previousBrushes.Clear(); scopedText = false;
            if (foregroundOverridden)
            {
                if (previousForegroundBinding != null) BindingOperations.SetBinding(grid, Control.ForegroundProperty, previousForegroundBinding);
                else if (previousForeground == DependencyProperty.UnsetValue) grid.ClearValue(Control.ForegroundProperty);
                else grid.SetValue(Control.ForegroundProperty, previousForeground);
            }
            foregroundOverridden = false;
            previousForegroundBinding = null; previousForeground = null;
        }
        private void Detach()
        {
            if (!attached) return;
            attached = false; grid.IsEnabledChanged -= EnabledChanged; ThemeManager.ThemeChanged -= ThemeChanged;
            Restore();
        }
        public void Dispose() { Detach(); grid.Loaded -= Loaded; grid.Unloaded -= Unloaded; }
    }
}
