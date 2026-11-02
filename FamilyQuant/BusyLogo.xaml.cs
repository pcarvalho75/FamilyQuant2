using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace FamilyQuant;

/// <summary>A single slow family-assembly wave. No worker, timer or layout animation.</summary>
public partial class BusyLogo : UserControl
{
    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(nameof(IsActive), typeof(bool), typeof(BusyLogo),
        new PropertyMetadata(false, (owner, _) => ((BusyLogo)owner).RefreshMotion()));
    public bool IsActive { get => (bool)GetValue(IsActiveProperty); set => SetValue(IsActiveProperty, value); }
    private Storyboard? motion;
    private bool subscribed;
    private bool? preferenceOverride;
    internal bool? AnimationPreferenceOverride { get => preferenceOverride; set { preferenceOverride = value; RefreshMotion(); } }
    internal bool IsAnimating => motion != null;
    private FrameworkElement[] Families => new FrameworkElement[] { FamilyLeft, FamilyMiddle, FamilyRight };
    private FrameworkElement[] Bars => new FrameworkElement[] { BarLeft, BarMiddle, BarRight };
    internal bool HasLogoClocks => Families.Any(family => family.HasAnimatedProperties || ((ScaleTransform)family.RenderTransform).HasAnimatedProperties)
        || Bars.Any(bar => bar.HasAnimatedProperties);

    public BusyLogo()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (!subscribed) { SystemParameters.StaticPropertyChanged += PreferenceChanged; subscribed = true; }
            RefreshMotion();
        };
        Unloaded += (_, _) =>
        {
            StopMotion();
            if (subscribed) { SystemParameters.StaticPropertyChanged -= PreferenceChanged; subscribed = false; }
        };
        IsVisibleChanged += (_, _) => RefreshMotion();
    }

    private void PreferenceChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is not (null or "" or nameof(SystemParameters.ClientAreaAnimation))) return;
        if (Dispatcher.CheckAccess()) RefreshMotion();
        else Dispatcher.BeginInvoke((Action)RefreshMotion);
    }

    private void RefreshMotion()
    {
        if (!IsLoaded || !IsVisible || !IsActive || !(preferenceOverride ?? SystemParameters.ClientAreaAnimation)) { StopMotion(); return; }
        if (motion != null) return;
        motion = new Storyboard { Duration = TimeSpan.FromSeconds(6), RepeatBehavior = RepeatBehavior.Forever };
        Timeline.SetDesiredFrameRate(motion, 30);
        for (int index = 0; index < Families.Length; index++)
        {
            var family = Families[index]; double delay = .2 + index * .35;
            var scale = (ScaleTransform)family.RenderTransform;
            Wave(scale, ScaleTransform.ScaleXProperty, 1.32, 1, delay);
            Wave(scale, ScaleTransform.ScaleYProperty, 1.32, 1, delay);
            Wave(family, OpacityProperty, .66, 1, delay);
            Wave(Bars[index], OpacityProperty, .56, 1, delay + .25);
        }
        motion.Begin(this, HandoffBehavior.SnapshotAndReplace, true);
    }

    private void Wave(DependencyObject target, DependencyProperty property, double quiet, double assembled, double delay)
    {
        var animation = new DoubleAnimationUsingKeyFrames();
        void Key(double time, double value) => animation.KeyFrames.Add(new EasingDoubleKeyFrame(value, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(time)),
            new SineEase { EasingMode = EasingMode.EaseInOut }));
        Key(0, quiet); Key(delay, quiet); Key(delay + 1.1, assembled); Key(3.7 + delay * .3, assembled); Key(6, quiet);
        Storyboard.SetTarget(animation, target); Storyboard.SetTargetProperty(animation, new PropertyPath(property));
        motion!.Children.Add(animation);
    }

    private void StopMotion()
    {
        motion?.Remove(this); motion = null;
        // Remove synchronously as well: an unloaded view may never receive another timing tick.
        foreach (var family in Families)
        {
            family.BeginAnimation(OpacityProperty, null);
            var scale = (ScaleTransform)family.RenderTransform;
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null); scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        }
        foreach (var bar in Bars) bar.BeginAnimation(OpacityProperty, null);
    }

    // Native proof capture seeks the real storyboard; production never drives frames manually.
    internal void SetCaptureTime(TimeSpan time)
    {
        if (motion == null) return;
        motion.Pause(this); motion.SeekAlignedToLastTick(this, time, TimeSeekOrigin.BeginTime);
    }
}
