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
using System.Linq;
using Quillfolio.Core.Models;

namespace Quillfolio.Core.Geometry;

public class ShapeRecognitionResult
{
    public ShapeKind Kind { get; set; } = ShapeKind.None;
    public RectD Bounds { get; set; }
    public PointD? StartPoint { get; set; }
    public PointD? EndPoint { get; set; }
    public List<PointD> GeometryPoints { get; set; } = new();
    public double RotationDegrees { get; set; } = 0.0;
}

public static class ShapeRecognizer
{
    public static ShapeRecognitionResult Recognize(List<PointD> strokePoints)
    {
        var result = new ShapeRecognitionResult { Kind = ShapeKind.None };
        if (strokePoints == null || strokePoints.Count < 5) return result;

        var bounds = RectD.FromPoints(strokePoints.ToArray());
        result.Bounds = bounds;

        // Ignore tiny accidental dots or microscopic gestures
        if (bounds.Width < 18 && bounds.Height < 18) return result;

        // 1. Handwriting Protection Filter
        if (IsLikelyHandwrittenText(strokePoints, bounds))
        {
            return result;
        }

        double strokeLength = CalculatePerimeter(strokePoints);
        if (strokeLength < 10.0) return result;
        double endDistance = strokePoints[0].DistanceTo(strokePoints[^1]);
        bool isClosed = endDistance < Math.Max(36.0, strokeLength * 0.22) && strokeLength > endDistance * 1.6;

        // 2. Closed Shape Analysis
        if (isClosed)
        {
            bool ellipseHit = TryDetectCircleOrEllipse(strokePoints, bounds, out var circleResult, out double ellipseErr);

            // A. Polygon Simplification via Douglas-Peucker (adaptive epsilon)
            double epsilon = Math.Clamp(strokeLength * 0.026, 6.5, 22.0);
            var simplified = SimplifyClosedPolygon(strokePoints, epsilon);
            int vertexCount = simplified.Count - 1; // Last equals first
            double polyErr = ComputePolygonFitError(strokePoints, simplified, bounds);

            // Stretched / Regular Hexagon check BEFORE triangle/quad fallback
            if (TryDetectHexagon(strokePoints, bounds, ellipseHit, ellipseErr, out var hexResult))
            {
                return hexResult;
            }

            if (vertexCount == 3 && (!ellipseHit || polyErr < ellipseErr * 1.18 || polyErr < 0.095))
            {
                return BuildTriangle(simplified, bounds);
            }

            if (vertexCount == 4)
            {
                if (ellipseHit && ellipseErr < 0.075 && polyErr > ellipseErr * 1.45)
                {
                    return circleResult;
                }
                return BuildQuadrilateral(simplified, bounds);
            }

            if (vertexCount == 5)
            {
                if (ellipseHit && ellipseErr < 0.09 && polyErr > ellipseErr * 1.2)
                {
                    return circleResult;
                }
                var merged4 = CollapseShortestEdgeIfTiny(simplified, strokeLength * 0.16);
                if (merged4 != null)
                {
                    return BuildQuadrilateral(merged4, bounds);
                }
            }

            if (ellipseHit)
            {
                return circleResult;
            }
        }
        else
        {
            // 3. Open Shape Analysis
            // Check clean monotone straight line BEFORE arrow/double-arrow heuristics so slow-start
            // or diagonal straight lines never falsely trigger DoubleArrow/Arrow.
            if (IsCleanStraightLine(strokePoints, endDistance, strokeLength))
            {
                return BuildLineResult(strokePoints, bounds);
            }

            if (TryDetectDoubleArrow(strokePoints, out var doubleArrowResult))
            {
                return doubleArrowResult;
            }

            if (TryDetectArrow(strokePoints, out var arrowResult))
            {
                return arrowResult;
            }

            // Relaxed Straight Line Fallback
            if (endDistance > 24.0 && endDistance / strokeLength > 0.85)
            {
                return BuildLineResult(strokePoints, bounds);
            }
        }

        return result;
    }

