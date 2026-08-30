using Godot;
using MotoGymkhanaTrainer.Tracks;

namespace MotoGymkhanaTrainer.ExerciseEditor;

/// <summary>Active authoring tool for the Exercise Editor canvas.</summary>
public enum ExerciseEditorTool
{
    Select,
    AddCone,
    EditTrajectory,
    CreateMarking,
    AppendLine,
    AppendCubicBezier,
    SplitMarkingSegment,
}

/// <summary>Kind of domain object selected on the canvas.</summary>
public enum ExerciseSelectionKind
{
    None,
    Cone,
    TrajectoryPoint,
    TrajectorySegment,
    BezierControl,
    Marking,
    MarkingSegment,
    MarkingHandle,
}

/// <summary>Outcome of deleting the currently selected object.</summary>
public enum SelectionDeleteResult
{
    NothingSelected,
    DeletedCone,
    DeletedTrajectoryPoint,
    TrajectoryMinimumBlocked,
    DeletedMarking,
    DeletedMarkingSegment,
}

/// <summary>
/// Draws and manipulates an Exercise Definition in local two-dimensional metre space.
/// View transform, rendering samples, active tool and selection remain transient UI state.
/// </summary>
public partial class ExerciseEditorCanvas : Control
{
    private const int CubicBezierSubdivisionCount = 32;
    private const float MinimumPixelsPerMeter = 18.0f;
    private const float MaximumPixelsPerMeter = 180.0f;
    private const float DefaultPixelsPerMeter = 48.0f;
    private const float SnapStepMeters = 0.25f;
    private const float ConeHitRadiusPixels = 14.0f;
    private const float AnchorHitRadiusPixels = 13.0f;
    private const float HandleHitRadiusPixels = 12.0f;
    private const float SectionHitTolerancePixels = 9.0f;

    private readonly Color _minorGridColor = new(0.23f, 0.26f, 0.30f);
    private readonly Color _majorGridColor = new(0.34f, 0.38f, 0.44f);
    private readonly Dictionary<string, MarkingStyleGeometry> _markingGeometryCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PathBounds> _markingBoundsCache = new(StringComparer.Ordinal);
    private ExerciseDocument? _document;
    private Vector2 _panPixels;
    private float _pixelsPerMeter = DefaultPixelsPerMeter;
    private bool _panning;
    private bool _draggingSelection;
    private bool _buildingTrajectory;
    private int _trajectoryBuildClickCount;
    private bool _buildingMarking;
    private Point2Dto? _markingDraftStart;
    private Point2Dto? _markingPreviewEnd;
    private string _buildingMarkingId = string.Empty;
    private PathDefinition? _dragBeforeMarkingPath;
    private Point2Dto? _dragBeforePoint;
    private Point2Dto? _dragPointerStart;
    private bool _dragChanged;

    /// <summary>Raised whenever a domain value was changed through direct manipulation.</summary>
    public event Action<string>? DocumentChanged;

    /// <summary>Raised whenever the current object selection changes.</summary>
    public event Action? SelectionChanged;

    /// <summary>Raised when trajectory construction starts or finishes.</summary>
    public event Action? TrajectoryBuildStateChanged;

    /// <summary>Raised for actionable feedback that belongs in the editor status line.</summary>
    public event Action<string, bool>? MessageRequested;

    /// <summary>The currently active authoring tool.</summary>
    public ExerciseEditorTool Tool { get; private set; } = ExerciseEditorTool.Select;

    /// <summary>The selected object's category; this value is UI state only.</summary>
    public ExerciseSelectionKind SelectionKind { get; private set; }

    /// <summary>Id of a selected cone, or an empty string for other selections.</summary>
    public string SelectedConeId { get; private set; } = string.Empty;

    /// <summary>Id of the selected marking or marking point.</summary>
    public string SelectedMarkingId { get; private set; } = string.Empty;

    /// <summary>Stable editor-only selection within a marking Path.</summary>
    public MarkingSelection MarkingSelection { get; private set; } = MarkingSelection.None;

    public int SelectedMarkingSegmentIndex => MarkingSelection.SegmentIndex;

    public MarkingHandleKind SelectedMarkingHandle => MarkingSelection.HandleKind;

    /// <summary>Global conceptual anchor index, or -1.</summary>
    public int SelectedTrajectoryPointIndex { get; private set; } = -1;

    /// <summary>Persisted segment index associated with a section or handle selection.</summary>
    public int SelectedTrajectorySegmentIndex { get; private set; } = -1;

    /// <summary>Polyline pair index inside the selected persisted segment.</summary>
    public int SelectedTrajectorySectionIndex { get; private set; } = -1;

    /// <summary>Selected control handle. Meaningful only for BezierControl selection.</summary>
    public BezierControlKind SelectedBezierControl { get; private set; }

    /// <summary>Whether clicks are currently constructing a replacement polyline.</summary>
    public bool IsBuildingTrajectory => _buildingTrajectory;

    /// <summary>Whether Line/Polyline construction is currently awaiting points.</summary>
    public bool IsBuildingMarking => _buildingMarking;

