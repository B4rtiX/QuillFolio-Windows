using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using Quillfolio.Core.Models;
using Quillfolio.Core.Services;
using Quillfolio.WPF.Services;

namespace Quillfolio.WPF.Views;

public partial class SettingsWindow : Window
{
    private bool _isInitializing = true;
    private ShortcutAction? _capturingAction;

    public SettingsWindow()
    {
        InitializeComponent();
        WindowBackdropHelper.ApplyModernWindowChrome(this);
        UIAnimationHelper.AttachWindowEntranceAnimation(this);
        TxtDataPath.Text = DataStore.Instance.GetDataFilePath();

        if (LocalizationService.Instance.CurrentLanguage == AppLanguage.English)
            RbEnglish.IsChecked = true;
        else
            RbPolish.IsChecked = true;

        var page = DataStore.Instance.ActiveNote?.CurrentPage;
        if (page != null)
        {
            CmbPaperStyle.SelectedIndex = (int)page.PaperStyle;
            CmbPaperColor.SelectedIndex = (int)page.PaperColor;
        }
        else
        {
            CmbPaperStyle.SelectedIndex = (int)PaperStyle.Grid;
            CmbPaperColor.SelectedIndex = (int)PaperColorTheme.White;
        }

        SliderGuidelineSpacing.Value = DataStore.Instance.GuidelineSpacing;
        TxtSpacingVal.Text = $"{(int)DataStore.Instance.GuidelineSpacing} px";

        ChkSmoothing.IsChecked = DataStore.Instance.IsSmoothingEnabled;
        ChkShapeSnap.IsChecked = DataStore.Instance.IsShapeSnapEnabled;

        SliderSensitivity.Value = DataStore.Instance.StylusPressureFactor;
        TxtSensitivityVal.Text = $"{DataStore.Instance.StylusPressureFactor:0.0}x";

        SliderHoldDelay.Value = DataStore.Instance.ShapeHoldDelayMs;
        TxtHoldDelayVal.Text = $"{DataStore.Instance.ShapeHoldDelayMs} ms";

        var ds = DataStore.Instance;
        ChkAutoSelectShapes.IsChecked = ds.AutoSelectInsertedShapes;

        SliderNibAngle.Value = ds.FountainNibAngleDegrees;
        TxtNibAngleVal.Text = $"{ds.FountainNibAngleDegrees:0}°";

        SliderEraserSize.Value = ds.EraserSizeMultiplier;
        TxtEraserSizeVal.Text = $"{ds.EraserSizeMultiplier:0.0}x";

        SliderLaserFade.Value = ds.LaserFadeMs;
        TxtLaserFadeVal.Text = $"{ds.LaserFadeMs} ms";

        string appThemeTag = ds.AppTheme.ToString();
        foreach (ComboBoxItem item in CmbAppTheme.Items)
        {
            if (item.Tag?.ToString() == appThemeTag)
            {
                CmbAppTheme.SelectedItem = item;
                break;
            }
        }
        if (CmbAppTheme.SelectedIndex < 0) CmbAppTheme.SelectedIndex = 0;

        foreach (ComboBoxItem item in CmbDeskTheme.Items)
        {
            if (item.Tag?.ToString() == ds.DeskTheme)
            {
                CmbDeskTheme.SelectedItem = item;
                break;
            }
        }
        if (CmbDeskTheme.SelectedIndex < 0) CmbDeskTheme.SelectedIndex = 0;

        string toolTag = ds.DefaultStartupTool.ToString();
        foreach (ComboBoxItem item in CmbDefaultTool.Items)
        {
            if (item.Tag?.ToString() == toolTag)
            {
                CmbDefaultTool.SelectedItem = item;
                break;
            }
        }
        if (CmbDefaultTool.SelectedIndex < 0) CmbDefaultTool.SelectedIndex = 0;

        string sizeTag = $"{ds.DefaultPageWidth:0}x{ds.DefaultPageHeight:0}";
        foreach (ComboBoxItem item in CmbDefaultPageSize.Items)
        {
            if (item.Tag?.ToString() == sizeTag)
            {
                CmbDefaultPageSize.SelectedItem = item;
                break;
            }
        }
        if (CmbDefaultPageSize.SelectedIndex < 0) CmbDefaultPageSize.SelectedIndex = 0;

        PreviewKeyDown += SettingsWindow_PreviewKeyDown;

        _isInitializing = false;
        UpdateLocalization();
        BuildShortcutsList();
    }

