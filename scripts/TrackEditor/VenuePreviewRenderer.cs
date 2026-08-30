using Godot;
using MotoGymkhanaTrainer.Tracks;
using MotoGymkhanaTrainer.VenueEditor;

namespace MotoGymkhanaTrainer.TrackEditor;

/// <summary>Prepared immutable geometry for the Venue selector preview.</summary>
public sealed record VenuePreviewGeometry(
    VenueContentBounds Bounds,
    IReadOnlyList<(VenueObjectInstanceDto Item, Point2Dto[] Footprint)> Objects,
    IReadOnlyList<ConeDto> Cones,
    IReadOnlyList<(MarkingDto Marking, MarkingStyleGeometry Geometry)> Markings,
    IReadOnlyList<string> Diagnostics);

/// <summary>
/// Builds and draws the Venue-specific, read-only geometry of the New Track preview.
/// It deliberately uses persisted footprints and paths, never assets, physics, or editor state.
/// </summary>
public static class VenuePreviewRenderer
{
    /// <summary>Builds cached geometry and combined bounds once per loaded definition.</summary>
    public static VenuePreviewGeometry Build(VenueDefinitionDto definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (!float.IsFinite(definition.Area.Width) || !float.IsFinite(definition.Area.Length) ||
            definition.Area.Width <= 0.0f || definition.Area.Length <= 0.0f)
            throw new InvalidDataException("Venue area width and length must be positive finite values.");

        float minX = -definition.Area.Width * 0.5f;
        float maxX = definition.Area.Width * 0.5f;
        float minY = -definition.Area.Length * 0.5f;
        float maxY = definition.Area.Length * 0.5f;
        var objects = new List<(VenueObjectInstanceDto Item, Point2Dto[] Footprint)>();
        var cones = new List<ConeDto>();
        var markings = new List<(MarkingDto Marking, MarkingStyleGeometry Geometry)>();
        var diagnostics = new List<string>();

        // A bad optional entity must not prevent the user from assessing the rest of
        // an otherwise loadable Venue. The strict store still owns document validation.
        foreach (VenueObjectInstanceDto item in definition.Objects)
        {
            try
            {
                if (!float.IsFinite(item.Footprint.Width) || !float.IsFinite(item.Footprint.Length) ||
                    item.Footprint.Width <= 0.0f || item.Footprint.Length <= 0.0f)
                    throw new InvalidDataException("Footprint dimensions must be positive finite values.");
                Point2Dto[] footprint = VenueGeometry.TransformFootprint(item);
                if (footprint.Length == 0) throw new InvalidDataException("Footprint is empty.");
                objects.Add((item, footprint));
                foreach (Point2Dto point in footprint) Include(point, ref minX, ref minY, ref maxX, ref maxY);
            }
            catch (Exception exception)
            {
                diagnostics.Add($"Object '{item.ObjectId}' skipped: {exception.Message}");
            }
        }

        foreach (ConeDto cone in definition.Cones)
        {
            if (!float.IsFinite(cone.Position.X) || !float.IsFinite(cone.Position.Y))
            {
                diagnostics.Add($"Cone '{cone.Id}' skipped: position is not finite.");
                continue;
            }
            cones.Add(cone);
            Include(cone.Position, ref minX, ref minY, ref maxX, ref maxY);
        }

        foreach (MarkingDto marking in definition.Markings)
        {
            // This selector represents the future runtime Venue, unlike the Venue
            // Editor authoring canvas where hidden markings intentionally remain visible.
            if (!marking.VisibleInViewer) continue;
            try
            {
                if (!MarkingGeometry.TryNormalizeColor(marking.Color, allowLegacyNames: false, out _))
                    throw new InvalidDataException("Color is not a canonical RGB value.");
                MarkingStyleGeometry style = MarkingGeometry.CreateStyleGeometry(
                    PathSampler.Sample(marking.Path), marking.Style);
                markings.Add((marking, style));
                PathBounds bounds = PathBoundsCalculator.Calculate(marking.Path, marking.WidthMeters);
                Include(bounds, ref minX, ref minY, ref maxX, ref maxY);
            }
            catch (Exception exception)
            {
                diagnostics.Add($"Marking '{marking.Id}' skipped: {exception.Message}");
            }
        }

        return new VenuePreviewGeometry(
            new VenueContentBounds(minX, minY, maxX, maxY), objects, cones, markings, diagnostics);
    }

