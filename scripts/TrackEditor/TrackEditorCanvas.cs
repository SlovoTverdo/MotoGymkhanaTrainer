using Godot;
using MotoGymkhanaTrainer.ExerciseEditor;
using MotoGymkhanaTrainer.Tracks;
using MotoGymkhanaTrainer.VenueEditor;

namespace MotoGymkhanaTrainer.TrackEditor;

/// <summary>Top-down Track Project canvas; all rendered geometry is derived on demand.</summary>
public partial class TrackEditorCanvas : Control
{
    private enum ManipulationMode
    {
        None,
        Move,
        ResizeX,
        ResizeY,
        Rotate,
        TransitionControl1,
        TransitionControl2,
    }

    private readonly record struct HandleHit(string InstanceId, ManipulationMode Mode, CursorShape Cursor);
    private readonly record struct TransitionHandleHit(string TransitionId, int ControlIndex);

    private const float DefaultPixelsPerMeter = 14.0f;
    private const float MinimumPixelsPerMeter = 3.0f;
    private const float MaximumPixelsPerMeter = 120.0f;
    private const float SnapMeters = 0.25f;
    private const float ScaleSnap = 0.05f;
    private const float MinimumScaleMagnitude = 0.1f;
    private const float SideHitTolerancePixels = 8.0f;
    private const float CornerHitTolerancePixels = 11.0f;
    private const float TransitionHitTolerancePixels = 9.0f;
    private const float TransitionHandleTolerancePixels = 12.0f;
    private const int BezierSubdivisions = 32;

    private TrackProjectDocument _document = null!;
    private string? _selectedInstanceId;
    private string? _selectedTransitionId;
    private Vector2 _panPixels;
    private float _pixelsPerMeter = DefaultPixelsPerMeter;
    private bool _panning;
    private ManipulationMode _manipulationMode;
    private string? _manipulatedInstanceId;
    private string? _manipulatedTransitionId;
    private Point2Dto _dragPointerStart = new();
    private Point2Dto _dragInstanceStart = new();
    private Point2Dto _dragScaleStart = new() { X = 1.0f, Y = 1.0f };
    private float _dragPointerAngle;
    private float _rotationAccumulator;
    private IReadOnlyList<CompiledTransition> _transitionPreview = [];
    private bool _showTransitions = true;
    private readonly HashSet<string> _lockedInstanceIds = new(StringComparer.Ordinal);
    private PopupMenu? _instanceContextMenu;

    [Signal]
    public delegate void SelectionChangedEventHandler();

    [Signal]
    public delegate void DocumentChangedEventHandler();

    /// <summary>Raised when an Exercise JSON is dropped at a snapped track position.</summary>
    public event Action<string, Point2Dto>? ExerciseDropped;

    /// <summary>
    /// Raised with an absolute track-space handle position. The document converts
    /// it to a persisted offset; the canvas never owns transition geometry.
    /// </summary>
    public event Action<string, int, Point2Dto>? TransitionControlPointDragged;

    /// <summary>Bounds one mouse gesture into one history transaction.</summary>
    public event Action<string>? EditTransactionStarted;

    /// <summary>Completes the active mouse gesture, including a no-op drag.</summary>
    public event Action? EditTransactionFinished;

    public event Action<string>? DuplicateRequested;

    public event Action<string>? LockedTransformAttempted;

    /// <summary>Selected instance id is UI state and is never serialized.</summary>
    public string? SelectedInstanceId => _selectedInstanceId;

    /// <summary>Selected derived transition id; this is editor UI state only.</summary>
    public string? SelectedTransitionId => _selectedTransitionId;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        FocusMode = FocusModeEnum.Click;
        Resized += QueueRedraw;
        MouseExited += ResetHoverCursor;

        // Godot has no built-in curved rotation cursor. Cross is reserved for
        // this canvas-only custom shape and becomes active over a bounds corner.
        Texture2D? rotateCursor = GD.Load<Texture2D>("res://Assets/Editor/rotate_cursor.svg");
        if (rotateCursor is not null)
        {
            Input.SetCustomMouseCursor(rotateCursor, Input.CursorShape.Cross, new Vector2(16.0f, 16.0f));
        }

