using System.Text.Json.Serialization;

namespace MotoGymkhanaTrainer.Tracks;

/// <summary>Self-contained exported track snapshot consumed by the Viewer.</summary>
public sealed class TrackSnapshotDto
{
    [JsonPropertyName("formatVersion")]
    public int FormatVersion { get; set; }

    [JsonPropertyName("track")]
    public TrackMetadataDto Track { get; init; } = new();

    [JsonPropertyName("venue")]
    public VenueMetadataSnapshotDto Venue { get; init; } = new();

    [JsonPropertyName("area")]
    public AreaDto Area { get; init; } = new();

    [JsonPropertyName("panorama")]
    public PanoramaSnapshotDto Panorama { get; init; } = new();

    [JsonPropertyName("venueObjects")]
    public VenueObjectSnapshotDto[] VenueObjects { get; init; } = [];

    [JsonPropertyName("elements")]
    public ElementDto[] Elements { get; init; } = [];

    [JsonPropertyName("cones")]
    public ConeDto[] Cones { get; init; } = [];

    [JsonPropertyName("markings")]
    public MarkingDto[] Markings { get; set; } = [];

    [JsonPropertyName("trajectory")]
    public TrajectoryDto Trajectory { get; init; } = new();

    [JsonPropertyName("checkpoints")]
    public CheckpointDto[] Checkpoints { get; init; } = [];
}

/// <summary>Identity displayed for an exported track.</summary>
public sealed class TrackMetadataDto
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;
}

/// <summary>Identity copied from the source Venue Definition.</summary>
public sealed class VenueMetadataSnapshotDto
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;
}

/// <summary>Runtime panorama configuration copied from Venue.</summary>
public sealed class PanoramaSnapshotDto
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; }

    [JsonPropertyName("texturePath")]
    public string TexturePath { get; init; } = string.Empty;

    [JsonPropertyName("rotationDeg")]
    public float RotationDeg { get; init; }

    [JsonPropertyName("energyMultiplier")]
    public float EnergyMultiplier { get; init; } = 1.0f;
}

/// <summary>One immutable Venue object placement consumed by Viewer.</summary>
public sealed class VenueObjectSnapshotDto
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("assetPath")]
    public string AssetPath { get; init; } = string.Empty;

    [JsonPropertyName("objectType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ObjectType { get; init; }

    [JsonPropertyName("assetId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AssetId { get; init; }

    [JsonPropertyName("position")]
    public Point2Dto Position { get; init; } = new();

    [JsonPropertyName("elevation")]
    public float Elevation { get; init; }

    [JsonPropertyName("rotationDeg")]
    public float RotationDeg { get; init; }

    [JsonPropertyName("scale")]
    public Point3Dto Scale { get; init; } = new();

    [JsonPropertyName("footprint")]
    public AreaDto Footprint { get; init; } = new();

    [JsonPropertyName("collisionEnabled")]
    public bool CollisionEnabled { get; init; } = true;

    [JsonPropertyName("collisionMode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CollisionMode { get; init; }

    [JsonPropertyName("visibleInViewer")]
    public bool VisibleInViewer { get; init; } = true;
}

/// <summary>Three independent scale components for a Viewer scene instance.</summary>
public sealed class Point3Dto
{
    [JsonPropertyName("x")]
    public float X { get; init; } = 1.0f;

    [JsonPropertyName("y")]
    public float Y { get; init; } = 1.0f;

    [JsonPropertyName("z")]
    public float Z { get; init; } = 1.0f;
}

/// <summary>Rectangular training area dimensions in metres.</summary>
public sealed class AreaDto
{
    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? Name { get; init; }

    [JsonPropertyName("width")]
    public float Width { get; init; }

    [JsonPropertyName("length")]
    public float Length { get; init; }

    [JsonPropertyName("centerX")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public float CenterX { get; init; }

    [JsonPropertyName("centerY")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public float CenterY { get; init; }
}

/// <summary>Informational metadata about a resolved exercise instance.</summary>
public sealed class ElementDto
{
    [JsonPropertyName("instanceId")]
    public string InstanceId { get; init; } = string.Empty;

    [JsonPropertyName("definitionId")]
    public string DefinitionId { get; init; } = string.Empty;

    [JsonPropertyName("exercisePath")]
    public string ExercisePath { get; init; } = string.Empty;

    [JsonPropertyName("position")]
    public Point2Dto Position { get; init; } = new();

    [JsonPropertyName("rotationDeg")]
    public float RotationDeg { get; init; }

    [JsonPropertyName("scale")]
    public Point2Dto Scale { get; init; } = new();
}

/// <summary>A resolved cone in world-space domain coordinates.</summary>
public sealed class ConeDto
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("position")]
    public Point2Dto Position { get; init; } = new();

    [JsonPropertyName("color")]
    public string Color { get; init; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;
}

/// <summary>A colored marking whose Path is local or resolved according to its root document.</summary>
public sealed class MarkingDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("color")]
    public string Color { get; set; } = "#FFFFFF";

    [JsonPropertyName("widthMeters")]
    public float WidthMeters { get; set; } = 0.08f;

    /// <summary>Line pattern: solid, dashed or dotted.</summary>
    [JsonPropertyName("style")]
    public string Style { get; set; } = "solid";

    /// <summary>
    /// Export visibility flag. Exercise Editor still draws hidden markings with
    /// reduced opacity so they remain selectable and editable.
    /// </summary>
    [JsonPropertyName("visibleInViewer")]
    public bool VisibleInViewer { get; set; } = true;

    [JsonPropertyName("path")]
    public PathDefinition Path { get; set; } = new();

    /// <summary>
    /// Viewer-only diagnostic assigned when one exported marking cannot be
    /// parsed or validated. It is never serialized back into the contract.
    /// </summary>
    [JsonIgnore]
    public string LoadError { get; set; } = string.Empty;
}

/// <summary>Resolved world-space reference trajectory rendered by the Viewer.</summary>
public sealed class TrajectoryDto
{
    [JsonPropertyName("segments")]
    public TrajectorySegmentDto[] Segments { get; set; } = null!;
}

/// <summary>
/// One resolved trajectory segment. Geometry fields are populated according to <see cref="Type"/>.
/// </summary>
public sealed class TrajectorySegmentDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("points")]
    public Point2Dto[]? Points { get; set; }

    [JsonPropertyName("start")]
    public Point2Dto? Start { get; set; }

    [JsonPropertyName("control1")]
    public Point2Dto? Control1 { get; set; }

    [JsonPropertyName("control2")]
    public Point2Dto? Control2 { get; set; }

    [JsonPropertyName("end")]
    public Point2Dto? End { get; set; }
}

/// <summary>Reserved checkpoint data from the exported contract.</summary>
public sealed class CheckpointDto
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("order")]
    public int Order { get; init; }

    [JsonPropertyName("center")]
    public Point2Dto Center { get; init; } = new();

    [JsonPropertyName("direction")]
    public Point2Dto Direction { get; init; } = new();

    [JsonPropertyName("width")]
    public float Width { get; init; }
}

/// <summary>A two-dimensional point in domain X/Y metres.</summary>
public sealed class Point2Dto
{
    [JsonPropertyName("x")]
    public float X { get; init; }

    [JsonPropertyName("y")]
    public float Y { get; init; }
}