    /// <inheritdoc />
    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        FocusMode = FocusModeEnum.All;
        ClipContents = true;
    }

    /// <summary>Changes the displayed domain document without copying editable geometry.</summary>
    public void SetDocument(ExerciseDocument document, bool resetView = true)
    {
        _document = document;
        _markingGeometryCache.Clear();
        _markingBoundsCache.Clear();
        _buildingTrajectory = false;
        _trajectoryBuildClickCount = 0;
        _buildingMarking = false;
        _markingDraftStart = null;
        _markingPreviewEnd = null;
        _buildingMarkingId = string.Empty;
        ClearSelection();
        if (resetView)
        {
            _panPixels = Vector2.Zero;
            _pixelsPerMeter = DefaultPixelsPerMeter;
        }

        TrajectoryBuildStateChanged?.Invoke();
        QueueRedraw();
    }

    /// <summary>Activates a tool without changing domain geometry.</summary>
    public void SetTool(ExerciseEditorTool tool)
    {
        Tool = tool;
        CancelTransientOperation(restoreDrag: true);
        if (tool is ExerciseEditorTool.CreateMarking)
            ClearSelection();
        TrajectoryBuildStateChanged?.Invoke();
    }

    /// <summary>Begins replacement of the current trajectory through canvas clicks.</summary>
    public void BeginTrajectoryBuild()
    {
        Tool = ExerciseEditorTool.EditTrajectory;
        _buildingTrajectory = true;
        _trajectoryBuildClickCount = 0;
        ClearSelection();
        TrajectoryBuildStateChanged?.Invoke();
        MessageRequested?.Invoke(
            "Trajectory construction started. Click at least two points, then press Finish Trajectory or right-click.",
            false);
    }

    /// <summary>Finishes construction once two user clicks have defined its direction.</summary>
    public bool TryFinishTrajectoryBuild()
    {
        if (!_buildingTrajectory)
        {
            return true;
        }

        if (_trajectoryBuildClickCount < 2)
        {
            MessageRequested?.Invoke("Add at least two trajectory points before finishing.", true);
            return false;
        }

        _buildingTrajectory = false;
        _trajectoryBuildClickCount = 0;
        TrajectoryBuildStateChanged?.Invoke();
        MessageRequested?.Invoke("Trajectory construction finished.", false);
        QueueRedraw();
        return true;
    }

    /// <summary>Selects a cone after creation or another UI action.</summary>
    public void SelectCone(string coneId) =>
        SetSelection(ExerciseSelectionKind.Cone, coneId, string.Empty, -1, -1, -1);

    /// <summary>Selects a conceptual trajectory anchor.</summary>
    public void SelectTrajectoryPoint(int pointIndex) =>
        SetSelection(ExerciseSelectionKind.TrajectoryPoint, string.Empty, string.Empty, pointIndex, -1, -1);

    /// <summary>Selects one section between adjacent anchors.</summary>
    public void SelectTrajectorySection(TrajectorySectionLocation location) =>
        SetSelection(
            ExerciseSelectionKind.TrajectorySegment,
            string.Empty,
            string.Empty,
            -1,
            location.SegmentIndex,
            location.SectionIndex);

    /// <summary>Selects a marking body.</summary>
    public void SelectMarking(string markingId)
    {
        MarkingSelection = new MarkingSelection(markingId, -1, MarkingHandleKind.None);
        SetSelection(ExerciseSelectionKind.Marking, string.Empty, markingId, -1, -1, -1);
    }

    /// <summary>Selects a segment by stable marking id and persisted segment index.</summary>
    public void SelectMarkingSegment(string markingId, int segmentIndex)
    {
        MarkingSelection = new MarkingSelection(markingId, segmentIndex, MarkingHandleKind.None);
        SetSelection(ExerciseSelectionKind.MarkingSegment, string.Empty, markingId, -1, -1, -1);
    }

    /// <summary>Selects one editor-only Path handle.</summary>
    public void SelectMarkingHandle(string markingId, int segmentIndex, MarkingHandleKind handle)
    {
        MarkingSelection = new MarkingSelection(markingId, segmentIndex, handle);
        SetSelection(ExerciseSelectionKind.MarkingHandle, string.Empty, markingId, -1, -1, -1);
    }

    /// <summary>Clears transient selection without changing the domain document.</summary>
    public void ClearSelection()
    {
        SelectionKind = ExerciseSelectionKind.None;
        SelectedConeId = string.Empty;
        SelectedMarkingId = string.Empty;
        MarkingSelection = MarkingSelection.None;
        SelectedTrajectoryPointIndex = -1;
        SelectedTrajectorySegmentIndex = -1;
        SelectedTrajectorySectionIndex = -1;
        _draggingSelection = false;
        SelectionChanged?.Invoke();
        QueueRedraw();
    }

    /// <summary>Deletes the selected cone or anchor when domain rules permit it.</summary>
    public SelectionDeleteResult DeleteSelected()
    {
        if (_document is null)
        {
            return SelectionDeleteResult.NothingSelected;
        }

        if (SelectionKind == ExerciseSelectionKind.Cone && _document.DeleteCone(SelectedConeId))
        {
            ClearSelection();
            DocumentChanged?.Invoke("Delete cone");
            return SelectionDeleteResult.DeletedCone;
        }

        if (SelectionKind == ExerciseSelectionKind.Marking && _document.DeleteMarking(SelectedMarkingId))
        {
            _markingGeometryCache.Remove(SelectedMarkingId);
            _markingBoundsCache.Remove(SelectedMarkingId);
            ClearSelection();
            DocumentChanged?.Invoke("Delete marking");
            return SelectionDeleteResult.DeletedMarking;
        }

        if (SelectionKind is ExerciseSelectionKind.MarkingSegment or ExerciseSelectionKind.MarkingHandle &&
            SelectedMarkingSegmentIndex >= 0)
        {
            int deletedIndex = SelectedMarkingSegmentIndex;
            if (!_document.DeleteMarkingSegment(SelectedMarkingId, deletedIndex, out bool markingDeleted))
                return SelectionDeleteResult.NothingSelected;
            if (markingDeleted) ClearSelection();
            else
            {
                MarkingDto marking = _document.FindMarking(SelectedMarkingId)!;
                RefreshMarking(marking.Id);
                SelectMarkingSegment(marking.Id, Math.Min(deletedIndex, marking.Path.Segments.Length - 1));
            }
            DocumentChanged?.Invoke("Delete marking segment");
            return markingDeleted ? SelectionDeleteResult.DeletedMarking : SelectionDeleteResult.DeletedMarkingSegment;
        }

        if (SelectionKind != ExerciseSelectionKind.TrajectoryPoint)
        {
            return SelectionDeleteResult.NothingSelected;
        }

        TrajectoryAnchorDeleteResult result = _document.DeleteTrajectoryPoint(SelectedTrajectoryPointIndex);
        if (result == TrajectoryAnchorDeleteResult.MinimumBlocked)
        {
            return SelectionDeleteResult.TrajectoryMinimumBlocked;
        }

        if (result != TrajectoryAnchorDeleteResult.Deleted)
        {
            return SelectionDeleteResult.NothingSelected;
        }

        ClearSelection();
        DocumentChanged?.Invoke("Delete trajectory point");
        return SelectionDeleteResult.DeletedTrajectoryPoint;
    }

    /// <summary>Inserts a midpoint after the selected anchor when the next section is straight.</summary>
    public bool InsertPointAfterSelected()
    {
        if (_document is null || SelectionKind != ExerciseSelectionKind.TrajectoryPoint)
        {
            return false;
        }

        int insertedIndex = _document.InsertTrajectoryPointAfter(SelectedTrajectoryPointIndex);
        if (insertedIndex < 0)
        {
            MessageRequested?.Invoke("The Exit point has no following section.", true);
            return false;
        }

        SelectTrajectoryPoint(insertedIndex);
        DocumentChanged?.Invoke("Insert trajectory point");
        return true;
    }

    /// <summary>Splits the selected marking segment at its midpoint.</summary>
    public bool SplitSelectedMarkingSegment(float parameter = 0.5f)
    {
        if (_document is null || SelectedMarkingSegmentIndex < 0 ||
            !_document.SplitMarkingSegment(SelectedMarkingId, SelectedMarkingSegmentIndex, parameter)) return false;
        RefreshMarking(SelectedMarkingId);
        SelectMarkingSegment(SelectedMarkingId, SelectedMarkingSegmentIndex + 1);
        DocumentChanged?.Invoke("Split marking segment");
        return true;
    }

    /// <summary>Finishes a marking after at least two user clicks.</summary>
    public bool TryFinishMarkingBuild()
    {
        if (!_buildingMarking)
        {
            return true;
        }

        if (_markingDraftStart is not null && string.IsNullOrEmpty(_buildingMarkingId))
        {
            MessageRequested?.Invoke("Add a second marking point before finishing.", true);
            return false;
        }

        _buildingMarking = false;
        _markingDraftStart = null;
        _markingPreviewEnd = null;
        _buildingMarkingId = string.Empty;
        TrajectoryBuildStateChanged?.Invoke();
        MessageRequested?.Invoke("Marking construction finished.", false);
        QueueRedraw();
        return true;
    }

    /// <summary>Converts the selected straight section into a cubic Bezier.</summary>
    public bool ConvertSelectedToCubic()
    {
        if (_document is null || SelectionKind != ExerciseSelectionKind.TrajectorySegment)
        {
            return false;
        }

        TrajectorySectionLocation? result = _document.ConvertSectionToCubic(
            new TrajectorySectionLocation(SelectedTrajectorySegmentIndex, SelectedTrajectorySectionIndex));
        if (result is null)
        {
            return false;
        }

        SelectTrajectorySection(result.Value);
        DocumentChanged?.Invoke("Convert trajectory section to cubic");
        return true;
    }

    /// <summary>Converts the selected cubic Bezier to a straight line.</summary>
    public bool ConvertSelectedToLine()
    {
        if (_document is null ||
            SelectionKind is not (ExerciseSelectionKind.TrajectorySegment or ExerciseSelectionKind.BezierControl))
        {
            return false;
        }

        TrajectorySectionLocation? result = _document.ConvertCubicToLine(SelectedTrajectorySegmentIndex);
        if (result is null)
        {
            return false;
        }

        SelectTrajectorySection(result.Value);
        DocumentChanged?.Invoke("Convert trajectory section to line");
        return true;
    }

    /// <summary>Converts the selected marking segment and keeps its stable index selected.</summary>
    public bool ConvertSelectedMarkingSegment(bool toCubic)
    {
        if (_document is null || SelectedMarkingSegmentIndex < 0 ||
            !_document.ConvertMarkingSegment(SelectedMarkingId, SelectedMarkingSegmentIndex, toCubic)) return false;
        RefreshMarking(SelectedMarkingId);
        SelectMarkingSegment(SelectedMarkingId, SelectedMarkingSegmentIndex);
        DocumentChanged?.Invoke(toCubic ? "Convert marking segment to curve" : "Convert marking segment to line");
        return true;
    }

    /// <summary>Cancels a transient operation or returns to Select.</summary>
    public bool HandleEscape()
    {
        if (_buildingTrajectory && !TryFinishTrajectoryBuild())
            return true;
        bool handled = _draggingSelection || _buildingMarking || Tool != ExerciseEditorTool.Select;
        CancelTransientOperation(restoreDrag: true);
        Tool = ExerciseEditorTool.Select;
        TrajectoryBuildStateChanged?.Invoke();
        QueueRedraw();
        return handled;
    }

    /// <summary>Completes the active marking construction on Enter.</summary>
    public bool HandleEnter() => _buildingMarking && TryFinishMarkingBuild();

    /// <summary>Invalidates only one marking's sampled/style preview geometry.</summary>
    public void RefreshMarking(string markingId)
    {
        _markingGeometryCache.Remove(markingId);
        _markingBoundsCache.Remove(markingId);
        if (_document?.FindMarking(markingId) is { } marking)
            _markingBoundsCache[markingId] = PathBoundsCalculator.Calculate(marking.Path, marking.WidthMeters);
        QueueRedraw();
    }

    /// <inheritdoc />
    public override void _GuiInput(InputEvent @event)
    {
        if (_document is null)
        {
            return;
        }

        if (@event is InputEventMouseButton button)
        {
            HandleMouseButton(button);
        }
        else if (@event is InputEventMouseMotion motion)
        {
            HandleMouseMotion(motion);
        }
    }

    /// <inheritdoc />
    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.105f, 0.12f, 0.145f));
        DrawGridAndOrigin();
        if (_document is null)
        {
            return;
        }

        DrawBounds(_document.Definition.Bounds);
        DrawMarkings();
        DrawMarkingPreview();
        DrawCones(_document.Definition.Cones, _document.Definition.Bounds);
        DrawTrajectory();
    }

    private void HandleMouseButton(InputEventMouseButton button)
    {
        if (button.ButtonIndex == MouseButton.Middle)
        {
            _panning = button.Pressed;
            if (button.Pressed)
            {
                GrabFocus();
            }

            AcceptEvent();
            return;
        }

        if (button.Pressed && button.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
        {
            ZoomAt(button.Position, button.ButtonIndex == MouseButton.WheelUp ? 1.12f : 1.0f / 1.12f);
            AcceptEvent();
            return;
        }

        if (button.Pressed && button.ButtonIndex == MouseButton.Right &&
            Tool == ExerciseEditorTool.EditTrajectory && _buildingTrajectory)
        {
            TryFinishTrajectoryBuild();
            AcceptEvent();
            return;
        }

        if (button.Pressed && button.ButtonIndex == MouseButton.Right && _buildingMarking)
        {
            TryFinishMarkingBuild();
            AcceptEvent();
            return;
        }

        if (button.ButtonIndex != MouseButton.Left)
        {
            return;
        }

        if (!button.Pressed)
        {
            CompleteDrag();
            return;
        }

        GrabFocus();
        switch (Tool)
        {
            case ExerciseEditorTool.AddCone:
                AddConeAt(button.Position);
                break;
            case ExerciseEditorTool.EditTrajectory:
                HandleTrajectoryClick(button.Position);
                break;
            case ExerciseEditorTool.CreateMarking:
            case ExerciseEditorTool.AppendLine:
            case ExerciseEditorTool.AppendCubicBezier:
                AddMarkingBuildPoint(ResolveSnap(button.Position, button.CtrlPressed));
                if (button.DoubleClick) TryFinishMarkingBuild();
                break;
            case ExerciseEditorTool.SplitMarkingSegment:
                SplitMarkingAt(button.Position);
                break;
            default:
                // Handles are editor overlays and therefore receive priority over
                // the sampled centreline and all unrelated scene objects.
                if (!HitTestMarkingHandle(button.Position) && !HitTestMarking(button.Position) &&
                    !HitTestCone(button.Position))
                {
                    ClearSelection();
                }

                _draggingSelection = SelectionKind is ExerciseSelectionKind.Cone or
                    ExerciseSelectionKind.Marking or ExerciseSelectionKind.MarkingSegment or ExerciseSelectionKind.MarkingHandle;
                if (_draggingSelection) BeginDrag(ScreenToDomain(button.Position));
                break;
        }

        AcceptEvent();
    }

    private void AddConeAt(Vector2 screenPosition)
    {
        Point2Dto position = Snap(ScreenToDomain(screenPosition));
        ConeDto? existing = _document!.FindConeAt(position);
        if (existing is not null)
        {
            SelectCone(existing.Id);
            MessageRequested?.Invoke(
                $"Cone '{existing.Id}' already occupies this position and was selected.", false);
            return;
        }

        string coneId = _document!.AddCone(position);
        SelectCone(coneId);
        DocumentChanged?.Invoke("Create cone");
    }

    private void HandleTrajectoryClick(Vector2 screenPosition)
    {
        if (_buildingTrajectory)
        {
            AddTrajectoryBuildPoint(Snap(ScreenToDomain(screenPosition)));
            return;
        }

        /*
         * Predictable Edit-tool priority is handle -> anchor -> section -> cone.
         * Handles are tested only while their Bezier is selected/visible. All radii
         * are screen pixels, so hit tolerance remains stable at every zoom level.
         */
        if (HitTestBezierControl(screenPosition) || HitTestTrajectoryPoint(screenPosition) ||
            HitTestTrajectorySection(screenPosition) || HitTestCone(screenPosition))
        {
            _draggingSelection = SelectionKind is ExerciseSelectionKind.BezierControl or
                ExerciseSelectionKind.TrajectoryPoint or ExerciseSelectionKind.Cone;
            if (_draggingSelection) BeginDrag(ScreenToDomain(screenPosition));
            return;
        }

        ClearSelection();
    }

    private void AddTrajectoryBuildPoint(Point2Dto position)
    {
        int selectedIndex;
        if (_trajectoryBuildClickCount == 0)
        {
            _document!.StartSplineTrajectoryAt(position);
            selectedIndex = 0;
        }
        else if (_trajectoryBuildClickCount == 1)
        {
            selectedIndex = _document!.SetInitialTrajectorySplineEnd(position);
        }
        else
        {
            selectedIndex = _document!.AppendTrajectorySpline(position);
        }

        _trajectoryBuildClickCount++;
        SelectTrajectoryPoint(selectedIndex);
        DocumentChanged?.Invoke("Edit trajectory construction");
        MessageRequested?.Invoke(
            $"Trajectory point {selectedIndex} added. " +
            (_trajectoryBuildClickCount < 2 ? "Add one more point before finishing." : "Continue clicking or finish."),
            false);
    }

    private void AddMarkingBuildPoint(Point2Dto position)
    {
        if (Tool is ExerciseEditorTool.AppendLine or ExerciseEditorTool.AppendCubicBezier)
        {
            string id = SelectedMarkingId;
            if (string.IsNullOrEmpty(id))
            {
                MessageRequested?.Invoke("Select a marking before appending a segment.", true);
                return;
            }

            int index = _document!.AppendMarkingSegment(id, position, Tool == ExerciseEditorTool.AppendCubicBezier);
            if (index < 0)
            {
                MessageRequested?.Invoke("A zero-length segment cannot be appended.", true);
                return;
            }

            SelectMarkingSegment(id, index);
            RefreshMarking(id);
            DocumentChanged?.Invoke(Tool == ExerciseEditorTool.AppendCubicBezier
                ? "Append cubic marking segment"
                : "Append line marking segment");
            return;
        }

        if (!_buildingMarking)
        {
            _buildingMarking = true;
            _markingDraftStart = new Point2Dto { X = position.X, Y = position.Y };
            _markingPreviewEnd = new Point2Dto { X = position.X, Y = position.Y };
            _buildingMarkingId = string.Empty;
            TrajectoryBuildStateChanged?.Invoke();
            MessageRequested?.Invoke("Marking start set. Click an endpoint for the first line.", false);
            QueueRedraw();
            return;
        }

        if (string.IsNullOrEmpty(_buildingMarkingId))
        {
            if (_markingDraftStart is null || PathEditing.PointsEqual(_markingDraftStart, position))
            {
                MessageRequested?.Invoke("A zero-length marking segment is not allowed.", true);
                return;
            }

            var path = new PathDefinition
            {
                Start = new Point2Dto { X = _markingDraftStart.X, Y = _markingDraftStart.Y },
                Segments = [new LinePathSegmentDefinition { End = new Point2Dto { X = position.X, Y = position.Y } }],
            };
            _buildingMarkingId = _document!.AddMarking(path);
            _markingPreviewEnd = position;
            SelectMarkingSegment(_buildingMarkingId, 0);
            RefreshMarking(_buildingMarkingId);
            DocumentChanged?.Invoke("Create marking");
            MessageRequested?.Invoke("Marking created. Continue clicking to append lines or press Enter.", false);
            return;
        }

        int appended = _document!.AppendMarkingSegment(_buildingMarkingId, position, cubic: false);
        if (appended < 0)
        {
            MessageRequested?.Invoke("A zero-length marking segment is not allowed.", true);
            return;
        }

        SelectMarkingSegment(_buildingMarkingId, appended);
        RefreshMarking(_buildingMarkingId);
        DocumentChanged?.Invoke("Append line marking segment");
        MessageRequested?.Invoke("Line appended. Continue clicking or press Enter to finish.", false);
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

        if (_buildingMarking || Tool is ExerciseEditorTool.AppendLine or ExerciseEditorTool.AppendCubicBezier)
        {
            _markingPreviewEnd = ResolveSnap(motion.Position, motion.CtrlPressed);
            QueueRedraw();
        }

        if (!_draggingSelection || (motion.ButtonMask & MouseButtonMask.Left) == 0)
        {
            return;
        }

        /*
         * Every movable coordinate follows one drag policy. Ctrl is read from each
         * motion event, so it can be pressed or released without restarting the drag.
         * Construction clicks remain snapped; this modifier affects movement only.
         */
        Point2Dto dragged = EditorCanvasMath.ResolveDragPosition(
            ScreenToDomain(motion.Position), SnapStepMeters, motion.CtrlPressed);
        if (SelectionKind == ExerciseSelectionKind.Cone)
        {
            _document!.MoveCone(SelectedConeId, dragged);
        }
        else if (SelectionKind == ExerciseSelectionKind.TrajectoryPoint)
        {
            _document!.MoveTrajectoryPoint(SelectedTrajectoryPointIndex, dragged);
        }
        else if (SelectionKind == ExerciseSelectionKind.BezierControl)
        {
            _document!.MoveBezierControl(SelectedTrajectorySegmentIndex, SelectedBezierControl, dragged);
        }
        else if (SelectionKind == ExerciseSelectionKind.MarkingHandle)
        {
            _document!.MoveMarkingCoordinate(
                SelectedMarkingId,
                SelectedMarkingSegmentIndex,
                ToCoordinateKind(SelectedMarkingHandle),
                dragged);
            RefreshMarking(SelectedMarkingId);
        }
        else if (SelectionKind is ExerciseSelectionKind.Marking or ExerciseSelectionKind.MarkingSegment &&
            _dragBeforeMarkingPath is not null && _dragPointerStart is not null)
        {
            _document!.ReplaceMarkingPath(SelectedMarkingId, _dragBeforeMarkingPath);
            _document.MoveMarking(
                SelectedMarkingId,
                dragged.X - _dragPointerStart.X,
                dragged.Y - _dragPointerStart.Y);
            RefreshMarking(SelectedMarkingId);
        }

        _dragChanged = true;
        SelectionChanged?.Invoke();
        QueueRedraw();
        AcceptEvent();
    }

    private void BeginDrag(Point2Dto pointer)
    {
        _dragChanged = false;
        _dragPointerStart = new Point2Dto { X = pointer.X, Y = pointer.Y };
        _dragBeforePoint = null;
        _dragBeforeMarkingPath = null;
        if (SelectionKind == ExerciseSelectionKind.Cone)
            _dragBeforePoint = CopyPoint(_document!.FindCone(SelectedConeId)!.Position);
        else if (SelectionKind == ExerciseSelectionKind.TrajectoryPoint)
            _dragBeforePoint = CopyPoint(_document!.GetTrajectoryPoint(SelectedTrajectoryPointIndex));
        else if (SelectionKind == ExerciseSelectionKind.BezierControl)
        {
            TrajectorySegmentDto segment = _document!.GetTrajectorySegment(SelectedTrajectorySegmentIndex);
            _dragBeforePoint = CopyPoint(SelectedBezierControl == BezierControlKind.Control1 ? segment.Control1! : segment.Control2!);
        }
        else if (SelectionKind is ExerciseSelectionKind.Marking or ExerciseSelectionKind.MarkingSegment or ExerciseSelectionKind.MarkingHandle)
            _dragBeforeMarkingPath = PathEditing.CopyPath(_document!.FindMarking(SelectedMarkingId)!.Path);
    }

    private void CompleteDrag()
    {
        if (!_draggingSelection) return;
        _draggingSelection = false;
        if (_dragChanged)
        {
            string description = SelectionKind switch
            {
                ExerciseSelectionKind.MarkingHandle => $"Move marking {SelectedMarkingHandle}",
                ExerciseSelectionKind.Marking or ExerciseSelectionKind.MarkingSegment => "Move marking",
                ExerciseSelectionKind.Cone => "Move cone",
                ExerciseSelectionKind.BezierControl => "Move trajectory control",
                _ => "Move trajectory point",
            };
            DocumentChanged?.Invoke(description);
        }

        _dragBeforePoint = null;
        _dragBeforeMarkingPath = null;
        _dragPointerStart = null;
        _dragChanged = false;
    }

    private void CancelTransientOperation(bool restoreDrag)
    {
        if (restoreDrag && _draggingSelection && _dragChanged)
        {
            if (_dragBeforeMarkingPath is not null)
            {
                _document?.ReplaceMarkingPath(SelectedMarkingId, _dragBeforeMarkingPath);
                RefreshMarking(SelectedMarkingId);
            }
            else if (_dragBeforePoint is not null && SelectionKind == ExerciseSelectionKind.Cone)
                _document?.MoveCone(SelectedConeId, _dragBeforePoint);
            else if (_dragBeforePoint is not null && SelectionKind == ExerciseSelectionKind.TrajectoryPoint)
                _document?.MoveTrajectoryPoint(SelectedTrajectoryPointIndex, _dragBeforePoint);
            else if (_dragBeforePoint is not null && SelectionKind == ExerciseSelectionKind.BezierControl)
                _document?.MoveBezierControl(SelectedTrajectorySegmentIndex, SelectedBezierControl, _dragBeforePoint);
        }

        _draggingSelection = false;
        _dragChanged = false;
        _dragBeforePoint = null;
        _dragBeforeMarkingPath = null;
        _dragPointerStart = null;
        _buildingMarking = false;
        _markingDraftStart = null;
        _markingPreviewEnd = null;
        _buildingMarkingId = string.Empty;
        SelectionChanged?.Invoke();
        QueueRedraw();
    }

    private void SplitMarkingAt(Vector2 screenPosition)
    {
        if (!TryFindNearestMarkingSegment(screenPosition, out string id, out int segmentIndex, out float parameter))
        {
            MessageRequested?.Invoke("Click a marking segment to split it.", true);
            return;
        }

        SelectMarkingSegment(id, segmentIndex);
        if (SplitSelectedMarkingSegment(parameter))
            MessageRequested?.Invoke("Marking segment split without changing its shape.", false);
    }

    private bool TryFindNearestMarkingSegment(
        Vector2 screenPosition,
        out string markingId,
        out int segmentIndex,
        out float parameter)
    {
        markingId = string.Empty;
        segmentIndex = -1;
        parameter = 0.5f;
        float best = SectionHitTolerancePixels;
        bool bestIsSelected = false;
        foreach (MarkingDto marking in _document!.Definition.Markings.OrderByDescending(item => item.Id == SelectedMarkingId))
        {
            if (!MarkingPathHitTester.TryHitCenterline(marking.Path, screenPosition, DomainToScreen,
                SectionHitTolerancePixels, out int index, out float candidate, out float distance)) continue;
            bool candidateIsSelected = marking.Id == SelectedMarkingId;
            if ((bestIsSelected && !candidateIsSelected) ||
                (candidateIsSelected == bestIsSelected && distance > best)) continue;
            best = distance;
            bestIsSelected = candidateIsSelected;
            markingId = marking.Id;
            segmentIndex = index;
            parameter = candidate;
        }

        return segmentIndex >= 0;
    }

    private void ZoomAt(Vector2 screenPosition, float factor)
    {
        float updated = Mathf.Clamp(
            _pixelsPerMeter * factor,
            MinimumPixelsPerMeter,
            MaximumPixelsPerMeter);
        _panPixels = EditorCanvasMath.ZoomAt(
            screenPosition, Size, _panPixels, _pixelsPerMeter, updated);
        _pixelsPerMeter = updated;
        QueueRedraw();
    }

    private bool HitTestBezierControl(Vector2 screenPosition)
    {
        if (SelectedTrajectorySegmentIndex < 0 ||
            SelectionKind is not (ExerciseSelectionKind.TrajectorySegment or ExerciseSelectionKind.BezierControl))
        {
            return false;
        }

        TrajectorySegmentDto segment = _document!.GetTrajectorySegment(SelectedTrajectorySegmentIndex);
        if (segment.Type != "cubicBezier")
        {
            return false;
        }

        if (screenPosition.DistanceTo(DomainToScreen(segment.Control1!)) <= HandleHitRadiusPixels)
        {
            SelectBezierControl(SelectedTrajectorySegmentIndex, BezierControlKind.Control1);
            return true;
        }

        if (screenPosition.DistanceTo(DomainToScreen(segment.Control2!)) <= HandleHitRadiusPixels)
        {
            SelectBezierControl(SelectedTrajectorySegmentIndex, BezierControlKind.Control2);
            return true;
        }

        return false;
    }

    private bool HitTestTrajectoryPoint(Vector2 screenPosition)
    {
        for (int index = _document!.TrajectoryPointCount - 1; index >= 0; index--)
        {
            if (screenPosition.DistanceTo(DomainToScreen(_document.GetTrajectoryPoint(index))) <= AnchorHitRadiusPixels)
            {
                SelectTrajectoryPoint(index);
                return true;
            }
        }

        return false;
    }

    private bool HitTestTrajectorySection(Vector2 screenPosition)
    {
        IReadOnlyList<TrajectorySectionLocation> sections = _document!.GetTrajectorySections();
        float bestDistance = SectionHitTolerancePixels;
        TrajectorySectionLocation? best = null;
        foreach (TrajectorySectionLocation location in sections)
        {
            TrajectorySegmentDto segment = _document.GetTrajectorySegment(location.SegmentIndex);
            Point2Dto[] renderPoints = segment.Type == "polyline"
                ? [segment.Points![location.SectionIndex], segment.Points[location.SectionIndex + 1]]
                : TrajectoryGeometry.SampleCubicBezier(segment, CubicBezierSubdivisionCount);

            // Curve hit testing uses the same temporary samples as rendering, then
            // measures in screen pixels. Zoom therefore changes detail, not tolerance.
            for (int index = 0; index < renderPoints.Length - 1; index++)
            {
                float distance = DistanceToScreenSegment(
                    screenPosition,
                    DomainToScreen(renderPoints[index]),
                    DomainToScreen(renderPoints[index + 1]));
                if (distance <= bestDistance)
                {
                    bestDistance = distance;
                    best = location;
                }
            }
        }

        if (best is null)
        {
            return false;
        }

        SelectTrajectorySection(best.Value);
        return true;
    }

    private bool HitTestCone(Vector2 screenPosition)
    {
        for (int index = _document!.Definition.Cones.Length - 1; index >= 0; index--)
        {
            ConeDto cone = _document.Definition.Cones[index];
            if (screenPosition.DistanceTo(DomainToScreen(cone.Position)) <= ConeHitRadiusPixels)
            {
                SelectCone(cone.Id);
                return true;
            }
        }

        return false;
    }

    private bool HitTestMarkingHandle(Vector2 screenPosition)
    {
        if (string.IsNullOrEmpty(SelectedMarkingId))
        {
            return false;
        }

        MarkingDto? marking = _document!.FindMarking(SelectedMarkingId);
        if (marking is null)
        {
            return false;
        }

        var handles = MarkingPathHitTester.EnumerateHandles(marking.Path).ToArray();
        // The selected handle wins ties, followed by later overlay handles which
        // are drawn last and are therefore visually on top.
        foreach (MarkingHandleLocation handle in handles
            .OrderBy(item => item.SegmentIndex == SelectedMarkingSegmentIndex &&
                item.Kind == SelectedMarkingHandle ? 0 : 1))
        {
            if (screenPosition.DistanceTo(DomainToScreen(handle.Point)) <= HandleHitRadiusPixels)
            {
                SelectMarkingHandle(marking.Id, handle.SegmentIndex, handle.Kind);
                return true;
            }
        }

        return false;
    }

    private bool HitTestMarking(Vector2 screenPosition)
    {
        float bestDistance = float.MaxValue;
        string? bestId = null;
        int bestSegment = -1;
        bool bestIsSelected = false;
        foreach (MarkingDto marking in _document!.Definition.Markings
            .OrderByDescending(candidate => candidate.Id == SelectedMarkingId))
        {
            float tolerance = MathF.Max(SectionHitTolerancePixels, marking.WidthMeters * _pixelsPerMeter / 2.0f + 5.0f);
            if (!_markingBoundsCache.TryGetValue(marking.Id, out PathBounds bounds))
            {
                bounds = PathBoundsCalculator.Calculate(marking.Path, marking.WidthMeters);
                _markingBoundsCache[marking.Id] = bounds;
            }
            Point2Dto domainPointer = ScreenToDomain(screenPosition);
            float toleranceMeters = tolerance / _pixelsPerMeter;
            if (domainPointer.X < bounds.MinX - toleranceMeters || domainPointer.X > bounds.MaxX + toleranceMeters ||
                domainPointer.Y < bounds.MinY - toleranceMeters || domainPointer.Y > bounds.MaxY + toleranceMeters)
                continue;
            // Hit testing follows the persisted path, not temporary dash samples.
            // A user can therefore select a dashed/dotted marking even when the
            // pointer happens to be over one of its visual gaps.
            for (int segmentIndex = 0; segmentIndex < marking.Path.Segments.Length; segmentIndex++)
            {
                Point2Dto start = PathEditing.GetSegmentStart(marking.Path, segmentIndex);
                var segmentPath = new PathDefinition
                {
                    Start = start,
                    Segments = [marking.Path.Segments[segmentIndex]],
                };
                Point2Dto[] points = PathSampler.Sample(segmentPath).Points;
                for (int pointIndex = 0; pointIndex < points.Length - 1; pointIndex++)
                {
                    float distance = DistanceToScreenSegment(
                        screenPosition,
                        DomainToScreen(points[pointIndex]),
                        DomainToScreen(points[pointIndex + 1]));
                    bool candidateIsSelected = marking.Id == SelectedMarkingId;
                    if (distance <= tolerance &&
                        ((candidateIsSelected && !bestIsSelected) || candidateIsSelected == bestIsSelected && distance < bestDistance))
                    {
                        bestDistance = distance;
                        bestId = marking.Id;
                        bestSegment = segmentIndex;
                        bestIsSelected = candidateIsSelected;
                    }
                }
            }
        }

        if (bestId is null)
        {
            return false;
        }

        SelectMarkingSegment(bestId, bestSegment);
        return true;
    }

    private void SelectBezierControl(int segmentIndex, BezierControlKind control)
    {
        SelectedBezierControl = control;
        SetSelection(ExerciseSelectionKind.BezierControl, string.Empty, string.Empty, -1, segmentIndex, 0);
    }

    private void SetSelection(
        ExerciseSelectionKind kind,
        string coneId,
        string markingId,
        int trajectoryPointIndex,
        int segmentIndex,
        int sectionIndex)
    {
        SelectionKind = kind;
        if (kind is not (ExerciseSelectionKind.Marking or ExerciseSelectionKind.MarkingSegment or ExerciseSelectionKind.MarkingHandle))
            MarkingSelection = MarkingSelection.None;
        SelectedConeId = coneId;
        SelectedMarkingId = markingId;
        SelectedTrajectoryPointIndex = trajectoryPointIndex;
        SelectedTrajectorySegmentIndex = segmentIndex;
        SelectedTrajectorySectionIndex = sectionIndex;
        SelectionChanged?.Invoke();
        QueueRedraw();
    }

    private void DrawGridAndOrigin()
    {
        Point2Dto topLeft = ScreenToDomain(Vector2.Zero);
        Point2Dto bottomRight = ScreenToDomain(Size);
        int minimumX = Mathf.FloorToInt(MathF.Min(topLeft.X, bottomRight.X)) - 1;
        int maximumX = Mathf.CeilToInt(MathF.Max(topLeft.X, bottomRight.X)) + 1;
        int minimumY = Mathf.FloorToInt(MathF.Min(topLeft.Y, bottomRight.Y)) - 1;
        int maximumY = Mathf.CeilToInt(MathF.Max(topLeft.Y, bottomRight.Y)) + 1;

        for (int x = minimumX; x <= maximumX; x++)
        {
            bool major = x % 5 == 0;
            float screenX = DomainToScreen(new Point2Dto { X = x }).X;
            DrawLine(new Vector2(screenX, 0.0f), new Vector2(screenX, Size.Y),
                major ? _majorGridColor : _minorGridColor, major ? 2.0f : 1.0f);
        }

        for (int y = minimumY; y <= maximumY; y++)
        {
            bool major = y % 5 == 0;
            float screenY = DomainToScreen(new Point2Dto { Y = y }).Y;
            DrawLine(new Vector2(0.0f, screenY), new Vector2(Size.X, screenY),
                major ? _majorGridColor : _minorGridColor, major ? 2.0f : 1.0f);
        }

        Vector2 origin = DomainToScreen(new Point2Dto());
        DrawLine(new Vector2(origin.X, 0.0f), new Vector2(origin.X, Size.Y), new Color(0.94f, 0.3f, 0.26f), 2.5f);
        DrawLine(new Vector2(0.0f, origin.Y), new Vector2(Size.X, origin.Y), new Color(0.26f, 0.68f, 1.0f), 2.5f);
        DrawCircle(origin, 5.0f, Colors.White);
        DrawString(ThemeDB.FallbackFont, origin + new Vector2(8.0f, -8.0f), "origin",
            HorizontalAlignment.Left, -1, 13, Colors.White);
    }

    private void DrawBounds(ExerciseBoundsDto bounds)
    {
        Vector2 topLeft = DomainToScreen(new Point2Dto { X = -bounds.Width / 2.0f, Y = bounds.Length / 2.0f });
        Vector2 bottomRight = DomainToScreen(new Point2Dto { X = bounds.Width / 2.0f, Y = -bounds.Length / 2.0f });
        DrawRect(new Rect2(topLeft, bottomRight - topLeft), new Color(0.95f, 0.83f, 0.25f, 0.08f), true);
        DrawRect(new Rect2(topLeft, bottomRight - topLeft), new Color(0.95f, 0.83f, 0.25f), false, 3.0f);
    }

    private void DrawMarkings()
    {
        foreach (MarkingDto marking in _document!.Definition.Markings)
        {
            Color color = ResolveMarkingColor(marking.Color);
            // visibleInViewer is an export/runtime choice, not Editor visibility.
            // Reduced opacity communicates the hidden state without making the
            // marking impossible to select or restore.
            if (!marking.VisibleInViewer)
            {
                color.A *= 0.32f;
            }

            bool selected = SelectedMarkingId == marking.Id &&
                SelectionKind is ExerciseSelectionKind.Marking or ExerciseSelectionKind.MarkingSegment or ExerciseSelectionKind.MarkingHandle;
            float widthPixels = MathF.Max(1.0f, marking.WidthMeters * _pixelsPerMeter);
            if (!_markingGeometryCache.TryGetValue(marking.Id, out MarkingStyleGeometry? geometry))
            {
                geometry = MarkingGeometry.CreateStyleGeometry(PathSampler.Sample(marking.Path), marking.Style);
                _markingGeometryCache[marking.Id] = geometry;
            }
            foreach (MarkingStroke stroke in geometry.Strokes)
            {
                Vector2 start = DomainToScreen(stroke.Start);
                Vector2 end = DomainToScreen(stroke.End);
                if (selected)
                {
                    DrawLine(start, end, new Color(1.0f, 1.0f, 1.0f, 0.55f), widthPixels + 5.0f, true);
                }

                DrawLine(start, end, color, widthPixels, true);
            }
            foreach (Point2Dto dot in geometry.Dots)
            {
                float radius = MathF.Max(1.0f, widthPixels * 0.5f);
                Vector2 center = DomainToScreen(dot);
                if (selected) DrawCircle(center, radius + 2.5f, new Color(1, 1, 1, 0.55f));
                DrawCircle(center, radius, color);
            }

            if (!selected)
            {
                continue;
            }

            DrawMarkingHandles(marking, color);
        }
    }

    private void DrawMarkingHandles(MarkingDto marking, Color color)
    {
        MarkingHandleOverlay.Draw(this, marking, MarkingSelection, color, DomainToScreen);
    }

    private void DrawMarkingPreview()
    {
        if (_markingPreviewEnd is null) return;
        Point2Dto? start = string.IsNullOrEmpty(_buildingMarkingId) ? _markingDraftStart : null;
        if (start is null && !string.IsNullOrEmpty(SelectedMarkingId))
            start = _document?.FindMarking(SelectedMarkingId) is { } marking ? PathEditing.GetPathEnd(marking.Path) : null;
        if (start is null || PathEditing.PointsEqual(start, _markingPreviewEnd)) return;

        var preview = new PathDefinition { Start = start, Segments = [] };
        if (Tool == ExerciseEditorTool.AppendCubicBezier) PathEditing.AppendCubic(preview, _markingPreviewEnd);
        else PathEditing.AppendLine(preview, _markingPreviewEnd);
        Vector2[] screen = PathSampler.Sample(preview).Points.Select(DomainToScreen).ToArray();
        DrawPolyline(screen, new Color(1.0f, 0.82f, 0.2f, 0.85f), 3.0f, true);
        DrawCircle(DomainToScreen(_markingPreviewEnd), 5.0f, Colors.White);
    }

    private void DrawCones(IEnumerable<ConeDto> cones, ExerciseBoundsDto bounds)
    {
        foreach (ConeDto cone in cones)
        {
            Vector2 position = DomainToScreen(cone.Position);
            bool outside = MathF.Abs(cone.Position.X) > bounds.Width / 2.0f ||
                MathF.Abs(cone.Position.Y) > bounds.Length / 2.0f;
            DrawCircle(position, 8.0f, ResolveConeColor(cone.Color));
            DrawArc(position, 8.0f, 0.0f, Mathf.Tau, 24, outside ? Colors.Red : Colors.Black, 2.0f);
            if (SelectionKind == ExerciseSelectionKind.Cone && SelectedConeId == cone.Id)
            {
                DrawArc(position, 13.0f, 0.0f, Mathf.Tau, 32, Colors.White, 2.0f);
            }
        }
    }

    private void DrawTrajectory()
    {
        foreach (TrajectorySectionLocation location in _document!.GetTrajectorySections())
        {
            TrajectorySegmentDto segment = _document.GetTrajectorySegment(location.SegmentIndex);
            Point2Dto[] renderPoints = segment.Type == "polyline"
                ? [segment.Points![location.SectionIndex], segment.Points[location.SectionIndex + 1]]
                : TrajectoryGeometry.SampleCubicBezier(segment, CubicBezierSubdivisionCount);
            var screenPoints = renderPoints.Select(DomainToScreen).ToArray();
            bool selected = SelectedTrajectorySegmentIndex == location.SegmentIndex &&
                SelectedTrajectorySectionIndex == location.SectionIndex &&
                SelectionKind is ExerciseSelectionKind.TrajectorySegment or ExerciseSelectionKind.BezierControl;
            Color color = selected ? new Color(1.0f, 0.72f, 0.16f) : new Color(0.12f, 0.9f, 0.95f);
            DrawPolyline(screenPoints, color, selected ? 6.0f : 3.0f, true);

            int middle = Math.Max(0, (screenPoints.Length - 1) / 2);
            DrawDirectionMarker(screenPoints[middle], screenPoints[Math.Min(middle + 1, screenPoints.Length - 1)]);
        }

        for (int index = 0; index < _document.TrajectoryPointCount; index++)
        {
            DrawTrajectoryAnchor(DomainToScreen(_document.GetTrajectoryPoint(index)), index, _document.TrajectoryPointCount);
        }

        DrawSelectedBezierHandles();
    }

    private void DrawSelectedBezierHandles()
    {
        if (SelectedTrajectorySegmentIndex < 0 ||
            SelectionKind is not (ExerciseSelectionKind.TrajectorySegment or ExerciseSelectionKind.BezierControl))
        {
            return;
        }

        TrajectorySegmentDto segment = _document!.GetTrajectorySegment(SelectedTrajectorySegmentIndex);
        if (segment.Type != "cubicBezier")
        {
            return;
        }

        Vector2 start = DomainToScreen(segment.Start!);
        Vector2 control1 = DomainToScreen(segment.Control1!);
        Vector2 control2 = DomainToScreen(segment.Control2!);
        Vector2 end = DomainToScreen(segment.End!);
        Color helper = new(0.7f, 0.72f, 0.78f, 0.9f);
        DrawLine(start, control1, helper, 1.5f, true);
        DrawLine(end, control2, helper, 1.5f, true);
        DrawControlHandle(control1, BezierControlKind.Control1);
        DrawControlHandle(control2, BezierControlKind.Control2);
    }

    private void DrawControlHandle(Vector2 center, BezierControlKind control)
    {
        bool selected = SelectionKind == ExerciseSelectionKind.BezierControl && SelectedBezierControl == control;
        DrawRect(new Rect2(center - new Vector2(6.0f, 6.0f), new Vector2(12.0f, 12.0f)),
            selected ? Colors.White : new Color(1.0f, 0.55f, 0.16f), true);
        DrawRect(new Rect2(center - new Vector2(6.0f, 6.0f), new Vector2(12.0f, 12.0f)), Colors.Black, false, 2.0f);
    }

    private void DrawDirectionMarker(Vector2 start, Vector2 end)
    {
        Vector2 delta = end - start;
        if (delta.Length() < 2.0f)
        {
            return;
        }

        Vector2 direction = delta.Normalized();
        Vector2 perpendicular = new(-direction.Y, direction.X);
        Vector2 tip = (start + end) / 2.0f + direction * 6.0f;
        Vector2 tail = tip - direction * 12.0f;
        Color color = new(0.05f, 0.2f, 0.24f);
        DrawLine(tail + perpendicular * 5.0f, tip, color, 2.0f, true);
        DrawLine(tail - perpendicular * 5.0f, tip, color, 2.0f, true);
    }

    private void DrawTrajectoryAnchor(Vector2 center, int index, int pointCount)
    {
        bool entry = index == 0;
        bool exit = index == pointCount - 1;
        Color color = entry
            ? new Color(0.2f, 0.95f, 0.38f)
            : exit ? new Color(0.95f, 0.3f, 0.78f) : new Color(0.15f, 0.82f, 0.95f);

        if (entry || exit)
        {
            Vector2[] diamond =
            [
                center + new Vector2(0.0f, -10.0f),
                center + new Vector2(10.0f, 0.0f),
                center + new Vector2(0.0f, 10.0f),
                center + new Vector2(-10.0f, 0.0f),
            ];
            DrawColoredPolygon(diamond, color);
            DrawPolyline([.. diamond, diamond[0]], Colors.Black, 2.0f);
            DrawString(ThemeDB.FallbackFont, center + new Vector2(13.0f, 5.0f), entry ? "Entry" : "Exit",
                HorizontalAlignment.Left, -1, 13, color);
        }
        else
        {
            DrawCircle(center, 7.0f, color);
            DrawArc(center, 7.0f, 0.0f, Mathf.Tau, 20, Colors.Black, 2.0f);
        }

        if (SelectionKind == ExerciseSelectionKind.TrajectoryPoint && SelectedTrajectoryPointIndex == index)
        {
            DrawArc(center, 15.0f, 0.0f, Mathf.Tau, 32, Colors.White, 2.5f);
        }
    }

    private Vector2 DomainToScreen(Point2Dto point)
    {
        return EditorCanvasMath.DomainToScreen(point, Size, _panPixels, _pixelsPerMeter);
    }

    private Point2Dto ScreenToDomain(Vector2 screenPosition)
    {
        /*
         * This one inverse transform is shared by cones, anchors, handles, segment
         * hit testing and construction: remove centre/pan, divide by zoom, then
         * restore the upward local Y axis. One resulting unit is one metre.
         */
        return EditorCanvasMath.ScreenToDomain(
            screenPosition, Size, _panPixels, _pixelsPerMeter);
    }

    private static Point2Dto Snap(Point2Dto point)
    {
        return EditorCanvasMath.Snap(point, SnapStepMeters);
    }

    private Point2Dto ResolveSnap(Vector2 screenPosition, bool bypassSnap) =>
        EditorCanvasMath.ResolveDragPosition(ScreenToDomain(screenPosition), SnapStepMeters, bypassSnap);

    private static MarkingPathCoordinateKind ToCoordinateKind(MarkingHandleKind kind) => kind switch
    {
        MarkingHandleKind.PathStart => MarkingPathCoordinateKind.PathStart,
        MarkingHandleKind.SegmentEnd => MarkingPathCoordinateKind.SegmentEnd,
        MarkingHandleKind.Control1 => MarkingPathCoordinateKind.Control1,
        MarkingHandleKind.Control2 => MarkingPathCoordinateKind.Control2,
        _ => throw new InvalidOperationException("The marking selection does not address a coordinate."),
    };

    private static Point2Dto CopyPoint(Point2Dto point) => new() { X = point.X, Y = point.Y };

    private static float DistanceToScreenSegment(Vector2 point, Vector2 start, Vector2 end)
    {
        return EditorCanvasMath.DistanceToSegment(point, start, end);
    }

    private static Color ResolveConeColor(string color)
    {
        return color switch
        {
            "blue" => new Color(0.18f, 0.45f, 1.0f),
            "yellow" => new Color(1.0f, 0.82f, 0.12f),
            "orange" => new Color(1.0f, 0.42f, 0.08f),
            "none" => new Color(0.82f, 0.34f, 0.08f),
            _ => new Color(0.95f, 0.18f, 0.14f),
        };
    }

    private static Color ResolveMarkingColor(string value)
    {
        if (!MarkingGeometry.TryNormalizeColor(value, allowLegacyNames: true, out string canonical))
        {
            return Colors.White;
        }

        byte red = Convert.ToByte(canonical.Substring(1, 2), 16);
        byte green = Convert.ToByte(canonical.Substring(3, 2), 16);
        byte blue = Convert.ToByte(canonical.Substring(5, 2), 16);
        return new Color(red / 255.0f, green / 255.0f, blue / 255.0f);
    }
}
