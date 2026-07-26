using Godot;
using MotoGymkhanaTrainer.Tracks;

namespace MotoGymkhanaTrainer.Viewer;

/// <summary>Explicit movement modes supported by the Viewer controller.</summary>
public enum ViewerMovementMode
{
    Walk,
    Fly,
}

/// <summary>
/// Physics-backed first-person Viewer character. Walk uses CharacterBody3D
/// movement; Fly keeps free inspection without gravity or collision.
/// </summary>
public partial class FirstPersonCamera : CharacterBody3D
{
    [Export(PropertyHint.Range, "0.1,20.0,0.1")]
    public float MoveSpeed { get; set; } = 5.0f;

    [Export(PropertyHint.Range, "1.0,10.0,0.1")]
    public float ShiftMultiplier { get; set; } = 3.0f;

    [Export(PropertyHint.Range, "0.01,1.0,0.01")]
    public float MouseSensitivity { get; set; } = 0.12f;

    [Export(PropertyHint.Range, "0.25,0.45,0.01")]
    public float CapsuleRadius { get; set; } = 0.32f;

    [Export(PropertyHint.Range, "0.9,1.6,0.05")]
    public float CapsuleHeight { get; set; } = 1.2f;

    [Export(PropertyHint.Range, "1.4,2.0,0.05")]
    public float EyeHeight { get; set; } = 1.7f;

    [Export(PropertyHint.Range, "30,60,1")]
    public float MaximumFloorAngleDegrees { get; set; } = 50.0f;

    [Export(PropertyHint.Range, "0.05,0.6,0.01")]
    public float FloorSnapMeters { get; set; } = 0.3f;

    [Export(PropertyHint.Range, "1,50,0.5")]
    public float GroundAcceleration { get; set; } = 20.0f;

    [Export(PropertyHint.Range, "0.1,3.0,0.05")]
    public float GravityMultiplier { get; set; } = 1.0f;

    [Export(PropertyHint.Range, "0.01,0.2,0.01")]
    public float SpawnSurfaceClearance { get; set; } = 0.03f;

    private static readonly Vector2[] SpawnOffsets =
    [
        Vector2.Zero,
        new(0.75f, 0.0f),
        new(-0.75f, 0.0f),
        new(0.0f, 0.75f),
        new(0.0f, -0.75f),
        new(0.75f, 0.75f),
        new(-0.75f, 0.75f),
        new(0.75f, -0.75f),
        new(-0.75f, -0.75f),
    ];

    private CollisionShape3D? _collisionShape;
    private CapsuleShape3D? _capsule;
    private Node3D? _head;
    private SurfaceProjectionService? _projection;
    private float _gravity;
    private float _pitchDegrees;
    private float _yawDegrees;

    public ViewerMovementMode MovementMode { get; private set; } = ViewerMovementMode.Walk;

    /// <summary>Raised when mode changes or a requested transition is rejected.</summary>
    public event Action<ViewerMovementMode, string>? MovementStatusChanged;

    /// <inheritdoc />
    public override void _Ready()
    {
        _collisionShape = GetNode<CollisionShape3D>("CollisionShape3D");
        _head = GetNode<Node3D>("Head");
        _capsule = _collisionShape.Shape as CapsuleShape3D ??
            throw new InvalidOperationException("ViewerCharacter requires CapsuleShape3D.");

        _capsule.Radius = CapsuleRadius;
        _capsule.Height = MathF.Max(CapsuleHeight, CapsuleRadius * 2.0f);
        _collisionShape.Position = new Vector3(0.0f, _capsule.Height / 2.0f, 0.0f);
        _head.Position = new Vector3(0.0f, EyeHeight, 0.0f);

        CollisionLayer = ViewerPhysicsLayers.ViewerCharacter;
        CollisionMask = ViewerPhysicsLayers.CharacterMask;
        UpDirection = Vector3.Up;
        FloorMaxAngle = Mathf.DegToRad(MaximumFloorAngleDegrees);
        FloorSnapLength = FloorSnapMeters;
        SafeMargin = 0.04f;
        MotionMode = MotionModeEnum.Grounded;

        _gravity = (float)ProjectSettings.GetSetting(
            "physics/3d/default_gravity",
            9.8).AsDouble() * GravityMultiplier;
        _pitchDegrees = _head.RotationDegrees.X;
        _yawDegrees = RotationDegrees.Y;
        Input.MouseMode = Input.MouseModeEnum.Visible;

        // No surface exists before the first Track load. Suspending physics keeps
        // the character from falling while the file dialog is being used.
        SetPhysicsProcess(false);
    }

