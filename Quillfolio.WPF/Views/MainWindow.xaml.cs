/*
 ╭───────────────────────────────────────────────────────────────────╮
 │   ██████╗     ██╗  ██╗    ██████╗   ████████╗  ██╗  ██╗  ██╗      │
 │   ██╔══██╗    ██║  ██║    ██╔══██╗  ╚══██╔══╝  ██║  ╚██╗██╔╝      │
 │   ██████╔╝    ███████║    ██████╔╝     ██║     ██║   ╚███╔╝       │
 │   ██╔══██╗    ╚════██║    ██╔══██╗     ██║     ██║   ██╔██╗       │
 │   ██████╔╝         ██║    ██║  ██║     ██║     ██║  ██╔╝ ██╗      │
 │   ╚═════╝          ╚═╝    ╚═╝  ╚═╝     ╚═╝     ╚═╝  ╚═╝  ╚═╝      │
 │                                                                   │
 │   Architect & Lead Developer: B4rtiX                              │
 │   Quillfolio (Windows .NET Edition)                               │
 ╰───────────────────────────────────────────────────────────────────╯
*/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using Quillfolio.Core.Geometry;
using Quillfolio.Core.Models;
using Quillfolio.Core.Services;
using Quillfolio.WPF.Controls;
using Quillfolio.WPF.Services;

namespace Quillfolio.WPF.Views;

public partial class MainWindow : Window
{
    private bool _isUpdatingNoteList;
    private bool _isUpdatingCategories;
    private bool _isSidebarCollapsed;
    private bool _isBottomBarCollapsed;
    private bool _isFullscreen;
    private WindowState _preFullscreenState = WindowState.Normal;
    private WindowStyle _preFullscreenStyle = WindowStyle.SingleBorderWindow;
    private ResizeMode _preFullscreenResizeMode = ResizeMode.CanResize;
    private bool _isUpdatingThickness;
    private bool _isUpdatingTextFormat;
    private RadioButton? _stylusDeselectTarget;
    private Category? _selectedCategory;
    private Subcategory? _selectedSubcategory;
    private StickyNoteOverlayControl? _activeTextOverlay;
    private Point? _noteDragStartPoint;
    private Note? _draggingNote;

    public MainWindow()
    {
        InitializeComponent();
        WindowBackdropHelper.ApplyModernWindowChrome(this);
        WindowBackdropHelper.ApplyAppTheme(DataStore.Instance.AppTheme, DataStore.Instance.DeskTheme);
        UIAnimationHelper.AttachWindowEntranceAnimation(this);

        DataStore.Instance.EnsureShortcutDefaults();
        DataStore.Instance.SelectedTool = DataStore.Instance.DefaultStartupTool;
        SelectToolRadioButton(DataStore.Instance.SelectedTool);

        BtnShapeSnap.IsChecked = DataStore.Instance.IsShapeSnapEnabled;
        SetThickness(DataStore.Instance.SelectedThickness);
        UpdateActiveColorIndicator();
        UpdateEraserModeUI();
        UpdateSelectionModeUI();
        BuildThicknessPresets();
        BuildShapesGallery();
        BuildSymbolsPopup();

        RefreshCategorySelectors();
        RefreshNotesList();
        UpdatePageBar();
        UpdateUndoRedoButtons();

        PreviewKeyDown += MainWindow_PreviewKeyDown;
        PreviewMouseDown += MainWindow_PreviewMouseDown;
        RegisterExpressKeyCommandBindings();

        DataStore.Instance.UndoStateChanged += () =>
        {
            if (Dispatcher.CheckAccess()) UpdateUndoRedoButtons();
            else Dispatcher.BeginInvoke(UpdateUndoRedoButtons);
        };

        DataStore.Instance.StateChanged += () =>
        {
            void Refresh()
            {
                RefreshCategorySelectors();
                RefreshNotesList();
                UpdatePageBar();
                UpdateUndoRedoButtons();
                if (HomeScreenOverlay.Visibility == Visibility.Visible) RefreshHomeScreen();
            }

            if (Dispatcher.CheckAccess()) Refresh();
            else Dispatcher.BeginInvoke(Refresh);
        };

        MainInkCanvas.ZoomRequested += ApplyZoom;
        MainInkCanvas.SelectionChanged += UpdateSelectionActions;
        MainInkCanvas.ActiveTextOverlayChanged += OnActiveTextOverlayChanged;
        MainInkCanvas.ShapeInsertedAndSelected += () =>
        {
            BtnSelect.IsChecked = true;
            UpdateSelectionActions();
        };

        UpdateLocalization();
        LocalizationService.Instance.LanguageChanged += () =>
        {
            UpdateLocalization();
            BuildShapesGallery();
            BuildSymbolsPopup();
            if (HomeScreenOverlay.Visibility == Visibility.Visible) RefreshHomeScreen();
        };

        DrawingToolsPanel.SizeChanged += (_, _) =>
        {
            if (IsLoaded && DataStore.Instance.SelectedTool != ToolType.None)
            {
                AnimateToolIndicator(GetRadioButtonForTool(DataStore.Instance.SelectedTool), animate: false);
            }
        };

        Loaded += (_, _) =>
        {
            AnimateToolIndicator(GetRadioButtonForTool(DataStore.Instance.SelectedTool), animate: false);
            StartLoadingSequence();
        };

        Closed += (_, _) => DataStore.Instance.FlushSaveToDisk();
    }

    private void StartLoadingSequence()
    {
        LoadingScreenOverlay.Visibility = Visibility.Visible;
        LoadingScreenOverlay.Opacity = 1;

        var spin = new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(900))
        {
            RepeatBehavior = RepeatBehavior.Forever
        };
        LoadingSpinnerRotate.BeginAnimation(RotateTransform.AngleProperty, spin);