    public static ShapeRecognitionResult CreateTemplateShape(ShapeKind kind, double cx, double cy, double width = 160, double height = 120)
    {
        double rx = width * 0.5;
        double ry = height * 0.5;
        var bounds = new RectD(cx - rx, cy - ry, width, height);

        switch (kind)
        {
            case ShapeKind.Line:
            case ShapeKind.Arrow:
            case ShapeKind.DoubleArrow:
                var sp = new PointD(cx - rx, cy);
                var c1 = new PointD(cx - rx / 3.0, cy);
                var c2 = new PointD(cx + rx / 3.0, cy);
                var ep = new PointD(cx + rx, cy);
                return new ShapeRecognitionResult
                {
                    Kind = kind,
                    Bounds = new RectD(sp.X, cy - 10, width, 20),
                    StartPoint = sp,
                    EndPoint = ep,
                    GeometryPoints = new List<PointD> { sp, c1, c2, ep }
                };

            case ShapeKind.Circle:
                double r = Math.Min(rx, ry);
                var cBounds = new RectD(cx - r, cy - r, r * 2, r * 2);
                return new ShapeRecognitionResult
                {
                    Kind = ShapeKind.Circle,
                    Bounds = cBounds,
                    GeometryPoints = GenerateEllipsePoints(cx, cy, r, r, 64)
                };

            case ShapeKind.Ellipse:
                return new ShapeRecognitionResult
                {
                    Kind = ShapeKind.Ellipse,
                    Bounds = bounds,
                    GeometryPoints = GenerateEllipsePoints(cx, cy, rx, ry, 64)
                };

            case ShapeKind.Square:
                double s = Math.Min(width, height);
                var sq = new RectD(cx - s * 0.5, cy - s * 0.5, s, s);
                return new ShapeRecognitionResult
                {
                    Kind = ShapeKind.Square,
                    Bounds = sq,
                    GeometryPoints = RectPoints(sq)
                };

            case ShapeKind.Rectangle:
            case ShapeKind.RoundedRectangle:
                return new ShapeRecognitionResult
                {
                    Kind = kind,
                    Bounds = bounds,
                    GeometryPoints = RectPoints(bounds)
                };

            case ShapeKind.Triangle:
                var tri = new List<PointD>
                {
                    new(cx, cy - ry),
                    new(cx + rx, cy + ry),
                    new(cx - rx, cy + ry),
                    new(cx, cy - ry)
                };
                return new ShapeRecognitionResult { Kind = kind, Bounds = bounds, GeometryPoints = tri };

            case ShapeKind.RightTriangle:
                var rtri = new List<PointD>
                {
                    new(cx - rx, cy - ry),
                    new(cx - rx, cy + ry),
                    new(cx + rx, cy + ry),
                    new(cx - rx, cy - ry)
                };
                return new ShapeRecognitionResult { Kind = kind, Bounds = bounds, GeometryPoints = rtri };

            case ShapeKind.Trapezoid:
                var trap = new List<PointD>
                {
                    new(cx - rx * 0.55, cy - ry),
                    new(cx + rx * 0.55, cy - ry),
                    new(cx + rx, cy + ry),
                    new(cx - rx, cy + ry),
                    new(cx - rx * 0.55, cy - ry)
                };
                return new ShapeRecognitionResult { Kind = kind, Bounds = bounds, GeometryPoints = trap };

            case ShapeKind.RightTrapezoid:
                var rtrap = new List<PointD>
                {
                    new(cx - rx, cy - ry),
                    new(cx + rx * 0.45, cy - ry),
                    new(cx + rx, cy + ry),
                    new(cx - rx, cy + ry),
                    new(cx - rx, cy - ry)
                };
                return new ShapeRecognitionResult { Kind = kind, Bounds = bounds, GeometryPoints = rtrap };

            case ShapeKind.Parallelogram:
                double skew = rx * 0.35;
                var para = new List<PointD>
                {
                    new(cx - rx + skew, cy - ry),
                    new(cx + rx, cy - ry),
                    new(cx + rx - skew, cy + ry),
                    new(cx - rx, cy + ry),
                    new(cx - rx + skew, cy - ry)
                };
                return new ShapeRecognitionResult { Kind = kind, Bounds = bounds, GeometryPoints = para };

            case ShapeKind.Rhombus:
                var rhom = new List<PointD>
                {
                    new(cx, cy - ry),
                    new(cx + rx, cy),
                    new(cx, cy + ry),
                    new(cx - rx, cy),
                    new(cx, cy - ry)
                };
                return new ShapeRecognitionResult { Kind = kind, Bounds = bounds, GeometryPoints = rhom };

            case ShapeKind.Hexagon:
                return BuildHexagon(bounds);

            case ShapeKind.Star:
                var starPts = new List<PointD>();
                double rOut = Math.Min(rx, ry);
                double rIn = rOut * 0.45;
                for (int i = 0; i < 10; i++)
                {
                    double rad = (i % 2 == 0) ? rOut : rIn;
                    double ang = -Math.PI / 2.0 + i * Math.PI / 5.0;
                    starPts.Add(new PointD(cx + rad * Math.Cos(ang), cy + rad * Math.Sin(ang)));
                }
                starPts.Add(starPts[0]);
                return new ShapeRecognitionResult { Kind = ShapeKind.Star, Bounds = bounds, GeometryPoints = starPts };

            case ShapeKind.Cloud:
            case ShapeKind.CoordinateAxes:
                return new ShapeRecognitionResult
                {
                    Kind = kind,
                    Bounds = bounds,
                    GeometryPoints = RectPoints(bounds)
                };

            default:
                if (Geometry3DHelper.Is3DSolid(kind))
                {
                    return new ShapeRecognitionResult
                    {
                        Kind = kind,
                        Bounds = bounds,
                        GeometryPoints = Geometry3DHelper.BuildBoundingPerimeterPoints(kind, bounds)
                    };
                }
                return new ShapeRecognitionResult { Kind = ShapeKind.Rectangle, Bounds = bounds, GeometryPoints = RectPoints(bounds) };
        }
    }

