using Godot;
using MotoGymkhanaTrainer.Tracks;

namespace MotoGymkhanaTrainer.VenueEditor;

/// <summary>Explicit Venue authoring tools; active state is displayed by the host toolbar.</summary>
public enum VenueTool { Select, AddCone, AddMarking, AddLine, AddCubicBezier }

/// <summary>Kind of stable editor-only Venue selection.</summary>
public enum VenueSelectionKind { None, Object, Cone, Marking, MarkingSegment, MarkingHandle }

/// <summary>Top-down Venue canvas using shared curved-marking editor services.</summary>
public partial class VenueEditorCanvas : Control
{
    private const float MinimumPixelsPerMeter = 3;
    private const float MaximumPixelsPerMeter = 120;
    private const float SnapMeters = 0.25f;
    private const float HitPixels = 10;
    private const float HandleHitPixels = 13;
    private VenueDocument _document = VenueDocument.CreateNew();
    private readonly HashSet<string> _lockedObjects = new(StringComparer.Ordinal);
    private readonly HashSet<string> _unresolvedObjects = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MarkingStyleGeometry> _markingGeometryCache = new(StringComparer.Ordinal);
    private Vector2 _panPixels;
    private float _pixelsPerMeter = 10;
    private bool _panning;
    private bool _dragging;
    private VenueTool _tool;
    private VenueSelectionKind _selectionKind;
    private string? _selectedId;
    private MarkingSelection _markingSelection = MarkingSelection.None;
    private PathDefinition? _draftPath;
    private string _buildingMarkingId = string.Empty;
    private Point2Dto? _previewEnd;
    private PathDefinition? _dragBeforeMarkingPath;
    private Point2Dto? _dragBeforePoint;
    private Point2Dto? _dragPointerStart;
    private PopupMenu? _objectContextMenu;

    [Signal] public delegate void SelectionChangedEventHandler();
    [Signal] public delegate void DocumentChangedEventHandler();
    public event Action<string>? EditTransactionStarted;
    public event Action? EditTransactionFinished;
    public event Action? EditTransactionCanceled;
    public event Action<VenueTool>? ToolChanged;
    public event Action<string>? DuplicateRequested;
    public event Action<string>? LockedTransformAttempted;

