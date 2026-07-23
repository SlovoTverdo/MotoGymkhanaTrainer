using Godot;

namespace MotoGymkhanaTrainer.Tracks;

/// <summary>Owns the single mapping from domain X/Y metres to Godot X/Z space.</summary>
public static class DomainCoordinateMapper
{
    /// <summary>Maps a ground-level domain point into Godot world coordinates.</summary>
    public static Vector3 ToGodot(Point2Dto point, float height = 0.0f)
    {
        // Domain Y is distance along the flat track, while Godot Y is vertical.
        return new Vector3(point.X, height, point.Y);
    }
}

