using Godot;
using MotoGymkhanaTrainer.Tracks;

namespace MotoGymkhanaTrainer.VenueEditor;

/// <summary>Tools that create persisted Venue geometry.</summary>
public enum VenueTool { Select, AddCone, AddLine, AddPolyline }

/// <summary>Kind of the current editor-only canvas selection.</summary>
public enum VenueSelectionKind { None, Object, Cone, Marking, MarkingPoint }

/// <summary>Top-down Venue canvas. DTO geometry is sampled only while drawing.</summary>
public partial class VenueEditorCanvas : Control
{
    private const float MinimumPixelsPerMeter = 3;
    private const float MaximumPixelsPerMeter = 120;
    private const float SnapMeters = 0.25f;
    private const float HitPixels = 10;
    private VenueDocument _document = VenueDocument.CreateNew();
    private readonly HashSet<string> _lockedObjects = new(StringComparer.Ordinal);
    private readonly HashSet<string> _unresolvedObjects = new(StringComparer.Ordinal);
    private readonly List<Point2Dto> _pendingMarking = [];
    private Vector2 _panPixels;
    private float _pixelsPerMeter = 10;
    private bool _panning;
    private bool _dragging;
    private VenueTool _tool;
    private VenueSelectionKind _selectionKind;
    private string? _selectedId;
    private int _selectedPointIndex = -1;
    private PopupMenu? _objectContextMenu;

    [Signal] public delegate void SelectionChangedEventHandler();
    [Signal] public delegate void DocumentChangedEventHandler();
    public event Action<string>? EditTransactionStarted;
    public event Action? EditTransactionFinished;
    public event Action<string>? DuplicateRequested;
    public event Action<string>? LockedTransformAttempted;

