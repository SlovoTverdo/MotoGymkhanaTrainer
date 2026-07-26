using Godot;

namespace MotoGymkhanaTrainer.TrackEditor;

/// <summary>Launches Viewer in a separate process so the editor session stays alive.</summary>
public static class ViewerPreviewLauncher
{
    public const string PreviewRelativePath = "_preview/current-track-preview.json";

    /// <summary>Builds arguments separately so the path-only contract is testable.</summary>
    public static string[] BuildArguments(
        string projectRoot,
        string exportedTrackPath,
        string? logPath = null) =>
    [
        "--path",
        projectRoot,
        "--log-file",
        logPath ?? Path.Combine(projectRoot, ".godot", "viewer-preview.log"),
        "res://scenes/Main.tscn",
        "--",
        "--track",
        exportedTrackPath,
    ];

    /// <summary>Returns the child PID, or throws without mutating editor state.</summary>
    public static int Launch(string exportedTrackPath)
    {
        string executable = OS.GetExecutablePath();
        string projectRoot = ProjectSettings.GlobalizePath("res://");
        // Concurrent Godot processes must not compete for the same timestamped
        // project log file on Windows. A unique log also preserves diagnostics if
        // the child exits before its window becomes visible.
        string logPath = Path.Combine(projectRoot, ".godot",
            $"viewer-preview-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.log");
        long processId = OS.CreateProcess(
            executable, BuildArguments(projectRoot, exportedTrackPath, logPath));
        if (processId <= 0)
        {
            throw new IOException($"Godot failed to start Viewer (process result {processId}).");
        }

        return checked((int)processId);
    }
}
