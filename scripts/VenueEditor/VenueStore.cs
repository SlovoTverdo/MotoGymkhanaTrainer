using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MotoGymkhanaTrainer.Tracks;

namespace MotoGymkhanaTrainer.VenueEditor;

/// <summary>A validated Venue Definition and its non-blocking diagnostics.</summary>
public sealed record VenueLoadResult(VenueDefinitionDto Definition, IReadOnlyList<string> Warnings);

/// <summary>Strict Venue Definition v1 serialization and validation.</summary>
public static class VenueStore
{
    private static readonly HashSet<string> ConeColors = ["red", "blue", "yellow", "orange", "none"];
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

    /// <summary>Loads a temporary candidate; caller replaces the live document only on success.</summary>
    public static VenueLoadResult LoadFromFile(string path, string projectRoot) =>
        LoadFromJson(File.ReadAllText(path, Encoding.UTF8), path, projectRoot);

    /// <summary>Deserializes, validates and diagnoses a Venue without touching editor state.</summary>
    public static VenueLoadResult LoadFromJson(string json, string sourceName, string projectRoot)
    {
        if (string.IsNullOrWhiteSpace(json)) throw Error(sourceName, "file is empty");
        try
        {
            VenueDefinitionDto definition = JsonSerializer.Deserialize<VenueDefinitionDto>(json, ReadOptions)
                ?? throw Error(sourceName, "JSON root is missing");
            Validate(definition, sourceName);
            return new VenueLoadResult(definition, Diagnose(definition, projectRoot));
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Venue file '{sourceName}' contains invalid JSON: {exception.Message}", exception);
        }
    }

