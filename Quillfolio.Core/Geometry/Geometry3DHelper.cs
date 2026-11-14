using System;
using System.Collections.Generic;
using Quillfolio.Core.Models;

namespace Quillfolio.Core.Geometry;

public sealed record WireframePolyline(IReadOnlyList<PointD> Points, bool IsDashed);

public static class Geometry3DHelper
{
    public static bool Is3DSolid(ShapeKind kind) => kind is
        ShapeKind.Cube3D or
        ShapeKind.Cuboid3D or
        ShapeKind.TriangularPrism3D or
        ShapeKind.HexagonalPrism3D or
        ShapeKind.SquarePyramid3D or
        ShapeKind.Tetrahedron3D or
        ShapeKind.Cylinder3D or
        ShapeKind.Cone3D or
        ShapeKind.Sphere3D;

    public static (double Width, double Height) GetDefaultSize(ShapeKind kind) => kind switch
    {
        ShapeKind.Cube3D => (160, 160),
        ShapeKind.Cuboid3D => (220, 150),
        ShapeKind.TriangularPrism3D => (190, 150),
        ShapeKind.HexagonalPrism3D => (150, 190),
        ShapeKind.SquarePyramid3D => (170, 175),
        ShapeKind.Tetrahedron3D => (170, 170),
        ShapeKind.Cylinder3D => (145, 185),
        ShapeKind.Cone3D => (155, 185),
        ShapeKind.Sphere3D => (165, 165),
        _ => (160, 160)
    };

