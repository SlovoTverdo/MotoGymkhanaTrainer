using Godot;
using MotoGymkhanaTrainer.ExerciseEditor;
using MotoGymkhanaTrainer.Tracks;
using System.Text.Json;

namespace MotoGymkhanaTrainer.TrackEditor;

/// <summary>Transient state of the Exercise Library preview; never part of Track history.</summary>
public enum ExercisePreviewState
{
    NoSelection,
    Loading,
    Ready,
    InvalidExercise,
    MissingExercise,
}

/// <summary>
/// Non-domain identity recovered for a preview diagnostic when the full
/// Exercise Definition cannot pass validation and therefore has no DTO.
/// </summary>
public sealed record ExercisePreviewIdentity(string? DisplayName, string? ExerciseId);

/// <summary>Axis-aligned local Exercise content bounds in metres.</summary>
public readonly record struct ExerciseContentBounds(float MinX, float MinY, float MaxX, float MaxY)
{
    /// <summary>Horizontal extent in metres.</summary>
    public float Width => MaxX - MinX;
    /// <summary>Vertical extent in metres.</summary>
    public float Height => MaxY - MinY;
    /// <summary>Center of the complete local geometry bounds.</summary>
    public Point2Dto Center => new() { X = (MinX + MaxX) * 0.5f, Y = (MinY + MaxY) * 0.5f };
}

/// <summary>Uniform preview mapping calculated from cached content bounds and current panel size.</summary>
public readonly record struct ExercisePreviewFit(Point2Dto Center, float PixelsPerMeter)
{
    /// <summary>Maps one local Exercise point into the resized preview viewport.</summary>
    public Vector2 ToScreen(Point2Dto point, Vector2 viewportSize) =>
        viewportSize * 0.5f + new Vector2(
            (point.X - Center.X) * PixelsPerMeter,
            -(point.Y - Center.Y) * PixelsPerMeter);
}

/// <summary>Pure Exercise preview state, separated from Godot Controls for regression tests.</summary>
public sealed class ExercisePreviewModel
{
    /// <summary>Current preview lifecycle state.</summary>
    public ExercisePreviewState State { get; private set; } = ExercisePreviewState.NoSelection;
    /// <summary>Freshly loaded definition when <see cref="State"/> is ready.</summary>
    public ExerciseDefinitionDto? Definition { get; private set; }
    /// <summary>Selected Exercise Library path, never a Track instance reference.</summary>
    public string SourcePath { get; private set; } = string.Empty;
    /// <summary>Non-fatal load or validation diagnostic displayed by the panel.</summary>
    public string Diagnostic { get; private set; } = string.Empty;
    /// <summary>Load warnings for compact metadata display.</summary>
    public IReadOnlyList<string> Warnings { get; private set; } = [];
    /// <summary>Recoverable source metadata retained for invalid preview diagnostics.</summary>
    public ExercisePreviewIdentity? Identity { get; private set; }

    /// <summary>Returns the preview to the no-selection state.</summary>
    public void Clear()
    {
        State = ExercisePreviewState.NoSelection;
        Definition = null;
        SourcePath = string.Empty;
        Diagnostic = string.Empty;
        Warnings = [];
        Identity = null;
    }

    /// <summary>Clears stale geometry while a new library file is being resolved.</summary>
    public void SetLoading(string sourcePath)
    {
        State = ExercisePreviewState.Loading;
        Definition = null;
        SourcePath = sourcePath;
        Diagnostic = string.Empty;
        Warnings = [];
        Identity = null;
    }

    /// <summary>Replaces preview data with a successfully loaded definition.</summary>
    public void SetReady(
        string sourcePath,
        ExerciseDefinitionDto definition,
        IReadOnlyList<string>? warnings = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        State = ExercisePreviewState.Ready;
        Definition = definition;
        SourcePath = sourcePath;
        Diagnostic = string.Empty;
        Warnings = warnings ?? [];
        Identity = new ExercisePreviewIdentity(definition.Exercise.Name, definition.Exercise.Id);
    }

