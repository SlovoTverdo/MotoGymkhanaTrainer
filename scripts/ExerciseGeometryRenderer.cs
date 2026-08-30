using Godot;
using MotoGymkhanaTrainer.ExerciseEditor;
using MotoGymkhanaTrainer.Tracks;

namespace MotoGymkhanaTrainer;

/// <summary>Read-only display switches shared by Exercise previews and Track instances.</summary>
public sealed class ExerciseGeometryRenderOptions
{
    /// <summary>Whether to draw the Exercise footprint.</summary>
    public bool ShowFootprint { get; init; } = true;
    /// <summary>Whether to draw cones.</summary>
    public bool ShowCones { get; init; } = true;
    /// <summary>Whether to draw sampled markings.</summary>
    public bool ShowMarkings { get; init; } = true;
    /// <summary>Whether to draw the authored trajectory.</summary>
    public bool ShowTrajectory { get; init; } = true;
    /// <summary>Whether to draw entry and exit glyphs.</summary>
    public bool ShowEntryExit { get; init; } = true;
    /// <summary>Whether trajectory strokes include direction arrows.</summary>
    public bool ShowDirectionMarkers { get; init; } = true;
    /// <summary>Uniform domain-to-screen scale used for metre-based stroke widths.</summary>
    public float PixelsPerMeter { get; init; } = 12.0f;
    /// <summary>Opacity applied without mutating source colors.</summary>
    public float Opacity { get; init; } = 1.0f;
    /// <summary>Screen-space cone glyph radius.</summary>
    public float ConeRadiusPixels { get; init; } = 6.0f;
    /// <summary>Whether cone glyphs receive a contrasting outline.</summary>
    public bool ShowConeOutline { get; init; } = true;
    /// <summary>Color used for the read-only trajectory layer.</summary>
    public Color TrajectoryColor { get; init; } = new(0.12f, 0.9f, 0.95f);
}

/// <summary>One sampled marking prepared when Exercise data changes, not when a panel resizes.</summary>
public sealed record ExerciseMarkingRenderData(
    string Color,
    float WidthMeters,
    bool VisibleInViewer,
    MarkingStyleGeometry Geometry);

/// <summary>Immutable read-only Exercise geometry in one caller-selected domain coordinate system.</summary>
public sealed class ExerciseGeometryRenderData
{
    /// <summary>Exercise footprint corners in caller-selected coordinates.</summary>
    public required Point2Dto[] Footprint { get; init; }
    /// <summary>Sampled and styled marking geometry.</summary>
    public required ExerciseMarkingRenderData[] Markings { get; init; }
    /// <summary>Copied cones in caller-selected coordinates.</summary>
    public required ConeDto[] Cones { get; init; }
    /// <summary>Sampled trajectory polylines.</summary>
    public required Point2Dto[][] TrajectoryLines { get; init; }
    /// <summary>Entry point in caller-selected coordinates.</summary>
    public required Point2Dto Entry { get; init; }
    /// <summary>Exit point in caller-selected coordinates.</summary>
    public required Point2Dto Exit { get; init; }
}

/// <summary>
/// Builds and draws read-only Exercise geometry without owning selection, input,
/// handles, documents, or history.
/// </summary>
public static class ExerciseGeometryRenderer
{
    private const int CubicTrajectorySubdivisions = 32;

