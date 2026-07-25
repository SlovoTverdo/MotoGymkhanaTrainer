using System.Text.Json.Serialization;
using MotoGymkhanaTrainer.Tracks;

namespace MotoGymkhanaTrainer.TrackEditor;

/// <summary>
/// Editable Track Project root. It stores Exercise references and transforms;
/// unlike exported <see cref="TrackSnapshotDto"/>, it is not a Viewer snapshot.
/// </summary>
public sealed class TrackProjectDto
{
    [JsonPropertyName("formatVersion")]
    public int FormatVersion { get; set; } = 1;

    [JsonPropertyName("track")]
    public TrackProjectMetadataDto Track { get; set; } = new();

    [JsonPropertyName("area")]
    public TrackProjectAreaDto Area { get; set; } = new();

    [JsonPropertyName("instances")]
    public TrackProjectInstanceDto[] Instances { get; set; } = [];
}

/// <summary>Stable Track Project identity; its file path remains editor state.</summary>
public sealed class TrackProjectMetadataDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

/// <summary>Track workspace bounds in domain metres.</summary>
public sealed class TrackProjectAreaDto
{
    [JsonPropertyName("width")]
    public float Width { get; set; } = 60.0f;

    [JsonPropertyName("length")]
    public float Length { get; set; } = 100.0f;
}

/// <summary>One ordered Exercise reference and its only persisted transform.</summary>
public sealed class TrackProjectInstanceDto
{
    [JsonPropertyName("instanceId")]
    public string InstanceId { get; set; } = string.Empty;

    [JsonPropertyName("exercisePath")]
    public string ExercisePath { get; set; } = string.Empty;

    [JsonPropertyName("position")]
    public Point2Dto Position { get; set; } = new();

    [JsonPropertyName("rotationDeg")]
    public float RotationDeg { get; set; }

    [JsonPropertyName("scale")]
    public Point2Dto Scale { get; set; } = new() { X = 1.0f, Y = 1.0f };
}
