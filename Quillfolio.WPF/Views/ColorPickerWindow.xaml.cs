using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Quillfolio.WPF.Services;

namespace Quillfolio.WPF.Views;

public partial class ColorPickerWindow : Window
{
    private const int WheelSize = 180;
    private const double WheelRadius = 87.0;

    private bool _updating;
    private bool _isDraggingWheel;
    private double _hue;        // 0..360
    private double _saturation = 1.0; // 0..1
    private double _value = 1.0;      // 0..1

    public string SelectedHex { get; private set; } = "#1E1E1E";

    private static readonly string[] ExtendedPalette =
    {
        "#000000", "#1E1E1E", "#4B5563", "#9CA3AF", "#FFFFFF",
        "#EF4444", "#F97316", "#F59E0B", "#EAB308", "#84CC16",
        "#22C55E", "#10B981", "#06B6D4", "#0EA5E9", "#2563EB",
        "#4F46E5", "#7C3AED", "#A855F7", "#EC4899", "#F43F5E"
    };

    public ColorPickerWindow(string initialHex)
    {
        InitializeComponent();
        WindowBackdropHelper.ApplyModernWindowChrome(this);
        UIAnimationHelper.AttachWindowEntranceAnimation(this);
        BuildSwatches();
        RenderColorWheel();
        SetFromHex(initialHex);
    }

    private void BuildSwatches()
    {
        SwatchesPanel.Children.Clear();
        foreach (var hex in ExtendedPalette)
        {
            string captured = hex;
            var btn = new Button
            {
                Width = 28,
                Height = 28,
                Margin = new Thickness(3),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(captured)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                ToolTip = captured,
                Tag = captured
            };
            btn.Template = CreateSwatchTemplate();
            btn.Click += (_, _) => SetFromHex(captured);
            btn.MouseDoubleClick += (_, _) =>
            {
                SetFromHex(captured);
                DialogResult = true;
                Close();
            };
            SwatchesPanel.Children.Add(btn);
        }
    }