    /// <summary>Clears stale geometry and exposes a non-fatal invalid-file diagnostic.</summary>
    public void SetInvalid(
        string sourcePath,
        string diagnostic,
        ExercisePreviewIdentity? identity = null)
    {
        State = ExercisePreviewState.InvalidExercise;
        Definition = null;
        SourcePath = sourcePath;
        Diagnostic = diagnostic;
        Warnings = [];
        Identity = identity;
    }

    /// <summary>Clears stale geometry when the selected library file disappeared.</summary>
    public void SetMissing(string sourcePath)
    {
        State = ExercisePreviewState.MissingExercise;
        Definition = null;
        SourcePath = sourcePath;
        Diagnostic = $"Exercise '{sourcePath}' больше не существует.";
        Warnings = [];
        Identity = null;
    }
}

/// <summary>Pure compact metadata presentation used by the preview panel and tests.</summary>
public static class ExercisePreviewMetadataFormatter
{
    /// <summary>Reports the established routing-only convention: no cones.</summary>
    public static bool IsRoutingOnly(ExerciseDefinitionDto definition) => definition.Cones.Length == 0;

    /// <summary>Formats compact read-only Exercise metadata for the left panel.</summary>
    public static string Format(ExerciseDefinitionDto definition, int warningCount = 0)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return
            $"{definition.Exercise.Name}\n" +
            $"ID: {definition.Exercise.Id}   Footprint: {definition.Bounds.Width:0.##} × {definition.Bounds.Length:0.##} m\n" +
            $"Cones: {definition.Cones.Length}   Markings: {definition.Markings.Length}   " +
            $"Routing only: {(IsRoutingOnly(definition) ? "yes" : "no")}" +
            (warningCount > 0 ? $"   Warnings: {warningCount}" : string.Empty);
    }

    /// <summary>Formats a non-fatal invalid-file diagnostic without inventing unavailable metadata.</summary>
    public static string FormatInvalid(
        string sourcePath,
        string diagnostic,
        ExercisePreviewIdentity? identity = null)
    {
        var lines = new List<string> { "Invalid Exercise" };
        if (!string.IsNullOrWhiteSpace(identity?.DisplayName)) lines.Add($"Name: {identity.DisplayName}");
        if (!string.IsNullOrWhiteSpace(identity?.ExerciseId)) lines.Add($"ID: {identity.ExerciseId}");
        lines.Add($"Path: {sourcePath}");
        if (!string.IsNullOrWhiteSpace(diagnostic)) lines.Add(diagnostic);
        return string.Join('\n', lines);
    }
}