    public VenueSelectionKind SelectionKind => _selectionKind;
    public string? SelectedId => _selectedId;
    public int SelectedPointIndex => _selectedPointIndex;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        FocusMode = FocusModeEnum.Click;
        Resized += QueueRedraw;
        _objectContextMenu = new PopupMenu { Name = "VenueObjectContextMenu" };
        _objectContextMenu.AddItem("Duplicate Object", 1);
        _objectContextMenu.IdPressed += _ =>
        {
            if (_selectedId is not null) DuplicateRequested?.Invoke(_selectedId);
        };
        AddChild(_objectContextMenu);
        Callable.From(FitAreaInView).CallDeferred();
    }

    /// <summary>Replaces the DTO owner and clears only canvas UI state.</summary>
    public void SetDocument(VenueDocument document, bool resetView = true)
    {
        _document = document;
        ClearSelection();
        _pendingMarking.Clear();
        if (resetView) Callable.From(FitAreaInView).CallDeferred();
        QueueRedraw();
    }

    public void SetTool(VenueTool tool)
    {
        _tool = tool;
        _pendingMarking.Clear();
        QueueRedraw();
    }

    public void SetLockedObjects(IEnumerable<string> ids)
    {
        _lockedObjects.Clear();
        _lockedObjects.UnionWith(ids);
        QueueRedraw();
    }

    public void SetUnresolvedObjects(IEnumerable<string> ids)
    {
        _unresolvedObjects.Clear();
        _unresolvedObjects.UnionWith(ids);
        QueueRedraw();
    }

    public void SelectObject(string? id) => Select(
        VenueSelectionKind.Object,
        id is not null && _document.FindObject(id) is not null ? id : null,
        -1);

    /// <summary>Restores editor selection by stable persisted id after Undo/Redo.</summary>
    public void RestoreSelection(VenueSelectionKind kind, string? id, int pointIndex)
    {
        bool valid = kind switch
        {
            VenueSelectionKind.Object => id is not null && _document.FindObject(id) is not null,
            VenueSelectionKind.Cone => id is not null && _document.FindCone(id) is not null,
            VenueSelectionKind.Marking => id is not null && _document.FindMarking(id) is not null,
            VenueSelectionKind.MarkingPoint => id is not null && _document.FindMarking(id) is MarkingDto marking &&
                pointIndex >= 0 && pointIndex < marking.Points.Length,
            _ => false,
        };
        Select(valid ? kind : VenueSelectionKind.None, valid ? id : null, valid ? pointIndex : -1);
    }

    /// <summary>Finishes a UI-only point collection and creates one persisted marking.</summary>
    public bool FinishMarking()
    {
        if (_tool != VenueTool.AddPolyline || _pendingMarking.Count < 2) return false;
        MarkingDto marking = _document.AddMarking("polyline", _pendingMarking);
        _pendingMarking.Clear();
        Select(VenueSelectionKind.Marking, marking.Id, -1);
        EmitSignal(SignalName.DocumentChanged);
        QueueRedraw();
        return true;
    }

    public void FitAreaInView()
    {
        if (Size.X <= 1 || Size.Y <= 1) return;
        _panPixels = Vector2.Zero;
        _pixelsPerMeter = EditorCanvasMath.FitPixelsPerMeter(
            _document.Definition.Area.Width, _document.Definition.Area.Length,
            Size, 28, MinimumPixelsPerMeter, MaximumPixelsPerMeter);
        QueueRedraw();
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton button)
        {
            if (button.ButtonIndex == MouseButton.Middle)
            {
                _panning = button.Pressed;
                AcceptEvent();
            }
            else if (button.Pressed && button.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
            {
                float old = _pixelsPerMeter;
                float factor = button.ButtonIndex == MouseButton.WheelUp ? 1.12f : 1 / 1.12f;
                _pixelsPerMeter = Mathf.Clamp(old * factor, MinimumPixelsPerMeter, MaximumPixelsPerMeter);
                _panPixels = EditorCanvasMath.ZoomAt(button.Position, Size, _panPixels, old, _pixelsPerMeter);
                QueueRedraw();
                AcceptEvent();
            }
            else if (button.ButtonIndex == MouseButton.Left)
            {
                if (button.Pressed) HandleLeftPress(button.Position);
                else EndDrag();
                AcceptEvent();
            }
            else if (button.Pressed && button.ButtonIndex == MouseButton.Right &&
                     _selectionKind == VenueSelectionKind.Object && _selectedId is not null)
            {
                _objectContextMenu!.Position = DisplayServer.MouseGetPosition();
                _objectContextMenu.Popup();
                AcceptEvent();
            }
        }
        else if (@event is InputEventMouseMotion motion)
        {
            if (_panning)
            {
                _panPixels += motion.Relative;
                QueueRedraw();
            }
            else if (_dragging)
            {
                DragTo(EditorCanvasMath.Snap(ToDomain(motion.Position), SnapMeters));
            }
        }
    }

    public override void _Draw()
    {
        DrawGridAndArea();
        foreach (MarkingDto marking in _document.Definition.Markings) DrawMarking(marking);
        foreach (VenueObjectInstanceDto item in _document.Definition.Objects) DrawObject(item);
        foreach (ConeDto cone in _document.Definition.Cones) DrawCone(cone);
        if (_pendingMarking.Count > 0)
            for (int index = 0; index < _pendingMarking.Count - 1; index++)
                DrawLine(ToScreen(_pendingMarking[index]), ToScreen(_pendingMarking[index + 1]), Colors.White, 2);
    }

    private void HandleLeftPress(Vector2 screen)
    {
        Point2Dto point = EditorCanvasMath.Snap(ToDomain(screen), SnapMeters);
        if (_tool == VenueTool.AddCone)
        {
            ConeDto cone = _document.AddCone(point);
            Select(VenueSelectionKind.Cone, cone.Id, -1);
            EmitSignal(SignalName.DocumentChanged);
            return;
        }
        if (_tool is VenueTool.AddLine or VenueTool.AddPolyline)
        {
            _pendingMarking.Add(point);
            if (_tool == VenueTool.AddLine && _pendingMarking.Count == 2)
            {
                MarkingDto marking = _document.AddMarking("line", _pendingMarking);
                _pendingMarking.Clear();
                Select(VenueSelectionKind.Marking, marking.Id, -1);
                EmitSignal(SignalName.DocumentChanged);
            }
            QueueRedraw();
            return;
        }

        HitTest(screen);
        if (_selectionKind != VenueSelectionKind.None && _selectedId is not null)
        {
            if (_selectionKind == VenueSelectionKind.Object && _lockedObjects.Contains(_selectedId))
            {
                LockedTransformAttempted?.Invoke(_selectedId);
                return;
            }
            _dragging = true;
            EditTransactionStarted?.Invoke("Move Venue item");
        }
    }

    private void EndDrag()
    {
        if (!_dragging) return;
        _dragging = false;
        EditTransactionFinished?.Invoke();
    }

    private void DragTo(Point2Dto point)
    {
        if (_selectedId is null) return;
        switch (_selectionKind)
        {
            case VenueSelectionKind.Object: _document.MoveObject(_selectedId, point); break;
            case VenueSelectionKind.Cone: _document.MoveCone(_selectedId, point); break;
            case VenueSelectionKind.MarkingPoint: _document.MoveMarkingPoint(_selectedId, _selectedPointIndex, point); break;
            default: return;
        }
        EmitSignal(SignalName.DocumentChanged);
        QueueRedraw();
    }

    /* Hit order is marking anchor, cone, topmost object footprint, then marking
     * stroke. Small editable anchors therefore cannot be hidden by large assets. */
    private void HitTest(Vector2 screen)
    {
        foreach (MarkingDto marking in _document.Definition.Markings.Reverse())
            for (int index = marking.Points.Length - 1; index >= 0; index--)
                if (screen.DistanceTo(ToScreen(marking.Points[index])) <= HitPixels)
                { Select(VenueSelectionKind.MarkingPoint, marking.Id, index); return; }
        foreach (ConeDto cone in _document.Definition.Cones.Reverse())
            if (screen.DistanceTo(ToScreen(cone.Position)) <= HitPixels)
            { Select(VenueSelectionKind.Cone, cone.Id, -1); return; }
        Point2Dto domain = ToDomain(screen);
        foreach (VenueObjectInstanceDto item in _document.Definition.Objects.Reverse())
            if (VenueGeometry.Contains(VenueGeometry.TransformFootprint(item), domain))
            { Select(VenueSelectionKind.Object, item.ObjectId, -1); return; }
        foreach (MarkingDto marking in _document.Definition.Markings.Reverse())
            // Hit testing follows the persisted path, not render-only dash pieces,
            // so a click in a visual dash gap still selects the logical marking.
            for (int index = 0; index < marking.Points.Length - 1; index++)
                if (EditorCanvasMath.DistanceToSegment(
                    screen, ToScreen(marking.Points[index]), ToScreen(marking.Points[index + 1])) <= HitPixels)
                { Select(VenueSelectionKind.Marking, marking.Id, -1); return; }
        ClearSelection();
    }

    private void Select(VenueSelectionKind kind, string? id, int pointIndex)
    {
        _selectionKind = id is null ? VenueSelectionKind.None : kind;
        _selectedId = id;
        _selectedPointIndex = pointIndex;
        EmitSignal(SignalName.SelectionChanged);
        QueueRedraw();
    }
    private void ClearSelection() => Select(VenueSelectionKind.None, null, -1);

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
        Vector2[] corners =
        [
            ToScreen(new Point2Dto { X = -halfWidth, Y = -halfLength }), ToScreen(new Point2Dto { X = halfWidth, Y = -halfLength }),
            ToScreen(new Point2Dto { X = halfWidth, Y = halfLength }), ToScreen(new Point2Dto { X = -halfWidth, Y = halfLength }),
        ];
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
        Vector2 center = ToScreen(item.Position);
        float radians = item.RotationDeg * MathF.PI / 180;
        Vector2 direction = new(MathF.Sin(radians), -MathF.Cos(radians));
        DrawLine(center, center + direction * 22, color, 3);
        DrawString(ThemeDB.FallbackFont, center + new Vector2(8, -8), item.Name, HorizontalAlignment.Left, -1, 13, color);
        if (_lockedObjects.Contains(item.ObjectId)) DrawString(ThemeDB.FallbackFont, center + new Vector2(-18, 20), "LOCK", HorizontalAlignment.Left, -1, 11, Colors.Orange);
        if (unresolved)
        {
            DrawLine(polygon[0], polygon[2], Colors.OrangeRed, 2);
            DrawLine(polygon[1], polygon[3], Colors.OrangeRed, 2);
        }
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
        if (!marking.VisibleInViewer) color.A = 0.35f; // hidden-in-Viewer remains editable here.
        bool selected = _selectedId == marking.Id && _selectionKind is VenueSelectionKind.Marking or VenueSelectionKind.MarkingPoint;
        foreach (MarkingStroke stroke in MarkingGeometry.CreateStrokes(marking.Points, marking.Style))
            DrawLine(ToScreen(stroke.Start), ToScreen(stroke.End), selected ? Colors.Cyan : color, MathF.Max(1, marking.WidthMeters * _pixelsPerMeter), true);
        if (selected)
            for (int index = 0; index < marking.Points.Length; index++)
                DrawCircle(ToScreen(marking.Points[index]), index == _selectedPointIndex ? 7 : 5, index == _selectedPointIndex ? Colors.Yellow : Colors.Cyan);
    }

    private void DrawClosed(IReadOnlyList<Vector2> points, Color color, float width)
    {
        for (int index = 0; index < points.Count; index++) DrawLine(points[index], points[(index + 1) % points.Count], color, width, true);
    }
    private Vector2 ToScreen(Point2Dto point) => EditorCanvasMath.DomainToScreen(point, Size, _panPixels, _pixelsPerMeter);
    private Point2Dto ToDomain(Vector2 point) => EditorCanvasMath.ScreenToDomain(point, Size, _panPixels, _pixelsPerMeter);
}
