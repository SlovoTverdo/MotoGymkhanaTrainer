using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MotoGymkhanaTrainer.Tracks;

namespace MotoGymkhanaTrainer.ExerciseEditor;

/// <summary>A validated Exercise Definition plus non-fatal load diagnostics.</summary>
public sealed class ExerciseDefinitionLoadResult
{
    /// <summary>Creates a load result without coupling serialization to Godot logging.</summary>
    public ExerciseDefinitionLoadResult(ExerciseDefinitionDto definition, IReadOnlyList<string> warnings)
    {
        Definition = definition;
        Warnings = warnings;
    }

    /// <summary>The validated and in-memory-normalized definition.</summary>
    public ExerciseDefinitionDto Definition { get; }

    /// <summary>Non-fatal consistency problems found in the source file.</summary>
    public IReadOnlyList<string> Warnings { get; }
}

/// <summary>Serializes Exercise v2 and performs the small documented v1 migration.</summary>
public static class ExerciseDefinitionStore
{
    private const int SupportedFormatVersion = 2;
    private const int LegacyFormatVersion = 1;
    private const float EndpointToleranceMeters = 0.001f;
    private static readonly HashSet<string> SupportedColors =
        ["red", "blue", "yellow", "orange", "none"];

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Reads a UTF-8 file and returns its normalized DTO.</summary>
    public static ExerciseDefinitionDto LoadFromFile(string path)
    {
        return LoadFromFileWithDiagnostics(path).Definition;
    }

    /// <summary>Reads a UTF-8 file while preserving non-fatal validation warnings.</summary>
    public static ExerciseDefinitionLoadResult LoadFromFileWithDiagnostics(string path)
    {
        return LoadFromJsonWithDiagnostics(File.ReadAllText(path, Encoding.UTF8), path);
    }

    /// <summary>Deserializes JSON without mutating any live editor document.</summary>
    public static ExerciseDefinitionDto LoadFromJson(string json, string sourceName)
    {
        return LoadFromJsonWithDiagnostics(json, sourceName).Definition;
    }

