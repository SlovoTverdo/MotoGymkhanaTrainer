using Godot;
using MotoGymkhanaTrainer.Tracks;

namespace MotoGymkhanaTrainer.VenueEditor;

/// <summary>Standalone 2D authoring scene for Venue Definition v1.</summary>
public partial class VenueEditor : Control
{
    private enum PendingAction { None, New, Open }

    private VenueDocument _document = VenueDocument.CreateNew();
    private readonly EditorSnapshotHistory _history = new(100);
    private readonly HashSet<string> _lockedObjectIds = new(StringComparer.Ordinal);
    private SandboxedJsonLibrary? _library;
    private VenueEditorCanvas? _canvas;
    private Tree? _libraryTree;
    private ItemList? _objectList;
    private LineEdit? _venueId, _venueName, _panoramaPath, _objectName, _objectPath, _selectedId;
    private SpinBox? _areaWidth, _areaLength, _panoramaRotation, _panoramaEnergy;
    private SpinBox? _positionX, _positionY, _elevation, _rotation, _scaleX, _scaleY, _scaleZ, _footprintWidth, _footprintLength;
    private SpinBox? _pointX, _pointY, _markingWidth;
    private CheckButton? _panoramaEnabled, _collisionEnabled, _visibleInViewer, _lockObject;
    private OptionButton? _coneColor, _markingStyle;
    private ColorPickerButton? _markingColor;
    private Button? _undoButton, _redoButton, _duplicateButton, _insertPointButton, _deletePointButton;
    private Label? _fileLabel, _dirtyLabel, _statusLabel, _selectionLabel;
    private RichTextLabel? _warningsLabel;
    private FileDialog? _openDialog, _saveDialog, _objectDialog, _textureDialog;
    private ConfirmationDialog? _unsavedDialog, _newFolderDialog, _deleteDialog;
    private LineEdit? _newFolderName;
    private string? _currentFilePath;
    private string _selectedFolder = string.Empty;
    private PendingAction _pendingAction;
    private bool _updatingUi;
    private string? _transactionSnapshot;