    private static List<PointD> RectPoints(RectD b) => new()
    {
        new(b.Left, b.Top),
        new(b.Right, b.Top),
        new(b.Right, b.Bottom),
        new(b.Left, b.Bottom),
        new(b.Left, b.Top)
    };

    private static List<PointD> SimplifyClosedPolygon(List<PointD> strokePoints, double epsilon)
    {
        var simplified = DouglasPeucker(strokePoints, epsilon);
        if (simplified.Count >= 2 && simplified[0].DistanceTo(simplified[^1]) < epsilon * 1.6)
        {
            simplified[^1] = simplified[0];
        }
        else if (simplified.Count >= 3)
        {
            simplified.Add(simplified[0]);
        }

        // Merge consecutive vertices that are too close
        var cleaned = new List<PointD>();
        foreach (var pt in simplified)
        {
            if (cleaned.Count == 0 || cleaned[^1].DistanceTo(pt) > epsilon * 0.75)
            {
                cleaned.Add(pt);
            }
        }
        if (cleaned.Count >= 2 && cleaned[0].DistanceTo(cleaned[^1]) > 0.1)
        {
            cleaned[^1] = cleaned[0];
        }
        return cleaned;
    }

    private static bool IsLikelyHandwrittenText(List<PointD> points, RectD bounds)
    {
        double aspectRatio = bounds.Width / Math.Max(1.0, bounds.Height);

        int inflectionX = 0, inflectionY = 0;
        int lastDirX = 0, lastDirY = 0;
        for (int i = 2; i < points.Count; i++)
        {
            double dx = points[i].X - points[i - 1].X;
            int dirX = dx > 1.8 ? 1 : (dx < -1.8 ? -1 : 0);
            if (dirX != 0 && dirX != lastDirX)
            {
                if (lastDirX != 0) inflectionX++;
                lastDirX = dirX;
            }

            double dy = points[i].Y - points[i - 1].Y;
            int dirY = dy > 1.8 ? 1 : (dy < -1.8 ? -1 : 0);
            if (dirY != 0 && dirY != lastDirY)
            {
                if (lastDirY != 0) inflectionY++;
                lastDirY = dirY;
            }
        }

        int maxInflections = Math.Max(inflectionX, inflectionY);
        // Wide horizontal strokes are handwritten words ONLY when they oscillate (letters/cursive),
        // never when they are straight horizontal underlines or horizontal double arrows.
        if (aspectRatio > 3.5 && bounds.Height < 48 && maxInflections >= 5) return true;
        if (maxInflections >= 12) return true;
        return maxInflections >= 8 && aspectRatio > 2.2;
    }

    private static double ComputePolygonFitError(List<PointD> points, List<PointD> poly, RectD bounds)
    {
        if (poly.Count < 4) return double.MaxValue;
        double minDim = Math.Max(1.0, Math.Min(bounds.Width, bounds.Height));
        double totalPolyDist = 0;
        foreach (var pt in points)
        {
            double bestEdgeDist = double.MaxValue;
            for (int i = 1; i < poly.Count; i++)
            {
                double d = PerpendicularDistance(pt, poly[i - 1], poly[i]);
                if (d < bestEdgeDist) bestEdgeDist = d;
            }
            totalPolyDist += bestEdgeDist;
        }
        return (totalPolyDist / points.Count) / (minDim * 0.5);
    }

    private static List<PointD>? CollapseShortestEdgeIfTiny(List<PointD> poly, double maxEdgeLen)
    {
        if (poly.Count != 6) return null; // 5 vertices + closing vertex
        int shortestIdx = -1;
        double shortestLen = double.MaxValue;
        for (int i = 0; i < 5; i++)
        {
            double len = poly[i].DistanceTo(poly[i + 1]);
            if (len < shortestLen)
            {
                shortestLen = len;
                shortestIdx = i;
            }
        }
        if (shortestIdx < 0 || shortestLen > maxEdgeLen) return null;

        var merged = new List<PointD>(5);
        int nextIdx = (shortestIdx + 1) % 5;
        var mid = new PointD(
            (poly[shortestIdx].X + poly[nextIdx].X) * 0.5,
            (poly[shortestIdx].Y + poly[nextIdx].Y) * 0.5);

        for (int i = 0; i < 5; i++)
        {
            if (i == shortestIdx) merged.Add(mid);
            else if (i != nextIdx) merged.Add(poly[i]);
        }
        merged.Add(merged[0]);
        return merged;
    }