    /// <summary>Suspends movement while old/new Venue physics is replaced.</summary>
    public void SuspendForReload()
    {
        Velocity = Vector3.Zero;
        SetPhysicsProcess(false);
        _projection = null;
    }

    /// <summary>Installs the projection policy belonging to the active Venue.</summary>
    public void SetProjectionService(SurfaceProjectionService projection)
    {
        _projection = projection ?? throw new ArgumentNullException(nameof(projection));
    }

    /// <summary>
    /// Places the character at a safe projected start and aims it along the
    /// domain trajectory direction.
    /// </summary>
    public bool TryPlaceAtDomainStart(Point2Dto start, Point2Dto direction)
    {
        if (!TryFindSafeWalkPosition(start, "CharacterSpawn", out Vector3 safePosition))
            return false;

        GlobalPosition = safePosition;
        Vector3 worldForward = new Vector3(direction.X, 0.0f, -direction.Y).Normalized();
        if (worldForward.LengthSquared() > 0.00001f)
            _yawDegrees = Mathf.RadToDeg(Mathf.Atan2(-worldForward.X, -worldForward.Z));
        RotationDegrees = new Vector3(0.0f, _yawDegrees, 0.0f);
        _head!.RotationDegrees = new Vector3(_pitchDegrees, 0.0f, 0.0f);
        EnterWalkMode();
        SetPhysicsProcess(true);
        return true;
    }

