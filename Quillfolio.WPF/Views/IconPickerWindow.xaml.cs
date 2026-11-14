using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Quillfolio.Core.Services;
using Quillfolio.WPF.Services;

namespace Quillfolio.WPF.Views;

public partial class IconPickerWindow : Window
{
    public string SelectedIconKey { get; private set; }
    public string SelectedColorHex { get; private set; }
    private readonly bool _allowColor;

    private static readonly string[] FolderColors =
    {
        "#3B82F6", "#6366F1", "#8B5CF6", "#EC4899", "#EF4444",
        "#F59E0B", "#10B981", "#0D9488", "#475569"
    };

    public IconPickerWindow(string currentIconKey, string currentColorHex = "#3B82F6", bool allowColor = true)
    {
        InitializeComponent();
        WindowBackdropHelper.ApplyModernWindowChrome(this);
        UIAnimationHelper.AttachWindowEntranceAnimation(this);
        SelectedIconKey = currentIconKey;
        SelectedColorHex = currentColorHex;
        _allowColor = allowColor;

        if (!allowColor)
        {
            ColorCard.Visibility = Visibility.Collapsed;
            Height = 390;
        }

        BuildColors();
        BuildIcons();
    }

    private void BuildColors()
    {
        ColorsPanel.Children.Clear();
        foreach (string hex in FolderColors)
        {
            bool selected = string.Equals(hex, SelectedColorHex, StringComparison.OrdinalIgnoreCase);
            var ring = new Border
            {
                CornerRadius = new CornerRadius(13),
                Padding = new Thickness(2),
                Margin = new Thickness(0, 0, 6, 0),
                BorderThickness = new Thickness(2),
                BorderBrush = selected ? (Brush)FindResource("PrimaryBrush") : Brushes.Transparent
            };

            var btn = new Button
            {
                Width = 20,
                Height = 20,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)),
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            btn.Click += (_, _) =>
            {
                SelectedColorHex = hex;
                BuildColors();
                BuildIcons();
            };

            ring.Child = btn;
            ColorsPanel.Children.Add(ring);
        }
    }

    private void BuildIcons()
    {
        IconsGrid.Children.Clear();
        bool isPl = LocalizationService.Instance.CurrentLanguage == AppLanguage.Polish;

        Brush accent;
        try { accent = new SolidColorBrush((Color)ColorConverter.ConvertFromString(SelectedColorHex)); }
        catch { accent = (Brush)FindResource("PrimaryBrush"); }

        foreach (var entry in FolderIconCatalog.All)
        {
            bool isSelected = string.Equals(entry.Key, SelectedIconKey, StringComparison.OrdinalIgnoreCase);
            var btn = new Button
            {
                Style = (Style)FindResource("FluentButtonStyle"),
                Height = 66,
                Margin = new Thickness(3),
                Padding = new Thickness(4),
                Background = isSelected ? new SolidColorBrush(Color.FromRgb(245, 243, 255)) : Brushes.Transparent,
                BorderBrush = isSelected ? new SolidColorBrush(Color.FromRgb(196, 181, 253)) : new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                BorderThickness = new Thickness(1)
            };

            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            stack.Children.Add(new Path
            {
                Width = 20,
                Height = 20,
                Stretch = Stretch.Uniform,
                Fill = accent,
                Data = Geometry.Parse(entry.PathData),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 5)
            });
            stack.Children.Add(new TextBlock
            {
                Text = isPl ? entry.LabelPl : entry.LabelEn,
                FontSize = 10,
                FontWeight = isSelected ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = (Brush)FindResource("TextPrimaryBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                HorizontalAlignment = HorizontalAlignment.Center
            });

            btn.Content = stack;
            btn.Click += (_, _) =>
            {
                SelectedIconKey = entry.Key;
                BuildIcons();
            };
            IconsGrid.Children.Add(btn);
        }
    }

    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
