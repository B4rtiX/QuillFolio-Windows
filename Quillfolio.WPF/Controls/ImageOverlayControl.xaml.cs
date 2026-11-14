using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Quillfolio.Core.Models;
using Quillfolio.Core.Services;

namespace Quillfolio.WPF.Controls;

public partial class ImageOverlayControl : UserControl
{
    public CanvasImage Model { get; }
    private Point _dragStart;
    private bool _isDragging;
    private bool _isResizing;
    private Point _resizeStart;
    private double _startW;
    private double _startH;
    private double _aspectRatio = 1.0;
    private bool _isActive;
    private bool _deleteTriggered;

    public bool IsActive => _isActive;

    public event Action<CanvasImage>? Deleted;
    public event Action<ImageOverlayControl>? Activated;

    public ImageOverlayControl(CanvasImage model)
    {
        InitializeComponent();
        Model = model;
        Width = Math.Max(40, model.Width);
        Height = Math.Max(40, model.Height);
        if (Height > 0) _aspectRatio = Width / Height;

        LoadBitmap();
        SetActive(false);

        PreviewMouseDown += (_, _) => ActivateOverlay();
        PreviewStylusDown += (_, _) => ActivateOverlay();

        // Allow dragging directly from DragHandle OR body
        DragHandle.MouseDown += DragStart_MouseDown;
        DragHandle.MouseMove += Drag_MouseMove;
        DragHandle.MouseUp += Drag_MouseUp;
        DragHandle.LostMouseCapture += (_, _) => _isDragging = false;

        DragHandle.StylusDown += DragStart_StylusDown;
        DragHandle.StylusMove += Drag_StylusMove;
        DragHandle.StylusUp += Drag_StylusUp;
        DragHandle.LostStylusCapture += (_, _) => _isDragging = false;

        ImgContent.MouseLeftButtonDown += DragStart_MouseDown;
        ImgContent.MouseMove += Drag_MouseMove;
        ImgContent.MouseLeftButtonUp += Drag_MouseUp;
        ImgContent.LostMouseCapture += (_, _) => _isDragging = false;

        ImgContent.StylusDown += DragStart_StylusDown;
        ImgContent.StylusMove += Drag_StylusMove;
        ImgContent.StylusUp += Drag_StylusUp;
        ImgContent.LostStylusCapture += (_, _) => _isDragging = false;

        // Resize handle
        ResizeHandle.MouseDown += ResizeHandle_MouseDown;
        ResizeHandle.MouseMove += ResizeHandle_MouseMove;
        ResizeHandle.MouseUp += ResizeHandle_MouseUp;
        ResizeHandle.LostMouseCapture += (_, _) => _isResizing = false;

        ResizeHandle.StylusDown += ResizeHandle_StylusDown;
        ResizeHandle.StylusMove += ResizeHandle_StylusMove;
        ResizeHandle.StylusUp += ResizeHandle_StylusUp;
        ResizeHandle.LostStylusCapture += (_, _) => _isResizing = false;
    }

    private void LoadBitmap()
    {
        if (string.IsNullOrWhiteSpace(Model.ImageBase64)) return;
        try
        {
            byte[] bytes = Convert.FromBase64String(Model.ImageBase64);
            using var ms = new MemoryStream(bytes);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            ImgContent.Source = bmp;
        }
        catch { }
    }

    public void ActivateOverlay()
    {
        if (!_isActive) SetActive(true);
        Activated?.Invoke(this);
    }

    public void SetActive(bool active)
    {
        _isActive = active;
        HeaderBar.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        ResizeHandle.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        FrameBorder.BorderBrush = active
            ? new SolidColorBrush(Color.FromRgb(124, 58, 237))
            : Brushes.Transparent;
        FrameBorder.BorderThickness = active ? new Thickness(2) : new Thickness(0);
    }

    public void MoveBy(double dx, double dy)
    {
        Model.X = Math.Max(0, Model.X + dx);
        Model.Y = Math.Max(0, Model.Y + dy);
        Canvas.SetLeft(this, Model.X);
        Canvas.SetTop(this, Model.Y);
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
        if (BtnDelete.IsStylusCaptured) BtnDelete.ReleaseStylusCapture();
        e.Handled = true;
        TriggerDelete();
    }

    private void DragStart_StylusDown(object sender, StylusDownEventArgs e)
    {
        ActivateOverlay();
        if (Parent is Canvas parentCanvas && sender is UIElement el)
        {
            DataStore.Instance.PushUndoState();
            _isDragging = true;
            _dragStart = e.GetPosition(parentCanvas);
            el.CaptureStylus();
            e.Handled = true;
        }
    }

