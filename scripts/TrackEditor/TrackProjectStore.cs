using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MotoGymkhanaTrainer.ExerciseEditor;
using MotoGymkhanaTrainer.Tracks;

namespace MotoGymkhanaTrainer.TrackEditor;

/// <summary>Validated project plus resolved read-only Exercise dependencies.</summary>
public sealed class TrackProjectLoadResult
{
    public TrackProjectLoadResult(
        TrackProjectDto project,
        IReadOnlyDictionary<string, ExerciseDefinitionDto> definitions,
        IReadOnlyList<string> warnings)
    {
        Project = project;
        Definitions = definitions;
        Warnings = warnings;
    }

    public TrackProjectDto Project { get; }

    /// <summary>Definitions keyed by instanceId; absent keys are unresolved instances.</summary>
    public IReadOnlyDictionary<string, ExerciseDefinitionDto> Definitions { get; }

    public IReadOnlyList<string> Warnings { get; }
}

/// <summary>UTF-8 serialization, v1 migration and validation for Track Project v2.</summary>
public static class TrackProjectStore
{
    private const int SupportedFormatVersion = 2;
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

    /// <summary>
    /// Loads and validates the project before resolving references. Missing or bad
    /// Exercise files produce unresolved entries rather than invalidating the root.
    /// </summary>
    public static TrackProjectLoadResult LoadFromFile(string path, SandboxedJsonLibrary exerciseLibrary)
    {
        return LoadFromJson(File.ReadAllText(path, Encoding.UTF8), path, exerciseLibrary);
    }

