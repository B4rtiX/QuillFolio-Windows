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
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Quillfolio.Core.Geometry;
using Quillfolio.Core.Models;
using Quillfolio.Core.Services;
using Page = Quillfolio.Core.Models.Page;

namespace Quillfolio.WPF.Controls;

public partial class InkCanvasControl : UserControl
{
    private readonly DrawingVisual _paperVisual = new();
    private readonly DrawingVisual _committedVisual = new();
    private readonly DrawingVisual _activeVisual = new();
    private readonly DrawingVisual _cursorVisual = new();
    private readonly List<PointD> _activeStrokePoints = new();
    private (PaperStyle Style, PaperColorTheme Color, double Width, double Height, double Spacing)? _lastPaperKey;

    private sealed class CachedStrokeVisual
    {
        public required DrawingGroup Group { get; init; }
        public required int PointCount { get; init; }
        public required RectD? ShapeBounds { get; init; }
        public required string ColorHex { get; init; }
        public required double Thickness { get; init; }
        public required double PressureFactor { get; init; }
        public required bool IsReversed { get; init; }
    }

    private readonly ConditionalWeakTable<DrawingStroke, CachedStrokeVisual> _strokeVisualCache = new();
    private bool _pendingDragUndo;

    private bool _isDrawing;
    private bool _isErasing;
    private bool _eraserUndoPushed;
    private bool _isPanning;
    private Point _panStartPoint;
    private double _panStartH;
    private double _panStartV;

    // Selection, Resize & Bend Handle state
    private bool _isSelectingMarquee;
    private bool _isSelectingLasso;
    private readonly List<Point> _lassoPoints = new();
    private bool _isDraggingSelection;
    private int _activeResizeHandleIndex = -1; // 0..7 for 8 bbox handles
    private int _activeControlPointIndex = -1; // 0..3 for bendable 3-segment lines/arrows
    private DrawingStroke? _activeHandleStroke;
    private RectD _resizeStartBounds;
    private List<PointD>? _resizeStartPoints;
    private PointD? _resizeStartStartPt;
    private PointD? _resizeStartEndPt;
    private Point _selectionStart;
    private Point _selectionCurrent;
    private Point _dragSelectionLast;

    // Laser pointer state
    private readonly List<(Point Point, long TimestampMs)> _laserTrail = new();
    private readonly DispatcherTimer _laserTimer;
    private bool _isLaserActive;

    // Eraser cursor position
    private Point? _eraserCursorPos;

    // Active Overlay Focus Tracking
    private StickyNoteOverlayControl? _activeTextOverlay;
    private TableOverlayControl? _activeTableOverlay;
    private ImageOverlayControl? _activeImageOverlay;
    private Guid? _pendingActivateTextId;
    private Guid? _pendingActivateTableId;
    private Guid? _pendingActivateImageId;

    // Input capture / context / clipboard state
    private bool _suppressCancelOnRelease;
    private bool _isCursorPanning;
    private Point _cursorPanStartViewport;
    private double _cursorPanStartH;
    private double _cursorPanStartV;
    private Point? _lastContextMenuCanvasPoint;
    private readonly List<DrawingStroke> _strokeClipboard = new();

    // Hold-to-snap timer
    private readonly DispatcherTimer _holdToSnapTimer;
    private PointD _lastHoldPoint;

    // Velocity tracking for mouse pseudo-pressure
    private PointD? _lastMousePoint;

    public StickyNoteOverlayControl? ActiveTextOverlay => _activeTextOverlay;

    public event Action<double>? ZoomRequested;
    public event Action? SelectionChanged;
    public event Action? ShapeInsertedAndSelected;
    public event Action<StickyNoteOverlayControl?>? ActiveTextOverlayChanged;

    public InkCanvasControl()
    {
        InitializeComponent();

        VectorLayer.Children.Add(new VisualHost(_paperVisual));
        VectorLayer.Children.Add(new VisualHost(_committedVisual));
        VectorLayer.Children.Add(new VisualHost(_activeVisual));
        TopCursorLayer.Children.Add(new VisualHost(_cursorVisual));

        PinnedOverlayLayer.SizeChanged += (_, _) => ClampPinnedOverlaysToViewport();

        // Hardware Stylus & Mouse Event Subscriptions
        PaperCanvas.StylusDown += PaperCanvas_StylusDown;
        PaperCanvas.StylusMove += PaperCanvas_StylusMove;
        PaperCanvas.StylusUp += PaperCanvas_StylusUp;
        PaperCanvas.StylusInAirMove += PaperCanvas_StylusInAirMove;
        PaperCanvas.StylusOutOfRange += (_, _) =>
        {
            if (_eraserCursorPos.HasValue)
            {
                _eraserCursorPos = null;
                RenderTopCursorLayer();
            }
        };
        PaperCanvas.LostStylusCapture += (_, _) =>
        {
            if (!_suppressCancelOnRelease) CancelActiveInput();
        };

        PaperCanvas.MouseDown += PaperCanvas_MouseDown;
        PaperCanvas.MouseMove += PaperCanvas_MouseMove;
        PaperCanvas.MouseUp += PaperCanvas_MouseUp;
        PaperCanvas.MouseLeave += (_, _) =>
        {
            if (_eraserCursorPos.HasValue)
            {
                _eraserCursorPos = null;
                RenderTopCursorLayer();
            }
        };
        PaperCanvas.LostMouseCapture += (_, _) =>
        {
            if (!_suppressCancelOnRelease) CancelActiveInput();
        };

        PaperCanvas.ContextMenu = new ContextMenu();
        PaperCanvas.ContextMenuOpening += PaperCanvas_ContextMenuOpening;

        CanvasScrollViewer.PreviewMouseWheel += CanvasScrollViewer_PreviewMouseWheel;

        // Auto-Snap Hold Timer
        _holdToSnapTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(DataStore.Instance.ShapeHoldDelayMs)
        };
        _holdToSnapTimer.Tick += HoldToSnapTimer_Tick;

        // Laser fade timer (~30 fps when laser points exist)
        _laserTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _laserTimer.Tick += LaserTimer_Tick;