        var progressAnim = new DoubleAnimation(0, 210, TimeSpan.FromMilliseconds(680))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };
        LoadingProgressFill.BeginAnimation(WidthProperty, progressAnim);

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(720) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            ShowHomeScreen(animate: false);
            UIAnimationHelper.AnimateFade(LoadingScreenOverlay, 1, 0, 320, () =>
            {
                LoadingScreenOverlay.Visibility = Visibility.Collapsed;
                LoadingSpinnerRotate.BeginAnimation(RotateTransform.AngleProperty, null);
            });
        };
        timer.Start();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        source?.AddHook(WndProc);
    }

    private void RegisterExpressKeyCommandBindings()
    {
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Undo, (_, _) => DataStore.Instance.Undo()));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Redo, (_, _) => DataStore.Instance.Redo()));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Copy, (_, _) =>
        {
            if (!IsTextEditorFocused()) MainInkCanvas.CopySelectedStrokes();
        }));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Paste, (_, _) =>
        {
            if (!IsTextEditorFocused()) MainInkCanvas.PasteFromClipboard();
        }));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.SelectAll, (_, _) =>
        {
            if (!IsTextEditorFocused()) MainInkCanvas.SelectAllStrokes();
        }));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Delete, (_, _) =>
        {
            if (!IsTextEditorFocused()) MainInkCanvas.DeleteActiveOverlayOrSelected();
        }));
    }

    private RadioButton? GetRadioButtonForTool(ToolType tool) => tool switch
    {
        ToolType.Cursor => BtnCursor,
        ToolType.Pen => BtnPen,
        ToolType.FountainPen => BtnFountainPen,
        ToolType.Pencil => BtnPencil,
        ToolType.Highlighter => BtnHighlighter,
        ToolType.Eraser => BtnEraser,
        ToolType.Select => BtnSelect,
        ToolType.Laser => BtnLaser,
        _ => null
    };

    private void SelectToolRadioButton(ToolType tool)
    {
        switch (tool)
        {
            case ToolType.None:
                BtnCursor.IsChecked = false;
                BtnPen.IsChecked = false;
                BtnFountainPen.IsChecked = false;
                BtnPencil.IsChecked = false;
                BtnHighlighter.IsChecked = false;
                BtnEraser.IsChecked = false;
                BtnSelect.IsChecked = false;
                BtnLaser.IsChecked = false;
                AnimateToolIndicator(null, animate: IsLoaded);
                break;
            case ToolType.Cursor: BtnCursor.IsChecked = true; break;
            case ToolType.Pen: BtnPen.IsChecked = true; break;
            case ToolType.FountainPen: BtnFountainPen.IsChecked = true; break;
            case ToolType.Pencil: BtnPencil.IsChecked = true; break;
            case ToolType.Highlighter: BtnHighlighter.IsChecked = true; break;
            case ToolType.Eraser: BtnEraser.IsChecked = true; break;
            case ToolType.Select: BtnSelect.IsChecked = true; break;
            case ToolType.Laser: BtnLaser.IsChecked = true; break;
        }
        MainInkCanvas?.UpdateToolInteractionMode();
    }

    private void AnimateToolIndicator(RadioButton? target, bool animate = true)
    {
        if (ActiveToolIndicator == null || DrawingToolsHost == null) return;

        if (target == null)
        {
            if (animate)
            {
                UIAnimationHelper.AnimateFade(ActiveToolIndicator, ActiveToolIndicator.Opacity, 0, 140);
            }
            else
            {
                ActiveToolIndicator.Opacity = 0;
            }
            return;
        }

        if (!IsLoaded || target.ActualWidth <= 0)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => AnimateToolIndicator(target, animate: false));
            return;
        }

        try
        {
            Point pos = target.TranslatePoint(new Point(0, 0), DrawingToolsHost);
            double targetWidth = target.ActualWidth;
            double targetHeight = target.ActualHeight;
            if (targetHeight > 0) ActiveToolIndicator.Height = targetHeight;

            if (!animate || ActiveToolIndicator.Opacity < 0.05 || ActiveToolIndicator.Width <= 0)
            {
                ActiveToolIndicatorTransform.BeginAnimation(TranslateTransform.XProperty, null);
                ActiveToolIndicator.BeginAnimation(WidthProperty, null);
                ActiveToolIndicatorTransform.X = pos.X;
                ActiveToolIndicator.Width = targetWidth;
                if (animate)
                {
                    UIAnimationHelper.AnimateFade(ActiveToolIndicator, 0, 1, 160);
                }
                else
                {
                    ActiveToolIndicator.Opacity = 1;
                }
            }
            else
            {
                var dur = TimeSpan.FromMilliseconds(220);
                ActiveToolIndicatorTransform.BeginAnimation(TranslateTransform.XProperty,
                    new DoubleAnimation(pos.X, dur) { EasingFunction = UIAnimationHelper.SmoothOut });
                ActiveToolIndicator.BeginAnimation(WidthProperty,
                    new DoubleAnimation(targetWidth, dur) { EasingFunction = UIAnimationHelper.SmoothOut });
                ActiveToolIndicator.BeginAnimation(OpacityProperty, null);
                ActiveToolIndicator.Opacity = 1;
            }
        }
        catch
        {
            // Ignore layout race during teardown
        }
    }

    private void BuildSymbolsPopup()
    {
        if (SymbolsCategoryHost == null) return;
        SymbolsCategoryHost.Children.Clear();

        var loc = LocalizationService.Instance;
        var groups = new (string Title, string[] Symbols)[]
        {
            (loc.Get("SymGroupAlgebra"), new[] { "±", "∓", "×", "·", "÷", "⁄", "=", "≠", "≈", "≡", "≢", "∼", "≃", "≅", "<", ">", "≤", "≥", "≪", "≫", "∝", "∞", "√", "∛", "∜", "%", "‰", "∱", "!" }),
            (loc.Get("SymGroupAnalysis"), new[] { "∑", "∏", "∐", "∫", "∬", "∭", "∮", "∂", "∆", "∇", "′", "″", "lim", "∈", "∉", "∋", "∌", "⊂", "⊃", "⊆", "⊇", "⊄", "∪", "∩", "∖", "∅", "∀", "∃", "∄", "¬", "∧", "∨", "⊕", "⊗", "⊢", "⊨", "∴", "∵", "∎" }),
            (loc.Get("SymGroupGreek"), new[] { "α", "β", "γ", "δ", "ε", "ζ", "η", "θ", "ι", "κ", "λ", "μ", "ν", "ξ", "ο", "π", "ρ", "σ", "ς", "τ", "υ", "φ", "χ", "ψ", "ω", "Γ", "Δ", "Θ", "Λ", "Ξ", "Π", "Σ", "Φ", "Ψ", "Ω" }),
            (loc.Get("SymGroupPhysics"), new[] { "℃", "℉", "K", "Å", "Ω", "µ", "ℏ", "ℓ", "ℯ", "ℕ", "ℤ", "ℚ", "ℝ", "ℂ", "ℙ", "⇌", "⇄", "↑", "↓", "°", "′", "″", "†", "‡", "§" }),
            (loc.Get("SymGroupArrows"), new[] { "→", "←", "↔", "↑", "↓", "↕", "↗", "↘", "↙", "↖", "⇒", "⇐", "⇔", "⇑", "⇓", "↦", "∠", "∡", "∢", "⊥", "∥", "∦", "△", "□", "○", "◊", "★", "✓", "✗" }),
            (loc.Get("SymGroupScripts"), new[] { "⁰", "¹", "²", "³", "⁴", "⁵", "⁶", "⁷", "⁸", "⁹", "ⁿ", "ⁱ", "⁺", "⁻", "₀", "₁", "₂", "₃", "₄", "₅", "₆", "₇", "₈", "₉", "ₙ", "½", "⅓", "⅔", "¼", "¾", "⅕", "⅛" })
        };

        foreach (var (title, symbols) in groups)
        {
            SymbolsCategoryHost.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
                Margin = new Thickness(2, 4, 0, 4)
            });

            var wrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
            foreach (string sym in symbols)
            {
                string captured = sym;
                var btn = new Button
                {
                    Content = captured,
                    MinWidth = 30,
                    Height = 28,
                    Margin = new Thickness(2),
                    Padding = new Thickness(5, 0, 5, 0),
                    FontSize = 13,
                    FontWeight = FontWeights.Medium,
                    Focusable = false,
                    Style = (Style)FindResource("FluentButtonStyle")
                };
                btn.Click += (_, _) =>
                {
                    _activeTextOverlay?.InsertSymbolAtCaret(captured);
                    PopupTextSymbols.IsOpen = false;
                };
                wrap.Children.Add(btn);
            }
            SymbolsCategoryHost.Children.Add(wrap);
        }
    }

    private void BuildThicknessPresets()
    {
        ThicknessPresetsPanel.Children.Clear();
        double[] presets = { 1.0, 2.0, 3.5, 5.0, 8.0, 12.0, 18.0 };
        foreach (double size in presets)
        {
            var btn = new Button
            {
                Style = (Style)FindResource("FluentButtonStyle"),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(8, 5, 8, 5),
                Margin = new Thickness(0, 1, 0, 1),
                BorderThickness = new Thickness(0)
            };

            var row = new DockPanel();
            var label = new TextBlock
            {
                Text = $"{size:0.#} px",
                Width = 48,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(label, Dock.Left);

            var line = new Line
            {
                X1 = 0,
                Y1 = 8,
                X2 = 120,
                Y2 = 8,
                StrokeThickness = Math.Min(size, 14.0),
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                VerticalAlignment = VerticalAlignment.Center
            };
            line.SetResourceReference(Shape.StrokeProperty, "TextPrimaryBrush");

            row.Children.Add(label);
            row.Children.Add(line);
            btn.Content = row;
            btn.Click += (_, _) =>
            {
                SetThickness(size);
                PopupThickness.IsOpen = false;
            };
            ThicknessPresetsPanel.Children.Add(btn);
        }
    }

    private void BuildShapesGallery()
    {
        ShapesGalleryPanel.Children.Clear();
        Shapes3DGalleryPanel.Children.Clear();
        var loc = LocalizationService.Instance;
        LblShapes2DHeader.Text = loc.Get("Shapes2DHeader");
        LblShapes3DHeader.Text = loc.Get("Shapes3DHeader");

        var items2D = new (ShapeKind Kind, string LabelKey, string Icon)[]
        {
            (ShapeKind.Line, "ShapeLine", "╱"),
            (ShapeKind.Arrow, "ShapeArrow", "→"),
            (ShapeKind.DoubleArrow, "ShapeDoubleArrow", "↔"),
            (ShapeKind.Rectangle, "ShapeRectangle", "▭"),
            (ShapeKind.RoundedRectangle, "ShapeRoundedRect", "▢"),
            (ShapeKind.Square, "ShapeSquare", "■"),
            (ShapeKind.Circle, "ShapeCircle", "◯"),
            (ShapeKind.Ellipse, "ShapeEllipse", "⬭"),
            (ShapeKind.Triangle, "ShapeTriangle", "△"),
            (ShapeKind.RightTriangle, "ShapeRightTriangle", "⊿"),
            (ShapeKind.Trapezoid, "ShapeTrapezoid", "⏢"),
            (ShapeKind.RightTrapezoid, "ShapeRightTrapezoid", "⏥"),
            (ShapeKind.Parallelogram, "ShapeParallelogram", "▱"),
            (ShapeKind.Rhombus, "ShapeRhombus", "◇"),
            (ShapeKind.Hexagon, "ShapeHexagon", "⬡"),
            (ShapeKind.Star, "ShapeStar", "☆"),
            (ShapeKind.Cloud, "ShapeCloud", "☁")
        };

        foreach (var (kind, key, icon) in items2D)
        {
            var btn = new Button
            {
                Style = (Style)FindResource("FluentButtonStyle"),
                Width = 90,
                Height = 52,
                Margin = new Thickness(2.5),
                Padding = new Thickness(4),
                ToolTip = loc.Get(key)
            };

            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            stack.Children.Add(new TextBlock
            {
                Text = icon,
                FontSize = 16,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            stack.Children.Add(new TextBlock
            {
                Text = loc.Get(key),
                FontSize = 10,
                TextTrimming = TextTrimming.CharacterEllipsis,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            btn.Content = stack;
            btn.Click += (_, _) =>
            {
                MainInkCanvas.InsertShape(kind);
                PopupShapes.IsOpen = false;
            };
            ShapesGalleryPanel.Children.Add(btn);
        }

        var items3D = new (ShapeKind Kind, string LabelKey)[]
        {
            (ShapeKind.Cube3D, "ShapeCube3D"),
            (ShapeKind.Cuboid3D, "ShapeCuboid3D"),
            (ShapeKind.TriangularPrism3D, "ShapeTriangularPrism3D"),
            (ShapeKind.HexagonalPrism3D, "ShapeHexagonalPrism3D"),
            (ShapeKind.SquarePyramid3D, "ShapeSquarePyramid3D"),
            (ShapeKind.Tetrahedron3D, "ShapeTetrahedron3D"),
            (ShapeKind.Cylinder3D, "ShapeCylinder3D"),
            (ShapeKind.Cone3D, "ShapeCone3D"),
            (ShapeKind.Sphere3D, "ShapeSphere3D")
        };

        foreach (var (kind, key) in items3D)
        {
            var btn = new Button
            {
                Style = (Style)FindResource("FluentButtonStyle"),
                Width = 90,
                Height = 58,
                Margin = new Thickness(2.5),
                Padding = new Thickness(4),
                ToolTip = loc.Get(key)
            };

            var previewCanvas = new Canvas { Width = 28, Height = 24, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 2) };
            var wireframe = Geometry3DHelper.BuildWireframe(kind, new RectD(4, 2, 20, 20));
            foreach (var part in wireframe)
            {
                var poly = new System.Windows.Shapes.Polyline
                {
                    StrokeThickness = part.IsDashed ? 1.1 : 1.45,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    StrokeLineJoin = PenLineJoin.Round,
                    Opacity = part.IsDashed ? 0.55 : 0.95
                };
                poly.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, part.IsDashed ? "TextSecondaryBrush" : "PrimaryBrush");
                if (part.IsDashed)
                {
                    poly.StrokeDashArray = new DoubleCollection { 2.2, 2.0 };
                }
                foreach (var pt in part.Points)
                {
                    poly.Points.Add(new Point(pt.X, pt.Y));
                }
                previewCanvas.Children.Add(poly);
            }

            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            stack.Children.Add(previewCanvas);
            stack.Children.Add(new TextBlock
            {
                Text = loc.Get(key),
                FontSize = 10,
                TextTrimming = TextTrimming.CharacterEllipsis,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            btn.Content = stack;
            btn.Click += (_, _) =>
            {
                MainInkCanvas.InsertShape(kind);
                PopupShapes.IsOpen = false;
            };
            Shapes3DGalleryPanel.Children.Add(btn);
        }
    }

    private void SetThickness(double thickness)
    {
        double clamped = Math.Clamp(thickness, 0.5, 40.0);
        DataStore.Instance.SelectedThickness = clamped;
        DataStore.Instance.SaveSettings();

        _isUpdatingThickness = true;
        LineThicknessPreview.StrokeThickness = Math.Clamp(clamped, 1.0, 12.0);
        TxtThicknessValue.Text = $"{clamped:0.#} px ▾";
        SliderCustomThickness.Value = clamped;
        TxtCustomThickness.Text = clamped.ToString("0.#", CultureInfo.InvariantCulture);
        _isUpdatingThickness = false;
    }

    private void UpdateLocalization()
    {
        var loc = LocalizationService.Instance;
        Title = loc.Get("AppTitle");
        BtnNewNote.Content = "+ " + loc.Get("NewNote");
        TxtSearchPlaceholder.Text = loc.Get("SearchNotes");
        BtnToggleSidebar.ToolTip = loc.Get("ToggleSidebar");
        BtnAddCategory.ToolTip = loc.Get("AddCategory");
        BtnAddSubcategory.ToolTip = loc.Get("AddSubcategory");

        LblToolCursor.Text = loc.Get("ToolCursor");
        LblToolPen.Text = loc.Get("ToolPen");
        LblToolFountainPen.Text = loc.Get("ToolFountainPen");
        LblToolPencil.Text = loc.Get("ToolPencil");
        LblToolHighlighter.Text = loc.Get("ToolHighlighter");
        LblToolEraser.Text = loc.Get("ToolEraser");
        LblToolSelect.Text = loc.Get("ToolSelect");
        LblToolLaser.Text = loc.Get("ToolLaser");

        LblFoldersHeader.Text = loc.Get("FoldersTitle").ToUpperInvariant();
        BtnAddCategory.Content = loc.Get("AddCategory");
        BtnAddSubcategory.Content = loc.Get("AddSubcategory");
        BtnAddCategory.ToolTip = loc.Get("AddCategory");
        BtnAddSubcategory.ToolTip = loc.Get("AddSubcategory");
        UpdateEraserModeUI();

        LblShapeSnap.Text = loc.Get("ShapeSnap");
        LblInsertShape.Text = loc.Get("InsertShape") + " ▾";
        LblInsertTable.Text = loc.Get("ToolTable");
        LblInsertText.Text = loc.Get("ToolText");
        LblInsertTextBox.Text = loc.Get("ToolTextBox");
        LblInsertAxes.Text = loc.Get("ToolAxes");
        LblInsertImage.Text = loc.Get("InsertImage") + " ▾";
        TxtPasteImageTitle.Text = loc.Get("PasteImageClipboard") + " (Ctrl+V)";
        TxtChooseImageFileTitle.Text = loc.Get("InsertImageFromFile");
        BtnToggleBottomBar.ToolTip = loc.Get("HideBottomBar");
        BtnRestoreBottomBar.ToolTip = loc.Get("ShowBottomBar");
        LblCustomThickness.Text = loc.Get("CustomThickness");
        LblSettings.Text = loc.Get("Settings");

        BtnUndo.ToolTip = loc.Get("Undo");
        BtnRedo.ToolTip = loc.Get("Redo");
        BtnZoomOut.ToolTip = loc.Get("ZoomOut");
        BtnZoomIn.ToolTip = loc.Get("ZoomIn");
        BtnZoomReset.ToolTip = loc.Get("ZoomReset");
        BtnCustomColor.ToolTip = loc.Get("CustomColor");

        BtnPrevPage.ToolTip = loc.Get("PrevPage");
        BtnNextPage.ToolTip = loc.Get("NextPage");
        BtnAddPage.Content = loc.Get("AddPage");
        BtnDeletePage.Content = loc.Get("DeletePage");
        BtnExpandWidth.Content = loc.Get("ExpandWidth");
        BtnExpandHeight.Content = loc.Get("ExpandHeight");
        BtnClearPage.Content = loc.Get("ClearCanvas");
        BtnExportPng.Content = loc.Get("ExportPng");
        BtnExportPdf.Content = loc.Get("ExportPdf");
        BtnReverseArrow.Content = "⇄ " + loc.Get("ReverseArrow");
        BtnCopySelected.Content = "📋 " + loc.Get("CopySelected");
        BtnDeleteSelected.ToolTip = loc.Get("DeleteSelected");
        BtnCopySelected.ToolTip = loc.Get("CopySelected");
        BtnTabFolders.Content = loc.Get("FoldersTab");
        BtnTabFavorites.Content = "★ " + loc.Get("FavoritesTab");
        BtnTextSymbols.Content = "∑ " + loc.Get("Symbols") + " ▾";
        TxtSelectMarqueeTitle.Text = loc.Get("SelectMarquee");
        TxtSelectLassoTitle.Text = loc.Get("SelectLasso");
        BtnHome.ToolTip = loc.Get("HomeBtnTooltip");
        LblHomeTitle.Text = loc.Get("HomeTitle");
        LblHomeSubtitle.Text = loc.Get("HomeSubtitle");
        BtnHomeOpenCanvas.Content = loc.Get("HomeOpenCanvas") + " →";
        BtnHomeNewNoteTop.Content = "+ " + loc.Get("NewNote");
        LblHomeContinue.Text = loc.Get("HomeContinueLast");
        LblHomeFavorites.Text = "★ " + loc.Get("HomeFavorites").ToUpperInvariant();
        LblHomeRecent.Text = loc.Get("HomeRecentNotes").ToUpperInvariant();

        BuildSymbolsPopup();
        UpdatePageBar();
        if (IsLoaded)
        {
            RefreshHomeScreen();
            RefreshNotesList();
        }
    }

    private Subcategory? GetEffectiveTargetSubcategory()
    {
        return _selectedSubcategory ?? _selectedCategory?.Subcategories.FirstOrDefault();
    }

    private void BtnToggleExpandAll_Click(object sender, RoutedEventArgs e)
    {
        var categories = DataStore.Instance.Categories;
        if (categories.Count == 0) return;
        bool anyCollapsed = categories.Any(c => !c.IsExpanded);
        foreach (var cat in categories)
        {
            cat.IsExpanded = anyCollapsed;
        }
        RefreshCategorySelectors();
    }

    private void AttachFolderDropHandlers(Border targetBorder, Category targetCat, Subcategory? targetSub, Brush idleBg, Brush idleBorder)
    {
        targetBorder.AllowDrop = true;
        targetBorder.DragOver += (_, ev) =>
        {
            if (ev.Data.GetDataPresent(typeof(Note)))
            {
                ev.Effects = DragDropEffects.Move;
                targetBorder.Background = (Brush)TryFindResource("SelectionTintBrush") ?? new SolidColorBrush(Color.FromArgb(30, 124, 58, 237));
                targetBorder.BorderBrush = (Brush)TryFindResource("PrimaryBrush") ?? new SolidColorBrush(Color.FromRgb(124, 58, 237));
                targetBorder.BorderThickness = new Thickness(1.5);
                ev.Handled = true;
            }
            else
            {
                ev.Effects = DragDropEffects.None;
            }
        };
        targetBorder.DragLeave += (_, _) =>
        {
            targetBorder.Background = idleBg;
            targetBorder.BorderBrush = idleBorder;
            targetBorder.BorderThickness = new Thickness(1);
        };
        targetBorder.Drop += (_, ev) =>
        {
            targetBorder.Background = idleBg;
            targetBorder.BorderBrush = idleBorder;
            targetBorder.BorderThickness = new Thickness(1);
            if (ev.Data.GetData(typeof(Note)) is Note droppedNote)
            {
                var destSub = targetSub ?? targetCat.Subcategories.FirstOrDefault();
                if (destSub != null)
                {
                    DataStore.Instance.MoveNoteToSubcategory(droppedNote, destSub);
                    targetCat.IsExpanded = true;
                    _selectedCategory = targetCat;
                    _selectedSubcategory = targetSub;
                    RefreshCategorySelectors();
                    RefreshNotesList();
                }
                ev.Handled = true;
            }
        };
    }

    private void RefreshCategorySelectors()
    {
        if (_isUpdatingCategories) return;
        _isUpdatingCategories = true;
        try
        {
            var categories = DataStore.Instance.Categories;
            if (_selectedCategory == null || !categories.Contains(_selectedCategory))
            {
                (_selectedCategory, _selectedSubcategory) = FindOwnerFolder(DataStore.Instance.ActiveNote);
            }
            if (_selectedCategory == null && categories.Count > 0)
            {
                _selectedCategory = categories[0];
                _selectedSubcategory = _selectedCategory.Subcategories.FirstOrDefault();
            }
            if (_selectedCategory != null && _selectedSubcategory != null && !_selectedCategory.Subcategories.Contains(_selectedSubcategory))
            {
                _selectedSubcategory = _selectedCategory.Subcategories.FirstOrDefault();
            }

            if (BtnToggleExpandAll != null)
            {
                bool anyCollapsed = categories.Any(c => !c.IsExpanded);
                BtnToggleExpandAll.Content = anyCollapsed ? "▸" : "▾";
                BtnToggleExpandAll.ToolTip = LocalizationService.Instance.Get(anyCollapsed ? "ExpandAll" : "CollapseAll");
            }

            FoldersTreePanel.Children.Clear();

            foreach (var cat in categories)
            {
                bool isCatFocused = ReferenceEquals(cat, _selectedCategory) && _selectedSubcategory == null;
                bool isCatAncestor = ReferenceEquals(cat, _selectedCategory);

                Brush folderIdleBg = isCatFocused
                    ? (Brush)TryFindResource("SelectionTintBrush") ?? new SolidColorBrush(Color.FromArgb(24, 124, 58, 237))
                    : Brushes.Transparent;
                Brush folderIdleBorder = isCatFocused
                    ? (Brush)TryFindResource("ActiveBorderBrush") ?? new SolidColorBrush(Color.FromRgb(196, 181, 253))
                    : Brushes.Transparent;

                var folderBtn = new Border
                {
                    Background = folderIdleBg,
                    BorderBrush = folderIdleBorder,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(4, 5, 6, 5),
                    Margin = new Thickness(0, 1, 0, 1),
                    Cursor = Cursors.Hand
                };

                var folderDock = new DockPanel();
                Category capCat = cat;

                var chevronBtn = new Border
                {
                    Width = 18,
                    Height = 20,
                    Background = Brushes.Transparent,
                    CornerRadius = new CornerRadius(4),
                    Cursor = Cursors.Hand,
                    Child = new TextBlock
                    {
                        Text = cat.IsExpanded ? "▾" : "▸",
                        FontSize = 11,
                        Foreground = (Brush)FindResource("TextSecondaryBrush"),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                };
                chevronBtn.MouseLeftButtonDown += (_, ev) =>
                {
                    ev.Handled = true;
                    capCat.IsExpanded = !capCat.IsExpanded;
                    RefreshCategorySelectors();
                };
                DockPanel.SetDock(chevronBtn, Dock.Left);

                Brush folderTint;
                try { folderTint = new SolidColorBrush((Color)ColorConverter.ConvertFromString(cat.ColorHex)); }
                catch { folderTint = (Brush)FindResource("PrimaryBrush"); }

                var folderIconBadge = new Border
                {
                    Width = 22,
                    Height = 22,
                    CornerRadius = new CornerRadius(6),
                    Background = Brushes.Transparent,
                    Margin = new Thickness(0, 0, 5, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = LocalizationService.Instance.Get("ChangeIcon"),
                    Child = new System.Windows.Shapes.Path
                    {
                        Width = 14,
                        Height = 14,
                        Stretch = Stretch.Uniform,
                        Fill = folderTint,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        Data = FolderIconCatalog.GetGeometry(cat.Icon)
                    }
                };
                folderIconBadge.MouseLeftButtonDown += (_, ev) =>
                {
                    ev.Handled = true;
                    OpenCategoryIconPicker(capCat);
                };
                DockPanel.SetDock(folderIconBadge, Dock.Left);

                var addSubInlineBtn = new Button
                {
                    Content = "+",
                    Width = 20,
                    Height = 20,
                    Padding = new Thickness(0),
                    FontSize = 11,
                    FontWeight = FontWeights.Bold,
                    Style = (Style)FindResource("FluentButtonStyle"),
                    ToolTip = LocalizationService.Instance.Get("AddSubcategory"),
                    Margin = new Thickness(4, 0, 0, 0)
                };
                addSubInlineBtn.Click += (s, ev) =>
                {
                    ev.Handled = true;
                    _selectedCategory = capCat;
                    BtnAddSubcategory_Click(s, ev);
                };
                DockPanel.SetDock(addSubInlineBtn, Dock.Right);

                var countBadge = new Border
                {
                    Background = isCatFocused
                        ? (Brush)TryFindResource("SelectionTintBrush") ?? new SolidColorBrush(Color.FromRgb(245, 243, 255))
                        : Brushes.Transparent,
                    CornerRadius = new CornerRadius(5),
                    Padding = new Thickness(5, 1, 5, 1),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(4, 0, 0, 0),
                    Child = new TextBlock
                    {
                        Text = cat.TotalNotesCount.ToString(),
                        FontSize = 10.5,
                        FontWeight = isCatFocused ? FontWeights.Bold : FontWeights.Normal,
                        Foreground = isCatFocused
                            ? (Brush)FindResource("PrimaryBrush")
                            : (Brush)FindResource("TextSecondaryBrush")
                    }
                };
                DockPanel.SetDock(countBadge, Dock.Right);

                var folderName = new TextBlock
                {
                    Text = cat.Name,
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 12.5,
                    Foreground = isCatAncestor
                        ? (Brush)FindResource("PrimaryBrush")
                        : (Brush)FindResource("TextPrimaryBrush"),
                    VerticalAlignment = VerticalAlignment.Center
                };

                folderDock.Children.Add(chevronBtn);
                folderDock.Children.Add(folderIconBadge);
                folderDock.Children.Add(addSubInlineBtn);
                folderDock.Children.Add(countBadge);
                folderDock.Children.Add(folderName);
                folderBtn.Child = folderDock;

                folderBtn.MouseLeftButtonDown += (_, ev) =>
                {
                    ev.Handled = true;
                    if (ev.ClickCount == 2)
                    {
                        string? renamed = PromptInputDialog(LocalizationService.Instance.Get("CategoryName"), capCat.Name);
                        if (!string.IsNullOrWhiteSpace(renamed)) DataStore.Instance.RenameCategory(capCat, renamed);
                        return;
                    }

                    _selectedCategory = capCat;
                    _selectedSubcategory = null; // Show all notes across this folder
                    capCat.IsExpanded = true;
                    RefreshCategorySelectors();
                    RefreshNotesList();
                };

                AttachFolderDropHandlers(folderBtn, capCat, null, folderIdleBg, folderIdleBorder);

                // Context menu for Folder
                var catMenu = new ContextMenu();
                var miNewNoteInCat = new MenuItem { Header = LocalizationService.Instance.Get("NewNoteHere") };
                miNewNoteInCat.Click += (_, _) =>
                {
                    var destSub = capCat.Subcategories.FirstOrDefault();
                    _selectedCategory = capCat;
                    _selectedSubcategory = destSub;
                    DataStore.Instance.CreateNote(targetSubcategory: destSub);
                    MainInkCanvas.RenderCanvas();
                };
                var miAddSub = new MenuItem { Header = LocalizationService.Instance.Get("AddSubcategory") };
                miAddSub.Click += (s, ev) => { _selectedCategory = capCat; BtnAddSubcategory_Click(s, ev); };
                var miIconCat = new MenuItem { Header = LocalizationService.Instance.Get("ChangeIcon") };
                miIconCat.Click += (_, _) => OpenCategoryIconPicker(capCat);
                var miRenameCat = new MenuItem { Header = LocalizationService.Instance.Get("RenameNote") };
                miRenameCat.Click += (_, _) =>
                {
                    string? n = PromptInputDialog(LocalizationService.Instance.Get("CategoryName"), capCat.Name);
                    if (!string.IsNullOrWhiteSpace(n)) DataStore.Instance.RenameCategory(capCat, n);
                };
                catMenu.Items.Add(miNewNoteInCat);
                catMenu.Items.Add(miAddSub);
                catMenu.Items.Add(miIconCat);
                catMenu.Items.Add(miRenameCat);
                if (categories.Count > 1)
                {
                    catMenu.Items.Add(new Separator());
                    var miDelCat = new MenuItem
                    {
                        Header = LocalizationService.Instance.Get("DeleteItem"),
                        Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38))
                    };
                    miDelCat.Click += (_, _) =>
                    {
                        string msg = string.Format(LocalizationService.Instance.Get("ConfirmDeleteFolder"), capCat.Name);
                        if (ConfirmDialog.ShowConfirm(
                                this,
                                LocalizationService.Instance.Get("DeleteItem"),
                                msg,
                                LocalizationService.Instance.Get("DeleteItem"),
                                LocalizationService.Instance.Get("Close"),
                                isDestructive: true))
                        {
                            DataStore.Instance.DeleteCategory(capCat);
                        }
                    };
                    catMenu.Items.Add(miDelCat);
                }
                folderBtn.ContextMenu = catMenu;

                FoldersTreePanel.Children.Add(folderBtn);

                if (!cat.IsExpanded) continue;

                // Visual tree guide rail wrapping subfolders of this category
                var subTreeContainer = new Border
                {
                    BorderBrush = (Brush)TryFindResource("BorderBrush") ?? new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                    BorderThickness = new Thickness(1.5, 0, 0, 0),
                    Margin = new Thickness(14, 1, 0, 4),
                    Padding = new Thickness(6, 0, 0, 0)
                };
                var subStack = new StackPanel();

                foreach (var sub in cat.Subcategories)
                {
                    bool isSubSelected = ReferenceEquals(sub, _selectedSubcategory);
                    Brush subIdleBg = isSubSelected
                        ? (Brush)TryFindResource("ControlSurfaceBrush") ?? Brushes.White
                        : Brushes.Transparent;
                    Brush subIdleBorder = isSubSelected
                        ? (Brush)TryFindResource("ActiveBorderBrush") ?? new SolidColorBrush(Color.FromRgb(196, 181, 253))
                        : Brushes.Transparent;

                    var subBorder = new Border
                    {
                        Background = subIdleBg,
                        BorderBrush = subIdleBorder,
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(8),
                        Padding = new Thickness(7, 4, 6, 4),
                        Margin = new Thickness(0, 1, 0, 1),
                        Cursor = Cursors.Hand
                    };

                    var subDock = new DockPanel();
                    Subcategory capSub = sub;
                    string subIconKey = string.IsNullOrWhiteSpace(sub.Icon) ? "Subfolder" : sub.Icon;

                    var subIconBadge = new Border
                    {
                        Width = 20,
                        Height = 20,
                        CornerRadius = new CornerRadius(5),
                        Background = Brushes.Transparent,
                        Margin = new Thickness(0, 0, 5, 0),
                        VerticalAlignment = VerticalAlignment.Center,
                        ToolTip = LocalizationService.Instance.Get("ChangeIcon"),
                        Child = new System.Windows.Shapes.Path
                        {
                            Width = 12,
                            Height = 12,
                            Stretch = Stretch.Uniform,
                            Fill = isSubSelected
                                ? (Brush)FindResource("PrimaryBrush")
                                : (Brush)FindResource("TextSecondaryBrush"),
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center,
                            Data = FolderIconCatalog.GetGeometry(subIconKey)
                        }
                    };
                    subIconBadge.MouseLeftButtonDown += (_, ev) =>
                    {
                        ev.Handled = true;
                        OpenSubcategoryIconPicker(capSub);
                    };
                    DockPanel.SetDock(subIconBadge, Dock.Left);

                    var quickAddNoteBtn = new Button
                    {
                        Content = "+",
                        Width = 18,
                        Height = 18,
                        Padding = new Thickness(0),
                        FontSize = 10.5,
                        FontWeight = FontWeights.Bold,
                        Style = (Style)FindResource("FluentButtonStyle"),
                        ToolTip = LocalizationService.Instance.Get("NewNoteHere"),
                        Margin = new Thickness(4, 0, 0, 0),
                        Opacity = 0.72
                    };
                    quickAddNoteBtn.Click += (_, ev) =>
                    {
                        ev.Handled = true;
                        _selectedCategory = capCat;
                        _selectedSubcategory = capSub;
                        DataStore.Instance.CreateNote(targetSubcategory: capSub);
                        MainInkCanvas.RenderCanvas();
                    };
                    DockPanel.SetDock(quickAddNoteBtn, Dock.Right);

                    var subCount = new Border
                    {
                        Background = isSubSelected
                            ? (Brush)TryFindResource("SelectionTintBrush") ?? new SolidColorBrush(Color.FromRgb(245, 243, 255))
                            : (Brush)TryFindResource("PillTrackBrush") ?? new SolidColorBrush(Color.FromArgb(20, 100, 116, 139)),
                        CornerRadius = new CornerRadius(6),
                        Padding = new Thickness(6, 1, 6, 1),
                        VerticalAlignment = VerticalAlignment.Center,
                        Child = new TextBlock
                        {
                            Text = sub.NotesCount.ToString(),
                            FontSize = 10,
                            FontWeight = FontWeights.SemiBold,
                            Foreground = isSubSelected
                                ? (Brush)FindResource("PrimaryBrush")
                                : (Brush)FindResource("TextSecondaryBrush")
                        }
                    };
                    DockPanel.SetDock(subCount, Dock.Right);

                    var subName = new TextBlock
                    {
                        Text = sub.Name,
                        FontSize = 12,
                        FontWeight = isSubSelected ? FontWeights.SemiBold : FontWeights.Normal,
                        Foreground = isSubSelected
                            ? (Brush)FindResource("PrimaryBrush")
                            : (Brush)FindResource("TextPrimaryBrush"),
                        VerticalAlignment = VerticalAlignment.Center
                    };

                    subDock.Children.Add(subIconBadge);
                    subDock.Children.Add(quickAddNoteBtn);
                    subDock.Children.Add(subCount);
                    subDock.Children.Add(subName);
                    subBorder.Child = subDock;

                    subBorder.MouseLeftButtonDown += (_, ev) =>
                    {
                        ev.Handled = true;
                        if (ev.ClickCount == 2)
                        {
                            string? renamed = PromptInputDialog(LocalizationService.Instance.Get("SubcategoryName"), capSub.Name);
                            if (!string.IsNullOrWhiteSpace(renamed)) DataStore.Instance.RenameSubcategory(capSub, renamed);
                            return;
                        }

                        _selectedCategory = capCat;
                        _selectedSubcategory = capSub;
                        RefreshCategorySelectors();
                        RefreshNotesList();
                    };

                    AttachFolderDropHandlers(subBorder, capCat, capSub, subIdleBg, subIdleBorder);

                    var subMenu = new ContextMenu();
                    var miNewNoteInSub = new MenuItem { Header = LocalizationService.Instance.Get("NewNoteHere") };
                    miNewNoteInSub.Click += (_, _) =>
                    {
                        _selectedCategory = capCat;
                        _selectedSubcategory = capSub;
                        DataStore.Instance.CreateNote(targetSubcategory: capSub);
                        MainInkCanvas.RenderCanvas();
                    };
                    var miIconSub = new MenuItem { Header = LocalizationService.Instance.Get("ChangeIcon") };
                    miIconSub.Click += (_, _) => OpenSubcategoryIconPicker(capSub);
                    var miRenameSub = new MenuItem { Header = LocalizationService.Instance.Get("RenameNote") };
                    miRenameSub.Click += (_, _) =>
                    {
                        string? n = PromptInputDialog(LocalizationService.Instance.Get("SubcategoryName"), capSub.Name);
                        if (!string.IsNullOrWhiteSpace(n)) DataStore.Instance.RenameSubcategory(capSub, n);
                    };
                    subMenu.Items.Add(miNewNoteInSub);
                    subMenu.Items.Add(miIconSub);
                    subMenu.Items.Add(miRenameSub);
                    if (cat.Subcategories.Count > 1)
                    {
                        subMenu.Items.Add(new Separator());
                        var miDelSub = new MenuItem
                        {
                            Header = LocalizationService.Instance.Get("DeleteItem"),
                            Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38))
                        };
                        miDelSub.Click += (_, _) =>
                        {
                            string msg = string.Format(LocalizationService.Instance.Get("ConfirmDeleteSubfolder"), capSub.Name);
                            if (ConfirmDialog.ShowConfirm(
                                    this,
                                    LocalizationService.Instance.Get("DeleteItem"),
                                    msg,
                                    LocalizationService.Instance.Get("DeleteItem"),
                                    LocalizationService.Instance.Get("Close"),
                                    isDestructive: true))
                            {
                                DataStore.Instance.DeleteSubcategory(capCat, capSub);
                            }
                        };
                        subMenu.Items.Add(miDelSub);
                    }
                    subBorder.ContextMenu = subMenu;

                    subStack.Children.Add(subBorder);
                }

                subTreeContainer.Child = subStack;
                FoldersTreePanel.Children.Add(subTreeContainer);
            }
        }
        finally
        {
            _isUpdatingCategories = false;
        }
    }

    private void OpenCategoryIconPicker(Category cat)
    {
        var dlg = new IconPickerWindow(cat.Icon, cat.ColorHex, allowColor: true) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            cat.ColorHex = dlg.SelectedColorHex;
            DataStore.Instance.SetCategoryIcon(cat, dlg.SelectedIconKey);
            RefreshCategorySelectors();
        }
    }

    private void OpenSubcategoryIconPicker(Subcategory sub)
    {
        string key = string.IsNullOrWhiteSpace(sub.Icon) ? "Subfolder" : sub.Icon;
        var dlg = new IconPickerWindow(key, "#2563EB", allowColor: false) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            DataStore.Instance.SetSubcategoryIcon(sub, dlg.SelectedIconKey);
            RefreshCategorySelectors();
        }
    }

    private static (Category? Cat, Subcategory? Sub) FindOwnerFolder(Note? note)
    {
        if (note != null)
        {
            foreach (var cat in DataStore.Instance.Categories)
            {
                foreach (var sub in cat.Subcategories)
                {
                    if (sub.Notes.Contains(note)) return (cat, sub);
                }
            }
        }
        var firstCat = DataStore.Instance.Categories.FirstOrDefault();
        return (firstCat, firstCat?.Subcategories.FirstOrDefault());
    }

    private void RefreshNotesList()
    {
        _isUpdatingNoteList = true;
        try
        {
            string query = TxtSearchNotes?.Text?.Trim() ?? string.Empty;
            bool favoritesOnly = BtnTabFavorites?.IsChecked == true;
            var notes = new List<Note>();

            if (favoritesOnly)
            {
                foreach (var cat in DataStore.Instance.Categories)
                    foreach (var sub in cat.Subcategories)
                        notes.AddRange(sub.Notes.Where(n => n.IsFavorite &&
                            (string.IsNullOrEmpty(query) || n.Title.Contains(query, StringComparison.OrdinalIgnoreCase))));
                notes = notes.OrderByDescending(n => n.UpdatedAt).ToList();
            }
            else if (!string.IsNullOrEmpty(query))
            {
                foreach (var cat in DataStore.Instance.Categories)
                    foreach (var sub in cat.Subcategories)
                        notes.AddRange(sub.Notes.Where(n => n.Title.Contains(query, StringComparison.OrdinalIgnoreCase)));
            }
            else if (_selectedSubcategory != null)
            {
                notes.AddRange(_selectedSubcategory.Notes);
            }
            else if (_selectedCategory != null)
            {
                foreach (var sub in _selectedCategory.Subcategories)
                    notes.AddRange(sub.Notes);
            }
            else
            {
                foreach (var cat in DataStore.Instance.Categories)
                    foreach (var sub in cat.Subcategories)
                        notes.AddRange(sub.Notes);
            }

            ListNotes.ItemsSource = null;
            ListNotes.ItemsSource = notes;

            if (DataStore.Instance.ActiveNote != null && notes.Contains(DataStore.Instance.ActiveNote))
            {
                ListNotes.SelectedItem = DataStore.Instance.ActiveNote;
            }

            UpdateFolderBreadcrumb(notes.Count, favoritesOnly, query);
        }
        finally
        {
            _isUpdatingNoteList = false;
        }
    }

    private void UpdateFolderBreadcrumb(int noteCount, bool favoritesOnly, string query)
    {
        if (BreadcrumbPanel == null || TxtScopeNoteCount == null) return;
        BreadcrumbPanel.Children.Clear();
        TxtScopeNoteCount.Text = noteCount.ToString();

        var secondaryBrush = (Brush)TryFindResource("TextSecondaryBrush") ?? Brushes.Gray;
        var primaryBrush = (Brush)TryFindResource("TextPrimaryBrush") ?? Brushes.Black;
        var accentBrush = (Brush)TryFindResource("PrimaryBrush") ?? Brushes.Purple;
        var loc = LocalizationService.Instance;

        if (favoritesOnly)
        {
            BreadcrumbPanel.Children.Add(new TextBlock
            {
                Text = "★ " + loc.Get("FavoritesTab"),
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = accentBrush,
                VerticalAlignment = VerticalAlignment.Center
            });
            return;
        }

        if (!string.IsNullOrEmpty(query))
        {
            BreadcrumbPanel.Children.Add(new TextBlock
            {
                Text = $"🔍 \"{query}\"",
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = primaryBrush,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            });
            return;
        }

        if (_selectedCategory != null)
        {
            var catBtn = new TextBlock
            {
                Text = _selectedCategory.Name,
                FontSize = 11.5,
                FontWeight = _selectedSubcategory == null ? FontWeights.Bold : FontWeights.SemiBold,
                Foreground = _selectedSubcategory == null ? accentBrush : secondaryBrush,
                Cursor = Cursors.Hand,
                MaxWidth = 115,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = loc.Get("AllNotesInFolder")
            };
            var capCat = _selectedCategory;
            catBtn.MouseLeftButtonDown += (_, ev) =>
            {
                ev.Handled = true;
                _selectedCategory = capCat;
                _selectedSubcategory = null;
                RefreshCategorySelectors();
                RefreshNotesList();
            };
            BreadcrumbPanel.Children.Add(catBtn);

            if (_selectedSubcategory != null)
            {
                BreadcrumbPanel.Children.Add(new TextBlock
                {
                    Text = " › ",
                    FontSize = 11.5,
                    Foreground = secondaryBrush,
                    VerticalAlignment = VerticalAlignment.Center
                });
                BreadcrumbPanel.Children.Add(new TextBlock
                {
                    Text = _selectedSubcategory.Name,
                    FontSize = 11.5,
                    FontWeight = FontWeights.Bold,
                    Foreground = accentBrush,
                    MaxWidth = 115,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center
                });
            }
        }
    }

    private void UpdatePageBar()
    {
        var note = DataStore.Instance.ActiveNote;
        if (note == null) return;

        int pageCount = note.Pages.Count;
        int current = Math.Clamp(note.ActivePageIndex, 0, Math.Max(0, pageCount - 1)) + 1;
        TxtPageIndicator.Text = $"{LocalizationService.Instance.Get("Page")} {current} / {pageCount}";
        BtnPrevPage.IsEnabled = current > 1;
        BtnNextPage.IsEnabled = current < pageCount;
        BtnDeletePage.IsEnabled = pageCount > 1;
    }

    private void UpdateUndoRedoButtons()
    {
        BtnUndo.IsEnabled = DataStore.Instance.CanUndo;
        BtnRedo.IsEnabled = DataStore.Instance.CanRedo;
    }

    private void UpdateSelectionActions()
    {
        bool hasSelection = MainInkCanvas.HasSelectedStrokes();
        SelectionActionsPanel.Visibility = hasSelection ? Visibility.Visible : Visibility.Collapsed;
        BtnReverseArrow.Visibility = MainInkCanvas.HasSelectedArrow() ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateActiveColorIndicator()
    {
        string activeHex = DataStore.Instance.SelectedColorHex;
        bool matchedPreset = false;
        foreach (var child in ColorPalettePanel.Children)
        {
            if (child is Border border && border.Tag is string hex)
            {
                bool isSelected = string.Equals(hex, activeHex, StringComparison.OrdinalIgnoreCase);
                if (isSelected) matchedPreset = true;
                border.BorderBrush = isSelected
                    ? new SolidColorBrush(Color.FromRgb(124, 58, 237))
                    : Brushes.Transparent;
            }
        }

        if (!matchedPreset)
        {
            try
            {
                CustomColorDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(activeHex));
            }
            catch { }
        }
    }

    private void BtnToggleSidebar_Click(object sender, RoutedEventArgs e)
    {
        _isSidebarCollapsed = !_isSidebarCollapsed;
        var dur = TimeSpan.FromMilliseconds(260);

        if (_isSidebarCollapsed)
        {
            double currentW = SidebarColumn.ActualWidth > 10 ? SidebarColumn.ActualWidth : 296;
            MainGridSplitter.Visibility = Visibility.Collapsed;
            SplitterColumn.Width = new GridLength(0);

            var colAnim = new GridLengthAnimation
            {
                From = new GridLength(currentW, GridUnitType.Pixel),
                To = new GridLength(0, GridUnitType.Pixel),
                Duration = dur,
                EasingFunction = UIAnimationHelper.SmoothOut
            };
            SidebarColumn.BeginAnimation(ColumnDefinition.WidthProperty, colAnim);

            var slideAnim = new DoubleAnimation(-24, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = UIAnimationHelper.SmoothIn
            };
            SidebarTranslate.BeginAnimation(TranslateTransform.XProperty, slideAnim);

            UIAnimationHelper.AnimateFade(SidebarPanel, SidebarPanel.Opacity, 0, 210, () =>
            {
                if (_isSidebarCollapsed)
                {
                    SidebarPanel.Visibility = Visibility.Collapsed;
                    MainInkCanvas.CenterPageInViewport();
                }
            });
        }
        else
        {
            SidebarPanel.Visibility = Visibility.Visible;
            SidebarPanel.Opacity = 0;
            SidebarTranslate.X = -24;

            var colAnim = new GridLengthAnimation
            {
                From = new GridLength(0, GridUnitType.Pixel),
                To = new GridLength(296, GridUnitType.Pixel),
                Duration = dur,
                EasingFunction = UIAnimationHelper.SmoothOut
            };
            colAnim.Completed += (_, _) =>
            {
                if (!_isSidebarCollapsed)
                {
                    SidebarColumn.BeginAnimation(ColumnDefinition.WidthProperty, null);
                    SidebarColumn.Width = new GridLength(296);
                    SplitterColumn.Width = GridLength.Auto;
                    MainGridSplitter.Visibility = Visibility.Visible;
                }
            };
            SidebarColumn.BeginAnimation(ColumnDefinition.WidthProperty, colAnim);

            var slideAnim = new DoubleAnimation(0, dur) { EasingFunction = UIAnimationHelper.SmoothOut };
            SidebarTranslate.BeginAnimation(TranslateTransform.XProperty, slideAnim);
            UIAnimationHelper.AnimateFade(SidebarPanel, 0, 1, 240);
        }
    }

    private void BtnAddCategory_Click(object sender, RoutedEventArgs e)
    {
        string? name = PromptInputDialog(LocalizationService.Instance.Get("CategoryName"), "");
        if (!string.IsNullOrWhiteSpace(name))
        {
            var cat = DataStore.Instance.CreateCategory(name);
            _selectedCategory = cat;
            _selectedSubcategory = cat.Subcategories.FirstOrDefault();
            RefreshCategorySelectors();
            RefreshNotesList();
        }
    }

    private void BtnAddSubcategory_Click(object sender, RoutedEventArgs e)
    {
        var cat = _selectedCategory ?? DataStore.Instance.Categories.FirstOrDefault();
        if (cat == null) return;
        string? name = PromptInputDialog(LocalizationService.Instance.Get("SubcategoryName"), "");
        if (!string.IsNullOrWhiteSpace(name))
        {
            var sub = DataStore.Instance.CreateSubcategory(cat, name);
            _selectedCategory = cat;
            _selectedSubcategory = sub;
            RefreshCategorySelectors();
            RefreshNotesList();
        }
    }

    private void BtnMoveNote_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not Note note) return;

        var menu = new ContextMenu();
        foreach (var cat in DataStore.Instance.Categories)
        {
            foreach (var sub in cat.Subcategories)
            {
                var item = new MenuItem { Header = $"{cat.Name} / {sub.Name}" };
                Category targetCat = cat;
                Subcategory targetSub = sub;
                item.Click += (_, _) =>
                {
                    DataStore.Instance.MoveNoteToSubcategory(note, targetSub);
                    _selectedCategory = targetCat;
                    _selectedSubcategory = targetSub;
                    RefreshCategorySelectors();
                    RefreshNotesList();
                };
                menu.Items.Add(item);
            }
        }
        menu.IsOpen = true;
    }

    private void TxtSearchNotes_TextChanged(object sender, TextChangedEventArgs e)
    {
        TxtSearchPlaceholder.Visibility = string.IsNullOrEmpty(TxtSearchNotes.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
        RefreshNotesList();
    }

    private void ListNotes_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingNoteList) return;
        if (ListNotes.SelectedItem is Note selected)
        {
            DataStore.Instance.SelectNote(selected);
            var (ownerCat, ownerSub) = FindOwnerFolder(selected);
            if (ownerCat != null && ownerSub != null &&
                (!ReferenceEquals(ownerCat, _selectedCategory) || (_selectedSubcategory != null && !ReferenceEquals(ownerSub, _selectedSubcategory))))
            {
                _selectedCategory = ownerCat;
                _selectedSubcategory = ownerSub;
                ownerCat.IsExpanded = true;
                RefreshCategorySelectors();
            }
            UpdateSelectionActions();
        }
    }

    private void ListNotes_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _noteDragStartPoint = e.GetPosition(null);
        var dep = e.OriginalSource as DependencyObject;
        if (dep is FrameworkElement fe && FindVisualAncestor<Button>(fe) != null)
        {
            _draggingNote = null;
            return;
        }
        var item = FindVisualAncestor<ListBoxItem>(dep);
        _draggingNote = item?.DataContext as Note;
    }

    private void ListNotes_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _noteDragStartPoint == null || _draggingNote == null)
        {
            return;
        }

        Point pos = e.GetPosition(null);
        Vector diff = _noteDragStartPoint.Value - pos;
        if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance + 4 ||
            Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance + 4)
        {
            var note = _draggingNote;
            _draggingNote = null;
            _noteDragStartPoint = null;
            DragDrop.DoDragDrop(ListNotes, new DataObject(typeof(Note), note), DragDropEffects.Move);
        }
    }

    private static T? FindVisualAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T match) return match;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private void BtnNewNote_Click(object sender, RoutedEventArgs e)
    {
        var target = GetEffectiveTargetSubcategory();
        _selectedSubcategory = target;
        DataStore.Instance.CreateNote(targetSubcategory: target);
        MainInkCanvas.RenderCanvas();
    }

    private void BtnRenameNote_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is Note note)
        {
            string? newTitle = PromptInputDialog(LocalizationService.Instance.Get("RenameNote"), note.Title);
            if (!string.IsNullOrWhiteSpace(newTitle))
            {
                DataStore.Instance.RenameNote(note, newTitle);
            }
        }
    }

    private void BtnDeleteNote_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is Note note)
        {
            string msg = string.Format(LocalizationService.Instance.Get("ConfirmDeleteNote"), note.Title);
            if (ConfirmDialog.ShowConfirm(
                    this,
                    LocalizationService.Instance.Get("DeleteNote"),
                    msg,
                    LocalizationService.Instance.Get("DeleteItem"),
                    LocalizationService.Instance.Get("Close"),
                    isDestructive: true))
            {
                DataStore.Instance.DeleteNote(note);
            }
        }
    }

    private string? PromptInputDialog(string title, string initialValue)
    {
        var dlg = new Window
        {
            Title = title,
            Width = 420,
            SizeToContent = SizeToContent.Height,
            MinHeight = 195,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize,
            Background = (Brush)FindResource("DeskBackgroundBrush")
        };
        WindowBackdropHelper.ApplyModernWindowChrome(dlg);

        var card = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(18),
            Margin = new Thickness(16)
        };
        card.SetResourceReference(Border.BackgroundProperty, "CardBackgroundBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");

        var panel = new StackPanel();
        var titleBlock = new TextBlock
        {
            Text = title,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 10)
        };
        titleBlock.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        panel.Children.Add(titleBlock);

        var tb = new TextBox
        {
            Text = initialValue,
            FontSize = 13.5,
            Padding = new Thickness(10, 7, 10, 7),
            Margin = new Thickness(0, 0, 0, 14)
        };
        tb.SetResourceReference(Control.BackgroundProperty, "ControlSurfaceBrush");
        tb.SetResourceReference(Control.ForegroundProperty, "TextPrimaryBrush");
        tb.SetResourceReference(Control.BorderBrushProperty, "BorderBrush");
        tb.SetResourceReference(TextBox.CaretBrushProperty, "TextPrimaryBrush");
        tb.SetResourceReference(TextBox.SelectionBrushProperty, "PrimaryBrush");
        tb.SelectAll();
        tb.PreviewStylusUp += (_, _) =>
        {
            tb.Focus();
            TouchKeyboardHelper.ShowTouchKeyboard(new TouchKeyboardHelper.VisualElementTarget(tb));
        };

        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancelBtn = new Button
        {
            Content = LocalizationService.Instance.Get("Close"),
            MinWidth = 88,
            Height = 32,
            IsCancel = true,
            Style = (Style)FindResource("OutlineButtonStyle"),
            Margin = new Thickness(0, 0, 8, 0)
        };
        var okBtn = new Button
        {
            Content = "OK",
            MinWidth = 88,
            Height = 32,
            IsDefault = true,
            Style = (Style)FindResource("PrimaryButtonStyle")
        };

        okBtn.Click += (_, _) => { dlg.DialogResult = true; dlg.Close(); };

        btnPanel.Children.Add(cancelBtn);
        btnPanel.Children.Add(okBtn);
        panel.Children.Add(tb);
        panel.Children.Add(btnPanel);
        card.Child = panel;
        dlg.Content = card;
        dlg.Loaded += (_, _) => tb.Focus();

        return dlg.ShowDialog() == true ? tb.Text : null;
    }

    private void Tool_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag != null)
        {
            int toolIdx = int.Parse(rb.Tag.ToString()!);
            var newTool = (ToolType)toolIdx;
            DataStore.Instance.SelectedTool = newTool;
            AnimateToolIndicator(rb, animate: IsLoaded);
            MainInkCanvas?.UpdateToolInteractionMode();
            MainInkCanvas?.ClearActiveOverlay();
            if (newTool != ToolType.Select && newTool != ToolType.Cursor)
            {
                MainInkCanvas?.ClearSelection();
            }
        }
    }

    private void ToolRadio_PreviewPointerDown(object sender, MouseButtonEventArgs e)
    {
        if (e.StylusDevice != null) return;
        if (sender is RadioButton rb && rb.IsChecked == true)
        {
            DeselectActiveTool(rb);
            e.Handled = true;
        }
    }

    private void ToolRadio_PreviewStylusDown(object sender, StylusDownEventArgs e)
    {
        if (sender is RadioButton rb && rb.IsChecked == true)
        {
            _stylusDeselectTarget = rb;
            e.Handled = true;
        }
        else
        {
            _stylusDeselectTarget = null;
        }
    }

    private void ToolRadio_PreviewStylusUp(object sender, StylusEventArgs e)
    {
        if (sender is RadioButton rb && ReferenceEquals(_stylusDeselectTarget, rb))
        {
            _stylusDeselectTarget = null;
            DeselectActiveTool(rb);
            e.Handled = true;
        }
    }

    private void DeselectActiveTool(RadioButton rb)
    {
        rb.IsChecked = false;
        DataStore.Instance.SelectedTool = ToolType.None;
        AnimateToolIndicator(null, animate: IsLoaded);
        MainInkCanvas?.UpdateToolInteractionMode();
        MainInkCanvas?.ClearActiveOverlay();
        MainInkCanvas?.ClearSelection();
    }

    private void BtnInsertImage_Click(object sender, RoutedEventArgs e)
    {
        PopupInsertImage.IsOpen = !PopupInsertImage.IsOpen;
    }

    private void BtnPasteImage_Click(object sender, RoutedEventArgs e)
    {
        PopupInsertImage.IsOpen = false;
        MainInkCanvas.PasteFromClipboard();
    }

    private void BtnChooseImageFile_Click(object sender, RoutedEventArgs e)
    {
        PopupInsertImage.IsOpen = false;
        MainInkCanvas.InsertImageFromFileDialog();
    }

    private void BtnToggleBottomBar_Click(object sender, RoutedEventArgs e)
    {
        _isBottomBarCollapsed = !_isBottomBarCollapsed;
        if (_isBottomBarCollapsed)
        {
            var dur = TimeSpan.FromMilliseconds(220);
            BottomHudScale.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(0.16, dur) { EasingFunction = UIAnimationHelper.SmoothInOut });
            BottomHudScale.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation(0.55, dur) { EasingFunction = UIAnimationHelper.SmoothInOut });
            BottomHudTranslate.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(14, dur) { EasingFunction = UIAnimationHelper.SmoothInOut });

            UIAnimationHelper.AnimateFade(BottomHudIsland, BottomHudIsland.Opacity, 0, 200, () =>
            {
                if (!_isBottomBarCollapsed) return;
                BottomHudIsland.Visibility = Visibility.Collapsed;

                BottomHudRestoreIsland.Visibility = Visibility.Visible;
                BottomHudRestoreIsland.Opacity = 0;
                BottomHudRestoreScale.ScaleX = 0.72;
                BottomHudRestoreScale.ScaleY = 0.72;
                var inDur = TimeSpan.FromMilliseconds(180);
                BottomHudRestoreScale.BeginAnimation(ScaleTransform.ScaleXProperty,
                    new DoubleAnimation(1.0, inDur) { EasingFunction = UIAnimationHelper.SmoothOut });
                BottomHudRestoreScale.BeginAnimation(ScaleTransform.ScaleYProperty,
                    new DoubleAnimation(1.0, inDur) { EasingFunction = UIAnimationHelper.SmoothOut });
                UIAnimationHelper.AnimateFade(BottomHudRestoreIsland, 0, 1, 180);
            });
        }
        else
        {
            UIAnimationHelper.AnimateFade(BottomHudRestoreIsland, BottomHudRestoreIsland.Opacity, 0, 120, () =>
            {
                if (_isBottomBarCollapsed) return;
                BottomHudRestoreIsland.Visibility = Visibility.Collapsed;

                BottomHudIsland.Visibility = Visibility.Visible;
                BottomHudIsland.Opacity = 0;
                BottomHudScale.ScaleX = 0.22;
                BottomHudScale.ScaleY = 0.6;
                BottomHudTranslate.Y = 14;

                var dur = TimeSpan.FromMilliseconds(260);
                BottomHudScale.BeginAnimation(ScaleTransform.ScaleXProperty,
                    new DoubleAnimation(1.0, dur) { EasingFunction = UIAnimationHelper.SmoothOut });
                BottomHudScale.BeginAnimation(ScaleTransform.ScaleYProperty,
                    new DoubleAnimation(1.0, dur) { EasingFunction = UIAnimationHelper.SmoothOut });
                BottomHudTranslate.BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(0, dur) { EasingFunction = UIAnimationHelper.SmoothOut });
                UIAnimationHelper.AnimateFade(BottomHudIsland, 0, 1, 240);
            });
        }
    }

    private void BtnEraserDropdown_Click(object sender, RoutedEventArgs e)
    {
        BtnEraser.IsChecked = true;
        PopupEraserMode.IsOpen = !PopupEraserMode.IsOpen;
    }

    private void BtnEraserStroke_Click(object sender, RoutedEventArgs e)
    {
        DataStore.Instance.SelectedEraserMode = EraserMode.Stroke;
        DataStore.Instance.SaveSettings();
        BtnEraser.IsChecked = true;
        UpdateEraserModeUI();
        PopupEraserMode.IsOpen = false;
    }

    private void BtnEraserPoint_Click(object sender, RoutedEventArgs e)
    {
        DataStore.Instance.SelectedEraserMode = EraserMode.Point;
        DataStore.Instance.SaveSettings();
        BtnEraser.IsChecked = true;
        UpdateEraserModeUI();
        PopupEraserMode.IsOpen = false;
    }

    private void UpdateEraserModeUI()
    {
        bool isPoint = DataStore.Instance.SelectedEraserMode == EraserMode.Point;
        ChkEraserStroke.Visibility = isPoint ? Visibility.Hidden : Visibility.Visible;
        ChkEraserPoint.Visibility = isPoint ? Visibility.Visible : Visibility.Hidden;
        BtnEraser.ToolTip = isPoint
            ? LocalizationService.Instance.Get("EraserPoint")
            : LocalizationService.Instance.Get("EraserStroke");
    }

    private void BtnShapeSnap_Toggle(object sender, RoutedEventArgs e)
    {
        DataStore.Instance.IsShapeSnapEnabled = BtnShapeSnap.IsChecked == true;
        DataStore.Instance.SaveSettings();
    }

    private void BtnInsertShape_Click(object sender, RoutedEventArgs e)
    {
        PopupShapes.IsOpen = !PopupShapes.IsOpen;
    }

    private void BtnInsertTable_Click(object sender, RoutedEventArgs e)
    {
        PopupTableSize.IsOpen = !PopupTableSize.IsOpen;
    }

    private void TablePreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string spec)
        {
            var parts = spec.Split(',');
            if (parts.Length == 2 && int.TryParse(parts[0], out int rows) && int.TryParse(parts[1], out int cols))
            {
                InsertTableWithSize(rows, cols);
                PopupTableSize.IsOpen = false;
            }
        }
    }

    private void BtnInsertCustomTable_Click(object sender, RoutedEventArgs e)
    {
        int rows = int.TryParse(TxtTableRows.Text, out int r) ? Math.Clamp(r, 1, 20) : 3;
        int cols = int.TryParse(TxtTableCols.Text, out int c) ? Math.Clamp(c, 1, 12) : 3;
        InsertTableWithSize(rows, cols);
        PopupTableSize.IsOpen = false;
    }

    private void InsertTableWithSize(int rows, int cols)
    {
        if (DataStore.Instance.ActiveNote?.CurrentPage == null) return;
        DataStore.Instance.PushUndoState();
        var center = MainInkCanvas.GetViewportCenterCanvasPoint();
        double w = cols * 140.0;
        double h = rows * 42.0;
        var table = new DynamicTable(Math.Max(40, center.X - w * 0.5), Math.Max(40, center.Y - h * 0.5), rows: rows, cols: cols);
        DataStore.Instance.ActiveNote.CurrentPage.Tables.Add(table);
        MainInkCanvas.ActivateTableOnNextSync(table.Id);
        DataStore.Instance.NotifyCanvasChanged();
    }

    private void BtnInsertText_Click(object sender, RoutedEventArgs e)
    {
        if (DataStore.Instance.ActiveNote?.CurrentPage == null) return;

        DataStore.Instance.PushUndoState();
        var center = MainInkCanvas.GetViewportCenterCanvasPoint();
        var textBox = new FloatingTextBox
        {
            X = Math.Max(40, center.X - 140),
            Y = Math.Max(40, center.Y - 75),
            Width = 280,
            IsStickyNote = true,
            BackgroundColor = "#FEF08A",
            Text = ""
        };
        DataStore.Instance.ActiveNote.CurrentPage.TextBoxes.Add(textBox);
        MainInkCanvas.ActivateTextBoxOnNextSync(textBox.Id);
        DataStore.Instance.NotifyCanvasChanged();
        MainInkCanvas.ActiveTextOverlay?.FocusEditor();
    }

    private void BtnInsertTextBox_Click(object sender, RoutedEventArgs e)
    {
        if (DataStore.Instance.ActiveNote?.CurrentPage == null) return;

        DataStore.Instance.PushUndoState();
        var center = MainInkCanvas.GetViewportCenterCanvasPoint();
        var textBox = new FloatingTextBox
        {
            X = Math.Max(40, center.X - 150),
            Y = Math.Max(40, center.Y - 65),
            Width = 300,
            IsStickyNote = false,
            BackgroundColor = "Transparent",
            FontSize = 16,
            Text = ""
        };
        DataStore.Instance.ActiveNote.CurrentPage.TextBoxes.Add(textBox);
        MainInkCanvas.ActivateTextBoxOnNextSync(textBox.Id);
        DataStore.Instance.NotifyCanvasChanged();
        MainInkCanvas.ActiveTextOverlay?.FocusEditor();
    }

    #region Contextual Text Formatting Bar

    private void OnActiveTextOverlayChanged(StickyNoteOverlayControl? overlay)
    {
        _activeTextOverlay = overlay;
        if (overlay == null)
        {
            if (TextFormattingBar.Visibility == Visibility.Visible)
            {
                var slideOut = new DoubleAnimation(-6, TimeSpan.FromMilliseconds(140)) { EasingFunction = UIAnimationHelper.SmoothIn };
                TextFormattingBarTranslate.BeginAnimation(TranslateTransform.YProperty, slideOut);
                UIAnimationHelper.AnimateFade(TextFormattingBar, TextFormattingBar.Opacity, 0, 140, () =>
                {
                    if (_activeTextOverlay == null) TextFormattingBar.Visibility = Visibility.Collapsed;
                });
            }
            return;
        }

        _isUpdatingTextFormat = true;
        try
        {
            var m = overlay.Model;
            BtnTextBold.IsChecked = m.IsBold;
            BtnTextItalic.IsChecked = m.IsItalic;
            BtnTextUnderline.IsChecked = m.IsUnderline;
            BtnTextStrike.IsChecked = m.IsStrikethrough;

            foreach (ComboBoxItem item in CmbTextFontFamily.Items)
            {
                if (string.Equals(item.Content?.ToString(), m.FontFamily, StringComparison.OrdinalIgnoreCase))
                {
                    CmbTextFontFamily.SelectedItem = item;
                    break;
                }
            }

            string targetSize = Math.Round(m.FontSize).ToString(CultureInfo.InvariantCulture);
            foreach (ComboBoxItem item in CmbTextFontSize.Items)
            {
                if (string.Equals(item.Content?.ToString(), targetSize, StringComparison.OrdinalIgnoreCase))
                {
                    CmbTextFontSize.SelectedItem = item;
                    break;
                }
            }

            if (TextFormattingBar.Visibility != Visibility.Visible)
            {
                TextFormattingBar.Visibility = Visibility.Visible;
                TextFormattingBar.Opacity = 0;
                TextFormattingBarTranslate.Y = -8;
                TextFormattingBarTranslate.BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(0, TimeSpan.FromMilliseconds(180)) { EasingFunction = UIAnimationHelper.SmoothOut });
                UIAnimationHelper.AnimateFade(TextFormattingBar, 0, 1, 180);
            }
            else
            {
                TextFormattingBar.BeginAnimation(OpacityProperty, null);
                TextFormattingBar.Opacity = 1;
                TextFormattingBarTranslate.Y = 0;
            }
        }
        finally
        {
            _isUpdatingTextFormat = false;
        }
    }

    private void CmbTextFontFamily_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingTextFormat || _activeTextOverlay == null) return;
        if (CmbTextFontFamily.SelectedItem is ComboBoxItem item && item.Content is string family)
        {
            DataStore.Instance.PushUndoState();
            _activeTextOverlay.Model.FontFamily = family;
            _activeTextOverlay.ApplyFormattingUpdate();
            DataStore.Instance.SaveToDisk();
        }
    }

    private void CmbTextFontSize_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingTextFormat || _activeTextOverlay == null) return;
        if (CmbTextFontSize.SelectedItem is ComboBoxItem item &&
            double.TryParse(item.Content?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double size))
        {
            DataStore.Instance.PushUndoState();
            _activeTextOverlay.Model.FontSize = size;
            _activeTextOverlay.ApplyFormattingUpdate();
            DataStore.Instance.SaveToDisk();
        }
    }

    private void BtnTextFontSmaller_Click(object sender, RoutedEventArgs e)
    {
        if (_activeTextOverlay == null) return;
        DataStore.Instance.PushUndoState();
        _activeTextOverlay.Model.FontSize = Math.Max(10, _activeTextOverlay.Model.FontSize - 2);
        _activeTextOverlay.ApplyFormattingUpdate();
        OnActiveTextOverlayChanged(_activeTextOverlay);
        DataStore.Instance.SaveToDisk();
    }

    private void BtnTextFontLarger_Click(object sender, RoutedEventArgs e)
    {
        if (_activeTextOverlay == null) return;
        DataStore.Instance.PushUndoState();
        _activeTextOverlay.Model.FontSize = Math.Min(72, _activeTextOverlay.Model.FontSize + 2);
        _activeTextOverlay.ApplyFormattingUpdate();
        OnActiveTextOverlayChanged(_activeTextOverlay);
        DataStore.Instance.SaveToDisk();
    }

    private void BtnTextFormat_Click(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingTextFormat || _activeTextOverlay == null) return;
        DataStore.Instance.PushUndoState();
        var m = _activeTextOverlay.Model;
        m.IsBold = BtnTextBold.IsChecked == true;
        m.IsItalic = BtnTextItalic.IsChecked == true;
        m.IsUnderline = BtnTextUnderline.IsChecked == true;
        m.IsStrikethrough = BtnTextStrike.IsChecked == true;
        _activeTextOverlay.ApplyFormattingUpdate();
        DataStore.Instance.SaveToDisk();
    }

    private void BtnTextAlign_Click(object sender, RoutedEventArgs e)
    {
        if (_activeTextOverlay == null || sender is not Button btn || btn.Tag is not string align) return;
        DataStore.Instance.PushUndoState();
        _activeTextOverlay.Model.TextAlignment = align;
        _activeTextOverlay.ApplyFormattingUpdate();
        DataStore.Instance.SaveToDisk();
    }

    private void BtnTextBullets_Click(object sender, RoutedEventArgs e)
    {
        _activeTextOverlay?.ToggleBulletList();
    }

    private void BtnTextClearFormat_Click(object sender, RoutedEventArgs e)
    {
        if (_activeTextOverlay == null) return;
        _activeTextOverlay.ClearFormatting();
        OnActiveTextOverlayChanged(_activeTextOverlay);
    }

    private void BtnTextColor_Click(object sender, RoutedEventArgs e)
    {
        if (_activeTextOverlay == null || sender is not Button btn || btn.Tag is not string hex) return;
        DataStore.Instance.PushUndoState();
        _activeTextOverlay.Model.TextColor = hex;
        _activeTextOverlay.ApplyFormattingUpdate();
        DataStore.Instance.SaveToDisk();
    }

    private void BtnTextCustomColor_Click(object sender, RoutedEventArgs e)
    {
        if (_activeTextOverlay == null) return;
        var picker = new ColorPickerWindow(_activeTextOverlay.Model.TextColor) { Owner = this };
        if (picker.ShowDialog() == true)
        {
            DataStore.Instance.PushUndoState();
            _activeTextOverlay.Model.TextColor = picker.SelectedHex;
            _activeTextOverlay.ApplyFormattingUpdate();
            DataStore.Instance.SaveToDisk();
        }
    }

    private void BtnNoteBgColor_Click(object sender, RoutedEventArgs e)
    {
        if (_activeTextOverlay == null || sender is not Button btn || btn.Tag is not string hex) return;
        DataStore.Instance.PushUndoState();
        _activeTextOverlay.Model.BackgroundColor = hex;
        _activeTextOverlay.Model.IsStickyNote = !string.Equals(hex, "Transparent", StringComparison.OrdinalIgnoreCase);
        _activeTextOverlay.ApplyFormattingUpdate();
        DataStore.Instance.SaveToDisk();
    }

    private void BtnDeleteActiveTextBox_Click(object sender, RoutedEventArgs e)
    {
        if (_activeTextOverlay == null) return;
        var page = DataStore.Instance.ActiveNote?.CurrentPage;
        if (page == null) return;
        var target = _activeTextOverlay.Model;
        DataStore.Instance.PushUndoState();
        page.TextBoxes.Remove(target);
        MainInkCanvas.ClearActiveOverlay();
        DataStore.Instance.NotifyCanvasChanged();
    }

    #endregion

    private void BtnInsertAxes_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new AxesConfigWindow { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            MainInkCanvas.InsertCoordinateAxes(dlg.Options, dlg.AxesSize);
        }
    }

    private void BtnReverseArrow_Click(object sender, RoutedEventArgs e) => MainInkCanvas.ReverseSelectedArrows();

    private void BtnDeleteSelected_Click(object sender, RoutedEventArgs e) => MainInkCanvas.DeleteSelectedStrokes();
    private void BtnCopySelected_Click(object sender, RoutedEventArgs e) => MainInkCanvas.DuplicateSelectedStrokes();

    private void BtnSelectDropdown_Click(object sender, RoutedEventArgs e)
    {
        PopupSelectionMode.IsOpen = !PopupSelectionMode.IsOpen;
    }

    private void BtnSelectMarquee_Click(object sender, RoutedEventArgs e)
    {
        DataStore.Instance.SelectedSelectionMode = Quillfolio.Core.Models.SelectionMode.Marquee;
        DataStore.Instance.SelectedTool = ToolType.Select;
        BtnSelect.IsChecked = true;
        UpdateSelectionModeUI();
        PopupSelectionMode.IsOpen = false;
    }

    private void BtnSelectLasso_Click(object sender, RoutedEventArgs e)
    {
        DataStore.Instance.SelectedSelectionMode = Quillfolio.Core.Models.SelectionMode.Lasso;
        DataStore.Instance.SelectedTool = ToolType.Select;
        BtnSelect.IsChecked = true;
        UpdateSelectionModeUI();
        PopupSelectionMode.IsOpen = false;
    }

    private void UpdateSelectionModeUI()
    {
        bool isLasso = DataStore.Instance.SelectedSelectionMode == Quillfolio.Core.Models.SelectionMode.Lasso;
        ChkSelectMarquee.Visibility = isLasso ? Visibility.Hidden : Visibility.Visible;
        ChkSelectLasso.Visibility = isLasso ? Visibility.Visible : Visibility.Hidden;
        IconSelectMode.Data = Geometry.Parse(isLasso
            ? "M12,3C7.03,3 3,6.58 3,11C3,13.5 4.3,15.72 6.33,17.16C5.6,18.23 5.2,19.45 5.2,20.5C5.2,21.88 6.32,23 7.7,23C9.08,23 10.2,21.88 10.2,20.5C10.2,19.86 9.95,19.28 9.55,18.84C10.33,18.94 11.15,19 12,19C16.97,19 21,15.42 21,11C21,6.58 16.97,3 12,3M12,17C8.13,17 5,14.31 5,11C5,7.69 8.13,5 12,5C15.87,5 19,7.69 19,11C19,14.31 15.87,17 12,17M7.7,21C7.42,21 7.2,20.78 7.2,20.5C7.2,20.22 7.42,20 7.7,20C7.98,20 8.2,20.22 8.2,20.5C8.2,20.78 7.98,21 7.7,21Z"
            : "M11,2H13V5H11V2M11,19H13V22H11V19M2,11H5V13H2V11M19,11H22V13H19V11M12,7L17,21L14.8,21.8L11.5,12.5L7.5,16.5V5.5L12,7Z");
    }

    private void BtnTextSymbols_Click(object sender, RoutedEventArgs e)
    {
        PopupTextSymbols.IsOpen = !PopupTextSymbols.IsOpen;
    }

    private void SidebarTab_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        bool isFav = BtnTabFavorites.IsChecked == true;
        FoldersExplorerCard.Visibility = isFav ? Visibility.Collapsed : Visibility.Visible;
        RefreshNotesList();
    }

    private void BtnToggleFavorite_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is Note note)
        {
            DataStore.Instance.ToggleNoteFavorite(note);
            RefreshNotesList();
        }
    }

    private void MenuNoteToggleFavorite_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.Tag is Note note)
        {
            DataStore.Instance.ToggleNoteFavorite(note);
            RefreshNotesList();
        }
    }

    private void Color_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag != null)
        {
            DataStore.Instance.SelectedColorHex = btn.Tag.ToString()!;
            DataStore.Instance.SaveSettings();
            UpdateActiveColorIndicator();
        }
    }

    private void BtnCustomColor_Click(object sender, RoutedEventArgs e)
    {
        var picker = new ColorPickerWindow(DataStore.Instance.SelectedColorHex) { Owner = this };
        if (picker.ShowDialog() == true)
        {
            DataStore.Instance.SelectedColorHex = picker.SelectedHex;
            DataStore.Instance.SaveSettings();
            UpdateActiveColorIndicator();
        }
    }

    private void BtnThicknessDropdown_Click(object sender, RoutedEventArgs e)
    {
        PopupThickness.IsOpen = !PopupThickness.IsOpen;
    }

    private void SliderCustomThickness_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingThickness) return;
        SetThickness(e.NewValue);
    }

    private void TxtCustomThickness_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ApplyCustomThicknessText();
            PopupThickness.IsOpen = false;
        }
    }

    private void TxtCustomThickness_LostFocus(object sender, RoutedEventArgs e) => ApplyCustomThicknessText();

    private void ApplyCustomThicknessText()
    {
        if (_isUpdatingThickness) return;
        if (double.TryParse(TxtCustomThickness.Text.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out double val))
        {
            SetThickness(val);
        }
    }

    private void BtnUndo_Click(object sender, RoutedEventArgs e) => DataStore.Instance.Undo();

    private void BtnRedo_Click(object sender, RoutedEventArgs e) => DataStore.Instance.Redo();

    private void BtnZoomIn_Click(object sender, RoutedEventArgs e) => ApplyZoom(DataStore.Instance.ZoomLevel + 0.15);

    private void BtnZoomOut_Click(object sender, RoutedEventArgs e) => ApplyZoom(DataStore.Instance.ZoomLevel - 0.15);

    private void BtnZoomReset_Click(object sender, RoutedEventArgs e) => ApplyZoom(1.0);

    private void ApplyZoom(double newZoom)
    {
        double clamped = Math.Clamp(newZoom, 0.2, 4.0);
        DataStore.Instance.ZoomLevel = clamped;
        BtnZoomReset.Content = $"{(int)Math.Round(clamped * 100)}%";

        var matrix = new Matrix();
        matrix.Scale(clamped, clamped);
        MainInkCanvas.CanvasMatrixTransform.Matrix = matrix;
    }

    private void BtnPrevPage_Click(object sender, RoutedEventArgs e)
    {
        var note = DataStore.Instance.ActiveNote;
        if (note != null && note.ActivePageIndex > 0)
            DataStore.Instance.SwitchPage(note.ActivePageIndex - 1);
    }

    private void BtnNextPage_Click(object sender, RoutedEventArgs e)
    {
        var note = DataStore.Instance.ActiveNote;
        if (note != null && note.ActivePageIndex < note.Pages.Count - 1)
            DataStore.Instance.SwitchPage(note.ActivePageIndex + 1);
    }

    private void BtnAddPage_Click(object sender, RoutedEventArgs e) => DataStore.Instance.AddPageToActiveNote();

    private void BtnDeletePage_Click(object sender, RoutedEventArgs e)
    {
        if (ConfirmDialog.ShowConfirm(
                this,
                LocalizationService.Instance.Get("DeletePage"),
                LocalizationService.Instance.Get("ConfirmDeletePage"),
                LocalizationService.Instance.Get("DeleteItem"),
                LocalizationService.Instance.Get("Close"),
                isDestructive: true))
        {
            DataStore.Instance.DeleteCurrentPageFromActiveNote();
        }
    }

    private void BtnExpandWidth_Click(object sender, RoutedEventArgs e) => DataStore.Instance.ExpandActivePage(800, 0);

    private void BtnExpandHeight_Click(object sender, RoutedEventArgs e) => DataStore.Instance.ExpandActivePage(0, 1000);

    private void BtnClearPage_Click(object sender, RoutedEventArgs e)
    {
        if (ConfirmDialog.ShowConfirm(
                this,
                LocalizationService.Instance.Get("ClearCanvas"),
                LocalizationService.Instance.Get("ConfirmClearPage"),
                LocalizationService.Instance.Get("ClearCanvas"),
                LocalizationService.Instance.Get("Close"),
                isDestructive: true))
        {
            DataStore.Instance.ClearCurrentPage();
        }
    }

    private void BtnExportPng_Click(object sender, RoutedEventArgs e)
    {
        var note = DataStore.Instance.ActiveNote;
        if (note == null) return;

        var dialog = new SaveFileDialog
        {
            Filter = "PNG Image (*.png)|*.png",
            FileName = $"{note.Title}_Page{note.ActivePageIndex + 1}.png"
        };

        if (dialog.ShowDialog() == true)
        {
            MainInkCanvas.ExportToPng(dialog.FileName);
            ConfirmDialog.ShowInfo(
                this,
                LocalizationService.Instance.Get("ExportPng"),
                string.Format(LocalizationService.Instance.Get("ExportSuccess"), dialog.FileName));
        }
    }

    private void BtnExportPdf_Click(object sender, RoutedEventArgs e)
    {
        var note = DataStore.Instance.ActiveNote;
        if (note == null) return;

        var dialog = new SaveFileDialog
        {
            Filter = "PDF Document (*.pdf)|*.pdf",
            FileName = $"{note.Title}.pdf"
        };

        if (dialog.ShowDialog() == true)
        {
            MainInkCanvas.ExportAllPagesToPdf(dialog.FileName);
            ConfirmDialog.ShowInfo(
                this,
                LocalizationService.Instance.Get("ExportPdf"),
                string.Format(LocalizationService.Instance.Get("ExportSuccess"), dialog.FileName));
        }
    }

    private void BtnSettings_Click(object sender, RoutedEventArgs e)
    {
        var settingsWin = new SettingsWindow { Owner = this };
        settingsWin.ShowDialog();
        WindowBackdropHelper.ApplyAppTheme(DataStore.Instance.AppTheme, DataStore.Instance.DeskTheme);
        BtnShapeSnap.IsChecked = DataStore.Instance.IsShapeSnapEnabled;
        MainInkCanvas.RefreshHoldDelay();
        RefreshCategorySelectors();
        MainInkCanvas.RenderCanvas();
        if (HomeScreenOverlay.Visibility == Visibility.Visible) RefreshHomeScreen();
    }

    #region Home Screen

    private void BtnHome_Click(object sender, RoutedEventArgs e) => ShowHomeScreen(animate: true);

    private void BtnHomeOpenCanvas_Click(object sender, RoutedEventArgs e) => HideHomeScreen();

    private void BtnHomeNewNote_Click(object sender, RoutedEventArgs e)
    {
        var target = GetEffectiveTargetSubcategory();
        _selectedSubcategory = target;
        DataStore.Instance.CreateNote(targetSubcategory: target);
        MainInkCanvas.RenderCanvas();
        HideHomeScreen();
    }

    private void BtnHomeContinueLast_Click(object sender, RoutedEventArgs e) => HideHomeScreen();

    private void BtnHomeChooseImage_Click(object sender, RoutedEventArgs e)
    {
        HideHomeScreen();
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => MainInkCanvas.InsertImageFromFileDialog());
    }

    private void ShowHomeScreen(bool animate = true)
    {
        RefreshHomeScreen();
        HomeScreenOverlay.Visibility = Visibility.Visible;
        if (animate)
        {
            HomeScreenOverlay.Opacity = 0;
            HomeScreenTranslate.Y = 14;
            HomeScreenTranslate.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(0, TimeSpan.FromMilliseconds(240)) { EasingFunction = UIAnimationHelper.SmoothOut });
            UIAnimationHelper.AnimateFade(HomeScreenOverlay, 0, 1, 220);
        }
        else
        {
            HomeScreenOverlay.BeginAnimation(OpacityProperty, null);
            HomeScreenOverlay.Opacity = 1;
            HomeScreenTranslate.Y = 0;
        }
    }

    private void HideHomeScreen()
    {
        if (HomeScreenOverlay.Visibility != Visibility.Visible) return;
        HomeScreenTranslate.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(-10, TimeSpan.FromMilliseconds(180)) { EasingFunction = UIAnimationHelper.SmoothIn });
        UIAnimationHelper.AnimateFade(HomeScreenOverlay, HomeScreenOverlay.Opacity, 0, 180, () =>
        {
            HomeScreenOverlay.Visibility = Visibility.Collapsed;
            AnimateToolIndicator(GetRadioButtonForTool(DataStore.Instance.SelectedTool), animate: false);
        });
    }

    private void RefreshHomeScreen()
    {
        var allEntries = new List<(Category Cat, Subcategory Sub, Note Note)>();
        foreach (var cat in DataStore.Instance.Categories)
        {
            foreach (var sub in cat.Subcategories)
            {
                foreach (var n in sub.Notes)
                {
                    allEntries.Add((cat, sub, n));
                }
            }
        }

        var favorites = allEntries.Where(x => x.Note.IsFavorite)
            .OrderByDescending(x => x.Note.UpdatedAt)
            .ToList();
        var recent = allEntries.OrderByDescending(x => x.Note.UpdatedAt).Take(8).ToList();

        LblHomeLastNoteTitle.Text = DataStore.Instance.ActiveNote?.Title ?? recent.FirstOrDefault().Note?.Title ?? LocalizationService.Instance.Get("NoNotes");
        bool isPl = LocalizationService.Instance.CurrentLanguage == AppLanguage.Polish;
        LblHomeStats.Text = isPl
            ? $"{allEntries.Count} notatek • {favorites.Count} ulubionych • {DataStore.Instance.Categories.Count} folderów"
            : $"{allEntries.Count} notes • {favorites.Count} favorites • {DataStore.Instance.Categories.Count} folders";

        HomeFavoritesPanel.Children.Clear();
        HomeFavoritesSection.Visibility = favorites.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var item in favorites)
        {
            HomeFavoritesPanel.Children.Add(BuildHomeNoteCard(item.Cat, item.Sub, item.Note));
        }

        HomeRecentNotesPanel.Children.Clear();
        foreach (var item in recent)
        {
            HomeRecentNotesPanel.Children.Add(BuildHomeNoteCard(item.Cat, item.Sub, item.Note));
        }
    }

    private UIElement BuildHomeNoteCard(Category cat, Subcategory sub, Note note)
    {
        bool isActive = ReferenceEquals(note, DataStore.Instance.ActiveNote);
        var cardBtn = new Button
        {
            Style = (Style)FindResource("OutlineButtonStyle"),
            Width = 238,
            Margin = new Thickness(0, 0, 12, 12),
            Padding = new Thickness(14, 12, 14, 12),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Cursor = Cursors.Hand
        };
        if (isActive)
        {
            cardBtn.SetResourceReference(BorderBrushProperty, "PrimaryBrush");
        }

        var root = new StackPanel();
        var headerRow = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        if (note.IsFavorite)
        {
            var star = new TextBlock
            {
                Text = "★",
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(245, 158, 11)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0)
            };
            DockPanel.SetDock(star, Dock.Right);
            headerRow.Children.Add(star);
        }

        var titleTxt = new TextBlock
        {
            Text = note.Title,
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        titleTxt.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        headerRow.Children.Add(titleTxt);
        root.Children.Add(headerRow);

        var folderTxt = new TextBlock
        {
            Text = $"{cat.Name} / {sub.Name}",
            FontSize = 11.5,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 0, 8)
        };
        folderTxt.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        root.Children.Add(folderTxt);

        var footerRow = new DockPanel();
        var badge = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(7, 2, 7, 2),
            Child = new TextBlock
            {
                Text = note.PageCountDisplay,
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold
            }
        };
        badge.SetResourceReference(Border.BackgroundProperty, "PillTrackBrush");
        ((TextBlock)badge.Child).SetResourceReference(TextBlock.ForegroundProperty, "PrimaryBrush");
        DockPanel.SetDock(badge, Dock.Left);

        var dateTxt = new TextBlock
        {
            Text = note.UpdatedAtDisplay,
            FontSize = 10.5,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        dateTxt.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");

        footerRow.Children.Add(badge);
        footerRow.Children.Add(dateTxt);
        root.Children.Add(footerRow);

        cardBtn.Content = root;
        cardBtn.Click += (_, _) =>
        {
            _selectedCategory = cat;
            _selectedSubcategory = sub;
            DataStore.Instance.SelectNote(note);
            RefreshCategorySelectors();
            RefreshNotesList();
            HideHomeScreen();
        };
        return cardBtn;
    }

    #endregion

    private static bool IsTextEditorFocused()
    {
        return Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase or PasswordBox;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_APPCOMMAND = 0x0319;
        if (msg == WM_APPCOMMAND)
        {
            int cmd = (int)(((long)lParam >> 16) & 0x0FFF);
            switch (cmd)
            {
                case 34: // APPCOMMAND_UNDO
                    DataStore.Instance.Undo();
                    handled = true;
                    break;
                case 35: // APPCOMMAND_REDO
                    DataStore.Instance.Redo();
                    handled = true;
                    break;
                case 36: // APPCOMMAND_COPY
                    if (!IsTextEditorFocused())
                    {
                        MainInkCanvas.CopySelectedStrokes();
                        handled = true;
                    }
                    break;
                case 38: // APPCOMMAND_PASTE
                    if (!IsTextEditorFocused())
                    {
                        MainInkCanvas.PasteFromClipboard();
                        handled = true;
                    }
                    break;
                case 53: // APPCOMMAND_DELETE
                    if (!IsTextEditorFocused())
                    {
                        MainInkCanvas.DeleteActiveOverlayOrSelected();
                        handled = true;
                    }
                    break;
                case 1: // APPCOMMAND_BROWSER_BACKWARD (common Wacom/Huion Express Key default)
                    BtnPrevPage_Click(this, new RoutedEventArgs());
                    handled = true;
                    break;
                case 2: // APPCOMMAND_BROWSER_FORWARD (common Wacom/Huion Express Key default)
                    BtnNextPage_Click(this, new RoutedEventArgs());
                    handled = true;
                    break;
            }
        }
        return IntPtr.Zero;
    }

    private void MainWindow_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.XButton1)
        {
            DataStore.Instance.Undo();
            e.Handled = true;
        }
        else if (e.ChangedButton == MouseButton.XButton2)
        {
            DataStore.Instance.Redo();
            e.Handled = true;
        }
    }

    private void ToggleFullscreen()
    {
        if (_isFullscreen)
        {
            _isFullscreen = false;
            WindowStyle = _preFullscreenStyle;
            ResizeMode = _preFullscreenResizeMode;
            WindowState = _preFullscreenState;
        }
        else
        {
            _isFullscreen = true;
            _preFullscreenState = WindowState;
            _preFullscreenStyle = WindowStyle;
            _preFullscreenResizeMode = ResizeMode;

            if (WindowState == WindowState.Maximized)
            {
                WindowState = WindowState.Normal;
            }

            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
        }
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.Escape)
        {
            MainInkCanvas.ClearActiveOverlay();
            MainInkCanvas.ClearSelection();
            e.Handled = true;
            return;
        }

        bool textFocused = IsTextEditorFocused();
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        bool alt = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);

        // Tablet Express Keys often emit BrowserBack/Forward
        if (!textFocused)
        {
            if (key == Key.BrowserBack) { DataStore.Instance.Undo(); e.Handled = true; return; }
            if (key == Key.BrowserForward) { DataStore.Instance.Redo(); e.Handled = true; return; }
        }

        // Dispatch customizable keyboard shortcuts from DataStore
        string keyName = key.ToString();
        foreach (var action in Enum.GetValues<ShortcutAction>())
        {
            if (textFocused && !ShortcutBinding.AllowsWhenTextFocused(action)) continue;
            var binding = DataStore.Instance.GetShortcut(action);
            if (binding != null && binding.Matches(keyName, ctrl, shift, alt))
            {
                ExecuteShortcutAction(action);
                e.Handled = true;
                return;
            }
        }

        if (textFocused) return;

        // Additional convenient fixed aliases (Ctrl+Shift+Z Redo & D1..D8 tool slots)
        if (ctrl && shift && key == Key.Z)
        {
            DataStore.Instance.Redo();
            e.Handled = true;
            return;
        }

        if (!ctrl && !alt)
        {
            ToolType? slotTool = key switch
            {
                Key.D1 => ToolType.Cursor,
                Key.D2 => ToolType.Pen,
                Key.D3 => ToolType.FountainPen,
                Key.D4 => ToolType.Pencil,
                Key.D5 => ToolType.Highlighter,
                Key.D6 => ToolType.Eraser,
                Key.D7 => ToolType.Select,
                Key.D8 => ToolType.Laser,
                Key.D0 => ToolType.None,
                _ => null
            };
            if (slotTool.HasValue)
            {
                DataStore.Instance.SelectedTool = slotTool.Value;
                SelectToolRadioButton(slotTool.Value);
                e.Handled = true;
            }
        }
    }

    private void ExecuteShortcutAction(ShortcutAction action)
    {
        switch (action)
        {
            case ShortcutAction.ToolCursor: SelectTool(ToolType.Cursor); break;
            case ShortcutAction.ToolPen: SelectTool(ToolType.Pen); break;
            case ShortcutAction.ToolFountainPen: SelectTool(ToolType.FountainPen); break;
            case ShortcutAction.ToolPencil: SelectTool(ToolType.Pencil); break;
            case ShortcutAction.ToolHighlighter: SelectTool(ToolType.Highlighter); break;
            case ShortcutAction.ToolEraser: SelectTool(ToolType.Eraser); break;
            case ShortcutAction.ToolSelect: SelectTool(ToolType.Select); break;
            case ShortcutAction.ToolLaser: SelectTool(ToolType.Laser); break;
            case ShortcutAction.ToolNone: SelectTool(ToolType.None); break;
            case ShortcutAction.ToggleFullscreen: ToggleFullscreen(); break;
            case ShortcutAction.ToggleSidebar: BtnToggleSidebar_Click(this, new RoutedEventArgs()); break;
            case ShortcutAction.ToggleBottomBar: BtnToggleBottomBar_Click(this, new RoutedEventArgs()); break;
            case ShortcutAction.OpenHome:
                if (HomeScreenOverlay.Visibility == Visibility.Visible) HideHomeScreen();
                else ShowHomeScreen(animate: true);
                break;
            case ShortcutAction.OpenSettings: BtnSettings_Click(this, new RoutedEventArgs()); break;
            case ShortcutAction.NewNote: BtnNewNote_Click(this, new RoutedEventArgs()); break;
            case ShortcutAction.AddPage: BtnAddPage_Click(this, new RoutedEventArgs()); break;
            case ShortcutAction.PrevPage: BtnPrevPage_Click(this, new RoutedEventArgs()); break;
            case ShortcutAction.NextPage: BtnNextPage_Click(this, new RoutedEventArgs()); break;
            case ShortcutAction.Undo: DataStore.Instance.Undo(); break;
            case ShortcutAction.Redo: DataStore.Instance.Redo(); break;
            case ShortcutAction.CopySelection: MainInkCanvas.CopySelectedStrokes(); break;
            case ShortcutAction.Paste: MainInkCanvas.PasteFromClipboard(); break;
            case ShortcutAction.DuplicateSelection: MainInkCanvas.DuplicateSelectedStrokes(); break;
            case ShortcutAction.SelectAll: MainInkCanvas.SelectAllStrokes(); break;
            case ShortcutAction.DeleteSelected: MainInkCanvas.DeleteActiveOverlayOrSelected(); break;
            case ShortcutAction.ZoomIn: ApplyZoom(DataStore.Instance.ZoomLevel + 0.15); break;
            case ShortcutAction.ZoomOut: ApplyZoom(DataStore.Instance.ZoomLevel - 0.15); break;
            case ShortcutAction.ZoomReset: ApplyZoom(1.0); break;
            case ShortcutAction.BrushSmaller: SetThickness(DataStore.Instance.SelectedThickness - 0.5); break;
            case ShortcutAction.BrushLarger: SetThickness(DataStore.Instance.SelectedThickness + 0.5); break;
        }
    }

    private void SelectTool(ToolType tool)
    {
        DataStore.Instance.SelectedTool = tool;
        SelectToolRadioButton(tool);
    }
}
