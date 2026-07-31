using MotoGymkhanaTrainer.Tracks;

namespace MotoGymkhanaTrainer;

/// <summary>
/// Bridges the existing vertex-based line/polyline tools to the new Path contract.
/// Cubic control geometry is intentionally read-only in Curved Markings Iteration 1.
/// </summary>
public static class PathEditing
{
    public static PathDefinition FromPolyline(IReadOnlyList<Point2Dto> points)
    {
        if (points.Count < 2) throw new ArgumentException("A marking requires at least two points.", nameof(points));
        return new PathDefinition
        {
            Start = Copy(points[0]),
            Segments = points.Skip(1).Select(point => (PathSegmentDefinition)new LinePathSegmentDefinition
            {
                End = Copy(point),
            }).ToArray(),
        };
    }

    public static bool IsAllLine(PathDefinition? path) =>
        path?.Segments is { Length: > 0 } && path.Segments.All(segment => segment is LinePathSegmentDefinition);

    public static Point2Dto[] GetVertices(PathDefinition path)
    {
        var points = new List<Point2Dto> { path.Start };
        points.AddRange(path.Segments.Select(segment => segment.EndPoint));
        return [.. points];
    }

    public static bool TryMoveVertex(PathDefinition path, int index, Point2Dto point)
    {
        if (!IsAllLine(path) || index < 0 || index > path.Segments.Length) return false;
        if (index == 0) path.Start = Copy(point);
        else ((LinePathSegmentDefinition)path.Segments[index - 1]).End = Copy(point);
        return true;
    }

    public static int AppendVertex(PathDefinition path, Point2Dto point)
    {
        if (!IsAllLine(path)) return -1;
        path.Segments = [.. path.Segments, new LinePathSegmentDefinition { End = Copy(point) }];
        return path.Segments.Length;
    }

    public static int InsertVertexAfter(PathDefinition path, int index)
    {
        if (!IsAllLine(path) || index < 0 || index >= path.Segments.Length) return -1;
        Point2Dto left = index == 0 ? path.Start : path.Segments[index - 1].EndPoint;
        Point2Dto right = path.Segments[index].EndPoint;
        var inserted = new LinePathSegmentDefinition
        {
            End = new Point2Dto { X = (left.X + right.X) * 0.5f, Y = (left.Y + right.Y) * 0.5f },
        };
        path.Segments = [.. path.Segments.Take(index), inserted, .. path.Segments.Skip(index)];
        return index + 1;
    }

    public static bool DeleteInternalVertex(PathDefinition path, int index)
    {
        if (!IsAllLine(path) || index <= 0 || index >= path.Segments.Length) return false;
        path.Segments = [.. path.Segments.Take(index - 1), .. path.Segments.Skip(index)];
        return true;
    }

    public static PathDefinition CopyPath(PathDefinition source) =>
        PathTransformService.Transform(source, Copy);

    private static Point2Dto Copy(Point2Dto point) => new() { X = point.X, Y = point.Y };
}
