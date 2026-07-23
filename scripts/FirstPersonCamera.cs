using Godot;

namespace MotoGymkhanaTrainer.Viewer;

/// <summary>Provides fixed-height WASD movement and mouse look for the Viewer.</summary>
public partial class FirstPersonCamera : Node3D
{
    [Export(PropertyHint.Range, "0.1,20.0,0.1")]
    public float MoveSpeed { get; set; } = 5.0f;

    [Export(PropertyHint.Range, "1.0,10.0,0.1")]
    public float ShiftMultiplier { get; set; } = 3.0f;

    [Export(PropertyHint.Range, "0.01,1.0,0.01")]
    public float MouseSensitivity { get; set; } = 0.12f;

    private float _fixedHeight;
    private float _pitchDegrees;
    private float _yawDegrees;

    /// <inheritdoc />
    public override void _Ready()
    {
        _fixedHeight = Position.Y;
        _pitchDegrees = RotationDegrees.X;
        _yawDegrees = RotationDegrees.Y;
        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    /// <inheritdoc />
    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseMotion mouseMotion && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            _yawDegrees -= mouseMotion.Relative.X * MouseSensitivity;
            _pitchDegrees = Mathf.Clamp(
                _pitchDegrees - mouseMotion.Relative.Y * MouseSensitivity,
                -85.0f,
                85.0f);
            RotationDegrees = new Vector3(_pitchDegrees, _yawDegrees, 0.0f);
            return;
        }

        if (@event is InputEventKey key && key.Pressed && key.Keycode == Key.Escape)
        {
            Input.MouseMode = Input.MouseMode == Input.MouseModeEnum.Captured
                ? Input.MouseModeEnum.Visible
                : Input.MouseModeEnum.Captured;
        }

        if (@event is InputEventMouseButton button && button.Pressed)
        {
            Input.MouseMode = Input.MouseModeEnum.Captured;
        }
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        Vector2 input = Input.GetVector("move_left", "move_right", "move_forward", "move_backward");

        // Use only the rig's yaw to keep movement horizontal even while looking up or down.
        float yawRadians = Mathf.DegToRad(_yawDegrees);
        Vector3 right = new(Mathf.Cos(yawRadians), 0.0f, -Mathf.Sin(yawRadians));
        Vector3 backward = new(Mathf.Sin(yawRadians), 0.0f, Mathf.Cos(yawRadians));
        Vector3 direction = (right * input.X + backward * input.Y).Normalized();

        float speed = MoveSpeed;
        if (Input.IsActionPressed("move_fast"))
        {
            speed *= ShiftMultiplier;
        }

        Vector3 position = Position + direction * speed * (float)delta;
        position.Y = _fixedHeight;
        Position = position;
    }
}