    /// <summary>Deserializes a candidate without mutating a live Track document.</summary>
    public static TrackProjectLoadResult LoadFromJson(
        string json,
        string sourceName,
        SandboxedJsonLibrary exerciseLibrary)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidDataException($"Track Project '{sourceName}' is empty.");
        }

        try
        {
            using JsonDocument parsed = JsonDocument.Parse(json);
            if (parsed.RootElement.ValueKind == JsonValueKind.Object &&
                parsed.RootElement.TryGetProperty("formatVersion", out JsonElement versionElement) &&
                versionElement.TryGetInt32(out int sourceVersion) && sourceVersion == SupportedFormatVersion &&
                !parsed.RootElement.TryGetProperty("transitionOverrides", out _))
            {
                throw ContractError(sourceName,
                    "transitionOverrides is mandatory for formatVersion 2");
            }

            TrackProjectDto project = JsonSerializer.Deserialize<TrackProjectDto>(json, ReadOptions)
                ?? throw new InvalidDataException($"Track Project '{sourceName}' contains no JSON object.");
            var migrationWarnings = new List<string>();
            MigrateInMemory(project, sourceName, migrationWarnings);
            Validate(project, sourceName, exerciseLibrary);
            return ResolveDefinitions(project, exerciseLibrary, migrationWarnings);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"Track Project '{sourceName}' contains invalid JSON: {exception.Message}", exception);
        }
    }

    /// <summary>Saves domain data only as readable UTF-8 without a BOM.</summary>
    public static void SaveToFile(TrackProjectDto project, string path, SandboxedJsonLibrary exerciseLibrary)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, Serialize(project, exerciseLibrary), new UTF8Encoding(false));
    }

    /// <summary>Serializes no cached definitions, transformed geometry or UI state.</summary>
    public static string Serialize(TrackProjectDto project, SandboxedJsonLibrary exerciseLibrary)
    {
        project.FormatVersion = SupportedFormatVersion;
        Validate(project, "in-memory Track Project", exerciseLibrary);
        return JsonSerializer.Serialize(project, WriteOptions);
    }

    /// <summary>
    /// Captures domain state for Undo without requiring the temporarily edited
    /// metadata to pass file-save validation. DTO serialization inherently omits
    /// resolved definitions and all editor UI state.
    /// </summary>
    public static string SerializeHistorySnapshot(TrackProjectDto project) =>
        JsonSerializer.Serialize(project, WriteOptions);

    /// <summary>Restores a trusted in-process history snapshot and resolves dependencies anew.</summary>
    public static TrackProjectLoadResult RestoreHistorySnapshot(
        string snapshot,
        SandboxedJsonLibrary exerciseLibrary)
    {
        TrackProjectDto project = JsonSerializer.Deserialize<TrackProjectDto>(snapshot, ReadOptions)
            ?? throw new InvalidDataException("Undo history snapshot contains no Track Project object.");
        if (project.FormatVersion != SupportedFormatVersion || project.Instances is null ||
            project.TransitionOverrides is null)
        {
            throw new InvalidDataException("Undo history snapshot has an incompatible Track Project shape.");
        }

        return ResolveDefinitions(project, exerciseLibrary);
    }

    private static void Validate(
        TrackProjectDto project,
        string sourceName,
        SandboxedJsonLibrary exerciseLibrary)
    {
        if (project.FormatVersion != SupportedFormatVersion)
        {
            throw ContractError(sourceName,
                $"unsupported formatVersion {project.FormatVersion}; expected {SupportedFormatVersion}");
        }

        if (project.Track is null || string.IsNullOrWhiteSpace(project.Track.Id) ||
            string.IsNullOrWhiteSpace(project.Track.Name))
        {
            throw ContractError(sourceName, "track.id and track.name must be non-empty");
        }

        if (project.Area is null || !IsPositiveFinite(project.Area.Width) ||
            !IsPositiveFinite(project.Area.Length))
        {
            throw ContractError(sourceName, "area width and length must be finite positive numbers");
        }

        if (project.Instances is null)
        {
            throw ContractError(sourceName, "instances must be an array");
        }

        if (project.TransitionOverrides is null)
        {
            throw ContractError(sourceName, "transitionOverrides must be an array");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < project.Instances.Length; index++)
        {
            TrackProjectInstanceDto? instance = project.Instances[index];
            string prefix = $"instances[{index}]";
            if (instance is null || string.IsNullOrWhiteSpace(instance.InstanceId) ||
                !ids.Add(instance.InstanceId))
            {
                throw ContractError(sourceName, $"{prefix}.instanceId must be unique and non-empty");
            }

            if (string.IsNullOrWhiteSpace(instance.ExercisePath) ||
                Path.IsPathRooted(instance.ExercisePath) ||
                instance.ExercisePath.StartsWith("res://", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Path.GetExtension(instance.ExercisePath), ".json", StringComparison.OrdinalIgnoreCase))
            {
                throw ContractError(sourceName,
                    $"{prefix}.exercisePath must be a JSON path relative to res://exercises/");
            }

            // Resolution is also the path sandbox validation. File existence is
            // intentionally not required here because unresolved instances persist.
            try
            {
                exerciseLibrary.ResolveUserPath(instance.ExercisePath);
            }
            catch (Exception exception) when (exception is InvalidDataException or IOException)
            {
                throw ContractError(sourceName, $"{prefix}.exercisePath is unsafe: {exception.Message}");
            }

            ValidatePoint(instance.Position, sourceName, $"{prefix}.position");
            ValidatePoint(instance.Scale, sourceName, $"{prefix}.scale");
            if (!float.IsFinite(instance.RotationDeg))
            {
                throw ContractError(sourceName, $"{prefix}.rotationDeg must be finite");
            }

            if (MathF.Abs(instance.Scale.X) < 0.0001f || MathF.Abs(instance.Scale.Y) < 0.0001f)
            {
                throw ContractError(sourceName, $"{prefix}.scale x/y must be finite non-zero numbers");
            }
        }


        var transitionIds = new HashSet<string>(StringComparer.Ordinal);
        var pairs = new HashSet<(string From, string To)>();
        for (int index = 0; index < project.TransitionOverrides.Length; index++)
        {
            TransitionOverrideDto? item = project.TransitionOverrides[index];
            string prefix = $"transitionOverrides[{index}]";
            if (item is null || string.IsNullOrWhiteSpace(item.TransitionId) ||
                !transitionIds.Add(item.TransitionId))
            {
                throw ContractError(sourceName,
                    $"{prefix}.transitionId must be unique and non-empty");
            }

            if (string.IsNullOrWhiteSpace(item.FromInstanceId) ||
                string.IsNullOrWhiteSpace(item.ToInstanceId) ||
                !pairs.Add((item.FromInstanceId, item.ToInstanceId)))
            {
                throw ContractError(sourceName,
                    $"{prefix} must identify a unique non-empty from/to pair");
            }

            ValidatePoint(item.Control1Offset, sourceName, $"{prefix}.control1Offset");
            ValidatePoint(item.Control2Offset, sourceName, $"{prefix}.control2Offset");
        }
    }

    private static void MigrateInMemory(
        TrackProjectDto project,
        string sourceName,
        ICollection<string> warnings)
    {
        if (project.FormatVersion != 1)
        {
            return;
        }

        /*
         * Version 1 had no manual transition state. Migration therefore has one
         * lossless default: an empty override array. This changes only the loaded
         * candidate in memory; the source file is untouched until explicit Save.
         */
        project.FormatVersion = SupportedFormatVersion;
        project.TransitionOverrides = [];
        warnings.Add(
            $"Track Project '{sourceName}' was migrated in memory from formatVersion 1 to 2; " +
            "transitionOverrides defaulted to an empty array.");
    }

    private static TrackProjectLoadResult ResolveDefinitions(
        TrackProjectDto project,
        SandboxedJsonLibrary exerciseLibrary,
        IEnumerable<string>? initialWarnings = null)
    {
        var definitions = new Dictionary<string, ExerciseDefinitionDto>(StringComparer.Ordinal);
        var warnings = initialWarnings?.ToList() ?? [];
        foreach (TrackProjectInstanceDto instance in project.Instances)
        {
            try
            {
                string path = exerciseLibrary.ResolveExistingJson(instance.ExercisePath);
                ExerciseDefinitionLoadResult loaded = ExerciseDefinitionStore.LoadFromFileWithDiagnostics(path);
                definitions[instance.InstanceId] = loaded.Definition;
                warnings.AddRange(loaded.Warnings.Select(warning => $"{instance.InstanceId}: {warning}"));
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                warnings.Add(
                    $"Instance '{instance.InstanceId}' is unresolved: exercisePath " +
                    $"'{instance.ExercisePath}' could not be loaded ({exception.Message}).");
            }
        }

        return new TrackProjectLoadResult(project, definitions, warnings);
    }

    private static void ValidatePoint(Point2Dto? point, string sourceName, string path)
    {
        if (point is null || !float.IsFinite(point.X) || !float.IsFinite(point.Y))
        {
            throw ContractError(sourceName, $"{path} must contain finite x/y coordinates");
        }
    }

    private static bool IsPositiveFinite(float value) => float.IsFinite(value) && value > 0.0f;

    private static InvalidDataException ContractError(string sourceName, string message) =>
        new($"Track Project '{sourceName}' is invalid: {message}.");
}