    private static ControlTemplate CreateSwatchTemplate()
    {
        var tpl = new ControlTemplate(typeof(Button));
        var bd = new FrameworkElementFactory(typeof(Border));
        bd.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
        bd.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Button.BorderBrushProperty));
        bd.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Button.BorderThicknessProperty));
        bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        tpl.VisualTree = bd;
        return tpl;
    }

    private void RenderColorWheel()
    {
        if (WheelImage == null) return;
        var bmp = new WriteableBitmap(WheelSize, WheelSize, 96, 96, PixelFormats.Bgra32, null);
        byte[] pixels = new byte[WheelSize * WheelSize * 4];
        double cx = WheelSize * 0.5;
        double cy = WheelSize * 0.5;

        for (int y = 0; y < WheelSize; y++)
        {
            for (int x = 0; x < WheelSize; x++)
            {
                double dx = x - cx;
                double dy = y - cy;
                double dist = Math.Sqrt(dx * dx + dy * dy);
                int idx = (y * WheelSize + x) * 4;

                if (dist > WheelRadius + 1.0)
                {
                    pixels[idx + 3] = 0;
                    continue;
                }

                double alpha = dist <= WheelRadius - 0.5 ? 255.0 : Math.Clamp((WheelRadius + 1.0 - dist) / 1.5 * 255.0, 0, 255);
                double sat = Math.Clamp(dist / WheelRadius, 0.0, 1.0);
                double hue = (Math.Atan2(dy, dx) * 180.0 / Math.PI + 360.0) % 360.0;
                var c = HsvToColor(hue, sat, _value);

                pixels[idx + 0] = c.B;
                pixels[idx + 1] = c.G;
                pixels[idx + 2] = c.R;
                pixels[idx + 3] = (byte)alpha;
            }
        }

        bmp.WritePixels(new Int32Rect(0, 0, WheelSize, WheelSize), pixels, WheelSize * 4, 0);
        WheelImage.Source = bmp;
    }

    private void UpdateThumbPosition()
    {
        double cx = WheelSize * 0.5;
        double cy = WheelSize * 0.5;
        double rad = _hue * Math.PI / 180.0;
        double dist = Math.Clamp(_saturation, 0.0, 1.0) * WheelRadius;
        double tx = cx + dist * Math.Cos(rad) - WheelThumbOuter.Width * 0.5;
        double ty = cy + dist * Math.Sin(rad) - WheelThumbOuter.Height * 0.5;

        Canvas.SetLeft(WheelThumbOuter, tx);
        Canvas.SetTop(WheelThumbOuter, ty);
        Canvas.SetLeft(WheelThumbInner, tx);
        Canvas.SetTop(WheelThumbInner, ty);
    }

    private void PickFromWheelPoint(Point pt)
    {
        double cx = WheelSize * 0.5;
        double cy = WheelSize * 0.5;
        double dx = pt.X - cx;
        double dy = pt.Y - cy;
        double dist = Math.Sqrt(dx * dx + dy * dy);

        _saturation = Math.Clamp(dist / WheelRadius, 0.0, 1.0);
        _hue = (Math.Atan2(dy, dx) * 180.0 / Math.PI + 360.0) % 360.0;

        _updating = true;
        UpdateThumbPosition();
        UpdateDisplay(HsvToColor(_hue, _saturation, _value));
        _updating = false;
    }

    private void WheelCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.StylusDevice != null) return;
        _isDraggingWheel = true;
        WheelCanvas.CaptureMouse();
        PickFromWheelPoint(e.GetPosition(WheelCanvas));
        e.Handled = true;
    }

    private void WheelCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.StylusDevice != null || !_isDraggingWheel) return;
        PickFromWheelPoint(e.GetPosition(WheelCanvas));
    }

    private void WheelCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.StylusDevice != null) return;
        _isDraggingWheel = false;
        if (WheelCanvas.IsMouseCaptured) WheelCanvas.ReleaseMouseCapture();
    }

    private void WheelCanvas_StylusDown(object sender, StylusDownEventArgs e)
    {
        _isDraggingWheel = true;
        WheelCanvas.CaptureStylus();
        PickFromWheelPoint(e.GetPosition(WheelCanvas));
        e.Handled = true;
    }

    private void WheelCanvas_StylusMove(object sender, StylusEventArgs e)
    {
        if (!_isDraggingWheel) return;
        PickFromWheelPoint(e.GetPosition(WheelCanvas));
        e.Handled = true;
    }

    private void WheelCanvas_StylusUp(object sender, StylusEventArgs e)
    {
        _isDraggingWheel = false;
        if (WheelCanvas.IsStylusCaptured) WheelCanvas.ReleaseStylusCapture();
        e.Handled = true;
    }

    private void SliderValue_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updating || !IsInitialized || TxtValuePct == null) return;
        _value = e.NewValue;
        RenderColorWheel();
        _updating = true;
        UpdateDisplay(HsvToColor(_hue, _saturation, _value));
        _updating = false;
    }

    private void SetFromHex(string hex)
    {
        try
        {
            var c = (Color)ColorConverter.ConvertFromString(hex);
            _updating = true;
            RgbToHsv(c, out _hue, out _saturation, out _value);
            if (_value < 0.05) _value = 0.05;
            SliderValue.Value = _value;
            RenderColorWheel();
            UpdateThumbPosition();
            UpdateDisplay(c);
            _updating = false;
        }
        catch { }
    }

    private void TxtHex_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updating || !IsInitialized) return;
        string text = TxtHex.Text.Trim();
        if (!text.StartsWith("#")) text = "#" + text;
        if (text.Length == 7)
        {
            try
            {
                var c = (Color)ColorConverter.ConvertFromString(text);
                _updating = true;
                RgbToHsv(c, out _hue, out _saturation, out _value);
                SliderValue.Value = Math.Max(0.05, _value);
                RenderColorWheel();
                UpdateThumbPosition();
                SelectedHex = text.ToUpperInvariant();
                PreviewBorder.Background = new SolidColorBrush(c);
                TxtValuePct.Text = $"{(int)Math.Round(_value * 100)}%";
                _updating = false;
            }
            catch { }
        }
    }

    private void UpdateDisplay(Color c)
    {
        SelectedHex = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        TxtHex.Text = SelectedHex;
        TxtValuePct.Text = $"{(int)Math.Round(_value * 100)}%";
        PreviewBorder.Background = new SolidColorBrush(c);
    }

    private static Color HsvToColor(double h, double s, double v)
    {
        double c = v * s;
        double x = c * (1 - Math.Abs((h / 60.0) % 2 - 1));
        double m = v - c;
        double r = 0, g = 0, b = 0;

        if (h < 60) { r = c; g = x; }
        else if (h < 120) { r = x; g = c; }
        else if (h < 180) { g = c; b = x; }
        else if (h < 240) { g = x; b = c; }
        else if (h < 300) { r = x; b = c; }
        else { r = c; b = x; }

        return Color.FromRgb(
            (byte)Math.Clamp(Math.Round((r + m) * 255), 0, 255),
            (byte)Math.Clamp(Math.Round((g + m) * 255), 0, 255),
            (byte)Math.Clamp(Math.Round((b + m) * 255), 0, 255));
    }

    private static void RgbToHsv(Color color, out double h, out double s, out double v)
    {
        double r = color.R / 255.0;
        double g = color.G / 255.0;
        double b = color.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;

        v = max;
        s = max == 0 ? 0 : delta / max;

        if (delta == 0) h = 0;
        else if (max == r) h = 60.0 * (((g - b) / delta) % 6);
        else if (max == g) h = 60.0 * ((b - r) / delta + 2);
        else h = 60.0 * ((r - g) / delta + 4);
        if (h < 0) h += 360.0;
    }

    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
