using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Quillfolio.Core.Models;
using Quillfolio.Core.Services;
using Quillfolio.WPF.Services;

namespace Quillfolio.WPF.Controls;

public partial class StickyNoteOverlayControl : UserControl
{
    public FloatingTextBox Model { get; }
    private Point _dragStart;
    private bool _isDragging;
    private bool _isResizing;
    private Point _resizeStart;
    private double _startW;
    private double _startH;
    private bool _isActive;
    private bool _deleteTriggered;

    public bool IsActive => _isActive;

    public event Action<FloatingTextBox>? Deleted;
    public event Action<StickyNoteOverlayControl>? Activated;
    public event Action<FloatingTextBox>? ModelFormatted;
    public event Action<StickyNoteOverlayControl>? PinToggled;
    private long _lastPinToggleTicks;

    public StickyNoteOverlayControl(FloatingTextBox model)
    {
        InitializeComponent();
        Model = model;

        NoteBorder.Width = Math.Max(160, model.Width);
        if (model.Height > 0)
        {
            NoteBorder.MinHeight = Math.Max(64, model.Height);
        }
        TxtContent.Text = model.Text;

        ApplyFormattingUpdate();
        SetActive(false);

        TxtContent.TextChanged += (_, _) =>
        {
            Model.Text = TxtContent.Text;
            DataStore.Instance.SaveToDisk();
        };

        TxtContent.GotKeyboardFocus += (_, _) => ActivateOverlay();
        TxtContent.PreviewStylusUp += (_, _) =>
        {
            ActivateOverlay();
            TxtContent.Focus();
            Keyboard.Focus(TxtContent);
            TouchKeyboardHelper.ShowTouchKeyboard(new TouchKeyboardHelper.VisualElementTarget(TxtContent));
        };
        PreviewMouseDown += (_, _) => ActivateOverlay();
        PreviewStylusDown += (_, _) => ActivateOverlay();

        // Drag handlers (Mouse + Stylus)
        DragHandle.MouseDown += DragHandle_MouseDown;
        DragHandle.MouseMove += DragHandle_MouseMove;
        DragHandle.MouseUp += DragHandle_MouseUp;
        DragHandle.LostMouseCapture += (_, _) => _isDragging = false;

        DragHandle.StylusDown += DragHandle_StylusDown;
        DragHandle.StylusMove += DragHandle_StylusMove;
        DragHandle.StylusUp += DragHandle_StylusUp;
        DragHandle.LostStylusCapture += (_, _) => _isDragging = false;

        // Resize handlers (Mouse + Stylus)
        ResizeHandle.MouseDown += ResizeHandle_MouseDown;
        ResizeHandle.MouseMove += ResizeHandle_MouseMove;
        ResizeHandle.MouseUp += ResizeHandle_MouseUp;
        ResizeHandle.LostMouseCapture += (_, _) => _isResizing = false;

        ResizeHandle.StylusDown += ResizeHandle_StylusDown;
        ResizeHandle.StylusMove += ResizeHandle_StylusMove;
        ResizeHandle.StylusUp += ResizeHandle_StylusUp;
        ResizeHandle.LostStylusCapture += (_, _) => _isResizing = false;
    }

    public void ActivateOverlay()
    {
        if (!_isActive)
        {
            SetActive(true);
        }
        Activated?.Invoke(this);
    }

    public void FocusEditor()
    {
        ActivateOverlay();
        TxtContent.Focus();
        Keyboard.Focus(TxtContent);
        TxtContent.CaretIndex = TxtContent.Text?.Length ?? 0;
    }

    public void InsertSymbolAtCaret(string symbol)
    {
        if (string.IsNullOrEmpty(symbol)) return;
        DataStore.Instance.PushUndoState();
        int selStart = TxtContent.SelectionStart;
        int selLen = TxtContent.SelectionLength;
        string current = TxtContent.Text ?? string.Empty;
        string updated = current.Remove(selStart, selLen).Insert(selStart, symbol);
        TxtContent.Text = updated;
        TxtContent.CaretIndex = selStart + symbol.Length;
        TxtContent.Focus();
        Keyboard.Focus(TxtContent);
    }

    public void MoveBy(double deltaX, double deltaY)
    {
        ApplyOverlayMove(deltaX, deltaY, Parent as Canvas);
    }

