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
using System.Text.Json.Serialization;

namespace Quillfolio.Core.Models;

public record struct PointD
{
    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }

    [JsonPropertyName("p")]
    public double Pressure { get; set; }

    [JsonPropertyName("t")]
    public long Timestamp { get; set; }

    public PointD(double x, double y, double pressure = 1.0, long timestamp = 0)
    {
        X = x;
        Y = y;
        Pressure = Math.Clamp(pressure, 0.05, 3.0);
        Timestamp = timestamp == 0 ? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() : timestamp;
    }

    public double DistanceTo(PointD other)
    {
        double dx = X - other.X;
        double dy = Y - other.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    public static PointD Lerp(PointD a, PointD b, double t)
    {
        return new PointD(
            a.X + (b.X - a.X) * t,
            a.Y + (b.Y - a.Y) * t,
            a.Pressure + (b.Pressure - a.Pressure) * t,
            (long)(a.Timestamp + (b.Timestamp - a.Timestamp) * t)
        );
    }
}
