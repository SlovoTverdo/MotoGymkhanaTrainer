using System.Text.Json;
using System.Text.Json.Serialization;

namespace MotoGymkhanaTrainer.Tracks;

/// <summary>A continuous marking path with one start and ordered segments.</summary>
[JsonConverter(typeof(PathDefinitionJsonConverter))]
public sealed class PathDefinition
{
    [JsonPropertyName("start")]
    public Point2Dto Start { get; set; } = new();

    [JsonPropertyName("segments")]
    public PathSegmentDefinition[] Segments { get; set; } = [];
}

/// <summary>Requires both Path fields instead of accepting CLR defaults for missing JSON.</summary>
public sealed class PathDefinitionJsonConverter : JsonConverter<PathDefinition>
{
    public override PathDefinition Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using JsonDocument document = JsonDocument.ParseValue(ref reader);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new JsonException("Path must be an object.");
        if (!root.TryGetProperty("segments", out JsonElement segments) ||
            segments.ValueKind != JsonValueKind.Array)
            throw new JsonException("Path field 'segments' must be an array.");

        return new PathDefinition
        {
            Start = PathSegmentDefinitionJsonConverter.ReadRequiredPoint(root, "start", options),
            Segments = segments.Deserialize<PathSegmentDefinition[]>(options)
                ?? throw new JsonException("Path field 'segments' is required."),
        };
    }

    public override void Write(Utf8JsonWriter writer, PathDefinition value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("start");
        JsonSerializer.Serialize(writer, value.Start, options);
        writer.WritePropertyName("segments");
        JsonSerializer.Serialize(writer, value.Segments, options);
        writer.WriteEndObject();
    }
}

/// <summary>Base contract for one marking path segment.</summary>
[JsonConverter(typeof(PathSegmentDefinitionJsonConverter))]
public abstract class PathSegmentDefinition
{
    /// <summary>Returns the segment endpoint, which becomes the next segment start.</summary>
    [JsonIgnore]
    public abstract Point2Dto EndPoint { get; }
}

/// <summary>A straight marking path segment.</summary>
public sealed class LinePathSegmentDefinition : PathSegmentDefinition
{
    [JsonPropertyName("end")]
    public Point2Dto End { get; set; } = new();

    public override Point2Dto EndPoint => End;
}

/// <summary>A cubic Bézier marking path segment.</summary>
public sealed class CubicBezierPathSegmentDefinition : PathSegmentDefinition
{
    [JsonPropertyName("control1")]
    public Point2Dto Control1 { get; set; } = new();

    [JsonPropertyName("control2")]
    public Point2Dto Control2 { get; set; } = new();

    [JsonPropertyName("end")]
    public Point2Dto End { get; set; } = new();

    public override Point2Dto EndPoint => End;
}

/// <summary>
/// Strict, trim-safe JSON converter for marking path segments. Manual writing keeps
/// the external representation stable and prevents cubic-only fields leaking into lines.
/// </summary>
public sealed class PathSegmentDefinitionJsonConverter : JsonConverter<PathSegmentDefinition>
{
    public override PathSegmentDefinition Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using JsonDocument document = JsonDocument.ParseValue(ref reader);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("A path segment must be an object.");
        }

        if (!root.TryGetProperty("type", out JsonElement typeElement) ||
            typeElement.ValueKind != JsonValueKind.String)
        {
            throw new JsonException("Path segment field 'type' must be a string.");
        }

        string discriminator = typeElement.GetString() ?? string.Empty;
        return discriminator switch
        {
            "line" => new LinePathSegmentDefinition
            {
                End = ReadRequiredPoint(root, "end", options),
            },
            "cubicBezier" => new CubicBezierPathSegmentDefinition
            {
                Control1 = ReadRequiredPoint(root, "control1", options),
                Control2 = ReadRequiredPoint(root, "control2", options),
                End = ReadRequiredPoint(root, "end", options),
            },
            _ => throw new JsonException(
                $"Unknown path segment type '{discriminator}'. Expected 'line' or 'cubicBezier'."),
        };
    }

    public override void Write(
        Utf8JsonWriter writer,
        PathSegmentDefinition value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        switch (value)
        {
            case LinePathSegmentDefinition line:
                writer.WriteString("type", "line");
                writer.WritePropertyName("end");
                JsonSerializer.Serialize(writer, line.End, options);
                break;

            case CubicBezierPathSegmentDefinition cubic:
                writer.WriteString("type", "cubicBezier");
                writer.WritePropertyName("control1");
                JsonSerializer.Serialize(writer, cubic.Control1, options);
                writer.WritePropertyName("control2");
                JsonSerializer.Serialize(writer, cubic.Control2, options);
                writer.WritePropertyName("end");
                JsonSerializer.Serialize(writer, cubic.End, options);
                break;

            default:
                throw new JsonException($"Unsupported path segment CLR type '{value?.GetType().Name}'.");
        }

        writer.WriteEndObject();
    }

    internal static Point2Dto ReadRequiredPoint(
        JsonElement root,
        string propertyName,
        JsonSerializerOptions options)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement element) ||
            element.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException($"Path segment field '{propertyName}' must be an object.");
        }

        foreach (string coordinateName in new[] { "x", "y" })
        {
            if (!element.TryGetProperty(coordinateName, out JsonElement coordinate) ||
                coordinate.ValueKind != JsonValueKind.Number)
            {
                throw new JsonException(
                    $"Path segment field '{propertyName}.{coordinateName}' must be a number.");
            }
        }

        return element.Deserialize<Point2Dto>(options)
            ?? throw new JsonException($"Path segment field '{propertyName}' is required.");
    }
}