    public override void _Ready()
    {
        _library = new SandboxedJsonLibrary(
            ProjectSettings.GlobalizePath("res://venues"), "Venue library", "res://venues/");
        BuildUi();
        ReplaceDocument(VenueDocument.CreateNew(), null, saved: false);
        SetStatus("New Venue created.", false);
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey key || !key.Pressed || IsEditingText()) return;
        bool handled = false;
        if (key.CtrlPressed && key.Keycode == Key.Z && !key.Echo) handled = key.ShiftPressed ? Redo() : Undo();
        else if (key.CtrlPressed && key.Keycode == Key.Y && !key.Echo) handled = Redo();
        else if (key.CtrlPressed && key.Keycode == Key.D && !key.Echo) handled = DuplicateSelected();
        else if (key.Keycode == Key.Delete && !key.Echo) handled = RequestDelete();
        else if (_canvas?.SelectionKind == VenueSelectionKind.Object && _canvas.SelectedId is not null)
            handled = ApplyObjectShortcut(key);
        if (handled) GetViewport().SetInputAsHandled();
    }

    private void BuildUi()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var page = new VBoxContainer();
        page.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(page);
        page.AddChild(BuildToolbar());
        var status = new HBoxContainer();
        _fileLabel = new Label { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _dirtyLabel = new Label();
        status.AddChild(_fileLabel); status.AddChild(_dirtyLabel); page.AddChild(status);

        var body = new HSplitContainer { SizeFlagsVertical = SizeFlags.ExpandFill, SplitOffsets = [285, 1160] };
        body.AddChild(BuildLibrary());
        _canvas = new VenueEditorCanvas
        {
            Name = "VenueCanvas", CustomMinimumSize = new Vector2(620, 450),
            SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        _canvas.SelectionChanged += SynchronizeSelection;
        _canvas.DocumentChanged += OnCanvasChanged;
        _canvas.EditTransactionStarted += BeginTransaction;
        _canvas.EditTransactionFinished += EndTransaction;
        _canvas.DuplicateRequested += _ => DuplicateSelected();
        _canvas.LockedTransformAttempted += id => SetStatus($"Object '{id}' is locked.", true);
        body.AddChild(_canvas);
        body.AddChild(BuildProperties());
        page.AddChild(body);
        _statusLabel = new Label { CustomMinimumSize = new Vector2(0, 28) };
        page.AddChild(_statusLabel);
        _warningsLabel = new RichTextLabel { BbcodeEnabled = true, CustomMinimumSize = new Vector2(0, 64) };
        page.AddChild(_warningsLabel);
        BuildDialogs();
    }

    private Control BuildToolbar()
    {
        var bar = new HBoxContainer { CustomMinimumSize = new Vector2(0, 42) };
        bar.AddChild(Button("New", () => Request(PendingAction.New)));
        bar.AddChild(Button("Open", () => Request(PendingAction.Open)));
        bar.AddChild(Button("Save", Save));
        bar.AddChild(Button("Save As", ShowSaveAs));
        bar.AddChild(new VSeparator());
        _undoButton = Button("Undo", () => Undo()); _redoButton = Button("Redo", () => Redo());
        bar.AddChild(_undoButton); bar.AddChild(_redoButton);
        bar.AddChild(new VSeparator());
        bar.AddChild(Button("Select", () => _canvas?.SetTool(VenueTool.Select)));
        bar.AddChild(Button("Add Object", () => _objectDialog?.PopupCenteredRatio(0.8f)));
        bar.AddChild(Button("Add Cone", () => _canvas?.SetTool(VenueTool.AddCone)));
        bar.AddChild(Button("Add Line", () => _canvas?.SetTool(VenueTool.AddLine)));
        bar.AddChild(Button("Add Polyline", () => _canvas?.SetTool(VenueTool.AddPolyline)));
        bar.AddChild(Button("Finish Marking", FinishMarking));
        bar.AddChild(Button("Fit", () => _canvas?.FitAreaInView()));
        bar.AddChild(Button("Delete", () => RequestDelete()));
        return bar;
    }

    private Control BuildLibrary()
    {
        var panel = new VBoxContainer { CustomMinimumSize = new Vector2(280, 0) };
        panel.AddChild(Title("Venue Library — res://venues/"));
        var actions = new HBoxContainer();
        actions.AddChild(Button("Refresh", RefreshTree));
        actions.AddChild(Button("New Folder", ShowNewFolder));
        panel.AddChild(actions);
        _libraryTree = new Tree { HideRoot = false, SizeFlagsVertical = SizeFlags.ExpandFill };
        _libraryTree.ItemSelected += OnLibrarySelected;
        _libraryTree.ItemActivated += OpenSelectedLibraryFile;
        panel.AddChild(_libraryTree);
        panel.AddChild(Button("Open Selected", OpenSelectedLibraryFile));
        panel.AddChild(Button("Save in Selected Folder", SaveInSelectedFolder));
        return panel;
    }

    private Control BuildProperties()
    {
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(335, 0) };
        var panel = new VBoxContainer { CustomMinimumSize = new Vector2(320, 0) };
        scroll.AddChild(panel);
        panel.AddChild(Title("Venue"));
        _venueId = TextField(panel, "ID", OnVenueChanged); _venueName = TextField(panel, "Name", OnVenueChanged);
        _areaWidth = NumberField(panel, "Area Width", 0.05, 10000, 0.25, OnVenueChanged);
        _areaLength = NumberField(panel, "Area Length", 0.05, 10000, 0.25, OnVenueChanged);
        panel.AddChild(Title("Panorama metadata"));
        _panoramaEnabled = CheckField(panel, "Enabled", OnVenueChanged);
        _panoramaPath = TextField(panel, "Texture Path", OnVenueChanged);
        _panoramaPath.Editable = false;
        panel.AddChild(Button("Choose Texture2D", () => _textureDialog?.PopupCenteredRatio(0.8f)));
        _panoramaRotation = NumberField(panel, "Rotation Deg", -100000, 100000, 0.5, OnVenueChanged);
        _panoramaEnergy = NumberField(panel, "Energy", 0, 1000, 0.05, OnVenueChanged);

        panel.AddChild(Title("Objects"));
        _objectList = new ItemList { CustomMinimumSize = new Vector2(0, 130) };
        _objectList.ItemSelected += index => _canvas?.SelectObject(_document.Definition.Objects[(int)index].ObjectId);
        panel.AddChild(_objectList);
        _duplicateButton = Button("Duplicate Object (Ctrl+D)", () => DuplicateSelected()); panel.AddChild(_duplicateButton);
        _selectionLabel = Title("Selection: none"); panel.AddChild(_selectionLabel);
        _selectedId = TextField(panel, "ID", null); _selectedId.Editable = false;
        _objectName = TextField(panel, "Object Name", OnObjectChanged); _objectPath = TextField(panel, "Asset Path", OnObjectChanged);
        _objectPath.Editable = false;
        _positionX = NumberField(panel, "Position X", -10000, 10000, 0.05, OnSelectionGeometryChanged);
        _positionY = NumberField(panel, "Position Y", -10000, 10000, 0.05, OnSelectionGeometryChanged);
        _elevation = NumberField(panel, "Elevation", -10000, 10000, 0.05, OnObjectChanged);
        _rotation = NumberField(panel, "Rotation", -100000, 100000, 0.5, OnObjectChanged);
        _scaleX = NumberField(panel, "Scale X", 0.01, 1000, 0.05, OnObjectChanged);
        _scaleY = NumberField(panel, "Scale Y", 0.01, 1000, 0.05, OnObjectChanged);
        _scaleZ = NumberField(panel, "Scale Z", 0.01, 1000, 0.05, OnObjectChanged);
        _footprintWidth = NumberField(panel, "Footprint Width", 0.01, 10000, 0.05, OnObjectChanged);
        _footprintLength = NumberField(panel, "Footprint Length", 0.01, 10000, 0.05, OnObjectChanged);
        _collisionEnabled = CheckField(panel, "Collision Enabled", OnObjectChanged);
        _visibleInViewer = CheckField(panel, "Visible in Viewer", OnSelectionPropertiesChanged);
        _lockObject = CheckField(panel, "Editor Lock", ToggleLock);

        panel.AddChild(Title("Cone / marking"));
        _pointX = NumberField(panel, "Point X", -10000, 10000, 0.05, OnSelectionGeometryChanged);
        _pointY = NumberField(panel, "Point Y", -10000, 10000, 0.05, OnSelectionGeometryChanged);
        _coneColor = new OptionButton(); foreach (string value in new[] { "red", "blue", "yellow", "orange", "none" }) _coneColor.AddItem(value);
        _coneColor.ItemSelected += _ => OnSelectionPropertiesChanged(); panel.AddChild(Labeled("Cone Color", _coneColor));
        _markingColor = new ColorPickerButton(); _markingColor.ColorChanged += _ => OnSelectionPropertiesChanged(); panel.AddChild(Labeled("Marking Color", _markingColor));
        _markingWidth = NumberField(panel, "Marking Width", 0.001, 100, 0.01, OnSelectionPropertiesChanged);
        _markingStyle = new OptionButton(); foreach (string value in new[] { "solid", "dashed", "dotted" }) _markingStyle.AddItem(value);
        _markingStyle.ItemSelected += _ => OnSelectionPropertiesChanged(); panel.AddChild(Labeled("Marking Style", _markingStyle));
        _insertPointButton = Button("Insert Point After", InsertPoint); _deletePointButton = Button("Delete Internal Point", DeletePoint);
        panel.AddChild(_insertPointButton); panel.AddChild(_deletePointButton);
        return scroll;
    }

    private void BuildDialogs()
    {
        _openDialog = JsonDialog("Open Venue", FileDialog.FileModeEnum.OpenFile);
        _openDialog.FileSelected += OpenVenue; _openDialog.Canceled += () => SetStatus("Open canceled; current Venue preserved.", false); AddChild(_openDialog);
        _saveDialog = JsonDialog("Save Venue", FileDialog.FileModeEnum.SaveFile);
        _saveDialog.FileSelected += SaveVenue; AddChild(_saveDialog);
        _objectDialog = ResourceDialog("Choose Venue Object", "*.tscn ; Godot Scene");
        _objectDialog.FileSelected += AddObject; AddChild(_objectDialog);
        _textureDialog = ResourceDialog("Choose Panorama Texture", "*.png,*.jpg,*.jpeg,*.webp,*.svg ; Texture2D");
        _textureDialog.FileSelected += SetPanoramaTexture; AddChild(_textureDialog);
        _unsavedDialog = new ConfirmationDialog { Title = "Unsaved changes", DialogText = "Discard unsaved Venue changes?", OkButtonText = "Discard" };
        _unsavedDialog.Confirmed += ContinuePending; _unsavedDialog.Canceled += () => _pendingAction = PendingAction.None; AddChild(_unsavedDialog);
        _newFolderName = new LineEdit { PlaceholderText = "folder-name" };
        _newFolderDialog = new ConfirmationDialog { Title = "Create Venue Folder", DialogText = "Create a child folder in the selected folder:" };
        _newFolderDialog.AddChild(_newFolderName); _newFolderDialog.Confirmed += CreateFolder; AddChild(_newFolderDialog);
        _deleteDialog = new ConfirmationDialog { Title = "Delete Venue Object", DialogText = "Delete the selected object instance? The .tscn asset remains untouched." };
        _deleteDialog.Confirmed += DeleteSelectedNow; AddChild(_deleteDialog);
    }

    private void ReplaceDocument(VenueDocument document, string? path, bool saved)
    {
        _document = document; _currentFilePath = path; _lockedObjectIds.Clear(); _transactionSnapshot = null;
        _history.Reset(VenueStore.Serialize(_document.Definition), saved);
        _canvas!.SetDocument(document); _canvas.SetLockedObjects(_lockedObjectIds);
        SynchronizeAll();
    }

    private void SynchronizeAll()
    {
        _updatingUi = true;
        VenueDefinitionDto value = _document.Definition;
        _venueId!.Text = value.Venue.Id; _venueName!.Text = value.Venue.Name;
        _areaWidth!.Value = value.Area.Width; _areaLength!.Value = value.Area.Length;
        _panoramaEnabled!.ButtonPressed = value.Panorama.Enabled; _panoramaPath!.Text = value.Panorama.TexturePath;
        _panoramaRotation!.Value = value.Panorama.RotationDeg; _panoramaEnergy!.Value = value.Panorama.EnergyMultiplier;
        _objectList!.Clear(); foreach (VenueObjectInstanceDto item in value.Objects) _objectList.AddItem($"{item.Name}  [{item.ObjectId}]");
        _updatingUi = false;
        RefreshTree(); SynchronizeSelection(); RefreshDiagnostics(); UpdateState();
    }

    private void SynchronizeSelection()
    {
        if (_canvas is null) return;
        _updatingUi = true;
        string? id = _canvas.SelectedId; VenueSelectionKind kind = _canvas.SelectionKind;
        _selectionLabel!.Text = $"Selection: {kind}"; _selectedId!.Text = id ?? string.Empty;
        VenueObjectInstanceDto? item = kind == VenueSelectionKind.Object ? _document.FindObject(id) : null;
        ConeDto? cone = kind == VenueSelectionKind.Cone ? _document.FindCone(id) : null;
        MarkingDto? marking = kind is VenueSelectionKind.Marking or VenueSelectionKind.MarkingPoint ? _document.FindMarking(id) : null;
        if (item is not null)
        {
            _objectName!.Text = item.Name; _objectPath!.Text = item.AssetPath;
            SetPosition(item.Position); _elevation!.Value = item.Elevation; _rotation!.Value = item.RotationDeg;
            _scaleX!.Value = item.Scale.X; _scaleY!.Value = item.Scale.Y; _scaleZ!.Value = item.Scale.Z;
            _footprintWidth!.Value = item.Footprint.Width; _footprintLength!.Value = item.Footprint.Length;
            _collisionEnabled!.ButtonPressed = item.CollisionEnabled; _visibleInViewer!.ButtonPressed = item.VisibleInViewer;
            _lockObject!.ButtonPressed = _lockedObjectIds.Contains(item.ObjectId);
            int index = Array.IndexOf(_document.Definition.Objects, item); if (index >= 0) _objectList!.Select(index);
        }
        else if (cone is not null)
        {
            SetPoint(cone.Position); _coneColor!.Select(Array.IndexOf(new[] { "red", "blue", "yellow", "orange", "none" }, cone.Color));
        }
        else if (marking is not null)
        {
            int index = _canvas.SelectedPointIndex >= 0 ? _canvas.SelectedPointIndex : 0; SetPoint(marking.Points[index]);
            _markingColor!.Color = new Color(marking.Color.TrimStart('#')); _markingWidth!.Value = marking.WidthMeters;
            _markingStyle!.Select(marking.Style switch { "dashed" => 1, "dotted" => 2, _ => 0 });
            _visibleInViewer!.ButtonPressed = marking.VisibleInViewer;
        }
        bool locked = item is not null && _lockedObjectIds.Contains(item.ObjectId);
        foreach (SpinBox control in ObjectTransformControls()) control.Editable = !locked;
        _duplicateButton!.Disabled = item is null; _insertPointButton!.Disabled = marking?.Type != "polyline";
        _deletePointButton!.Disabled = marking?.Type != "polyline" || marking.Points.Length <= 2 || _canvas.SelectedPointIndex < 0;
        _updatingUi = false;
    }

    private void OnVenueChanged()
    {
        if (_updatingUi) return;
        _document.Definition.Venue.Id = _venueId!.Text.Trim(); _document.Definition.Venue.Name = _venueName!.Text.Trim();
        _document.Definition.Area.Width = (float)_areaWidth!.Value; _document.Definition.Area.Length = (float)_areaLength!.Value;
        _document.Definition.Panorama.Enabled = _panoramaEnabled!.ButtonPressed; _document.Definition.Panorama.TexturePath = _panoramaPath!.Text.Trim();
        _document.Definition.Panorama.RotationDeg = (float)_panoramaRotation!.Value; _document.Definition.Panorama.EnergyMultiplier = (float)_panoramaEnergy!.Value;
        Commit("Edit Venue properties"); _canvas?.QueueRedraw();
    }

    private void OnObjectChanged()
    {
        if (_updatingUi || _canvas?.SelectedId is not string id || _document.FindObject(id) is not VenueObjectInstanceDto item) return;
        if (_lockedObjectIds.Contains(id)) { SetStatus($"Object '{id}' is locked.", true); SynchronizeSelection(); return; }
        item.Name = _objectName!.Text.Trim(); item.AssetPath = _objectPath!.Text.Trim();
        item.Position = new Point2Dto { X = (float)_positionX!.Value, Y = (float)_positionY!.Value };
        item.Elevation = (float)_elevation!.Value; item.RotationDeg = (float)_rotation!.Value;
        item.Scale.X = (float)_scaleX!.Value; item.Scale.Y = (float)_scaleY!.Value; item.Scale.Z = (float)_scaleZ!.Value;
        item.Footprint.Width = (float)_footprintWidth!.Value; item.Footprint.Length = (float)_footprintLength!.Value;
        item.CollisionEnabled = _collisionEnabled!.ButtonPressed; item.VisibleInViewer = _visibleInViewer!.ButtonPressed;
        Commit("Edit Venue object"); _canvas.QueueRedraw();
    }

    private void OnSelectionGeometryChanged()
    {
        if (_updatingUi || _canvas?.SelectedId is not string id) return;
        var point = new Point2Dto { X = (float)(_canvas.SelectionKind == VenueSelectionKind.Object ? _positionX!.Value : _pointX!.Value), Y = (float)(_canvas.SelectionKind == VenueSelectionKind.Object ? _positionY!.Value : _pointY!.Value) };
        if (_canvas.SelectionKind == VenueSelectionKind.Object) { if (_lockedObjectIds.Contains(id)) { SynchronizeSelection(); return; } _document.MoveObject(id, point); }
        else if (_canvas.SelectionKind == VenueSelectionKind.Cone) _document.MoveCone(id, point);
        else if (_canvas.SelectionKind == VenueSelectionKind.MarkingPoint) _document.MoveMarkingPoint(id, _canvas.SelectedPointIndex, point);
        else return;
        Commit("Edit item position"); _canvas.QueueRedraw();
    }

    private void OnSelectionPropertiesChanged()
    {
        if (_updatingUi || _canvas?.SelectedId is not string id) return;
        if (_canvas.SelectionKind == VenueSelectionKind.Cone) _document.SetConeColor(id, _coneColor!.GetItemText(_coneColor.Selected));
        else if (_canvas.SelectionKind is VenueSelectionKind.Marking or VenueSelectionKind.MarkingPoint)
        {
            MarkingDto marking = _document.FindMarking(id)!;
            marking.Color = $"#{_markingColor!.Color.ToHtml(false).ToUpperInvariant()}";
            marking.WidthMeters = (float)_markingWidth!.Value; marking.Style = _markingStyle!.GetItemText(_markingStyle.Selected);
            marking.VisibleInViewer = _visibleInViewer!.ButtonPressed;
        }
        else if (_canvas.SelectionKind == VenueSelectionKind.Object) OnObjectChanged(); else return;
        Commit("Edit item properties"); _canvas.QueueRedraw();
    }

    private void OnCanvasChanged()
    {
        SynchronizeSelection(); RefreshDiagnostics();
        if (_transactionSnapshot is null) Commit("Edit canvas item");
    }

    private void BeginTransaction(string description) => _transactionSnapshot = VenueStore.Serialize(_document.Definition);
    private void EndTransaction()
    {
        if (_transactionSnapshot is null) return;
        _transactionSnapshot = null; Commit("Move Venue item");
    }

    private void Commit(string description)
    {
        try { _history.Commit(VenueStore.Serialize(_document.Definition), description); SetStatus(description, false); }
        catch (Exception exception) { SetStatus(exception.Message, true); }
        RefreshDiagnostics(); UpdateState();
    }

    private bool Undo() => RestoreHistory(_history.Undo(), "Undo");
    private bool Redo() => RestoreHistory(_history.Redo(), "Redo");
    private bool RestoreHistory(string? snapshot, string action)
    {
        if (snapshot is null) return false;
        string? selected = _canvas?.SelectedId;
        VenueSelectionKind selectedKind = _canvas?.SelectionKind ?? VenueSelectionKind.None;
        int selectedPoint = _canvas?.SelectedPointIndex ?? -1;
        _document.Replace(VenueStore.LoadFromJson(snapshot, "history", ProjectSettings.GlobalizePath("res://")).Definition);
        _lockedObjectIds.RemoveWhere(id => _document.FindObject(id) is null);
        _canvas!.SetDocument(_document, false); _canvas.SetLockedObjects(_lockedObjectIds);
        _canvas.RestoreSelection(selectedKind, selected, selectedPoint);
        SynchronizeAll(); SetStatus(action, false); return true;
    }

    private void Request(PendingAction action)
    {
        _pendingAction = action;
        if (_history.IsDirty) _unsavedDialog!.PopupCentered(); else ContinuePending();
    }
    private void ContinuePending()
    {
        PendingAction action = _pendingAction; _pendingAction = PendingAction.None;
        if (action == PendingAction.New) ReplaceDocument(VenueDocument.CreateNew(), null, false);
        else if (action == PendingAction.Open) { _openDialog!.CurrentDir = _library!.RootPath; _openDialog.PopupCenteredRatio(0.8f); }
    }

    private void Save() { if (_currentFilePath is null) ShowSaveAs(); else SaveVenue(_currentFilePath); }
    private void ShowSaveAs()
    {
        _saveDialog!.CurrentDir = _library!.ResolveFolder(_selectedFolder);
        _saveDialog.CurrentFile = SandboxedJsonLibrary.SuggestFileName(_document.Definition.Venue.Id, "venue"); _saveDialog.PopupCenteredRatio(0.8f);
    }
    private void SaveInSelectedFolder()
    {
        try { SaveVenue(_library!.ResolveSaveJson(_selectedFolder, SandboxedJsonLibrary.SuggestFileName(_document.Definition.Venue.Id, "venue"))); }
        catch (Exception exception) { SetStatus(exception.Message, true); }
    }
    private void SaveVenue(string path)
    {
        try
        {
            string requested = Filesystem(path); string folder = Path.GetDirectoryName(requested) ?? _library!.RootPath;
            string target = _library!.ResolveSaveJson(_library.ToRelative(folder), Path.GetFileName(requested));
            VenueStore.SaveToFile(_document.Definition, target); _currentFilePath = target; _history.MarkSaved(); RefreshTree(); UpdateState(); SetStatus($"Saved '{target}'.", false);
        }
        catch (Exception exception) { SetStatus($"Save failed: {exception.Message}", true); GD.PushError(exception.ToString()); }
    }
    private void OpenVenue(string path)
    {
        try
        {
            string file = _library!.ResolveExistingJson(Filesystem(path)); VenueLoadResult loaded = VenueStore.LoadFromFile(file, ProjectSettings.GlobalizePath("res://"));
            // Resource resolution is deliberately performed on the temporary
            // candidate. A bad PackedScene becomes a placeholder warning, but a
            // malformed Venue root never replaces the current document.
            var warnings = loaded.Warnings.ToList();
            foreach (VenueObjectInstanceDto item in loaded.Definition.Objects)
                if (!ResourceLoader.Exists(item.AssetPath, "PackedScene") || GD.Load<PackedScene>(item.AssetPath) is null)
                    warnings.Add($"Object '{item.ObjectId}' cannot be resolved as PackedScene: {item.AssetPath}");
            if (!string.IsNullOrWhiteSpace(loaded.Definition.Panorama.TexturePath) &&
                (!ResourceLoader.Exists(loaded.Definition.Panorama.TexturePath, "Texture2D") ||
                 GD.Load<Texture2D>(loaded.Definition.Panorama.TexturePath) is null))
                warnings.Add($"Panorama cannot be resolved as Texture2D: {loaded.Definition.Panorama.TexturePath}");
            ReplaceDocument(new VenueDocument(loaded.Definition), file, true);
            foreach (string warning in warnings.Distinct(StringComparer.Ordinal)) GD.PushWarning(warning);
            SetStatus($"Loaded with {warnings.Distinct(StringComparer.Ordinal).Count()} warning(s).", warnings.Count > 0);
        }
        catch (Exception exception) { SetStatus($"Open failed: {exception.Message}", true); GD.PushError(exception.ToString()); }
    }

    private void AddObject(string path)
    {
        try
        {
            string resource = CanonicalResource(path); if (!resource.EndsWith(".tscn", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Object must be a .tscn resource.");
            if (!ResourceLoader.Exists(resource, "PackedScene") || GD.Load<PackedScene>(resource) is null)
                throw new InvalidDataException($"Godot could not load PackedScene '{resource}'.");
            VenueObjectInstanceDto item = _document.AddObject(resource); _canvas!.SelectObject(item.ObjectId); Commit("Add Venue object"); SynchronizeAll();
        }
        catch (Exception exception) { SetStatus($"Add Object failed: {exception.Message}", true); }
    }
    private void SetPanoramaTexture(string path)
    {
        try
        {
            string resource = CanonicalResource(path);
            if (!ResourceLoader.Exists(resource, "Texture2D") || GD.Load<Texture2D>(resource) is null)
                throw new InvalidDataException($"Godot could not load Texture2D '{resource}'.");
            _panoramaPath!.Text = resource; OnVenueChanged();
        }
        catch (Exception exception) { SetStatus(exception.Message, true); }
    }

    private bool DuplicateSelected()
    {
        if (_canvas?.SelectionKind != VenueSelectionKind.Object || _canvas.SelectedId is not string id) return false;
        VenueObjectInstanceDto copy = _document.DuplicateObject(id); _lockedObjectIds.Remove(copy.ObjectId); _canvas.SelectObject(copy.ObjectId); Commit("Duplicate Venue object"); SynchronizeAll(); return true;
    }
    private bool RequestDelete()
    {
        if (_canvas?.SelectedId is null) return false;
        if (_canvas.SelectionKind == VenueSelectionKind.Object) { _deleteDialog!.PopupCentered(); return true; }
        DeleteSelectedNow(); return true;
    }
    private void DeleteSelectedNow()
    {
        if (_canvas?.SelectedId is not string id) return;
        bool changed = _canvas.SelectionKind switch
        {
            VenueSelectionKind.Object => _document.DeleteObject(id), VenueSelectionKind.Cone => _document.DeleteCone(id),
            VenueSelectionKind.Marking or VenueSelectionKind.MarkingPoint => _document.DeleteMarking(id), _ => false,
        };
        if (changed) { _lockedObjectIds.Remove(id); Commit("Delete Venue item"); _canvas.SetDocument(_document, false); SynchronizeAll(); }
    }
    private void InsertPoint()
    {
        if (_canvas?.SelectedId is not string id || _canvas.SelectedPointIndex < 0) return;
        _document.InsertMarkingPointAfter(id, _canvas.SelectedPointIndex); Commit("Insert marking point"); _canvas.QueueRedraw(); SynchronizeSelection();
    }
    private void DeletePoint()
    {
        if (_canvas?.SelectedId is not string id || _canvas.SelectedPointIndex < 0) return;
        try { _document.DeleteMarkingPoint(id, _canvas.SelectedPointIndex); Commit("Delete marking point"); _canvas.QueueRedraw(); SynchronizeSelection(); }
        catch (Exception exception) { SetStatus(exception.Message, true); }
    }
    private void FinishMarking() { if (_canvas?.FinishMarking() == true) Commit("Add polyline marking"); else SetStatus("Polyline needs at least two points.", true); }

    private bool ApplyObjectShortcut(InputEventKey key)
    {
        if (_canvas?.SelectedId is not string id || _document.FindObject(id) is not VenueObjectInstanceDto item || _lockedObjectIds.Contains(id)) return false;
        float step = key.AltPressed ? 0.05f : key.ShiftPressed ? 1 : 0.25f;
        if (key.Keycode is Key.Left or Key.Right or Key.Up or Key.Down)
        {
            float dx = key.Keycode == Key.Left ? -step : key.Keycode == Key.Right ? step : 0;
            float dy = key.Keycode == Key.Down ? -step : key.Keycode == Key.Up ? step : 0;
            item.Position = new Point2Dto { X = item.Position.X + dx, Y = item.Position.Y + dy };
        }
        else if (key.Keycode is Key.Q or Key.E)
            item.RotationDeg += (key.Keycode == Key.Q ? -1 : 1) * (key.ShiftPressed ? 90 : 15);
        else return false;
        Commit("Keyboard object transform"); _canvas.QueueRedraw(); SynchronizeSelection(); return true;
    }
    private void ToggleLock()
    {
        if (_updatingUi || _canvas?.SelectionKind != VenueSelectionKind.Object || _canvas.SelectedId is not string id) return;
        if (_lockObject!.ButtonPressed) _lockedObjectIds.Add(id); else _lockedObjectIds.Remove(id);
        _canvas.SetLockedObjects(_lockedObjectIds); SetStatus(_lockObject.ButtonPressed ? "Object locked (editor only)." : "Object unlocked.", false); UpdateState();
    }

    private void RefreshDiagnostics()
    {
        try
        {
            var warnings = VenueStore.Diagnose(_document.Definition, ProjectSettings.GlobalizePath("res://")).ToList();
            var unresolved = _document.Definition.Objects.Where(item =>
                !ResourceLoader.Exists(item.AssetPath, "PackedScene") || GD.Load<PackedScene>(item.AssetPath) is null).Select(item => item.ObjectId).ToArray();
            foreach (string id in unresolved)
                if (!warnings.Any(value => value.Contains($"'{id}'", StringComparison.Ordinal)))
                    warnings.Add($"Object '{id}' exists on disk but cannot be resolved as PackedScene.");
            if (!string.IsNullOrWhiteSpace(_document.Definition.Panorama.TexturePath) &&
                (!ResourceLoader.Exists(_document.Definition.Panorama.TexturePath, "Texture2D") ||
                 GD.Load<Texture2D>(_document.Definition.Panorama.TexturePath) is null))
                warnings.Add($"Panorama resource cannot be resolved as Texture2D: {_document.Definition.Panorama.TexturePath}");
            _canvas?.SetUnresolvedObjects(unresolved);
            _warningsLabel!.Text = warnings.Count == 0 ? "[color=#75D48A]No validation warnings.[/color]" : "[color=#FFB45C]" + string.Join("\n", warnings.Select(EscapeBbcode)) + "[/color]";
        }
        catch (Exception exception) { _warningsLabel!.Text = $"[color=#FF6B6B]{EscapeBbcode(exception.Message)}[/color]"; }
    }

    private void RefreshTree()
    {
        if (_libraryTree is null || _library is null) return;
        _libraryTree.Clear(); TreeItem root = _libraryTree.CreateItem(); root.SetText(0, "venues"); root.SetMetadata(0, "D|");
        var folders = new Dictionary<string, TreeItem>(StringComparer.OrdinalIgnoreCase) { [string.Empty] = root };
        foreach (JsonLibraryEntry entry in _library.EnumerateEntries())
        {
            string parentPath = Path.GetDirectoryName(entry.RelativePath) ?? string.Empty; TreeItem item = _libraryTree.CreateItem(folders.GetValueOrDefault(parentPath, root));
            item.SetText(0, entry.IsDirectory ? $"📁 {entry.DisplayName}" : entry.DisplayName); item.SetMetadata(0, $"{(entry.IsDirectory ? 'D' : 'F')}|{entry.RelativePath}");
            if (entry.IsDirectory) folders[entry.RelativePath] = item;
        }
    }
    private void OnLibrarySelected()
    {
        if (!ReadSelection(out bool directory, out string path)) return;
        _selectedFolder = directory ? path : Path.GetDirectoryName(path) ?? string.Empty;
    }
    private void OpenSelectedLibraryFile()
    {
        if (!ReadSelection(out bool directory, out string path) || directory) return;
        if (_history.IsDirty) { SetStatus("Save or discard current changes before opening another Venue.", true); return; }
        OpenVenue(_library!.ResolveExistingJson(path));
    }
    private bool ReadSelection(out bool directory, out string path)
    {
        directory = false; path = string.Empty; TreeItem? selected = _libraryTree?.GetSelected(); if (selected is null) return false;
        string meta = selected.GetMetadata(0).AsString(); if (meta.Length < 2) return false; directory = meta[0] == 'D'; path = meta[2..]; return true;
    }
    private void ShowNewFolder() { _newFolderName!.Text = string.Empty; _newFolderDialog!.PopupCentered(); }
    private void CreateFolder()
    {
        try { _selectedFolder = _library!.CreateFolder(_selectedFolder, _newFolderName!.Text); RefreshTree(); SetStatus("Venue folder created.", false); }
        catch (Exception exception) { SetStatus(exception.Message, true); }
    }

    private void UpdateState()
    {
        _fileLabel!.Text = _currentFilePath ?? "Unsaved Venue"; _dirtyLabel!.Text = _history.IsDirty ? "● Unsaved" : "Saved";
        _undoButton!.Disabled = !_history.CanUndo; _redoButton!.Disabled = !_history.CanRedo;
    }
    private void SetStatus(string text, bool error) { _statusLabel!.Text = text; _statusLabel.Modulate = error ? Colors.OrangeRed : Colors.White; }
    private bool IsEditingText() => GetViewport().GuiGetFocusOwner() is LineEdit or TextEdit or SpinBox;
    private void SetPosition(Point2Dto value) { _positionX!.Value = value.X; _positionY!.Value = value.Y; }
    private void SetPoint(Point2Dto value) { _pointX!.Value = value.X; _pointY!.Value = value.Y; }
    private IEnumerable<SpinBox> ObjectTransformControls() => [_positionX!, _positionY!, _elevation!, _rotation!, _scaleX!, _scaleY!, _scaleZ!, _footprintWidth!, _footprintLength!];

    private static Button Button(string text, Action action) { var value = new Button { Text = text }; value.Pressed += action; return value; }
    private static Label Title(string text) => new() { Text = text, ThemeTypeVariation = "HeaderSmall" };
    private static Control Labeled(string label, Control field) { var row = new HBoxContainer(); row.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(125, 0) }); field.SizeFlagsHorizontal = SizeFlags.ExpandFill; row.AddChild(field); return row; }
    private static LineEdit TextField(Container parent, string label, Action? changed)
    {
        var value = new LineEdit(); if (changed is not null) value.TextChanged += _ => changed(); parent.AddChild(Labeled(label, value)); return value;
    }
    private static SpinBox NumberField(Container parent, string label, double min, double max, double step, Action changed)
    {
        var value = new SpinBox { MinValue = min, MaxValue = max, Step = step, AllowGreater = false, AllowLesser = false };
        value.ValueChanged += _ => changed(); parent.AddChild(Labeled(label, value)); return value;
    }
    private static CheckButton CheckField(Container parent, string label, Action changed)
    {
        var value = new CheckButton(); value.Toggled += _ => changed(); parent.AddChild(Labeled(label, value)); return value;
    }
    private static FileDialog JsonDialog(string title, FileDialog.FileModeEnum mode) => new()
    {
        Title = title, Access = FileDialog.AccessEnum.Filesystem, FileMode = mode, UseNativeDialog = false,
        Size = new Vector2I(900, 600), Filters = ["*.json ; Venue Definition JSON"],
    };
    private static FileDialog ResourceDialog(string title, string filter) => new()
    {
        Title = title, Access = FileDialog.AccessEnum.Resources, FileMode = FileDialog.FileModeEnum.OpenFile,
        UseNativeDialog = false, Size = new Vector2I(900, 600), Filters = [filter],
    };
    private static string Filesystem(string path) => path.StartsWith("res://", StringComparison.Ordinal) ? ProjectSettings.GlobalizePath(path) : path;
    private static string CanonicalResource(string path)
    {
        if (path.StartsWith("res://", StringComparison.Ordinal)) return path.Replace('\\', '/');
        string root = Path.GetFullPath(ProjectSettings.GlobalizePath("res://")); string candidate = Path.GetFullPath(path);
        string prefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Resource must be inside the Godot project.");
        return "res://" + Path.GetRelativePath(root, candidate).Replace('\\', '/');
    }
    private static string EscapeBbcode(string value) => value.Replace("[", "(").Replace("]", ")");
}
