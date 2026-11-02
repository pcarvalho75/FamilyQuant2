using FamilyQuant.DeNovo;
using PatternTools.PLP;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace FamilyQuant.Tests
{
    internal static partial class Program
    {

        /// <summary>
        /// Lays the control out at a real screen size and checks that no text is cut off and that no text
        /// runs underneath a button.
        ///
        /// Data cells are excluded: a Description longer than its column is clipped by any table ever
        /// written and is not a defect. Everything else - labels, headings, status lines, button captions,
        /// column headers - is chrome we control, and it has to fit.
        /// </summary>
        /// <summary>Lays a control out at a real screen size, on a chosen tab, and lets it settle.</summary>
        private static void LayoutAt(FrameworkElement root, Size size, int tabIndex)
        {
            if (tabIndex >= 0)
            {
                var tabs = WalkTree(root).OfType<TabControl>().FirstOrDefault();
                if (tabs != null && tabIndex < tabs.Items.Count) tabs.SelectedIndex = tabIndex;
            }

            // Three passes, not one. A DataGrid settles over more than one layout pass: the first tells
            // it how wide its headers and cells want to be, and what it computes from that is only
            // honoured on the next one.
            for (int pass = 0; pass < 3; pass++)
            {
                root.InvalidateMeasure();
                root.Measure(size);
                root.Arrange(new Rect(new Point(0, 0), size));
                root.UpdateLayout();
            }
        }

        private static void CheckLayout(string what, FrameworkElement root, Size size, int tabIndex)
        {
            LayoutAt(root, size, tabIndex);

            // Collapsed controls can retain a previous template size. They paint no pixels and cannot
            // cover a visible header, even when the harness measures a root without showing a Window.
            bool Displayed(FrameworkElement element)
            {
                for (DependencyObject? node = element; node != null; node = VisualParentOf(node))
                {
                    if (node is UIElement ui && ui.Visibility != Visibility.Visible) return false;
                    if (ReferenceEquals(node, root)) return true;
                }
                return false;
            }
            var elements = WalkTree(root).OfType<FrameworkElement>().Where(Displayed).ToList();

            // WPF's own answer to "is this element bigger than the space it was given". Comparing
            // ActualWidth against a re-measurement does NOT work: a TextBlock that overflows reports the
            // width of its text, not the width of its slot, so that comparison is always equal and would
            // pass no matter how badly the text was cut off. GetLayoutClip is the geometry the layout
            // system installs precisely when it has to cut an element down to its slot.
            var clipped = elements
                .Where(e => e.ActualWidth > 0 && e.ActualHeight > 0)
                .Where(e => e is TextBlock || e is Label || e is Button || e is CheckBox || e is ComboBox)
                .Where(e => !HasAncestor<DataGridCell>(e))      // data, not chrome: any table clips long values
                .Where(e => LayoutInformation.GetLayoutClip(e) != null)
                .Select(DescribeVisually)
                .ToList();

            Check(what + ": no text is cut off", clipped.Count == 0, string.Join(" · ", clipped.Take(4)));

            // The defect from the screenshot: text rendered underneath a button.
            var buttons = elements.OfType<Button>()
                .Where(b => b.ActualWidth > 1 && b.ActualHeight > 1)
                .ToList();

            var texts = elements.OfType<TextBlock>()
                .Where(t => t.ActualWidth > 1 && t.ActualHeight > 1 && !string.IsNullOrWhiteSpace(t.Text))
                .ToList();

            var overlaps = new List<string>();

            foreach (Button button in buttons)
            {
                Rect buttonBounds = VisibleBoundsIn(root, button);
                if (buttonBounds.IsEmpty) continue;

                foreach (TextBlock text in texts)
                {
                    if (IsAncestorOf(button, text)) continue;    // the button's own caption

                    Rect textBounds = VisibleBoundsIn(root, text);
                    if (textBounds.IsEmpty) continue;

                    // Half a unit of slack so that merely touching edges is not an overlap.
                    textBounds.Inflate(-0.5, -0.5);
                    if (textBounds.Width <= 0 || textBounds.Height <= 0) continue;

                    if (buttonBounds.IntersectsWith(textBounds))
                    {
                        overlaps.Add(Shorten(text.Text) + " under " + Describe(button));
                    }
                }
            }

            Check(what + ": no text runs underneath a button", overlaps.Count == 0,
                string.Join(" · ", overlaps.Take(4)));
        }

        /// <summary>
        /// Proves the clipping detector is live before it is trusted. A check that cannot fail is not a
        /// check, and the first version of this one could not: it compared a TextBlock's ActualWidth
        /// with a re-measurement of the same text, which are equal by construction even when the text is
        /// being cut in half.
        /// </summary>
        private static void ClippingDetectorSelfTest()
        {
            var tooLong = new TextBlock
            {
                Text = "a caption far longer than the ten pixels it is about to be given",
                TextWrapping = TextWrapping.NoWrap
            };

            var host = new Border { Width = 10, Height = 20, Child = tooLong };
            host.Measure(new Size(10, 20));
            host.Arrange(new Rect(0, 0, 10, 20));
            host.UpdateLayout();

            Check("the clipping detector can actually see a label that does not fit",
                LayoutInformation.GetLayoutClip(tooLong) != null);

            var fits = new TextBlock { Text = "ok", TextWrapping = TextWrapping.NoWrap };
            var roomy = new Border { Width = 400, Height = 40, Child = fits };
            roomy.Measure(new Size(400, 40));
            roomy.Arrange(new Rect(0, 0, 400, 40));
            roomy.UpdateLayout();

            Check("and does not accuse a label that fits",
                LayoutInformation.GetLayoutClip(fits) == null);

            var overflow = new TextBlock { Text = new string('M', 50), Width = 500, Height = 20 };
            var clippedHost = new Border { Width = 50, Height = 20, ClipToBounds = true, Child = overflow };
            var canvas = new Canvas { Width = 600, Height = 100 };
            canvas.Children.Add(clippedHost);
            LayoutAt(canvas, new Size(600, 100), -1);
            Check("the overlap detector respects clipped table and scroll content",
                BoundsIn(canvas, overflow).Width > 50
                && Math.Abs(VisibleBoundsIn(canvas, overflow).Width - 50) < 0.001,
                "raw=" + BoundsIn(canvas, overflow) + "; visible=" + VisibleBoundsIn(canvas, overflow));

            string longHeading = new string('M', 50);
            var headerGrid = new DataGrid { Width = 600, Height = 100, AutoGenerateColumns = false };
            headerGrid.Columns.Add(new DataGridTextColumn { Header = longHeading, Width = 100, MinWidth = 100 });
            headerGrid.Columns.Add(new DataGridTextColumn { Header = "Second", Width = 400 });
            canvas.Children.Clear(); canvas.Children.Add(headerGrid);
            LayoutAt(canvas, new Size(600, 100), -1);
            var heading = WalkTree(headerGrid).OfType<TextBlock>().First(block => block.Text == longHeading);
            Check("overlap bounds respect native per-column layout clipping",
                VisibleBoundsIn(canvas, heading).Width <= 100,
                "raw=" + BoundsIn(canvas, heading) + "; visible=" + VisibleBoundsIn(canvas, heading));
        }

        private static string DescribeVisually(FrameworkElement element)
        {
            Rect slot = LayoutInformation.GetLayoutSlot(element);

            string name = element is TextBlock block && !string.IsNullOrWhiteSpace(block.Text) ? Shorten(block.Text)
                        : element is ContentControl content && content.Content is string s ? Shorten(s)
                        : Describe(element);

            return name + " [needs " + element.RenderSize.Width.ToString("F0") + "x" +
                   element.RenderSize.Height.ToString("F0") + ", slot " + slot.Width.ToString("F0") + "x" +
                   slot.Height.ToString("F0") + "]";
        }

        private static string Shorten(string text)
        {
            string flat = text.Replace("\r", " ").Replace("\n", " ").Trim();
            return flat.Length <= 42 ? "\"" + flat + "\"" : "\"" + flat.Substring(0, 40) + "...\"";
        }

        /// <summary>Bounds relative to <paramref name="root"/>, or empty when the element is not under it.</summary>
        private static Rect BoundsIn(FrameworkElement root, FrameworkElement element)
        {
            try
            {
                return element.TransformToAncestor(root)
                    .TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
            }
            catch (InvalidOperationException)
            {
                // Left over from a tab that is no longer realised; nothing to measure.
                return Rect.Empty;
            }
        }

        private static Rect VisibleBoundsIn(FrameworkElement root, FrameworkElement element)
        {
            Rect bounds = BoundsIn(root, element);
            for (DependencyObject? node = element; node != null && !bounds.IsEmpty; node = VisualParentOf(node))
            {
                if (node is FrameworkElement parent)
                {
                    if (parent.ClipToBounds || ReferenceEquals(parent, root)) bounds.Intersect(BoundsIn(root, parent));
                    if (parent.Clip is Geometry clip)
                        bounds.Intersect(parent.TransformToAncestor(root).TransformBounds(clip.Bounds));
                    if (VisualTreeHelper.GetClip(parent) is Geometry renderedClip)
                        bounds.Intersect(parent.TransformToAncestor(root).TransformBounds(renderedClip.Bounds));
                }
                if (ReferenceEquals(node, root)) break;
            }
            return bounds;
        }

        private static bool IsAncestorOf(DependencyObject ancestor, DependencyObject node)
        {
            for (DependencyObject? current = node; current != null; current = VisualParentOf(current))
            {
                if (ReferenceEquals(current, ancestor)) return true;
            }

            return false;
        }

        private static DependencyObject? VisualParentOf(DependencyObject node)
        {
            if (node is Visual || node is System.Windows.Media.Media3D.Visual3D)
            {
                DependencyObject? parent = VisualTreeHelper.GetParent(node);
                if (parent != null) return parent;
            }

            return LogicalTreeHelper.GetParent(node);
        }

        /// <summary>
        /// Every element of a control, taking the logical tree AND the visual one.
        ///
        /// Neither alone is enough. The logical tree holds everything the XAML declares even when it has
        /// never been rendered, which is what makes the tooltip sweep work on a control that was never put
        /// in a window; the visual tree holds what a template produced, which is what the layout checks
        /// have to measure.
        /// </summary>
        private static List<DependencyObject> WalkTree(DependencyObject root)
        {
            var found = new List<DependencyObject>();
            var seen = new HashSet<DependencyObject>();
            var pending = new Stack<DependencyObject>();

            pending.Push(root);

            while (pending.Count > 0)
            {
                DependencyObject node = pending.Pop();
                if (!seen.Add(node)) continue;

                found.Add(node);

                foreach (object child in LogicalTreeHelper.GetChildren(node))
                {
                    if (child is DependencyObject dependencyChild) pending.Push(dependencyChild);
                }

                // GetChildrenCount throws on anything that is not a Visual, and plenty of what the
                // logical tree returns is not - Run, Inline, DataGridColumn.
                if (node is Visual || node is System.Windows.Media.Media3D.Visual3D)
                {
                    int count = VisualTreeHelper.GetChildrenCount(node);
                    for (int i = 0; i < count; i++) pending.Push(VisualTreeHelper.GetChild(node, i));
                }
            }

            return found;
        }

        /// <summary>The rows the grid is currently showing, i.e. exactly what an export would write.</summary>
        private static List<FastaItemExtended> VisibleRows(DataGrid grid)
        {
            var source = grid.ItemsSource;
            if (source == null) return new List<FastaItemExtended>();

            return source.Cast<FastaItemExtended>().ToList();
        }

    }
}
