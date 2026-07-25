using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MotoGymkhanaTrainer.Tracks;

namespace MotoGymkhanaTrainer.TrackEditor;

/// <summary>Writes canonical self-contained Viewer snapshots as Track v3 JSON.</summary>
public static class TrackExportStore
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Serializes and round-trips through the Viewer loader before disk output.</summary>
    public static string Serialize(TrackSnapshotDto snapshot)
    {
        snapshot.FormatVersion = 3;
        string json = JsonSerializer.Serialize(snapshot, WriteOptions);
        _ = TrackLoader.LoadFromJson(json, "compiled Track export");
        return json;
    }

    /// <summary>Saves readable UTF-8 without BOM; the caller owns sandbox resolution.</summary>
    public static void SaveToFile(TrackSnapshotDto snapshot, string path)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(path, Serialize(snapshot), new UTF8Encoding(false));
    }
}
