namespace MotoGymkhanaTrainer.Tracks;

/// <summary>
/// Pure trajectory geometry shared by the 2D authoring canvas and the 3D Viewer.
/// Persisted Bezier data always remains four control points; samples returned here
/// are temporary rendering geometry and must never be written back to JSON.
/// </summary>
public static class TrajectoryGeometry
{
    /// <summary>Evaluates a cubic Bezier at a normalized parameter in [0, 1].</summary>
    public static Point2Dto EvaluateCubicBezier(
        Point2Dto start,
        Point2Dto control1,
        Point2Dto control2,
        Point2Dto end,
        float t)
    {
        float clampedT = Math.Clamp(t, 0.0f, 1.0f);
        float inverseT = 1.0f - clampedT;
        float startWeight = inverseT * inverseT * inverseT;
        float control1Weight = 3.0f * inverseT * inverseT * clampedT;
        float control2Weight = 3.0f * inverseT * clampedT * clampedT;
        float endWeight = clampedT * clampedT * clampedT;

        return new Point2Dto
        {
            X = startWeight * start.X + control1Weight * control1.X +
                control2Weight * control2.X + endWeight * end.X,
            Y = startWeight * start.Y + control1Weight * control1.Y +
                control2Weight * control2.Y + endWeight * end.Y,
        };
    }

    /// <summary>Creates a fixed-resolution rendering polyline for one cubic Bezier.</summary>
    public static Point2Dto[] SampleCubicBezier(TrajectorySegmentDto segment, int subdivisionCount)
    {
        if (segment.Start is null || segment.Control1 is null ||
            segment.Control2 is null || segment.End is null)
        {
            throw new ArgumentException("A cubic Bezier requires start, control1, control2 and end.", nameof(segment));
        }

        if (subdivisionCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(subdivisionCount));
        }

        var points = new Point2Dto[subdivisionCount + 1];
        for (int index = 0; index <= subdivisionCount; index++)
        {
            points[index] = EvaluateCubicBezier(
                segment.Start,
                segment.Control1,
                segment.Control2,
                segment.End,
                index / (float)subdivisionCount);
        }

        return points;
    }
}
