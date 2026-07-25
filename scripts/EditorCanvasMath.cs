using Godot;
using MotoGymkhanaTrainer.Tracks;

namespace MotoGymkhanaTrainer;

/// <summary>Shared 2D editor mapping between domain metres and Control pixels.</summary>
public static class EditorCanvasMath
{
    /// <summary>Maps local X/right and Y/up metres into Godot canvas coordinates.</summary>
    public static Vector2 DomainToScreen(
        Point2Dto point,
        Vector2 viewportSize,
        Vector2 panPixels,
        float pixelsPerMeter)
    {
        // Canvas Y grows downward, while domain Y grows upward. Pan is UI state
        // in pixels and therefore never appears in either editor JSON contract.
        return viewportSize * 0.5f + panPixels +
            new Vector2(point.X * pixelsPerMeter, -point.Y * pixelsPerMeter);
    }

    /// <summary>Converts a pointer position back to local domain metres.</summary>
    public static Point2Dto ScreenToDomain(
        Vector2 screen,
        Vector2 viewportSize,
        Vector2 panPixels,
        float pixelsPerMeter)
    {
        Vector2 relative = screen - viewportSize * 0.5f - panPixels;
        return new Point2Dto { X = relative.X / pixelsPerMeter, Y = -relative.Y / pixelsPerMeter };
    }

    /// <summary>Rounds a domain point to a stable editor grid step.</summary>
    public static Point2Dto Snap(Point2Dto point, float stepMeters)
    {
        if (!float.IsFinite(stepMeters) || stepMeters <= 0.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(stepMeters));
        }

        return new Point2Dto
        {
            X = MathF.Round(point.X / stepMeters) * stepMeters,
            Y = MathF.Round(point.Y / stepMeters) * stepMeters,
        };
    }

    /// <summary>
    /// Returns the adjusted pan that keeps the domain point under the mouse fixed
    /// while pixels-per-metre changes.
    /// </summary>
    public static Vector2 ZoomAt(
        Vector2 screen,
        Vector2 viewportSize,
        Vector2 panPixels,
        float oldPixelsPerMeter,
        float newPixelsPerMeter)
    {
        Point2Dto fixedDomain = ScreenToDomain(screen, viewportSize, panPixels, oldPixelsPerMeter);
        Vector2 newOffset = new(fixedDomain.X * newPixelsPerMeter, -fixedDomain.Y * newPixelsPerMeter);
        return screen - viewportSize * 0.5f - newOffset;
    }

    /// <summary>Screen-space distance used by zoom-independent hit testing.</summary>
    public static float DistanceToSegment(Vector2 point, Vector2 start, Vector2 end)
    {
        Vector2 delta = end - start;
        float lengthSquared = delta.LengthSquared();
        if (lengthSquared <= float.Epsilon)
        {
            return point.DistanceTo(start);
        }

        float amount = Mathf.Clamp((point - start).Dot(delta) / lengthSquared, 0.0f, 1.0f);
        return point.DistanceTo(start + delta * amount);
    }
}