    /// <summary>Draws the cached top-down Venue geometry in its documented pass order.</summary>
    public static void Draw(Control canvas, VenuePreviewGeometry geometry, VenuePreviewFit fit)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(geometry);
        Func<Point2Dto, Vector2> toScreen = point => fit.ToScreen(point, canvas.Size);
        float halfWidth = MathF.Abs(geometry.Bounds.AreaWidth) * 0.5f;
        float halfLength = MathF.Abs(geometry.Bounds.AreaLength) * 0.5f;
        Point2Dto[] area =
        [
            new() { X = -halfWidth, Y = -halfLength }, new() { X = halfWidth, Y = -halfLength },
            new() { X = halfWidth, Y = halfLength }, new() { X = -halfWidth, Y = halfLength },
        ];

        Vector2[] areaPoints = area.Select(toScreen).ToArray();
        canvas.DrawColoredPolygon(areaPoints, new Color(0.12f, 0.15f, 0.18f, 0.92f));
        DrawClosed(canvas, areaPoints, new Color(0.48f, 0.72f, 0.88f), 2.0f);
        // The fence is deliberately symbolic: the preview must not load a fence mesh.
        foreach (Vector2 corner in areaPoints) canvas.DrawCircle(corner, 3.0f, new Color(0.62f, 0.78f, 0.88f));

        foreach ((MarkingDto marking, MarkingStyleGeometry style) in geometry.Markings)
        {
            Color color = ResolveColor(marking.Color);
            float width = MathF.Max(1.0f, marking.WidthMeters * fit.PixelsPerMeter);
            foreach (MarkingStroke stroke in style.Strokes)
                canvas.DrawLine(toScreen(stroke.Start), toScreen(stroke.End), color, width, true);
            foreach (Point2Dto dot in style.Dots)
                canvas.DrawCircle(toScreen(dot), MathF.Max(1.0f, width * 0.5f), color);
        }

        foreach ((VenueObjectInstanceDto item, Point2Dto[] footprint) in geometry.Objects)
        {
            Color color = item.VisibleInViewer
                ? new Color(0.66f, 0.60f, 0.38f, 0.94f)
                : new Color(0.46f, 0.46f, 0.50f, 0.72f);
            DrawClosed(canvas, footprint.Select(toScreen).ToArray(), color, 2.0f);
        }

        foreach (ConeDto cone in geometry.Cones)
        {
            Vector2 position = toScreen(cone.Position);
            canvas.DrawCircle(position, 4.5f, ResolveConeColor(cone.Color));
            canvas.DrawArc(position, 6.5f, 0.0f, MathF.Tau, 16, new Color(0.8f, 0.8f, 0.8f, 0.75f), 1.0f);
        }
    }

    private static void Include(Point2Dto point, ref float minX, ref float minY, ref float maxX, ref float maxY)
    {
        minX = MathF.Min(minX, point.X); minY = MathF.Min(minY, point.Y);
        maxX = MathF.Max(maxX, point.X); maxY = MathF.Max(maxY, point.Y);
    }

    private static void Include(PathBounds bounds, ref float minX, ref float minY, ref float maxX, ref float maxY)
    {
        minX = MathF.Min(minX, bounds.MinX); minY = MathF.Min(minY, bounds.MinY);
        maxX = MathF.Max(maxX, bounds.MaxX); maxY = MathF.Max(maxY, bounds.MaxY);
    }

    private static void DrawClosed(Control canvas, IReadOnlyList<Vector2> points, Color color, float width)
    {
        for (int index = 0; index < points.Count; index++)
            canvas.DrawLine(points[index], points[(index + 1) % points.Count], color, width, true);
    }

    private static Color ResolveColor(string? color)
    {
        try { return Color.FromHtml(string.IsNullOrWhiteSpace(color) ? "#FFFFFF" : color); }
        catch (Exception) { return Colors.Magenta; }
    }

    private static Color ResolveConeColor(string? color) => (color ?? string.Empty).ToLowerInvariant() switch
    {
        "blue" => new Color(0.18f, 0.55f, 1.0f),
        "yellow" => new Color(1.0f, 0.82f, 0.16f),
        "white" => new Color(0.92f, 0.92f, 0.92f),
        _ => new Color(1.0f, 0.38f, 0.08f),
    };
}