    public static List<WireframePolyline> BuildWireframe(ShapeKind kind, RectD b)
    {
        var result = new List<WireframePolyline>();
        double x = b.X, y = b.Y;
        double w = Math.Max(8.0, b.Width);
        double h = Math.Max(8.0, b.Height);

        switch (kind)
        {
            case ShapeKind.Cube3D:
            case ShapeKind.Cuboid3D:
            {
                double dx = w * (kind == ShapeKind.Cube3D ? 0.28 : 0.24);
                double dy = h * (kind == ShapeKind.Cube3D ? 0.28 : 0.24);

                var flt = new PointD(x, y + dy);
                var frt = new PointD(x + w - dx, y + dy);
                var frb = new PointD(x + w - dx, y + h);
                var flb = new PointD(x, y + h);

                var blt = new PointD(x + dx, y);
                var brt = new PointD(x + w, y);
                var brb = new PointD(x + w, y + h - dy);
                var blb = new PointD(x + dx, y + h - dy);

                result.Add(new WireframePolyline(new[] { flt, frt, frb, flb, flt }, false));
                result.Add(new WireframePolyline(new[] { flt, blt, brt, brb, frb }, false));
                result.Add(new WireframePolyline(new[] { frt, brt }, false));

                result.Add(new WireframePolyline(new[] { blt, blb, brb }, true));
                result.Add(new WireframePolyline(new[] { flb, blb }, true));
                break;
            }

            case ShapeKind.TriangularPrism3D:
            {
                double dx = w * 0.28;
                double dy = h * 0.24;

                var fTop = new PointD(x + (w - dx) * 0.5, y + dy);
                var fRight = new PointD(x + w - dx, y + h);
                var fLeft = new PointD(x, y + h);

                var bTop = new PointD(fTop.X + dx, y);
                var bRight = new PointD(x + w, y + h - dy);
                var bLeft = new PointD(x + dx, y + h - dy);

                result.Add(new WireframePolyline(new[] { fTop, fRight, fLeft, fTop }, false));
                result.Add(new WireframePolyline(new[] { fTop, bTop, bRight, fRight }, false));

                result.Add(new WireframePolyline(new[] { bTop, bLeft, bRight }, true));
                result.Add(new WireframePolyline(new[] { fLeft, bLeft }, true));
                break;
            }

            case ShapeKind.HexagonalPrism3D:
            {
                double cx = x + w * 0.5;
                double rx = w * 0.5;
                double ry = Math.Min(h * 0.16, rx * 0.48);
                double topCy = y + ry;
                double botCy = y + h - ry;

                var top = new PointD[6];
                var bot = new PointD[6];
                for (int i = 0; i < 6; i++)
                {
                    double a = i * Math.PI / 3.0;
                    double vx = cx + rx * Math.Cos(a);
                    double dySin = ry * Math.Sin(a);
                    top[i] = new PointD(vx, topCy + dySin);
                    bot[i] = new PointD(vx, botCy + dySin);
                }

                // Top hexagonal face (all visible)
                result.Add(new WireframePolyline(new[] { top[0], top[1], top[2], top[3], top[4], top[5], top[0] }, false));
                // Bottom front half (indices 3 -> 2 -> 1 -> 0)
                result.Add(new WireframePolyline(new[] { bot[3], bot[2], bot[1], bot[0] }, false));
                // Visible front vertical ribs
                for (int i = 0; i <= 3; i++)
                {
                    result.Add(new WireframePolyline(new[] { top[i], bot[i] }, false));
                }

                // Bottom back half (indices 0 -> 5 -> 4 -> 3)
                result.Add(new WireframePolyline(new[] { bot[0], bot[5], bot[4], bot[3] }, true));
                // Hidden back vertical ribs
                result.Add(new WireframePolyline(new[] { top[4], bot[4] }, true));
                result.Add(new WireframePolyline(new[] { top[5], bot[5] }, true));
                break;
            }

            case ShapeKind.SquarePyramid3D:
            {
                var apex = new PointD(x + w * 0.50, y);
                var fl = new PointD(x, y + h);
                var fr = new PointD(x + w * 0.72, y + h);
                var br = new PointD(x + w, y + h * 0.72);
                var bl = new PointD(x + w * 0.28, y + h * 0.72);

                result.Add(new WireframePolyline(new[] { fl, fr, br }, false));
                result.Add(new WireframePolyline(new[] { apex, fl }, false));
                result.Add(new WireframePolyline(new[] { apex, fr }, false));
                result.Add(new WireframePolyline(new[] { apex, br }, false));

                result.Add(new WireframePolyline(new[] { fl, bl, br }, true));
                result.Add(new WireframePolyline(new[] { apex, bl }, true));
                break;
            }

            case ShapeKind.Tetrahedron3D:
            {
                var apex = new PointD(x + w * 0.48, y);
                var bl = new PointD(x, y + h * 0.88);
                var bf = new PointD(x + w * 0.54, y + h);
                var br = new PointD(x + w, y + h * 0.76);

                result.Add(new WireframePolyline(new[] { bl, bf, br }, false));
                result.Add(new WireframePolyline(new[] { apex, bl }, false));
                result.Add(new WireframePolyline(new[] { apex, bf }, false));
                result.Add(new WireframePolyline(new[] { apex, br }, false));

                result.Add(new WireframePolyline(new[] { bl, br }, true));
                break;
            }

            case ShapeKind.Cylinder3D:
            {
                double cx = x + w * 0.5;
                double rx = w * 0.5;
                double ry = Math.Min(h * 0.16, rx * 0.46);
                double topY = y + ry;
                double botY = y + h - ry;

                result.Add(new WireframePolyline(SampleEllipseArc(cx, topY, rx, ry, 0, Math.PI * 2, 48), false));
                result.Add(new WireframePolyline(SampleEllipseArc(cx, botY, rx, ry, 0, Math.PI, 28), false));
                result.Add(new WireframePolyline(SampleEllipseArc(cx, botY, rx, ry, Math.PI, Math.PI * 2, 28), true));
                result.Add(new WireframePolyline(new[] { new PointD(x, topY), new PointD(x, botY) }, false));
                result.Add(new WireframePolyline(new[] { new PointD(x + w, topY), new PointD(x + w, botY) }, false));
                break;
            }

            case ShapeKind.Cone3D:
            {
                double cx = x + w * 0.5;
                double rx = w * 0.5;
                double ry = Math.Min(h * 0.17, rx * 0.46);
                double botY = y + h - ry;
                var apex = new PointD(cx, y);

                result.Add(new WireframePolyline(SampleEllipseArc(cx, botY, rx, ry, 0, Math.PI, 28), false));
                result.Add(new WireframePolyline(SampleEllipseArc(cx, botY, rx, ry, Math.PI, Math.PI * 2, 28), true));
                result.Add(new WireframePolyline(new[] { new PointD(x, botY), apex, new PointD(x + w, botY) }, false));
                break;
            }

            case ShapeKind.Sphere3D:
            {
                double cx = x + w * 0.5;
                double cy = y + h * 0.5;
                double rx = w * 0.5;
                double ry = h * 0.5;
                double eqRy = Math.Max(4.0, h * 0.16);

                result.Add(new WireframePolyline(SampleEllipseArc(cx, cy, rx, ry, 0, Math.PI * 2, 60), false));
                result.Add(new WireframePolyline(SampleEllipseArc(cx, cy, rx, eqRy, 0, Math.PI, 28), false));
                result.Add(new WireframePolyline(SampleEllipseArc(cx, cy, rx, eqRy, Math.PI, Math.PI * 2, 28), true));
                break;
            }
        }

        return result;
    }

    public static List<PointD> BuildBoundingPerimeterPoints(ShapeKind kind, RectD b)
    {
        var pts = new List<PointD>();
        var parts = BuildWireframe(kind, b);
        foreach (var part in parts)
        {
            pts.AddRange(part.Points);
        }
        return pts;
    }

    private static List<PointD> SampleEllipseArc(double cx, double cy, double rx, double ry, double startRad, double endRad, int steps)
    {
        var pts = new List<PointD>(steps + 1);
        for (int i = 0; i <= steps; i++)
        {
            double a = startRad + (endRad - startRad) * (i / (double)steps);
            pts.Add(new PointD(cx + rx * Math.Cos(a), cy + ry * Math.Sin(a)));
        }
        return pts;
    }
}
