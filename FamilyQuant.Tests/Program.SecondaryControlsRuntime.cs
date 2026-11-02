using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void SecondaryControlsRuntime(Assembly legacy, string output, Themes.AppTheme theme, bool negative, SecondaryAdd add)
    {
        const string source = "FamilyQuant/ComputeXicWindow.xaml";
        var frozen = (Window)Activator.CreateInstance(legacy.GetType("FamilyQuant.ComputeXicWindow", true)!, new object?[] { "native fixture", null, null })!;
        var current = new ComputeXicWindow("native fixture");
        SecondaryHost(frozen); SecondaryHost(current);
        try
        {
            FrameworkElement oldRoot = frozen, newRoot = current.Screen;
            if (negative) ((FrameworkElement)current.Screen.FindName("TextBoxRawDirectory")).Visibility = Visibility.Collapsed;
            FrameworkElement? Find(FrameworkElement root, string name)
            {
                var found = SecondaryNamed(root, name)
                    ?? (root is QuantifyControl quantify ? SecondaryNamed((FrameworkElement)quantify.FindName("FilterControl"), name) : null);
                if (found != null) return found;
                string? path = name switch { "TextBoxPeaksMinimumAlc" => "PeaksMinimumAlc", "TextBoxDeepNovoMinimumScore" => "DeepNovoMinimumScore", _ => null };
                return path == null ? null : WalkTree(root).OfType<TextBox>().SingleOrDefault(box =>
                    BindingOperations.GetBinding(box, TextBox.TextProperty)?.Path?.Path == path);
            }
            bool Visible(FrameworkElement? value) => value?.IsVisible == true && PresentationSource.FromVisual(value) != null;
            foreach (var field in new[] { (37, "TextBoxRawDirectory"), (47, "TextBoxSeproPackage"), (57, "TextBoxDeNovoDirectory"),
                (67, "ComboBoxDeNovoSource"), (81, "TextBoxPeaksMinimumAlc"), (86, "TextBoxDeepNovoMinimumScore"),
                (95, "IntegerUpDownIsotopes"), (101, "IntegerUpDownMinPoints"), (111, "ButtonCompute"), (117, "ButtonViewSpectrumComparison") })
            {
                var old = Find(oldRoot, field.Item2); var actual = Find(newRoot, field.Item2);
                string before = old?.GetType().Name + ":" + Visible(old), after = actual?.GetType().Name + ":" + Visible(actual);
                add(source, field.Item1, field.Item1, "native old/current Quantify control is reachable: " + field.Item2,
                    before, after, old != null && actual != null && old.GetType().Name == actual.GetType().Name && Visible(old) && Visible(actual),
                    "QuantifyControl." + field.Item2, row => row.Kind == "control");
            }
            var oldSource = (ComboBox)Find(oldRoot, "ComboBoxDeNovoSource")!;
            var newSource = (ComboBox)Find(newRoot, "ComboBoxDeNovoSource")!;
            add(source, 68, 68, "native default source selection", oldSource.SelectedIndex, newSource.SelectedIndex,
                oldSource.SelectedIndex == newSource.SelectedIndex, "DeNovoFilterControl.ComboBoxDeNovoSource.SelectedIndex");
            string[] tags = { "peaks-only", "peaks-all", "deepnovo", "union-only-deepnovo", "union-all-deepnovo" };
            for (int index = 0; index < tags.Length; index++)
            {
                var beforeItem = oldSource.Items.OfType<ComboBoxItem>().Single(item => item.Tag as string == tags[index]);
                var currentItem = newSource.Items.OfType<ComboBoxItem>().Single(item => item.Tag as string == tags[index]);
                add(source, 71 + index, 71 + index, "native source item remains selectable: " + tags[index],
                    beforeItem.GetType().Name, currentItem.GetType().Name, Visible(oldSource) && Visible(newSource), "DeNovoFilterControl source " + tags[index], row => row.Kind == "control");
                add(source, 71 + index, 71 + index, "native old/current source-item copy: " + tags[index],
                    beforeItem.Content, currentItem.Content, Equals(beforeItem.Content, currentItem.Content), "DeNovoFilterControl source " + tags[index], row => row.Kind == "option/copy");
            }
            var valuesBefore = new SortedDictionary<string, string>(StringComparer.Ordinal);
            var valuesAfter = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (string tag in tags)
            {
                oldSource.SelectedItem = oldSource.Items.OfType<ComboBoxItem>().Single(item => item.Tag as string == tag);
                newSource.SelectedItem = newSource.Items.OfType<ComboBoxItem>().Single(item => item.Tag as string == tag);
                PumpDispatcher();
                foreach (var pair in new[] { ("before", oldRoot, valuesBefore), ("after", newRoot, valuesAfter) })
                {
                    var peaks = (TextBox)Find(pair.Item2, "TextBoxPeaksMinimumAlc")!;
                    var deep = (TextBox)Find(pair.Item2, "TextBoxDeepNovoMinimumScore")!;
                    pair.Item3[tag] = peaks.IsEnabled + "/" + deep.IsEnabled;
                }
            }
            bool enabledMatches = valuesBefore.SequenceEqual(valuesAfter);
            add(source, 69, 69, "changing the actual source selector updates the correct cutoff boxes for all five modes",
                valuesBefore, valuesAfter, enabledMatches, "DeNovoFilterControl source SelectionChanged / bound enabled state");
            add("FamilyQuant/ComputeXicWindow.xaml.cs", 129, 139, "all legacy source modes keep distinct cutoff-enabled state",
                valuesBefore, valuesAfter, enabledMatches, "DeNovoFilterViewModel source state");
            foreach (var field in new[] { (81, "TextBoxPeaksMinimumAlc"), (86, "TextBoxDeepNovoMinimumScore") })
            {
                string before = ((TextBox)Find(oldRoot, field.Item2)!).Text, after = ((TextBox)Find(newRoot, field.Item2)!).Text;
                add(source, field.Item1, field.Item1, "native initial confidence value " + field.Item2, before, after,
                    before == after && before == "80", "DeNovoFilterControl." + field.Item2, row => row.Kind == "option/copy");
            }
            foreach (var field in new[] { (95, "IntegerUpDownIsotopes"), (101, "IntegerUpDownMinPoints") })
            {
                var old = Find(oldRoot, field.Item2)!; var actual = Find(newRoot, field.Item2)!;
                foreach (string property in new[] { "Minimum", "Maximum" })
                {
                    string before = Convert.ToString(SecondaryRead(old, property), CultureInfo.InvariantCulture)!;
                    string after = Convert.ToString(SecondaryRead(actual, property), CultureInfo.InvariantCulture)!;
                    add(source, field.Item1, field.Item1, "native numeric boundary " + field.Item2 + "." + property,
                        before, after, before == after, "QuantifyControl." + field.Item2 + "." + property,
                        row => row.Kind == "option/copy" && row.Detail.Contains("." + property + " = ", StringComparison.Ordinal));
                }
            }
            var beforeBasis = new List<string>(); var afterBasis = new List<string>();
            foreach (int value in new[] { 1, 4, 6 })
            {
                foreach (var root in new[] { oldRoot, newRoot }) SecondaryProperty(Find(root, "IntegerUpDownIsotopes")!.GetType(), "Value").SetValue(Find(root, "IntegerUpDownIsotopes"), value);
                PumpDispatcher();
                beforeBasis.Add(Convert.ToString(SecondaryRead(frozen, "IsotopeCount"), CultureInfo.InvariantCulture)!);
                afterBasis.Add(current.ViewModel.IsotopeCount.ToString(CultureInfo.InvariantCulture));
            }
            add("FamilyQuant/ComputeXicWindow.xaml.cs", 75, 75, "isotope value-change event reaches the active extraction basis",
                beforeBasis, afterBasis, beforeBasis.SequenceEqual(afterBasis), "IntegerUpDownIsotopes.ValueChanged / ComputeXicViewModel.IsotopeCount", row => row.Kind == "handler");
            var beforePoints = new List<string>(); var afterPoints = new List<string>();
            foreach (int value in new[] { 1, 3, 8 })
            {
                foreach (var root in new[] { oldRoot, newRoot }) SecondaryProperty(Find(root, "IntegerUpDownMinPoints")!.GetType(), "Value").SetValue(Find(root, "IntegerUpDownMinPoints"), value);
                PumpDispatcher(); beforePoints.Add(Convert.ToString(SecondaryRead(frozen, "MinPointsForQuant"), CultureInfo.InvariantCulture)!);
                afterPoints.Add(current.ViewModel.MinPointsForQuant.ToString(CultureInfo.InvariantCulture));
            }
            add("FamilyQuant/ComputeXicWindow.xaml.cs", 76, 76, "minimum-point value-change event reaches the active extraction basis",
                beforePoints, afterPoints, beforePoints.SequenceEqual(afterPoints), "IntegerUpDownMinPoints.ValueChanged / ComputeXicViewModel.MinPointsForQuant", row => row.Kind == "handler");
        }
        finally { current.Close(); frozen.Close(); }
    }
}
