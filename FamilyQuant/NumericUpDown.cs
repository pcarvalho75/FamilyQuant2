using System;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace FamilyQuant
{
    /// <summary>One numeric editor for the app, using its Fluent text and button styles.</summary>
    [TemplatePart(Name = "PART_Text", Type = typeof(TextBox))]
    [TemplatePart(Name = "PART_Up", Type = typeof(RepeatButton))]
    [TemplatePart(Name = "PART_Down", Type = typeof(RepeatButton))]
    public class NumericUpDown : Control
    {
        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(decimal?),
            typeof(NumericUpDown), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                ValueChanged, CoerceValue));
        public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(nameof(Minimum), typeof(decimal),
            typeof(NumericUpDown), new PropertyMetadata(decimal.MinValue, BoundsChanged));
        public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(nameof(Maximum), typeof(decimal),
            typeof(NumericUpDown), new PropertyMetadata(decimal.MaxValue, BoundsChanged));
        public static readonly DependencyProperty IncrementProperty = DependencyProperty.Register(nameof(Increment), typeof(decimal),
            typeof(NumericUpDown), new PropertyMetadata(1m), value => (decimal)value > 0);

        public decimal? Value
        {
            get => (decimal?)GetValue(ValueProperty);
            set => SetCurrentValue(ValueProperty, value.HasValue ? Normalize(value.Value) : null);
        }
        public decimal Minimum { get => (decimal)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
        public decimal Maximum { get => (decimal)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
        public decimal Increment { get => (decimal)GetValue(IncrementProperty); set => SetValue(IncrementProperty, value); }
        protected bool IntegerOnly { get; init; }

        private TextBox? editor;
        private RepeatButton? up;
        private RepeatButton? down;
        private bool editing;

        public override void OnApplyTemplate()
        {
            if (editor != null)
            {
                editor.TextChanged -= TextChanged;
                editor.LostKeyboardFocus -= TextLostFocus;
            }
            if (up != null) up.Click -= Up;
            if (down != null) down.Click -= Down;
            base.OnApplyTemplate();
            editor = GetTemplateChild("PART_Text") as TextBox;
            up = GetTemplateChild("PART_Up") as RepeatButton;
            down = GetTemplateChild("PART_Down") as RepeatButton;
            if (editor != null)
            {
                editor.TextChanged += TextChanged;
                editor.LostKeyboardFocus += TextLostFocus;
            }
            if (up != null) up.Click += Up;
            if (down != null) down.Click += Down;
            RefreshText();
        }

        private static object CoerceValue(DependencyObject target, object value)
        {
            if (value is not decimal number) return value;
            return ((NumericUpDown)target).Normalize(number);
        }

        private decimal Normalize(decimal number) => Math.Clamp(IntegerOnly ? decimal.Truncate(number) : number,
            Minimum, Math.Max(Minimum, Maximum));

        private static void ValueChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
        {
            var control = (NumericUpDown)target;
            if (!control.editing) control.RefreshText();
        }

        private static void BoundsChanged(DependencyObject target, DependencyPropertyChangedEventArgs args) =>
            target.CoerceValue(ValueProperty);

        private void RefreshText()
        {
            if (editor == null) return;
            editing = true;
            editor.Text = Value?.ToString(CultureInfo.CurrentCulture) ?? string.Empty;
            editing = false;
        }

        private void TextChanged(object sender, TextChangedEventArgs args)
        {
            if (editing || editor == null) return;
            // This is localized UI input, like the replaced editor. Persisted data uses invariant parsing.
            if (decimal.TryParse(editor.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out decimal number)
                && (!IntegerOnly || number == decimal.Truncate(number)))
            {
                editing = true;
                Value = number;
                editing = false;
            }
            else if (string.IsNullOrWhiteSpace(editor.Text)) Value = null;
        }

        private void TextLostFocus(object sender, KeyboardFocusChangedEventArgs args) => RefreshText();
        private void Up(object sender, RoutedEventArgs args) => Step(1);
        private void Down(object sender, RoutedEventArgs args) => Step(-1);

        public void Step(int direction)
        {
            if (!IsEnabled || direction == 0) return;
            decimal number;
            try { number = (Value ?? 0m) + Math.Sign(direction) * Increment; }
            catch (OverflowException) { number = direction > 0 ? Maximum : Minimum; }
            Value = number;
            RefreshText();
        }

        protected override void OnPreviewKeyDown(KeyEventArgs args)
        {
            if (args.Key is Key.Up or Key.Down)
            {
                Step(args.Key == Key.Up ? 1 : -1);
                args.Handled = true;
            }
            else if (args.Key == Key.Enter) RefreshText();
            base.OnPreviewKeyDown(args);
        }

        protected override void OnMouseWheel(MouseWheelEventArgs args)
        {
            if (IsKeyboardFocusWithin)
            {
                Step(args.Delta);
                args.Handled = true;
            }
            base.OnMouseWheel(args);
        }

        protected override AutomationPeer OnCreateAutomationPeer() => new NumericPeer(this);

        private sealed class NumericPeer(NumericUpDown owner) : FrameworkElementAutomationPeer(owner), IRangeValueProvider
        {
            protected override string GetClassNameCore() => nameof(NumericUpDown);
            protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Spinner;
            public override object? GetPattern(PatternInterface pattern) =>
                pattern == PatternInterface.RangeValue ? this : base.GetPattern(pattern);
            public bool IsReadOnly => !owner.IsEnabled;
            public double LargeChange => (double)owner.Increment;
            public double SmallChange => (double)owner.Increment;
            public double Maximum => (double)owner.Maximum;
            public double Minimum => (double)owner.Minimum;
            public double Value => (double)(owner.Value ?? 0m);
            public void SetValue(double value)
            {
                if (!owner.IsEnabled) throw new ElementNotEnabledException();
                if (!double.IsFinite(value) || value < Minimum || value > Maximum) throw new ArgumentOutOfRangeException(nameof(value));
                owner.Value = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
            }
        }
    }

    // Typed adapters preserve the existing callers' nullable integer/decimal Value contract.
    public sealed class IntegerUpDown : NumericUpDown
    {
        public IntegerUpDown()
        {
            IntegerOnly = true;
            Minimum = int.MinValue;
            Maximum = int.MaxValue;
        }
        public new int? Value { get => (int?)base.Value; set => base.Value = value; }
    }

    public sealed class DecimalUpDown : NumericUpDown { }
}
