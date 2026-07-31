using MotoGymkhanaTrainer.Tracks;

namespace MotoGymkhanaTrainer;

/// <summary>Transforms Path control geometry without converting curves to samples.</summary>
public static class PathTransformService
{
    /// <summary>Returns a deep transformed copy while preserving segment types.</summary>
    public static PathDefinition Transform(PathDefinition source, Func<Point2Dto, Point2Dto> transform)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(transform);
        return new PathDefinition
        {
            Start = transform(source.Start),
            Segments = source.Segments.Select<PathSegmentDefinition, PathSegmentDefinition>(segment => segment switch
            {
                LinePathSegmentDefinition line => new LinePathSegmentDefinition
                {
                    End = transform(line.End),
                },
                CubicBezierPathSegmentDefinition cubic => new CubicBezierPathSegmentDefinition
                {
                    Control1 = transform(cubic.Control1),
                    Control2 = transform(cubic.Control2),
                    End = transform(cubic.End),
                },
                _ => throw new InvalidDataException(
                    $"Unsupported Path segment CLR type '{segment?.GetType().Name ?? "<null>"}'."),
            }).ToArray(),
        };
    }
}
