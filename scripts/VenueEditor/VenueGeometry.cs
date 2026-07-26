using MotoGymkhanaTrainer.Tracks;

namespace MotoGymkhanaTrainer.VenueEditor;

/// <summary>Pure geometry shared by Venue validation, hit testing and drawing.</summary>
public static class VenueGeometry
{
    /// <summary>
    /// Builds the world-space rectangle for an object footprint. Venue Y maps to
    /// the asset's local Z, so footprint length uses scale Z; elevation and scale Y
    /// intentionally do not affect this top-down projection.
    /// </summary>
    public static Point2Dto[] TransformFootprint(VenueObjectInstanceDto item)
    {
        float halfWidth = item.Footprint.Width * item.Scale.X * 0.5f;
        float halfLength = item.Footprint.Length * item.Scale.Z * 0.5f;
        Point2Dto[] local =
        [
            new() { X = -halfWidth, Y = -halfLength },
            new() { X = halfWidth, Y = -halfLength },
            new() { X = halfWidth, Y = halfLength },
            new() { X = -halfWidth, Y = halfLength },
        ];
        float radians = item.RotationDeg * MathF.PI / 180.0f;
        float cosine = MathF.Cos(radians);
        float sine = MathF.Sin(radians);
        return local.Select(point => new Point2Dto
        {
            X = point.X * cosine - point.Y * sine + item.Position.X,
            Y = point.X * sine + point.Y * cosine + item.Position.Y,
        }).ToArray();
    }

    /// <summary>Returns true when a world point lies inside a convex footprint.</summary>
    public static bool Contains(IReadOnlyList<Point2Dto> polygon, Point2Dto point)
    {
        bool? sign = null;
        for (int index = 0; index < polygon.Count; index++)
        {
            Point2Dto a = polygon[index];
            Point2Dto b = polygon[(index + 1) % polygon.Count];
            float cross = (b.X - a.X) * (point.Y - a.Y) - (b.Y - a.Y) * (point.X - a.X);
            if (MathF.Abs(cross) <= 0.0001f) continue;
            bool current = cross > 0.0f;
            if (sign.HasValue && sign.Value != current) return false;
            sign = current;
        }
        return true;
    }

    /// <summary>Separating-axis overlap test for two transformed rectangles.</summary>
    public static bool Overlaps(IReadOnlyList<Point2Dto> first, IReadOnlyList<Point2Dto> second)
    {
        return !HasSeparatingAxis(first, second) && !HasSeparatingAxis(second, first);
    }

    /// <summary>Area is centered at the domain origin.</summary>
    public static bool IsOutsideArea(Point2Dto point, VenueAreaDto area) =>
        MathF.Abs(point.X) > area.Width * 0.5f || MathF.Abs(point.Y) > area.Length * 0.5f;

    private static bool HasSeparatingAxis(IReadOnlyList<Point2Dto> source, IReadOnlyList<Point2Dto> other)
    {
        for (int index = 0; index < source.Count; index++)
        {
            Point2Dto a = source[index];
            Point2Dto b = source[(index + 1) % source.Count];
            float axisX = -(b.Y - a.Y);
            float axisY = b.X - a.X;
            Project(source, axisX, axisY, out float sourceMin, out float sourceMax);
            Project(other, axisX, axisY, out float otherMin, out float otherMax);
            if (sourceMax < otherMin || otherMax < sourceMin) return true;
        }
        return false;
    }

    private static void Project(
        IReadOnlyList<Point2Dto> polygon,
        float axisX,
        float axisY,
        out float minimum,
        out float maximum)
    {
        minimum = maximum = polygon[0].X * axisX + polygon[0].Y * axisY;
        for (int index = 1; index < polygon.Count; index++)
        {
            float projection = polygon[index].X * axisX + polygon[index].Y * axisY;
            minimum = MathF.Min(minimum, projection);
            maximum = MathF.Max(maximum, projection);
        }
    }
}