    private static bool TryDetectHexagon(List<PointD> points, RectD bounds, bool ellipseHit, double ellipseErr, out ShapeRecognitionResult result)
    {
        result = new ShapeRecognitionResult();
        double cx = bounds.CenterX;
        double cy = bounds.CenterY;
        double rx = bounds.Width * 0.5;
        double ry = bounds.Height * 0.5;
        if (rx < 14 || ry < 14) return false;

        // Normalize into [-1, 1] x [-1, 1] so stretched wide/tall hexagons and regular hexagons behave uniformly
        var normPts = new List<PointD>(points.Count);
        foreach (var p in points)
        {
            normPts.Add(new PointD((p.X - cx) / rx, (p.Y - cy) / ry));
        }

        List<PointD>? bestHex = null;
        double bestErr = double.MaxValue;

        foreach (double eps in new[] { 0.072, 0.092, 0.115 })
        {
            var cand = SimplifyClosedPolygon(normPts, eps);
            if (cand.Count == 8) // 7 vertices: collapse flattest/nearly-collinear corner if one side was slightly bent
            {
                cand = CollapseMostCollinearVertex(cand, maxTurnDeg: 28.0) ?? cand;
            }
            if (cand.Count != 7) continue;

            if (!ValidateConvexHexagon(cand)) continue;

            double polyErr = ComputeNormalizedPolygonError(normPts, cand);
            if (polyErr < bestErr)
            {
                bestErr = polyErr;
                bestHex = cand;
            }
        }

        if (bestHex == null) return false;

        // Strong Ellipse vs Hexagon tie-breaker:
        // On a smooth ellipse, chords dip ~0.07-0.10 while ellipseErr is < 0.045.
        // On a true hexagon (including stretched wide/tall hexagons), points hug the 6 flat chords tightly.
        if (ellipseHit)
        {
            if (bestErr >= 0.075) return false;
            if (ellipseErr < 0.042 && bestErr > ellipseErr * 0.95) return false;
            if (bestErr >= ellipseErr * 0.94) return false;
        }
        else if (bestErr >= 0.115)
        {
            return false;
        }

        result = BuildHexagon(bounds);
        return true;
    }

    private static List<PointD>? CollapseMostCollinearVertex(List<PointD> closedPoly, double maxTurnDeg)
    {
        int n = closedPoly.Count - 1;
        if (n < 4) return null;
        int flatIdx = -1;
        double minTurn = double.MaxValue;
        for (int i = 0; i < n; i++)
        {
            var prev = closedPoly[(i - 1 + n) % n];
            var curr = closedPoly[i];
            var next = closedPoly[(i + 1) % n];
            double interiorDeg = CornerAngleDegrees(prev, curr, next);
            double turnDeg = Math.Abs(180.0 - interiorDeg);
            if (turnDeg < minTurn)
            {
                minTurn = turnDeg;
                flatIdx = i;
            }
        }
        if (flatIdx < 0 || minTurn > maxTurnDeg) return null;

        var reduced = new List<PointD>(n);
        for (int i = 0; i < n; i++)
        {
            if (i != flatIdx) reduced.Add(closedPoly[i]);
        }
        reduced.Add(reduced[0]);
        return reduced;
    }

    private static bool ValidateConvexHexagon(List<PointD> hex)
    {
        if (hex.Count != 7) return false;
        double[] edgeLens = new double[6];
        double totalLen = 0;
        for (int i = 0; i < 6; i++)
        {
            edgeLens[i] = hex[i].DistanceTo(hex[i + 1]);
            totalLen += edgeLens[i];
        }
        double avgEdge = totalLen / 6.0;
        if (avgEdge < 0.25) return false;
        foreach (double len in edgeLens)
        {
            if (len < avgEdge * 0.22) return false;
        }

        int windingSign = 0;
        for (int i = 0; i < 6; i++)
        {
            var prev = hex[(i + 5) % 6];
            var curr = hex[i];
            var next = hex[(i + 1) % 6];

            double ax = curr.X - prev.X, ay = curr.Y - prev.Y;
            double bx = next.X - curr.X, by = next.Y - curr.Y;
            double cross = ax * by - ay * bx;
            int sign = cross > 1e-4 ? 1 : (cross < -1e-4 ? -1 : 0);
            if (sign == 0) return false;
            if (windingSign == 0) windingSign = sign;
            else if (sign != windingSign) return false;

            double interiorDeg = CornerAngleDegrees(prev, curr, next);
            double turnDeg = 180.0 - interiorDeg;
            if (turnDeg < 22.0 || turnDeg > 115.0) return false;
        }
        return true;
    }

    private static double ComputeNormalizedPolygonError(List<PointD> normPts, List<PointD> normPoly)
    {
        double total = 0;
        foreach (var pt in normPts)
        {
            double best = double.MaxValue;
            for (int i = 1; i < normPoly.Count; i++)
            {
                double d = PerpendicularDistance(pt, normPoly[i - 1], normPoly[i]);
                if (d < best) best = d;
            }
            total += best;
        }
        return total / Math.Max(1, normPts.Count);
    }