    private void Drag_StylusMove(object sender, StylusEventArgs e)
    {
        if (_isDragging && Parent is Canvas parentCanvas)
        {
            Point curr = e.GetPosition(parentCanvas);
            MoveBy(curr.X - _dragStart.X, curr.Y - _dragStart.Y);
            _dragStart = curr;
            e.Handled = true;
        }
    }

    private void Drag_StylusUp(object sender, StylusEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            if (sender is UIElement el && el.IsStylusCaptured) el.ReleaseStylusCapture();
            DataStore.Instance.SaveToDisk();
            e.Handled = true;
        }
    }

    private void DragStart_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.StylusDevice != null) return;
        ActivateOverlay();
        if (e.LeftButton == MouseButtonState.Pressed && Parent is Canvas parentCanvas && sender is UIElement el)
        {
            DataStore.Instance.PushUndoState();
            _isDragging = true;
            _dragStart = e.GetPosition(parentCanvas);
            el.CaptureMouse();
            e.Handled = true;
        }
    }

    private void Drag_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.StylusDevice != null) return;
        if (_isDragging && Parent is Canvas parentCanvas)
        {
            Point curr = e.GetPosition(parentCanvas);
            MoveBy(curr.X - _dragStart.X, curr.Y - _dragStart.Y);
            _dragStart = curr;
        }
    }

    private void Drag_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.StylusDevice != null) return;
        if (_isDragging)
        {
            _isDragging = false;
            if (sender is UIElement el && el.IsMouseCaptured) el.ReleaseMouseCapture();
            DataStore.Instance.SaveToDisk();
        }
    }

    private void ResizeHandle_StylusDown(object sender, StylusDownEventArgs e)
    {
        ActivateOverlay();
        if (Parent is Canvas parentCanvas)
        {
            DataStore.Instance.PushUndoState();
            _isResizing = true;
            _resizeStart = e.GetPosition(parentCanvas);
            _startW = Width;
            _startH = Height;
            ResizeHandle.CaptureStylus();
            e.Handled = true;
        }
    }

    private void ResizeHandle_StylusMove(object sender, StylusEventArgs e)
    {
        if (_isResizing && Parent is Canvas parentCanvas)
        {
            Point curr = e.GetPosition(parentCanvas);
            ApplyResize(curr.X - _resizeStart.X, curr.Y - _resizeStart.Y, keepRatio: true);
            e.Handled = true;
        }
    }

    private void ResizeHandle_StylusUp(object sender, StylusEventArgs e)
    {
        if (_isResizing)
        {
            _isResizing = false;
            if (ResizeHandle.IsStylusCaptured) ResizeHandle.ReleaseStylusCapture();
            DataStore.Instance.SaveToDisk();
            e.Handled = true;
        }
    }

    private void ResizeHandle_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.StylusDevice != null) return;
        ActivateOverlay();
        if (e.LeftButton == MouseButtonState.Pressed && Parent is Canvas parentCanvas)
        {
            DataStore.Instance.PushUndoState();
            _isResizing = true;
            _resizeStart = e.GetPosition(parentCanvas);
            _startW = Width;
            _startH = Height;
            ResizeHandle.CaptureMouse();
            e.Handled = true;
        }
    }

    private void ResizeHandle_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.StylusDevice != null) return;
        if (_isResizing && Parent is Canvas parentCanvas)
        {
            Point curr = e.GetPosition(parentCanvas);
            bool freeform = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            ApplyResize(curr.X - _resizeStart.X, curr.Y - _resizeStart.Y, !freeform);
        }
    }

    private void ResizeHandle_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.StylusDevice != null) return;
        if (_isResizing)
        {
            _isResizing = false;
            if (ResizeHandle.IsMouseCaptured) ResizeHandle.ReleaseMouseCapture();
            DataStore.Instance.SaveToDisk();
        }
    }

    private void ApplyResize(double dx, double dy, bool keepRatio)
    {
        double newW = Math.Max(48.0, _startW + dx);
        double newH = keepRatio && _aspectRatio > 0.05
            ? Math.Max(48.0, newW / _aspectRatio)
            : Math.Max(48.0, _startH + dy);
        Width = newW;
        Height = newH;
        Model.Width = newW;
        Model.Height = newH;
    }
}
