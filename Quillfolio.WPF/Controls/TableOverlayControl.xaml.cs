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

public partial class TableOverlayControl : UserControl
{
    public DynamicTable TableModel { get; private set; }
    private Point _dragStart;
    private bool _isDragging;
    private bool _isActive;
    private bool _deleteTriggered;
    private int _selectedRow;
    private int _selectedCol;

    public bool IsActive => _isActive;

    public event Action? TableChanged;
    public event Action<DynamicTable>? TableDeleted;
    public event Action<TableOverlayControl>? Activated;

    public TableOverlayControl(DynamicTable tableModel)
    {
        InitializeComponent();
        TableModel = tableModel;
        BuildCellColorSwatches();
        RebuildGrid();
        SetActive(false);

        PreviewMouseDown += (_, _) => ActivateOverlay();
        PreviewStylusDown += (_, _) => ActivateOverlay();

        DragHandle.MouseDown += DragHandle_MouseDown;
        DragHandle.MouseMove += DragHandle_MouseMove;
        DragHandle.MouseUp += DragHandle_MouseUp;
        DragHandle.LostMouseCapture += (_, _) => _isDragging = false;

        DragHandle.StylusDown += DragHandle_StylusDown;
        DragHandle.StylusMove += DragHandle_StylusMove;
        DragHandle.StylusUp += DragHandle_StylusUp;
        DragHandle.LostStylusCapture += (_, _) => _isDragging = false;

        UpdateLocalization();
        LocalizationService.Instance.LanguageChanged += UpdateLocalization;
        Unloaded += (s, e) => LocalizationService.Instance.LanguageChanged -= UpdateLocalization;
    }

    public void ActivateOverlay()
    {
        if (!_isActive)
        {
            SetActive(true);
        }
        Activated?.Invoke(this);
    }

    public void SetActive(bool active)
    {
        _isActive = active;
        ActionBar.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        if (active)
        {
            CardBorder.SetResourceReference(Border.BackgroundProperty, "CardBackgroundBrush");
            CardBorder.SetResourceReference(Border.BorderBrushProperty, "PrimaryBrush");
            CardBorder.BorderThickness = new Thickness(1.5);
            CardBorder.Padding = new Thickness(10);
            CardBorder.Effect = null;
        }
        else
        {
            CardBorder.Background = Brushes.Transparent;
            CardBorder.BorderBrush = Brushes.Transparent;
            CardBorder.BorderThickness = new Thickness(0);
            CardBorder.Padding = new Thickness(0);
            CardBorder.Effect = null;
        }
        RefreshCellSelectionHighlights();
    }

    private void UpdateLocalization()
    {
        var loc = LocalizationService.Instance;
        BtnAddRow.Content = loc.Get("AddRow");
        BtnDelRow.Content = loc.Get("RemoveRow");
        BtnAddCol.Content = loc.Get("AddCol");
        BtnDelCol.Content = loc.Get("RemoveCol");
        DragHandle.ToolTip = loc.Get("DragTable");
        BtnDeleteTable.ToolTip = loc.Get("DeleteItem");
    }