    public VenueSelectionKind SelectionKind => _selectionKind;
    public string? SelectedId => _selectedId;
    public MarkingSelection MarkingSelection => _markingSelection;
    public int SelectedSegmentIndex => _markingSelection.SegmentIndex;
    public MarkingHandleKind SelectedHandleKind => _markingSelection.HandleKind;
    public int SelectedPointIndex => _markingSelection.HandleKind switch
    {
        MarkingHandleKind.PathStart => 0,
        MarkingHandleKind.SegmentEnd => _markingSelection.SegmentIndex + 1,
        _ => -1,
    };
    public VenueTool Tool => _tool;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        FocusMode = FocusModeEnum.Click;
        Resized += QueueRedraw;
        _objectContextMenu = new PopupMenu { Name = "VenueObjectContextMenu" };
        _objectContextMenu.AddItem("Duplicate Object", 1);
        _objectContextMenu.IdPressed += _ => { if (_selectedId is not null) DuplicateRequested?.Invoke(_selectedId); };
        AddChild(_objectContextMenu);
        Callable.From(FitAreaInView).CallDeferred();
    }

    public void SetDocument(VenueDocument document, bool resetView = true)
    {
        _document = document;
        ClearSelection();
        ClearTransientCreation();
        _markingGeometryCache.Clear();
        if (resetView) Callable.From(FitAreaInView).CallDeferred();
        QueueRedraw();
    }

    public void SetTool(VenueTool tool)
    {
        CancelTransientOperation();
        _tool = tool;
        ToolChanged?.Invoke(tool);
        QueueRedraw();
    }

    public void SetLockedObjects(IEnumerable<string> ids) { _lockedObjects.Clear(); _lockedObjects.UnionWith(ids); QueueRedraw(); }
    public void SetUnresolvedObjects(IEnumerable<string> ids) { _unresolvedObjects.Clear(); _unresolvedObjects.UnionWith(ids); QueueRedraw(); }
    public void RefreshMarking(string? id) { if (id is not null) _markingGeometryCache.Remove(id); QueueRedraw(); }

    public void SelectObject(string? id) => Select(VenueSelectionKind.Object,
        id is not null && _document.FindObject(id) is not null ? id : null, MarkingSelection.None);

    /// <summary>Restores stable selection identity after history replaces DTO instances.</summary>
    public void RestoreSelection(VenueSelectionKind kind, string? id, MarkingSelection markingSelection)
    {
        bool valid = kind switch
        {
            VenueSelectionKind.Object => id is not null && _document.FindObject(id) is not null,
            VenueSelectionKind.Cone => id is not null && _document.FindCone(id) is not null,
            VenueSelectionKind.Marking or VenueSelectionKind.MarkingSegment or VenueSelectionKind.MarkingHandle =>
                id is not null && _document.FindMarking(id) is not null,
            _ => false,
        };
        if (!valid) { ClearSelection(); return; }
        MarkingSelection sanitized = kind is VenueSelectionKind.Marking or VenueSelectionKind.MarkingSegment or VenueSelectionKind.MarkingHandle
            ? markingSelection.Sanitize(_document.FindMarking(id)!.Path) : MarkingSelection.None;
        Select(kind, id, sanitized);
    }

    /// <summary>Finishes current Path creation without altering confirmed segments.</summary>
    public bool FinishMarking()
    {
        bool hadOperation = _draftPath is not null || !string.IsNullOrEmpty(_buildingMarkingId);
        if (!string.IsNullOrEmpty(_buildingMarkingId) && _document.FindMarking(_buildingMarkingId) is MarkingDto marking)
            Select(VenueSelectionKind.Marking, marking.Id, new MarkingSelection(marking.Id, -1, MarkingHandleKind.None));
        ClearTransientCreation();
        _tool = VenueTool.Select;
        ToolChanged?.Invoke(_tool);
        QueueRedraw();
        return hadOperation;
    }

    /// <summary>Cancels an active drag or transient Path and restores its before-state.</summary>
    public bool CancelTransientOperation()
    {
        bool canceled = false;
        if (_dragging)
        {
            RestoreDragBeforeState();
            _dragging = false;
            EditTransactionCanceled?.Invoke();
            canceled = true;
        }
        if (_draftPath is not null || !string.IsNullOrEmpty(_buildingMarkingId))
        {
            ClearTransientCreation();
            canceled = true;
        }
        if (canceled)
        {
            _tool = VenueTool.Select;
            ToolChanged?.Invoke(_tool);
            QueueRedraw();
        }
        return canceled;
    }

    public void FitAreaInView()
    {
        if (Size.X <= 1 || Size.Y <= 1) return;
        _panPixels = Vector2.Zero;
        _pixelsPerMeter = EditorCanvasMath.FitPixelsPerMeter(_document.Definition.Area.Width,
            _document.Definition.Area.Length, Size, 28, MinimumPixelsPerMeter, MaximumPixelsPerMeter);
        QueueRedraw();
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton button)
        {
            if (button.ButtonIndex == MouseButton.Middle) { _panning = button.Pressed; AcceptEvent(); }
            else if (button.Pressed && button.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
            {
                float old = _pixelsPerMeter;
                _pixelsPerMeter = Mathf.Clamp(old * (button.ButtonIndex == MouseButton.WheelUp ? 1.12f : 1 / 1.12f),
                    MinimumPixelsPerMeter, MaximumPixelsPerMeter);
                _panPixels = EditorCanvasMath.ZoomAt(button.Position, Size, _panPixels, old, _pixelsPerMeter);
                QueueRedraw(); AcceptEvent();
            }
            else if (button.ButtonIndex == MouseButton.Left)
            {
                if (button.Pressed && button.DoubleClick && IsPathTool(_tool)) FinishMarking();
                else if (button.Pressed) HandleLeftPress(button.Position, button.CtrlPressed);
                else EndDrag();
                AcceptEvent();
            }
            else if (button.Pressed && button.ButtonIndex == MouseButton.Right &&
                _selectionKind == VenueSelectionKind.Object && _selectedId is not null)
            {
                Vector2 popupPosition = GetScreenTransform() * button.Position;
                _objectContextMenu!.Position = new Vector2I(Mathf.RoundToInt(popupPosition.X), Mathf.RoundToInt(popupPosition.Y));
                _objectContextMenu.Popup(); AcceptEvent();
            }
        }
        else if (@event is InputEventMouseMotion motion)
        {
            if (_panning) { _panPixels += motion.Relative; QueueRedraw(); }
            else if (_dragging) DragTo(ResolveSnap(motion.Position, motion.CtrlPressed));
            else if (IsPathTool(_tool)) { _previewEnd = ResolveSnap(motion.Position, motion.CtrlPressed); QueueRedraw(); }
        }
    }

    public override void _Draw()
    {
        DrawGridAndArea();
        foreach (MarkingDto marking in _document.Definition.Markings) DrawMarking(marking);
        foreach (VenueObjectInstanceDto item in _document.Definition.Objects) DrawObject(item);
        foreach (ConeDto cone in _document.Definition.Cones) DrawCone(cone);
        DrawMarkingPreview();
    }

    private void HandleLeftPress(Vector2 screen, bool bypassSnap)
    {
        Point2Dto point = ResolveSnap(screen, bypassSnap);
        if (_tool == VenueTool.AddCone)
        {
            ConeDto cone = _document.AddCone(point);
            Select(VenueSelectionKind.Cone, cone.Id, MarkingSelection.None);
            EmitSignal(SignalName.DocumentChanged);
            return;
        }
        if (IsPathTool(_tool)) { AddPathPoint(point); return; }
        HitTest(screen);
        if (_selectionKind == VenueSelectionKind.None || _selectedId is null) return;
        if (_selectionKind == VenueSelectionKind.Object && _lockedObjects.Contains(_selectedId))
        { LockedTransformAttempted?.Invoke(_selectedId); return; }
        _dragging = true;
        _dragPointerStart = point;
        _dragBeforeMarkingPath = _document.FindMarking(_selectedId) is MarkingDto marking ? PathEditing.CopyPath(marking.Path) : null;
        _dragBeforePoint = _selectionKind switch
        {
            VenueSelectionKind.Object => _document.FindObject(_selectedId)?.Position,
            VenueSelectionKind.Cone => _document.FindCone(_selectedId)?.Position,
            _ => null,
        };
        EditTransactionStarted?.Invoke(_selectionKind is VenueSelectionKind.MarkingHandle ? "Move marking handle" :
            _selectionKind is VenueSelectionKind.Marking or VenueSelectionKind.MarkingSegment ? "Move marking" : "Move Venue item");
    }

    private void AddPathPoint(Point2Dto point)
    {
        bool cubic = _tool == VenueTool.AddCubicBezier;
        if (_tool == VenueTool.AddMarking) cubic = false;
        if (_draftPath is null && string.IsNullOrEmpty(_buildingMarkingId) &&
            (_tool == VenueTool.AddMarking || string.IsNullOrEmpty(_markingSelection.MarkingId)))
        {
            _draftPath = new PathDefinition { Start = Copy(point), Segments = [] };
            _previewEnd = point;
            QueueRedraw();
            return;
        }
        MarkingDto? marking = string.IsNullOrEmpty(_buildingMarkingId)
            ? _document.FindMarking(_markingSelection.MarkingId) : _document.FindMarking(_buildingMarkingId);
        if (marking is null && _draftPath is not null)
        {
            PathDefinition candidate = PathEditing.CopyPath(_draftPath);
            int index = cubic ? PathEditing.AppendCubic(candidate, point) : PathEditing.AppendLine(candidate, point);
            if (index < 0) return;
            marking = _document.AddMarking(candidate);
            _buildingMarkingId = marking.Id;
            _draftPath = null;
            RefreshMarking(marking.Id);
            Select(VenueSelectionKind.MarkingSegment, marking.Id,
                new MarkingSelection(marking.Id, index, MarkingHandleKind.None));
            EmitSignal(SignalName.DocumentChanged);
            return;
        }
        if (marking is null) return;
        int appended = _document.AppendMarkingSegment(marking.Id, point, cubic);
        if (appended < 0) return;
        _buildingMarkingId = marking.Id;
        RefreshMarking(marking.Id);
        Select(cubic ? VenueSelectionKind.MarkingHandle : VenueSelectionKind.MarkingSegment, marking.Id,
            new MarkingSelection(marking.Id, appended, cubic ? MarkingHandleKind.Control1 : MarkingHandleKind.None));
        EmitSignal(SignalName.DocumentChanged);
    }

    private void EndDrag()
    {
        if (!_dragging) return;
        _dragging = false;
        ClearDragState();
        EditTransactionFinished?.Invoke();
    }

    private void DragTo(Point2Dto point)
    {
        if (_selectedId is null) return;
        bool changed = true;
        switch (_selectionKind)
        {
            case VenueSelectionKind.Object: _document.MoveObject(_selectedId, point); break;
            case VenueSelectionKind.Cone: _document.MoveCone(_selectedId, point); break;
            case VenueSelectionKind.MarkingHandle:
                changed = _document.MoveMarkingCoordinate(_selectedId, _markingSelection.SegmentIndex,
                    ToCoordinateKind(_markingSelection.HandleKind), point);
                break;
            case VenueSelectionKind.Marking:
            case VenueSelectionKind.MarkingSegment:
                if (_dragBeforeMarkingPath is null || _dragPointerStart is null) return;
                _document.ReplaceMarkingPath(_selectedId, _dragBeforeMarkingPath);
                changed = _document.MoveMarking(_selectedId, point.X - _dragPointerStart.X, point.Y - _dragPointerStart.Y);
                break;
            default: return;
        }
        if (!changed) return;
        RefreshMarking(_selectedId);
        EmitSignal(SignalName.DocumentChanged);
    }

    private void RestoreDragBeforeState()
    {
        if (_selectedId is null) return;
        if (_dragBeforeMarkingPath is not null) _document.ReplaceMarkingPath(_selectedId, _dragBeforeMarkingPath);
        else if (_dragBeforePoint is not null && _selectionKind == VenueSelectionKind.Object) _document.MoveObject(_selectedId, _dragBeforePoint);
        else if (_dragBeforePoint is not null && _selectionKind == VenueSelectionKind.Cone) _document.MoveCone(_selectedId, _dragBeforePoint);
        RefreshMarking(_selectedId);
        EmitSignal(SignalName.DocumentChanged);
        ClearDragState();
    }

    private void ClearDragState() { _dragBeforeMarkingPath = null; _dragBeforePoint = null; _dragPointerStart = null; }
    private void ClearTransientCreation() { _draftPath = null; _buildingMarkingId = string.Empty; _previewEnd = null; }

    /* Handles and Path centerlines win before cone/object selection. */
    private void HitTest(Vector2 screen)
    {
        if (!string.IsNullOrEmpty(_markingSelection.MarkingId) &&
            _document.FindMarking(_markingSelection.MarkingId) is MarkingDto selected)
        {
            foreach (MarkingHandleLocation handle in MarkingPathHitTester.EnumerateHandles(selected.Path)
                .OrderBy(item => item.SegmentIndex == _markingSelection.SegmentIndex && item.Kind == _markingSelection.HandleKind ? 0 : 1))
                if (screen.DistanceTo(ToScreen(handle.Point)) <= HandleHitPixels)
                {
                    Select(VenueSelectionKind.MarkingHandle, selected.Id,
                        new MarkingSelection(selected.Id, handle.SegmentIndex, handle.Kind));
                    return;
                }
        }
        string? bestId = null; int bestSegment = -1; float bestDistance = float.MaxValue; bool bestSelected = false;
        foreach (MarkingDto marking in _document.Definition.Markings.Reverse())
        {
            float tolerance = MathF.Max(HitPixels, marking.WidthMeters * _pixelsPerMeter * 0.5f + 5);
            if (!MarkingPathHitTester.TryHitCenterline(marking.Path, screen, ToScreen, tolerance,
                out int segment, out _, out float distance)) continue;
            bool isSelected = marking.Id == _markingSelection.MarkingId;
            if ((bestSelected && !isSelected) || (bestSelected == isSelected && distance >= bestDistance)) continue;
            bestId = marking.Id; bestSegment = segment; bestDistance = distance; bestSelected = isSelected;
        }
        if (bestId is not null)
        {
            Select(VenueSelectionKind.MarkingSegment, bestId,
                new MarkingSelection(bestId, bestSegment, MarkingHandleKind.None));
            return;
        }
        foreach (ConeDto cone in _document.Definition.Cones.Reverse())
            if (screen.DistanceTo(ToScreen(cone.Position)) <= HitPixels)
            { Select(VenueSelectionKind.Cone, cone.Id, MarkingSelection.None); return; }
        Point2Dto domain = ToDomain(screen);
        foreach (VenueObjectInstanceDto item in _document.Definition.Objects.Reverse())
            if (VenueGeometry.Contains(VenueGeometry.TransformFootprint(item), domain))
            { Select(VenueSelectionKind.Object, item.ObjectId, MarkingSelection.None); return; }
        ClearSelection();
    }

    private void Select(VenueSelectionKind kind, string? id, MarkingSelection markingSelection)
    {
        _selectionKind = id is null ? VenueSelectionKind.None : kind;
        _selectedId = id;
        _markingSelection = id is null ? MarkingSelection.None : markingSelection;
        EmitSignal(SignalName.SelectionChanged);
        QueueRedraw();
    }
    private void ClearSelection() => Select(VenueSelectionKind.None, null, MarkingSelection.None);

    private void DrawGridAndArea()
    {
        float halfWidth = _document.Definition.Area.Width * 0.5f;
        float halfLength = _document.Definition.Area.Length * 0.5f;
        for (int x = Mathf.CeilToInt(-halfWidth); x <= Mathf.FloorToInt(halfWidth); x++)
            DrawLine(ToScreen(new Point2Dto { X = x, Y = -halfLength }), ToScreen(new Point2Dto { X = x, Y = halfLength }),
                x % 5 == 0 ? new Color("59636F") : new Color("343A43"), x % 5 == 0 ? 2 : 1);
        for (int y = Mathf.CeilToInt(-halfLength); y <= Mathf.FloorToInt(halfLength); y++)
            DrawLine(ToScreen(new Point2Dto { X = -halfWidth, Y = y }), ToScreen(new Point2Dto { X = halfWidth, Y = y }),
                y % 5 == 0 ? new Color("59636F") : new Color("343A43"), y % 5 == 0 ? 2 : 1);
        Vector2[] corners = [ToScreen(new Point2Dto { X = -halfWidth, Y = -halfLength }),
            ToScreen(new Point2Dto { X = halfWidth, Y = -halfLength }), ToScreen(new Point2Dto { X = halfWidth, Y = halfLength }),
            ToScreen(new Point2Dto { X = -halfWidth, Y = halfLength })];
        DrawClosed(corners, new Color("83B6E8"), 3);
        Vector2 origin = ToScreen(new Point2Dto());
        DrawLine(origin - Vector2.Right * 9, origin + Vector2.Right * 9, Colors.White, 2);
        DrawLine(origin - Vector2.Up * 9, origin + Vector2.Up * 9, Colors.White, 2);
    }

    private void DrawObject(VenueObjectInstanceDto item)
    {
        Vector2[] polygon = VenueGeometry.TransformFootprint(item).Select(ToScreen).ToArray();
        bool selected = _selectionKind == VenueSelectionKind.Object && _selectedId == item.ObjectId;
        bool unresolved = _unresolvedObjects.Contains(item.ObjectId);
        Color color = unresolved ? Colors.OrangeRed : !item.VisibleInViewer ? new Color(0.55f, 0.7f, 0.8f, 0.35f) : new Color(0.55f, 0.7f, 0.8f);
        DrawClosed(polygon, selected ? Colors.Cyan : color, selected ? 4 : 2);
        Vector2 center = ToScreen(item.Position); float radians = item.RotationDeg * MathF.PI / 180;
        DrawLine(center, center + new Vector2(MathF.Sin(radians), -MathF.Cos(radians)) * 22, color, 3);
        DrawString(ThemeDB.FallbackFont, center + new Vector2(8, -8), item.Name, HorizontalAlignment.Left, -1, 13, color);
        if (_lockedObjects.Contains(item.ObjectId)) DrawString(ThemeDB.FallbackFont, center + new Vector2(-18, 20), "LOCK", HorizontalAlignment.Left, -1, 11, Colors.Orange);
        if (unresolved) { DrawLine(polygon[0], polygon[2], Colors.OrangeRed, 2); DrawLine(polygon[1], polygon[3], Colors.OrangeRed, 2); }
    }

    private void DrawCone(ConeDto cone)
    {
        Color color = cone.Color switch { "blue" => Colors.Blue, "yellow" => Colors.Yellow, "orange" => Colors.Orange, "none" => Colors.SaddleBrown, _ => Colors.Red };
        bool selected = _selectionKind == VenueSelectionKind.Cone && _selectedId == cone.Id;
        DrawCircle(ToScreen(cone.Position), selected ? 8 : 6, selected ? Colors.Cyan : color);
    }

    private void DrawMarking(MarkingDto marking)
    {
        Color color = MarkingGeometry.TryNormalizeColor(marking.Color, false, out string canonical) ? new Color(canonical.TrimStart('#')) : Colors.White;
        if (!marking.VisibleInViewer) color.A = 0.35f;
        bool selected = _selectedId == marking.Id && _selectionKind is VenueSelectionKind.Marking or VenueSelectionKind.MarkingSegment or VenueSelectionKind.MarkingHandle;
        if (!_markingGeometryCache.TryGetValue(marking.Id, out MarkingStyleGeometry? geometry))
        {
            geometry = MarkingGeometry.CreateStyleGeometry(PathSampler.Sample(marking.Path), marking.Style);
            _markingGeometryCache[marking.Id] = geometry;
        }
        foreach (MarkingStroke stroke in geometry.Strokes)
            DrawLine(ToScreen(stroke.Start), ToScreen(stroke.End), selected ? Colors.Cyan : color,
                MathF.Max(1, marking.WidthMeters * _pixelsPerMeter), true);
        foreach (Point2Dto dot in geometry.Dots)
            DrawCircle(ToScreen(dot), MathF.Max(1, marking.WidthMeters * _pixelsPerMeter * 0.5f), selected ? Colors.Cyan : color);
        if (selected) MarkingHandleOverlay.Draw(this, marking, _markingSelection, color, ToScreen);
    }

    private void DrawMarkingPreview()
    {
        if (_previewEnd is null || !IsPathTool(_tool)) return;
        Point2Dto? start = _draftPath?.Start;
        if (start is null)
        {
            string id = !string.IsNullOrEmpty(_buildingMarkingId) ? _buildingMarkingId : _markingSelection.MarkingId;
            start = _document.FindMarking(id) is MarkingDto marking ? PathEditing.GetPathEnd(marking.Path) : null;
        }
        if (start is null || PathEditing.PointsEqual(start, _previewEnd)) return;
        var preview = new PathDefinition { Start = start, Segments = [] };
        if (_tool == VenueTool.AddCubicBezier) PathEditing.AppendCubic(preview, _previewEnd); else PathEditing.AppendLine(preview, _previewEnd);
        DrawPolyline(PathSampler.Sample(preview).Points.Select(ToScreen).ToArray(), new Color(1, 0.82f, 0.2f, 0.85f), 3, true);
        DrawCircle(ToScreen(_previewEnd), 5, Colors.White);
    }

    private void DrawClosed(IReadOnlyList<Vector2> points, Color color, float width)
    { for (int index = 0; index < points.Count; index++) DrawLine(points[index], points[(index + 1) % points.Count], color, width, true); }
    private Vector2 ToScreen(Point2Dto point) => EditorCanvasMath.DomainToScreen(point, Size, _panPixels, _pixelsPerMeter);
    private Point2Dto ToDomain(Vector2 point) => EditorCanvasMath.ScreenToDomain(point, Size, _panPixels, _pixelsPerMeter);
    private Point2Dto ResolveSnap(Vector2 screen, bool bypass) => EditorCanvasMath.ResolveDragPosition(ToDomain(screen), SnapMeters, bypass);
    private static bool IsPathTool(VenueTool tool) => tool is VenueTool.AddMarking or VenueTool.AddLine or VenueTool.AddCubicBezier;
    private static Point2Dto Copy(Point2Dto point) => new() { X = point.X, Y = point.Y };
    private static MarkingPathCoordinateKind ToCoordinateKind(MarkingHandleKind kind) => kind switch
    {
        MarkingHandleKind.PathStart => MarkingPathCoordinateKind.PathStart,
        MarkingHandleKind.SegmentEnd => MarkingPathCoordinateKind.SegmentEnd,
        MarkingHandleKind.Control1 => MarkingPathCoordinateKind.Control1,
        MarkingHandleKind.Control2 => MarkingPathCoordinateKind.Control2,
        _ => throw new InvalidOperationException("The selection does not address a Path coordinate."),
    };
}
