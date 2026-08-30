using Godot;
using MotoGymkhanaTrainer.Tracks;
using MotoGymkhanaTrainer.VenueEditor;

namespace MotoGymkhanaTrainer.TrackEditor;

/// <summary>Transient lifecycle state for the New Track Venue preview.</summary>
public enum VenuePreviewState { NoSelection, Loading, Ready, InvalidVenue, MissingVenue }

/// <summary>Combined world-space bounds used for an aspect-preserving Venue preview fit.</summary>
public readonly record struct VenueContentBounds(float MinX, float MinY, float MaxX, float MaxY)
{
    /// <summary>Width of all previewable Venue geometry in metres.</summary>
    public float Width => MaxX - MinX;
    /// <summary>Length of all previewable Venue geometry in metres.</summary>
    public float Height => MaxY - MinY;
    /// <summary>Center of all previewable Venue geometry.</summary>
    public Point2Dto Center => new() { X = (MinX + MaxX) * 0.5f, Y = (MinY + MaxY) * 0.5f };
    /// <summary>Area width retained separately because combined bounds may extend beyond it.</summary>
    public float AreaWidth { get; init; }
    /// <summary>Area length retained separately because combined bounds may extend beyond it.</summary>
    public float AreaLength { get; init; }
}

/// <summary>Uniform domain-to-screen mapping for the read-only Venue preview.</summary>
public readonly record struct VenuePreviewFit(Point2Dto Center, float PixelsPerMeter)
{
    /// <summary>Maps Venue X/Y metres to the top-down Control coordinate system.</summary>
    public Vector2 ToScreen(Point2Dto point, Vector2 viewportSize) => viewportSize * 0.5f + new Vector2(
        (point.X - Center.X) * PixelsPerMeter, -(point.Y - Center.Y) * PixelsPerMeter);
}

/// <summary>Pure state holder that keeps preview data out of Track Project history.</summary>
public sealed class VenuePreviewModel
{
    /// <summary>Current selector preview lifecycle state.</summary>
    public VenuePreviewState State { get; private set; } = VenuePreviewState.NoSelection;
    /// <summary>Freshly loaded source Venue; it is never copied into a Track document here.</summary>
    public VenueDefinitionDto? Definition { get; private set; }
    /// <summary>Current safe Venue Library relative path.</summary>
    public string SourcePath { get; private set; } = string.Empty;
    /// <summary>Short non-fatal diagnostic for invalid or missing sources.</summary>
    public string Diagnostic { get; private set; } = string.Empty;
    /// <summary>Non-blocking load and per-entity preview diagnostics.</summary>
    public IReadOnlyList<string> Warnings { get; private set; } = [];

    /// <summary>Clears all loaded data on deselection.</summary>
    public void Clear() => Set(VenuePreviewState.NoSelection, string.Empty, null, string.Empty, []);
    /// <summary>Clears stale data before loading a new selection.</summary>
    public void SetLoading(string path) => Set(VenuePreviewState.Loading, path, null, string.Empty, []);
    /// <summary>Sets a successfully loaded current source definition.</summary>
    public void SetReady(string path, VenueDefinitionDto definition, IReadOnlyList<string> warnings) =>
        Set(VenuePreviewState.Ready, path, definition, string.Empty, warnings);
    /// <summary>Sets an invalid source without retaining stale geometry.</summary>
    public void SetInvalid(string path, string diagnostic) => Set(VenuePreviewState.InvalidVenue, path, null, diagnostic, []);
    /// <summary>Sets a deleted source without retaining stale geometry.</summary>
    public void SetMissing(string path) => Set(VenuePreviewState.MissingVenue, path, null, $"Venue '{path}' больше не существует.", []);

    private void Set(VenuePreviewState state, string path, VenueDefinitionDto? definition, string diagnostic, IReadOnlyList<string> warnings)
    {
        State = state; SourcePath = path; Definition = definition; Diagnostic = diagnostic; Warnings = warnings;
    }
}

/// <summary>Formats compact source metadata for the New Track dialog.</summary>
public static class VenuePreviewMetadataFormatter
{
    /// <summary>Formats only current source data; panorama is reported but not rendered.</summary>
    public static string Format(VenueDefinitionDto definition, int warningCount = 0)
    {
        ArgumentNullException.ThrowIfNull(definition);
        int visibleMarkings = definition.Markings.Count(marking => marking.VisibleInViewer);
        return $"{definition.Venue.Name}\nID: {definition.Venue.Id}   Size: {definition.Area.Width:0.##} × {definition.Area.Length:0.##} m\n" +
            $"Objects: {definition.Objects.Length}   Cones: {definition.Cones.Length}   Visible markings: {visibleMarkings}\n" +
            $"Panorama: {(definition.Panorama.Enabled ? "available" : "not configured")}" +
            (warningCount > 0 ? $"   Warnings: {warningCount}" : string.Empty);
    }
}