    /// <summary>Saves canonical indented UTF-8 without a BOM.</summary>
    public static void SaveToFile(VenueDefinitionDto definition, string path)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(path, Serialize(definition), new UTF8Encoding(false));
    }

    /// <summary>Serializes persisted DTO fields only; canvas and resolved resources never enter this graph.</summary>
    public static string Serialize(VenueDefinitionDto definition)
    {
        Validate(definition, "in-memory Venue");
        return JsonSerializer.Serialize(definition, WriteOptions);
    }

    /// <summary>Restores a history snapshot through the same contract validation as disk loading.</summary>
    public static VenueDefinitionDto RestoreSnapshot(string json) =>
        LoadFromJson(json, "history snapshot", Directory.GetCurrentDirectory()).Definition;

    /// <summary>Returns non-blocking resource and spatial warnings.</summary>
    public static IReadOnlyList<string> Diagnose(VenueDefinitionDto definition, string projectRoot)
    {
        var warnings = new List<string>();
        if (definition.Panorama.Enabled && string.IsNullOrWhiteSpace(definition.Panorama.TexturePath))
            warnings.Add("Panorama is enabled but Texture Path is empty.");
        if (!string.IsNullOrWhiteSpace(definition.Panorama.TexturePath) &&
            !File.Exists(ToProjectPath(definition.Panorama.TexturePath, projectRoot)))
            warnings.Add($"Panorama texture is unresolved: {definition.Panorama.TexturePath}");

        var footprints = new List<(VenueObjectInstanceDto Item, Point2Dto[] Polygon)>();
        foreach (VenueObjectInstanceDto item in definition.Objects)
        {
            if (!File.Exists(ToProjectPath(item.AssetPath, projectRoot)))
                warnings.Add($"Object '{item.ObjectId}' is unresolved: {item.AssetPath}");
            Point2Dto[] polygon = VenueGeometry.TransformFootprint(item);
            footprints.Add((item, polygon));
            if (polygon.Any(point => VenueGeometry.IsOutsideArea(point, definition.Area)))
                warnings.Add($"Object '{item.ObjectId}' extends outside the Venue area.");
            if (item.Scale.X < 0.1f || item.Scale.Y < 0.1f || item.Scale.Z < 0.1f ||
                item.Scale.X > 10 || item.Scale.Y > 10 || item.Scale.Z > 10)
                warnings.Add($"Object '{item.ObjectId}' has an unusual scale.");
        }
        for (int left = 0; left < footprints.Count; left++)
            for (int right = left + 1; right < footprints.Count; right++)
                if (VenueGeometry.Overlaps(footprints[left].Polygon, footprints[right].Polygon))
                    warnings.Add($"Object footprints '{footprints[left].Item.ObjectId}' and '{footprints[right].Item.ObjectId}' overlap.");
        foreach (ConeDto cone in definition.Cones)
            if (VenueGeometry.IsOutsideArea(cone.Position, definition.Area))
                warnings.Add($"Cone '{cone.Id}' is outside the Venue area.");
        foreach (MarkingDto marking in definition.Markings)
            if (marking.Points.Any(point => VenueGeometry.IsOutsideArea(point, definition.Area)))
                warnings.Add($"Marking '{marking.Id}' extends outside the Venue area.");
        return warnings;
    }

    /// <summary>Maps a canonical res path beneath one explicitly supplied project root.</summary>
    public static string ToProjectPath(string resourcePath, string projectRoot)
    {
        ValidateResourcePath(resourcePath, "resource path");
        string root = Path.GetFullPath(projectRoot);
        string candidate = Path.GetFullPath(Path.Combine(root, resourcePath[6..].Replace('/', Path.DirectorySeparatorChar)));
        string prefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Godot resource path escapes the project root.");
        return candidate;
    }

    private static void Validate(VenueDefinitionDto definition, string source)
    {
        if (definition.FormatVersion != 1) throw Error(source, $"unsupported formatVersion {definition.FormatVersion}; expected 1");
        if (definition.Venue is null || string.IsNullOrWhiteSpace(definition.Venue.Id) || string.IsNullOrWhiteSpace(definition.Venue.Name))
            throw Error(source, "venue id and name must be non-empty");
        if (definition.Area is null || !Positive(definition.Area.Width) || !Positive(definition.Area.Length))
            throw Error(source, "area width and length must be finite positive numbers");
        if (definition.Panorama is null || !float.IsFinite(definition.Panorama.RotationDeg) ||
            !float.IsFinite(definition.Panorama.EnergyMultiplier) || definition.Panorama.EnergyMultiplier < 0)
            throw Error(source, "panorama rotation must be finite and energyMultiplier must be finite and non-negative");
        if (!string.IsNullOrEmpty(definition.Panorama.TexturePath)) ValidateResourcePath(definition.Panorama.TexturePath, "panorama.texturePath");
        if (definition.Objects is null || definition.Cones is null || definition.Markings is null)
            throw Error(source, "objects, cones and markings must be arrays");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < definition.Objects.Length; index++)
        {
            VenueObjectInstanceDto item = definition.Objects[index] ?? throw Error(source, $"objects[{index}] is null");
            if (string.IsNullOrWhiteSpace(item.ObjectId) || !ids.Add(item.ObjectId)) throw Error(source, $"objects[{index}] must have a unique id");
            if (string.IsNullOrWhiteSpace(item.Name)) throw Error(source, $"objects[{index}].name is empty");
            ValidateResourcePath(item.AssetPath, $"objects[{index}].assetPath");
            if (!item.AssetPath.EndsWith(".tscn", StringComparison.OrdinalIgnoreCase)) throw Error(source, $"objects[{index}].assetPath must reference .tscn");
            ValidatePoint(item.Position, source, $"objects[{index}].position");
            if (!float.IsFinite(item.Elevation) || !float.IsFinite(item.RotationDeg)) throw Error(source, $"objects[{index}] transform contains a non-finite value");
            if (item.Scale is null || !Positive(item.Scale.X) || !Positive(item.Scale.Y) || !Positive(item.Scale.Z)) throw Error(source, $"objects[{index}].scale components must be positive");
            if (item.Footprint is null || !Positive(item.Footprint.Width) || !Positive(item.Footprint.Length)) throw Error(source, $"objects[{index}].footprint dimensions must be positive");
        }

        ids.Clear();
        for (int index = 0; index < definition.Cones.Length; index++)
        {
            ConeDto cone = definition.Cones[index] ?? throw Error(source, $"cones[{index}] is null");
            if (string.IsNullOrWhiteSpace(cone.Id) || !ids.Add(cone.Id)) throw Error(source, $"cones[{index}] must have a unique id");
            ValidatePoint(cone.Position, source, $"cones[{index}].position");
            if (cone.Type != "standard" || !ConeColors.Contains(cone.Color)) throw Error(source, $"cones[{index}] has unsupported type or color");
        }

        ids.Clear();
        for (int index = 0; index < definition.Markings.Length; index++)
        {
            MarkingDto marking = definition.Markings[index] ?? throw Error(source, $"markings[{index}] is null");
            if (string.IsNullOrWhiteSpace(marking.Id) || !ids.Add(marking.Id)) throw Error(source, $"markings[{index}] must have a unique id");
            if (marking.Type is not ("line" or "polyline") || marking.Points is null || marking.Points.Length < 2 ||
                (marking.Type == "line" && marking.Points.Length != 2)) throw Error(source, $"markings[{index}] has invalid type or point count");
            foreach (Point2Dto point in marking.Points) ValidatePoint(point, source, $"markings[{index}].points");
            if (!MarkingGeometry.TryNormalizeColor(marking.Color, false, out string canonical) || canonical != marking.Color)
                throw Error(source, $"markings[{index}].color must be canonical #RRGGBB");
            if (!Positive(marking.WidthMeters) || !MarkingGeometry.IsSupportedStyle(marking.Style))
                throw Error(source, $"markings[{index}] has invalid width or style");
        }
    }

    private static void ValidateResourcePath(string value, string field)
    {
        string[] components = value.Split('/');
        if (string.IsNullOrWhiteSpace(value) || !value.StartsWith("res://", StringComparison.Ordinal) ||
            value.Contains('\\') || components.Skip(2).Any(component => component is "" or "." or "..") ||
            value.StartsWith("res://.godot/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"{field} must be a canonical res:// path inside the project.");
    }

    private static void ValidatePoint(Point2Dto? point, string source, string field)
    {
        if (point is null || !float.IsFinite(point.X) || !float.IsFinite(point.Y)) throw Error(source, $"{field} must contain finite x/y");
    }

    private static bool Positive(float value) => float.IsFinite(value) && value > 0;
    private static InvalidDataException Error(string source, string message) => new($"Venue file '{source}' is invalid: {message}.");
}