        _instanceContextMenu = new PopupMenu { Name = "InstanceContextMenu" };
        _instanceContextMenu.AddItem("Duplicate Instance", 1);
        _instanceContextMenu.IdPressed += _ =>
        {
            if (_selectedInstanceId is not null) DuplicateRequested?.Invoke(_selectedInstanceId);
        };
        AddChild(_instanceContextMenu);
    }

    /// <summary>Replaces the rendered document and optionally fits the area in view.</summary>
    public void SetDocument(TrackProjectDocument document, bool resetView = true)
    {
        _document = document;
        _selectedInstanceId = null;
        _selectedTransitionId = null;
        if (resetView)
        {
            _panPixels = Vector2.Zero;
            // Layout containers have not necessarily assigned the central panel
            // its monitor-dependent size when the document is replaced in _Ready.
            // Deferred fitting uses the real canvas after both side panels settle.
            Callable.From(FitAreaInView).CallDeferred();
        }

        QueueRedraw();
    }

    /// <summary>Centres the whole Track area inside the current central canvas.</summary>
    public void FitAreaInView()
    {
        if (_document is null) return;
        _panPixels = Vector2.Zero;
        _pixelsPerMeter = EditorCanvasMath.FitPixelsPerMeter(
            _document.Venue.Definition.Area.Width,
            _document.Venue.Definition.Area.Length,
            Size,
            paddingPixels: 28.0f,
            MinimumPixelsPerMeter,
            MaximumPixelsPerMeter);
        QueueRedraw();
    }

    /// <inheritdoc />
    public override bool _CanDropData(Vector2 atPosition, Variant data) =>
        ExerciseLibraryTree.TryReadExercisePath(data, out _);

    /// <inheritdoc />
    public override void _DropData(Vector2 atPosition, Variant data)
    {
        if (!ExerciseLibraryTree.TryReadExercisePath(data, out string relativePath))
        {
            return;
        }

        Point2Dto position = EditorCanvasMath.Snap(ToDomain(atPosition), SnapMeters);
        ExerciseDropped?.Invoke(relativePath, position);
    }

    /// <summary>
    /// Supplies derived transition geometry. It is a render cache only: Track
    /// Project remains free of generated spline data.
    /// </summary>
    public void SetTransitionPreview(
        IReadOnlyList<CompiledTransition> transitions,
        bool visible)
    {
        _transitionPreview = transitions;
        _showTransitions = visible;
        if (!visible || (_selectedTransitionId is not null &&
            transitions.All(item => item.TransitionId != _selectedTransitionId)))
        {
            bool changed = _selectedTransitionId is not null;
            _selectedTransitionId = null;
            if (changed)
            {
                EmitSignal(SignalName.SelectionChanged);
            }
        }
        QueueRedraw();
    }

    /// <summary>Copies editor-only lock identity; it is never written to the document.</summary>
    public void SetLockedInstances(IEnumerable<string> instanceIds)
    {
        _lockedInstanceIds.Clear();
        _lockedInstanceIds.UnionWith(instanceIds);
        QueueRedraw();
    }

    /// <summary>Selects an instance from Route Order or properties.</summary>
    public void SelectInstance(string? instanceId)
    {
        _selectedInstanceId = instanceId is not null && _document.FindInstance(instanceId) is not null
            ? instanceId
            : null;
        _selectedTransitionId = null;
        EmitSignal(SignalName.SelectionChanged);
        QueueRedraw();
    }

    /// <summary>Selects a compiled transition and clears instance selection.</summary>
    public void SelectTransition(string? transitionId)
    {
        _selectedTransitionId = transitionId is not null &&
            _transitionPreview.Any(item => item.TransitionId == transitionId)
            ? transitionId
            : null;
        _selectedInstanceId = null;
        EmitSignal(SignalName.SelectionChanged);
        QueueRedraw();
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (_document is null) return;
        switch (@event)
        {
            case InputEventMouseButton button:
                HandleMouseButton(button);
                break;
            case InputEventMouseMotion motion:
                HandleMouseMotion(motion);
                break;
        }
    }

    public override void _Draw()
    {
        if (_document is null) return;
        DrawGrid();
        DrawArea();
        DrawVenuePreview();

        // Exercise geometry is rendered in document-defined passes so route
        // order cannot accidentally place one instance's cones beneath another
        // instance's markings or trajectory.
        foreach (TrackProjectInstanceDto instance in _document.Project.Instances)
            DrawInstanceMarkings(instance);
        foreach (TrackProjectInstanceDto instance in _document.Project.Instances)
            DrawInstanceCones(instance);
        foreach (TrackProjectInstanceDto instance in _document.Project.Instances)
            DrawInstanceTrajectory(instance);

        if (_showTransitions)
            DrawTransitionPreview();

        for (int index = 0; index < _document.Project.Instances.Length; index++)
        {
            DrawInstanceOverlay(_document.Project.Instances[index], index);
        }
    }

    private void DrawTransitionPreview()
    {
        foreach (CompiledTransition transition in _transitionPreview)
        {
            TrajectorySegmentDto segment = transition.ToTrajectorySegment();
            Point2Dto[] samples = TrajectoryGeometry.SampleCubicBezier(
                segment, BezierSubdivisions);
            bool selected = transition.TransitionId == _selectedTransitionId;
            Color color = selected
                ? new Color(1.0f, 0.88f, 0.18f)
                : transition.SourceMode == TransitionSourceMode.Override
                    ? new Color(0.25f, 0.85f, 1.0f, 0.98f)
                    : new Color(1.0f, 0.38f, 0.92f, 0.95f);
            string style = selected || transition.SourceMode == TransitionSourceMode.Override
                ? "solid"
                : "dashed";
            foreach (MarkingStroke stroke in MarkingGeometry.CreateStrokes(samples, style))
            {
                DrawLine(ToScreen(stroke.Start), ToScreen(stroke.End), color, selected ? 6.0f : 4.0f, true);
            }

            if (selected)
            {
                DrawTransitionHandles(transition);
            }
        }
    }

    private void DrawTransitionHandles(CompiledTransition transition)
    {
        Vector2 start = ToScreen(transition.Start);
        Vector2 control1 = ToScreen(transition.Control1);
        Vector2 control2 = ToScreen(transition.Control2);
        Vector2 end = ToScreen(transition.End);
        Color guide = new(0.92f, 0.92f, 0.72f, 0.72f);
        DrawLine(start, control1, guide, 2.0f, true);
        DrawLine(end, control2, guide, 2.0f, true);
        DrawCircle(start, 4.0f, Colors.White);
        DrawCircle(end, 4.0f, Colors.White);
        DrawCircle(control1, 8.0f, new Color(1.0f, 0.58f, 0.16f));
        DrawCircle(control2, 8.0f, new Color(0.20f, 0.78f, 1.0f));
    }

    private void HandleMouseButton(InputEventMouseButton button)
    {
        if (button.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown && button.Pressed)
        {
            float factor = button.ButtonIndex == MouseButton.WheelUp ? 1.15f : 1.0f / 1.15f;
            float updated = Mathf.Clamp(_pixelsPerMeter * factor, MinimumPixelsPerMeter, MaximumPixelsPerMeter);
            _panPixels = EditorCanvasMath.ZoomAt(
                button.Position, Size, _panPixels, _pixelsPerMeter, updated);
            _pixelsPerMeter = updated;
            QueueRedraw();
            AcceptEvent();
            return;
        }

        if (button.ButtonIndex == MouseButton.Middle)
        {
            _panning = button.Pressed;
            AcceptEvent();
            return;
        }

        if (button.ButtonIndex == MouseButton.Right && button.Pressed)
        {
            string? contextInstanceId = HitTestInstance(button.Position);
            if (contextInstanceId is not null)
            {
                SelectInstance(contextInstanceId);
                _instanceContextMenu!.Position = (Vector2I)GetGlobalMousePosition();
                // Opening during the same right-button event can make Godot treat
                // that event as an outside click and immediately close the popup.
                Callable.From(() => _instanceContextMenu.Popup()).CallDeferred();
                AcceptEvent();
            }
            return;
        }

        if (button.ButtonIndex != MouseButton.Left)
        {
            return;
        }

        if (!button.Pressed)
        {
            bool hadManipulation = _manipulationMode != ManipulationMode.None;
            _manipulationMode = ManipulationMode.None;
            _manipulatedInstanceId = null;
            _manipulatedTransitionId = null;
            UpdateHoverCursor(button.Position);
            if (hadManipulation) EditTransactionFinished?.Invoke();
            return;
        }

        // Transition handles have the highest priority. Merely pressing/selecting
        // one does not create an override; creation happens on the first motion.
        TransitionHandleHit? transitionHandle = HitTestTransitionHandle(button.Position);
        if (transitionHandle is TransitionHandleHit controlHit)
        {
            _manipulationMode = controlHit.ControlIndex == 1
                ? ManipulationMode.TransitionControl1
                : ManipulationMode.TransitionControl2;
            _manipulatedTransitionId = controlHit.TransitionId;
            EditTransactionStarted?.Invoke("Edit transition handle");
            GrabFocus();
            AcceptEvent();
            return;
        }

        string? transitionId = HitTestTransition(button.Position);
        if (transitionId is not null)
        {
            SelectTransition(transitionId);
            GrabFocus();
            AcceptEvent();
            return;
        }

        HandleHit? handle = HitTestHandle(button.Position);
        if (handle is HandleHit hit)
        {
            SelectInstance(hit.InstanceId);
            if (_lockedInstanceIds.Contains(hit.InstanceId))
            {
                LockedTransformAttempted?.Invoke(hit.InstanceId);
                AcceptEvent();
                return;
            }
            BeginManipulation(hit, button.Position);
            EditTransactionStarted?.Invoke(hit.Mode == ManipulationMode.Rotate
                ? "Rotate instance"
                : "Resize instance");
            GrabFocus();
            AcceptEvent();
            return;
        }

        string? hitInstanceId = HitTestInstance(button.Position);
        SelectInstance(hitInstanceId);
        if (hitInstanceId is not null)
        {
            if (_lockedInstanceIds.Contains(hitInstanceId))
            {
                LockedTransformAttempted?.Invoke(hitInstanceId);
                GrabFocus();
                AcceptEvent();
                return;
            }
            TrackProjectInstanceDto instance = _document.FindInstance(hitInstanceId)!;
            _dragPointerStart = ToDomain(button.Position);
            _dragInstanceStart = CopyPoint(instance.Position);
            _manipulationMode = ManipulationMode.Move;
            _manipulatedInstanceId = hitInstanceId;
            EditTransactionStarted?.Invoke("Move instance");
        }

        GrabFocus();
        AcceptEvent();
    }

    private void HandleMouseMotion(InputEventMouseMotion motion)
    {
        if (_panning)
        {
            _panPixels += motion.Relative;
            QueueRedraw();
            AcceptEvent();
            return;
        }

        if (_manipulationMode == ManipulationMode.None || _manipulatedInstanceId is null)
        {
            if ((_manipulationMode is ManipulationMode.TransitionControl1 or
                ManipulationMode.TransitionControl2) && _manipulatedTransitionId is not null)
            {
                Point2Dto point = ToDomain(motion.Position);
                // Alt deliberately bypasses the shared 0.25 m snap for fine edits.
                if (!motion.AltPressed)
                {
                    point = EditorCanvasMath.Snap(point, SnapMeters);
                }

                int controlIndex = _manipulationMode == ManipulationMode.TransitionControl1 ? 1 : 2;
                TransitionControlPointDragged?.Invoke(_manipulatedTransitionId, controlIndex, point);
                QueueRedraw();
                AcceptEvent();
                return;
            }

            UpdateHoverCursor(motion.Position);
            return;
        }

        bool changed = _manipulationMode switch
        {
            ManipulationMode.Move => MoveManipulatedInstance(motion.Position),
            ManipulationMode.ResizeX or ManipulationMode.ResizeY => ResizeManipulatedInstance(motion.Position),
            ManipulationMode.Rotate => RotateManipulatedInstance(motion.Position),
            _ => false,
        };
        if (changed)
        {
            EmitSignal(SignalName.DocumentChanged);
            QueueRedraw();
        }

        AcceptEvent();
    }

    private void BeginManipulation(HandleHit hit, Vector2 screenPosition)
    {
        TrackProjectInstanceDto instance = _document.FindInstance(hit.InstanceId)!;
        _manipulationMode = hit.Mode;
        _manipulatedInstanceId = hit.InstanceId;
        _dragScaleStart = CopyPoint(instance.Scale);
        if (hit.Mode == ManipulationMode.Rotate)
        {
            Point2Dto pointer = ToDomain(screenPosition);
            _dragPointerAngle = MathF.Atan2(
                pointer.Y - instance.Position.Y,
                pointer.X - instance.Position.X);
            _rotationAccumulator = instance.RotationDeg;
        }
    }

    private bool MoveManipulatedInstance(Vector2 screenPosition)
    {
        Point2Dto current = ToDomain(screenPosition);
        Point2Dto target = EditorCanvasMath.Snap(new Point2Dto
        {
            X = _dragInstanceStart.X + current.X - _dragPointerStart.X,
            Y = _dragInstanceStart.Y + current.Y - _dragPointerStart.Y,
        }, SnapMeters);
        return _document.MoveInstance(_manipulatedInstanceId!, target);
    }

    private bool ResizeManipulatedInstance(Vector2 screenPosition)
    {
        TrackProjectInstanceDto instance = _document.FindInstance(_manipulatedInstanceId!)!;
        ExerciseDefinitionDto? definition = _document.FindDefinition(instance.InstanceId);
        if (definition is null)
        {
            return false;
        }

        /*
         * A side drag changes the selected axis symmetrically around the instance
         * center. We remove translation and rotation, but deliberately keep the
         * scaled coordinate: twice its distance from the center divided by the
         * source bounds size is the new scale magnitude. The original sign is
         * retained because it represents mirror state rather than size.
         */
        Point2Dto rotated = ExerciseInstanceGeometry.InverseRotationTranslation(
            ToDomain(screenPosition), instance.Position, instance.RotationDeg);
        float updatedX = _dragScaleStart.X;
        float updatedY = _dragScaleStart.Y;
        if (_manipulationMode == ManipulationMode.ResizeX)
        {
            float magnitude = SnapScale(2.0f * MathF.Abs(rotated.X) / definition.Bounds.Width);
            updatedX = MathF.CopySign(magnitude, _dragScaleStart.X);
        }
        else
        {
            float magnitude = SnapScale(2.0f * MathF.Abs(rotated.Y) / definition.Bounds.Length);
            updatedY = MathF.CopySign(magnitude, _dragScaleStart.Y);
        }

        return _document.SetTransform(instance.InstanceId, instance.Position, instance.RotationDeg,
            new Point2Dto { X = updatedX, Y = updatedY });
    }

    private bool RotateManipulatedInstance(Vector2 screenPosition)
    {
        TrackProjectInstanceDto instance = _document.FindInstance(_manipulatedInstanceId!)!;
        Point2Dto pointer = ToDomain(screenPosition);
        float angle = MathF.Atan2(pointer.Y - instance.Position.Y, pointer.X - instance.Position.X);
        float delta = Mathf.Wrap(angle - _dragPointerAngle, -MathF.PI, MathF.PI);
        _dragPointerAngle = angle;
        _rotationAccumulator += Mathf.RadToDeg(delta);
        float snappedRotation = MathF.Round(_rotationAccumulator);
        return _document.SetTransform(instance.InstanceId, instance.Position, snappedRotation, instance.Scale);
    }

    private HandleHit? HitTestHandle(Vector2 screenPosition)
    {
        // Corners win over sides, and both win over interior move selection.
        for (int index = _document.Project.Instances.Length - 1; index >= 0; index--)
        {
            TrackProjectInstanceDto instance = _document.Project.Instances[index];
            ExerciseDefinitionDto? definition = _document.FindDefinition(instance.InstanceId);
            if (definition is null)
            {
                continue;
            }

            Vector2[] corners = ExerciseInstanceGeometry.TransformBounds(
                    definition.Bounds.Width, definition.Bounds.Length,
                    instance.Position, instance.RotationDeg, instance.Scale)
                .Select(ToScreen).ToArray();
            if (corners.Any(corner => corner.DistanceTo(screenPosition) <= CornerHitTolerancePixels))
            {
                return new HandleHit(instance.InstanceId, ManipulationMode.Rotate, CursorShape.Cross);
            }

            for (int side = 0; side < corners.Length; side++)
            {
                Vector2 start = corners[side];
                Vector2 end = corners[(side + 1) % corners.Length];
                if (DistanceToSegment(screenPosition, start, end) > SideHitTolerancePixels)
                {
                    continue;
                }

                bool localXAxisSide = side is 1 or 3;
                Vector2 edge = end - start;
                CursorShape cursor = MathF.Abs(edge.X) < MathF.Abs(edge.Y)
                    ? CursorShape.Hsize
                    : CursorShape.Vsize;
                return new HandleHit(instance.InstanceId,
                    localXAxisSide ? ManipulationMode.ResizeX : ManipulationMode.ResizeY,
                    cursor);
            }
        }

        return null;
    }

    private TransitionHandleHit? HitTestTransitionHandle(Vector2 screenPosition)
    {
        if (!_showTransitions || _selectedTransitionId is null)
        {
            return null;
        }

        CompiledTransition? transition = _transitionPreview.FirstOrDefault(
            item => item.TransitionId == _selectedTransitionId);
        if (transition is null)
        {
            return null;
        }

        if (ToScreen(transition.Control1).DistanceTo(screenPosition) <= TransitionHandleTolerancePixels)
        {
            return new TransitionHandleHit(transition.TransitionId, 1);
        }

        if (ToScreen(transition.Control2).DistanceTo(screenPosition) <= TransitionHandleTolerancePixels)
        {
            return new TransitionHandleHit(transition.TransitionId, 2);
        }

        return null;
    }

    private string? HitTestTransition(Vector2 screenPosition)
    {
        if (!_showTransitions)
        {
            return null;
        }

        /*
         * Sampling is a rendering/hit-test cache only. Persisted geometry remains
         * cubicBezier control points. Testing screen-space line segments keeps the
         * pointer tolerance stable at every zoom level.
         */
        IEnumerable<CompiledTransition> ordered = _transitionPreview
            .OrderByDescending(item => item.TransitionId == _selectedTransitionId);
        foreach (CompiledTransition transition in ordered)
        {
            Point2Dto[] samples = TrajectoryGeometry.SampleCubicBezier(
                transition.ToTrajectorySegment(), BezierSubdivisions);
            for (int index = 0; index + 1 < samples.Length; index++)
            {
                if (DistanceToSegment(screenPosition, ToScreen(samples[index]),
                    ToScreen(samples[index + 1])) <= TransitionHitTolerancePixels)
                {
                    return transition.TransitionId;
                }
            }
        }

        return null;
    }

    private void UpdateHoverCursor(Vector2 screenPosition)
    {
        if (HitTestTransitionHandle(screenPosition) is not null)
        {
            MouseDefaultCursorShape = CursorShape.PointingHand;
            return;
        }

        HandleHit? handle = HitTestHandle(screenPosition);
        MouseDefaultCursorShape = handle?.Cursor ??
            (HitTestTransition(screenPosition) is not null ? CursorShape.PointingHand : CursorShape.Arrow);
    }

    private void ResetHoverCursor()
    {
        if (_manipulationMode == ManipulationMode.None)
        {
            MouseDefaultCursorShape = CursorShape.Arrow;
        }
    }

    private string? HitTestInstance(Vector2 screenPosition)
    {
        Point2Dto trackPoint = ToDomain(screenPosition);
        // Reverse traversal makes selection agree with draw order when bounds overlap.
        for (int index = _document.Project.Instances.Length - 1; index >= 0; index--)
        {
            TrackProjectInstanceDto instance = _document.Project.Instances[index];
            ExerciseDefinitionDto? definition = _document.FindDefinition(instance.InstanceId);
            if (definition is null)
            {
                if (Distance(trackPoint, instance.Position) <= 1.5f)
                {
                    return instance.InstanceId;
                }

                continue;
            }

            Point2Dto local = ExerciseInstanceGeometry.InverseTransformPoint(
                trackPoint, instance.Position, instance.RotationDeg, instance.Scale);
            if (MathF.Abs(local.X) <= definition.Bounds.Width * 0.5f &&
                MathF.Abs(local.Y) <= definition.Bounds.Length * 0.5f)
            {
                return instance.InstanceId;
            }
        }

        return null;
    }

    private void DrawGrid()
    {
        float halfWidth = _document.Venue.Definition.Area.Width * 0.5f;
        float halfLength = _document.Venue.Definition.Area.Length * 0.5f;
        int minimumX = Mathf.CeilToInt(-halfWidth);
        int maximumX = Mathf.FloorToInt(halfWidth);
        int minimumY = Mathf.CeilToInt(-halfLength);
        int maximumY = Mathf.FloorToInt(halfLength);
        Color minor = new(0.20f, 0.23f, 0.27f);
        Color major = new(0.34f, 0.38f, 0.44f);

        // Grid indices are integral metres, but every line is clipped to the
        // exact area boundary. Pan and zoom may reveal empty canvas around the
        // project; that space intentionally receives no measurement grid.
        for (int x = minimumX; x <= maximumX; x++)
        {
            Color color = x % 5 == 0 ? major : minor;
            DrawLine(ToScreen(new Point2Dto { X = x, Y = -halfLength }),
                ToScreen(new Point2Dto { X = x, Y = halfLength }), color, x % 5 == 0 ? 2.0f : 1.0f);
        }

        for (int y = minimumY; y <= maximumY; y++)
        {
            Color color = y % 5 == 0 ? major : minor;
            DrawLine(ToScreen(new Point2Dto { X = -halfWidth, Y = y }),
                ToScreen(new Point2Dto { X = halfWidth, Y = y }), color, y % 5 == 0 ? 2.0f : 1.0f);
        }

        Vector2 origin = ToScreen(new Point2Dto());
        DrawLine(new Vector2(origin.X - 10, origin.Y), new Vector2(origin.X + 10, origin.Y), Colors.White, 2.0f);
        DrawLine(new Vector2(origin.X, origin.Y - 10), new Vector2(origin.X, origin.Y + 10), Colors.White, 2.0f);
    }

    private void DrawArea()
    {
        float halfWidth = _document.Venue.Definition.Area.Width * 0.5f;
        float halfLength = _document.Venue.Definition.Area.Length * 0.5f;
        Vector2[] corners =
        [
            ToScreen(new Point2Dto { X = -halfWidth, Y = -halfLength }),
            ToScreen(new Point2Dto { X = halfWidth, Y = -halfLength }),
            ToScreen(new Point2Dto { X = halfWidth, Y = halfLength }),
            ToScreen(new Point2Dto { X = -halfWidth, Y = halfLength }),
        ];
        DrawClosedPolyline(corners, new Color(0.55f, 0.72f, 0.90f), 3.0f);
    }

    /// <summary>
    /// Draws immutable Venue world geometry as a muted background. None of these
    /// shapes participate in Track hit testing, selection or manipulation.
    /// </summary>
    private void DrawVenuePreview()
    {
        VenueDefinitionDto venue = _document.Venue.Definition;
        foreach (MarkingDto marking in venue.Markings)
        {
            Color color = ResolveRgb(marking.Color);
            color.A = marking.VisibleInViewer ? 0.58f : 0.22f;
            foreach (MarkingStroke stroke in MarkingGeometry.CreateStrokes(marking.Points, marking.Style))
            {
                DrawLine(ToScreen(stroke.Start), ToScreen(stroke.End), color,
                    MathF.Max(1.0f, marking.WidthMeters * _pixelsPerMeter), true);
            }
        }

        foreach (VenueObjectInstanceDto item in venue.Objects)
        {
            Point2Dto[] footprint = VenueGeometry.TransformFootprint(item);
            bool resolved = _document.Venue.IsObjectResolved(item.ObjectId);
            Color color = !resolved
                ? new Color(1.0f, 0.28f, 0.22f, 0.9f)
                : item.VisibleInViewer
                    ? new Color(0.58f, 0.56f, 0.45f, 0.78f)
                    : new Color(0.45f, 0.45f, 0.48f, 0.36f);
            DrawClosedPolyline(footprint.Select(ToScreen).ToArray(), color, resolved ? 2.0f : 3.0f);
            Vector2 labelPosition = ToScreen(item.Position) + new Vector2(6.0f, -6.0f);
            string marker = !resolved ? "! " : item.VisibleInViewer ? "" : "HIDDEN ";
            DrawString(ThemeDB.FallbackFont, labelPosition, $"{marker}{item.Name}",
                HorizontalAlignment.Left, -1.0f, 12, color);
        }

        foreach (ConeDto cone in venue.Cones)
        {
            DrawCircle(ToScreen(cone.Position), 4.5f, ResolveConeColor(cone.Color));
            DrawArc(ToScreen(cone.Position), 7.0f, 0, MathF.Tau, 16,
                new Color(0.72f, 0.72f, 0.72f, 0.7f), 1.0f);
        }
    }

    private void DrawInstanceMarkings(TrackProjectInstanceDto instance)
    {
        ExerciseDefinitionDto? definition = _document.FindDefinition(instance.InstanceId);
        if (definition is null) return;
        foreach (MarkingDto marking in definition.Markings)
        {
            Color color = ResolveRgb(marking.Color);
            if (!marking.VisibleInViewer)
            {
                color.A = 0.35f; // Editor-only visibility indication; data remains present.
            }

            foreach (MarkingStroke stroke in MarkingGeometry.CreateStrokes(marking.Points, marking.Style))
            {
                DrawLine(ToScreen(Transform(stroke.Start, instance)),
                    ToScreen(Transform(stroke.End, instance)), color,
                    MathF.Max(1.0f, marking.WidthMeters * _pixelsPerMeter), true);
            }
        }
    }

    private void DrawInstanceTrajectory(TrackProjectInstanceDto instance)
    {
        ExerciseDefinitionDto? definition = _document.FindDefinition(instance.InstanceId);
        if (definition is null) return;
        foreach (TrajectorySegmentDto segment in definition.Trajectory.Segments)
        {
            Point2Dto[] points = segment.Type == "polyline"
                ? segment.Points!
                : TrajectoryGeometry.SampleCubicBezier(segment, BezierSubdivisions);
            for (int index = 0; index < points.Length - 1; index++)
            {
                DrawLine(ToScreen(Transform(points[index], instance)),
                    ToScreen(Transform(points[index + 1], instance)),
                    new Color(0.2f, 0.95f, 0.55f), 3.0f, true);
            }
        }
    }

    private void DrawInstanceCones(TrackProjectInstanceDto instance)
    {
        ExerciseDefinitionDto? definition = _document.FindDefinition(instance.InstanceId);
        if (definition is null) return;
        foreach (ConeDto cone in definition.Cones)
        {
            DrawCircle(ToScreen(Transform(cone.Position, instance)), 5.0f, ResolveConeColor(cone.Color));
        }
    }

    private void DrawInstanceOverlay(TrackProjectInstanceDto instance, int routeIndex)
    {
        ExerciseDefinitionDto? definition = _document.FindDefinition(instance.InstanceId);
        bool selected = instance.InstanceId == _selectedInstanceId;
        if (definition is null)
        {
            DrawUnresolved(instance, routeIndex, selected);
            return;
        }

        Point2Dto[] bounds = ExerciseInstanceGeometry.TransformBounds(
            definition.Bounds.Width, definition.Bounds.Length,
            instance.Position, instance.RotationDeg, instance.Scale);
        bool outside = ExerciseInstanceGeometry.IsOutsideArea(
            bounds, _document.Venue.Definition.Area.Width, _document.Venue.Definition.Area.Length);
        DrawClosedPolyline(bounds.Select(ToScreen).ToArray(),
            outside ? Colors.OrangeRed : selected ? Colors.Cyan : new Color(0.45f, 0.62f, 0.75f, 0.8f),
            selected ? 4.0f : 2.0f);

        DrawRouteNumber(instance.Position, routeIndex, outside ? Colors.OrangeRed : Colors.White);
        if (_lockedInstanceIds.Contains(instance.InstanceId))
        {
            DrawLockOverlay(instance.Position);
        }
        if (selected)
        {
            DrawManipulationHandles(bounds.Select(ToScreen).ToArray());
        }
    }

    private void DrawManipulationHandles(IReadOnlyList<Vector2> corners)
    {
        Color color = new(0.25f, 0.95f, 1.0f);
        for (int index = 0; index < corners.Count; index++)
        {
            Vector2 corner = corners[index];
            Vector2 midpoint = (corner + corners[(index + 1) % corners.Count]) * 0.5f;
            DrawCircle(corner, 6.0f, color);
            DrawRect(new Rect2(midpoint - Vector2.One * 4.0f, Vector2.One * 8.0f), color);
        }
    }

    private void DrawUnresolved(TrackProjectInstanceDto instance, int routeIndex, bool selected)
    {
        Vector2 center = ToScreen(instance.Position);
        float radius = 18.0f;
        Color color = selected ? Colors.Yellow : Colors.OrangeRed;
        DrawRect(new Rect2(center - Vector2.One * radius, Vector2.One * radius * 2.0f), color, false, 3.0f);
        DrawLine(center + new Vector2(-radius, -radius), center + new Vector2(radius, radius), color, 3.0f);
        DrawLine(center + new Vector2(-radius, radius), center + new Vector2(radius, -radius), color, 3.0f);
        DrawRouteNumber(instance.Position, routeIndex, color);
        if (_lockedInstanceIds.Contains(instance.InstanceId)) DrawLockOverlay(instance.Position);
    }

    private void DrawRouteNumber(Point2Dto position, int routeIndex, Color color)
    {
        Vector2 label = ToScreen(position) + new Vector2(10.0f, -10.0f);
        DrawString(ThemeDB.FallbackFont, label, (routeIndex + 1).ToString(),
            HorizontalAlignment.Left, -1.0f, 18, color);
    }

    private void DrawLockOverlay(Point2Dto position)
    {
        Vector2 label = ToScreen(position) + new Vector2(-22.0f, -16.0f);
        Color color = new(1.0f, 0.72f, 0.18f);
        DrawRect(new Rect2(label, new Vector2(42.0f, 21.0f)), new Color(0.08f, 0.08f, 0.08f, 0.86f));
        DrawString(ThemeDB.FallbackFont, label + new Vector2(4.0f, 16.0f), "LOCK",
            HorizontalAlignment.Left, -1.0f, 12, color);
    }

    private void DrawClosedPolyline(IReadOnlyList<Vector2> points, Color color, float width)
    {
        for (int index = 0; index < points.Count; index++)
        {
            DrawLine(points[index], points[(index + 1) % points.Count], color, width, true);
        }
    }

    private Point2Dto Transform(Point2Dto point, TrackProjectInstanceDto instance) =>
        ExerciseInstanceGeometry.TransformPoint(point, instance.Position, instance.RotationDeg, instance.Scale);

    private Vector2 ToScreen(Point2Dto point) =>
        EditorCanvasMath.DomainToScreen(point, Size, _panPixels, _pixelsPerMeter);

    private Point2Dto ToDomain(Vector2 point) =>
        EditorCanvasMath.ScreenToDomain(point, Size, _panPixels, _pixelsPerMeter);

    private static float Distance(Point2Dto left, Point2Dto right)
    {
        float x = left.X - right.X;
        float y = left.Y - right.Y;
        return MathF.Sqrt(x * x + y * y);
    }

    private static float DistanceToSegment(Vector2 point, Vector2 start, Vector2 end)
    {
        Vector2 segment = end - start;
        float lengthSquared = segment.LengthSquared();
        if (lengthSquared <= float.Epsilon)
        {
            return point.DistanceTo(start);
        }

        float t = Mathf.Clamp((point - start).Dot(segment) / lengthSquared, 0.0f, 1.0f);
        return point.DistanceTo(start + segment * t);
    }

    private static float SnapScale(float value) =>
        MathF.Max(MinimumScaleMagnitude, MathF.Round(value / ScaleSnap) * ScaleSnap);

    private static Point2Dto CopyPoint(Point2Dto point) => new() { X = point.X, Y = point.Y };

    private static Color ResolveConeColor(string color) => color switch
    {
        "blue" => new Color("1452FF"),
        "yellow" => new Color("FFD10D"),
        "orange" => new Color("FF6B14"),
        "none" => new Color("D35718"),
        _ => new Color("F21F14"),
    };

    private static Color ResolveRgb(string value)
    {
        return MarkingGeometry.TryNormalizeColor(value, false, out string canonical)
            ? new Color(canonical.TrimStart('#'))
            : Colors.White;
    }
}
