using MotoGymkhanaTrainer.Tracks;

namespace MotoGymkhanaTrainer;

/// <summary>
/// Provides editor-safe structural operations for line/cubic marking paths.
/// Every operation preserves the implicit-start contract: only <see cref="PathDefinition.Start"/>
/// and segment endpoints own join coordinates.
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

    /// <summary>Returns the implicit start of the requested segment.</summary>
    public static Point2Dto GetSegmentStart(PathDefinition path, int segmentIndex)
    {
        ArgumentNullException.ThrowIfNull(path);
        if ((uint)segmentIndex >= (uint)path.Segments.Length)
            throw new ArgumentOutOfRangeException(nameof(segmentIndex));
        return segmentIndex == 0 ? path.Start : path.Segments[segmentIndex - 1].EndPoint;
    }

    /// <summary>Appends a non-zero straight segment and returns its index, or -1.</summary>
    public static int AppendLine(PathDefinition path, Point2Dto end)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!IsFinite(end) || PointsEqual(GetPathEnd(path), end)) return -1;
        int index = path.Segments.Length;
        path.Segments = [.. path.Segments, new LinePathSegmentDefinition { End = Copy(end) }];
        return index;
    }

    /// <summary>
    /// Appends an initially straight cubic. Controls at one and two thirds make its
    /// Bézier polynomial exactly equal to the endpoint chord.
    /// </summary>
    public static int AppendCubic(PathDefinition path, Point2Dto end)
    {
        ArgumentNullException.ThrowIfNull(path);
        Point2Dto start = GetPathEnd(path);
        if (!IsFinite(end) || PointsEqual(start, end)) return -1;
        int index = path.Segments.Length;
        path.Segments =
        [
            .. path.Segments,
            new CubicBezierPathSegmentDefinition
            {
                Control1 = Lerp(start, end, 1.0f / 3.0f),
                Control2 = Lerp(start, end, 2.0f / 3.0f),
                End = Copy(end),
            },
        ];
        return index;
    }

    /// <summary>Moves one persisted path coordinate without applying tangent rules.</summary>
    public static bool MoveCoordinate(
        PathDefinition path,
        int segmentIndex,
        MarkingPathCoordinateKind kind,
        Point2Dto point)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!IsFinite(point)) return false;
        if (kind == MarkingPathCoordinateKind.PathStart)
        {
            path.Start = Copy(point);
            return true;
        }

        if ((uint)segmentIndex >= (uint)path.Segments.Length) return false;
        PathSegmentDefinition segment = path.Segments[segmentIndex];
        switch (kind)
        {
            case MarkingPathCoordinateKind.SegmentEnd when segment is LinePathSegmentDefinition line:
                line.End = Copy(point);
                return true;
            case MarkingPathCoordinateKind.SegmentEnd when segment is CubicBezierPathSegmentDefinition cubic:
                cubic.End = Copy(point);
                return true;
            case MarkingPathCoordinateKind.Control1 when segment is CubicBezierPathSegmentDefinition cubic:
                cubic.Control1 = Copy(point);
                return true;
            case MarkingPathCoordinateKind.Control2 when segment is CubicBezierPathSegmentDefinition cubic:
                cubic.Control2 = Copy(point);
                return true;
            default:
                return false;
        }
    }

    /// <summary>Translates all path coordinates while preserving width and segment types.</summary>
    public static PathDefinition Translate(PathDefinition path, float deltaX, float deltaY) =>
        PathTransformService.Transform(path, point => new Point2Dto
        {
            X = point.X + deltaX,
            Y = point.Y + deltaY,
        });

    /// <summary>Converts a line to an exactly collinear cubic.</summary>
    public static bool ConvertLineToCubic(PathDefinition path, int segmentIndex)
    {
        if ((uint)segmentIndex >= (uint)path.Segments.Length ||
            path.Segments[segmentIndex] is not LinePathSegmentDefinition line)
            return false;
        Point2Dto start = GetSegmentStart(path, segmentIndex);
        path.Segments[segmentIndex] = new CubicBezierPathSegmentDefinition
        {
            Control1 = Lerp(start, line.End, 1.0f / 3.0f),
            Control2 = Lerp(start, line.End, 2.0f / 3.0f),
            End = Copy(line.End),
        };
        return true;
    }

    /// <summary>Converts a cubic to a line while retaining its endpoint.</summary>
    public static bool ConvertCubicToLine(PathDefinition path, int segmentIndex)
    {
        if ((uint)segmentIndex >= (uint)path.Segments.Length ||
            path.Segments[segmentIndex] is not CubicBezierPathSegmentDefinition cubic)
            return false;
        path.Segments[segmentIndex] = new LinePathSegmentDefinition { End = Copy(cubic.End) };
        return true;
    }

    /// <summary>Splits a segment at an interior parameter while preserving its exact shape.</summary>
    public static bool SplitSegment(PathDefinition path, int segmentIndex, float parameter)
    {
        if ((uint)segmentIndex >= (uint)path.Segments.Length || !float.IsFinite(parameter)) return false;
        float t = Math.Clamp(parameter, 0.0001f, 0.9999f);
        Point2Dto start = GetSegmentStart(path, segmentIndex);
        PathSegmentDefinition[] replacements;
        switch (path.Segments[segmentIndex])
        {
            case LinePathSegmentDefinition line:
                Point2Dto middle = Lerp(start, line.End, t);
                replacements =
                [
                    new LinePathSegmentDefinition { End = middle },
                    new LinePathSegmentDefinition { End = Copy(line.End) },
                ];
                break;
            case CubicBezierPathSegmentDefinition cubic:
                // De Casteljau subdivision shares Q0 as the new implicit join and
                // therefore preserves the original cubic exactly on both halves.
                Point2Dto a = Lerp(start, cubic.Control1, t);
                Point2Dto b = Lerp(cubic.Control1, cubic.Control2, t);
                Point2Dto c = Lerp(cubic.Control2, cubic.End, t);
                Point2Dto d = Lerp(a, b, t);
                Point2Dto e = Lerp(b, c, t);
                Point2Dto q = Lerp(d, e, t);
                replacements =
                [
                    new CubicBezierPathSegmentDefinition { Control1 = a, Control2 = d, End = q },
                    new CubicBezierPathSegmentDefinition { Control1 = e, Control2 = c, End = Copy(cubic.End) },
                ];
                break;
            default:
                return false;
        }

        path.Segments =
        [
            .. path.Segments.Take(segmentIndex),
            .. replacements,
            .. path.Segments.Skip(segmentIndex + 1),
        ];
        return true;
    }

    /// <summary>
    /// Removes one segment. Removing the first segment advances Path.start to its
    /// endpoint so the remaining geometry is not silently reconnected from the old start.
    /// </summary>
    public static bool DeleteSegment(PathDefinition path, int segmentIndex)
    {
        if ((uint)segmentIndex >= (uint)path.Segments.Length) return false;
        if (segmentIndex == 0 && path.Segments.Length > 1)
            path.Start = Copy(path.Segments[0].EndPoint);
        path.Segments = [.. path.Segments.Take(segmentIndex), .. path.Segments.Skip(segmentIndex + 1)];
        return true;
    }

    /// <summary>Evaluates one segment at a normalized parameter.</summary>
    public static Point2Dto Evaluate(PathDefinition path, int segmentIndex, float parameter)
    {
        Point2Dto start = GetSegmentStart(path, segmentIndex);
        float t = Math.Clamp(parameter, 0.0f, 1.0f);
        return path.Segments[segmentIndex] switch
        {
            LinePathSegmentDefinition line => Lerp(start, line.End, t),
            CubicBezierPathSegmentDefinition cubic => PathSampler.EvaluateCubic(
                start, cubic.Control1, cubic.Control2, cubic.End, t),
            _ => throw new InvalidDataException("Unsupported path segment."),
        };
    }

    /// <summary>Returns the current path endpoint, including a transient segment-less path.</summary>
    public static Point2Dto GetPathEnd(PathDefinition path) =>
        path.Segments.Length == 0 ? path.Start : path.Segments[^1].EndPoint;

    public static bool PointsEqual(Point2Dto left, Point2Dto right, float tolerance = 0.000001f) =>
        MathF.Abs(left.X - right.X) <= tolerance && MathF.Abs(left.Y - right.Y) <= tolerance;

    public static bool IsFinite(Point2Dto point) => float.IsFinite(point.X) && float.IsFinite(point.Y);

    private static Point2Dto Lerp(Point2Dto start, Point2Dto end, float weight) => new()
    {
        X = start.X + (end.X - start.X) * weight,
        Y = start.Y + (end.Y - start.Y) * weight,
    };

    private static Point2Dto Copy(Point2Dto point) => new() { X = point.X, Y = point.Y };
}

/// <summary>Address of one persisted coordinate in a marking Path.</summary>
public enum MarkingPathCoordinateKind
{
    PathStart,
    SegmentEnd,
    Control1,
    Control2,
}