    /// <summary>
    /// Prepares all sampled geometry. Transform is applied to control geometry
    /// before curved marking sampling so non-uniform Track scale remains correct.
    /// </summary>
    public static ExerciseGeometryRenderData Build(
        ExerciseDefinitionDto definition,
        Func<Point2Dto, Point2Dto>? transform = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        transform ??= CopyPoint;

        float halfWidth = definition.Bounds.Width * 0.5f;
        float halfLength = definition.Bounds.Length * 0.5f;
        Point2Dto[] footprint =
        [
            transform(new Point2Dto { X = -halfWidth, Y = -halfLength }),
            transform(new Point2Dto { X = halfWidth, Y = -halfLength }),
            transform(new Point2Dto { X = halfWidth, Y = halfLength }),
            transform(new Point2Dto { X = -halfWidth, Y = halfLength }),
        ];

        ExerciseMarkingRenderData[] markings = definition.Markings.Select(marking =>
        {
            PathDefinition path = PathTransformService.Transform(marking.Path, transform);
            return new ExerciseMarkingRenderData(
                marking.Color,
                marking.WidthMeters,
                marking.VisibleInViewer,
                MarkingGeometry.CreateStyleGeometry(PathSampler.Sample(path), marking.Style));
        }).ToArray();

        ConeDto[] cones = definition.Cones.Select(cone => new ConeDto
        {
            Id = cone.Id,
            Position = transform(cone.Position),
            Color = cone.Color,
            Type = cone.Type,
        }).ToArray();

        Point2Dto[][] trajectory = definition.Trajectory.Segments.Select(segment =>
        {
            if (segment.Type == "polyline")
            {
                return segment.Points!.Select(transform).ToArray();
            }

            var transformed = new TrajectorySegmentDto
            {
                Id = segment.Id,
                Type = "cubicBezier",
                Start = transform(segment.Start!),
                Control1 = transform(segment.Control1!),
                Control2 = transform(segment.Control2!),
                End = transform(segment.End!),
            };
            return TrajectoryGeometry.SampleCubicBezier(transformed, CubicTrajectorySubdivisions);
        }).ToArray();

        return new ExerciseGeometryRenderData
        {
            Footprint = footprint,
            Markings = markings,
            Cones = cones,
            TrajectoryLines = trajectory,
            Entry = transform(definition.EntryPoint),
            Exit = transform(definition.ExitPoint),
        };
    }

    /// <summary>Draws selected read-only layers using a caller-owned world-to-screen mapping.</summary>
    public static void Draw(
        CanvasItem canvas,
        ExerciseGeometryRenderData geometry,
        Func<Point2Dto, Vector2> toScreen,
        ExerciseGeometryRenderOptions options)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(toScreen);
        ArgumentNullException.ThrowIfNull(options);