    /// <inheritdoc />
    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("toggle_walk_fly"))
        {
            ToggleMovementMode();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (@event is InputEventMouseMotion mouseMotion &&
            Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            _yawDegrees -= mouseMotion.Relative.X * MouseSensitivity;
            _pitchDegrees = Mathf.Clamp(
                _pitchDegrees - mouseMotion.Relative.Y * MouseSensitivity,
                -85.0f,
                85.0f);
            RotationDegrees = new Vector3(0.0f, _yawDegrees, 0.0f);
            _head!.RotationDegrees = new Vector3(_pitchDegrees, 0.0f, 0.0f);
            return;
        }

        if (@event is InputEventKey key && key.Pressed && key.Keycode == Key.Escape)
        {
            Input.MouseMode = Input.MouseMode == Input.MouseModeEnum.Captured
                ? Input.MouseModeEnum.Visible
                : Input.MouseModeEnum.Captured;
        }

        if (@event is InputEventMouseButton button && button.Pressed)
            Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    /// <inheritdoc />
    public override void _PhysicsProcess(double delta)
    {
        if (MovementMode == ViewerMovementMode.Fly)
            ProcessFlyMovement((float)delta);
        else
            ProcessWalkMovement((float)delta);
    }

    private void ProcessWalkMovement(float delta)
    {
        Vector2 input = Input.GetVector(
            "move_left", "move_right", "move_forward", "move_backward");
        Vector3 right = GlobalTransform.Basis.X;
        Vector3 backward = GlobalTransform.Basis.Z;
        right.Y = 0.0f;
        backward.Y = 0.0f;
        Vector3 direction = (right.Normalized() * input.X + backward.Normalized() * input.Y)
            .Normalized();
        float speed = MoveSpeed * (Input.IsActionPressed("move_fast") ? ShiftMultiplier : 1.0f);
        Vector3 desired = direction * speed;

        Velocity = new Vector3(
            Mathf.MoveToward(Velocity.X, desired.X, GroundAcceleration * delta),
            IsOnFloor() ? MathF.Min(Velocity.Y, 0.0f) : Velocity.Y - _gravity * delta,
            Mathf.MoveToward(Velocity.Z, desired.Z, GroundAcceleration * delta));
        MoveAndSlide();
    }

    private void ProcessFlyMovement(float delta)
    {
        Vector2 input = Input.GetVector(
            "move_left", "move_right", "move_forward", "move_backward");
        float vertical = Input.GetAxis("fly_down", "fly_up");
        Basis viewBasis = _head!.GlobalTransform.Basis;
        Vector3 direction =
            viewBasis.X * input.X +
            (-viewBasis.Z) * -input.Y +
            Vector3.Up * vertical;
        if (direction.LengthSquared() > 1.0f) direction = direction.Normalized();
        float speed = MoveSpeed * (Input.IsActionPressed("move_fast") ? ShiftMultiplier : 1.0f);
        Velocity = direction * speed;

        // Fly mode deliberately ignores physics. Direct movement is confined to
        // this explicit mode; Walk always uses Velocity + MoveAndSlide.
        GlobalPosition += Velocity * delta;
    }

    private void ToggleMovementMode()
    {
        if (MovementMode == ViewerMovementMode.Walk)
        {
            MovementMode = ViewerMovementMode.Fly;
            Velocity = Vector3.Zero;
            _collisionShape!.Disabled = true;
            CollisionMask = 0;
            SetPhysicsProcess(true);
            MovementStatusChanged?.Invoke(
                MovementMode,
                "Mode: Fly (F toggles, Space/Ctrl vertical).");
            return;
        }

        Point2Dto currentDomain = new() { X = GlobalPosition.X, Y = -GlobalPosition.Z };
        if (!TryFindSafeWalkPosition(
                currentDomain,
                "WalkModeSwitch",
                out Vector3 safePosition,
                GlobalPosition.Y + FloorSnapMeters))
        {
            MovementStatusChanged?.Invoke(
                MovementMode,
                "Fly → Walk rejected: no safe walkable position below the camera.");
            return;
        }

        GlobalPosition = safePosition;
        EnterWalkMode();
        MovementStatusChanged?.Invoke(MovementMode, "Mode: Walk (F toggles Fly).");
    }

    private void EnterWalkMode()
    {
        MovementMode = ViewerMovementMode.Walk;
        _collisionShape!.Disabled = false;
        CollisionLayer = ViewerPhysicsLayers.ViewerCharacter;
        CollisionMask = ViewerPhysicsLayers.CharacterMask;
        Velocity = Vector3.Zero;
        ApplyFloorSnap();
        MovementStatusChanged?.Invoke(MovementMode, "Mode: Walk (F toggles Fly).");
    }

    private bool TryFindSafeWalkPosition(
        Point2Dto requested,
        string diagnosticSource,
        out Vector3 safePosition,
        float? rayStartY = null)
    {
        safePosition = GlobalPosition;
        if (_projection is null) return false;

        foreach (Vector2 offset in SpawnOffsets)
        {
            var candidate = new Point2Dto
            {
                X = requested.X + offset.X,
                Y = requested.Y + offset.Y,
            };
            Vector3 mapped = DomainCoordinateMapper.ToGodot(candidate);
            if (!_projection.TryProjectGodotXZ(
                    new Vector2(mapped.X, mapped.Z),
                    diagnosticSource,
                    diagnosticSource,
                    out ProjectedSurfacePoint surface,
                    visualOffset: 0.0f,
                    rayStartY: rayStartY))
                continue;

            Vector3 foot = surface.Position + surface.Normal * SpawnSurfaceClearance;
            if (CanOccupy(foot))
            {
                safePosition = foot;
                return true;
            }
        }

        return false;
    }

    private bool CanOccupy(Vector3 footPosition)
    {
        Transform3D bodyTransform = GlobalTransform;
        bodyTransform.Origin = footPosition;
        var query = new PhysicsShapeQueryParameters3D
        {
            Shape = _capsule,
            Transform = bodyTransform * _collisionShape!.Transform,
            CollisionMask = ViewerPhysicsLayers.WorldObstacle,
            CollideWithAreas = false,
            CollideWithBodies = true,
            Margin = SafeMargin,
        };
        return GetWorld3D().DirectSpaceState.IntersectShape(query, 1).Count == 0;
    }
}
