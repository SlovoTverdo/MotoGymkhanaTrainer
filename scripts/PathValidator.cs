using MotoGymkhanaTrainer.Tracks;

namespace MotoGymkhanaTrainer;

/// <summary>Central structural and usable-geometry validation for marking Paths.</summary>
public static class PathValidator
{
    /// <summary>Returns path-specific errors prefixed with the caller's document path.</summary>
    public static IReadOnlyList<string> Validate(PathDefinition? path, string propertyPath)
    {
        var errors = new List<string>();
        if (path is null)
        {
            errors.Add($"{propertyPath} must be an object");
            return errors;
        }

        ValidatePoint(path.Start, $"{propertyPath}.start", errors);
        if (path.Segments is null || path.Segments.Length == 0)
        {
            errors.Add($"{propertyPath}.segments must contain at least one segment");
            return errors;
        }

        for (int index = 0; index < path.Segments.Length; index++)
        {
            string segmentPath = $"{propertyPath}.segments[{index}]";
            switch (path.Segments[index])
            {
                case LinePathSegmentDefinition line:
                    ValidatePoint(line.End, $"{segmentPath}.end", errors);
                    break;
                case CubicBezierPathSegmentDefinition cubic:
                    ValidatePoint(cubic.Control1, $"{segmentPath}.control1", errors);
                    ValidatePoint(cubic.Control2, $"{segmentPath}.control2", errors);
                    ValidatePoint(cubic.End, $"{segmentPath}.end", errors);
                    break;
                case null:
                    errors.Add($"{segmentPath} must be an object");
                    break;
                default:
                    errors.Add($"{segmentPath}.type must be 'line' or 'cubicBezier'");
                    break;
            }
        }

        if (errors.Count == 0)
        {
            for (int index = 0; index < path.Segments.Length; index++)
            {
                var single = new PathDefinition
                {
                    Start = index == 0 ? path.Start : path.Segments[index - 1].EndPoint,
                    Segments = [path.Segments[index]],
                };
                if (PathSampler.Sample(single).TotalLength <= PathSamplingSettings.DuplicatePointToleranceMeters)
                    errors.Add($"{propertyPath}.segments[{index}] must have non-zero usable length");
            }
        }

        if (errors.Count == 0)
        {
            SampledPath sampled = PathSampler.Sample(path);
            if (sampled.Points.Length < 2 || !float.IsFinite(sampled.TotalLength) ||
                sampled.TotalLength <= PathSamplingSettings.DuplicatePointToleranceMeters)
            {
                errors.Add($"{propertyPath} must produce at least two distinct finite sampled points");
            }
        }

        return errors;
    }

    /// <summary>Throws one path-rich error when a caller cannot continue with invalid geometry.</summary>
    public static void ValidateOrThrow(PathDefinition? path, string propertyPath)
    {
        IReadOnlyList<string> errors = Validate(path, propertyPath);
        if (errors.Count > 0) throw new InvalidDataException(string.Join("; ", errors));
    }

    private static void ValidatePoint(Point2Dto? point, string path, ICollection<string> errors)
    {
        if (point is null || !float.IsFinite(point.X) || !float.IsFinite(point.Y))
        {
            errors.Add($"{path} must contain finite x/y coordinates");
        }
    }
}