/// <summary>Reads only optional diagnostic metadata from JSON without constructing an Exercise DTO.</summary>
public static class ExercisePreviewIdentityExtractor
{
    /// <summary>
    /// Attempts to recover the persisted Exercise name and id after validation fails.
    /// Malformed JSON intentionally returns no identity rather than guessing metadata.
    /// </summary>
    public static ExercisePreviewIdentity? TryReadFile(string filePath)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(filePath));
            if (!document.RootElement.TryGetProperty("exercise", out JsonElement exercise) ||
                exercise.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            string? name = ReadString(exercise, "name");
            string? id = ReadString(exercise, "id");
            return string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(id)
                ? null
                : new ExercisePreviewIdentity(name, id);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? ReadString(JsonElement objectElement, string propertyName) =>
        objectElement.TryGetProperty(propertyName, out JsonElement value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}

/// <summary>Result of explicitly re-resolving the selected library item after Refresh.</summary>
public sealed record ExercisePreviewLibraryResolution(
    ExercisePreviewState State,
    string RelativePath,
    ExerciseDefinitionLoadResult? LoadResult,
    string Diagnostic,
    ExercisePreviewIdentity? Identity = null);

/// <summary>
/// Re-resolves preview data by stable Exercise id without retaining DTO references
/// from before a library refresh.
/// </summary>
public static class ExercisePreviewLibraryResolver
{
    /// <summary>
    /// Reloads the prior selection from disk, preferring its stable Exercise id
    /// when Refresh moved or replaced the original relative path.
    /// </summary>
    public static ExercisePreviewLibraryResolution Resolve(
        SandboxedJsonLibrary library,
        string previousPath,
        string previousId)
    {
        ArgumentNullException.ThrowIfNull(library);
        if (string.IsNullOrWhiteSpace(previousPath))
            return new ExercisePreviewLibraryResolution(
                ExercisePreviewState.NoSelection, string.Empty, null, string.Empty);

        Exception? exactPathError = null;
        ExercisePreviewIdentity? invalidIdentity = null;
        if (TryLoad(library, previousPath, out ExerciseDefinitionLoadResult? exact))
        {
            if (string.IsNullOrEmpty(previousId) ||
                string.Equals(exact!.Definition.Exercise.Id, previousId, StringComparison.Ordinal))
            {
                return Ready(previousPath, exact!);
            }
        }
        else
        {
            string? exactFile = null;
            try
            {
                exactFile = library.ResolveExistingJson(previousPath);
                _ = ExerciseDefinitionStore.LoadFromFileWithDiagnostics(exactFile);
            }
            catch (Exception exception)
            {
                exactPathError = exception;
                invalidIdentity = exactFile is null
                    ? null
                    : ExercisePreviewIdentityExtractor.TryReadFile(exactFile);
            }
        }

        if (!string.IsNullOrEmpty(previousId))
        {
            foreach (JsonLibraryEntry entry in library.EnumerateEntries().Where(item => !item.IsDirectory))
            {
                if (string.Equals(entry.RelativePath, previousPath, StringComparison.OrdinalIgnoreCase)) continue;
                if (!TryLoad(library, entry.RelativePath, out ExerciseDefinitionLoadResult? moved) ||
                    !string.Equals(moved!.Definition.Exercise.Id, previousId, StringComparison.Ordinal)) continue;
                return Ready(entry.RelativePath, moved);
            }
        }

        if (exactPathError is not null && exactPathError is not FileNotFoundException)
        {
            return new ExercisePreviewLibraryResolution(
                ExercisePreviewState.InvalidExercise, previousPath, null, exactPathError.Message, invalidIdentity);
        }
        return new ExercisePreviewLibraryResolution(
            ExercisePreviewState.MissingExercise, previousPath, null,
            $"Exercise '{previousPath}' больше не существует.");
    }

    private static bool TryLoad(
        SandboxedJsonLibrary library,
        string relativePath,
        out ExerciseDefinitionLoadResult? loaded)
    {
        try
        {
            loaded = ExerciseDefinitionStore.LoadFromFileWithDiagnostics(
                library.ResolveExistingJson(relativePath));
            return true;
        }
        catch (Exception)
        {
            loaded = null;
            return false;
        }
    }

    private static ExercisePreviewLibraryResolution Ready(
        string relativePath,
        ExerciseDefinitionLoadResult loaded) =>
        new(ExercisePreviewState.Ready, relativePath, loaded, string.Empty);
}

/// <summary>Calculates preview bounds from every requested local Exercise geometry layer.</summary>
public static class ExerciseContentBoundsCalculator
{
    /// <summary>Calculates analytical bounds across every preview geometry layer.</summary>
    public static ExerciseContentBounds Calculate(ExerciseDefinitionDto definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        float minX = -definition.Bounds.Width * 0.5f;
        float maxX = definition.Bounds.Width * 0.5f;
        float minY = -definition.Bounds.Length * 0.5f;
        float maxY = definition.Bounds.Length * 0.5f;

        foreach (ConeDto cone in definition.Cones)
            Include(cone.Position, ref minX, ref minY, ref maxX, ref maxY);

        foreach (MarkingDto marking in definition.Markings)
        {
            PathBounds bounds = PathBoundsCalculator.Calculate(marking.Path, marking.WidthMeters);
            Include(bounds, ref minX, ref minY, ref maxX, ref maxY);
        }

        foreach (TrajectorySegmentDto segment in definition.Trajectory.Segments)
        {
            if (segment.Type == "polyline")
            {
                foreach (Point2Dto point in segment.Points!)
                    Include(point, ref minX, ref minY, ref maxX, ref maxY);
                continue;
            }

            // PathBoundsCalculator owns the analytical cubic extrema math. The
            // temporary wrapper shares mathematics without conflating persisted
            // trajectory and marking domain contracts.
            var path = new PathDefinition
            {
                Start = segment.Start!,
                Segments =
                [
                    new CubicBezierPathSegmentDefinition
                    {
                        Control1 = segment.Control1!,
                        Control2 = segment.Control2!,
                        End = segment.End!,
                    },
                ],
            };
            Include(PathBoundsCalculator.Calculate(path, 0.0f),
                ref minX, ref minY, ref maxX, ref maxY);
        }

        Include(definition.EntryPoint, ref minX, ref minY, ref maxX, ref maxY);
        Include(definition.ExitPoint, ref minX, ref minY, ref maxX, ref maxY);
        return new ExerciseContentBounds(minX, minY, maxX, maxY);
    }

    private static void Include(
        Point2Dto point,
        ref float minX,
        ref float minY,
        ref float maxX,
        ref float maxY)
    {
        minX = MathF.Min(minX, point.X);
        minY = MathF.Min(minY, point.Y);
        maxX = MathF.Max(maxX, point.X);
        maxY = MathF.Max(maxY, point.Y);
    }

    private static void Include(
        PathBounds bounds,
        ref float minX,
        ref float minY,
        ref float maxX,
        ref float maxY)
    {
        minX = MathF.Min(minX, bounds.MinX);
        minY = MathF.Min(minY, bounds.MinY);
        maxX = MathF.Max(maxX, bounds.MaxX);
        maxY = MathF.Max(maxY, bounds.MaxY);
    }
}

/// <summary>Aspect-preserving auto-fit calculation for a resized preview panel.</summary>
public static class ExercisePreviewFitCalculator
{
    /// <summary>Calculates a centered, aspect-preserving fit with proportional margins.</summary>
    public static ExercisePreviewFit Calculate(
        ExerciseContentBounds bounds,
        Vector2 viewportSize,
        float marginFraction = 0.12f)
    {
        if (!float.IsFinite(viewportSize.X) || !float.IsFinite(viewportSize.Y) ||
            viewportSize.X <= 0.0f || viewportSize.Y <= 0.0f)
            throw new ArgumentOutOfRangeException(nameof(viewportSize));
        if (!float.IsFinite(marginFraction) || marginFraction < 0.0f || marginFraction >= 0.5f)
            throw new ArgumentOutOfRangeException(nameof(marginFraction));

        float usableWidth = MathF.Max(1.0f, viewportSize.X * (1.0f - marginFraction * 2.0f));
        float usableHeight = MathF.Max(1.0f, viewportSize.Y * (1.0f - marginFraction * 2.0f));
        float width = MathF.Max(bounds.Width, 0.001f);
        float height = MathF.Max(bounds.Height, 0.001f);
        float pixelsPerMeter = Math.Clamp(MathF.Min(usableWidth / width, usableHeight / height), 0.01f, 400.0f);
        return new ExercisePreviewFit(bounds.Center, pixelsPerMeter);
    }
}

/// <summary>Read-only, auto-fitted preview of the Exercise selected in the library.</summary>
public partial class ExercisePreviewControl : Control
{
    private readonly ExercisePreviewModel _model = new();
    private ExerciseGeometryRenderData? _geometry;
    private ExerciseContentBounds _contentBounds;

    /// <summary>Read-only preview state exposed for host metadata and diagnostics.</summary>
    public ExercisePreviewModel Model => _model;

    /// <summary>Counts data rebuilds so tests can distinguish them from resize redraws.</summary>
    public int GeometryRevision { get; private set; }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Resized += QueueRedraw;
        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.055f, 0.065f, 0.08f), true);
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.30f, 0.36f, 0.43f), false, 1.0f);
        if (_model.State != ExercisePreviewState.Ready || _geometry is null)
        {
            DrawStateMessage();
            return;
        }

        ExercisePreviewFit fit = ExercisePreviewFitCalculator.Calculate(
            _contentBounds,
            new Vector2(MathF.Max(Size.X, 1.0f), MathF.Max(Size.Y, 1.0f)));
        Func<Point2Dto, Vector2> toScreen = point => fit.ToScreen(point, Size);
        ExerciseGeometryRenderer.Draw(this, _geometry, toScreen, new ExerciseGeometryRenderOptions
        {
            PixelsPerMeter = fit.PixelsPerMeter,
            ShowFootprint = true,
            ShowCones = true,
            ShowMarkings = true,
            ShowTrajectory = true,
            ShowEntryExit = true,
            ShowDirectionMarkers = true,
        });
    }

    /// <summary>Clears both the displayed data and cached prepared geometry.</summary>
    public void ClearPreview()
    {
        _model.Clear();
        _geometry = null;
        QueueRedraw();
    }

    /// <summary>Displays a transient state while stale preview data is cleared before a file load.</summary>
    public void ShowLoading(string sourcePath)
    {
        _model.SetLoading(sourcePath);
        _geometry = null;
        QueueRedraw();
    }

    /// <summary>Displays and caches a freshly loaded Exercise definition.</summary>
    public void ShowExercise(
        string sourcePath,
        ExerciseDefinitionDto definition,
        IReadOnlyList<string>? warnings = null)
    {
        _model.SetReady(sourcePath, definition, warnings);
        _geometry = ExerciseGeometryRenderer.Build(definition);
        _contentBounds = ExerciseContentBoundsCalculator.Calculate(definition);
        GeometryRevision++;
        QueueRedraw();
    }

    /// <summary>Displays a non-fatal invalid-file state without stale geometry.</summary>
    public void ShowInvalid(
        string sourcePath,
        string diagnostic,
        ExercisePreviewIdentity? identity = null)
    {
        _model.SetInvalid(sourcePath, diagnostic, identity);
        _geometry = null;
        QueueRedraw();
    }

    /// <summary>Displays a non-fatal missing-file state without stale geometry.</summary>
    public void ShowMissing(string sourcePath)
    {
        _model.SetMissing(sourcePath);
        _geometry = null;
        QueueRedraw();
    }

    private void DrawStateMessage()
    {
        string text = _model.State switch
        {
            ExercisePreviewState.NoSelection => "Выберите упражнение для предпросмотра",
            ExercisePreviewState.Loading => "Loading Exercise…",
            ExercisePreviewState.InvalidExercise => ExercisePreviewMetadataFormatter.FormatInvalid(
                _model.SourcePath, ShortDiagnostic(_model.Diagnostic), _model.Identity),
            ExercisePreviewState.MissingExercise => $"Missing Exercise\n{_model.SourcePath}",
            _ => string.Empty,
        };
        Vector2 position = new(12.0f, MathF.Max(24.0f, Size.Y * 0.5f - 10.0f));
        DrawMultilineString(ThemeDB.FallbackFont, position, text,
            HorizontalAlignment.Center, MathF.Max(40.0f, Size.X - 24.0f), 13, -1,
            new Color(0.78f, 0.82f, 0.88f));
    }

    private static string ShortDiagnostic(string value)
    {
        const int maximumLength = 180;
        string singleLine = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return singleLine.Length <= maximumLength ? singleLine : singleLine[..maximumLength] + "…";
    }
}