        DataStore.Instance.CanvasContentChanged += () =>
        {
            if (Dispatcher.CheckAccess()) RenderCanvas();
            else Dispatcher.BeginInvoke(RenderCanvas);
        };
        Loaded += (_, _) =>
        {
            UpdateToolInteractionMode();
            RenderCanvas();
        };
    }

    public void UpdateToolInteractionMode()
    {
        var tool = DataStore.Instance.SelectedTool;
        ImagesLayer.IsHitTestVisible = tool is ToolType.Cursor or ToolType.Select or ToolType.None;
        if (tool != ToolType.Eraser && _eraserCursorPos.HasValue)
        {
            _eraserCursorPos = null;
            RenderTopCursorLayer();
        }
        if (!_isPanning && !_isCursorPanning)
        {
            PaperCanvas.Cursor = tool switch
            {
                ToolType.Cursor => Cursors.Arrow,
                ToolType.None => Cursors.Hand,
                ToolType.Select => Cursors.Cross,
                ToolType.Laser => Cursors.Cross,
                ToolType.Eraser => Cursors.Cross,
                _ => Cursors.Pen
            };
        }
    }

    public void RefreshHoldDelay()
    {
        _holdToSnapTimer.Interval = TimeSpan.FromMilliseconds(
            Math.Clamp(DataStore.Instance.ShapeHoldDelayMs, 250, 800));
    }

    public Point GetViewportCenterCanvasPoint()
    {
        double zoom = Math.Max(0.2, DataStore.Instance.ZoomLevel);
        double centerX = (CanvasScrollViewer.HorizontalOffset + CanvasScrollViewer.ViewportWidth * 0.5) / zoom;
        double centerY = (CanvasScrollViewer.VerticalOffset + CanvasScrollViewer.ViewportHeight * 0.5) / zoom;
        return new Point(Math.Max(60, centerX), Math.Max(60, centerY));
    }

    private void EnsureCanvasCoversPoint(PointD pt)
    {
        var page = DataStore.Instance.ActiveNote?.CurrentPage;
        if (page == null) return;

        bool expanded = false;
        if (pt.X > page.Width - 150)
        {
            page.Width += 800;
            expanded = true;
        }
        if (pt.Y > page.Height - 150)
        {
            page.Height += 800;
            expanded = true;
        }

        if (expanded)
        {
            PaperCanvas.Width = page.Width;
            PaperCanvas.Height = page.Height;
            ImagesLayer.Width = page.Width;
            ImagesLayer.Height = page.Height;
            VectorLayer.Width = page.Width;
            VectorLayer.Height = page.Height;
            OverlayLayer.Width = page.Width;
            OverlayLayer.Height = page.Height;
            SelectionHudLayer.Width = page.Width;
            SelectionHudLayer.Height = page.Height;
            TopCursorLayer.Width = page.Width;
            TopCursorLayer.Height = page.Height;
            EnsurePaperRendered(page);
            RenderCommittedLayer(page);
        }
    }

    public void RenderCanvas()
    {
        var page = DataStore.Instance.ActiveNote?.CurrentPage;
        if (page == null) return;

        PaperCanvas.Width = page.Width;
        PaperCanvas.Height = page.Height;
        ImagesLayer.Width = page.Width;
        ImagesLayer.Height = page.Height;
        VectorLayer.Width = page.Width;
        VectorLayer.Height = page.Height;
        OverlayLayer.Width = page.Width;
        OverlayLayer.Height = page.Height;
        SelectionHudLayer.Width = page.Width;
        SelectionHudLayer.Height = page.Height;
        TopCursorLayer.Width = page.Width;
        TopCursorLayer.Height = page.Height;
        PaperCanvas.Background = GetPaperBackgroundBrush(page.PaperColor);
        UpdateToolInteractionMode();

        EnsurePaperRendered(page);
        RenderCommittedLayer(page);
        RenderActiveLayer();
        RenderTopCursorLayer();
        SyncOverlays(page);
    }

    private void EnsurePaperRendered(Page page)
    {
        double spacing = Math.Clamp(DataStore.Instance.GuidelineSpacing, 16.0, 64.0);
        var key = (page.PaperStyle, page.PaperColor, page.Width, page.Height, spacing);
        if (_lastPaperKey.HasValue && _lastPaperKey.Value == key) return;

        _lastPaperKey = key;
        using DrawingContext dc = _paperVisual.RenderOpen();
        DrawPaperGuidelines(dc, page.PaperStyle, page.PaperColor, page.Width, page.Height, spacing);
    }

    private void RenderCommittedLayer(Page page)
    {
        using DrawingContext dc = _committedVisual.RenderOpen();

        foreach (var stroke in page.Strokes)
        {
            DrawSingleStrokeCached(dc, stroke);
            if (stroke.IsSelected)
            {
                DrawSelectionHighlight(dc, stroke);
            }
        }

        UpdateSelectionFloatingHud(page);
    }

    private void DrawSingleStrokeCached(DrawingContext dc, DrawingStroke stroke)
    {
        double pf = DataStore.Instance.StylusPressureFactor;
        if (_strokeVisualCache.TryGetValue(stroke, out var entry) &&
            entry.PointCount == stroke.Points.Count &&
            entry.ShapeBounds == stroke.ShapeBounds &&
            entry.ColorHex == stroke.ColorHex &&
            Math.Abs(entry.Thickness - stroke.Thickness) < 1e-6 &&
            Math.Abs(entry.PressureFactor - pf) < 1e-6 &&
            entry.IsReversed == stroke.IsReversed)
        {
            dc.DrawDrawing(entry.Group);
            return;
        }

        var group = new DrawingGroup();
        using (var groupDc = group.Open())
        {
            DrawSingleStroke(groupDc, stroke);
        }
        group.Freeze();

        _strokeVisualCache.AddOrUpdate(stroke, new CachedStrokeVisual
        {
            Group = group,
            PointCount = stroke.Points.Count,
            ShapeBounds = stroke.ShapeBounds,
            ColorHex = stroke.ColorHex,
            Thickness = stroke.Thickness,
            PressureFactor = pf,
            IsReversed = stroke.IsReversed
        });

        dc.DrawDrawing(group);
    }

    private void RenderActiveLayer()
    {
        using DrawingContext dc = _activeVisual.RenderOpen();

        // 1. Live stroke preview without O(N^2) spline resampling per stylus packet
        if (_isDrawing && _activeStrokePoints.Count > 1)
        {
            var tool = DataStore.Instance.SelectedTool;
            var liveStroke = new DrawingStroke
            {
                Tool = tool,
                ColorHex = DataStore.Instance.SelectedColorHex,
                Thickness = DataStore.Instance.SelectedThickness,
                Opacity = GetDefaultOpacity(tool),
                Points = _activeStrokePoints
            };
            DrawSingleStroke(dc, liveStroke);
        }

        // 2. Selection marquee / lasso
        if (_isSelectingMarquee)
        {
            var rect = new Rect(_selectionStart, _selectionCurrent);
            var fill = new SolidColorBrush(Color.FromArgb(30, 124, 58, 237));
            var border = new Pen(new SolidColorBrush(Color.FromRgb(124, 58, 237)), 1.5)
            {
                DashStyle = DashStyles.Dash
            };
            fill.Freeze();
            border.Freeze();
            dc.DrawRectangle(fill, border, rect);
        }
        else if (_isSelectingLasso && _lassoPoints.Count >= 2)
        {
            var fill = new SolidColorBrush(Color.FromArgb(28, 124, 58, 237));
            var border = new Pen(new SolidColorBrush(Color.FromRgb(124, 58, 237)), 1.6)
            {
                DashStyle = DashStyles.Dash,
                LineJoin = PenLineJoin.Round
            };
            fill.Freeze();
            border.Freeze();

            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(_lassoPoints[0], true, true);
                for (int i = 1; i < _lassoPoints.Count; i++)
                {
                    ctx.LineTo(_lassoPoints[i], true, true);
                }
            }
            geo.Freeze();
            dc.DrawGeometry(fill, border, geo);
        }

        // 3. Uniform continuous Laser pointer trail
        if (_laserTrail.Count > 1)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            int fadeMs = Math.Max(300, DataStore.Instance.LaserFadeMs);
            var activePts = new List<Point>();
            foreach (var item in _laserTrail)
            {
                if (now - item.TimestampMs <= fadeMs)
                {
                    activePts.Add(item.Point);
                }
            }

            if (activePts.Count >= 2)
            {
                var geo = new StreamGeometry();
                using (var ctx = geo.Open())
                {
                    ctx.BeginFigure(activePts[0], false, false);
                    for (int i = 1; i < activePts.Count - 1; i++)
                    {
                        Point mid = new Point(
                            (activePts[i].X + activePts[i + 1].X) * 0.5,
                            (activePts[i].Y + activePts[i + 1].Y) * 0.5);
                        ctx.QuadraticBezierTo(activePts[i], mid, true, true);
                    }
                    ctx.LineTo(activePts[^1], true, true);
                }
                geo.Freeze();

                var outerGlow = new Pen(new SolidColorBrush(Color.FromArgb(80, 239, 68, 68)), 14.0)
                {
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round,
                    LineJoin = PenLineJoin.Round
                };
                var midGlow = new Pen(new SolidColorBrush(Color.FromArgb(180, 239, 68, 68)), 6.5)
                {
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round,
                    LineJoin = PenLineJoin.Round
                };
                var innerCore = new Pen(new SolidColorBrush(Color.FromArgb(245, 255, 255, 255)), 2.5)
                {
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round,
                    LineJoin = PenLineJoin.Round
                };
                outerGlow.Freeze();
                midGlow.Freeze();
                innerCore.Freeze();

                dc.DrawGeometry(null, outerGlow, geo);
                dc.DrawGeometry(null, midGlow, geo);
                dc.DrawGeometry(null, innerCore, geo);

                var tipBrush = new SolidColorBrush(Color.FromRgb(239, 68, 68));
                var tipCore = new SolidColorBrush(Colors.White);
                tipBrush.Freeze();
                tipCore.Freeze();
                dc.DrawEllipse(tipBrush, null, activePts[^1], 6.5, 6.5);
                dc.DrawEllipse(tipCore, null, activePts[^1], 3.0, 3.0);
            }
        }
    }

    private void RenderTopCursorLayer()
    {
        using DrawingContext dc = _cursorVisual.RenderOpen();
        if (DataStore.Instance.SelectedTool == ToolType.Eraser && _eraserCursorPos.HasValue)
        {
            double radius = GetEraserRadius();
            var fill = new SolidColorBrush(Color.FromArgb(32, 220, 38, 38));
            var outerHalo = new Pen(new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)), 3.2);
            var ringPen = new Pen(new SolidColorBrush(Color.FromArgb(225, 220, 38, 38)), 1.6);
            fill.Freeze();
            outerHalo.Freeze();
            ringPen.Freeze();
            dc.DrawEllipse(null, outerHalo, _eraserCursorPos.Value, radius, radius);
            dc.DrawEllipse(fill, ringPen, _eraserCursorPos.Value, radius, radius);
        }
    }

    private static double GetDefaultOpacity(ToolType tool) => tool switch
    {
        ToolType.Pencil => 0.65,
        ToolType.Highlighter => 0.45,
        _ => 1.0
    };

    private void DrawSingleStroke(DrawingContext dc, DrawingStroke stroke)
    {
        if (stroke.Points.Count < 2) return;

        Color strokeColor;
        try
        {
            strokeColor = (Color)ColorConverter.ConvertFromString(stroke.ColorHex);
        }
        catch
        {
            strokeColor = Colors.Black;
        }

        double baseThickness = stroke.Thickness;
        double opacity = stroke.Opacity > 0 ? Math.Clamp(stroke.Opacity, 0.1, 1.0) : GetDefaultOpacity(stroke.Tool);

        if (stroke.Tool == ToolType.Highlighter)
        {
            opacity = Math.Min(opacity, 0.45);
            baseThickness = Math.Max(stroke.Thickness * 3.2, 14.0);
        }
        else if (stroke.Tool == ToolType.Pencil)
        {
            opacity = Math.Min(opacity, 0.65);
            baseThickness = Math.Max(1.0, stroke.Thickness * 0.85);
        }

        strokeColor = Color.FromArgb((byte)(opacity * 255), strokeColor.R, strokeColor.G, strokeColor.B);

        var brush = new SolidColorBrush(strokeColor);
        brush.Freeze();

        var pen = new Pen(brush, baseThickness)
        {
            StartLineCap = stroke.Tool == ToolType.Highlighter ? PenLineCap.Flat : PenLineCap.Round,
            EndLineCap = stroke.Tool == ToolType.Highlighter ? PenLineCap.Flat : PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };
        pen.Freeze();

        // Recognized Shapes
        if (stroke.ShapeKind != ShapeKind.None && stroke.Points.Count >= 2)
        {
            DrawGeometricShape(dc, stroke, pen);
            return;
        }

        // Highlighter or uniform-pressure strokes use a single smooth StreamGeometry (zero joint artifacts)
        if (stroke.Tool == ToolType.FountainPen)
        {
            DrawFountainPenRibbon(dc, stroke.Points, baseThickness, brush);
            return;
        }

        bool hasPressureVariation = false;
        if (stroke.Tool != ToolType.Highlighter)
        {
            double minP = stroke.Points[0].Pressure, maxP = stroke.Points[0].Pressure;
            for (int i = 1; i < stroke.Points.Count; i++)
            {
                double p = stroke.Points[i].Pressure;
                if (p < minP) minP = p;
                if (p > maxP) maxP = p;
                if (maxP - minP > 0.06)
                {
                    hasPressureVariation = true;
                    break;
                }
            }
        }

        if (stroke.Tool == ToolType.Highlighter || !hasPressureVariation)
        {
            double uniformWidth = stroke.Tool == ToolType.Highlighter
                ? baseThickness
                : Math.Clamp(baseThickness * Math.Pow(stroke.Points[0].Pressure * DataStore.Instance.StylusPressureFactor, 2.0), 0.6, baseThickness * 2.5);
            var uniformPen = new Pen(brush, uniformWidth)
            {
                StartLineCap = stroke.Tool == ToolType.Highlighter ? PenLineCap.Flat : PenLineCap.Round,
                EndLineCap = stroke.Tool == ToolType.Highlighter ? PenLineCap.Flat : PenLineCap.Round,
                LineJoin = PenLineJoin.Round
            };
            uniformPen.Freeze();

            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(new Point(stroke.Points[0].X, stroke.Points[0].Y), false, false);
                for (int i = 1; i < stroke.Points.Count - 1; i++)
                {
                    Point mid = new Point(
                        (stroke.Points[i].X + stroke.Points[i + 1].X) * 0.5,
                        (stroke.Points[i].Y + stroke.Points[i + 1].Y) * 0.5);
                    ctx.QuadraticBezierTo(new Point(stroke.Points[i].X, stroke.Points[i].Y), mid, true, true);
                }
                ctx.LineTo(new Point(stroke.Points[^1].X, stroke.Points[^1].Y), true, true);
            }
            geometry.Freeze();
            dc.DrawGeometry(null, uniformPen, geometry);
            return;
        }

        // Variable-width pressure-sensitive rendering as a single smooth filled ribbon (zero jagged joints!)
        DrawVariableWidthRibbon(dc, stroke.Points, baseThickness, brush);
    }

    private void DrawVariableWidthRibbon(DrawingContext dc, List<PointD> pts, double baseThickness, Brush brush)
    {
        int n = pts.Count;
        if (n < 2) return;

        double sensitivity = DataStore.Instance.StylusPressureFactor;
        var leftSide = new Point[n];
        var rightSide = new Point[n];

        for (int i = 0; i < n; i++)
        {
            double dx, dy;
            if (i == 0)
            {
                dx = pts[1].X - pts[0].X;
                dy = pts[1].Y - pts[0].Y;
            }
            else if (i == n - 1)
            {
                dx = pts[n - 1].X - pts[n - 2].X;
                dy = pts[n - 1].Y - pts[n - 2].Y;
            }
            else
            {
                dx = pts[i + 1].X - pts[i - 1].X;
                dy = pts[i + 1].Y - pts[i - 1].Y;
            }

            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 0.0001) { dx = 1; dy = 0; len = 1; }
            double nx = -dy / len;
            double ny = dx / len;

            double effP = Math.Clamp(Math.Pow(pts[i].Pressure * sensitivity, 2.0), 0.25, 2.5);
            double halfW = Math.Clamp(baseThickness * effP, 0.6, baseThickness * 2.5) * 0.5;

            leftSide[i] = new Point(pts[i].X + nx * halfW, pts[i].Y + ny * halfW);
            rightSide[i] = new Point(pts[i].X - nx * halfW, pts[i].Y - ny * halfW);
        }

        var geo = new StreamGeometry { FillRule = FillRule.Nonzero };
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(leftSide[0], true, true);
            for (int i = 1; i < n; i++)
            {
                ctx.LineTo(leftSide[i], true, true);
            }
            for (int i = n - 1; i >= 0; i--)
            {
                ctx.LineTo(rightSide[i], true, true);
            }
        }
        geo.Freeze();
        dc.DrawGeometry(brush, null, geo);

        // Smooth round start & end caps
        double startR = leftSide[0].X == rightSide[0].X && leftSide[0].Y == rightSide[0].Y
            ? baseThickness * 0.5
            : Math.Sqrt(Math.Pow(leftSide[0].X - rightSide[0].X, 2) + Math.Pow(leftSide[0].Y - rightSide[0].Y, 2)) * 0.5;
        double endR = Math.Sqrt(Math.Pow(leftSide[^1].X - rightSide[^1].X, 2) + Math.Pow(leftSide[^1].Y - rightSide[^1].Y, 2)) * 0.5;
        dc.DrawEllipse(brush, null, new Point(pts[0].X, pts[0].Y), startR, startR);
        dc.DrawEllipse(brush, null, new Point(pts[^1].X, pts[^1].Y), endR, endR);
    }

    private void DrawFountainPenRibbon(DrawingContext dc, List<PointD> pts, double baseThickness, Brush brush)
    {
        int n = pts.Count;
        if (n < 2) return;

        // Classic 38-degree angled chisel fountain pen nib
        double nibAngleRad = DataStore.Instance.FountainNibAngleDegrees * Math.PI / 180.0;
        double nibUx = Math.Cos(nibAngleRad);
        double nibUy = Math.Sin(nibAngleRad);
        double sensitivity = DataStore.Instance.StylusPressureFactor;

        var leftSide = new Point[n];
        var rightSide = new Point[n];

        for (int i = 0; i < n; i++)
        {
            double dx, dy;
            if (i == 0)
            {
                dx = pts[1].X - pts[0].X;
                dy = pts[1].Y - pts[0].Y;
            }
            else if (i == n - 1)
            {
                dx = pts[n - 1].X - pts[n - 2].X;
                dy = pts[n - 1].Y - pts[n - 2].Y;
            }
            else
            {
                dx = pts[i + 1].X - pts[i - 1].X;
                dy = pts[i + 1].Y - pts[i - 1].Y;
            }

            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 0.0001) { dx = 1; dy = 0; len = 1; }
            double tx = dx / len;
            double ty = dy / len;
            double nx = -ty;
            double ny = tx;

            // Nib projection perpendicular to stroke direction creates natural classic thick/thin contrast
            double cross = Math.Abs(tx * nibUy - ty * nibUx);
            double nibFactor = 0.28 + 1.15 * cross;

            double pressFactor = Math.Clamp(Math.Pow(pts[i].Pressure * sensitivity, 1.35), 0.65, 1.85);
            double edgeTaper = 1.0;
            if (i == 0 || i == n - 1) edgeTaper = 0.78;

            double halfW = Math.Clamp(baseThickness * nibFactor * pressFactor * edgeTaper, 0.5, baseThickness * 2.8) * 0.5;

            leftSide[i] = new Point(pts[i].X + nx * halfW, pts[i].Y + ny * halfW);
            rightSide[i] = new Point(pts[i].X - nx * halfW, pts[i].Y - ny * halfW);
        }

        var geo = new StreamGeometry { FillRule = FillRule.Nonzero };
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(leftSide[0], true, true);
            for (int i = 1; i < n; i++)
            {
                ctx.LineTo(leftSide[i], true, true);
            }
            for (int i = n - 1; i >= 0; i--)
            {
                ctx.LineTo(rightSide[i], true, true);
            }
        }
        geo.Freeze();
        dc.DrawGeometry(brush, null, geo);

        double startR = Math.Sqrt(Math.Pow(leftSide[0].X - rightSide[0].X, 2) + Math.Pow(leftSide[0].Y - rightSide[0].Y, 2)) * 0.5;
        double endR = Math.Sqrt(Math.Pow(leftSide[^1].X - rightSide[^1].X, 2) + Math.Pow(leftSide[^1].Y - rightSide[^1].Y, 2)) * 0.5;
        dc.DrawEllipse(brush, null, new Point(pts[0].X, pts[0].Y), startR, startR);
        dc.DrawEllipse(brush, null, new Point(pts[^1].X, pts[^1].Y), endR, endR);
    }

    private void DrawGeometricShape(DrawingContext dc, DrawingStroke stroke, Pen pen)
    {
        switch (stroke.ShapeKind)
        {
            case ShapeKind.Circle:
            case ShapeKind.Ellipse:
                if (stroke.ShapeBounds.HasValue)
                {
                    var b = stroke.ShapeBounds.Value;
                    dc.DrawEllipse(null, pen, new Point(b.CenterX, b.CenterY), b.Width / 2.0, b.Height / 2.0);
                }
                break;

            case ShapeKind.RoundedRectangle:
                if (stroke.ShapeBounds.HasValue)
                {
                    var b = stroke.ShapeBounds.Value;
                    double rad = Math.Min(b.Width, b.Height) * 0.16;
                    dc.DrawRoundedRectangle(null, pen, new Rect(b.X, b.Y, b.Width, b.Height), rad, rad);
                }
                break;

            case ShapeKind.Cloud:
                if (stroke.ShapeBounds.HasValue)
                {
                    DrawCloudShape(dc, stroke.ShapeBounds.Value, pen);
                }
                break;

            case ShapeKind.CoordinateAxes:
                if (stroke.ShapeBounds.HasValue)
                {
                    DrawCoordinateAxes(dc, stroke.ShapeBounds.Value, pen, stroke.Thickness, stroke.AxesOptions);
                }
                break;

            case ShapeKind.Line:
                if (stroke.Points.Count >= 3)
                {
                    DrawSmoothCurveThroughPoints(dc, stroke.Points, pen);
                }
                else
                {
                    dc.DrawLine(pen, new Point(stroke.Points[0].X, stroke.Points[0].Y), new Point(stroke.Points[^1].X, stroke.Points[^1].Y));
                }
                break;

            case ShapeKind.Arrow:
            case ShapeKind.DoubleArrow:
            {
                double headLen = Math.Max(16.0, stroke.Thickness * 4.5);
                var shaftPen = new Pen(pen.Brush, pen.Thickness)
                {
                    StartLineCap = PenLineCap.Flat,
                    EndLineCap = PenLineCap.Flat,
                    LineJoin = PenLineJoin.Round
                };
                shaftPen.Freeze();

                if (stroke.Points.Count >= 3)
                {
                    var pPrev = new Point(stroke.Points[^2].X, stroke.Points[^2].Y);
                    var pEnd = new Point(stroke.Points[^1].X, stroke.Points[^1].Y);
                    var pStart = new Point(stroke.Points[0].X, stroke.Points[0].Y);
                    var pNext = new Point(stroke.Points[1].X, stroke.Points[1].Y);

                    var shaftPts = new List<PointD>(stroke.Points);
                    Point trimmedEnd = TrimPointBeforeArrowHead(pPrev, pEnd, headLen);
                    shaftPts[^1] = new PointD(trimmedEnd.X, trimmedEnd.Y, shaftPts[^1].Pressure);

                    if (stroke.ShapeKind == ShapeKind.DoubleArrow)
                    {
                        Point trimmedStart = TrimPointBeforeArrowHead(pNext, pStart, headLen);
                        shaftPts[0] = new PointD(trimmedStart.X, trimmedStart.Y, shaftPts[0].Pressure);
                    }

                    DrawSmoothCurveThroughPoints(dc, shaftPts, shaftPen);
                    DrawArrowHead(dc, pPrev, pEnd, stroke.Thickness, pen.Brush);
                    if (stroke.ShapeKind == ShapeKind.DoubleArrow)
                    {
                        DrawArrowHead(dc, pNext, pStart, stroke.Thickness, pen.Brush);
                    }
                }
                else if (stroke.StartPoint.HasValue && stroke.EndPoint.HasValue)
                {
                    var p1 = new Point(stroke.StartPoint.Value.X, stroke.StartPoint.Value.Y);
                    var p2 = new Point(stroke.EndPoint.Value.X, stroke.EndPoint.Value.Y);
                    Point shaftStart = stroke.ShapeKind == ShapeKind.DoubleArrow
                        ? TrimPointBeforeArrowHead(p2, p1, headLen)
                        : p1;
                    Point shaftEnd = TrimPointBeforeArrowHead(p1, p2, headLen);

                    dc.DrawLine(shaftPen, shaftStart, shaftEnd);
                    DrawArrowHead(dc, p1, p2, stroke.Thickness, pen.Brush);
                    if (stroke.ShapeKind == ShapeKind.DoubleArrow)
                    {
                        DrawArrowHead(dc, p2, p1, stroke.Thickness, pen.Brush);
                    }
                }
                break;
            }

            default:
                if (Geometry3DHelper.Is3DSolid(stroke.ShapeKind) && stroke.ShapeBounds.HasValue)
                {
                    Draw3DSolid(dc, stroke.ShapeKind, stroke.ShapeBounds.Value, pen);
                    break;
                }
                var poly = new StreamGeometry();
                using (var ctx = poly.Open())
                {
                    ctx.BeginFigure(new Point(stroke.Points[0].X, stroke.Points[0].Y), false, false);
                    for (int i = 1; i < stroke.Points.Count; i++)
                    {
                        ctx.LineTo(new Point(stroke.Points[i].X, stroke.Points[i].Y), true, true);
                    }
                }
                poly.Freeze();
                dc.DrawGeometry(null, pen, poly);
                break;
        }
    }

    private static void Draw3DSolid(DrawingContext dc, ShapeKind kind, RectD bounds, Pen solidPen)
    {
        var parts = Geometry3DHelper.BuildWireframe(kind, bounds);
        if (parts.Count == 0) return;

        Pen? dashedPen = null;
        foreach (var part in parts)
        {
            if (part.Points.Count < 2) continue;
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(new Point(part.Points[0].X, part.Points[0].Y), false, false);
                for (int i = 1; i < part.Points.Count; i++)
                {
                    ctx.LineTo(new Point(part.Points[i].X, part.Points[i].Y), true, true);
                }
            }
            geo.Freeze();

            if (part.IsDashed)
            {
                if (dashedPen == null)
                {
                    Brush dashBrush = solidPen.Brush;
                    if (solidPen.Brush is SolidColorBrush scb)
                    {
                        var c = scb.Color;
                        var hiddenColor = Color.FromArgb((byte)Math.Max(45, c.A * 0.58), c.R, c.G, c.B);
                        var sb = new SolidColorBrush(hiddenColor);
                        sb.Freeze();
                        dashBrush = sb;
                    }
                    dashedPen = new Pen(dashBrush, Math.Max(0.9, solidPen.Thickness * 0.82))
                    {
                        DashStyle = new DashStyle(new[] { 4.5, 4.0 }, 0),
                        StartLineCap = PenLineCap.Round,
                        EndLineCap = PenLineCap.Round,
                        LineJoin = PenLineJoin.Round
                    };
                    dashedPen.Freeze();
                }
                dc.DrawGeometry(null, dashedPen, geo);
            }
            else
            {
                dc.DrawGeometry(null, solidPen, geo);
            }
        }
    }

    private static Point TrimPointBeforeArrowHead(Point towardStart, Point arrowTip, double headLength)
    {
        double dx = arrowTip.X - towardStart.X;
        double dy = arrowTip.Y - towardStart.Y;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1.0) return arrowTip;
        double trim = Math.Min(len * 0.65, headLength * 0.68);
        return new Point(arrowTip.X - (dx / len) * trim, arrowTip.Y - (dy / len) * trim);
    }

    private void DrawArrowHead(DrawingContext dc, Point start, Point end, double thickness, Brush brush)
    {
        double angle = Math.Atan2(end.Y - start.Y, end.X - start.X);
        double headLength = Math.Max(16.0, thickness * 4.5);
        double headAngle = Math.PI / 6.0;

        Point arrowP1 = new Point(end.X - headLength * Math.Cos(angle - headAngle),
                                  end.Y - headLength * Math.Sin(angle - headAngle));
        Point arrowP2 = new Point(end.X - headLength * Math.Cos(angle + headAngle),
                                  end.Y - headLength * Math.Sin(angle + headAngle));

        var headGeo = new StreamGeometry();
        using (var ctx = headGeo.Open())
        {
            ctx.BeginFigure(end, true, true);
            ctx.LineTo(arrowP1, true, false);
            ctx.LineTo(arrowP2, true, false);
        }
        headGeo.Freeze();
        dc.DrawGeometry(brush, null, headGeo);
    }

    private void DrawCloudShape(DrawingContext dc, RectD b, Pen pen)
    {
        double x = b.X, y = b.Y, w = b.Width, h = b.Height;
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(new Point(x + w * 0.25, y + h * 0.85), false, true);
            ctx.BezierTo(new Point(x - w * 0.05, y + h * 0.85), new Point(x - w * 0.02, y + h * 0.42), new Point(x + w * 0.22, y + h * 0.40), true, true);
            ctx.BezierTo(new Point(x + w * 0.20, y + h * 0.12), new Point(x + w * 0.55, y + h * 0.05), new Point(x + w * 0.65, y + h * 0.28), true, true);
            ctx.BezierTo(new Point(x + w * 0.88, y + h * 0.15), new Point(x + w * 1.05, y + h * 0.48), new Point(x + w * 0.85, y + h * 0.65), true, true);
            ctx.BezierTo(new Point(x + w * 0.95, y + h * 0.88), new Point(x + w * 0.55, y + h * 0.95), new Point(x + w * 0.25, y + h * 0.85), true, true);
        }
        geo.Freeze();
        dc.DrawGeometry(null, pen, geo);
    }

    private void DrawSmoothCurveThroughPoints(DrawingContext dc, List<PointD> pts, Pen pen)
    {
        var smoothed = SplineSmoother.SmoothStroke(pts);
        if (smoothed.Count < 2) return;

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(new Point(smoothed[0].X, smoothed[0].Y), false, false);
            for (int i = 1; i < smoothed.Count; i++)
            {
                ctx.LineTo(new Point(smoothed[i].X, smoothed[i].Y), true, true);
            }
        }
        geo.Freeze();
        dc.DrawGeometry(null, pen, geo);
    }

    private void DrawCoordinateAxes(DrawingContext dc, RectD b, Pen pen, double thickness, CoordinateAxesOptions? opts)
    {
        opts ??= new CoordinateAxesOptions();
        int rangeX = Math.Max(1, opts.MaxX - opts.MinX);
        int rangeY = Math.Max(1, opts.MaxY - opts.MinY);

        double originX = b.Left + (-opts.MinX / (double)rangeX) * b.Width;
        double originY = b.Bottom - (-opts.MinY / (double)rangeY) * b.Height;
        originX = Math.Clamp(originX, b.Left + 12, b.Right - 12);
        originY = Math.Clamp(originY, b.Top + 12, b.Bottom - 12);

        double unitX = b.Width / rangeX;
        double unitY = b.Height / rangeY;

        var left = new Point(b.Left, originY);
        var right = new Point(b.Right, originY);
        var bottom = new Point(originX, b.Bottom);
        var top = new Point(originX, b.Top);

        double headLen = Math.Max(16.0, thickness * 4.5);
        var axisShaftPen = new Pen(pen.Brush, pen.Thickness)
        {
            StartLineCap = PenLineCap.Flat,
            EndLineCap = PenLineCap.Flat
        };
        axisShaftPen.Freeze();

        if (opts.ShowXAxis)
        {
            dc.DrawLine(axisShaftPen, left, TrimPointBeforeArrowHead(left, right, headLen));
            DrawArrowHead(dc, left, right, thickness, pen.Brush);
        }
        if (opts.ShowYAxis)
        {
            dc.DrawLine(axisShaftPen, bottom, TrimPointBeforeArrowHead(bottom, top, headLen));
            DrawArrowHead(dc, bottom, top, thickness, pen.Brush);
        }

        var typeface = new Typeface("Segoe UI");
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        int step = Math.Max(1, opts.Step);

        if (opts.ShowXAxis)
        {
            for (int x = opts.MinX; x <= opts.MaxX; x += step)
            {
                if (x == 0 && opts.ShowYAxis) continue;
                double px = originX + x * unitX;
                if (px < b.Left + 12 || px > b.Right - 18) continue;
                dc.DrawLine(pen, new Point(px, originY - 5), new Point(px, originY + 5));
                if (opts.ShowLabels)
                {
                    var ft = new FormattedText(x.ToString(), System.Globalization.CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight, typeface, 11, pen.Brush, dpi);
                    dc.DrawText(ft, new Point(px - ft.Width * 0.5, originY + 7));
                }
            }
        }

        if (opts.ShowYAxis)
        {
            for (int y = opts.MinY; y <= opts.MaxY; y += step)
            {
                if (y == 0 && opts.ShowXAxis) continue;
                double py = originY - y * unitY;
                if (py < b.Top + 18 || py > b.Bottom - 12) continue;
                dc.DrawLine(pen, new Point(originX - 5, py), new Point(originX + 5, py));
                if (opts.ShowLabels)
                {
                    var ft = new FormattedText(y.ToString(), System.Globalization.CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight, typeface, 11, pen.Brush, dpi);
                    dc.DrawText(ft, new Point(originX - ft.Width - 8, py - ft.Height * 0.5));
                }
            }
        }
    }

    private static bool IsBendableLineOrArrow(DrawingStroke s) =>
        (s.ShapeKind == ShapeKind.Line || s.ShapeKind == ShapeKind.Arrow || s.ShapeKind == ShapeKind.DoubleArrow) &&
        s.Points.Count == 4;

    private static Point[] GetResizeHandles(RectD bounds)
    {
        double pad = 6.0;
        double l = bounds.Left - pad, r = bounds.Right + pad;
        double t = bounds.Top - pad, b = bounds.Bottom + pad;
        double cx = (l + r) * 0.5, cy = (t + b) * 0.5;
        return new[]
        {
            new Point(l, t), new Point(cx, t), new Point(r, t),
            new Point(r, cy), new Point(r, b), new Point(cx, b),
            new Point(l, b), new Point(l, cy)
        };
    }

    private void DrawSelectionHighlight(DrawingContext dc, DrawingStroke stroke)
    {
        var handleFill = Brushes.White;
        var handlePen = new Pen(new SolidColorBrush(Color.FromRgb(124, 58, 237)), 1.8);
        handlePen.Freeze();

        if (IsBendableLineOrArrow(stroke))
        {
            // Draw bend control polygon + 4 circular control handles
            var guidePen = new Pen(new SolidColorBrush(Color.FromArgb(120, 124, 58, 237)), 1.0)
            {
                DashStyle = DashStyles.Dot
            };
            guidePen.Freeze();
            for (int i = 1; i < stroke.Points.Count; i++)
            {
                dc.DrawLine(guidePen,
                    new Point(stroke.Points[i - 1].X, stroke.Points[i - 1].Y),
                    new Point(stroke.Points[i].X, stroke.Points[i].Y));
            }
            for (int i = 0; i < stroke.Points.Count; i++)
            {
                var pt = new Point(stroke.Points[i].X, stroke.Points[i].Y);
                Brush fill = (i == 0 || i == 3) ? handleFill : new SolidColorBrush(Color.FromRgb(237, 233, 254));
                dc.DrawEllipse(fill, handlePen, pt, 5.5, 5.5);
            }
            return;
        }

        var bounds = GetStrokeBounds(stroke);
        var pad = 6.0;
        var rect = new Rect(bounds.X - pad, bounds.Y - pad, bounds.Width + pad * 2, bounds.Height + pad * 2);
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(124, 58, 237)), 1.5)
        {
            DashStyle = DashStyles.Dash
        };
        var fillRect = new SolidColorBrush(Color.FromArgb(18, 124, 58, 237));
        pen.Freeze();
        fillRect.Freeze();
        dc.DrawRectangle(fillRect, pen, rect);

        // Draw 8 resize handles
        foreach (var h in GetResizeHandles(bounds))
        {
            dc.DrawRectangle(handleFill, handlePen, new Rect(h.X - 4.5, h.Y - 4.5, 9, 9));
        }
    }

    private void DrawPaperGuidelines(DrawingContext dc, PaperStyle style, PaperColorTheme color, double width, double height, double spacing)
    {
        if (style == PaperStyle.Blank) return;

        bool isDark = color == PaperColorTheme.Chalkboard || color == PaperColorTheme.DarkSlate;
        Color lineColor = isDark
            ? Color.FromArgb(95, 255, 255, 255)
            : Color.FromArgb(45, 0, 0, 0);
        var pen = new Pen(new SolidColorBrush(lineColor), 1.0);
        pen.Freeze();

        if (style == PaperStyle.Ruled || style == PaperStyle.Grid)
        {
            var gridGeo = new StreamGeometry();
            using (var ctx = gridGeo.Open())
            {
                for (double y = spacing; y < height; y += spacing)
                {
                    ctx.BeginFigure(new Point(0, y), false, false);
                    ctx.LineTo(new Point(width, y), true, false);
                }

                if (style == PaperStyle.Grid)
                {
                    for (double x = spacing; x < width; x += spacing)
                    {
                        ctx.BeginFigure(new Point(x, 0), false, false);
                        ctx.LineTo(new Point(x, height), true, false);
                    }
                }
            }
            gridGeo.Freeze();
            dc.DrawGeometry(null, pen, gridGeo);
        }

        if (style == PaperStyle.Dotted)
        {
            var dotBrush = isDark
                ? new SolidColorBrush(Color.FromArgb(120, 255, 255, 255))
                : new SolidColorBrush(Color.FromArgb(65, 0, 0, 0));
            dotBrush.Freeze();
            double dotRadius = isDark ? 1.5 : 1.2;

            var tileGeo = new EllipseGeometry(new Point(spacing, spacing), dotRadius, dotRadius);
            tileGeo.Freeze();
            var drawing = new GeometryDrawing(dotBrush, null, tileGeo);
            drawing.Freeze();

            var tileBrush = new DrawingBrush(drawing)
            {
                Viewport = new Rect(spacing * 0.5, spacing * 0.5, spacing, spacing),
                ViewportUnits = BrushMappingMode.Absolute,
                Viewbox = new Rect(spacing * 0.5, spacing * 0.5, spacing, spacing),
                ViewboxUnits = BrushMappingMode.Absolute,
                TileMode = TileMode.Tile
            };
            tileBrush.Freeze();

            double fillW = Math.Max(0, width - spacing);
            double fillH = Math.Max(0, height - spacing);
            if (fillW > 0 && fillH > 0)
            {
                dc.DrawRectangle(tileBrush, null, new Rect(spacing * 0.5, spacing * 0.5, fillW, fillH));
            }
        }
    }

    private Brush GetPaperBackgroundBrush(PaperColorTheme theme) => theme switch
    {
        PaperColorTheme.Cream => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FDFBF7")),
        PaperColorTheme.LegalYellow => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FEF9C3")),
        PaperColorTheme.DarkSlate => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B")),
        PaperColorTheme.Chalkboard => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#15241C")),
        _ => Brushes.White
    };

    private void SyncOverlays(Page page)
    {
        bool sameImages = ImagesLayer.Children.Count == page.Images.Count &&
            page.Images.Select((m, i) => ImagesLayer.Children[i] is ImageOverlayControl c && ReferenceEquals(c.Model, m)).All(ok => ok);

        var unpinnedTexts = page.TextBoxes.Where(t => !t.IsPinnedToScreen).ToList();
        var pinnedTexts = page.TextBoxes.Where(t => t.IsPinnedToScreen).ToList();

        bool sameOverlays = OverlayLayer.Children.Count == (page.Tables.Count + unpinnedTexts.Count);
        if (sameOverlays)
        {
            int idx = 0;
            for (int i = 0; i < page.Tables.Count && sameOverlays; i++, idx++)
            {
                if (OverlayLayer.Children[idx] is not TableOverlayControl tc || !ReferenceEquals(tc.TableModel, page.Tables[i]))
                    sameOverlays = false;
            }
            for (int i = 0; i < unpinnedTexts.Count && sameOverlays; i++, idx++)
            {
                if (OverlayLayer.Children[idx] is not StickyNoteOverlayControl sc || !ReferenceEquals(sc.Model, unpinnedTexts[i]))
                    sameOverlays = false;
            }
        }

        bool samePinned = PinnedOverlayLayer.Children.Count == pinnedTexts.Count &&
            pinnedTexts.Select((m, i) => PinnedOverlayLayer.Children[i] is StickyNoteOverlayControl sc && ReferenceEquals(sc.Model, m)).All(ok => ok);

        if (sameImages && sameOverlays && samePinned && !_pendingActivateTextId.HasValue && !_pendingActivateTableId.HasValue && !_pendingActivateImageId.HasValue)
        {
            return;
        }

        Guid? activeTextId = _pendingActivateTextId ?? _activeTextOverlay?.Model.Id;
        Guid? activeTableId = _pendingActivateTableId ?? _activeTableOverlay?.TableModel.Id;
        Guid? activeImageId = _pendingActivateImageId ?? _activeImageOverlay?.Model.Id;
        _pendingActivateTextId = null;
        _pendingActivateTableId = null;
        _pendingActivateImageId = null;

        if (!sameImages || _pendingActivateImageId != activeImageId)
        {
            _activeImageOverlay = null;
            ImagesLayer.Children.Clear();

            foreach (var img in page.Images)
            {
                var imgOverlay = new ImageOverlayControl(img);
                imgOverlay.Deleted += deletedImg =>
                {
                    if (_activeImageOverlay?.Model.Id == deletedImg.Id) _activeImageOverlay = null;
                    DataStore.Instance.PushUndoState();
                    page.Images.Remove(deletedImg);
                    DataStore.Instance.NotifyCanvasChanged();
                };
                imgOverlay.Activated += SetActiveImageOverlay;
                Canvas.SetLeft(imgOverlay, img.X);
                Canvas.SetTop(imgOverlay, img.Y);
                ImagesLayer.Children.Add(imgOverlay);

                if (activeImageId.HasValue && img.Id == activeImageId.Value)
                {
                    _activeImageOverlay = imgOverlay;
                    imgOverlay.SetActive(true);
                }
            }
        }
        else if (activeImageId.HasValue)
        {
            foreach (ImageOverlayControl c in ImagesLayer.Children)
            {
                bool match = c.Model.Id == activeImageId.Value;
                c.SetActive(match);
                if (match) _activeImageOverlay = c;
            }
        }

        if (!sameOverlays || !samePinned || _pendingActivateTableId != activeTableId || _pendingActivateTextId != activeTextId)
        {
            _activeTextOverlay = null;
            _activeTableOverlay = null;
            OverlayLayer.Children.Clear();
            PinnedOverlayLayer.Children.Clear();

            foreach (var table in page.Tables)
            {
                var overlay = new TableOverlayControl(table);
                overlay.TableChanged += () => DataStore.Instance.NotifyCanvasChanged();
                overlay.TableDeleted += t =>
                {
                    if (_activeTableOverlay?.TableModel.Id == t.Id) _activeTableOverlay = null;
                    DataStore.Instance.PushUndoState();
                    page.Tables.Remove(t);
                    DataStore.Instance.NotifyCanvasChanged();
                };
                overlay.Activated += SetActiveTableOverlay;
                Canvas.SetLeft(overlay, table.X);
                Canvas.SetTop(overlay, table.Y);
                OverlayLayer.Children.Add(overlay);

                if (activeTableId.HasValue && table.Id == activeTableId.Value)
                {
                    _activeTableOverlay = overlay;
                    overlay.SetActive(true);
                }
            }

            foreach (var textBox in page.TextBoxes)
            {
                var sticky = new StickyNoteOverlayControl(textBox);
                sticky.Deleted += tb =>
                {
                    if (_activeTextOverlay?.Model.Id == tb.Id)
                    {
                        _activeTextOverlay = null;
                        ActiveTextOverlayChanged?.Invoke(null);
                    }
                    DataStore.Instance.PushUndoState();
                    page.TextBoxes.Remove(tb);
                    DataStore.Instance.NotifyCanvasChanged();
                };
                sticky.Activated += SetActiveTextOverlay;
                sticky.PinToggled += s => ToggleStickyNotePin(page, s);

                if (textBox.IsPinnedToScreen)
                {
                    double clampedX = PinnedOverlayLayer.ActualWidth > 80
                        ? Math.Clamp(textBox.PinnedScreenX, 0, Math.Max(0, PinnedOverlayLayer.ActualWidth - Math.Max(80, textBox.Width)))
                        : Math.Max(0, textBox.PinnedScreenX);
                    double clampedY = PinnedOverlayLayer.ActualHeight > 60
                        ? Math.Clamp(textBox.PinnedScreenY, 0, Math.Max(0, PinnedOverlayLayer.ActualHeight - Math.Max(48, textBox.Height)))
                        : Math.Max(0, textBox.PinnedScreenY);
                    Canvas.SetLeft(sticky, clampedX);
                    Canvas.SetTop(sticky, clampedY);
                    PinnedOverlayLayer.Children.Add(sticky);
                }
                else
                {
                    Canvas.SetLeft(sticky, textBox.X);
                    Canvas.SetTop(sticky, textBox.Y);
                    OverlayLayer.Children.Add(sticky);
                }

                if (activeTextId.HasValue && textBox.Id == activeTextId.Value)
                {
                    _activeTextOverlay = sticky;
                    sticky.SetActive(true);
                }
            }
        }

        ActiveTextOverlayChanged?.Invoke(_activeTextOverlay);
    }

    private void ToggleStickyNotePin(Page page, StickyNoteOverlayControl sticky)
    {
        var tb = sticky.Model;
        DataStore.Instance.PushUndoState();

        if (!tb.IsPinnedToScreen)
        {
            Point screenPos;
            try
            {
                screenPos = sticky.TranslatePoint(new Point(0, 0), PinnedOverlayLayer);
            }
            catch
            {
                screenPos = new Point(32, 32);
            }

            double maxX = PinnedOverlayLayer.ActualWidth > 80
                ? Math.Max(0, PinnedOverlayLayer.ActualWidth - Math.Max(80, sticky.ActualWidth > 0 ? sticky.ActualWidth : tb.Width))
                : 1200;
            double maxY = PinnedOverlayLayer.ActualHeight > 60
                ? Math.Max(0, PinnedOverlayLayer.ActualHeight - Math.Max(48, sticky.ActualHeight > 0 ? sticky.ActualHeight : tb.Height))
                : 800;

            tb.PinnedScreenX = Math.Clamp(screenPos.X, 0, maxX);
            tb.PinnedScreenY = Math.Clamp(screenPos.Y, 0, maxY);
            tb.IsPinnedToScreen = true;
        }
        else
        {
            Point pagePos;
            try
            {
                pagePos = sticky.TranslatePoint(new Point(0, 0), OverlayLayer);
            }
            catch
            {
                pagePos = new Point(tb.X, tb.Y);
            }

            tb.X = Math.Clamp(pagePos.X, 0, Math.Max(0, page.Width - 80));
            tb.Y = Math.Clamp(pagePos.Y, 0, Math.Max(0, page.Height - 48));
            tb.IsPinnedToScreen = false;
        }

        _pendingActivateTextId = tb.Id;
        SyncOverlays(page);
        DataStore.Instance.SaveToDisk();
    }

    private void ClampPinnedOverlaysToViewport()
    {
        if (PinnedOverlayLayer.ActualWidth <= 80 || PinnedOverlayLayer.ActualHeight <= 60) return;
        foreach (UIElement child in PinnedOverlayLayer.Children)
        {
            if (child is StickyNoteOverlayControl sticky)
            {
                double maxX = Math.Max(0, PinnedOverlayLayer.ActualWidth - Math.Max(80, sticky.ActualWidth > 0 ? sticky.ActualWidth : sticky.Model.Width));
                double maxY = Math.Max(0, PinnedOverlayLayer.ActualHeight - Math.Max(48, sticky.ActualHeight > 0 ? sticky.ActualHeight : sticky.Model.Height));
                double x = Math.Clamp(sticky.Model.PinnedScreenX, 0, maxX);
                double y = Math.Clamp(sticky.Model.PinnedScreenY, 0, maxY);
                if (Math.Abs(x - sticky.Model.PinnedScreenX) > 0.5 || Math.Abs(y - sticky.Model.PinnedScreenY) > 0.5)
                {
                    sticky.Model.PinnedScreenX = x;
                    sticky.Model.PinnedScreenY = y;
                    Canvas.SetLeft(sticky, x);
                    Canvas.SetTop(sticky, y);
                }
            }
        }
    }

    public void ActivateTextBoxOnNextSync(Guid textBoxId)
    {
        _pendingActivateTextId = textBoxId;
        _pendingActivateTableId = null;
        _pendingActivateImageId = null;
    }

    public void ActivateTableOnNextSync(Guid tableId)
    {
        _pendingActivateTableId = tableId;
        _pendingActivateTextId = null;
        _pendingActivateImageId = null;
    }

    public void ActivateImageOnNextSync(Guid imageId)
    {
        _pendingActivateImageId = imageId;
        _pendingActivateTextId = null;
        _pendingActivateTableId = null;
    }

    private void SetActiveTextOverlay(StickyNoteOverlayControl overlay)
    {
        if (_activeTableOverlay != null)
        {
            _activeTableOverlay.SetActive(false);
            _activeTableOverlay = null;
        }
        if (_activeImageOverlay != null)
        {
            _activeImageOverlay.SetActive(false);
            _activeImageOverlay = null;
        }
        if (_activeTextOverlay != null && _activeTextOverlay != overlay)
        {
            _activeTextOverlay.SetActive(false);
        }
        _activeTextOverlay = overlay;
        overlay.SetActive(true);
        ActiveTextOverlayChanged?.Invoke(overlay);
    }

    private void SetActiveTableOverlay(TableOverlayControl overlay)
    {
        if (_activeTextOverlay != null)
        {
            _activeTextOverlay.SetActive(false);
            _activeTextOverlay = null;
            ActiveTextOverlayChanged?.Invoke(null);
        }
        if (_activeImageOverlay != null)
        {
            _activeImageOverlay.SetActive(false);
            _activeImageOverlay = null;
        }
        if (_activeTableOverlay != null && _activeTableOverlay != overlay)
        {
            _activeTableOverlay.SetActive(false);
        }
        _activeTableOverlay = overlay;
        overlay.SetActive(true);
    }

    private void SetActiveImageOverlay(ImageOverlayControl overlay)
    {
        if (_activeTextOverlay != null)
        {
            _activeTextOverlay.SetActive(false);
            _activeTextOverlay = null;
            ActiveTextOverlayChanged?.Invoke(null);
        }
        if (_activeTableOverlay != null)
        {
            _activeTableOverlay.SetActive(false);
            _activeTableOverlay = null;
        }
        if (_activeImageOverlay != null && _activeImageOverlay != overlay)
        {
            _activeImageOverlay.SetActive(false);
        }
        _activeImageOverlay = overlay;
        overlay.SetActive(true);
    }

    public void ClearActiveOverlay()
    {
        bool changed = false;
        if (_activeTextOverlay != null)
        {
            _activeTextOverlay.SetActive(false);
            _activeTextOverlay = null;
            ActiveTextOverlayChanged?.Invoke(null);
            changed = true;
        }
        if (_activeTableOverlay != null)
        {
            _activeTableOverlay.SetActive(false);
            _activeTableOverlay = null;
            changed = true;
        }
        if (_activeImageOverlay != null)
        {
            _activeImageOverlay.SetActive(false);
            _activeImageOverlay = null;
            changed = true;
        }
        if (changed)
        {
            Focus();
        }
    }

    #region Selection & Editing Helpers

    public bool HasSelectedStrokes()
    {
        var page = DataStore.Instance.ActiveNote?.CurrentPage;
        return page != null && page.Strokes.Any(s => s.IsSelected);
    }

    public bool HasSelectedArrow()
    {
        var page = DataStore.Instance.ActiveNote?.CurrentPage;
        return page != null && page.Strokes.Any(s => s.IsSelected && (s.ShapeKind == ShapeKind.Arrow || s.ShapeKind == ShapeKind.DoubleArrow));
    }

    public void DeleteSelectedStrokes()
    {
        var page = DataStore.Instance.ActiveNote?.CurrentPage;
        if (page == null) return;
        if (!page.Strokes.Any(s => s.IsSelected)) return;

        DataStore.Instance.PushUndoState();
        page.Strokes.RemoveAll(s => s.IsSelected);
        DataStore.Instance.NotifyCanvasChanged();
        SelectionChanged?.Invoke();
    }

    public void DuplicateSelectedStrokes()
    {
        var page = DataStore.Instance.ActiveNote?.CurrentPage;
        if (page == null) return;
        var selected = page.Strokes.Where(s => s.IsSelected).ToList();
        if (selected.Count == 0) return;

        DataStore.Instance.PushUndoState();
        foreach (var s in page.Strokes) s.IsSelected = false;

        foreach (var s in selected)
        {
            var copy = s.Clone();
            copy.Id = Guid.NewGuid();
            copy.IsSelected = true;
            MoveStroke(copy, 24, 24);
            page.Strokes.Add(copy);
        }

        DataStore.Instance.NotifyCanvasChanged();
        SelectionChanged?.Invoke();
    }

    public void ReverseSelectedArrows()
    {
        var page = DataStore.Instance.ActiveNote?.CurrentPage;
        if (page == null) return;

        var arrows = page.Strokes.Where(s => s.IsSelected && (s.ShapeKind == ShapeKind.Arrow || s.ShapeKind == ShapeKind.DoubleArrow)).ToList();
        if (arrows.Count == 0) return;

        DataStore.Instance.PushUndoState();
        foreach (var arrow in arrows)
        {
            arrow.ReverseDirection();
            _strokeVisualCache.Remove(arrow);
        }
        DataStore.Instance.NotifyCanvasChanged();
    }

    public void ClearSelection()
    {
        var page = DataStore.Instance.ActiveNote?.CurrentPage;
        if (page == null) return;
        bool any = false;
        foreach (var s in page.Strokes)
        {
            if (s.IsSelected)
            {
                s.IsSelected = false;
                any = true;
            }
        }
        if (any)
        {
            RenderCommittedLayer(page);
            SelectionChanged?.Invoke();
        }
    }

    public void SelectAllStrokes()
    {
        var page = DataStore.Instance.ActiveNote?.CurrentPage;
        if (page == null || page.Strokes.Count == 0) return;

        foreach (var s in page.Strokes) s.IsSelected = true;
        RenderCommittedLayer(page);
        SelectionChanged?.Invoke();
    }

    public void CopySelectedStrokes()
    {
        var page = DataStore.Instance.ActiveNote?.CurrentPage;
        if (page == null) return;
        var selected = page.Strokes.Where(s => s.IsSelected).ToList();
        if (selected.Count == 0) return;

        _strokeClipboard.Clear();
        foreach (var s in selected)
        {
            _strokeClipboard.Add(s.Clone());
        }
    }

    public bool DeleteActiveOverlayOrSelected()
    {
        var page = DataStore.Instance.ActiveNote?.CurrentPage;
        if (page == null) return false;

        if (HasSelectedStrokes())
        {
            DeleteSelectedStrokes();
            return true;
        }
        if (_activeImageOverlay != null)
        {
            var img = _activeImageOverlay.Model;
            _activeImageOverlay = null;
            DataStore.Instance.PushUndoState();
            page.Images.Remove(img);
            DataStore.Instance.NotifyCanvasChanged();
            return true;
        }
        if (_activeTableOverlay != null)
        {
            var tbl = _activeTableOverlay.TableModel;
            _activeTableOverlay = null;
            DataStore.Instance.PushUndoState();
            page.Tables.Remove(tbl);
            DataStore.Instance.NotifyCanvasChanged();
            return true;
        }
        if (_activeTextOverlay != null)
        {
            var tb = _activeTextOverlay.Model;
            _activeTextOverlay = null;
            ActiveTextOverlayChanged?.Invoke(null);
            DataStore.Instance.PushUndoState();
            page.TextBoxes.Remove(tb);
            DataStore.Instance.NotifyCanvasChanged();
            return true;
        }
        return false;
    }

    public bool CanPasteFromClipboard()
    {
        if (_strokeClipboard.Count > 0) return true;
        try
        {
            return Clipboard.ContainsImage() || Clipboard.ContainsText() || Clipboard.ContainsFileDropList();
        }
        catch
        {
            return false;
        }
    }

    public void InsertImageFromFileDialog(Point? targetPoint = null)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = LocalizationService.Instance.Get("InsertImageFromFile"),
            Filter = "Obrazy (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|Wszystkie pliki (*.*)|*.*"
        };
        if (dlg.ShowDialog(Window.GetWindow(this)) == true)
        {
            InsertImageFromFile(dlg.FileName, targetPoint);
        }
    }

    public bool InsertImageFromFile(string filePath, Point? targetPoint = null)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return false;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(filePath, UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze();
            InsertImageFromBitmapSource(bmp, Path.GetFileNameWithoutExtension(filePath), targetPoint);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void InsertImageFromBitmapSource(BitmapSource source, string name = "Obraz", Point? targetPoint = null)
    {
        var page = DataStore.Instance.ActiveNote?.CurrentPage;
        if (page == null) return;

        BitmapSource finalSource = source;
        const int maxStoreDim = 1600;
        if (source.PixelWidth > maxStoreDim || source.PixelHeight > maxStoreDim)
        {
            double scale = Math.Min((double)maxStoreDim / source.PixelWidth, (double)maxStoreDim / source.PixelHeight);
            var scaled = new TransformedBitmap(source, new ScaleTransform(scale, scale));
            scaled.Freeze();
            finalSource = scaled;
        }

        byte[] pngBytes;
        using (var ms = new MemoryStream())
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(finalSource));
            encoder.Save(ms);
            pngBytes = ms.ToArray();
        }

        double aspect = finalSource.PixelHeight > 0 ? (double)finalSource.PixelWidth / finalSource.PixelHeight : 1.33;
        double displayW = Math.Clamp(finalSource.PixelWidth, 160, 520);
        double displayH = Math.Clamp(displayW / Math.Max(0.15, aspect), 80, 620);

        var center = targetPoint ?? GetViewportCenterCanvasPoint();
        double x = Math.Max(24, center.X - displayW * 0.5);
        double y = Math.Max(24, center.Y - displayH * 0.5);

        EnsureCanvasCoversPoint(new PointD(x + displayW, y + displayH));
        DataStore.Instance.PushUndoState();

        var img = new CanvasImage
        {
            X = x,
            Y = y,
            Width = displayW,
            Height = displayH,
            ImageBase64 = Convert.ToBase64String(pngBytes),
            Name = name
        };

        page.Images.Add(img);
        ActivateImageOnNextSync(img.Id);
        DataStore.Instance.NotifyCanvasChanged();
    }

    public void PasteFromClipboard(Point? targetPoint = null)
    {
        var page = DataStore.Instance.ActiveNote?.CurrentPage;
        if (page == null) return;

        try
        {
            if (Clipboard.ContainsImage())
            {
                var imgSource = Clipboard.GetImage();
                if (imgSource != null)
                {
                    InsertImageFromBitmapSource(imgSource, "Schowek", targetPoint);
                    return;
                }
            }

            if (Clipboard.ContainsFileDropList())
            {
                var files = Clipboard.GetFileDropList();
                foreach (string? f in files)
                {
                    if (!string.IsNullOrEmpty(f) && InsertImageFromFile(f, targetPoint))
                        return;
                }
            }
        }
        catch
        {
            // Ignore transient clipboard lock errors from other apps
        }

        if (_strokeClipboard.Count > 0)
        {
            DataStore.Instance.PushUndoState();
            foreach (var s in page.Strokes) s.IsSelected = false;

            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;
            foreach (var s in _strokeClipboard)
            {
                var b = GetStrokeBounds(s);
                minX = Math.Min(minX, b.Left);
                minY = Math.Min(minY, b.Top);
                maxX = Math.Max(maxX, b.Right);
                maxY = Math.Max(maxY, b.Bottom);
            }

            double dx = 28.0;
            double dy = 28.0;
            if (targetPoint.HasValue)
            {
                dx = targetPoint.Value.X - (minX + maxX) * 0.5;
                dy = targetPoint.Value.Y - (minY + maxY) * 0.5;
            }

            foreach (var s in _strokeClipboard)
            {
                var copy = s.Clone();
                copy.Id = Guid.NewGuid();
                copy.IsSelected = true;
                MoveStroke(copy, dx, dy);
                page.Strokes.Add(copy);
            }

            DataStore.Instance.NotifyCanvasChanged();
            SelectionChanged?.Invoke();
            return;
        }

        try
        {
            if (Clipboard.ContainsText())
            {
                string txt = Clipboard.GetText()?.Trim() ?? "";
                if (!string.IsNullOrEmpty(txt))
                {
                    var center = targetPoint ?? GetViewportCenterCanvasPoint();
                    DataStore.Instance.PushUndoState();
                    var tb = new FloatingTextBox
                    {
                        X = Math.Max(24, center.X - 150),
                        Y = Math.Max(24, center.Y - 60),
                        Width = 300,
                        Height = 140,
                        IsStickyNote = false,
                        BackgroundColor = "Transparent",
                        TextColor = DataStore.Instance.SelectedColorHex,
                        Text = txt
                    };
                    page.TextBoxes.Add(tb);
                    ActivateTextBoxOnNextSync(tb.Id);
                    DataStore.Instance.NotifyCanvasChanged();
                }
            }
        }
        catch
        {
            // Ignore clipboard errors
        }
    }

    public void EditCoordinateAxes(DrawingStroke stroke)
    {
        if (stroke.ShapeKind != ShapeKind.CoordinateAxes) return;

        var existingOpts = stroke.AxesOptions ?? new CoordinateAxesOptions();
        double currentSize = stroke.ShapeBounds?.Width ?? 420.0;
        var dlg = new Quillfolio.WPF.Views.AxesConfigWindow(existingOpts, currentSize, isEdit: true)
        {
            Owner = Window.GetWindow(this)
        };

        if (dlg.ShowDialog() == true)
        {
            DataStore.Instance.PushUndoState();
            stroke.AxesOptions = dlg.Options;
            var oldBounds = GetStrokeBounds(stroke);
            double cx = oldBounds.CenterX;
            double cy = oldBounds.CenterY;
            var template = ShapeRecognizer.CreateTemplateShape(ShapeKind.CoordinateAxes, cx, cy, dlg.AxesSize, dlg.AxesSize);
            stroke.ShapeBounds = template.Bounds;
            stroke.Points = template.GeometryPoints;
            _strokeVisualCache.Remove(stroke);
            DataStore.Instance.NotifyCanvasChanged();
        }
    }

    private RectD GetStrokeBounds(DrawingStroke stroke)
    {
        if (stroke.ShapeBounds.HasValue) return stroke.ShapeBounds.Value;
        return RectD.FromPoints(CollectionsMarshal.AsSpan(stroke.Points));
    }

    private void MoveStroke(DrawingStroke stroke, double dx, double dy)
    {
        for (int i = 0; i < stroke.Points.Count; i++)
        {
            var pt = stroke.Points[i];
            pt.X += dx;
            pt.Y += dy;
            stroke.Points[i] = pt;
        }
        if (stroke.ShapeBounds.HasValue)
        {
            var b = stroke.ShapeBounds.Value;
            b.X += dx;
            b.Y += dy;
            stroke.ShapeBounds = b;
        }
        if (stroke.StartPoint.HasValue)
        {
            var sp = stroke.StartPoint.Value;
            sp.X += dx;
            sp.Y += dy;
            stroke.StartPoint = sp;
        }
        if (stroke.EndPoint.HasValue)
        {
            var ep = stroke.EndPoint.Value;
            ep.X += dx;
            ep.Y += dy;
            stroke.EndPoint = ep;
        }
        _strokeVisualCache.Remove(stroke);
    }

    #endregion

    #region Eraser Hit Testing

    private double GetEraserRadius() => Math.Max(10.0, DataStore.Instance.SelectedThickness * DataStore.Instance.EraserSizeMultiplier);

    private void EraseAt(Point pt)
    {
        var page = DataStore.Instance.ActiveNote?.CurrentPage;
        if (page == null) return;

        double radius = GetEraserRadius();

        if (DataStore.Instance.SelectedEraserMode == EraserMode.Point)
        {
            ErasePointAt(page, pt, radius);
            return;
        }

        var toRemove = new List<DrawingStroke>();

        foreach (var stroke in page.Strokes)
        {
            if (stroke.ShapeKind == ShapeKind.CoordinateAxes) continue;
            if (IsStrokeHit(stroke, pt, radius))
            {
                toRemove.Add(stroke);
            }
        }

        if (toRemove.Count > 0)
        {
            if (!_eraserUndoPushed)
            {
                DataStore.Instance.PushUndoState();
                _eraserUndoPushed = true;
            }
            foreach (var s in toRemove)
            {
                page.Strokes.Remove(s);
            }
            RenderCommittedLayer(page);
        }
    }

    private void ErasePointAt(Page page, Point pt, double radius)
    {
        bool anyModified = false;
        var updatedStrokes = new List<DrawingStroke>(page.Strokes.Count);

        foreach (var stroke in page.Strokes)
        {
            if (stroke.ShapeKind == ShapeKind.CoordinateAxes || !IsStrokeHit(stroke, pt, radius))
            {
                updatedStrokes.Add(stroke);
                continue;
            }

            if (stroke.ShapeKind == ShapeKind.Arrow || stroke.ShapeKind == ShapeKind.DoubleArrow)
            {
                // Arrowheads stay crisp geometric objects; remove on hit
                anyModified = true;
                continue;
            }

            if (Geometry3DHelper.Is3DSolid(stroke.ShapeKind) && stroke.ShapeBounds.HasValue)
            {
                anyModified = true;
                var parts = Geometry3DHelper.BuildWireframe(stroke.ShapeKind, stroke.ShapeBounds.Value);
                foreach (var part in parts)
                {
                    var segs = SplitPointsOutsideEraser(new List<PointD>(part.Points), new PointD(pt.X, pt.Y), radius + stroke.Thickness * 0.35);
                    foreach (var seg in segs)
                    {
                        if (seg.Count < 2) continue;
                        updatedStrokes.Add(new DrawingStroke
                        {
                            Id = Guid.NewGuid(),
                            Tool = stroke.Tool,
                            ColorHex = stroke.ColorHex,
                            Thickness = stroke.Thickness,
                            Opacity = part.IsDashed ? Math.Min(stroke.Opacity, 0.58) : stroke.Opacity,
                            IsSmoothed = false,
                            ShapeKind = ShapeKind.None,
                            Points = seg
                        });
                    }
                }
                continue;
            }

            var sourcePoints = GetErasablePointsForStroke(stroke);
            var segments = SplitPointsOutsideEraser(sourcePoints, new PointD(pt.X, pt.Y), radius + stroke.Thickness * 0.35);

            anyModified = true;
            foreach (var seg in segments)
            {
                if (seg.Count < 2) continue;
                updatedStrokes.Add(new DrawingStroke
                {
                    Id = Guid.NewGuid(),
                    Tool = stroke.Tool,
                    ColorHex = stroke.ColorHex,
                    Thickness = stroke.Thickness,
                    Opacity = stroke.Opacity,
                    IsSmoothed = false,
                    ShapeKind = ShapeKind.None,
                    Points = seg
                });
            }
        }

        if (anyModified)
        {
            if (!_eraserUndoPushed)
            {
                DataStore.Instance.PushUndoState();
                _eraserUndoPushed = true;
            }
            page.Strokes = updatedStrokes;
            RenderCommittedLayer(page);
        }
    }

    private static List<PointD> GetErasablePointsForStroke(DrawingStroke stroke)
    {
        if ((stroke.ShapeKind == ShapeKind.Circle || stroke.ShapeKind == ShapeKind.Ellipse) && stroke.ShapeBounds.HasValue)
        {
            var b = stroke.ShapeBounds.Value;
            double rx = b.Width * 0.5;
            double ry = b.Height * 0.5;
            var pts = new List<PointD>(65);
            for (int i = 0; i <= 64; i++)
            {
                double a = (i / 64.0) * Math.PI * 2.0;
                pts.Add(new PointD(b.CenterX + rx * Math.Cos(a), b.CenterY + ry * Math.Sin(a)));
            }
            return pts;
        }
        if (stroke.ShapeKind == ShapeKind.RoundedRectangle && stroke.ShapeBounds.HasValue)
        {
            var b = stroke.ShapeBounds.Value;
            return new List<PointD>
            {
                new(b.Left, b.Top), new(b.Right, b.Top),
                new(b.Right, b.Bottom), new(b.Left, b.Bottom), new(b.Left, b.Top)
            };
        }
        return stroke.Points;
    }

    private static List<List<PointD>> SplitPointsOutsideEraser(List<PointD> pts, PointD center, double eraseR)
    {
        var result = new List<List<PointD>>();
        if (pts.Count < 2) return result;

        double rSq = eraseR * eraseR;
        var currentRun = new List<PointD>();

        bool IsInside(PointD p)
        {
            double dx = p.X - center.X;
            double dy = p.Y - center.Y;
            return dx * dx + dy * dy <= rSq;
        }

        for (int i = 0; i < pts.Count - 1; i++)
        {
            var a = pts[i];
            var b = pts[i + 1];
            bool aIn = IsInside(a);
            bool bIn = IsInside(b);

            if (!aIn && currentRun.Count == 0)
            {
                currentRun.Add(a);
            }

            if (!aIn && !bIn)
            {
                // Check if segment passes cleanly through circle even though both endpoints are outside
                var hits = IntersectSegmentWithCircle(a, b, center, eraseR);
                if (hits.Count == 2)
                {
                    currentRun.Add(hits[0]);
                    if (currentRun.Count >= 2) result.Add(currentRun);
                    currentRun = new List<PointD> { hits[1], b };
                }
                else
                {
                    currentRun.Add(b);
                }
            }
            else if (!aIn && bIn)
            {
                var hits = IntersectSegmentWithCircle(a, b, center, eraseR);
                if (hits.Count >= 1) currentRun.Add(hits[0]);
                if (currentRun.Count >= 2) result.Add(currentRun);
                currentRun = new List<PointD>();
            }
            else if (aIn && !bIn)
            {
                var hits = IntersectSegmentWithCircle(a, b, center, eraseR);
                currentRun = new List<PointD>();
                if (hits.Count >= 1) currentRun.Add(hits[^1]);
                currentRun.Add(b);
            }
            else
            {
                if (currentRun.Count >= 2) result.Add(currentRun);
                currentRun = new List<PointD>();
            }
        }

        if (currentRun.Count >= 2)
        {
            result.Add(currentRun);
        }
        return result;
    }

    private static List<PointD> IntersectSegmentWithCircle(PointD a, PointD b, PointD center, double r)
    {
        var hits = new List<PointD>(2);
        double dx = b.X - a.X;
        double dy = b.Y - a.Y;
        double fx = a.X - center.X;
        double fy = a.Y - center.Y;

        double A = dx * dx + dy * dy;
        if (A < 1e-6) return hits;
        double B = 2.0 * (fx * dx + fy * dy);
        double C = fx * fx + fy * fy - r * r;

        double disc = B * B - 4.0 * A * C;
        if (disc < 0) return hits;

        double sqrtD = Math.Sqrt(disc);
        double t1 = (-B - sqrtD) / (2.0 * A);
        double t2 = (-B + sqrtD) / (2.0 * A);

        if (t1 >= 0.0 && t1 <= 1.0)
        {
            double p = a.Pressure + (b.Pressure - a.Pressure) * t1;
            hits.Add(new PointD(a.X + t1 * dx, a.Y + t1 * dy, p));
        }
        if (t2 >= 0.0 && t2 <= 1.0 && Math.Abs(t2 - t1) > 1e-4)
        {
            double p = a.Pressure + (b.Pressure - a.Pressure) * t2;
            hits.Add(new PointD(a.X + t2 * dx, a.Y + t2 * dy, p));
        }
        return hits;
    }

    private bool IsStrokeHit(DrawingStroke stroke, Point pt, double radius)
    {
        double hitRadius = radius + stroke.Thickness * 0.5;
        var p = new PointD(pt.X, pt.Y);

        if ((stroke.ShapeKind == ShapeKind.Circle || stroke.ShapeKind == ShapeKind.Ellipse) && stroke.ShapeBounds.HasValue)
        {
            var b = stroke.ShapeBounds.Value;
            double rx = b.Width / 2.0;
            double ry = b.Height / 2.0;
            if (rx > 1 && ry > 1)
            {
                double nx = (pt.X - b.CenterX) / rx;
                double ny = (pt.Y - b.CenterY) / ry;
                double dist = Math.Sqrt(nx * nx + ny * ny);
                double approxDistPx = Math.Abs(dist - 1.0) * Math.Min(rx, ry);
                if (approxDistPx <= hitRadius) return true;
            }
        }

        if (stroke.ShapeKind == ShapeKind.CoordinateAxes && stroke.ShapeBounds.HasValue)
        {
            var b = stroke.ShapeBounds.Value;
            if (pt.X >= b.Left - hitRadius && pt.X <= b.Right + hitRadius &&
                pt.Y >= b.Top - hitRadius && pt.Y <= b.Bottom + hitRadius)
            {
                return true;
            }
        }

        if (Geometry3DHelper.Is3DSolid(stroke.ShapeKind) && stroke.ShapeBounds.HasValue)
        {
            var parts = Geometry3DHelper.BuildWireframe(stroke.ShapeKind, stroke.ShapeBounds.Value);
            foreach (var part in parts)
            {
                for (int i = 1; i < part.Points.Count; i++)
                {
                    if (DistancePointToSegment(p, part.Points[i - 1], part.Points[i]) <= hitRadius)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        for (int i = 1; i < stroke.Points.Count; i++)
        {
            if (DistancePointToSegment(p, stroke.Points[i - 1], stroke.Points[i]) <= hitRadius)
            {
                return true;
            }
        }
        return false;
    }

    private static double DistancePointToSegment(PointD p, PointD a, PointD b)
    {
        double dx = b.X - a.X;
        double dy = b.Y - a.Y;
        double lenSq = dx * dx + dy * dy;
        if (lenSq < 0.0001) return p.DistanceTo(a);
        double t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lenSq, 0.0, 1.0);
        double projX = a.X + t * dx;
        double projY = a.Y + t * dy;
        double ex = p.X - projX;
        double ey = p.Y - projY;
        return Math.Sqrt(ex * ex + ey * ey);
    }

    #endregion

    public void InsertShape(ShapeKind kind)
    {
        var page = DataStore.Instance.ActiveNote?.CurrentPage;
        if (page == null) return;

        DataStore.Instance.PushUndoState();
        foreach (var s in page.Strokes) s.IsSelected = false;

        var center = GetViewportCenterCanvasPoint();
        var (defW, defH) = Geometry3DHelper.Is3DSolid(kind)
            ? Geometry3DHelper.GetDefaultSize(kind)
            : (180.0, 130.0);
        var template = ShapeRecognizer.CreateTemplateShape(kind, center.X, center.Y, defW, defH);

        bool autoSelect = DataStore.Instance.AutoSelectInsertedShapes;
        var stroke = new DrawingStroke
        {
            Tool = DataStore.Instance.SelectedTool == ToolType.Highlighter ? ToolType.Highlighter : ToolType.Pen,
            ColorHex = DataStore.Instance.SelectedColorHex,
            Thickness = DataStore.Instance.SelectedThickness,
            Opacity = DataStore.Instance.SelectedTool == ToolType.Highlighter ? 0.45 : 1.0,
            IsSmoothed = false,
            ShapeKind = template.Kind,
            ShapeBounds = template.Bounds,
            StartPoint = template.StartPoint,
            EndPoint = template.EndPoint,
            Points = template.GeometryPoints,
            IsSelected = autoSelect
        };

        page.Strokes.Add(stroke);
        DataStore.Instance.NotifyCanvasChanged();
        if (autoSelect) ShapeInsertedAndSelected?.Invoke();
        SelectionChanged?.Invoke();
    }

    public void InsertCoordinateAxes(CoordinateAxesOptions options, double size)
    {
        var page = DataStore.Instance.ActiveNote?.CurrentPage;
        if (page == null) return;

        DataStore.Instance.PushUndoState();
        foreach (var s in page.Strokes) s.IsSelected = false;

        var center = GetViewportCenterCanvasPoint();
        var template = ShapeRecognizer.CreateTemplateShape(ShapeKind.CoordinateAxes, center.X, center.Y, size, size);

        bool autoSelect = DataStore.Instance.AutoSelectInsertedShapes;
        var stroke = new DrawingStroke
        {
            Tool = ToolType.Pen,
            ColorHex = DataStore.Instance.SelectedColorHex,
            Thickness = DataStore.Instance.SelectedThickness,
            Opacity = 1.0,
            IsSmoothed = false,
            ShapeKind = ShapeKind.CoordinateAxes,
            ShapeBounds = template.Bounds,
            Points = template.GeometryPoints,
            AxesOptions = options,
            IsSelected = autoSelect
        };

        page.Strokes.Add(stroke);
        DataStore.Instance.NotifyCanvasChanged();
        if (autoSelect) ShapeInsertedAndSelected?.Invoke();
        SelectionChanged?.Invoke();
    }

    public void CenterPageInViewport()
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            double extentW = CanvasScrollViewer.ExtentWidth;
            double viewW = CanvasScrollViewer.ViewportWidth;
            if (extentW > viewW)
            {
                CanvasScrollViewer.ScrollToHorizontalOffset((extentW - viewW) * 0.5);
            }
            else
            {
                CanvasScrollViewer.ScrollToHorizontalOffset(0);
            }
        });
    }

    #region Export PNG & PDF

    private RenderTargetBitmap RenderPageToBitmap(Page page)
    {
        PaperCanvas.Width = page.Width;
        PaperCanvas.Height = page.Height;
        ImagesLayer.Width = page.Width;
        ImagesLayer.Height = page.Height;
        VectorLayer.Width = page.Width;
        VectorLayer.Height = page.Height;
        OverlayLayer.Width = page.Width;
        OverlayLayer.Height = page.Height;
        SelectionHudLayer.Width = page.Width;
        SelectionHudLayer.Height = page.Height;
        TopCursorLayer.Width = page.Width;
        TopCursorLayer.Height = page.Height;
        var prevHudVisibility = SelectionHudLayer.Visibility;
        var prevCursorVisibility = TopCursorLayer.Visibility;
        SelectionHudLayer.Visibility = Visibility.Collapsed;
        TopCursorLayer.Visibility = Visibility.Collapsed;
        PaperCanvas.Background = GetPaperBackgroundBrush(page.PaperColor);

        EnsurePaperRendered(page);
        RenderCommittedLayer(page);
        SyncOverlays(page);

        var parentBorder = PaperCanvas.Parent as FrameworkElement;
        var oldTransform = parentBorder?.LayoutTransform;
        if (parentBorder != null) parentBorder.LayoutTransform = Transform.Identity;

        PaperCanvas.Measure(new Size(page.Width, page.Height));
        PaperCanvas.Arrange(new Rect(0, 0, page.Width, page.Height));
        PaperCanvas.UpdateLayout();

        var rtb = new RenderTargetBitmap((int)page.Width, (int)page.Height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(PaperCanvas);

        SelectionHudLayer.Visibility = prevHudVisibility;
        TopCursorLayer.Visibility = prevCursorVisibility;
        if (parentBorder != null && oldTransform != null)
        {
            parentBorder.LayoutTransform = oldTransform;
            parentBorder.UpdateLayout();
        }

        return rtb;
    }

    public void ExportToPng(string filePath)
    {
        var page = DataStore.Instance.ActiveNote?.CurrentPage;
        if (page == null) return;

        ClearActiveOverlay();
        var rtb = RenderPageToBitmap(page);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(filePath);
        encoder.Save(fs);
    }

    public void ExportAllPagesToPdf(string filePath)
    {
        var note = DataStore.Instance.ActiveNote;
        if (note == null || note.Pages.Count == 0) return;

        ClearActiveOverlay();
        var currentPage = note.CurrentPage;
        var renderedPages = new List<(byte[] JpegBytes, int PixelWidth, int PixelHeight)>();

        foreach (var page in note.Pages)
        {
            var rtb = RenderPageToBitmap(page);
            var jpegEncoder = new JpegBitmapEncoder { QualityLevel = 92 };
            jpegEncoder.Frames.Add(BitmapFrame.Create(rtb));
            using var ms = new MemoryStream();
            jpegEncoder.Save(ms);
            renderedPages.Add((ms.ToArray(), rtb.PixelWidth, rtb.PixelHeight));
        }

        // Restore active page visuals
        RenderCanvas();

        Quillfolio.WPF.Services.PdfExporter.ExportPagesToPdf(filePath, renderedPages);
    }

    #endregion

    #region Input Handling (Hardware Stylus, Mouse, Pan & Zoom)

    private void CanvasScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            double delta = e.Delta > 0 ? 0.12 : -0.12;
            ZoomRequested?.Invoke(DataStore.Instance.ZoomLevel + delta);
            e.Handled = true;
        }
    }

    private static bool IsDescendantOfOverlay(DependencyObject? source)
    {
        var curr = source;
        while (curr != null)
        {
            if (curr is StickyNoteOverlayControl || curr is TableOverlayControl || curr is ImageOverlayControl ||
                (curr is FrameworkElement fe && fe.Name == "SelectionFloatingHud"))
                return true;
            curr = VisualTreeHelper.GetParent(curr) ?? LogicalTreeHelper.GetParent(curr);
        }
        return false;
    }

    private void PaperCanvas_StylusDown(object sender, StylusDownEventArgs e)
    {
        if (IsDescendantOfOverlay(e.OriginalSource as DependencyObject)) return;
        ClearActiveOverlay();

        var pt = e.GetPosition(PaperCanvas);
        var viewportPt = e.GetPosition(CanvasScrollViewer);
        var stylusPoints = e.GetStylusPoints(PaperCanvas);
        double pressure = stylusPoints.Count > 0 ? stylusPoints[0].PressureFactor : 1.0;
        PaperCanvas.CaptureStylus();
        HandlePointerDown(pt, viewportPt, pressure, false);
        e.Handled = true;
    }

    private void PaperCanvas_StylusMove(object sender, StylusEventArgs e)
    {
        if (!PaperCanvas.IsStylusCaptured && IsDescendantOfOverlay(e.OriginalSource as DependencyObject)) return;
        if (_isCursorPanning)
        {
            Point curr = e.GetPosition(CanvasScrollViewer);
            CanvasScrollViewer.ScrollToHorizontalOffset(_cursorPanStartH - (curr.X - _cursorPanStartViewport.X));
            CanvasScrollViewer.ScrollToVerticalOffset(_cursorPanStartV - (curr.Y - _cursorPanStartViewport.Y));
            e.Handled = true;
            return;
        }
        var pt = e.GetPosition(PaperCanvas);
        var stylusPoints = e.GetStylusPoints(PaperCanvas);
        double pressure = stylusPoints.Count > 0 ? stylusPoints[0].PressureFactor : 1.0;
        HandlePointerMove(pt, pressure);
        e.Handled = true;
    }

    private void PaperCanvas_StylusUp(object sender, StylusEventArgs e)
    {
        if (!PaperCanvas.IsStylusCaptured && IsDescendantOfOverlay(e.OriginalSource as DependencyObject)) return;
        HandlePointerUp();
        if (PaperCanvas.IsStylusCaptured)
        {
            _suppressCancelOnRelease = true;
            try { PaperCanvas.ReleaseStylusCapture(); }
            finally { _suppressCancelOnRelease = false; }
        }
        e.Handled = true;
    }

    private void PaperCanvas_StylusInAirMove(object sender, StylusEventArgs e)
    {
        if (DataStore.Instance.SelectedTool == ToolType.Eraser)
        {
            _eraserCursorPos = e.GetPosition(PaperCanvas);
            RenderTopCursorLayer();
        }
    }

    private void PaperCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.StylusDevice != null) return;
        if (IsDescendantOfOverlay(e.OriginalSource as DependencyObject)) return;

        if (e.ChangedButton == MouseButton.Right)
        {
            _lastContextMenuCanvasPoint = e.GetPosition(PaperCanvas);
            return;
        }

        ClearActiveOverlay();

        // Middle mouse button or Space + Left Click pans the canvas
        if (e.ChangedButton == MouseButton.Middle ||
            (e.LeftButton == MouseButtonState.Pressed && Keyboard.IsKeyDown(Key.Space)))
        {
            _isPanning = true;
            _panStartPoint = e.GetPosition(CanvasScrollViewer);
            _panStartH = CanvasScrollViewer.HorizontalOffset;
            _panStartV = CanvasScrollViewer.VerticalOffset;
            PaperCanvas.CaptureMouse();
            PaperCanvas.Cursor = Cursors.ScrollAll;
            e.Handled = true;
            return;
        }

        if (e.LeftButton == MouseButtonState.Pressed)
        {
            var pt = e.GetPosition(PaperCanvas);
            var viewportPt = e.GetPosition(CanvasScrollViewer);
            _lastMousePoint = new PointD(pt.X, pt.Y, 1.0);
            PaperCanvas.CaptureMouse();
            HandlePointerDown(pt, viewportPt, 1.0, e.ClickCount >= 2);
            e.Handled = true;
        }
    }

    private void PaperCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.StylusDevice != null) return;

        if (_isPanning)
        {
            Point curr = e.GetPosition(CanvasScrollViewer);
            CanvasScrollViewer.ScrollToHorizontalOffset(_panStartH - (curr.X - _panStartPoint.X));
            CanvasScrollViewer.ScrollToVerticalOffset(_panStartV - (curr.Y - _panStartPoint.Y));
            e.Handled = true;
            return;
        }

        if (_isCursorPanning)
        {
            Point curr = e.GetPosition(CanvasScrollViewer);
            CanvasScrollViewer.ScrollToHorizontalOffset(_cursorPanStartH - (curr.X - _cursorPanStartViewport.X));
            CanvasScrollViewer.ScrollToVerticalOffset(_cursorPanStartV - (curr.Y - _cursorPanStartViewport.Y));
            e.Handled = true;
            return;
        }

        var pt = e.GetPosition(PaperCanvas);
        double pseudoPressure = 1.0;
        if (_isDrawing && _lastMousePoint.HasValue)
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            double dist = Math.Sqrt(Math.Pow(pt.X - _lastMousePoint.Value.X, 2) + Math.Pow(pt.Y - _lastMousePoint.Value.Y, 2));
            long dt = Math.Max(1, now - _lastMousePoint.Value.Timestamp);
            double velocity = dist / dt; // px per ms
            pseudoPressure = Math.Clamp(1.18 - velocity * 0.22, 0.55, 1.25);
            _lastMousePoint = new PointD(pt.X, pt.Y, pseudoPressure, now);
        }

        HandlePointerMove(pt, pseudoPressure);
    }

    private void PaperCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.StylusDevice != null) return;
        if (e.ChangedButton == MouseButton.Right) return;

        if (_isPanning)
        {
            _isPanning = false;
            if (PaperCanvas.IsMouseCaptured)
            {
                _suppressCancelOnRelease = true;
                try { PaperCanvas.ReleaseMouseCapture(); }
                finally { _suppressCancelOnRelease = false; }
            }
            UpdateToolInteractionMode();
            e.Handled = true;
            return;
        }

        HandlePointerUp();
        if (PaperCanvas.IsMouseCaptured)
        {
            _suppressCancelOnRelease = true;
            try { PaperCanvas.ReleaseMouseCapture(); }
            finally { _suppressCancelOnRelease = false; }
        }
        e.Handled = true;
    }

    private void HandlePointerDown(Point pt, Point viewportPt, double pressure, bool isDoubleClick)
    {
        var tool = DataStore.Instance.SelectedTool;

        if (tool == ToolType.None)
        {
            _isCursorPanning = true;
            _cursorPanStartViewport = viewportPt;
            _cursorPanStartH = CanvasScrollViewer.HorizontalOffset;
            _cursorPanStartV = CanvasScrollViewer.VerticalOffset;
            PaperCanvas.Cursor = Cursors.ScrollAll;
            return;
        }

        if (tool == ToolType.Eraser)
        {
            _isErasing = true;
            _eraserUndoPushed = false;
            _eraserCursorPos = pt;
            EraseAt(pt);
            RenderTopCursorLayer();
            return;
        }

        if (tool == ToolType.Laser)
        {
            _isLaserActive = true;
            _laserTrail.Add((pt, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
            if (!_laserTimer.IsEnabled) _laserTimer.Start();
            RenderActiveLayer();
            return;
        }

        if (tool == ToolType.Select || tool == ToolType.Cursor)
        {
            var page = DataStore.Instance.ActiveNote?.CurrentPage;
            if (page == null) return;

            // 1. Check bendable control handles or resize handles on currently selected strokes
            foreach (var selected in page.Strokes.Where(s => s.IsSelected).Reverse())
            {
                if (IsBendableLineOrArrow(selected))
                {
                    for (int i = 0; i < selected.Points.Count; i++)
                    {
                        var cp = selected.Points[i];
                        if (Math.Sqrt(Math.Pow(pt.X - cp.X, 2) + Math.Pow(pt.Y - cp.Y, 2)) <= 12.0)
                        {
                            _activeControlPointIndex = i;
                            _activeHandleStroke = selected;
                            _pendingDragUndo = true;
                            return;
                        }
                    }
                }
                else
                {
                    var b = GetStrokeBounds(selected);
                    var handles = GetResizeHandles(b);
                    for (int i = 0; i < handles.Length; i++)
                    {
                        if (Math.Sqrt(Math.Pow(pt.X - handles[i].X, 2) + Math.Pow(pt.Y - handles[i].Y, 2)) <= 11.0)
                        {
                            _activeResizeHandleIndex = i;
                            _activeHandleStroke = selected;
                            _resizeStartBounds = b;
                            _resizeStartPoints = new List<PointD>(selected.Points);
                            _resizeStartStartPt = selected.StartPoint;
                            _resizeStartEndPt = selected.EndPoint;
                            _pendingDragUndo = true;
                            return;
                        }
                    }
                }
            }

            // 2. Hit test stroke body
            var hitStroke = page.Strokes.LastOrDefault(s => IsStrokeHit(s, pt, 10.0));
            if (hitStroke != null)
            {
                if (isDoubleClick && hitStroke.ShapeKind == ShapeKind.CoordinateAxes)
                {
                    EditCoordinateAxes(hitStroke);
                    return;
                }

                if (!hitStroke.IsSelected && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                {
                    foreach (var s in page.Strokes) s.IsSelected = false;
                }
                hitStroke.IsSelected = true;
                _isDraggingSelection = true;
                _pendingDragUndo = true;
                _dragSelectionLast = pt;
                RenderCommittedLayer(page);
                SelectionChanged?.Invoke();
            }
            else
            {
                if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                {
                    foreach (var s in page.Strokes) s.IsSelected = false;
                }

                if (tool == ToolType.Cursor)
                {
                    RenderCommittedLayer(page);
                    SelectionChanged?.Invoke();
                    _isCursorPanning = true;
                    _cursorPanStartViewport = viewportPt;
                    _cursorPanStartH = CanvasScrollViewer.HorizontalOffset;
                    _cursorPanStartV = CanvasScrollViewer.VerticalOffset;
                    PaperCanvas.Cursor = Cursors.ScrollAll;
                    return;
                }

                if (DataStore.Instance.SelectedSelectionMode == Quillfolio.Core.Models.SelectionMode.Lasso)
                {
                    _isSelectingLasso = true;
                    _lassoPoints.Clear();
                    _lassoPoints.Add(pt);
                }
                else
                {
                    _isSelectingMarquee = true;
                    _selectionStart = pt;
                    _selectionCurrent = pt;
                }
                RenderCommittedLayer(page);
                RenderActiveLayer();
                SelectionChanged?.Invoke();
            }
            return;
        }

        // Drawing tools: Pen, FountainPen, Pencil, Highlighter
        StartDrawing(new PointD(pt.X, pt.Y, pressure));
    }

    private void HandlePointerMove(Point pt, double pressure)
    {
        var tool = DataStore.Instance.SelectedTool;

        if (tool == ToolType.Eraser)
        {
            _eraserCursorPos = pt;
            if (_isErasing) EraseAt(pt);
            RenderTopCursorLayer();
            return;
        }

        if (tool == ToolType.Laser && _isLaserActive)
        {
            _laserTrail.Add((pt, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
            RenderActiveLayer();
            return;
        }

        if (tool == ToolType.Select || tool == ToolType.Cursor)
        {
            var page = DataStore.Instance.ActiveNote?.CurrentPage;
            if (page == null) return;

            if (_activeControlPointIndex >= 0 && _activeHandleStroke != null)
            {
                if (_pendingDragUndo)
                {
                    var cur = _activeHandleStroke.Points[_activeControlPointIndex];
                    if (Math.Abs(pt.X - cur.X) < 0.25 && Math.Abs(pt.Y - cur.Y) < 0.25) return;
                    DataStore.Instance.PushUndoState();
                    _pendingDragUndo = false;
                }
                var p = _activeHandleStroke.Points[_activeControlPointIndex];
                p.X = pt.X;
                p.Y = pt.Y;
                _activeHandleStroke.Points[_activeControlPointIndex] = p;
                if (_activeControlPointIndex == 0) _activeHandleStroke.StartPoint = p;
                if (_activeControlPointIndex == _activeHandleStroke.Points.Count - 1) _activeHandleStroke.EndPoint = p;
                _activeHandleStroke.ShapeBounds = RectD.FromPoints(CollectionsMarshal.AsSpan(_activeHandleStroke.Points));
                _strokeVisualCache.Remove(_activeHandleStroke);
                RenderCommittedLayer(page);
                return;
            }

            if (_activeResizeHandleIndex >= 0 && _activeHandleStroke != null && _resizeStartPoints != null)
            {
                if (_pendingDragUndo)
                {
                    DataStore.Instance.PushUndoState();
                    _pendingDragUndo = false;
                }
                ApplyResizeHandle(_activeHandleStroke, _activeResizeHandleIndex, pt);
                RenderCommittedLayer(page);
                return;
            }

            if (_isDraggingSelection)
            {
                double dx = pt.X - _dragSelectionLast.X;
                double dy = pt.Y - _dragSelectionLast.Y;
                if (_pendingDragUndo)
                {
                    if (Math.Abs(dx) < 0.25 && Math.Abs(dy) < 0.25) return;
                    DataStore.Instance.PushUndoState();
                    _pendingDragUndo = false;
                }
                foreach (var s in page.Strokes.Where(s => s.IsSelected))
                {
                    MoveStroke(s, dx, dy);
                }
                _dragSelectionLast = pt;
                RenderCommittedLayer(page);
            }
            else if (_isSelectingMarquee)
            {
                _selectionCurrent = pt;
                RenderActiveLayer();
            }
            else if (_isSelectingLasso)
            {
                if (_lassoPoints.Count == 0 ||
                    Math.Abs(pt.X - _lassoPoints[^1].X) > 2.5 ||
                    Math.Abs(pt.Y - _lassoPoints[^1].Y) > 2.5)
                {
                    _lassoPoints.Add(pt);
                    RenderActiveLayer();
                }
            }
            return;
        }

        if (_isDrawing)
        {
            ContinueDrawing(new PointD(pt.X, pt.Y, pressure));
        }
    }

    private void HandlePointerUp()
    {
        if (_isCursorPanning)
        {
            _isCursorPanning = false;
            UpdateToolInteractionMode();
            return;
        }

        if (_isErasing)
        {
            _isErasing = false;
            if (_eraserUndoPushed)
            {
                _eraserUndoPushed = false;
                DataStore.Instance.NotifyCanvasChanged();
            }
            RenderTopCursorLayer();
            return;
        }

        if (_isLaserActive)
        {
            _isLaserActive = false;
            return;
        }

        if (_activeControlPointIndex >= 0 || _activeResizeHandleIndex >= 0)
        {
            _activeControlPointIndex = -1;
            _activeResizeHandleIndex = -1;
            _activeHandleStroke = null;
            _resizeStartPoints = null;
            bool modified = !_pendingDragUndo;
            _pendingDragUndo = false;
            if (modified) DataStore.Instance.NotifyCanvasChanged();
            return;
        }

        if (_isDraggingSelection)
        {
            _isDraggingSelection = false;
            bool modified = !_pendingDragUndo;
            _pendingDragUndo = false;
            if (modified) DataStore.Instance.NotifyCanvasChanged();
            return;
        }

        if (_isSelectingMarquee)
        {
            _isSelectingMarquee = false;
            var page = DataStore.Instance.ActiveNote?.CurrentPage;
            if (page != null)
            {
                var rect = new Rect(_selectionStart, _selectionCurrent);
                foreach (var stroke in page.Strokes)
                {
                    var b = GetStrokeBounds(stroke);
                    var strokeRect = new Rect(b.X, b.Y, b.Width, b.Height);
                    if (rect.IntersectsWith(strokeRect))
                    {
                        stroke.IsSelected = true;
                    }
                }
                RenderCommittedLayer(page);
                SelectionChanged?.Invoke();
            }
            RenderActiveLayer();
            return;
        }

        if (_isSelectingLasso)
        {
            _isSelectingLasso = false;
            var page = DataStore.Instance.ActiveNote?.CurrentPage;
            if (page != null && _lassoPoints.Count >= 3)
            {
                foreach (var stroke in page.Strokes)
                {
                    if (IsStrokeInPolygon(stroke, _lassoPoints))
                    {
                        stroke.IsSelected = true;
                    }
                }
                RenderCommittedLayer(page);
                SelectionChanged?.Invoke();
            }
            _lassoPoints.Clear();
            RenderActiveLayer();
            return;
        }

        FinishDrawing();
    }

    private void ApplyResizeHandle(DrawingStroke stroke, int handleIdx, Point pt)
    {
        double l = _resizeStartBounds.Left;
        double r = _resizeStartBounds.Right;
        double t = _resizeStartBounds.Top;
        double b = _resizeStartBounds.Bottom;

        const double minSize = 20.0;
        switch (handleIdx)
        {
            case 0: l = Math.Min(pt.X, r - minSize); t = Math.Min(pt.Y, b - minSize); break; // TL
            case 1: t = Math.Min(pt.Y, b - minSize); break;                                  // TC
            case 2: r = Math.Max(pt.X, l + minSize); t = Math.Min(pt.Y, b - minSize); break; // TR
            case 3: r = Math.Max(pt.X, l + minSize); break;                                  // MR
            case 4: r = Math.Max(pt.X, l + minSize); b = Math.Max(pt.Y, t + minSize); break; // BR
            case 5: b = Math.Max(pt.Y, t + minSize); break;                                  // BC
            case 6: l = Math.Min(pt.X, r - minSize); b = Math.Max(pt.Y, t + minSize); break; // BL
            case 7: l = Math.Min(pt.X, r - minSize); break;                                  // ML
        }

        var newBounds = new RectD(l, t, r - l, b - t);
        double oldW = Math.Max(1.0, _resizeStartBounds.Width);
        double oldH = Math.Max(1.0, _resizeStartBounds.Height);
        double scaleX = newBounds.Width / oldW;
        double scaleY = newBounds.Height / oldH;

        for (int i = 0; i < _resizeStartPoints!.Count; i++)
        {
            var src = _resizeStartPoints[i];
            double nx = newBounds.Left + (src.X - _resizeStartBounds.Left) * scaleX;
            double ny = newBounds.Top + (src.Y - _resizeStartBounds.Top) * scaleY;
            stroke.Points[i] = new PointD(nx, ny, src.Pressure, src.Timestamp);
        }

        stroke.ShapeBounds = newBounds;
        if (_resizeStartStartPt.HasValue)
        {
            var sp = _resizeStartStartPt.Value;
            stroke.StartPoint = new PointD(
                newBounds.Left + (sp.X - _resizeStartBounds.Left) * scaleX,
                newBounds.Top + (sp.Y - _resizeStartBounds.Top) * scaleY);
        }
        if (_resizeStartEndPt.HasValue)
        {
            var ep = _resizeStartEndPt.Value;
            stroke.EndPoint = new PointD(
                newBounds.Left + (ep.X - _resizeStartBounds.Left) * scaleX,
                newBounds.Top + (ep.Y - _resizeStartBounds.Top) * scaleY);
        }
        _strokeVisualCache.Remove(stroke);
    }

    private void CancelActiveInput()
    {
        _isPanning = false;
        _isCursorPanning = false;
        _isErasing = false;
        _isLaserActive = false;
        _isDraggingSelection = false;
        _pendingDragUndo = false;
        _isSelectingMarquee = false;
        _isSelectingLasso = false;
        _activeControlPointIndex = -1;
        _activeResizeHandleIndex = -1;
        _activeHandleStroke = null;
        _resizeStartPoints = null;
        _lassoPoints.Clear();
        if (_isDrawing)
        {
            FinishDrawing();
        }
        RenderActiveLayer();
        UpdateToolInteractionMode();
    }

    private void LaserTimer_Tick(object? sender, EventArgs e)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        int fadeMs = Math.Max(300, DataStore.Instance.LaserFadeMs);
        _laserTrail.RemoveAll(item => now - item.TimestampMs > fadeMs + 100);
        if (_laserTrail.Count == 0 && !_isLaserActive)
        {
            _laserTimer.Stop();
        }
        RenderActiveLayer();
    }

    private static bool IsPointInPolygon(Point pt, List<Point> poly)
    {
        bool inside = false;
        int n = poly.Count;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            double xi = poly[i].X, yi = poly[i].Y;
            double xj = poly[j].X, yj = poly[j].Y;
            bool intersect = ((yi > pt.Y) != (yj > pt.Y)) &&
                             (pt.X < (xj - xi) * (pt.Y - yi) / ((yj - yi) == 0 ? 1e-9 : (yj - yi)) + xi);
            if (intersect) inside = !inside;
        }
        return inside;
    }

    private bool IsStrokeInPolygon(DrawingStroke stroke, List<Point> poly)
    {
        if (poly.Count < 3) return false;

        double minPx = double.MaxValue, minPy = double.MaxValue;
        double maxPx = double.MinValue, maxPy = double.MinValue;
        foreach (var p in poly)
        {
            if (p.X < minPx) minPx = p.X;
            if (p.Y < minPy) minPy = p.Y;
            if (p.X > maxPx) maxPx = p.X;
            if (p.Y > maxPy) maxPy = p.Y;
        }

        var b = GetStrokeBounds(stroke);
        if (b.Right < minPx || b.Left > maxPx || b.Bottom < minPy || b.Top > maxPy)
            return false;

        var center = new Point((b.Left + b.Right) * 0.5, (b.Top + b.Bottom) * 0.5);
        if (IsPointInPolygon(center, poly)) return true;

        foreach (var pt in stroke.Points)
        {
            if (IsPointInPolygon(new Point(pt.X, pt.Y), poly)) return true;
        }

        if (stroke.StartPoint is PointD sp && IsPointInPolygon(new Point(sp.X, sp.Y), poly))
            return true;
        if (stroke.EndPoint is PointD ep && IsPointInPolygon(new Point(ep.X, ep.Y), poly))
            return true;

        if (stroke.ShapeBounds is RectD sb)
        {
            if (IsPointInPolygon(new Point(sb.Left, sb.Top), poly) ||
                IsPointInPolygon(new Point(sb.Right, sb.Top), poly) ||
                IsPointInPolygon(new Point(sb.Right, sb.Bottom), poly) ||
                IsPointInPolygon(new Point(sb.Left, sb.Bottom), poly))
                return true;
        }

        return false;
    }

    private void UpdateSelectionFloatingHud(Page page)
    {
        SelectionHudLayer.Children.Clear();
        if (_isDraggingSelection || _activeResizeHandleIndex >= 0 || _activeControlPointIndex >= 0) return;

        var selected = page.Strokes.Where(s => s.IsSelected).ToList();
        if (selected.Count == 0) return;

        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;
        foreach (var s in selected)
        {
            var b = GetStrokeBounds(s);
            if (b.Left < minX) minX = b.Left;
            if (b.Top < minY) minY = b.Top;
            if (b.Right > maxX) maxX = b.Right;
            if (b.Bottom > maxY) maxY = b.Bottom;
        }

        var loc = LocalizationService.Instance;
        var hud = new Border
        {
            Name = "SelectionFloatingHud",
            Background = new SolidColorBrush(Color.FromArgb(245, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(11),
            Padding = new Thickness(4),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Color.FromRgb(15, 23, 42),
                Opacity = 0.14,
                BlurRadius = 12,
                ShadowDepth = 2
            }
        };

        var stack = new StackPanel { Orientation = Orientation.Vertical };

        var axesStroke = selected.FirstOrDefault(s => s.ShapeKind == ShapeKind.CoordinateAxes);
        if (axesStroke != null)
        {
            var editAxesBtn = BuildHudIconButton(
                "M3,17.25V21H6.75L17.81,9.94L14.06,6.19L3,17.25M20.71,7.04C21.1,6.65 21.1,6 20.71,5.63L18.37,3.29C18,2.9 17.35,2.9 16.96,3.29L15.13,5.12L18.88,8.87L20.71,7.04Z",
                Color.FromRgb(124, 58, 237),
                Color.FromRgb(245, 243, 255),
                loc.Get("EditAxes"),
                () => EditCoordinateAxes(axesStroke));
            editAxesBtn.Margin = new Thickness(0, 0, 0, 4);
            stack.Children.Add(editAxesBtn);
        }

        var copyBtn = BuildHudIconButton(
            "M19,21H8V7H19M19,5H8A2,2 0 0,0 6,7V21A2,2 0 0,0 8,23H19A2,2 0 0,0 21,21V7A2,2 0 0,0 19,5M16,1H4A2,2 0 0,0 2,3V17H4V3H16V1Z",
            Color.FromRgb(124, 58, 237),
            Color.FromRgb(245, 243, 255),
            loc.Get("CopySelected"),
            DuplicateSelectedStrokes);

        var delBtn = BuildHudIconButton(
            "M6,19A2,2 0 0,0 8,21H16A2,2 0 0,0 18,19V7H6V19M8,9H16V19H8V9M15.5,4L14.5,3H9.5L8.5,4H5V6H19V4H15.5Z",
            Color.FromRgb(220, 38, 38),
            Color.FromRgb(254, 242, 242),
            loc.Get("Delete"),
            DeleteSelectedStrokes);
        delBtn.Margin = new Thickness(0, 4, 0, 0);

        stack.Children.Add(copyBtn);
        stack.Children.Add(delBtn);
        hud.Child = stack;

        const double hudW = 42;
        double hudH = stack.Children.Count * 36 + 8;
        double left = minX - hudW - 14;
        if (left < 8) left = Math.Min(page.Width - hudW - 8, maxX + 14);
        double top = Math.Clamp((minY + maxY) * 0.5 - hudH * 0.5, 8, Math.Max(8, page.Height - hudH - 8));

        Canvas.SetLeft(hud, left);
        Canvas.SetTop(hud, top);
        SelectionHudLayer.Children.Add(hud);
    }

    private void PaperCanvas_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (IsDescendantOfOverlay(e.OriginalSource as DependencyObject))
        {
            e.Handled = true;
            return;
        }

        var page = DataStore.Instance.ActiveNote?.CurrentPage;
        if (page == null)
        {
            e.Handled = true;
            return;
        }

        Point canvasPt = _lastContextMenuCanvasPoint ?? Mouse.GetPosition(PaperCanvas);
        if (canvasPt.X < 0 || canvasPt.Y < 0 || double.IsNaN(canvasPt.X) || double.IsNaN(canvasPt.Y))
        {
            canvasPt = GetViewportCenterCanvasPoint();
        }
        _lastContextMenuCanvasPoint = null;

        var loc = LocalizationService.Instance;
        var menu = PaperCanvas.ContextMenu!;
        menu.Items.Clear();

        DrawingStroke? hitAxes = page.Strokes.LastOrDefault(s =>
            s.ShapeKind == ShapeKind.CoordinateAxes && IsStrokeHit(s, canvasPt, 14.0))
            ?? page.Strokes.FirstOrDefault(s => s.IsSelected && s.ShapeKind == ShapeKind.CoordinateAxes);

        if (hitAxes != null)
        {
            var editAxesItem = new MenuItem { Header = loc.Get("EditAxes") };
            editAxesItem.Click += (_, _) => EditCoordinateAxes(hitAxes);
            menu.Items.Add(editAxesItem);
            menu.Items.Add(new Separator());
        }

        var pasteItem = new MenuItem
        {
            Header = loc.Get("ContextPaste"),
            InputGestureText = "Ctrl+V",
            IsEnabled = CanPasteFromClipboard()
        };
        pasteItem.Click += (_, _) => PasteFromClipboard(canvasPt);
        menu.Items.Add(pasteItem);

        var copyItem = new MenuItem
        {
            Header = loc.Get("ContextCopy"),
            InputGestureText = "Ctrl+C",
            IsEnabled = HasSelectedStrokes()
        };
        copyItem.Click += (_, _) => CopySelectedStrokes();
        menu.Items.Add(copyItem);

        var dupItem = new MenuItem
        {
            Header = loc.Get("ContextDuplicate"),
            InputGestureText = "Ctrl+D",
            IsEnabled = HasSelectedStrokes()
        };
        dupItem.Click += (_, _) => DuplicateSelectedStrokes();
        menu.Items.Add(dupItem);

        var delItem = new MenuItem
        {
            Header = loc.Get("ContextDelete"),
            InputGestureText = "Del",
            IsEnabled = HasSelectedStrokes() || _activeTextOverlay != null || _activeTableOverlay != null || _activeImageOverlay != null
        };
        delItem.Click += (_, _) => DeleteActiveOverlayOrSelected();
        menu.Items.Add(delItem);

        menu.Items.Add(new Separator());

        var selectAllItem = new MenuItem
        {
            Header = loc.Get("ContextSelectAll"),
            InputGestureText = "Ctrl+A",
            IsEnabled = page.Strokes.Count > 0
        };
        selectAllItem.Click += (_, _) => SelectAllStrokes();
        menu.Items.Add(selectAllItem);

        var undoItem = new MenuItem
        {
            Header = loc.Get("Undo"),
            InputGestureText = "Ctrl+Z",
            IsEnabled = DataStore.Instance.CanUndo
        };
        undoItem.Click += (_, _) => DataStore.Instance.Undo();
        menu.Items.Add(undoItem);

        var redoItem = new MenuItem
        {
            Header = loc.Get("Redo"),
            InputGestureText = "Ctrl+Y",
            IsEnabled = DataStore.Instance.CanRedo
        };
        redoItem.Click += (_, _) => DataStore.Instance.Redo();
        menu.Items.Add(redoItem);

        menu.Items.Add(new Separator());

        var insertImgItem = new MenuItem { Header = loc.Get("InsertImageFromFile") };
        insertImgItem.Click += (_, _) => InsertImageFromFileDialog(canvasPt);
        menu.Items.Add(insertImgItem);

        var insertTextItem = new MenuItem { Header = loc.Get("InsertTextBox") };
        insertTextItem.Click += (_, _) =>
        {
            DataStore.Instance.PushUndoState();
            var tb = new FloatingTextBox
            {
                X = Math.Max(24, canvasPt.X - 150),
                Y = Math.Max(24, canvasPt.Y - 40),
                Width = 300,
                Height = 140,
                IsStickyNote = false,
                BackgroundColor = "Transparent",
                TextColor = DataStore.Instance.SelectedColorHex
            };
            page.TextBoxes.Add(tb);
            ActivateTextBoxOnNextSync(tb.Id);
            DataStore.Instance.NotifyCanvasChanged();
        };
        menu.Items.Add(insertTextItem);

        var insertStickyItem = new MenuItem { Header = loc.Get("InsertStickyNote") };
        insertStickyItem.Click += (_, _) =>
        {
            DataStore.Instance.PushUndoState();
            var tb = new FloatingTextBox
            {
                X = Math.Max(24, canvasPt.X - 120),
                Y = Math.Max(24, canvasPt.Y - 80),
                Width = 240,
                Height = 160,
                IsStickyNote = true,
                BackgroundColor = "#FEF08A",
                TextColor = "#1E293B"
            };
            page.TextBoxes.Add(tb);
            ActivateTextBoxOnNextSync(tb.Id);
            DataStore.Instance.NotifyCanvasChanged();
        };
        menu.Items.Add(insertStickyItem);

        var insertTableItem = new MenuItem { Header = loc.Get("InsertTable") + " (3×3)" };
        insertTableItem.Click += (_, _) =>
        {
            DataStore.Instance.PushUndoState();
            var tbl = new DynamicTable(Math.Max(24, canvasPt.X - 210), Math.Max(24, canvasPt.Y - 65), 3, 3);
            page.Tables.Add(tbl);
            ActivateTableOnNextSync(tbl.Id);
            DataStore.Instance.NotifyCanvasChanged();
        };
        menu.Items.Add(insertTableItem);

        menu.Items.Add(new Separator());

        var clearPageItem = new MenuItem
        {
            Header = loc.Get("ClearPage"),
            Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38))
        };
        clearPageItem.Click += (_, _) =>
        {
            if (Quillfolio.WPF.Views.ConfirmDialog.ShowConfirm(
                    Window.GetWindow(this),
                    loc.Get("ClearCanvas"),
                    loc.Get("ConfirmClearPage"),
                    loc.Get("ClearCanvas"),
                    loc.Get("Close"),
                    isDestructive: true))
            {
                DataStore.Instance.ClearCurrentPage();
            }
        };
        menu.Items.Add(clearPageItem);
    }

    private static Button BuildHudIconButton(string pathData, Color fgColor, Color hoverBgColor, string tooltip, Action action)
    {
        var btn = new Button
        {
            Width = 32,
            Height = 32,
            Focusable = false,
            ToolTip = tooltip,
            Cursor = Cursors.Hand,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Content = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse(pathData),
                Fill = new SolidColorBrush(fgColor),
                Width = 15,
                Height = 15,
                Stretch = Stretch.Uniform
            }
        };

        var tpl = new ControlTemplate(typeof(Button));
        var bdFactory = new FrameworkElementFactory(typeof(Border));
        bdFactory.Name = "Bd";
        bdFactory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
        bdFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        var cpFactory = new FrameworkElementFactory(typeof(ContentPresenter));
        cpFactory.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        cpFactory.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        bdFactory.AppendChild(cpFactory);
        tpl.VisualTree = bdFactory;
        var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hoverTrigger.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(hoverBgColor), "Bd"));
        tpl.Triggers.Add(hoverTrigger);
        btn.Template = tpl;

        Stylus.SetIsPressAndHoldEnabled(btn, false);
        Stylus.SetIsFlicksEnabled(btn, false);

        bool stylusDown = false;
        btn.PreviewStylusDown += (_, e) => { stylusDown = true; e.Handled = true; };
        btn.PreviewStylusUp += (_, e) =>
        {
            if (stylusDown)
            {
                stylusDown = false;
                e.Handled = true;
                action();
            }
        };
        btn.Click += (_, _) => action();

        return btn;
    }

    private void StartDrawing(PointD pt)
    {
        _isDrawing = true;
        _activeStrokePoints.Clear();
        _activeStrokePoints.Add(pt);
        _lastHoldPoint = pt;

        if (DataStore.Instance.IsShapeSnapEnabled && DataStore.Instance.SelectedTool != ToolType.Highlighter)
        {
            RefreshHoldDelay();
            _holdToSnapTimer.Start();
        }
    }

    private void ContinueDrawing(PointD pt)
    {
        if (!_isDrawing) return;
        EnsureCanvasCoversPoint(pt);

        if (_activeStrokePoints.Count > 0)
        {
            var prev = _activeStrokePoints[^1];
            double dx = pt.X - prev.X;
            double dy = pt.Y - prev.Y;
            if (dx * dx + dy * dy < 1.25 && Math.Abs(pt.Pressure - prev.Pressure) < 0.08)
            {
                return;
            }
        }

        _activeStrokePoints.Add(pt);

        if (pt.DistanceTo(_lastHoldPoint) > 6.0)
        {
            _lastHoldPoint = pt;
            if (DataStore.Instance.IsShapeSnapEnabled && DataStore.Instance.SelectedTool != ToolType.Highlighter)
            {
                _holdToSnapTimer.Stop();
                _holdToSnapTimer.Start();
            }
        }

        RenderActiveLayer();
    }

    private void FinishDrawing()
    {
        if (!_isDrawing) return;
        _isDrawing = false;
        _holdToSnapTimer.Stop();

        if (_activeStrokePoints.Count >= 2)
        {
            DataStore.Instance.PushUndoState();

            var pointsToStore = DataStore.Instance.IsSmoothingEnabled
                ? SplineSmoother.SmoothStroke(_activeStrokePoints)
                : new List<PointD>(_activeStrokePoints);
            var tool = DataStore.Instance.SelectedTool;
            var stroke = new DrawingStroke
            {
                Tool = tool,
                ColorHex = DataStore.Instance.SelectedColorHex,
                Thickness = DataStore.Instance.SelectedThickness,
                Opacity = GetDefaultOpacity(tool),
                IsSmoothed = DataStore.Instance.IsSmoothingEnabled,
                Points = pointsToStore
            };

            DataStore.Instance.ActiveNote?.CurrentPage.Strokes.Add(stroke);
            DataStore.Instance.NotifyCanvasChanged();
        }

        _activeStrokePoints.Clear();
        RenderActiveLayer();
    }

    private void HoldToSnapTimer_Tick(object? sender, EventArgs e)
    {
        _holdToSnapTimer.Stop();
        if (!_isDrawing || _activeStrokePoints.Count < 6) return;

        var recognition = ShapeRecognizer.Recognize(_activeStrokePoints);
        if (recognition.Kind != ShapeKind.None)
        {
            DataStore.Instance.PushUndoState();

            var tool = DataStore.Instance.SelectedTool;
            var snappedStroke = new DrawingStroke
            {
                Tool = tool,
                ColorHex = DataStore.Instance.SelectedColorHex,
                Thickness = DataStore.Instance.SelectedThickness,
                Opacity = GetDefaultOpacity(tool),
                IsSmoothed = false,
                ShapeKind = recognition.Kind,
                ShapeBounds = recognition.Bounds,
                StartPoint = recognition.StartPoint,
                EndPoint = recognition.EndPoint,
                Points = recognition.GeometryPoints
            };

            DataStore.Instance.ActiveNote?.CurrentPage.Strokes.Add(snappedStroke);
            _activeStrokePoints.Clear();
            _isDrawing = false;
            DataStore.Instance.NotifyCanvasChanged();
            RenderActiveLayer();
        }
    }

    #endregion
}

public class VisualHost : FrameworkElement
{
    private readonly Visual _visual;

    public VisualHost(Visual visual)
    {
        _visual = visual;
        IsHitTestVisible = false;
        AddVisualChild(_visual);
        AddLogicalChild(_visual);
    }

    protected override int VisualChildrenCount => 1;
    protected override Visual GetVisualChild(int index) => _visual;
}