    private static bool TryDetectCircleOrEllipse(List<PointD> points, RectD bounds, out ShapeRecognitionResult result, out double avgError)
    {
        result = new ShapeRecognitionResult();
        avgError = double.MaxValue;
        double cx = bounds.CenterX;
        double cy = bounds.CenterY;
        double rx = bounds.Width / 2.0;
        double ry = bounds.Height / 2.0;

        if (rx < 12 || ry < 12) return false;

        double totalError = 0;
        foreach (var p in points)
        {
            double normX = (p.X - cx) / rx;
            double normY = (p.Y - cy) / ry;
            double distFromEllipse = Math.Abs(Math.Sqrt(normX * normX + normY * normY) - 1.0);
            totalError += distFromEllipse;
        }

        avgError = totalError / points.Count;
        if (avgError < 0.20)
        {
            double ratio = Math.Min(rx, ry) / Math.Max(rx, ry);
            bool isCircle = ratio > 0.82;

            result.Kind = isCircle ? ShapeKind.Circle : ShapeKind.Ellipse;
            result.Bounds = bounds;
            result.GeometryPoints = GenerateEllipsePoints(cx, cy, rx, ry, 64);
            return true;
        }

        return false;
    }

    private static ShapeRecognitionResult BuildTriangle(List<PointD> vertices, RectD bounds)
    {
        var v0 = vertices[0];
        var v1 = vertices[1];
        var v2 = vertices[2];

        double a2 = Math.Pow(v0.DistanceTo(v1), 2);
        double b2 = Math.Pow(v1.DistanceTo(v2), 2);
        double c2 = Math.Pow(v2.DistanceTo(v0), 2);

        double[] sides = { a2, b2, c2 };
        Array.Sort(sides);
        bool isRightAngle = sides[2] > 1 && Math.Abs(sides[0] + sides[1] - sides[2]) / sides[2] < 0.18;

        return new ShapeRecognitionResult
        {
            Kind = isRightAngle ? ShapeKind.RightTriangle : ShapeKind.Triangle,
            Bounds = bounds,
            GeometryPoints = new List<PointD> { v0, v1, v2, v0 }
        };
    }

    private static double AngleBetweenEdges(PointD a, PointD b, PointD c, PointD d)
    {
        double v1x = b.X - a.X, v1y = b.Y - a.Y;
        double v2x = d.X - c.X, v2y = d.Y - c.Y;
        double mag1 = Math.Sqrt(v1x * v1x + v1y * v1y);
        double mag2 = Math.Sqrt(v2x * v2x + v2y * v2y);
        if (mag1 < 1 || mag2 < 1) return Math.PI;
        double dot = Math.Clamp((v1x * v2x + v1y * v2y) / (mag1 * mag2), -1.0, 1.0);
        double ang = Math.Acos(Math.Abs(dot)); // 0 means parallel
        return ang;
    }

    private static double CornerAngleDegrees(PointD prev, PointD curr, PointD next)
    {
        double v1x = prev.X - curr.X, v1y = prev.Y - curr.Y;
        double v2x = next.X - curr.X, v2y = next.Y - curr.Y;
        double m1 = Math.Sqrt(v1x * v1x + v1y * v1y);
        double m2 = Math.Sqrt(v2x * v2x + v2y * v2y);
        if (m1 < 1 || m2 < 1) return 90.0;
        double dot = Math.Clamp((v1x * v2x + v1y * v2y) / (m1 * m2), -1.0, 1.0);
        return Math.Acos(dot) * 180.0 / Math.PI;
    }

