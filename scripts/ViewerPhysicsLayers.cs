namespace MotoGymkhanaTrainer.Viewer;

/// <summary>
/// Semantic 3D physics layers shared by the Viewer controller, runtime Venue
/// construction and surface projection. Values are bit masks, not layer numbers.
/// </summary>
public static class ViewerPhysicsLayers
{
    public const uint WalkableSurface = 1u << 0;
    public const uint WorldObstacle = 1u << 1;
    public const uint TrackVisual = 1u << 2;
    public const uint ViewerCharacter = 1u << 3;

    /// <summary>Surfaces and obstacles seen by the walking character.</summary>
    public const uint CharacterMask = WalkableSurface | WorldObstacle;

    /// <summary>Only surfaces on which projected geometry may be placed.</summary>
    public const uint ProjectionMask = WalkableSurface;
}
