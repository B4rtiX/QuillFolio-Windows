using System;
using System.Text.Json.Serialization;

namespace Quillfolio.Core.Models;

public enum ShapeKind
{
    None = 0,
    Line = 1,
    Arrow = 2,
    DoubleArrow = 3,
    Rectangle = 4,
    Square = 5,
    Parallelogram = 6,
    Rhombus = 7,
    Circle = 8,
    Ellipse = 9,
    Triangle = 10,
    RightTriangle = 11,
    Hexagon = 12,
    Star = 13,
    Cloud = 14,
    CoordinateAxes = 15,
    Table = 16,
    RoundedRectangle = 17,
    Trapezoid = 18,
    RightTrapezoid = 19,
    Cube3D = 20,
    Cuboid3D = 21,
    TriangularPrism3D = 22,
    HexagonalPrism3D = 23,
    SquarePyramid3D = 24,
    Tetrahedron3D = 25,
    Cylinder3D = 26,
    Cone3D = 27,
    Sphere3D = 28
}

public record struct RectD
{
    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }

    [JsonPropertyName("width")]
    public double Width { get; set; }

    [JsonPropertyName("height")]
    public double Height { get; set; }

    public RectD(double x, double y, double width, double height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public double Left => X;
    public double Top => Y;
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public double CenterX => X + Width / 2.0;
    public double CenterY => Y + Height / 2.0;

    public bool Contains(double px, double py) =>
        px >= X && px <= X + Width && py >= Y && py <= Y + Height;

    public static RectD FromPoints(ReadOnlySpan<PointD> points)
    {
        if (points.IsEmpty) return new RectD(0, 0, 0, 0);

        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;

        foreach (var pt in points)
        {
            if (pt.X < minX) minX = pt.X;
            if (pt.X > maxX) maxX = pt.X;
            if (pt.Y < minY) minY = pt.Y;
            if (pt.Y > maxY) maxY = pt.Y;
        }

        return new RectD(minX, minY, Math.Max(1.0, maxX - minX), Math.Max(1.0, maxY - minY));
    }
}