    private void ApplyOverlayMove(double deltaX, double deltaY, Canvas? parentCanvas)
    {
        if (Model.IsPinnedToScreen)
        {
            double maxX = parentCanvas != null && parentCanvas.ActualWidth > 60
                ? Math.Max(0, parentCanvas.ActualWidth - Math.Max(80, NoteBorder.ActualWidth))
                : 4000;
            double maxY = parentCanvas != null && parentCanvas.ActualHeight > 60
                ? Math.Max(0, parentCanvas.ActualHeight - Math.Max(48, NoteBorder.ActualHeight))
                : 4000;
            Model.PinnedScreenX = Math.Clamp(Model.PinnedScreenX + deltaX, 0, maxX);
            Model.PinnedScreenY = Math.Clamp(Model.PinnedScreenY + deltaY, 0, maxY);
            Canvas.SetLeft(this, Model.PinnedScreenX);
            Canvas.SetTop(this, Model.PinnedScreenY);
        }
        else
        {
            Model.X = Math.Max(0, Model.X + deltaX);
            Model.Y = Math.Max(0, Model.Y + deltaY);
            Canvas.SetLeft(this, Model.X);
            Canvas.SetTop(this, Model.Y);
        }
    }

    public void SetActive(bool active)
    {
        _isActive = active;
        HeaderBar.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        ResizeHandle.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        RefreshPinAppearance();
        ApplySurfaceAppearance();
    }

    private void RefreshPinAppearance()
    {
        bool pinned = Model.IsPinnedToScreen;
        var loc = LocalizationService.Instance;
        BtnPin.ToolTip = loc.Get(pinned ? "UnpinFromScreen" : "PinToScreen");
        TxtPinLabel.Text = loc.CurrentLanguage == AppLanguage.Polish
            ? (pinned ? "Przypięta" : "Przypnij")
            : (pinned ? "Pinned" : "Pin");
        TxtPinnedBadge.Text = loc.CurrentLanguage == AppLanguage.Polish ? "Ekran" : "Pinned";
        PinnedBadge.ToolTip = loc.Get("StickyPinnedTip");
        PinnedBadge.Visibility = (pinned && !_isActive) ? Visibility.Visible : Visibility.Collapsed;

        if (pinned)
        {
            BtnPin.Background = new SolidColorBrush(Color.FromRgb(124, 58, 237));
            TxtPinLabel.Foreground = Brushes.White;
            PinBtnIcon.Fill = Brushes.White;
        }
        else
        {
            BtnPin.ClearValue(Control.BackgroundProperty);
            TxtPinLabel.Foreground = new SolidColorBrush(Color.FromRgb(30, 41, 59));
            PinBtnIcon.Fill = new SolidColorBrush(Color.FromRgb(71, 85, 105));
        }
    }

    public void ApplyFormattingUpdate()
    {
        try
        {
            TxtContent.FontFamily = new FontFamily(string.IsNullOrWhiteSpace(Model.FontFamily) ? "Segoe UI" : Model.FontFamily);
        }
        catch
        {
            TxtContent.FontFamily = new FontFamily("Segoe UI");
        }

        TxtContent.FontWeight = Model.IsBold ? FontWeights.Bold : FontWeights.Normal;
        TxtContent.FontStyle = Model.IsItalic ? FontStyles.Italic : FontStyles.Normal;

        var decorations = new TextDecorationCollection();
        if (Model.IsUnderline) decorations.Add(TextDecorations.Underline);
        if (Model.IsStrikethrough) decorations.Add(TextDecorations.Strikethrough);
        TxtContent.TextDecorations = decorations.Count > 0 ? decorations : null;

        TxtContent.FontSize = Math.Clamp(Model.FontSize, 10, 72);

        TxtContent.TextAlignment = Model.TextAlignment switch
        {
            "Center" => TextAlignment.Center,
            "Right" => TextAlignment.Right,
            "Justify" => TextAlignment.Justify,
            _ => TextAlignment.Left
        };

        try
        {
            TxtContent.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(Model.TextColor));
        }
        catch
        {
            TxtContent.Foreground = Brushes.Black;
        }

