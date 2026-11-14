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
using Quillfolio.Core.Models;

namespace Quillfolio.Core.Geometry;

public static class SplineSmoother
{
    private const double JitterThreshold = 1.6; // px

    public static List<PointD> SmoothStroke(List<PointD> rawPoints)
    {
        if (rawPoints == null || rawPoints.Count < 3)
            return rawPoints == null ? new List<PointD>() : new List<PointD>(rawPoints);

        // 1. Anti-Jitter Filter
        var filtered = FilterJitter(rawPoints);
        if (filtered.Count < 3) return filtered;

        // 2. Spatial & Pressure Weighted Moving Average (removes digitizer stair-stepping)
        var smoothed = SmoothCoordinatesAndPressure(filtered);

        // 3. Catmull-Rom Centripetal Spline Interpolation (8 subdivisions for silky curves)
        return InterpolateCatmullRom(smoothed, subdivisions: 8);
    }

    private static List<PointD> FilterJitter(List<PointD> points)
    {
        var result = new List<PointD> { points[0] };
        var last = points[0];

        for (int i = 1; i < points.Count; i++)
        {
            var curr = points[i];
            double dist = last.DistanceTo(curr);
            if (dist >= JitterThreshold || i == points.Count - 1)
            {
                result.Add(curr);
                last = curr;
            }
        }
        return result;
    }

    private static List<PointD> SmoothCoordinatesAndPressure(List<PointD> points)
    {
        var result = new List<PointD>(points.Count) { points[0] };
        for (int i = 1; i < points.Count - 1; i++)
        {
            var prev = points[i - 1];
            var curr = points[i];
            var next = points[i + 1];

            double x = 0.25 * prev.X + 0.5 * curr.X + 0.25 * next.X;
            double y = 0.25 * prev.Y + 0.5 * curr.Y + 0.25 * next.Y;
            double p = (prev.Pressure + curr.Pressure + next.Pressure) / 3.0;
            result.Add(new PointD(x, y, p, curr.Timestamp));
        }
        result.Add(points[^1]);
        return result;
    }

    private static List<PointD> InterpolateCatmullRom(List<PointD> points, int subdivisions)
    {
        var result = new List<PointD>();
        int count = points.Count;

        for (int i = 0; i < count - 1; i++)
        {
            var p0 = i > 0 ? points[i - 1] : points[i];
            var p1 = points[i];
            var p2 = points[i + 1];
            var p3 = i + 2 < count ? points[i + 2] : p2;

            for (int s = 0; s < subdivisions; s++)
            {
                double t = (double)s / subdivisions;
                double t2 = t * t;
                double t3 = t2 * t;

                // Centripetal Catmull-Rom basis matrix
                double x = 0.5 * ((2 * p1.X) +
                    (-p0.X + p2.X) * t +
                    (2 * p0.X - 5 * p1.X + 4 * p2.X - p3.X) * t2 +
                    (-p0.X + 3 * p1.X - 3 * p2.X + p3.X) * t3);

                double y = 0.5 * ((2 * p1.Y) +
                    (-p0.Y + p2.Y) * t +
                    (2 * p0.Y - 5 * p1.Y + 4 * p2.Y - p3.Y) * t2 +
                    (-p0.Y + 3 * p1.Y - 3 * p2.Y + p3.Y) * t3);

                double p = p1.Pressure + (p2.Pressure - p1.Pressure) * t;
                long time = (long)(p1.Timestamp + (p2.Timestamp - p1.Timestamp) * t);

                result.Add(new PointD(x, y, p, time));
            }
        }

        result.Add(points[count - 1]);
        return result;
    }
}
