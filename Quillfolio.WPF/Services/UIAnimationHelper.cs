using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Quillfolio.WPF.Services;

public static class UIAnimationHelper
{
    public static readonly IEasingFunction SmoothOut = new CubicEase { EasingMode = EasingMode.EaseOut };
    public static readonly IEasingFunction SmoothInOut = new CubicEase { EasingMode = EasingMode.EaseInOut };
    public static readonly IEasingFunction SmoothIn = new CubicEase { EasingMode = EasingMode.EaseIn };

    /// <summary>
    /// Smoothly fades & scales in any Window/Dialog when opened without rasterizing child text once settled.
    /// </summary>
    public static void AttachWindowEntranceAnimation(Window window)
    {
        window.Opacity = 0;
        window.Loaded += (_, _) =>
        {
            if (window.Content is FrameworkElement root)
            {
                root.RenderTransformOrigin = new Point(0.5, 0.5);
                var scale = new ScaleTransform(0.965, 0.965);
                var translate = new TranslateTransform(0, 10);
                var group = new TransformGroup();
                group.Children.Add(scale);
                group.Children.Add(translate);
                root.RenderTransform = group;

                var dur = TimeSpan.FromMilliseconds(220);
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.965, 1.0, dur) { EasingFunction = SmoothOut });
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.965, 1.0, dur) { EasingFunction = SmoothOut });
                translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, dur) { EasingFunction = SmoothOut });
            }

            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = SmoothOut
            };
            window.BeginAnimation(UIElement.OpacityProperty, fade);
        };
    }

    public static void AnimateFade(UIElement element, double from, double to, int ms, Action? completed = null)
    {
        var anim = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(ms))
        {
            EasingFunction = to > from ? SmoothOut : SmoothIn
        };
        if (completed != null)
        {
            anim.Completed += (_, _) => completed();
        }
        element.BeginAnimation(UIElement.OpacityProperty, anim);
    }
}

public class GridLengthAnimation : AnimationTimeline
{
    public override Type TargetPropertyType => typeof(GridLength);

    public GridLength From
    {
        get => (GridLength)GetValue(FromProperty);
        set => SetValue(FromProperty, value);
    }
    public static readonly DependencyProperty FromProperty =
        DependencyProperty.Register(nameof(From), typeof(GridLength), typeof(GridLengthAnimation));

    public GridLength To
    {
        get => (GridLength)GetValue(ToProperty);
        set => SetValue(ToProperty, value);
    }
    public static readonly DependencyProperty ToProperty =
        DependencyProperty.Register(nameof(To), typeof(GridLength), typeof(GridLengthAnimation));

    public IEasingFunction? EasingFunction
    {
        get => (IEasingFunction?)GetValue(EasingFunctionProperty);
        set => SetValue(EasingFunctionProperty, value);
    }
    public static readonly DependencyProperty EasingFunctionProperty =
        DependencyProperty.Register(nameof(EasingFunction), typeof(IEasingFunction), typeof(GridLengthAnimation));

    protected override Freezable CreateInstanceCore() => new GridLengthAnimation();

    public override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, AnimationClock animationClock)
    {
        double progress = animationClock.CurrentProgress ?? 0.0;
        if (EasingFunction != null)
        {
            progress = EasingFunction.Ease(progress);
        }

        double fromVal = From.Value;
        double toVal = To.Value;
        return new GridLength(fromVal + (toVal - fromVal) * progress, GridUnitType.Pixel);
    }
}
