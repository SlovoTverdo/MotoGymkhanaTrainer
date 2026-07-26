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
        ResolvedVenue venue,
        IReadOnlyDictionary<string, ExerciseDefinitionDto> definitions,
        IReadOnlyList<string> warnings)
    {
        Project = project;
        Venue = venue;
        Definitions = definitions;
        Warnings = warnings;
    }

    public TrackProjectDto Project { get; }

    public ResolvedVenue Venue { get; }

    /// <summary>Definitions keyed by instanceId; absent keys are unresolved instances.</summary>
    public IReadOnlyDictionary<string, ExerciseDefinitionDto> Definitions { get; }

    public IReadOnlyList<string> Warnings { get; }
}

/// <summary>Strict UTF-8 serialization and validation for Venue-bound Track Project v3.</summary>
public static class TrackProjectStore
{
    private const int SupportedFormatVersion = 3;
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
    public static TrackProjectLoadResult LoadFromFile(
        string path,
        SandboxedJsonLibrary exerciseLibrary,
        SandboxedJsonLibrary venueLibrary,
        string projectRoot,
        Func<string, VenueResourceKind, bool> resourceProbe)
    {
        return LoadFromJson(File.ReadAllText(path, Encoding.UTF8), path, exerciseLibrary,
            venueLibrary, projectRoot, resourceProbe);
    }

    /// <summary>Deserializes a candidate without mutating a live Track document.</summary>
    public static TrackProjectLoadResult LoadFromJson(
        string json,
        string sourceName,
        SandboxedJsonLibrary exerciseLibrary,
        SandboxedJsonLibrary venueLibrary,
        string projectRoot,
        Func<string, VenueResourceKind, bool> resourceProbe)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidDataException($"Track Project '{sourceName}' is empty.");
        }

        try
        {
            using JsonDocument parsed = JsonDocument.Parse(json);
            if (parsed.RootElement.ValueKind != JsonValueKind.Object)
                throw ContractError(sourceName, "root must be an object");
            if (parsed.RootElement.TryGetProperty("formatVersion", out JsonElement versionElement) &&
                versionElement.TryGetInt32(out int sourceVersion) && sourceVersion == SupportedFormatVersion)
            {
                RequireProperty(parsed.RootElement, "track", JsonValueKind.Object, sourceName);
                RequireProperty(parsed.RootElement, "venuePath", JsonValueKind.String, sourceName);
                RequireProperty(parsed.RootElement, "instances", JsonValueKind.Array, sourceName);
                RequireProperty(parsed.RootElement, "transitionOverrides", JsonValueKind.Array, sourceName);
            }

            TrackProjectDto project = JsonSerializer.Deserialize<TrackProjectDto>(json, ReadOptions)
                ?? throw new InvalidDataException($"Track Project '{sourceName}' contains no JSON object.");
            Validate(project, sourceName, exerciseLibrary, venueLibrary);
            ResolvedVenue venue = ResolvedVenueLoader.Load(
                project.VenuePath, venueLibrary, projectRoot, resourceProbe);
            return ResolveDefinitions(project, venue, exerciseLibrary, venue.Warnings);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"Track Project '{sourceName}' contains invalid JSON: {exception.Message}", exception);
        }
    }

    private static void RequireProperty(
        JsonElement root,
        string property,
        JsonValueKind kind,
        string sourceName)
    {
        if (!root.TryGetProperty(property, out JsonElement value) || value.ValueKind != kind)
            throw ContractError(sourceName, $"{property} is mandatory and must be {kind}");
    }

    /// <summary>Saves domain data only as readable UTF-8 without a BOM.</summary>
    public static void SaveToFile(
        TrackProjectDto project,
        string path,
        SandboxedJsonLibrary exerciseLibrary,
        SandboxedJsonLibrary venueLibrary)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, Serialize(project, exerciseLibrary, venueLibrary), new UTF8Encoding(false));
    }

    /// <summary>Serializes no cached definitions, transformed geometry or UI state.</summary>
    public static string Serialize(
        TrackProjectDto project,
        SandboxedJsonLibrary exerciseLibrary,
        SandboxedJsonLibrary venueLibrary)
    {
        project.FormatVersion = SupportedFormatVersion;
        Validate(project, "in-memory Track Project", exerciseLibrary, venueLibrary);
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
        SandboxedJsonLibrary exerciseLibrary,
        ResolvedVenue venue)
    {
        TrackProjectDto project = JsonSerializer.Deserialize<TrackProjectDto>(snapshot, ReadOptions)
            ?? throw new InvalidDataException("Undo history snapshot contains no Track Project object.");
        if (project.FormatVersion != SupportedFormatVersion || project.Instances is null ||
            project.TransitionOverrides is null)
        {
            throw new InvalidDataException("Undo history snapshot has an incompatible Track Project shape.");
        }

        if (!string.Equals(project.VenuePath, venue.VenuePath, StringComparison.Ordinal))
            throw new InvalidDataException("Undo history snapshot references a different Venue.");
        return ResolveDefinitions(project, venue, exerciseLibrary);
    }

    private static void Validate(
        TrackProjectDto project,
        string sourceName,
        SandboxedJsonLibrary exerciseLibrary,
        SandboxedJsonLibrary venueLibrary)
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

        try
        {
            if (string.IsNullOrWhiteSpace(project.VenuePath) || Path.IsPathRooted(project.VenuePath) ||
                project.VenuePath.StartsWith("res://", StringComparison.OrdinalIgnoreCase) ||
                project.VenuePath.Contains('\\') || project.VenuePath.Contains(':') ||
                project.VenuePath.Split('/').Any(value => value is "" or "." or "..") ||
                !string.Equals(Path.GetExtension(project.VenuePath), ".json", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("venuePath must be a JSON path relative to res://venues/");
            }

            venueLibrary.ResolveUserPath(project.VenuePath);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException)
        {
            throw ContractError(sourceName, $"venuePath is unsafe: {exception.Message}");
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

    private static TrackProjectLoadResult ResolveDefinitions(
        TrackProjectDto project,
        ResolvedVenue venue,
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

        return new TrackProjectLoadResult(project, venue, definitions, warnings);
    }

    private static void ValidatePoint(Point2Dto? point, string sourceName, string path)
    {
        if (point is null || !float.IsFinite(point.X) || !float.IsFinite(point.Y))
        {
            throw ContractError(sourceName, $"{path} must contain finite x/y coordinates");
        }
    }

    private static InvalidDataException ContractError(string sourceName, string message) =>
        new($"Track Project '{sourceName}' is invalid: {message}.");
}
