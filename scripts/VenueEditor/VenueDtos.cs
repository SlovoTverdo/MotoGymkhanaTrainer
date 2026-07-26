using System.Text.Json.Serialization;
using MotoGymkhanaTrainer.Tracks;

namespace MotoGymkhanaTrainer.VenueEditor;

/// <summary>Standalone persisted Venue Definition v1 root.</summary>
public sealed class VenueDefinitionDto
{
    [JsonPropertyName("formatVersion")]
    public int FormatVersion { get; set; } = 1;

    [JsonPropertyName("venue")]
    public VenueMetadataDto Venue { get; set; } = new();

    [JsonPropertyName("area")]
    public VenueAreaDto Area { get; set; } = new();

    [JsonPropertyName("panorama")]
    public VenuePanoramaDto Panorama { get; set; } = new();

    [JsonPropertyName("objects")]
    public VenueObjectInstanceDto[] Objects { get; set; } = [];

    [JsonPropertyName("cones")]
    public ConeDto[] Cones { get; set; } = [];

    [JsonPropertyName("markings")]
    public MarkingDto[] Markings { get; set; } = [];
}

/// <summary>Stable identity displayed by the Venue library and editors.</summary>
public sealed class VenueMetadataDto
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
}

/// <summary>Reusable rectangular Venue dimensions in metres.</summary>
public sealed class VenueAreaDto
{
    [JsonPropertyName("width")] public float Width { get; set; } = 60.0f;
    [JsonPropertyName("length")] public float Length { get; set; } = 100.0f;
}

/// <summary>Metadata for future runtime PanoramaSkyMaterial setup.</summary>
public sealed class VenuePanoramaDto
{
    [JsonPropertyName("enabled")] public bool Enabled { get; set; }
    [JsonPropertyName("texturePath")] public string TexturePath { get; set; } = string.Empty;
    [JsonPropertyName("rotationDeg")] public float RotationDeg { get; set; }
    [JsonPropertyName("energyMultiplier")] public float EnergyMultiplier { get; set; } = 1.0f;
}

/// <summary>One persistent scene placement; no resolved PackedScene is serialized.</summary>
public sealed class VenueObjectInstanceDto
{
    [JsonPropertyName("objectId")] public string ObjectId { get; set; } = string.Empty;
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("assetPath")] public string AssetPath { get; set; } = string.Empty;
    [JsonPropertyName("position")] public Point2Dto Position { get; set; } = new();
    [JsonPropertyName("elevation")] public float Elevation { get; set; }
    [JsonPropertyName("rotationDeg")] public float RotationDeg { get; set; }
    [JsonPropertyName("scale")] public Scale3Dto Scale { get; set; } = new();
    [JsonPropertyName("footprint")] public FootprintDto Footprint { get; set; } = new();
    [JsonPropertyName("collisionEnabled")] public bool CollisionEnabled { get; set; } = true;
    [JsonPropertyName("visibleInViewer")] public bool VisibleInViewer { get; set; } = true;
}

/// <summary>Independent positive scale components for a placed 3D scene.</summary>
public sealed class Scale3Dto
{
    [JsonPropertyName("x")] public float X { get; set; } = 1.0f;
    [JsonPropertyName("y")] public float Y { get; set; } = 1.0f;
    [JsonPropertyName("z")] public float Z { get; set; } = 1.0f;
}

/// <summary>Unscaled local X/Z rectangle used by the top-down editor.</summary>
public sealed class FootprintDto
{
    [JsonPropertyName("width")] public float Width { get; set; } = 1.0f;
    [JsonPropertyName("length")] public float Length { get; set; } = 1.0f;
}
