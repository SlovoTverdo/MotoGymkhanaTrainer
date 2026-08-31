using Godot;

namespace MotoGymkhanaTrainer.VenueEditor;

/// <summary>
/// One visual AABB together with the transform that places it in the asset
/// scene root's local coordinate system.
/// </summary>
public readonly record struct VenueAssetVisualBounds(Aabb Bounds, Transform3D ToAssetRoot);

/// <summary>Measures the top-down X/Z footprint of an authored PackedScene.</summary>
public static class VenueAssetFootprint
{
    /// <summary>
    /// Instantiates an asset temporarily and combines every VisualInstance3D
    /// AABB in root-local coordinates. Collision shapes are intentionally not
    /// used because footprint represents the visible asset extent, not physics.
    /// </summary>
    public static FootprintDto Measure(PackedScene scene, string assetPath)
    {
        Node instance = scene.Instantiate();
        try
        {
            if (instance is not Node3D root)
            {
                throw new InvalidDataException(
                    $"Venue asset '{assetPath}' root must be Node3D to determine its footprint.");
            }

            VenueAssetVisualBounds[] visuals = EnumerateSelfAndDescendants(root)
                .OfType<VisualInstance3D>()
                .Select(visual => new VenueAssetVisualBounds(
                    visual.GetAabb(),
                    TransformToRoot(visual, root)))
                .ToArray();
            return Calculate(visuals, assetPath);
        }
        finally
        {
            // The measurement instance never enters the scene tree and must not
            // survive as hidden editor/runtime state.
            instance.Free();
        }
    }

    /// <summary>
    /// Projects transformed AABB corners onto asset-local X/Z and returns their
    /// combined dimensions in metres. Exposed separately for deterministic tests.
    /// </summary>
    public static FootprintDto Calculate(
        IEnumerable<VenueAssetVisualBounds> visuals,
        string assetPath)
    {
        bool found = false;
        float minimumX = float.PositiveInfinity;
        float maximumX = float.NegativeInfinity;
        float minimumZ = float.PositiveInfinity;
        float maximumZ = float.NegativeInfinity;

        foreach (VenueAssetVisualBounds visual in visuals)
        {
            Vector3 position = visual.Bounds.Position;
            Vector3 size = visual.Bounds.Size;
            for (int corner = 0; corner < 8; corner++)
            {
                var local = new Vector3(
                    position.X + ((corner & 1) == 0 ? 0.0f : size.X),
                    position.Y + ((corner & 2) == 0 ? 0.0f : size.Y),
                    position.Z + ((corner & 4) == 0 ? 0.0f : size.Z));
                Vector3 rootLocal = visual.ToAssetRoot * local;
                if (!IsFinite(rootLocal))
                {
                    throw new InvalidDataException(
                        $"Venue asset '{assetPath}' produces non-finite visual bounds.");
                }

                found = true;
                minimumX = MathF.Min(minimumX, rootLocal.X);
                maximumX = MathF.Max(maximumX, rootLocal.X);
                minimumZ = MathF.Min(minimumZ, rootLocal.Z);
                maximumZ = MathF.Max(maximumZ, rootLocal.Z);
            }
        }

        float width = maximumX - minimumX;
        float length = maximumZ - minimumZ;
        if (!found || !float.IsFinite(width) || !float.IsFinite(length) ||
            width <= 0.0f || length <= 0.0f)
        {
            throw new InvalidDataException(
                $"Venue asset '{assetPath}' has no finite positive VisualInstance3D extent on X/Z; " +
                "its footprint cannot be determined automatically.");
        }

        return new FootprintDto
        {
            Width = width,
            Length = length,
            CenterX = (minimumX + maximumX) * 0.5f,
            CenterY = (minimumZ + maximumZ) * 0.5f,
        };
    }

    private static IEnumerable<Node> EnumerateSelfAndDescendants(Node root)
    {
        yield return root;
        foreach (Node child in root.GetChildren())
            foreach (Node nested in EnumerateSelfAndDescendants(child))
                yield return nested;
    }

    private static Transform3D TransformToRoot(Node3D visual, Node3D root)
    {
        if (visual == root) return Transform3D.Identity;

        Transform3D result = visual.Transform;
        Node? ancestor = visual.GetParent();
        while (ancestor is not null && ancestor != root)
        {
            if (ancestor is Node3D spatial) result = spatial.Transform * result;
            ancestor = ancestor.GetParent();
        }

        if (ancestor != root)
            throw new InvalidDataException("VisualInstance3D is not a descendant of the asset root.");
        return result;
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