/// <summary>Aspect-preserving fit with the documented 12 percent preview margin.</summary>
public static class VenuePreviewFitCalculator
{
    /// <summary>Calculates a stable fit for normal, wide, tall and off-center Venue geometry.</summary>
    public static VenuePreviewFit Calculate(VenueContentBounds bounds, Vector2 viewportSize, float marginFraction = 0.12f)
    {
        if (!float.IsFinite(viewportSize.X) || !float.IsFinite(viewportSize.Y) || viewportSize.X <= 0 || viewportSize.Y <= 0)
            throw new ArgumentOutOfRangeException(nameof(viewportSize));
        if (!float.IsFinite(marginFraction) || marginFraction < 0 || marginFraction >= 0.5f)
            throw new ArgumentOutOfRangeException(nameof(marginFraction));
        float usableWidth = MathF.Max(1.0f, viewportSize.X * (1.0f - marginFraction * 2.0f));
        float usableHeight = MathF.Max(1.0f, viewportSize.Y * (1.0f - marginFraction * 2.0f));
        float pixelsPerMeter = Math.Clamp(MathF.Min(usableWidth / MathF.Max(bounds.Width, 0.001f),
            usableHeight / MathF.Max(bounds.Height, 0.001f)), 0.01f, 400.0f);
        return new VenuePreviewFit(bounds.Center, pixelsPerMeter);
    }
}

/// <summary>Read-only top-down preview hosted by the New Track Venue selector.</summary>
public partial class VenuePreviewControl : Control
{
    private readonly VenuePreviewModel _model = new();
    private VenuePreviewGeometry? _geometry;

    /// <summary>Preview lifecycle state for the hosting dialog metadata.</summary>
    public VenuePreviewModel Model => _model;
    /// <summary>Counts data rebuilds separately from ordinary resize redraws.</summary>
    public int GeometryRevision { get; private set; }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Resized += QueueRedraw;
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.055f, 0.065f, 0.08f), true);
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.30f, 0.36f, 0.43f), false, 1.0f);
        if (_model.State != VenuePreviewState.Ready || _geometry is null) { DrawStateMessage(); return; }
        VenuePreviewFit fit = VenuePreviewFitCalculator.Calculate(_geometry.Bounds, new Vector2(MathF.Max(Size.X, 1), MathF.Max(Size.Y, 1)));
        VenuePreviewRenderer.Draw(this, _geometry, fit);
    }

    /// <summary>Clears cached source geometry on deselection.</summary>
    public void ClearPreview() { _model.Clear(); _geometry = null; QueueRedraw(); }
    /// <summary>Clears cached source geometry while a selected file is loaded.</summary>
    public void ShowLoading(string path) { _model.SetLoading(path); _geometry = null; QueueRedraw(); }
    /// <summary>Builds the preview only after the current source Venue was loaded.</summary>
    public void ShowVenue(string path, VenueDefinitionDto definition, IReadOnlyList<string>? warnings = null)
    {
        VenuePreviewGeometry geometry = VenuePreviewRenderer.Build(definition);
        _geometry = geometry;
        _geometry = geometry with { Bounds = geometry.Bounds with { AreaWidth = definition.Area.Width, AreaLength = definition.Area.Length } };
        _model.SetReady(path, definition, (warnings ?? []).Concat(_geometry.Diagnostics).ToArray());
        GeometryRevision++; QueueRedraw();
    }
    /// <summary>Displays a non-fatal invalid-source state.</summary>
    public void ShowInvalid(string path, string diagnostic) { _model.SetInvalid(path, diagnostic); _geometry = null; QueueRedraw(); }
    /// <summary>Displays a non-fatal deleted-source state.</summary>
    public void ShowMissing(string path) { _model.SetMissing(path); _geometry = null; QueueRedraw(); }

    private void DrawStateMessage()
    {
        string text = _model.State switch
        {
            VenuePreviewState.NoSelection => "Выберите площадку для предпросмотра",
            VenuePreviewState.Loading => "Loading Venue…",
            VenuePreviewState.InvalidVenue => $"Invalid Venue\n{ShortDiagnostic(_model.Diagnostic)}",
            VenuePreviewState.MissingVenue => $"Missing Venue\n{_model.SourcePath}",
            _ => string.Empty,
        };
        DrawMultilineString(ThemeDB.FallbackFont, new Vector2(12, MathF.Max(24, Size.Y * 0.5f - 10)), text,
            HorizontalAlignment.Center, MathF.Max(40, Size.X - 24), 13, -1, new Color(0.78f, 0.82f, 0.88f));
    }

    private static string ShortDiagnostic(string value)
    {
        string singleLine = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return singleLine.Length <= 180 ? singleLine : singleLine[..180] + "…";
    }
}
