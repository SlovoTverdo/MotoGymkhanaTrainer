using System.Text.Json.Serialization;

namespace MotoGymkhanaTrainer.VenueEditor;

/// <summary>Persisted metadata for one managed imported Venue asset.</summary>
public sealed class VenueImportedAssetMetadata
{
    [JsonPropertyName("assetId")] public string AssetId { get; set; } = string.Empty;
    [JsonPropertyName("displayName")] public string DisplayName { get; set; } = string.Empty;
    [JsonPropertyName("sourceFile")] public string SourceFile { get; set; } = string.Empty;
    [JsonPropertyName("contentSha256")] public string ContentSha256 { get; set; } = string.Empty;
    [JsonPropertyName("sourcePath")] public string SourcePath { get; set; } = string.Empty;
    [JsonPropertyName("runtimeScenePath")] public string RuntimeScenePath { get; set; } = string.Empty;
    [JsonPropertyName("bounds")] public VenueImportedBoundsMetadata Bounds { get; set; } = new();
    [JsonPropertyName("footprint")] public FootprintDto Footprint { get; set; } = new();
    [JsonPropertyName("collisionMode")] public string CollisionMode { get; set; } = "generated";
}

/// <summary>Asset-root-local aggregate bounds retained for preview fitting and diagnostics.</summary>
public sealed class VenueImportedBoundsMetadata
{
    [JsonPropertyName("minX")] public float MinX { get; set; }
    [JsonPropertyName("minY")] public float MinY { get; set; }
    [JsonPropertyName("minZ")] public float MinZ { get; set; }
    [JsonPropertyName("maxX")] public float MaxX { get; set; }
    [JsonPropertyName("maxY")] public float MaxY { get; set; }
    [JsonPropertyName("maxZ")] public float MaxZ { get; set; }
}

/// <summary>Result of import or duplicate reuse.</summary>
public sealed record VenueImportedAssetResult(VenueImportedAssetMetadata Asset, bool ReusedExisting);
