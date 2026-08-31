using System.Text.Json;
using System.Text.Json.Nodes;
using MotoGymkhanaTrainer;
using MotoGymkhanaTrainer.VenueEditor;

namespace MotoGymkhanaTrainer.Tracks;

/// <summary>Deserializes and validates the exported Track JSON contract used by the Viewer.</summary>
public static class TrackLoader
{
    private const int SupportedFormatVersion = 5;

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
            using JsonDocument parsed = JsonDocument.Parse(json);
            ValidateRequiredShape(parsed.RootElement, sourceName);
            JsonObject root = JsonNode.Parse(json)?.AsObject()
                ?? throw ContractError(sourceName, "root must be an object");
            JsonNode?[] markingNodes = root["markings"]!.AsArray()
                .Select(node => node?.DeepClone())
                .ToArray();
            root["markings"] = new JsonArray();
            TrackSnapshotDto track = JsonSerializer.Deserialize<TrackSnapshotDto>(root.ToJsonString(), SerializerOptions)
                ?? throw new InvalidDataException($"Track file '{sourceName}' contains no JSON object.");
            track.Markings = ParseViewerMarkings(markingNodes, sourceName);

            Validate(track, sourceName);
            return track;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"Track file '{sourceName}' contains invalid JSON: {exception.Message}", exception);
        }
    }

    private static MarkingDto[] ParseViewerMarkings(JsonNode?[] nodes, string sourceName)
    {
        var markings = new MarkingDto[nodes.Length];
        for (int index = 0; index < nodes.Length; index++)
        {
            JsonNode? node = nodes[index];
            string id = node is JsonObject item && item["id"] is JsonValue idValue &&
                idValue.TryGetValue(out string? parsedId)
                    ? parsedId ?? string.Empty
                    : string.Empty;
            try
            {
                markings[index] = node?.Deserialize<MarkingDto>(SerializerOptions)
                    ?? throw new JsonException("marking must be an object");
            }
            catch (JsonException exception)
            {
                markings[index] = new MarkingDto
                {
                    Id = string.IsNullOrWhiteSpace(id) ? $"invalid-marking-{index}" : id,
                    LoadError =
                        $"Track '{sourceName}' marking '{(string.IsNullOrWhiteSpace(id) ? $"index {index}" : id)}' " +
                        $"was skipped: {exception.Message}",
                };
            }
        }

        return markings;
    }

    private static void ValidateRequiredShape(JsonElement root, string sourceName)
    {
        if (root.ValueKind != JsonValueKind.Object)
            throw ContractError(sourceName, "root must be an object");
        Require(root, "formatVersion", JsonValueKind.Number, sourceName);
        Require(root, "track", JsonValueKind.Object, sourceName);
        Require(root, "venue", JsonValueKind.Object, sourceName);
        Require(root, "area", JsonValueKind.Object, sourceName);
        Require(root, "panorama", JsonValueKind.Object, sourceName);
        Require(root, "venueObjects", JsonValueKind.Array, sourceName);
        Require(root, "elements", JsonValueKind.Array, sourceName);
        Require(root, "cones", JsonValueKind.Array, sourceName);
        Require(root, "markings", JsonValueKind.Array, sourceName);
        Require(root, "trajectory", JsonValueKind.Object, sourceName);
        Require(root, "checkpoints", JsonValueKind.Array, sourceName);
    }

    private static void Require(
        JsonElement root,
        string property,
        JsonValueKind kind,
        string sourceName)
    {
        if (!root.TryGetProperty(property, out JsonElement value) || value.ValueKind != kind)
            throw ContractError(sourceName, $"{property} must be present as {kind}");
    }

    private static void Validate(TrackSnapshotDto track, string sourceName)
    {
        if (track.FormatVersion != SupportedFormatVersion)
        {
            throw new InvalidDataException(
                $"Track file '{sourceName}' uses unsupported formatVersion {track.FormatVersion}; " +
                $"expected {SupportedFormatVersion}.");
        }

        if (track.Track is null || string.IsNullOrWhiteSpace(track.Track.Id) ||
            string.IsNullOrWhiteSpace(track.Track.Name))
        {
            throw ContractError(sourceName, "track.id and track.name must be non-empty strings");
        }

        if (track.Venue is null || string.IsNullOrWhiteSpace(track.Venue.Id) ||
            string.IsNullOrWhiteSpace(track.Venue.Name))
        {
            throw ContractError(sourceName, "venue.id and venue.name must be non-empty strings");
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

        if (track.Panorama is null || !float.IsFinite(track.Panorama.RotationDeg) ||
            !float.IsFinite(track.Panorama.EnergyMultiplier) || track.Panorama.EnergyMultiplier < 0 ||
            (track.Panorama.Enabled && string.IsNullOrWhiteSpace(track.Panorama.TexturePath)))
        {
            throw ContractError(sourceName,
                "panorama rotation/energy must be finite and enabled panorama requires texturePath");
        }
        if (!string.IsNullOrWhiteSpace(track.Panorama.TexturePath) &&
            !IsCanonicalResourcePath(track.Panorama.TexturePath))
        {
            throw ContractError(sourceName, "panorama.texturePath must be a canonical res:// path");
        }

        if (track.VenueObjects is null)
        {
            throw ContractError(sourceName, "venueObjects must be an array");
        }

        for (int index = 0; index < track.VenueObjects.Length; index++)
        {
            VenueObjectSnapshotDto? item = track.VenueObjects[index];
            string prefix = $"venueObjects[{index}]";
            if (item is null || string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.Name) ||
                string.IsNullOrWhiteSpace(item.AssetPath) || !IsCanonicalResourcePath(item.AssetPath, ".tscn") ||
                !item.AssetPath.EndsWith(".tscn", StringComparison.OrdinalIgnoreCase))
                throw ContractError(sourceName, $"{prefix} identity/name/assetPath is invalid");
            ValidatePoint(item.Position, sourceName, $"{prefix}.position");
            if (!float.IsFinite(item.Elevation) || !float.IsFinite(item.RotationDeg) ||
                item.Scale is null || !Positive(item.Scale.X) || !Positive(item.Scale.Y) || !Positive(item.Scale.Z) ||
                item.Footprint is null || !Positive(item.Footprint.Width) || !Positive(item.Footprint.Length) ||
                !float.IsFinite(item.Footprint.CenterX) || !float.IsFinite(item.Footprint.CenterY))
                throw ContractError(sourceName, $"{prefix} transform/scale/footprint is invalid");
            bool imported = string.Equals(item.ObjectType, "imported", StringComparison.Ordinal);
            if (item.ObjectType is not null && !imported ||
                imported && (!VenueImportedAssetLibrary.IsValidAssetId(item.AssetId) || item.CollisionMode is not ("generated" or "none")) ||
                imported && item.CollisionEnabled != (item.CollisionMode == "generated") ||
                !imported && (item.AssetId is not null || item.CollisionMode is not null))
                throw ContractError(sourceName, $"{prefix} imported asset metadata is invalid");
            if (imported && item.AssetPath != $"res://Assets/Venue/Imported/{item.AssetId}/scene.tscn")
                throw ContractError(sourceName, $"{prefix}.assetPath is not its managed imported wrapper");
        }

        if (track.Elements is null)
            throw ContractError(sourceName, "elements must be an array");
        for (int index = 0; index < track.Elements.Length; index++)
        {
            ElementDto? item = track.Elements[index];
            if (item is null || string.IsNullOrWhiteSpace(item.InstanceId) ||
                string.IsNullOrWhiteSpace(item.DefinitionId) || !IsSafeRelativeJsonPath(item.ExercisePath))
                throw ContractError(sourceName, $"elements[{index}] identity or exercisePath is invalid");
            ValidatePoint(item.Position, sourceName, $"elements[{index}].position");
            if (!float.IsFinite(item.RotationDeg) || item.Scale is null ||
                !float.IsFinite(item.Scale.X) || !float.IsFinite(item.Scale.Y) ||
                item.Scale.X == 0 || item.Scale.Y == 0)
                throw ContractError(sourceName, $"elements[{index}] transform is invalid");
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
            if (string.IsNullOrWhiteSpace(cone.Id))
                throw ContractError(sourceName, $"cones[{index}].id must be non-empty");
        }

        if (track.Markings is null)
        {
            throw ContractError(sourceName, "markings must be an array");
        }

        var markingIds = new HashSet<string>(StringComparer.Ordinal);
        for (int markingIndex = 0; markingIndex < track.Markings.Length; markingIndex++)
        {
            MarkingDto? marking = track.Markings[markingIndex];
            if (marking is null)
            {
                continue;
            }

            if (!string.IsNullOrEmpty(marking.LoadError)) continue;
            if (string.IsNullOrWhiteSpace(marking.Id))
            {
                marking.LoadError =
                    $"Track '{sourceName}' marking 'index {markingIndex}' was skipped: id must be non-empty.";
                continue;
            }

            try
            {
                PathValidator.ValidateOrThrow(marking.Path, $"markings[{markingIndex}].path");
            }
            catch (InvalidDataException exception)
            {
                marking.LoadError =
                    $"Track '{sourceName}' marking '{marking.Id}' was skipped: {exception.Message}";
                continue;
            }

            if (!float.IsFinite(marking.WidthMeters) || marking.WidthMeters <= 0)
            {
                marking.LoadError =
                    $"Track '{sourceName}' marking '{marking.Id}' was skipped: widthMeters must be finite and positive.";
                continue;
            }

            if (!MarkingGeometry.TryNormalizeColor(marking.Color, false, out string canonical) ||
                canonical != marking.Color || !MarkingGeometry.IsSupportedStyle(marking.Style))
            {
                marking.LoadError =
                    $"Track '{sourceName}' marking '{marking.Id}' was skipped: color or style is invalid.";
                continue;
            }

            if (!markingIds.Add(marking.Id))
            {
                marking.LoadError =
                    $"Track '{sourceName}' marking '{marking.Id}' was skipped: id is duplicated.";
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

        ValidateUniqueIds(track, sourceName);
    }

    private static bool IsCanonicalResourcePath(string path, string? requiredExtension = null)
    {
        if (!path.StartsWith("res://", StringComparison.Ordinal) || path.Contains('\\') ||
            Path.IsPathRooted(path) || path[6..].Split('/').Any(part =>
                string.IsNullOrWhiteSpace(part) || part is "." or ".."))
            return false;
        return requiredExtension is null || path.EndsWith(requiredExtension, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSafeRelativeJsonPath(string path) =>
        !string.IsNullOrWhiteSpace(path) && !path.StartsWith("res://", StringComparison.Ordinal) &&
        !path.Contains('\\') && !path.Contains(':') && !Path.IsPathRooted(path) &&
        path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
        path.Split('/').All(part => !string.IsNullOrWhiteSpace(part) && part is not ("." or ".."));

    private static void ValidateUniqueIds(TrackSnapshotDto track, string sourceName)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        IEnumerable<string> ids = track.VenueObjects.Select(item => item.Id)
            .Concat(track.Elements?.Select(item => item.InstanceId) ?? [])
            .Concat(track.Cones.Select(item => item.Id))
            .Concat(track.Trajectory.Segments.Select(item => item.Id))
            .Concat(track.Checkpoints?.Select(item => item.Id) ?? []);
        foreach (string id in ids)
        {
            if (string.IsNullOrWhiteSpace(id) || !seen.Add(id))
                throw ContractError(sourceName, $"exported id '{id}' is empty or duplicated");
        }

        foreach (MarkingDto marking in track.Markings.Where(item => string.IsNullOrEmpty(item.LoadError)))
        {
            if (string.IsNullOrWhiteSpace(marking.Id) || !seen.Add(marking.Id))
            {
                marking.LoadError =
                    $"Track '{sourceName}' marking '{marking.Id}' was skipped: exported id is empty or duplicated.";
            }
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

    private static bool Positive(float value) => float.IsFinite(value) && value > 0.0f;

    private static InvalidDataException ContractError(string sourceName, string message)
    {
        return new InvalidDataException($"Track file '{sourceName}' is invalid: {message}.");
    }
}