    private void UpdateLocalization()
    {
        var loc = LocalizationService.Instance;
        Title = loc.Get("SettingsTitle");
        TxtTitle.Text = loc.Get("SettingsTitle");
        LblLang.Text = loc.Get("Language");
        LblPaper.Text = loc.Get("PaperStyle");
        LblPaperColor.Text = loc.Get("PaperColor");
        LblGuidelineSpacing.Text = loc.Get("GuidelineSpacing");
        ChkSmoothing.Content = loc.Get("SmoothingEnabled");
        ChkShapeSnap.Content = loc.Get("ShapeSnapEnabled");
        LblStylus.Text = loc.Get("StylusSensitivity");
        LblHoldDelay.Text = loc.Get("ShapeHoldDelay");
        LblDataFolder.Text = loc.Get("DataFolder");
        BtnOpenDataFolder.Content = loc.Get("OpenDataFolder");
        BtnClose.Content = loc.Get("Close");

        ItemPaperBlank.Content = loc.Get("PaperBlank");
        ItemPaperRuled.Content = loc.Get("PaperRuled");
        ItemPaperGrid.Content = loc.Get("PaperGrid");
        ItemPaperDotted.Content = loc.Get("PaperDotted");

        ItemColorWhite.Content = loc.Get("ColorWhite");
        ItemColorCream.Content = loc.Get("ColorCream");
        ItemColorYellow.Content = loc.Get("ColorLegalYellow");
        ItemColorSlate.Content = loc.Get("ColorDarkSlate");
        ItemColorChalk.Content = loc.Get("ColorChalkboard");

        LblAppTheme.Text = loc.Get("AppTheme");
        ItemThemeLight.Content = loc.Get("ThemeLight");
        ItemThemeDark.Content = loc.Get("ThemeDark");
        ItemThemeOled.Content = loc.Get("ThemeOled");
        ItemThemeSystem.Content = loc.Get("ThemeSystem");

        LblShortcutsTitle.Text = loc.Get("ShortcutsTitle");
        BtnResetShortcuts.Content = loc.Get("ShortcutResetDefaults");
        BuildShortcutsList();
    }

    private void RbPolish_Checked(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;
        LocalizationService.Instance.SetLanguage(AppLanguage.Polish);
        DataStore.Instance.SaveSettings();
        UpdateLocalization();
    }

    private void RbEnglish_Checked(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;
        LocalizationService.Instance.SetLanguage(AppLanguage.English);
        DataStore.Instance.SaveSettings();
        UpdateLocalization();
    }