    /// <summary>
    /// Deserializes and validates a candidate. If stored Entry/Exit disagree with
    /// the ordered trajectory, trajectory wins in memory and a warning is returned.
    /// </summary>
    public static ExerciseDefinitionLoadResult LoadFromJsonWithDiagnostics(string json, string sourceName)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidDataException($"Exercise file '{sourceName}' is empty.");
        }

        try
        {
            ExerciseDefinitionDto definition = JsonSerializer.Deserialize<ExerciseDefinitionDto>(json, ReadOptions)
                ?? throw new InvalidDataException($"Exercise file '{sourceName}' contains no JSON object.");
            int sourceVersion = definition.FormatVersion;
            var warnings = new List<string>();
            MigrateToCurrentVersion(definition, sourceVersion, sourceName, warnings);
            NormalizeCurrentMarkings(definition, sourceName, warnings);
            ValidateStructure(definition, sourceName);

            SynchronizeEndpointsFromTrajectory(definition, sourceName, warnings);
            return new ExerciseDefinitionLoadResult(definition, warnings);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"Exercise file '{sourceName}' contains invalid JSON: {exception.Message}", exception);
        }
    }

    /// <summary>Saves only domain fields as readable UTF-8 JSON without a BOM.</summary>
    public static void SaveToFile(ExerciseDefinitionDto definition, string path)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, Serialize(definition), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    /// <summary>
    /// Returns canonical indented JSON. Entry/Exit are projected from the trajectory
    /// immediately before serialization so stale independent geometry cannot escape.
    /// </summary>
    public static string Serialize(ExerciseDefinitionDto definition)
    {
        definition.FormatVersion = SupportedFormatVersion;
        ValidateStructure(definition, "in-memory document");
        SynchronizeEndpointsFromTrajectory(definition, "in-memory document", warnings: null);
        return JsonSerializer.Serialize(definition, WriteOptions);
    }

    private static void ValidateStructure(ExerciseDefinitionDto definition, string sourceName)
    {
        if (definition.FormatVersion != SupportedFormatVersion)
        {
            throw ContractError(
                sourceName,
                $"unsupported formatVersion {definition.FormatVersion}; expected {SupportedFormatVersion}");
        }

        if (definition.Exercise is null || string.IsNullOrWhiteSpace(definition.Exercise.Id) ||
            string.IsNullOrWhiteSpace(definition.Exercise.Name) || definition.Exercise.Version < 1)
        {
            throw ContractError(sourceName, "exercise id/name must be non-empty and version must be positive");
        }

        if (definition.Bounds is null || !IsPositiveFinite(definition.Bounds.Width) ||
            !IsPositiveFinite(definition.Bounds.Length))
        {
            throw ContractError(sourceName, "bounds width and length must be finite positive numbers");
        }

        if (definition.Cones is null || definition.Markings is null || definition.Checkpoints is null)
        {
            throw ContractError(sourceName, "cones, markings and checkpoints must be arrays");
        }

        var coneIds = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < definition.Cones.Length; index++)
        {
            ConeDto? cone = definition.Cones[index];
            if (cone?.Position is null || string.IsNullOrWhiteSpace(cone.Id) || !coneIds.Add(cone.Id))
            {
                throw ContractError(sourceName, $"cones[{index}] must have a unique non-empty id and position");
            }

            ValidatePoint(cone.Position, sourceName, $"cones[{index}].position");
            if (cone.Type != "standard")
            {
                throw ContractError(sourceName, $"cones[{index}].type must be 'standard'");
            }

            if (!SupportedColors.Contains(cone.Color))
            {
                throw ContractError(
                    sourceName,
                    $"cones[{index}].color must be red, blue, yellow, orange or none");
            }
        }

        var markingIds = new HashSet<string>(StringComparer.Ordinal);
        for (int markingIndex = 0; markingIndex < definition.Markings.Length; markingIndex++)
        {
            MarkingDto? marking = definition.Markings[markingIndex];
            string path = $"markings[{markingIndex}]";
            if (marking is null || string.IsNullOrWhiteSpace(marking.Id) || !markingIds.Add(marking.Id))
            {
                throw ContractError(sourceName, $"{path} must have a unique non-empty id");
            }

            int minimumPoints = marking.Type == "line" ? 2 : marking.Type == "polyline" ? 2 : int.MaxValue;
            if (minimumPoints == int.MaxValue)
            {
                throw ContractError(sourceName, $"{path}.type must be 'line' or 'polyline'");
            }

            if (marking.Points is null || marking.Points.Length < minimumPoints ||
                (marking.Type == "line" && marking.Points.Length != 2))
            {
                throw ContractError(
                    sourceName,
                    $"{path}.points must contain {(marking.Type == "line" ? "exactly" : "at least")} two points");
            }

            for (int pointIndex = 0; pointIndex < marking.Points.Length; pointIndex++)
            {
                ValidatePoint(marking.Points[pointIndex], sourceName, $"{path}.points[{pointIndex}]");
            }

            if (!MarkingGeometry.TryNormalizeColor(marking.Color, allowLegacyNames: false, out string canonical) ||
                canonical != marking.Color)
            {
                throw ContractError(sourceName, $"{path}.color must be canonical #RRGGBB");
            }

            if (!IsPositiveFinite(marking.WidthMeters))
            {
                throw ContractError(sourceName, $"{path}.widthMeters must be a finite positive number");
            }

            if (!MarkingGeometry.IsSupportedStyle(marking.Style))
            {
                throw ContractError(sourceName, $"{path}.style must be 'solid', 'dashed' or 'dotted'");
            }
        }

        ValidatePoint(definition.EntryPoint, sourceName, "entryPoint");
        ValidatePoint(definition.ExitPoint, sourceName, "exitPoint");

        TrajectorySegmentDto[]? segments = definition.Trajectory?.Segments;
        if (segments is null || segments.Length == 0)
        {
            throw ContractError(sourceName, "trajectory must contain at least one segment");
        }

        var segmentIds = new HashSet<string>(StringComparer.Ordinal);
        Point2Dto? previousEnd = null;
        for (int segmentIndex = 0; segmentIndex < segments.Length; segmentIndex++)
        {
            TrajectorySegmentDto? segment = segments[segmentIndex];
            if (segment is null || string.IsNullOrWhiteSpace(segment.Id) || !segmentIds.Add(segment.Id))
            {
                throw ContractError(
                    sourceName,
                    $"trajectory.segments[{segmentIndex}] must have a unique non-empty id");
            }

            Point2Dto start;
            Point2Dto end;
            switch (segment.Type)
            {
                case "polyline":
                    if (segment.Points is null || segment.Points.Length < 2)
                    {
                        throw ContractError(
                            sourceName,
                            $"trajectory.segments[{segmentIndex}].points must contain at least two points");
                    }

                    for (int pointIndex = 0; pointIndex < segment.Points.Length; pointIndex++)
                    {
                        ValidatePoint(
                            segment.Points[pointIndex],
                            sourceName,
                            $"trajectory.segments[{segmentIndex}].points[{pointIndex}]");
                    }

                    start = segment.Points[0];
                    end = segment.Points[^1];
                    break;

                case "cubicBezier":
                    ValidatePoint(segment.Start, sourceName, $"trajectory.segments[{segmentIndex}].start");
                    ValidatePoint(segment.Control1, sourceName, $"trajectory.segments[{segmentIndex}].control1");
                    ValidatePoint(segment.Control2, sourceName, $"trajectory.segments[{segmentIndex}].control2");
                    ValidatePoint(segment.End, sourceName, $"trajectory.segments[{segmentIndex}].end");
                    start = segment.Start!;
                    end = segment.End!;
                    break;

                default:
                    throw ContractError(
                        sourceName,
                        $"trajectory.segments[{segmentIndex}].type must be 'polyline' or 'cubicBezier'");
            }

            if (previousEnd is not null && !PointsEqual(previousEnd, start))
            {
                float gap = Distance(previousEnd, start);
                throw ContractError(
                    sourceName,
                    $"trajectory is discontinuous between segments {segmentIndex - 1} and {segmentIndex} " +
                    $"(gap {gap:F3} m)");
            }

            previousEnd = end;
        }
    }

    private static void SynchronizeEndpointsFromTrajectory(
        ExerciseDefinitionDto definition,
        string sourceName,
        ICollection<string>? warnings)
    {
        TrajectorySegmentDto first = definition.Trajectory.Segments[0];
        TrajectorySegmentDto last = definition.Trajectory.Segments[^1];
        Point2Dto trajectoryEntry = GetSegmentStart(first);
        Point2Dto trajectoryExit = GetSegmentEnd(last);

        if (!PointsEqual(definition.EntryPoint, trajectoryEntry))
        {
            warnings?.Add(
                $"Exercise file '{sourceName}' has entryPoint inconsistent with trajectory; " +
                "the first trajectory anchor is used as the in-memory source of truth.");
        }

        if (!PointsEqual(definition.ExitPoint, trajectoryExit))
        {
            warnings?.Add(
                $"Exercise file '{sourceName}' has exitPoint inconsistent with trajectory; " +
                "the last trajectory anchor is used as the in-memory source of truth.");
        }

        // This normalization changes only the candidate DTO. The source file is
        // untouched until the user explicitly saves the loaded document.
        definition.EntryPoint = CopyPoint(trajectoryEntry);
        definition.ExitPoint = CopyPoint(trajectoryExit);
    }

    private static void MigrateToCurrentVersion(
        ExerciseDefinitionDto definition,
        int sourceVersion,
        string sourceName,
        ICollection<string> warnings)
    {
        if (sourceVersion == SupportedFormatVersion)
        {
            return;
        }

        if (sourceVersion != LegacyFormatVersion)
        {
            throw ContractError(
                sourceName,
                $"unsupported formatVersion {sourceVersion}; expected {LegacyFormatVersion} or {SupportedFormatVersion}");
        }

        foreach (MarkingDto? marking in definition.Markings ?? [])
        {
            if (marking is null)
            {
                continue;
            }

            // Version 1 had no style/visibility fields. Property defaults supply
            // solid/true, and named legacy colors are canonicalized for a future v2 Save.
            marking.Style = "solid";
            marking.VisibleInViewer = true;
            if (MarkingGeometry.TryNormalizeColor(marking.Color, allowLegacyNames: true, out string canonical))
            {
                marking.Color = canonical;
            }
        }

        definition.FormatVersion = SupportedFormatVersion;
        warnings.Add(
            $"Exercise file '{sourceName}' was migrated in memory from formatVersion 1 to 2; " +
            "markings default to solid and visibleInViewer=true. The source file was not changed.");
    }

    private static void NormalizeCurrentMarkings(
        ExerciseDefinitionDto definition,
        string sourceName,
        ICollection<string> warnings)
    {
        MarkingDto[] markings = definition.Markings ?? [];
        for (int markingIndex = 0; markingIndex < markings.Length; markingIndex++)
        {
            MarkingDto? marking = markings[markingIndex];
            if (marking is null)
            {
                continue;
            }
            string path = $"markings[{markingIndex}]";

            /*
             * ExerciseFormat deliberately treats an unknown style as a local,
             * recoverable problem. The editable DTO adopts solid in memory and a
             * warning marks the document dirty; unrelated geometry remains usable.
             */
            if (!MarkingGeometry.IsSupportedStyle(marking.Style))
            {
                warnings.Add(
                    $"Exercise file '{sourceName}' has unknown {path}.style '{marking.Style}'; " +
                    "the in-memory solid fallback is used.");
                marking.Style = "solid";
            }

            if (MarkingGeometry.TryNormalizeColor(
                    marking.Color,
                    allowLegacyNames: false,
                    out string canonical) && canonical != marking.Color)
            {
                warnings.Add(
                    $"Exercise file '{sourceName}' has non-canonical {path}.color '{marking.Color}'; " +
                    $"'{canonical}' is used in memory and will be written only on explicit Save.");
                marking.Color = canonical;
            }
        }
    }

    private static void ValidatePoint(Point2Dto? point, string sourceName, string propertyPath)
    {
        if (point is null || !float.IsFinite(point.X) || !float.IsFinite(point.Y))
        {
            throw ContractError(sourceName, $"{propertyPath} must contain finite x/y coordinates");
        }
    }

    private static bool IsPositiveFinite(float value) => float.IsFinite(value) && value > 0.0f;

    private static bool PointsEqual(Point2Dto left, Point2Dto right)
    {
        return MathF.Abs(left.X - right.X) <= EndpointToleranceMeters &&
            MathF.Abs(left.Y - right.Y) <= EndpointToleranceMeters;
    }

    private static float Distance(Point2Dto left, Point2Dto right)
    {
        float deltaX = left.X - right.X;
        float deltaY = left.Y - right.Y;
        return MathF.Sqrt(deltaX * deltaX + deltaY * deltaY);
    }

    private static Point2Dto GetSegmentStart(TrajectorySegmentDto segment)
    {
        return segment.Type == "polyline" ? segment.Points![0] : segment.Start!;
    }

    private static Point2Dto GetSegmentEnd(TrajectorySegmentDto segment)
    {
        return segment.Type == "polyline" ? segment.Points![^1] : segment.End!;
    }

    private static Point2Dto CopyPoint(Point2Dto point)
    {
        return new Point2Dto { X = point.X, Y = point.Y };
    }

    private static InvalidDataException ContractError(string sourceName, string message)
    {
        return new InvalidDataException($"Exercise file '{sourceName}' is invalid: {message}.");
    }
}
