using MotoGymkhanaTrainer.Tracks;

namespace MotoGymkhanaTrainer;

/// <summary>One temporary visible stroke derived from persisted marking points.</summary>
public readonly record struct MarkingStroke(Point2Dto Start, Point2Dto End);

/// <summary>
/// Converts persisted marking paths into temporary render strokes. The DTO points
/// remain the only geometry written to JSON; dash samples are never persisted.
/// </summary>
public static class MarkingGeometry
{
    private const float DashedLengthMeters = 0.75f;
    private const float DashedGapMeters = 0.4f;
    private const float DottedLengthMeters = 0.08f;
    private const float DottedGapMeters = 0.24f;

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
    /// Creates render-only line pieces in domain metres. Pattern phase continues
    /// across polyline corners, preventing every persisted segment from restarting
    /// with a dash and producing visibly uneven joins.
    /// </summary>
    public static IReadOnlyList<MarkingStroke> CreateStrokes(
        IReadOnlyList<Point2Dto> points,
        string style)
    {
        if (points.Count < 2)
        {
            return [];
        }

        if (style == "solid")
        {
            var solid = new List<MarkingStroke>(points.Count - 1);
            for (int index = 0; index < points.Count - 1; index++)
            {
                solid.Add(new MarkingStroke(points[index], points[index + 1]));
            }

            return solid;
        }

        float visibleLength = style == "dotted" ? DottedLengthMeters : DashedLengthMeters;
        float gapLength = style == "dotted" ? DottedGapMeters : DashedGapMeters;
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

        return strokes;
    }

    private static Point2Dto Lerp(Point2Dto start, Point2Dto end, float amount)
    {
        return new Point2Dto
        {
            X = start.X + (end.X - start.X) * amount,
            Y = start.Y + (end.Y - start.Y) * amount,
        };
    }
}