    private static ShapeRecognitionResult BuildQuadrilateral(List<PointD> vertices, RectD bounds)
    {
        var p0 = vertices[0];
        var p1 = vertices[1];
        var p2 = vertices[2];
        var p3 = vertices[3];

        // Parallelism of opposite edge pairs (0 = perfectly parallel)
        double pair1Angle = AngleBetweenEdges(p0, p1, p3, p2); // in radians
        double pair2Angle = AngleBetweenEdges(p1, p2, p0, p3);
        const double parallelTol = 0.28; // ~16 deg

        bool pair1Parallel = pair1Angle < parallelTol;
        bool pair2Parallel = pair2Angle < parallelTol;

        // Check corner angles
        double c0 = CornerAngleDegrees(p3, p0, p1);
        double c1 = CornerAngleDegrees(p0, p1, p2);
        double c2 = CornerAngleDegrees(p1, p2, p3);
        double c3 = CornerAngleDegrees(p2, p3, p0);
        bool allRightAngles = Math.Abs(c0 - 90) < 16 && Math.Abs(c1 - 90) < 16 &&
                              Math.Abs(c2 - 90) < 16 && Math.Abs(c3 - 90) < 16;

        // 1. Rectangle or Square (axis-aligned or intentionally tilted)
        if (pair1Parallel && pair2Parallel && allRightAngles)
        {
            double edgeAng = Math.Abs(Math.Atan2(p1.Y - p0.Y, p1.X - p0.X) * 180.0 / Math.PI) % 90.0;
            bool isAxisAligned = edgeAng < 14.0 || edgeAng > 76.0;
            if (isAxisAligned)
            {
                double ratio = bounds.Width / Math.Max(1.0, bounds.Height);
                bool isSquare = Math.Abs(ratio - 1.0) < 0.18;
                return new ShapeRecognitionResult
                {
                    Kind = isSquare ? ShapeKind.Square : ShapeKind.Rectangle,
                    Bounds = bounds,
                    GeometryPoints = RectPoints(bounds)
                };
            }
            else
            {
                double ux = p1.X - p0.X, uy = p1.Y - p0.Y;
                double w = Math.Sqrt(ux * ux + uy * uy);
                if (w > 1) { ux /= w; uy /= w; }
                double vxOrtho = -uy, vyOrtho = ux;
                double dotSign = ((p3.X - p0.X) * vxOrtho + (p3.Y - p0.Y) * vyOrtho) >= 0 ? 1.0 : -1.0;
                double h = p0.DistanceTo(p3);
                var r0 = p0;
                var r1 = new PointD(p0.X + ux * w, p0.Y + uy * w);
                var r3 = new PointD(p0.X + vxOrtho * dotSign * h, p0.Y + vyOrtho * dotSign * h);
                var r2 = new PointD(r1.X + (r3.X - r0.X), r1.Y + (r3.Y - r0.Y));
                var rotPts = new List<PointD> { r0, r1, r2, r3, r0 };
                double sideRatioRot = Math.Min(w, h) / Math.Max(1.0, Math.Max(w, h));
                return new ShapeRecognitionResult
                {
                    Kind = sideRatioRot > 0.82 ? ShapeKind.Square : ShapeKind.Rectangle,
                    Bounds = RectD.FromPoints(rotPts.ToArray()),
                    GeometryPoints = rotPts
                };
            }
        }

        // 2. Trapezoid or Right Trapezoid (exactly one pair of opposite sides parallel)
        if (pair1Parallel ^ pair2Parallel)
        {
            bool hasRightCorner = (Math.Abs(c0 - 90) < 14 && Math.Abs(c3 - 90) < 14) ||
                                  (Math.Abs(c1 - 90) < 14 && Math.Abs(c2 - 90) < 14) ||
                                  (Math.Abs(c0 - 90) < 14 && Math.Abs(c1 - 90) < 14) ||
                                  (Math.Abs(c2 - 90) < 14 && Math.Abs(c3 - 90) < 14);
            var trapPts = new List<PointD> { p0, p1, p2, p3, p0 };
            return new ShapeRecognitionResult
            {
                Kind = hasRightCorner ? ShapeKind.RightTrapezoid : ShapeKind.Trapezoid,
                Bounds = RectD.FromPoints(trapPts.ToArray()),
                GeometryPoints = trapPts
            };
        }

        // 3. Rhombus or Parallelogram (preserve user-drawn skew and dimensions)
        double vx = ((p1.X - p0.X) + (p2.X - p3.X)) * 0.5;
        double vy = ((p1.Y - p0.Y) + (p2.Y - p3.Y)) * 0.5;
        var q0 = p0;
        var q1 = new PointD(p0.X + vx, p0.Y + vy);
        var q3 = p3;
        var q2 = new PointD(p3.X + vx, p3.Y + vy);

        double sideA = q0.DistanceTo(q1);
        double sideB = q0.DistanceTo(q3);
        double sideRatio = Math.Min(sideA, sideB) / Math.Max(1.0, Math.Max(sideA, sideB));
        ShapeKind kind = sideRatio > 0.80 ? ShapeKind.Rhombus : ShapeKind.Parallelogram;

        var poly = new List<PointD> { q0, q1, q2, q3, q0 };
        return new ShapeRecognitionResult
        {
            Kind = kind,
            Bounds = RectD.FromPoints(poly.ToArray()),
            GeometryPoints = poly
        };
    }

    private static ShapeRecognitionResult BuildHexagon(RectD bounds)
    {
        double cx = bounds.CenterX;
        double cy = bounds.CenterY;
        double rx = bounds.Width / 2.0;
        double ry = bounds.Height / 2.0;
        double startAngle = ry > rx * 1.12 ? -Math.PI / 2.0 : 0.0;

        var pts = new List<PointD>();
        for (int i = 0; i < 6; i++)
        {
            double angle = startAngle + i * Math.PI / 3.0;
            pts.Add(new PointD(cx + rx * Math.Cos(angle), cy + ry * Math.Sin(angle)));
        }
        pts.Add(pts[0]);

        return new ShapeRecognitionResult
        {
            Kind = ShapeKind.Hexagon,
            Bounds = bounds,
            GeometryPoints = pts
        };
    }

