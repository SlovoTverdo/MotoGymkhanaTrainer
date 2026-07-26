using MotoGymkhanaTrainer.VenueEditor;

namespace MotoGymkhanaTrainer.TrackEditor;

/// <summary>Kind of Godot resource referenced by a Venue Definition.</summary>
public enum VenueResourceKind
{
    PackedScene,
    Texture2D,
}

/// <summary>Runtime-only resolved Venue dependency kept outside both persisted source DTOs.</summary>
public sealed class ResolvedVenue
{
    public required string VenuePath { get; init; }
    public required string SourcePath { get; init; }
    public required VenueDefinitionDto Definition { get; init; }
    public required IReadOnlySet<string> ResolvedObjectIds { get; init; }
    public required bool PanoramaTextureResolved { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }

    /// <summary>True when the referenced object scene passed the active resource probe.</summary>
    public bool IsObjectResolved(string objectId) => ResolvedObjectIds.Contains(objectId);
}

/// <summary>Loads and validates a Venue candidate without changing a live Track document.</summary>
public static class ResolvedVenueLoader
{
    /// <summary>
    /// Track Editor supplies a Godot ResourceLoader probe; pure tests may supply a
    /// filesystem probe without claiming to verify imported resource types.
    /// </summary>
    public static ResolvedVenue Load(
        string venuePath,
        SandboxedJsonLibrary venueLibrary,
        string projectRoot,
        Func<string, VenueResourceKind, bool> resourceProbe)
    {
        ValidateRelativePath(venuePath);
        string sourcePath;
        try
        {
            sourcePath = venueLibrary.ResolveExistingJson(venuePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new InvalidDataException(
                $"Venue '{venuePath}' could not be resolved inside res://venues/: {exception.Message}",
                exception);
        }

        VenueLoadResult loaded = VenueStore.LoadFromFile(sourcePath, projectRoot);
        var resolvedObjects = new HashSet<string>(StringComparer.Ordinal);
        var warnings = new List<string>(loaded.Warnings);
        foreach (VenueObjectInstanceDto item in loaded.Definition.Objects)
        {
            if (Probe(resourceProbe, item.AssetPath, VenueResourceKind.PackedScene))
            {
                resolvedObjects.Add(item.ObjectId);
            }
            else
            {
                warnings.Add(
                    $"Venue object '{item.ObjectId}' assetPath '{item.AssetPath}' is not loadable as PackedScene.");
            }
        }

        bool panoramaResolved = !string.IsNullOrWhiteSpace(loaded.Definition.Panorama.TexturePath) &&
            Probe(resourceProbe, loaded.Definition.Panorama.TexturePath, VenueResourceKind.Texture2D);
        if (!string.IsNullOrWhiteSpace(loaded.Definition.Panorama.TexturePath) && !panoramaResolved)
        {
            warnings.Add(
                $"Venue panorama texturePath '{loaded.Definition.Panorama.TexturePath}' is not loadable as Texture2D.");
        }

        return new ResolvedVenue
        {
            VenuePath = venuePath.Replace('\\', '/'),
            SourcePath = sourcePath,
            Definition = loaded.Definition,
            ResolvedObjectIds = resolvedObjects,
            PanoramaTextureResolved = panoramaResolved,
            Warnings = warnings.Distinct(StringComparer.Ordinal).ToArray(),
        };
    }

    /// <summary>Filesystem-only probe for non-Godot tests.</summary>
    public static Func<string, VenueResourceKind, bool> CreateFilesystemProbe(string projectRoot) =>
        (resourcePath, _) =>
        {
            try
            {
                return File.Exists(VenueStore.ToProjectPath(resourcePath, projectRoot));
            }
            catch (InvalidDataException)
            {
                return false;
            }
        };

    private static bool Probe(
        Func<string, VenueResourceKind, bool> resourceProbe,
        string resourcePath,
        VenueResourceKind kind)
    {
        try { return resourceProbe(resourcePath, kind); }
        catch { return false; }
    }

    private static void ValidateRelativePath(string venuePath)
    {
        string[] components = venuePath.Split('/');
        if (string.IsNullOrWhiteSpace(venuePath) || Path.IsPathRooted(venuePath) ||
            venuePath.StartsWith("res://", StringComparison.OrdinalIgnoreCase) ||
            venuePath.Contains('\\') || venuePath.Contains(':') ||
            components.Any(component => component is "" or "." or "..") ||
            !string.Equals(Path.GetExtension(venuePath), ".json", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "venuePath must be a safe JSON path relative to res://venues/ and must not contain res://, absolute roots or '..'.");
        }
    }
}
