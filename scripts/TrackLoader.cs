using System.Text.Json;

namespace MotoGymkhanaTrainer.Tracks;

/// <summary>Deserializes and validates the exported Track JSON contract used by the Viewer.</summary>
public static class TrackLoader
{
    private const int SupportedFormatVersion = 2;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>Loads a track snapshot from JSON text and reports its source in failures.</summary>
    /// <exception cref="InvalidDataException">The JSON is malformed or violates the supported contract.</exception>
    public static TrackSnapshotDto LoadFromJson(string json, string sourceName)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidDataException($"Track file '{sourceName}' is empty.");
        }

        try
        {
            TrackSnapshotDto track = JsonSerializer.Deserialize<TrackSnapshotDto>(json, SerializerOptions)
                ?? throw new InvalidDataException($"Track file '{sourceName}' contains no JSON object.");

            Validate(track, sourceName);
            return track;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"Track file '{sourceName}' contains invalid JSON: {exception.Message}", exception);
        }
    }

    private static void Validate(TrackSnapshotDto track, string sourceName)
    {
        if (track.FormatVersion != SupportedFormatVersion)
        {
            throw new InvalidDataException(
                $"Track file '{sourceName}' uses unsupported formatVersion {track.FormatVersion}; " +
                $"expected {SupportedFormatVersion}.");
        }

        if (track.Track is null)
        {
            throw ContractError(sourceName, "track must be an object");
        }

        if (track.Area is null)
        {
            throw ContractError(sourceName, "area must be an object");
        }

        if (!float.IsFinite(track.Area.Width) || !float.IsFinite(track.Area.Length) ||
            track.Area.Width <= 0 || track.Area.Length <= 0)
        {
            throw ContractError(sourceName, "area width and length must be finite positive numbers");
        }

        if (track.Cones is null)
        {
            throw ContractError(sourceName, "cones must be an array");
        }

        for (int index = 0; index < track.Cones.Length; index++)
        {
            ConeDto? cone = track.Cones[index];
            if (cone?.Position is null)
            {
                throw ContractError(sourceName, $"cones[{index}].position must be an object");
            }

            ValidatePoint(cone.Position, sourceName, $"cones[{index}].position");
        }

        if (track.Markings is null)
        {
            throw ContractError(sourceName, "markings must be an array");
        }

        for (int markingIndex = 0; markingIndex < track.Markings.Length; markingIndex++)
        {
            MarkingDto? marking = track.Markings[markingIndex];
            if (marking is null)
            {
                throw ContractError(sourceName, $"markings[{markingIndex}] must be an object");
            }

            if (marking.Points is null || marking.Points.Length < 2)
            {
                throw ContractError(sourceName, $"markings[{markingIndex}].points must contain at least two points");
            }

            if (!float.IsFinite(marking.WidthMeters) || marking.WidthMeters <= 0)
            {
                throw ContractError(sourceName, $"markings[{markingIndex}].widthMeters must be a finite positive number");
            }

            for (int pointIndex = 0; pointIndex < marking.Points.Length; pointIndex++)
            {
                Point2Dto? point = marking.Points[pointIndex];
                if (point is null)
                {
                    throw ContractError(
                        sourceName,
                        $"markings[{markingIndex}].points[{pointIndex}] must be an object");
                }

                ValidatePoint(
                    point,
                    sourceName,
                    $"markings[{markingIndex}].points[{pointIndex}]");
            }
        }

        if (track.Trajectory?.Segments is null)
        {
            throw ContractError(sourceName, "trajectory must be an object containing a segments array");
        }

        for (int index = 0; index < track.Trajectory.Segments.Length; index++)
        {
            TrajectorySegmentDto? segment = track.Trajectory.Segments[index];
            if (segment is null)
            {
                throw ContractError(sourceName, $"trajectory.segments[{index}] must be an object");
            }

            ValidateTrajectorySegment(segment, index, sourceName);
        }
    }

    private static void ValidateTrajectorySegment(
        TrajectorySegmentDto segment,
        int segmentIndex,
        string sourceName)
    {
        string propertyPath = $"trajectory.segments[{segmentIndex}]";

        if (string.IsNullOrWhiteSpace(segment.Id))
        {
            throw ContractError(sourceName, $"{propertyPath}.id must be a non-empty string");
        }

        if (string.IsNullOrWhiteSpace(segment.Type))
        {
            throw ContractError(sourceName, $"{propertyPath}.type must be a non-empty string");
        }

        switch (segment.Type)
        {
            case "polyline":
                if (segment.Points is null || segment.Points.Length < 2)
                {
                    throw ContractError(sourceName, $"{propertyPath}.points must contain at least two points");
                }

                for (int pointIndex = 0; pointIndex < segment.Points.Length; pointIndex++)
                {
                    Point2Dto? point = segment.Points[pointIndex];
                    if (point is null)
                    {
                        throw ContractError(sourceName, $"{propertyPath}.points[{pointIndex}] must be an object");
                    }

                    ValidatePoint(point, sourceName, $"{propertyPath}.points[{pointIndex}]");
                }

                break;

            case "cubicBezier":
                ValidateRequiredPoint(segment.Start, sourceName, $"{propertyPath}.start");
                ValidateRequiredPoint(segment.Control1, sourceName, $"{propertyPath}.control1");
                ValidateRequiredPoint(segment.Control2, sourceName, $"{propertyPath}.control2");
                ValidateRequiredPoint(segment.End, sourceName, $"{propertyPath}.end");
                break;

            default:
                // Future segment types remain loadable. The Viewer logs and skips them at render time.
                break;
        }
    }

    private static void ValidateRequiredPoint(
        Point2Dto? point,
        string sourceName,
        string propertyPath)
    {
        if (point is null)
        {
            throw ContractError(sourceName, $"{propertyPath} must be an object");
        }

        ValidatePoint(point, sourceName, propertyPath);
    }

    private static void ValidatePoint(Point2Dto point, string sourceName, string propertyPath)
    {
        if (!float.IsFinite(point.X) || !float.IsFinite(point.Y))
        {
            throw ContractError(sourceName, $"{propertyPath} coordinates must be finite numbers");
        }
    }

    private static InvalidDataException ContractError(string sourceName, string message)
    {
        return new InvalidDataException($"Track file '{sourceName}' is invalid: {message}.");
    }
}