        if (options.ShowFootprint) DrawFootprint(canvas, geometry, toScreen, options.Opacity);
        if (options.ShowMarkings) DrawMarkings(canvas, geometry, toScreen, options);
        if (options.ShowCones) DrawCones(canvas, geometry, toScreen, options);
        if (options.ShowTrajectory) DrawTrajectory(canvas, geometry, toScreen, options);
        if (options.ShowEntryExit) DrawEntryExit(canvas, geometry, toScreen, options.Opacity);
    }

    /// <summary>Draws the prepared footprint fill and outline.</summary>
    public static void DrawFootprint(
        CanvasItem canvas,
        ExerciseGeometryRenderData geometry,
        Func<Point2Dto, Vector2> toScreen,
        float opacity = 1.0f)
    {
        Vector2[] points = geometry.Footprint.Select(toScreen).ToArray();
        Color fill = new(0.95f, 0.83f, 0.25f, 0.08f * opacity);
        Color outline = new(0.95f, 0.83f, 0.25f, 0.9f * opacity);
        canvas.DrawColoredPolygon(points, fill);
        DrawClosedPolyline(canvas, points, outline, 2.0f);
    }

    /// <summary>Draws solid, dashed, and dotted prepared markings.</summary>
    public static void DrawMarkings(
        CanvasItem canvas,
        ExerciseGeometryRenderData geometry,
        Func<Point2Dto, Vector2> toScreen,
        ExerciseGeometryRenderOptions options)
    {
        foreach (ExerciseMarkingRenderData marking in geometry.Markings)
        {
            Color color = ResolveMarkingColor(marking.Color);
            color.A *= marking.VisibleInViewer ? options.Opacity : options.Opacity * 0.32f;
            float widthPixels = MathF.Max(1.0f, marking.WidthMeters * options.PixelsPerMeter);
            foreach (MarkingStroke stroke in marking.Geometry.Strokes)
                canvas.DrawLine(toScreen(stroke.Start), toScreen(stroke.End), color, widthPixels, true);
            foreach (Point2Dto dot in marking.Geometry.Dots)
                canvas.DrawCircle(toScreen(dot), MathF.Max(1.0f, widthPixels * 0.5f), color);
        }
    }

    /// <summary>Draws cones as screen-space editor glyphs.</summary>
    public static void DrawCones(
        CanvasItem canvas,
        ExerciseGeometryRenderData geometry,
        Func<Point2Dto, Vector2> toScreen,
        ExerciseGeometryRenderOptions options)
    {
        foreach (ConeDto cone in geometry.Cones)
        {
            Vector2 center = toScreen(cone.Position);
            Color color = ResolveConeColor(cone.Color);
            color.A *= options.Opacity;
            // Cone dimensions are not changed in domain data. This minimum is an
            // editor glyph size that keeps a valid cone legible after auto-fit.
            canvas.DrawCircle(center, options.ConeRadiusPixels, color);
            if (options.ShowConeOutline)
            {
                canvas.DrawArc(center, options.ConeRadiusPixels, 0.0f, MathF.Tau, 20,
                    new Color(0.03f, 0.03f, 0.03f, 0.9f * options.Opacity), 1.5f);
            }
        }
    }

    /// <summary>Draws sampled trajectory strokes and optional direction markers.</summary>
    public static void DrawTrajectory(
        CanvasItem canvas,
        ExerciseGeometryRenderData geometry,
        Func<Point2Dto, Vector2> toScreen,
        ExerciseGeometryRenderOptions options)
    {
        Color color = options.TrajectoryColor;
        color.A *= options.Opacity;
        foreach (Point2Dto[] line in geometry.TrajectoryLines)
        {
            if (line.Length < 2) continue;
            Vector2[] screen = line.Select(toScreen).ToArray();
            canvas.DrawPolyline(screen, color, 3.0f, true);
            if (options.ShowDirectionMarkers)
            {
                int middle = Math.Max(0, (screen.Length - 1) / 2);
                DrawDirectionMarker(canvas, screen[middle], screen[Math.Min(middle + 1, screen.Length - 1)], options.Opacity);
            }
        }
    }

    /// <summary>Draws distinct entry and exit glyphs.</summary>
    public static void DrawEntryExit(
        CanvasItem canvas,
        ExerciseGeometryRenderData geometry,
        Func<Point2Dto, Vector2> toScreen,
        float opacity = 1.0f)
    {
        DrawEndpoint(canvas, toScreen(geometry.Entry), "IN", new Color(0.2f, 0.95f, 0.38f, opacity));
        DrawEndpoint(canvas, toScreen(geometry.Exit), "OUT", new Color(0.95f, 0.3f, 0.78f, opacity));
    }

    private static void DrawEndpoint(CanvasItem canvas, Vector2 center, string label, Color color)
    {
        Vector2[] diamond =
        [
            center + new Vector2(0.0f, -8.0f),
            center + new Vector2(8.0f, 0.0f),
            center + new Vector2(0.0f, 8.0f),
            center + new Vector2(-8.0f, 0.0f),
        ];
        canvas.DrawColoredPolygon(diamond, color);
        canvas.DrawPolyline([.. diamond, diamond[0]], Colors.Black, 1.5f, true);
        canvas.DrawString(ThemeDB.FallbackFont, center + new Vector2(11.0f, 4.0f), label,
            HorizontalAlignment.Left, -1.0f, 11, color);
    }

    private static void DrawDirectionMarker(CanvasItem canvas, Vector2 start, Vector2 end, float opacity)
    {
        Vector2 delta = end - start;
        if (delta.Length() < 2.0f) return;
        Vector2 direction = delta.Normalized();
        Vector2 perpendicular = new(-direction.Y, direction.X);
        Vector2 tip = (start + end) * 0.5f + direction * 5.0f;
        Vector2 tail = tip - direction * 10.0f;
        Color color = new(0.03f, 0.18f, 0.22f, opacity);
        canvas.DrawLine(tail + perpendicular * 4.0f, tip, color, 2.0f, true);
        canvas.DrawLine(tail - perpendicular * 4.0f, tip, color, 2.0f, true);
    }

    private static void DrawClosedPolyline(CanvasItem canvas, IReadOnlyList<Vector2> points, Color color, float width)
    {
        for (int index = 0; index < points.Count; index++)
            canvas.DrawLine(points[index], points[(index + 1) % points.Count], color, width, true);
    }

    private static Point2Dto CopyPoint(Point2Dto point) => new() { X = point.X, Y = point.Y };

    private static Color ResolveConeColor(string color) => color switch
    {
        "blue" => new Color(0.18f, 0.45f, 1.0f),
        "yellow" => new Color(1.0f, 0.82f, 0.12f),
        "orange" => new Color(1.0f, 0.42f, 0.08f),
        "none" => new Color(0.82f, 0.34f, 0.08f),
        _ => new Color(0.95f, 0.18f, 0.14f),
    };

    private static Color ResolveMarkingColor(string value)
    {
        if (!MarkingGeometry.TryNormalizeColor(value, allowLegacyNames: true, out string canonical))
            return Colors.White;
        return new Color(canonical.TrimStart('#'));
    }
}
