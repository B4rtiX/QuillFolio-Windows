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
using System.Text.Json.Serialization;

namespace Quillfolio.Core.Models;

public class DrawingStroke
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [JsonPropertyName("tool")]
    public ToolType Tool { get; set; } = ToolType.Pen;

    [JsonPropertyName("color")]
    public string ColorHex { get; set; } = "#1E1E1E";

    [JsonPropertyName("thickness")]
    public double Thickness { get; set; } = 3.0;

    [JsonPropertyName("points")]
    public List<PointD> Points { get; set; } = new();

    [JsonPropertyName("isSmoothed")]
    public bool IsSmoothed { get; set; } = true;

    [JsonPropertyName("shapeKind")]
    public ShapeKind ShapeKind { get; set; } = ShapeKind.None;

    [JsonPropertyName("shapeBounds")]
    public RectD? ShapeBounds { get; set; }

    [JsonPropertyName("rotationDegrees")]
    public double RotationDegrees { get; set; } = 0.0;

    [JsonPropertyName("startPoint")]
    public PointD? StartPoint { get; set; }

    [JsonPropertyName("endPoint")]
    public PointD? EndPoint { get; set; }

    [JsonPropertyName("isReversed")]
    public bool IsReversed { get; set; } = false;

    [JsonPropertyName("opacity")]
    public double Opacity { get; set; } = 1.0;

    [JsonPropertyName("axesOptions")]
    public CoordinateAxesOptions? AxesOptions { get; set; }

    [JsonIgnore]
    public bool IsSelected { get; set; } = false;

    public DrawingStroke Clone()
    {
        return new DrawingStroke
        {
            Id = Guid.NewGuid(),
            Tool = this.Tool,
            ColorHex = this.ColorHex,
            Thickness = this.Thickness,
            Points = new List<PointD>(this.Points),
            IsSmoothed = this.IsSmoothed,
            ShapeKind = this.ShapeKind,
            ShapeBounds = this.ShapeBounds,
            RotationDegrees = this.RotationDegrees,
            StartPoint = this.StartPoint,
            EndPoint = this.EndPoint,
            IsReversed = this.IsReversed,
            Opacity = this.Opacity,
            AxesOptions = this.AxesOptions?.Clone()
        };
    }

    public void ReverseDirection()
    {
        if (ShapeKind == ShapeKind.Arrow || ShapeKind == ShapeKind.DoubleArrow || ShapeKind == ShapeKind.Line)
        {
            var temp = StartPoint;
            StartPoint = EndPoint;
            EndPoint = temp;
            IsReversed = !IsReversed;
            Points.Reverse();
        }
    }
}

public class CoordinateAxesOptions
{
    [JsonPropertyName("minX")]
    public int MinX { get; set; } = -5;

    [JsonPropertyName("maxX")]
    public int MaxX { get; set; } = 5;

    [JsonPropertyName("minY")]
    public int MinY { get; set; } = -5;

    [JsonPropertyName("maxY")]
    public int MaxY { get; set; } = 5;

    [JsonPropertyName("step")]
    public int Step { get; set; } = 1;

    [JsonPropertyName("showLabels")]
    public bool ShowLabels { get; set; } = true;

    [JsonPropertyName("showGrid")]
    public bool ShowGrid { get; set; } = false;

    [JsonPropertyName("showXAxis")]
    public bool ShowXAxis { get; set; } = true;

    [JsonPropertyName("showYAxis")]
    public bool ShowYAxis { get; set; } = true;

    public CoordinateAxesOptions Clone() => new()
    {
        MinX = MinX,
        MaxX = MaxX,
        MinY = MinY,
        MaxY = MaxY,
        Step = Step,
        ShowLabels = ShowLabels,
        ShowGrid = ShowGrid,
        ShowXAxis = ShowXAxis,
        ShowYAxis = ShowYAxis
    };
}
