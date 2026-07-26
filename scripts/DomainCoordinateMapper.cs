using Godot;

namespace MotoGymkhanaTrainer.Tracks;

/// <summary>Owns the single mapping from domain X/Y metres to Godot X/Z space.</summary>
public static class DomainCoordinateMapper
{
    /// <summary>Maps a ground-level domain point into Godot world coordinates.</summary>
    public static Vector3 ToGodot(Point2Dto point, float height = 0.0f)
    {
        /*
         * Domain editors use X/right and Y/forward on a top-down plane. Godot's
         * Camera3D looks along local -Z, so mapping forward to -Z preserves the
         * editor's visual orientation: +X remains screen-right and +Y extends
         * away from the canonical starting camera toward the track.
         */
        return new Vector3(point.X, height, -point.Y);
    }
}