    private static bool IsCleanStraightLine(List<PointD> points, double endDistance, double strokeLength)
    {
        if (points.Count < 2 || endDistance < 24.0 || strokeLength <= 0) return false;
        if (endDistance / strokeLength <= 0.93) return false;

        var start = points[0];
        var end = points[^1];
        double dx = (end.X - start.X) / endDistance;
        double dy = (end.Y - start.Y) / endDistance;
        double maxPerpAllowed = Math.Max(7.5, endDistance * 0.045);

        double maxProjSoFar = 0.0;
        for (int i = 0; i < points.Count; i++)
        {
            double perp = PerpendicularDistance(points[i], start, end);
            if (perp > maxPerpAllowed) return false;

            double proj = (points[i].X - start.X) * dx + (points[i].Y - start.Y) * dy;
            if (proj < -7.0 || proj > endDistance + 7.0) return false;
            if (maxProjSoFar - proj > 7.5) return false; // Genuine foldback detected
            if (proj > maxProjSoFar) maxProjSoFar = proj;
        }

        return true;
    }

    private static ShapeRecognitionResult BuildLineResult(List<PointD> points, RectD bounds)
    {
        var sp = points[0];
        var ep = points[^1];
        var c1 = new PointD(sp.X + (ep.X - sp.X) / 3.0, sp.Y + (ep.Y - sp.Y) / 3.0);
        var c2 = new PointD(sp.X + 2.0 * (ep.X - sp.X) / 3.0, sp.Y + 2.0 * (ep.Y - sp.Y) / 3.0);
        return new ShapeRecognitionResult
        {
            Kind = ShapeKind.Line,
            Bounds = bounds,
            StartPoint = sp,
            EndPoint = ep,
            GeometryPoints = new List<PointD> { sp, c1, c2, ep }
        };
    }

    private static bool TryDetectDoubleArrow(List<PointD> points, out ShapeRecognitionResult result)
    {
        result = new ShapeRecognitionResult();
        int n = points.Count;
        if (n < 16) return false;

        // Estimate dominant shaft direction from the middle half of the stroke
        var pMidStart = points[n / 4];
        var pMidEnd = points[(3 * n) / 4];
        double midDist = pMidStart.DistanceTo(pMidEnd);
        if (midDist < 30.0) return false;

        double dx = (pMidEnd.X - pMidStart.X) / midDist;
        double dy = (pMidEnd.Y - pMidStart.Y) / midDist;

        double[] proj = new double[n];
        for (int i = 0; i < n; i++)
        {
            proj[i] = (points[i].X - points[0].X) * dx + (points[i].Y - points[0].Y) * dy;
        }

        // Start reversal check (supports wing->leftTip->shaft AND leftTip->wing->leftTip->shaft)
        int startLimit = Math.Min(n / 3, n - 4);
        double runMaxStart = proj[0];
        double maxStartBack = 0.0;
        int startTipIdx = 0;
        for (int i = 1; i <= startLimit; i++)
        {
            if (proj[i] > runMaxStart) runMaxStart = proj[i];
            double back = runMaxStart - proj[i];
            if (back > maxStartBack)
            {
                maxStartBack = back;
                startTipIdx = i;
            }
        }
        if (startTipIdx < 2 || maxStartBack < 10.0) return false;

        // End reversal check (shaft reaches rightTip peak, then folds back along -dir)
        int endStart = Math.Max((2 * n) / 3, startTipIdx + 3);
        if (endStart >= n - 2) return false;

        double maxEndProj = double.NegativeInfinity;
        int endTipIdx = endStart;
        for (int i = endStart; i < n; i++)
        {
            if (proj[i] > maxEndProj)
            {
                maxEndProj = proj[i];
                endTipIdx = i;
            }
        }
        if (endTipIdx >= n - 2) return false;

        double maxEndBack = 0.0;
        for (int i = endTipIdx + 1; i < n; i++)
        {
            double back = maxEndProj - proj[i];
            if (back > maxEndBack) maxEndBack = back;
        }
        if (maxEndBack < 10.0) return false;

        var startTip = points[startTipIdx];
        var endTip = points[endTipIdx];
        double shaftLen = startTip.DistanceTo(endTip);
        if (shaftLen < 45.0) return false;

        if (maxStartBack >= shaftLen * 0.38 || maxEndBack >= shaftLen * 0.38) return false;

        // Ensure the shaft between the two tips is reasonably straight
        double maxShaftPerp = Math.Max(14.0, shaftLen * 0.16);
        for (int i = startTipIdx; i <= endTipIdx; i++)
        {
            if (PerpendicularDistance(points[i], startTip, endTip) > maxShaftPerp)
                return false;
        }

        var c1 = new PointD(startTip.X + (endTip.X - startTip.X) / 3.0, startTip.Y + (endTip.Y - startTip.Y) / 3.0);
        var c2 = new PointD(startTip.X + 2.0 * (endTip.X - startTip.X) / 3.0, startTip.Y + 2.0 * (endTip.Y - startTip.Y) / 3.0);
        result.Kind = ShapeKind.DoubleArrow;
        result.Bounds = RectD.FromPoints(points.ToArray());
        result.StartPoint = startTip;
        result.EndPoint = endTip;
        result.GeometryPoints = new List<PointD> { startTip, c1, c2, endTip };
        return true;
    }

