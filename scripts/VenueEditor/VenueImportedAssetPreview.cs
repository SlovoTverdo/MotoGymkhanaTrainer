using Godot;

namespace MotoGymkhanaTrainer.VenueEditor;

/// <summary>Small neutral-lighting preview of the real managed PackedScene.</summary>
public partial class VenueImportedAssetPreview : SubViewportContainer
{
    private readonly SubViewport _viewport = new()
    {
        Size = new Vector2I(320, 220),
        TransparentBg = false,
        RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
    };
    private readonly Node3D _content = new() { Name = "PreviewContent" };
    private readonly Node3D _collisionOverlay = new() { Name = "CollisionOverlay" };
    private readonly Camera3D _camera = new() { Current = true };
    private Node3D? _model;
    private VenueImportedAssetMetadata? _metadata;

    public override void _Ready()
    {
        Stretch = true;
        CustomMinimumSize = new Vector2(300, 210);
        AddChild(_viewport);
        _viewport.AddChild(_content);
        _content.AddChild(_collisionOverlay);
        _viewport.AddChild(_camera);
        var key = new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-55, -35, 0),
            LightEnergy = 1.15f,
            ShadowEnabled = true,
        };
        _viewport.AddChild(key);
        _viewport.AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-25, 145, 0),
            LightEnergy = 0.35f,
        });
        var ground = new MeshInstance3D
        {
            Name = "Ground",
            Mesh = new PlaneMesh { Size = new Vector2(20, 20) },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color("596169"),
                Roughness = 1,
            },
        };
        _viewport.AddChild(ground);
    }

    /// <summary>Loads the exact wrapper used by Venue instances and desktop Viewer.</summary>
    public void ShowAsset(VenueImportedAssetMetadata? metadata, bool showCollision)
    {
        ClearModel();
        _metadata = metadata;
        if (metadata is null) return;
        PackedScene? scene = ResourceLoader.Load<PackedScene>(metadata.RuntimeScenePath);
        if (scene?.Instantiate() is not Node3D model)
        {
            GD.PushWarning($"Imported asset preview cannot load '{metadata.RuntimeScenePath}'.");
            return;
        }
        _model = model;
        _content.AddChild(model);
        BuildCollisionOverlay(model);
        _collisionOverlay.Visible = showCollision;
        Frame(metadata.Bounds);
    }

    public void SetCollisionVisible(bool visible) => _collisionOverlay.Visible = visible;

    private void Frame(VenueImportedBoundsMetadata bounds)
    {
        Vector3 minimum = new(bounds.MinX, bounds.MinY, bounds.MinZ);
        Vector3 maximum = new(bounds.MaxX, bounds.MaxY, bounds.MaxZ);
        Vector3 center = (minimum + maximum) * 0.5f;
        float radius = MathF.Max(0.5f, (maximum - minimum).Length() * 0.6f);
        _camera.Position = center + new Vector3(radius * 1.35f, radius * 0.9f, radius * 1.35f);
        _camera.Near = MathF.Max(0.01f, radius * 0.01f);
        _camera.Far = MathF.Max(100, radius * 10);
        _camera.LookAt(center, Vector3.Up);
    }

    private void BuildCollisionOverlay(Node root)
    {
        var material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = new Color(1.0f, 0.25f, 0.1f, 0.42f),
            NoDepthTest = true,
        };
        foreach (CollisionShape3D collision in Enumerate(root).OfType<CollisionShape3D>())
        {
            if (collision.Shape is null) continue;
            Mesh? debugMesh = collision.Shape.GetDebugMesh();
            if (debugMesh is null) continue;
            var visual = new MeshInstance3D
            {
                Mesh = debugMesh,
                Transform = TransformToRoot(collision, root as Node3D ?? _model!),
                MaterialOverride = material,
            };
            _collisionOverlay.AddChild(visual);
        }
    }

    private void ClearModel()
    {
        foreach (Node child in _collisionOverlay.GetChildren()) child.QueueFree();
        if (_model is not null)
        {
            _content.RemoveChild(_model);
            _model.QueueFree();
            _model = null;
        }
    }

    private static IEnumerable<Node> Enumerate(Node root)
    {
        yield return root;
        foreach (Node child in root.GetChildren())
            foreach (Node nested in Enumerate(child)) yield return nested;
    }

    private static Transform3D TransformToRoot(Node3D node, Node3D root)
    {
        Transform3D result = node.Transform;
        Node? ancestor = node.GetParent();
        while (ancestor is not null && ancestor != root)
        {
            if (ancestor is Node3D spatial) result = spatial.Transform * result;
            ancestor = ancestor.GetParent();
        }
        return result;
    }
}
