using MotoGymkhanaTrainer.Tracks;

namespace MotoGymkhanaTrainer;

/// <summary>One temporary visible stroke derived from persisted marking points.</summary>
public readonly record struct MarkingStroke(Point2Dto Start, Point2Dto End);

/// <summary>Render-only world-space centerlines and dot centers for one marking.</summary>
public sealed record MarkingStyleGeometry(
    IReadOnlyList<MarkingStroke> Strokes,
    IReadOnlyList<Point2Dto> Dots);

/// <summary>
/// Converts persisted marking paths into temporary render strokes. The DTO points
/// remain the only geometry written to JSON; dash samples are never persisted.
/// </summary>
public static class MarkingGeometry
{
    private const float DashedLengthMeters = 0.75f;
    private const float DashedGapMeters = 0.4f;
    private const float DottedSpacingMeters = 0.32f;

    /// <summary>Returns whether a style is defined by the current contract.</summary>
    public static bool IsSupportedStyle(string? style) =>
        style is "solid" or "dashed" or "dotted";

    /// <summary>Normalizes canonical RGB and recognized legacy named colors.</summary>
    public static bool TryNormalizeColor(string? value, bool allowLegacyNames, out string canonical)
    {
        canonical = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string candidate = value.Trim();
        if (candidate.Length == 7 && candidate[0] == '#' &&
            candidate.Skip(1).All(Uri.IsHexDigit))
        {
            canonical = candidate.ToUpperInvariant();
            return true;
        }

        if (!allowLegacyNames)
        {
            return false;
        }

        canonical = candidate.ToLowerInvariant() switch
        {
            "red" => "#F21F14",
            "blue" => "#1452FF",
            "yellow" => "#FFD10D",
            "green" => "#1AD938",
            "orange" => "#FF6B14",
            "white" => "#FFFFFF",
            _ => string.Empty,
        };
        return canonical.Length > 0;
    }

    /// <summary>
    /// Creates render-only centerlines in world metres. Pattern phase follows the
    /// sampled cumulative length and therefore never resets at Path segment joins.
    /// </summary>
    public static MarkingStyleGeometry CreateStyleGeometry(SampledPath sampled, string style)
    {
        IReadOnlyList<Point2Dto> points = sampled.Points;
        if (points.Count < 2)
        {
            return new MarkingStyleGeometry([], []);
        }

        if (style == "solid")
        {
            var solid = new List<MarkingStroke>(points.Count - 1);
            for (int index = 0; index < points.Count - 1; index++)
            {
                solid.Add(new MarkingStroke(points[index], points[index + 1]));
            }

            return new MarkingStyleGeometry(solid, []);
        }

        if (style == "dotted")
        {
            var dots = new List<Point2Dto>();
            for (float distance = 0.0f;
                 distance <= sampled.TotalLength + PathSamplingSettings.DuplicatePointToleranceMeters;
                 distance += DottedSpacingMeters)
            {
                dots.Add(SampleAtDistance(sampled, MathF.Min(distance, sampled.TotalLength)));
            }

            return new MarkingStyleGeometry([], dots);
        }

        float visibleLength = DashedLengthMeters;
        float gapLength = DashedGapMeters;
        bool drawing = true;
        float remainingPatternPart = visibleLength;
        var strokes = new List<MarkingStroke>();

        for (int index = 0; index < points.Count - 1; index++)
        {
            Point2Dto start = points[index];
            Point2Dto end = points[index + 1];
            float deltaX = end.X - start.X;
            float deltaY = end.Y - start.Y;
            float length = MathF.Sqrt(deltaX * deltaX + deltaY * deltaY);
            if (length <= float.Epsilon)
            {
                continue;
            }

            float local = 0.0f;
            while (local < length - 0.000001f)
            {
                float step = MathF.Min(remainingPatternPart, length - local);
                float next = local + step;
                if (drawing && step > float.Epsilon)
                {
                    strokes.Add(new MarkingStroke(
                        Lerp(start, end, local / length),
                        Lerp(start, end, next / length)));
                }

                local = next;
                remainingPatternPart -= step;
                if (remainingPatternPart <= 0.000001f)
                {
                    drawing = !drawing;
                    remainingPatternPart = drawing ? visibleLength : gapLength;
                }
            }
        }

        return new MarkingStyleGeometry(strokes, []);
    }

    /// <summary>
    /// Compatibility helper for non-marking editor overlays such as transition
    /// previews. Persisted marking renderers should use <see cref="CreateStyleGeometry"/>.
    /// </summary>
    public static IReadOnlyList<MarkingStroke> CreateStrokes(
        IReadOnlyList<Point2Dto> points,
        string style)
    {
        float[] cumulative = new float[points.Count];
        for (int index = 1; index < points.Count; index++)
        {
            float dx = points[index].X - points[index - 1].X;
            float dy = points[index].Y - points[index - 1].Y;
            cumulative[index] = cumulative[index - 1] + MathF.Sqrt(dx * dx + dy * dy);
        }

        SampledPath sampled = new([.. points], cumulative, cumulative.Length == 0 ? 0.0f : cumulative[^1]);
        MarkingStyleGeometry geometry = CreateStyleGeometry(sampled, style);
        if (style != "dotted") return geometry.Strokes;

        // Transition overlays do not request dotted today. Retain a tiny stroke
        // representation so older pure geometry callers still receive visible data.
        const float halfLength = 0.04f;
        return geometry.Dots.Select(dot => new MarkingStroke(
            new Point2Dto { X = dot.X - halfLength, Y = dot.Y },
            new Point2Dto { X = dot.X + halfLength, Y = dot.Y })).ToArray();
    }

    private static Point2Dto Lerp(Point2Dto start, Point2Dto end, float amount)
    {
        return new Point2Dto
        {
            X = start.X + (end.X - start.X) * amount,
            Y = start.Y + (end.Y - start.Y) * amount,
        };
    }

    private static Point2Dto SampleAtDistance(SampledPath sampled, float distance)
    {
        if (distance <= 0.0f) return sampled.Points[0];
        if (distance >= sampled.TotalLength) return sampled.Points[^1];
        int upper = Array.BinarySearch(sampled.CumulativeDistances, distance);
        if (upper >= 0) return sampled.Points[upper];
        upper = ~upper;
        int lower = upper - 1;
        float span = sampled.CumulativeDistances[upper] - sampled.CumulativeDistances[lower];
        float amount = span <= float.Epsilon
            ? 0.0f
            : (distance - sampled.CumulativeDistances[lower]) / span;
        return Lerp(sampled.Points[lower], sampled.Points[upper], amount);
    }
}
