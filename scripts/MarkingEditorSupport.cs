using Godot;
using MotoGymkhanaTrainer.Tracks;

namespace MotoGymkhanaTrainer;

/// <summary>One editor-only handle projected from persisted Path geometry.</summary>
public readonly record struct MarkingHandleLocation(
    int SegmentIndex,
    MarkingHandleKind Kind,
    Point2Dto Point);

/// <summary>
/// Shared hit-testing for Exercise and Venue marking editors. It deliberately tests
/// the persisted centerline, so dashed and dotted gaps remain selectable.
/// </summary>
public static class MarkingPathHitTester
{
    /// <summary>Enumerates the single Path start and every persisted segment coordinate.</summary>
    public static IEnumerable<MarkingHandleLocation> EnumerateHandles(PathDefinition path)
    {
        yield return new MarkingHandleLocation(-1, MarkingHandleKind.PathStart, path.Start);
        for (int index = 0; index < path.Segments.Length; index++)
        {
            PathSegmentDefinition segment = path.Segments[index];
            yield return new MarkingHandleLocation(index, MarkingHandleKind.SegmentEnd, segment.EndPoint);
            if (segment is CubicBezierPathSegmentDefinition cubic)
            {
                yield return new MarkingHandleLocation(index, MarkingHandleKind.Control1, cubic.Control1);
                yield return new MarkingHandleLocation(index, MarkingHandleKind.Control2, cubic.Control2);
            }
        }
    }

    /// <summary>Returns the nearest segment and refined parameter inside a pixel tolerance.</summary>
    public static bool TryHitCenterline(
        PathDefinition path,
        Vector2 screenPoint,
        Func<Point2Dto, Vector2> toScreen,
        float tolerancePixels,
        out int segmentIndex,
        out float parameter,
        out float distance)
    {
        segmentIndex = -1;
        parameter = 0.5f;
        distance = float.MaxValue;
        for (int index = 0; index < path.Segments.Length; index++)
        {
            float candidate = FindNearestParameter(path, index, screenPoint, toScreen, out float candidateDistance);
            if (candidateDistance > tolerancePixels || candidateDistance >= distance) continue;
            segmentIndex = index;
            parameter = candidate;
            distance = candidateDistance;
        }
        return segmentIndex >= 0;
    }

    private static float FindNearestParameter(
        PathDefinition path,
        int segmentIndex,
        Vector2 screenPoint,
        Func<Point2Dto, Vector2> toScreen,
        out float distance)
    {
        const int coarseSteps = 32;
        float bestT = 0;
        distance = float.MaxValue;
        for (int step = 0; step <= coarseSteps; step++)
        {
            float t = step / (float)coarseSteps;
            float candidate = screenPoint.DistanceTo(toScreen(PathEditing.Evaluate(path, segmentIndex, t)));
            if (candidate < distance) { distance = candidate; bestT = t; }
        }

        float radius = 1.0f / coarseSteps;
        float left = MathF.Max(0, bestT - radius);
        float right = MathF.Min(1, bestT + radius);
        for (int iteration = 0; iteration < 10; iteration++)
        {
            float a = left + (right - left) / 3;
            float b = right - (right - left) / 3;
            float da = screenPoint.DistanceTo(toScreen(PathEditing.Evaluate(path, segmentIndex, a)));
            float db = screenPoint.DistanceTo(toScreen(PathEditing.Evaluate(path, segmentIndex, b)));
            if (da <= db) right = b; else left = a;
        }

        bestT = (left + right) * 0.5f;
        distance = screenPoint.DistanceTo(toScreen(PathEditing.Evaluate(path, segmentIndex, bestT)));
        return Math.Clamp(bestT, 0.0001f, 0.9999f);
    }
}

/// <summary>Shared screen-space marking handle overlay used by both desktop editors.</summary>
public static class MarkingHandleOverlay
{
    /// <summary>Draws control arms and shape-distinct, zoom-independent handles.</summary>
    public static void Draw(
        Control canvas,
        MarkingDto marking,
        MarkingSelection selection,
        Color markingColor,
        Func<Point2Dto, Vector2> toScreen)
    {
        int selectedSegment = selection.SegmentIndex;
        if ((uint)selectedSegment < (uint)marking.Path.Segments.Length &&
            marking.Path.Segments[selectedSegment] is CubicBezierPathSegmentDefinition cubic)
        {
            canvas.DrawLine(toScreen(PathEditing.GetSegmentStart(marking.Path, selectedSegment)),
                toScreen(cubic.Control1), new Color(0.72f, 0.75f, 0.82f), 1.5f);
            canvas.DrawLine(toScreen(cubic.End), toScreen(cubic.Control2),
                new Color(0.72f, 0.75f, 0.82f), 1.5f);
        }

        foreach (MarkingHandleLocation handle in MarkingPathHitTester.EnumerateHandles(marking.Path))
        {
            Vector2 center = toScreen(handle.Point);
            bool active = selection.HasHandle && selection.SegmentIndex == handle.SegmentIndex &&
                selection.HandleKind == handle.Kind;
            Color fill = active ? Colors.White : handle.Kind is MarkingHandleKind.Control1 or MarkingHandleKind.Control2
                ? new Color(1.0f, 0.55f, 0.16f) : markingColor;
            if (handle.Kind == MarkingHandleKind.PathStart)
            {
                canvas.DrawRect(new Rect2(center - new Vector2(6, 6), new Vector2(12, 12)), fill, true);
                canvas.DrawRect(new Rect2(center - new Vector2(6, 6), new Vector2(12, 12)), Colors.Black, false, 2);
            }
            else if (handle.Kind is MarkingHandleKind.Control1 or MarkingHandleKind.Control2)
            {
                Vector2[] diamond = [center + Vector2.Up * 7, center + Vector2.Right * 7,
                    center + Vector2.Down * 7, center + Vector2.Left * 7];
                canvas.DrawColoredPolygon(diamond, fill);
                canvas.DrawPolyline([.. diamond, diamond[0]], Colors.Black, 2);
            }
            else
            {
                canvas.DrawCircle(center, active ? 8 : 6, fill);
                canvas.DrawArc(center, active ? 8 : 6, 0, Mathf.Tau, 20, Colors.Black, 2);
            }
        }
    }
}