    private static bool TryDetectArrow(List<PointD> points, out ShapeRecognitionResult result)
    {
        result = new ShapeRecognitionResult();
        if (points.Count < 10) return false;

        var start = points[0];
        var end = points[^1];

        // Find the farthest point from start in the last half of the stroke (the arrow tip!)
        int searchStart = (int)(points.Count * 0.50);
        int tipIdx = searchStart;
        double maxDist = 0;
        for (int i = searchStart; i < points.Count; i++)
        {
            double d = start.DistanceTo(points[i]);
            if (d > maxDist)
            {
                maxDist = d;
                tipIdx = i;
            }
        }

        if (maxDist < 30.0 || tipIdx >= points.Count - 2) return false;

        var tip = points[tipIdx];
        double foldBack = tip.DistanceTo(end);
        if (foldBack <= 9.0 || foldBack >= maxDist * 0.50) return false;

        // Verify genuine backward projection from tip toward start (not forward/sideways wobble)
        double dx = (tip.X - start.X) / maxDist;
        double dy = (tip.Y - start.Y) / maxDist;
        double backProj = (tip.X - end.X) * dx + (tip.Y - end.Y) * dy;
        if (backProj < 7.5) return false;

        // Ensure shaft [0..tipIdx] is reasonably linear
        double maxShaftPerp = Math.Max(16.0, maxDist * 0.18);
        for (int i = 0; i <= tipIdx; i++)
        {
            if (PerpendicularDistance(points[i], start, tip) > maxShaftPerp)
                return false;
        }

        var c1 = new PointD(start.X + (tip.X - start.X) / 3.0, start.Y + (tip.Y - start.Y) / 3.0);
        var c2 = new PointD(start.X + 2.0 * (tip.X - start.X) / 3.0, start.Y + 2.0 * (tip.Y - start.Y) / 3.0);
        result.Kind = ShapeKind.Arrow;
        result.Bounds = RectD.FromPoints(points.ToArray());
        result.StartPoint = start;
        result.EndPoint = tip;
        result.GeometryPoints = new List<PointD> { start, c1, c2, tip };
        return true;
    }

    private static List<PointD> GenerateEllipsePoints(double cx, double cy, double rx, double ry, int segments)
    {
        var pts = new List<PointD>(segments + 1);
        for (int i = 0; i <= segments; i++)
        {
            double angle = (2.0 * Math.PI * i) / segments;
            pts.Add(new PointD(cx + rx * Math.Cos(angle), cy + ry * Math.Sin(angle)));
        }
        return pts;
    }

    private static List<PointD> DouglasPeucker(List<PointD> points, double epsilon)
    {
        if (points.Count < 3) return new List<PointD>(points);

        double maxDist = 0;
        int maxIndex = 0;

        for (int i = 1; i < points.Count - 1; i++)
        {
            double d = PerpendicularDistance(points[i], points[0], points[^1]);
            if (d > maxDist)
            {
                maxDist = d;
                maxIndex = i;
            }
        }

        if (maxDist > epsilon)
        {
            var left = DouglasPeucker(points.Take(maxIndex + 1).ToList(), epsilon);
            var right = DouglasPeucker(points.Skip(maxIndex).ToList(), epsilon);
            left.RemoveAt(left.Count - 1);
            left.AddRange(right);
            return left;
        }

        return new List<PointD> { points[0], points[^1] };
    }

    private static double PerpendicularDistance(PointD p, PointD lineStart, PointD lineEnd)
    {
        double dx = lineEnd.X - lineStart.X;
        double dy = lineEnd.Y - lineStart.Y;
        double mag = Math.Sqrt(dx * dx + dy * dy);
        if (mag < 0.0001) return p.DistanceTo(lineStart);
        return Math.Abs(dy * p.X - dx * p.Y + lineEnd.X * lineStart.Y - lineEnd.Y * lineStart.X) / mag;
    }

    private static double CalculatePerimeter(List<PointD> pts)
    {
        double total = 0;
        for (int i = 1; i < pts.Count; i++)
        {
            total += pts[i - 1].DistanceTo(pts[i]);
        }
        return total;
    }
}