        ApplySurfaceAppearance();
        ModelFormatted?.Invoke(Model);
    }

    private void ApplySurfaceAppearance()
    {
        bool hasCustomBg = !string.IsNullOrWhiteSpace(Model.BackgroundColor) &&
                           !string.Equals(Model.BackgroundColor, "Transparent", StringComparison.OrdinalIgnoreCase);

        if (!Model.IsStickyNote && string.Equals(Model.BackgroundColor, "#FEF08A", StringComparison.OrdinalIgnoreCase))
        {
            hasCustomBg = false;
        }

        if (hasCustomBg)
        {
            try
            {
                var bg = (Color)ColorConverter.ConvertFromString(Model.BackgroundColor);
                NoteBorder.Background = new SolidColorBrush(bg);
                NoteBorder.Effect = Model.IsPinnedToScreen
                    ? new DropShadowEffect { Color = Colors.Black, BlurRadius = 16, ShadowDepth = 4, Opacity = 0.22 }
                    : null;

                if (_isActive)
                {
                    NoteBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(124, 58, 237));
                    NoteBorder.BorderThickness = new Thickness(1.5);
                }
                else
                {
                    NoteBorder.BorderBrush = Model.IsPinnedToScreen
                        ? new SolidColorBrush(Color.FromRgb(167, 139, 250))
                        : new SolidColorBrush(Color.FromRgb(
                            (byte)Math.Max(0, bg.R - 30),
                            (byte)Math.Max(0, bg.G - 30),
                            (byte)Math.Max(0, bg.B - 30)));
                    NoteBorder.BorderThickness = new Thickness(Model.IsPinnedToScreen ? 1.25 : 1);
                }
                return;
            }
            catch { }
        }

        // Plain floating text box without solid background (or pinned floating box)
        NoteBorder.Effect = Model.IsPinnedToScreen
            ? new DropShadowEffect { Color = Colors.Black, BlurRadius = 14, ShadowDepth = 3, Opacity = 0.18 }
            : null;
        if (_isActive)
        {
            NoteBorder.Background = new SolidColorBrush(Color.FromArgb(24, 255, 255, 255));
            NoteBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(124, 58, 237));
            NoteBorder.BorderThickness = new Thickness(1.5);
        }
        else if (Model.IsPinnedToScreen)
        {
            NoteBorder.SetResourceReference(Border.BackgroundProperty, "CardBackgroundBrush");
            NoteBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(167, 139, 250));
            NoteBorder.BorderThickness = new Thickness(1.25);
        }
        else
        {
            NoteBorder.Background = Brushes.Transparent;
            NoteBorder.BorderBrush = Brushes.Transparent;
            NoteBorder.BorderThickness = new Thickness(0);
        }
    }

    public void ToggleBulletList()
    {
        DataStore.Instance.PushUndoState();
        string text = TxtContent.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            TxtContent.Text = "• ";
            TxtContent.CaretIndex = TxtContent.Text.Length;
            return;
        }

        var lines = text.Replace("\r\n", "\n").Split('\n');
        bool allBulleted = true;
        foreach (var line in lines)
        {
            if (!string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith("• "))
            {
                allBulleted = false;
                break;
            }
        }

        for (int i = 0; i < lines.Length; i++)
        {
            if (allBulleted)
            {
                if (lines[i].StartsWith("• ")) lines[i] = lines[i][2..];
            }
            else if (!string.IsNullOrWhiteSpace(lines[i]) && !lines[i].TrimStart().StartsWith("• "))
            {
                lines[i] = "• " + lines[i];
            }
        }

        TxtContent.Text = string.Join(Environment.NewLine, lines);
        TxtContent.CaretIndex = TxtContent.Text.Length;
    }

    public void ClearFormatting()
    {
        DataStore.Instance.PushUndoState();
        Model.FontFamily = "Segoe UI";
        Model.FontSize = 15.0;
        Model.IsBold = false;
        Model.IsItalic = false;
        Model.IsUnderline = false;
        Model.IsStrikethrough = false;
        Model.TextAlignment = "Left";
        Model.TextColor = "#111827";
        ApplyFormattingUpdate();
        DataStore.Instance.SaveToDisk();
    }

    private void TriggerPinToggle()
    {
        long now = Environment.TickCount64;
        if (now - _lastPinToggleTicks < 250) return;
        _lastPinToggleTicks = now;
        PinToggled?.Invoke(this);
    }

    private void BtnPin_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        TriggerPinToggle();
    }

    private void BtnPin_PreviewStylusDown(object sender, StylusDownEventArgs e)
    {
        BtnPin.CaptureStylus();
        e.Handled = true;
    }

    private void BtnPin_PreviewStylusUp(object sender, StylusEventArgs e)
    {
        if (BtnPin.IsStylusCaptured)
        {
            BtnPin.ReleaseStylusCapture();
        }
        e.Handled = true;
        TriggerPinToggle();
    }

    private void TriggerDelete()
    {
        if (_deleteTriggered) return;
        _deleteTriggered = true;
        Deleted?.Invoke(Model);
    }

    private void BtnDelete_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        TriggerDelete();
    }

    private void BtnDelete_PreviewStylusDown(object sender, StylusDownEventArgs e)
    {
        BtnDelete.CaptureStylus();
        e.Handled = true;
    }

    private void BtnDelete_PreviewStylusUp(object sender, StylusEventArgs e)
    {
        if (BtnDelete.IsStylusCaptured)
        {
            BtnDelete.ReleaseStylusCapture();
        }
        e.Handled = true;
        TriggerDelete();
    }

    #region Dragging (Stylus & Mouse)

    private void DragHandle_StylusDown(object sender, StylusDownEventArgs e)
    {
        ActivateOverlay();
        if (Parent is Canvas parentCanvas)
        {
            DataStore.Instance.PushUndoState();
            _isDragging = true;
            _dragStart = e.GetPosition(parentCanvas);
            DragHandle.CaptureStylus();
            e.Handled = true;
        }
    }

    private void DragHandle_StylusMove(object sender, StylusEventArgs e)
    {
        if (_isDragging && Parent is Canvas parentCanvas)
        {
            Point current = e.GetPosition(parentCanvas);
            ApplyOverlayMove(current.X - _dragStart.X, current.Y - _dragStart.Y, parentCanvas);
            _dragStart = current;
            e.Handled = true;
        }
    }

    private void DragHandle_StylusUp(object sender, StylusEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            DragHandle.ReleaseStylusCapture();
            DataStore.Instance.SaveToDisk();
            e.Handled = true;
        }
    }

    private void DragHandle_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.StylusDevice != null) return;
        ActivateOverlay();
        if (e.LeftButton == MouseButtonState.Pressed && Parent is Canvas parentCanvas)
        {
            DataStore.Instance.PushUndoState();
            _isDragging = true;
            _dragStart = e.GetPosition(parentCanvas);
            DragHandle.CaptureMouse();
            e.Handled = true;
        }
    }

    private void DragHandle_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.StylusDevice != null) return;
        if (_isDragging && Parent is Canvas parentCanvas)
        {
            Point current = e.GetPosition(parentCanvas);
            ApplyOverlayMove(current.X - _dragStart.X, current.Y - _dragStart.Y, parentCanvas);
            _dragStart = current;
        }
    }

    private void DragHandle_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.StylusDevice != null) return;
        if (_isDragging)
        {
            _isDragging = false;
            DragHandle.ReleaseMouseCapture();
            DataStore.Instance.SaveToDisk();
        }
    }

    #endregion

    #region Resizing (Stylus & Mouse)

    private void ResizeHandle_StylusDown(object sender, StylusDownEventArgs e)
    {
        if (Parent is Canvas parentCanvas)
        {
            DataStore.Instance.PushUndoState();
            _isResizing = true;
            _resizeStart = e.GetPosition(parentCanvas);
            _startW = NoteBorder.ActualWidth > 0 ? NoteBorder.ActualWidth : Model.Width;
            _startH = NoteBorder.ActualHeight > 0 ? NoteBorder.ActualHeight : Model.Height;
            ResizeHandle.CaptureStylus();
            e.Handled = true;
        }
    }

    private void ResizeHandle_StylusMove(object sender, StylusEventArgs e)
    {
        if (_isResizing && Parent is Canvas parentCanvas)
        {
            ApplyResizeDelta(e.GetPosition(parentCanvas));
            e.Handled = true;
        }
    }

    private void ResizeHandle_StylusUp(object sender, StylusEventArgs e)
    {
        if (_isResizing)
        {
            _isResizing = false;
            ResizeHandle.ReleaseStylusCapture();
            DataStore.Instance.SaveToDisk();
            e.Handled = true;
        }
    }

    private void ResizeHandle_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.StylusDevice != null) return;
        if (e.LeftButton == MouseButtonState.Pressed && Parent is Canvas parentCanvas)
        {
            DataStore.Instance.PushUndoState();
            _isResizing = true;
            _resizeStart = e.GetPosition(parentCanvas);
            _startW = NoteBorder.ActualWidth > 0 ? NoteBorder.ActualWidth : Model.Width;
            _startH = NoteBorder.ActualHeight > 0 ? NoteBorder.ActualHeight : Model.Height;
            ResizeHandle.CaptureMouse();
            e.Handled = true;
        }
    }

    private void ResizeHandle_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.StylusDevice != null) return;
        if (_isResizing && Parent is Canvas parentCanvas)
        {
            ApplyResizeDelta(e.GetPosition(parentCanvas));
        }
    }

    private void ResizeHandle_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.StylusDevice != null) return;
        if (_isResizing)
        {
            _isResizing = false;
            ResizeHandle.ReleaseMouseCapture();
            DataStore.Instance.SaveToDisk();
        }
    }

    private void ApplyResizeDelta(Point current)
    {
        double newW = Math.Max(160, _startW + (current.X - _resizeStart.X));
        double newH = Math.Max(64, _startH + (current.Y - _resizeStart.Y));
        Model.Width = newW;
        Model.Height = newH;
        NoteBorder.Width = newW;
        NoteBorder.MinHeight = newH;
    }

    #endregion
}