    public void RebuildGrid()
    {
        TableGrid.RowDefinitions.Clear();
        TableGrid.ColumnDefinitions.Clear();
        TableGrid.Children.Clear();

        for (int r = 0; r < TableModel.RowCount; r++)
        {
            TableGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(TableModel.CellHeight) });
        }

        for (int c = 0; c < TableModel.ColCount; c++)
        {
            TableGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(TableModel.CellWidth) });
        }

        for (int r = 0; r < TableModel.RowCount; r++)
        {
            for (int c = 0; c < TableModel.ColCount; c++)
            {
                var cellModel = TableModel.Cells[r][c];

                var border = new Border
                {
                    BorderThickness = new Thickness(0.5),
                    Background = TryParseBrush(cellModel.BackgroundColor, Brushes.White),
                    Tag = (r, c)
                };
                border.SetResourceReference(Border.BorderBrushProperty, "SubtleBorderBrush");

                var textBox = new TextBox
                {
                    Text = cellModel.Text,
                    FontWeight = cellModel.IsBold ? FontWeights.Bold : FontWeights.Normal,
                    Foreground = TryParseBrush(cellModel.TextColor, Brushes.Black),
                    CaretBrush = TryParseBrush(cellModel.TextColor, Brushes.Black),
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    Padding = new Thickness(6, 4, 6, 4),
                    FontSize = cellModel.IsHeader ? 13 : 12
                };
                textBox.SetResourceReference(TextBox.SelectionBrushProperty, "PrimaryBrush");

                int rowCapture = r;
                int colCapture = c;
                textBox.GotKeyboardFocus += (_, _) =>
                {
                    _selectedRow = rowCapture;
                    _selectedCol = colCapture;
                    ActivateOverlay();
                    RefreshCellSelectionHighlights();
                };
                textBox.PreviewMouseDown += (_, _) =>
                {
                    _selectedRow = rowCapture;
                    _selectedCol = colCapture;
                    RefreshCellSelectionHighlights();
                };
                textBox.PreviewStylusUp += (_, _) =>
                {
                    _selectedRow = rowCapture;
                    _selectedCol = colCapture;
                    ActivateOverlay();
                    RefreshCellSelectionHighlights();
                    textBox.Focus();
                    Keyboard.Focus(textBox);
                    TouchKeyboardHelper.ShowTouchKeyboard(new TouchKeyboardHelper.VisualElementTarget(textBox));
                };
                textBox.TextChanged += (s, e) =>
                {
                    TableModel.Cells[rowCapture][colCapture].Text = textBox.Text;
                    DataStore.Instance.SaveToDisk();
                };

                var cellMenu = new ContextMenu();
                var loc = LocalizationService.Instance;
                var miBg = new MenuItem { Header = loc.Get("TableMenuBg") };
                miBg.Click += (_, _) => { _selectedRow = rowCapture; _selectedCol = colCapture; BtnCustomCellBg_Click(this, new RoutedEventArgs()); };
                var miFg = new MenuItem { Header = loc.Get("TableMenuText") };
                miFg.Click += (_, _) => { _selectedRow = rowCapture; _selectedCol = colCapture; BtnCustomCellText_Click(this, new RoutedEventArgs()); };
                var miReset = new MenuItem { Header = loc.Get("TableMenuReset") };
                miReset.Click += (_, _) =>
                {
                    _selectedRow = rowCapture;
                    _selectedCol = colCapture;
                    bool isHdr = rowCapture == 0;
                    ApplyCellBackground(isHdr ? TableModel.HeaderColor : (rowCapture % 2 == 1 ? "#F3F4F6" : "#FFFFFF"));
                    ApplyCellTextColor(isHdr ? "#F9FAFB" : "#111827");
                };
                cellMenu.Items.Add(miBg);
                cellMenu.Items.Add(miFg);
                cellMenu.Items.Add(new Separator());
                cellMenu.Items.Add(miReset);
                textBox.ContextMenu = cellMenu;

                border.Child = textBox;
                Grid.SetRow(border, r);
                Grid.SetColumn(border, c);
                TableGrid.Children.Add(border);
            }
        }

        RefreshCellSelectionHighlights();
    }

    private static Brush TryParseBrush(string? hex, Brush fallback)
    {
        if (string.IsNullOrWhiteSpace(hex) || string.Equals(hex, "Transparent", StringComparison.OrdinalIgnoreCase))
            return Brushes.Transparent;
        try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
        catch { return fallback; }
    }

    private void RefreshCellSelectionHighlights()
    {
        foreach (UIElement child in TableGrid.Children)
        {
            if (child is Border b && b.Tag is ValueTuple<int, int> pos)
            {
                bool isSel = _isActive && pos.Item1 == _selectedRow && pos.Item2 == _selectedCol;
                b.SetResourceReference(Border.BorderBrushProperty, isSel ? "PrimaryBrush" : "SubtleBorderBrush");
                b.BorderThickness = isSel ? new Thickness(1.6) : new Thickness(0.5);
            }
        }
    }

    private void BuildCellColorSwatches()
    {
        CellBgSwatches.Children.Clear();
        string[] bgColors = { "#FFFFFF", "#F3F4F6", "#1F2937", "#FEF08A", "#EDE9FE", "#DBEAFE", "#DCFCE7", "#FCE7F3", "#FEE2E2" };
        foreach (string hex in bgColors)
        {
            string captured = hex;
            var btn = CreateMiniSwatch(captured, () =>
            {
                ApplyCellBackground(captured);
                PopupCellColor.IsOpen = false;
            });
            CellBgSwatches.Children.Add(btn);
        }

        CellTextSwatches.Children.Clear();
        string[] fgColors = { "#111827", "#FFFFFF", "#7C3AED", "#2563EB", "#DC2626", "#16A34A", "#D97706" };
        foreach (string hex in fgColors)
        {
            string captured = hex;
            var btn = CreateMiniSwatch(captured, () =>
            {
                ApplyCellTextColor(captured);
                PopupCellColor.IsOpen = false;
            });
            CellTextSwatches.Children.Add(btn);
        }
    }

    private static Button CreateMiniSwatch(string hex, Action onClick)
    {
        var btn = new Button
        {
            Width = 20,
            Height = 20,
            Margin = new Thickness(2),
            Focusable = false,
            Cursor = Cursors.Hand,
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)),
            BorderThickness = new Thickness(1)
        };
        btn.SetResourceReference(Control.BorderBrushProperty, "SubtleBorderBrush");
        var tpl = new ControlTemplate(typeof(Button));
        var bd = new FrameworkElementFactory(typeof(Border));
        bd.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
        bd.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Button.BorderBrushProperty));
        bd.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Button.BorderThicknessProperty));
        bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
        tpl.VisualTree = bd;
        btn.Template = tpl;
        btn.Click += (_, _) => onClick();
        return btn;
    }

    private void BtnCellColor_Click(object sender, RoutedEventArgs e)
    {
        PopupCellColor.IsOpen = !PopupCellColor.IsOpen;
    }

    private void BtnCustomCellBg_Click(object sender, RoutedEventArgs e)
    {
        PopupCellColor.IsOpen = false;
        string current = GetCurrentCell()?.BackgroundColor ?? "#FFFFFF";
        var dlg = new Quillfolio.WPF.Views.ColorPickerWindow(current) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true)
        {
            ApplyCellBackground(dlg.SelectedHex);
        }
    }

    private void BtnCustomCellText_Click(object sender, RoutedEventArgs e)
    {
        PopupCellColor.IsOpen = false;
        string current = GetCurrentCell()?.TextColor ?? "#111827";
        var dlg = new Quillfolio.WPF.Views.ColorPickerWindow(current) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true)
        {
            ApplyCellTextColor(dlg.SelectedHex);
        }
    }

    private TableCell? GetCurrentCell()
    {
        if (_selectedRow >= 0 && _selectedRow < TableModel.Cells.Count &&
            _selectedCol >= 0 && _selectedCol < TableModel.Cells[_selectedRow].Count)
        {
            return TableModel.Cells[_selectedRow][_selectedCol];
        }
        return null;
    }

    private void ApplyCellBackground(string hex)
    {
        var cell = GetCurrentCell();
        if (cell == null) return;
        DataStore.Instance.PushUndoState();
        cell.BackgroundColor = hex;
        RebuildGrid();
        DataStore.Instance.SaveToDisk();
    }

    private void ApplyCellTextColor(string hex)
    {
        var cell = GetCurrentCell();
        if (cell == null) return;
        DataStore.Instance.PushUndoState();
        cell.TextColor = hex;
        RebuildGrid();
        DataStore.Instance.SaveToDisk();
    }

    private void BtnAddRow_Click(object sender, RoutedEventArgs e)
    {
        DataStore.Instance.PushUndoState();
        TableModel.AddRow();
        RebuildGrid();
        TableChanged?.Invoke();
    }

    private void BtnDelRow_Click(object sender, RoutedEventArgs e)
    {
        if (TableModel.RowCount > 1)
        {
            DataStore.Instance.PushUndoState();
            TableModel.RemoveRow();
            RebuildGrid();
            TableChanged?.Invoke();
        }
    }

    private void BtnAddCol_Click(object sender, RoutedEventArgs e)
    {
        DataStore.Instance.PushUndoState();
        TableModel.AddColumn();
        RebuildGrid();
        TableChanged?.Invoke();
    }

    private void BtnDelCol_Click(object sender, RoutedEventArgs e)
    {
        if (TableModel.ColCount > 1)
        {
            DataStore.Instance.PushUndoState();
            TableModel.RemoveColumn();
            RebuildGrid();
            TableChanged?.Invoke();
        }
    }

    private void TriggerDelete()
    {
        if (_deleteTriggered) return;
        _deleteTriggered = true;
        TableDeleted?.Invoke(TableModel);
    }

    private void BtnDeleteTable_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        TriggerDelete();
    }

    private void BtnDeleteTable_PreviewStylusDown(object sender, StylusDownEventArgs e)
    {
        BtnDeleteTable.CaptureStylus();
        e.Handled = true;
    }

    private void BtnDeleteTable_PreviewStylusUp(object sender, StylusEventArgs e)
    {
        if (BtnDeleteTable.IsStylusCaptured)
        {
            BtnDeleteTable.ReleaseStylusCapture();
        }
        e.Handled = true;
        TriggerDelete();
    }

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
            MoveTableBy(current.X - _dragStart.X, current.Y - _dragStart.Y);
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
            MoveTableBy(current.X - _dragStart.X, current.Y - _dragStart.Y);
            _dragStart = current;
        }
    }

    public void MoveBy(double deltaX, double deltaY)
    {
        MoveTableBy(deltaX, deltaY);
    }

    private void MoveTableBy(double deltaX, double deltaY)
    {
        TableModel.X = Math.Max(0, TableModel.X + deltaX);
        TableModel.Y = Math.Max(0, TableModel.Y + deltaY);
        Canvas.SetLeft(this, TableModel.X);
        Canvas.SetTop(this, TableModel.Y);
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
}
