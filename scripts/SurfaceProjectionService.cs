using Godot;
using MotoGymkhanaTrainer.Tracks;

namespace MotoGymkhanaTrainer.Viewer;

/// <summary>One runtime-only point resolved against Venue walkable collision.</summary>
public readonly record struct ProjectedSurfacePoint(Vector3 Position, Vector3 Normal, bool Hit);

/// <summary>
/// Owns the Viewer downward-ray policy and grouped fallback diagnostics.
/// Projected heights are derived runtime state and are never written to DTOs.
/// </summary>
public sealed class SurfaceProjectionService
{
    private readonly World3D _world;
    private readonly HashSet<string> _warnedSources = new(StringComparer.Ordinal);
    private readonly List<string> _diagnostics = [];

    public SurfaceProjectionService(
        World3D world,
        float projectionTopY = 50.0f,
        float projectionBottomY = -10.0f,
        float surfaceVisualOffset = 0.03f,
        float fallbackY = 0.0f)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        if (!float.IsFinite(projectionTopY) || !float.IsFinite(projectionBottomY) ||
            projectionTopY <= projectionBottomY)
            throw new ArgumentOutOfRangeException(
                nameof(projectionTopY), "Projection top must be finite and above projection bottom.");
        if (!float.IsFinite(surfaceVisualOffset) || surfaceVisualOffset < 0.0f)
            throw new ArgumentOutOfRangeException(nameof(surfaceVisualOffset));
        if (!float.IsFinite(fallbackY))
            throw new ArgumentOutOfRangeException(nameof(fallbackY));

        ProjectionTopY = projectionTopY;
        ProjectionBottomY = projectionBottomY;
        SurfaceVisualOffset = surfaceVisualOffset;
        FallbackY = fallbackY;
    }

    public float ProjectionTopY { get; }
    public float ProjectionBottomY { get; }
    public float SurfaceVisualOffset { get; }
    public float FallbackY { get; }
    public IReadOnlyList<string> Diagnostics => _diagnostics;

    /// <summary>Projects a domain X/Y point onto the highest walkable X/Z surface.</summary>
    public bool TryProjectPoint(
        Point2Dto point,
        string sourceType,
        string sourceId,
        out ProjectedSurfacePoint projected,
        float? visualOffset = null)
    {
        Vector3 godot = DomainCoordinateMapper.ToGodot(point);
        return TryProjectGodotXZ(
            new Vector2(godot.X, godot.Z),
            sourceType,
            sourceId,
            out projected,
            visualOffset);
    }

    /// <summary>Projects an already mapped Godot X/Z coordinate.</summary>
    public bool TryProjectGodotXZ(
        Vector2 godotXZ,
        string sourceType,
        string sourceId,
        out ProjectedSurfacePoint projected,
        float? visualOffset = null,
        float? rayStartY = null)
    {
        float offset = visualOffset ?? SurfaceVisualOffset;
        float topY = rayStartY ?? ProjectionTopY;
        if (!float.IsFinite(topY) || topY <= ProjectionBottomY)
            throw new ArgumentOutOfRangeException(nameof(rayStartY));
        var from = new Vector3(godotXZ.X, topY, godotXZ.Y);
        var to = new Vector3(godotXZ.X, ProjectionBottomY, godotXZ.Y);
        PhysicsRayQueryParameters3D query = PhysicsRayQueryParameters3D.Create(
            from,
            to,
            ViewerPhysicsLayers.ProjectionMask);
        query.CollideWithAreas = false;
        query.CollideWithBodies = true;
        query.HitFromInside = false;

        Godot.Collections.Dictionary hit = _world.DirectSpaceState.IntersectRay(query);
        if (hit.Count > 0)
        {
            Vector3 position = hit["position"].AsVector3();
            Vector3 normal = hit["normal"].AsVector3().Normalized();
            projected = new ProjectedSurfacePoint(position + normal * offset, normal, true);
            return true;
        }

        projected = new ProjectedSurfacePoint(
            new Vector3(godotXZ.X, FallbackY + offset, godotXZ.Y),
            Vector3.Up,
            false);
        WarnOnce(sourceType, sourceId, godotXZ);
        return false;
    }

    /// <summary>
    /// Subdivides and projects an entire source path. One missing-hit warning is
    /// emitted per source rather than once for every generated sample.
    /// </summary>
    public ProjectedSurfacePoint[] ProjectPolyline(
        IReadOnlyList<Point2Dto> points,
        string sourceType,
        string sourceId,
        float maximumSpacingMeters,
        float? visualOffset = null)
    {
        Point2Dto[] samples = SubdividePolyline(points, maximumSpacingMeters);
        var result = new ProjectedSurfacePoint[samples.Length];
        for (int index = 0; index < samples.Length; index++)
            TryProjectPoint(
                samples[index], sourceType, sourceId, out result[index], visualOffset);
        return result;
    }

    /// <summary>Projects a cone pivot using one centralized base-offset policy.</summary>
    public Vector3 ProjectConePosition(Point2Dto point, string coneId, float baseOffset = 0.005f)
    {
        TryProjectPoint(point, "Cone", coneId, out ProjectedSurfacePoint projected, baseOffset);
        return projected.Position;
    }

    /// <summary>
    /// Adds uniform samples so no resulting interval exceeds the requested
    /// distance. Persisted source points are never modified.
    /// </summary>
    public static Point2Dto[] SubdividePolyline(
        IReadOnlyList<Point2Dto> points,
        float maximumSpacingMeters)
    {
        if (!float.IsFinite(maximumSpacingMeters) || maximumSpacingMeters <= 0.0f)
            throw new ArgumentOutOfRangeException(nameof(maximumSpacingMeters));
        if (points.Count == 0) return [];
        if (points.Count == 1)
            return [new Point2Dto { X = points[0].X, Y = points[0].Y }];

        var samples = new List<Point2Dto> { new() { X = points[0].X, Y = points[0].Y } };
        for (int index = 0; index < points.Count - 1; index++)
        {
            Point2Dto start = points[index];
            Point2Dto end = points[index + 1];
            float deltaX = end.X - start.X;
            float deltaY = end.Y - start.Y;
            float length = MathF.Sqrt(deltaX * deltaX + deltaY * deltaY);
            if (!float.IsFinite(length))
                throw new InvalidDataException("Projection source contains non-finite geometry.");
            if (length <= float.Epsilon) continue;

            int intervalCount = Math.Max(1, (int)MathF.Ceiling(length / maximumSpacingMeters));
            for (int interval = 1; interval <= intervalCount; interval++)
            {
                float amount = interval / (float)intervalCount;
                samples.Add(new Point2Dto
                {
                    X = start.X + deltaX * amount,
                    Y = start.Y + deltaY * amount,
                });
            }
        }

        return [.. samples];
    }

    private void WarnOnce(string sourceType, string sourceId, Vector2 godotXZ)
    {
        string safeType = string.IsNullOrWhiteSpace(sourceType) ? "Geometry" : sourceType;
        string safeId = string.IsNullOrWhiteSpace(sourceId) ? "<unknown>" : sourceId;
        if (!_warnedSources.Add($"{safeType}:{safeId}")) return;

        string message =
            $"{safeType} '{safeId}' could not find WalkableSurface at domain " +
            $"X={godotXZ.X:0.###}, Y={-godotXZ.Y:0.###}; fallback Y={FallbackY:0.###} was used.";
        _diagnostics.Add(message);
        GD.PushWarning(message);
    }
}