    private void CmbPaperStyle_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        if (DataStore.Instance.ActiveNote?.CurrentPage != null && CmbPaperStyle.SelectedIndex >= 0)
        {
            DataStore.Instance.ActiveNote.CurrentPage.PaperStyle = (PaperStyle)CmbPaperStyle.SelectedIndex;
            DataStore.Instance.NotifyCanvasChanged();
        }
    }

    private void CmbPaperColor_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        if (DataStore.Instance.ActiveNote?.CurrentPage != null && CmbPaperColor.SelectedIndex >= 0)
        {
            DataStore.Instance.ActiveNote.CurrentPage.PaperColor = (PaperColorTheme)CmbPaperColor.SelectedIndex;
            DataStore.Instance.NotifyCanvasChanged();
        }
    }

    private void SliderGuidelineSpacing_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isInitializing) return;
        DataStore.Instance.GuidelineSpacing = e.NewValue;
        if (TxtSpacingVal != null) TxtSpacingVal.Text = $"{(int)e.NewValue} px";
        DataStore.Instance.NotifyCanvasChanged();
    }

    private void ChkSmoothing_Changed(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;
        DataStore.Instance.IsSmoothingEnabled = ChkSmoothing.IsChecked == true;
        DataStore.Instance.SaveSettings();
    }

    private void ChkShapeSnap_Changed(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;
        DataStore.Instance.IsShapeSnapEnabled = ChkShapeSnap.IsChecked == true;
        DataStore.Instance.SaveSettings();
    }

    private void SliderSensitivity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isInitializing) return;
        DataStore.Instance.StylusPressureFactor = e.NewValue;
        if (TxtSensitivityVal != null) TxtSensitivityVal.Text = $"{e.NewValue:0.0}x";
    }

    private void SliderHoldDelay_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isInitializing) return;
        DataStore.Instance.ShapeHoldDelayMs = (int)e.NewValue;
        if (TxtHoldDelayVal != null) TxtHoldDelayVal.Text = $"{(int)e.NewValue} ms";
        DataStore.Instance.SaveSettings();
    }

    private void ChkAutoSelectShapes_Changed(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;
        DataStore.Instance.AutoSelectInsertedShapes = ChkAutoSelectShapes.IsChecked == true;
        DataStore.Instance.SaveSettings();
    }

    private void SliderNibAngle_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isInitializing) return;
        double deg = Math.Round(e.NewValue);
        DataStore.Instance.FountainNibAngleDegrees = deg;
        if (TxtNibAngleVal != null) TxtNibAngleVal.Text = $"{deg:0}°";
        DataStore.Instance.SaveSettings();
        DataStore.Instance.NotifyCanvasChanged();
    }

    private void SliderEraserSize_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isInitializing) return;
        double mult = Math.Round(e.NewValue, 1);
        DataStore.Instance.EraserSizeMultiplier = mult;
        if (TxtEraserSizeVal != null) TxtEraserSizeVal.Text = $"{mult:0.0}x";
        DataStore.Instance.SaveSettings();
    }

    private void SliderLaserFade_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isInitializing) return;
        int ms = (int)Math.Round(e.NewValue);
        DataStore.Instance.LaserFadeMs = ms;
        if (TxtLaserFadeVal != null) TxtLaserFadeVal.Text = $"{ms} ms";
        DataStore.Instance.SaveSettings();
    }

    private void CmbAppTheme_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing || CmbAppTheme.SelectedItem is not ComboBoxItem item) return;
        if (Enum.TryParse<AppThemeMode>(item.Tag?.ToString(), out var mode))
        {
            DataStore.Instance.AppTheme = mode;
            WindowBackdropHelper.ApplyAppTheme(mode, DataStore.Instance.DeskTheme);
            DataStore.Instance.SaveSettings();
        }
    }

    private void CmbDeskTheme_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing || CmbDeskTheme.SelectedItem is not ComboBoxItem item) return;
        string theme = item.Tag?.ToString() ?? "NordicFrost";
        DataStore.Instance.DeskTheme = theme;
        WindowBackdropHelper.ApplyDeskTheme(theme);
        DataStore.Instance.SaveSettings();
    }

    private void CmbDefaultTool_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing || CmbDefaultTool.SelectedItem is not ComboBoxItem item) return;
        if (Enum.TryParse<ToolType>(item.Tag?.ToString(), out var tool))
        {
            DataStore.Instance.DefaultStartupTool = tool;
            DataStore.Instance.SaveSettings();
        }
    }

    private void CmbDefaultPageSize_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing || CmbDefaultPageSize.SelectedItem is not ComboBoxItem item) return;
        string[] parts = (item.Tag?.ToString() ?? "2400x3200").Split('x');
        if (parts.Length == 2 && double.TryParse(parts[0], out double w) && double.TryParse(parts[1], out double h))
        {
            DataStore.Instance.DefaultPageWidth = w;
            DataStore.Instance.DefaultPageHeight = h;
            DataStore.Instance.SaveSettings();
        }
    }

    private void BtnOpenDataFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = DataStore.Instance.GetDataFolder(),
                UseShellExecute = true
            });
        }
        catch { }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        DataStore.Instance.SaveSettings();
        DataStore.Instance.SaveToDisk();
        Close();
    }

    #region Customizable Keyboard Shortcuts

    private void BuildShortcutsList()
    {
        if (ShortcutsListHost == null) return;
        ShortcutsListHost.Children.Clear();
        var loc = LocalizationService.Instance;

        foreach (ShortcutAction action in Enum.GetValues<ShortcutAction>())
        {
            var binding = DataStore.Instance.GetShortcut(action);
            bool isCapturing = _capturingAction == action;

            var row = new DockPanel
            {
                Margin = new Thickness(4, 3, 4, 3)
            };

            var changeBtn = new Button
            {
                Content = isCapturing ? loc.Get("ShortcutPressKey") : loc.Get("ShortcutChange"),
                Padding = new Thickness(10, 3, 10, 3),
                FontSize = 11.5,
                MinWidth = 72,
                Focusable = false,
                Style = (Style)FindResource(isCapturing ? "PrimaryButtonStyle" : "OutlineButtonStyle")
            };
            ShortcutAction capAction = action;
            changeBtn.Click += (_, _) =>
            {
                _capturingAction = _capturingAction == capAction ? null : capAction;
                BuildShortcutsList();
            };
            DockPanel.SetDock(changeBtn, Dock.Right);

            var keyBadge = new Border
            {
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(10, 4, 10, 4),
                Margin = new Thickness(8, 0, 8, 0),
                BorderThickness = new Thickness(1),
                MinWidth = 98,
                Child = new TextBlock
                {
                    Text = isCapturing ? "..." : binding.ToDisplayString(),
                    FontSize = 11.5,
                    FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            keyBadge.SetResourceReference(Border.BackgroundProperty, "CardBackgroundBrush");
            keyBadge.SetResourceReference(Border.BorderBrushProperty, isCapturing ? "PrimaryBrush" : "BorderBrush");
            ((TextBlock)keyBadge.Child).SetResourceReference(TextBlock.ForegroundProperty, isCapturing ? "PrimaryBrush" : "TextPrimaryBrush");
            DockPanel.SetDock(keyBadge, Dock.Right);

            var label = new TextBlock
            {
                Text = loc.Get("Shortcut_" + action),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");

            row.Children.Add(changeBtn);
            row.Children.Add(keyBadge);
            row.Children.Add(label);
            ShortcutsListHost.Children.Add(row);
        }
    }

    private void SettingsWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_capturingAction.HasValue) return;

        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape)
        {
            _capturingAction = null;
            BuildShortcutsList();
            e.Handled = true;
            return;
        }

        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
        {
            e.Handled = true;
            return;
        }

        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        bool alt = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);

        var binding = new ShortcutBinding(key.ToString(), ctrl, shift, alt);
        DataStore.Instance.UpdateShortcut(_capturingAction.Value, binding);
        _capturingAction = null;
        BuildShortcutsList();
        e.Handled = true;
    }

    private void BtnResetShortcuts_Click(object sender, RoutedEventArgs e)
    {
        _capturingAction = null;
        DataStore.Instance.ResetShortcutsToDefault();
        BuildShortcutsList();
    }

    #endregion
}
