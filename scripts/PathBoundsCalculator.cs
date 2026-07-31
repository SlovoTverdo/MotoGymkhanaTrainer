using MotoGymkhanaTrainer.Tracks;

namespace MotoGymkhanaTrainer;

/// <summary>Axis-aligned marking bounds in domain metres.</summary>
public readonly record struct PathBounds(float MinX, float MinY, float MaxX, float MaxY)
{
    public bool IsOutside(float width, float length) =>
        MinX < -width * 0.5f || MaxX > width * 0.5f ||
        MinY < -length * 0.5f || MaxY > length * 0.5f;
}

/// <summary>Calculates analytical line/cubic bounds including half line width.</summary>
public static class PathBoundsCalculator
{
    public static PathBounds Calculate(PathDefinition path, float widthMeters)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!float.IsFinite(widthMeters) || widthMeters < 0.0f)
            throw new ArgumentOutOfRangeException(nameof(widthMeters));

        float minX = path.Start.X;
        float maxX = path.Start.X;
        float minY = path.Start.Y;
        float maxY = path.Start.Y;
        Point2Dto start = path.Start;
        foreach (PathSegmentDefinition segment in path.Segments)
        {
            switch (segment)
            {
                case LinePathSegmentDefinition line:
                    Include(line.End, ref minX, ref minY, ref maxX, ref maxY);
                    start = line.End;
                    break;
                case CubicBezierPathSegmentDefinition cubic:
                    Include(cubic.End, ref minX, ref minY, ref maxX, ref maxY);
                    foreach (float t in DerivativeRoots(start.X, cubic.Control1.X, cubic.Control2.X, cubic.End.X))
                        Include(TrajectoryGeometry.EvaluateCubicBezier(
                            start, cubic.Control1, cubic.Control2, cubic.End, t),
                            ref minX, ref minY, ref maxX, ref maxY);
                    foreach (float t in DerivativeRoots(start.Y, cubic.Control1.Y, cubic.Control2.Y, cubic.End.Y))
                        Include(TrajectoryGeometry.EvaluateCubicBezier(
                            start, cubic.Control1, cubic.Control2, cubic.End, t),
                            ref minX, ref minY, ref maxX, ref maxY);
                    start = cubic.End;
                    break;
                default:
                    throw new InvalidDataException("Path contains an unsupported segment type.");
            }
        }

        float expansion = widthMeters * 0.5f;
        return new PathBounds(minX - expansion, minY - expansion, maxX + expansion, maxY + expansion);
    }

    private static IEnumerable<float> DerivativeRoots(float p0, float p1, float p2, float p3)
    {
        float a = -p0 + 3.0f * p1 - 3.0f * p2 + p3;
        float b = 3.0f * p0 - 6.0f * p1 + 3.0f * p2;
        float c = -3.0f * p0 + 3.0f * p1;
        float quadratic = 3.0f * a;
        float linear = 2.0f * b;
        const float epsilon = 0.0000001f;
        if (MathF.Abs(quadratic) <= epsilon)
        {
            if (MathF.Abs(linear) <= epsilon) yield break;
            float root = -c / linear;
            if (root is > 0.0f and < 1.0f) yield return root;
            yield break;
        }

        float discriminant = linear * linear - 4.0f * quadratic * c;
        if (discriminant < 0.0f) yield break;
        float squareRoot = MathF.Sqrt(MathF.Max(0.0f, discriminant));
        float first = (-linear + squareRoot) / (2.0f * quadratic);
        float second = (-linear - squareRoot) / (2.0f * quadratic);
        if (first is > 0.0f and < 1.0f) yield return first;
        if (second is > 0.0f and < 1.0f && MathF.Abs(second - first) > epsilon) yield return second;
    }

    private static void Include(
        Point2Dto point,
        ref float minX,
        ref float minY,
        ref float maxX,
        ref float maxY)
    {
        minX = MathF.Min(minX, point.X);
        minY = MathF.Min(minY, point.Y);
        maxX = MathF.Max(maxX, point.X);
        maxY = MathF.Max(maxY, point.Y);
    }
}
