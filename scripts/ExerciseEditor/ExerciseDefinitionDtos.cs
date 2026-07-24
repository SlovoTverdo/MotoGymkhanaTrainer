using System.Text.Json.Serialization;
using MotoGymkhanaTrainer.Tracks;

namespace MotoGymkhanaTrainer.ExerciseEditor;

/// <summary>
/// Editable exercise document. This root is deliberately separate from
/// <see cref="TrackSnapshotDto"/> because an exercise contains local authoring data,
/// while an exported track is a resolved world-space Viewer snapshot.
/// </summary>
public sealed class ExerciseDefinitionDto
{
    [JsonPropertyName("formatVersion")]
    public int FormatVersion { get; set; } = 1;

    [JsonPropertyName("exercise")]
    public ExerciseMetadataDto Exercise { get; set; } = new();

    [JsonPropertyName("bounds")]
    public ExerciseBoundsDto Bounds { get; set; } = new();

    [JsonPropertyName("cones")]
    public ConeDto[] Cones { get; set; } = [];

    [JsonPropertyName("markings")]
    public MarkingDto[] Markings { get; set; } = [];

    [JsonPropertyName("entryPoint")]
    public Point2Dto EntryPoint { get; set; } = new();

    [JsonPropertyName("exitPoint")]
    public Point2Dto ExitPoint { get; set; } = new();

    [JsonPropertyName("trajectory")]
    public TrajectoryDto Trajectory { get; set; } = new() { Segments = [] };

    [JsonPropertyName("checkpoints")]
    public CheckpointDto[] Checkpoints { get; set; } = [];
}

/// <summary>Stable identity and revision of one reusable exercise.</summary>
public sealed class ExerciseMetadataDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;
}

/// <summary>Local rectangular bounds centred on the exercise origin, in metres.</summary>
public sealed class ExerciseBoundsDto
{
    [JsonPropertyName("width")]
    public float Width { get; set; } = 10.0f;

    [JsonPropertyName("length")]
    public float Length { get; set; } = 10.0f;
}
