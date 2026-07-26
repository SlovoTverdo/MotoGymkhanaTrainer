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
    public int FormatVersion { get; set; } = 3;

    [JsonPropertyName("track")]
    public TrackProjectMetadataDto Track { get; set; } = new();

    /// <summary>Venue-library-relative path; the Venue remains an external source document.</summary>
    [JsonPropertyName("venuePath")]
    public string VenuePath { get; set; } = string.Empty;

    [JsonPropertyName("instances")]
    public TrackProjectInstanceDto[] Instances { get; set; } = [];

    /// <summary>
    /// Manual corrections for derived transitions. Automatic transition geometry
    /// is deliberately absent from the project and is rebuilt during compilation.
    /// </summary>
    [JsonPropertyName("transitionOverrides")]
    public TransitionOverrideDto[] TransitionOverrides { get; set; } = [];
}

/// <summary>Stable Track Project identity; its file path remains editor state.</summary>
public sealed class TrackProjectMetadataDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
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

/// <summary>
/// Persisted correction for one oriented adjacent instance pair. Endpoints are
/// derived from the current instances, so only track-space handle offsets persist.
/// </summary>
public sealed class TransitionOverrideDto
{
    [JsonPropertyName("transitionId")]
    public string TransitionId { get; set; } = string.Empty;

    [JsonPropertyName("fromInstanceId")]
    public string FromInstanceId { get; set; } = string.Empty;

    [JsonPropertyName("toInstanceId")]
    public string ToInstanceId { get; set; } = string.Empty;

    [JsonPropertyName("control1Offset")]
    public Point2Dto Control1Offset { get; set; } = new();

    [JsonPropertyName("control2Offset")]
    public Point2Dto Control2Offset { get; set; } = new();
}
