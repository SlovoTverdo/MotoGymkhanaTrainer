using MotoGymkhanaTrainer.Tracks;

namespace MotoGymkhanaTrainer;

/// <summary>Central tolerances for deterministic desktop marking sampling.</summary>
public static class PathSamplingSettings
{
    public const float MaximumChordLengthMeters = 0.35f;
    public const float FlatnessToleranceMeters = 0.02f;
    public const int MaximumRecursionDepth = 12;
    public const float DuplicatePointToleranceMeters = 0.000001f;
}

/// <summary>Temporary sampled path geometry; never serialized.</summary>
public sealed record SampledPath(
    Point2Dto[] Points,
    float[] CumulativeDistances,
    float TotalLength);

/// <summary>Adaptively samples line/cubic marking paths in domain metres.</summary>
public static class PathSampler
{
    /// <summary>
    /// Samples an already transformed Path. Calling this after an Exercise affine
    /// transform makes tolerance and cumulative length world-space quantities.
    /// </summary>
    public static SampledPath Sample(PathDefinition path)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(path.Start);
        ArgumentNullException.ThrowIfNull(path.Segments);

        var points = new List<Point2Dto>();
        AddDistinct(points, path.Start);
        Point2Dto current = path.Start;
        foreach (PathSegmentDefinition segment in path.Segments)
        {
            switch (segment)
            {
                case LinePathSegmentDefinition line:
                    AddDistinct(points, line.End);
                    current = line.End;
                    break;

                case CubicBezierPathSegmentDefinition cubic:
                    SampleCubic(points, current, cubic.Control1, cubic.Control2, cubic.End, 0);
                    current = cubic.End;
                    break;

                default:
                    throw new InvalidDataException(
                        $"Unsupported Path segment CLR type '{segment?.GetType().Name ?? "<null>"}'.");
            }
        }

        float[] cumulative = new float[points.Count];
        for (int index = 1; index < points.Count; index++)
        {
            cumulative[index] = cumulative[index - 1] + Distance(points[index - 1], points[index]);
        }

        return new SampledPath([.. points], cumulative, cumulative.Length == 0 ? 0.0f : cumulative[^1]);
    }

    private static void SampleCubic(
        ICollection<Point2Dto> output,
        Point2Dto start,
        Point2Dto control1,
        Point2Dto control2,
        Point2Dto end,
        int depth)
    {
        if (depth >= PathSamplingSettings.MaximumRecursionDepth || IsFlatEnough(start, control1, control2, end))
        {
            AddDistinct(output, end);
            return;
        }

        Point2Dto p01 = Midpoint(start, control1);
        Point2Dto p12 = Midpoint(control1, control2);
        Point2Dto p23 = Midpoint(control2, end);
        Point2Dto p012 = Midpoint(p01, p12);
        Point2Dto p123 = Midpoint(p12, p23);
        Point2Dto middle = Midpoint(p012, p123);
        SampleCubic(output, start, p01, p012, middle, depth + 1);
        SampleCubic(output, middle, p123, p23, end, depth + 1);
    }

    private static bool IsFlatEnough(
        Point2Dto start,
        Point2Dto control1,
        Point2Dto control2,
        Point2Dto end)
    {
        float chord = Distance(start, end);
        if (chord > PathSamplingSettings.MaximumChordLengthMeters)
        {
            return false;
        }

        // Perpendicular distance alone misses collinear cubics whose controls
        // overshoot or reverse along the chord. The control polygon excess
        // keeps those curves subdividing until their backtracking is retained.
        float controlPolygonLength = Distance(start, control1) +
            Distance(control1, control2) + Distance(control2, end);
        if (controlPolygonLength - chord > PathSamplingSettings.FlatnessToleranceMeters)
        {
            return false;
        }

        float flatness = MathF.Max(
            DistanceToInfiniteLine(control1, start, end),
            DistanceToInfiniteLine(control2, start, end));
        return flatness <= PathSamplingSettings.FlatnessToleranceMeters;
    }

    private static float DistanceToInfiniteLine(Point2Dto point, Point2Dto start, Point2Dto end)
    {
        float dx = end.X - start.X;
        float dy = end.Y - start.Y;
        float length = MathF.Sqrt(dx * dx + dy * dy);
        if (length <= PathSamplingSettings.DuplicatePointToleranceMeters)
        {
            return Distance(point, start);
        }

        return MathF.Abs(dx * (start.Y - point.Y) - (start.X - point.X) * dy) / length;
    }

    private static Point2Dto Midpoint(Point2Dto left, Point2Dto right) => new()
    {
        X = (left.X + right.X) * 0.5f,
        Y = (left.Y + right.Y) * 0.5f,
    };

    private static void AddDistinct(ICollection<Point2Dto> output, Point2Dto point)
    {
        if (output.LastOrDefault() is Point2Dto previous &&
            Distance(previous, point) <= PathSamplingSettings.DuplicatePointToleranceMeters)
        {
            return;
        }

        output.Add(new Point2Dto { X = point.X, Y = point.Y });
    }

    private static float Distance(Point2Dto left, Point2Dto right)
    {
        float dx = right.X - left.X;
        float dy = right.Y - left.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }
}
