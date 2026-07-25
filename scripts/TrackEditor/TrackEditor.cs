using Godot;
using MotoGymkhanaTrainer.ExerciseEditor;
using MotoGymkhanaTrainer.Tracks;

namespace MotoGymkhanaTrainer.TrackEditor;

/// <summary>Separate application scene for authoring Track Project v1 files.</summary>
public partial class TrackEditor : Control
{
    private enum PendingAction { None, New, OpenDialog, OpenLibrary }

    private TrackProjectDocument _document = TrackProjectDocument.CreateNew();
    private SandboxedJsonLibrary? _exerciseLibrary;
    private SandboxedJsonLibrary? _trackLibrary;
    private TrackEditorCanvas? _canvas;
    private Tree? _exerciseTree;
    private Tree? _trackTree;
    private ItemList? _routeList;
    private LineEdit? _trackId;
    private LineEdit? _trackName;
    private SpinBox? _areaWidth;
    private SpinBox? _areaLength;
    private Label? _selectionTitle;
    private Label? _exercisePath;
    private SpinBox? _positionX;
    private SpinBox? _positionY;
    private SpinBox? _rotation;
    private SpinBox? _scaleX;
    private SpinBox? _scaleY;
    private Button? _mirrorVerticalButton;
    private Button? _mirrorHorizontalButton;
    private Label? _fileLabel;
    private Label? _dirtyLabel;
    private Label? _statusLabel;
    private FileDialog? _openDialog;
    private FileDialog? _saveDialog;
    private ConfirmationDialog? _unsavedDialog;
    private ConfirmationDialog? _newFolderDialog;
    private LineEdit? _newFolderName;
    private string? _currentFilePath;
    private string _selectedExercisePath = string.Empty;
    private string _selectedTrackFolder = string.Empty;
    private string? _pendingOpenPath;
    private PendingAction _pendingAction;
    private bool _dirty;
    private bool _updatingUi;

    public override void _Ready()
    {
        _exerciseLibrary = new SandboxedJsonLibrary(
            ProjectSettings.GlobalizePath("res://exercises"), "Exercise library", "res://exercises/");
        _trackLibrary = new SandboxedJsonLibrary(
            ProjectSettings.GlobalizePath("res://tracks"), "Track Project library", "res://tracks/");
        BuildUi();
        ReplaceDocument(TrackProjectDocument.CreateNew(), null, dirty: true);
        SetStatus("New Track Project created.", false);
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Delete } && DeleteSelected())
        {
            GetViewport().SetInputAsHandled();
        }
    }

    private void BuildUi()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var page = new VBoxContainer { Name = "TrackEditorLayout" };
        page.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        page.AddThemeConstantOverride("separation", 6);
        AddChild(page);
        page.AddChild(BuildToolbar());
        page.AddChild(BuildFileStatus());

        var body = new HSplitContainer
        {
            Name = "TrackEditorBody",
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SplitOffsets = [300, 1150],
        };
        body.AddChild(BuildLibraries());

        var center = new VBoxContainer { Name = "CanvasAndRoute", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _canvas = new TrackEditorCanvas
        {
            Name = "TrackCanvas",
            CustomMinimumSize = new Vector2(600, 420),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        _canvas.SelectionChanged += SynchronizeSelectionUi;
        _canvas.DocumentChanged += OnCanvasChanged;
        center.AddChild(_canvas);
        center.AddChild(BuildRouteOrder());
        body.AddChild(center);
        body.AddChild(BuildProperties());
        page.AddChild(body);

        _statusLabel = new Label
        {
            Name = "StatusMessage",
            Text = "Ready.",
            CustomMinimumSize = new Vector2(0, 28),
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            ClipText = true,
        };
        page.AddChild(_statusLabel);
        BuildDialogs();
    }

    private Control BuildToolbar()
    {
        var toolbar = new HBoxContainer { Name = "Toolbar", CustomMinimumSize = new Vector2(0, 42) };
        toolbar.AddThemeConstantOverride("separation", 8);
        toolbar.AddChild(CreateButton("NewButton", "New", () => RequestAction(PendingAction.New)));
        toolbar.AddChild(CreateButton("OpenButton", "Open", () => RequestAction(PendingAction.OpenDialog)));
        toolbar.AddChild(CreateButton("SaveButton", "Save", Save));
        toolbar.AddChild(CreateButton("SaveAsButton", "Save As", ShowSaveAs));
        toolbar.AddChild(new VSeparator());
        toolbar.AddChild(CreateButton("DeleteButton", "Delete Instance", () => DeleteSelected()));
        return toolbar;
    }

    private Control BuildFileStatus()
    {
        var bar = new HBoxContainer { Name = "FileStatus", CustomMinimumSize = new Vector2(0, 28) };
        _fileLabel = new Label { Name = "CurrentFile", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _dirtyLabel = new Label { Name = "DirtyIndicator" };
        bar.AddChild(_fileLabel);
        bar.AddChild(_dirtyLabel);
        return bar;
    }

    private Control BuildLibraries()
    {
        var scroll = new ScrollContainer { Name = "LibrariesScroll", CustomMinimumSize = new Vector2(300, 0) };
        var panel = new VBoxContainer { Name = "Libraries", CustomMinimumSize = new Vector2(292, 0) };
        panel.AddChild(Section("Selected instance tools"));
        _mirrorVerticalButton = CreateButton(
            "MirrorVertical", "Отразить зеркально вертикально", () => MirrorSelected(vertical: true));
        _mirrorHorizontalButton = CreateButton(
            "MirrorHorizontal", "Отразить зеркально горизонтально", () => MirrorSelected(vertical: false));
        panel.AddChild(_mirrorVerticalButton);
        panel.AddChild(_mirrorHorizontalButton);
        panel.AddChild(Section("Exercise Library — res://exercises/"));
        var exerciseActions = new HBoxContainer();
        exerciseActions.AddChild(CreateButton("RefreshExercises", "Refresh", RefreshExerciseTree));
        exerciseActions.AddChild(CreateButton("AddToTrack", "Add to Track", AddSelectedExercise));
        panel.AddChild(exerciseActions);
        _exerciseTree = new Tree { Name = "ExerciseTree", HideRoot = false, CustomMinimumSize = new Vector2(0, 260) };
        _exerciseTree.ItemSelected += OnExerciseSelected;
        panel.AddChild(_exerciseTree);

        panel.AddChild(Section("Track Projects — res://tracks/"));
        var trackActions = new HBoxContainer();
        trackActions.AddChild(CreateButton("RefreshTracks", "Refresh", RefreshTrackTree));
        trackActions.AddChild(CreateButton("NewTrackFolder", "New Folder", ShowNewFolderDialog));
        panel.AddChild(trackActions);
        _trackTree = new Tree { Name = "TrackProjectTree", HideRoot = false, CustomMinimumSize = new Vector2(0, 260) };
        _trackTree.ItemSelected += OnTrackItemSelected;
        _trackTree.ItemActivated += OnTrackItemActivated;
        panel.AddChild(_trackTree);
        scroll.AddChild(panel);
        RefreshExerciseTree();
        RefreshTrackTree();
        return scroll;
    }

    private Control BuildProperties()
    {
        var scroll = new ScrollContainer { Name = "PropertiesScroll", CustomMinimumSize = new Vector2(320, 0) };
        var panel = new VBoxContainer { Name = "Properties", CustomMinimumSize = new Vector2(312, 0) };
        panel.AddChild(Section("Track Project"));
        _trackId = new LineEdit { Name = "TrackId" };
        _trackName = new LineEdit { Name = "TrackName" };
        _trackId.TextChanged += _ => OnMetadataEdited();
        _trackName.TextChanged += _ => OnMetadataEdited();
        panel.AddChild(Row("Id", _trackId));
        panel.AddChild(Row("Name", _trackName));
        _areaWidth = Spin("AreaWidth", 1, 10000, 1, " m");
        _areaLength = Spin("AreaLength", 1, 10000, 1, " m");
        _areaWidth.ValueChanged += _ => OnAreaEdited();
        _areaLength.ValueChanged += _ => OnAreaEdited();
        panel.AddChild(Row("Area width", _areaWidth));
        panel.AddChild(Row("Area length", _areaLength));

        panel.AddChild(Section("Selected instance"));
        _selectionTitle = new Label { Name = "SelectionTitle", Text = "None" };
        _exercisePath = new Label { Name = "ExercisePath", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        panel.AddChild(_selectionTitle);
        panel.AddChild(_exercisePath);
        _positionX = Spin("PositionX", -10000, 10000, 0.25, " m");
        _positionY = Spin("PositionY", -10000, 10000, 0.25, " m");
        _rotation = Spin("RotationDeg", -100000, 100000, 1, "°");
        _scaleX = Spin("ScaleX", 0.1, 10, 0.05, string.Empty);
        _scaleY = Spin("ScaleY", 0.1, 10, 0.05, string.Empty);
        foreach (SpinBox spin in new[] { _positionX, _positionY, _rotation, _scaleX, _scaleY })
        {
            spin.ValueChanged += _ => OnTransformEdited();
        }
        panel.AddChild(Row("Position X", _positionX));
        panel.AddChild(Row("Position Y", _positionY));
        panel.AddChild(Row("Rotation", _rotation));
        var rotateButtons = new HBoxContainer();
        rotateButtons.AddChild(CreateButton("RotateMinus15", "-15°", () => RotateBy(-15)));
        rotateButtons.AddChild(CreateButton("RotatePlus15", "+15°", () => RotateBy(15)));
        panel.AddChild(rotateButtons);
        panel.AddChild(Row("Scale X", _scaleX));
        panel.AddChild(Row("Scale Y", _scaleY));
        scroll.AddChild(panel);
        return scroll;
    }

    private Control BuildRouteOrder()
    {
        var panel = new VBoxContainer { Name = "RouteOrder", CustomMinimumSize = new Vector2(0, 190) };
        panel.AddChild(Section("Route Order / instances[]"));
        _routeList = new ItemList { Name = "RouteOrderList", CustomMinimumSize = new Vector2(0, 110) };
        _routeList.ItemSelected += OnRouteSelected;
        panel.AddChild(_routeList);
        var actions = new HBoxContainer();
        actions.AddChild(CreateButton("MoveUp", "Move Up", () => Reorder(up: true)));
        actions.AddChild(CreateButton("MoveDown", "Move Down", () => Reorder(up: false)));
        actions.AddChild(CreateButton("DeleteRoute", "Delete", () => DeleteSelected()));
        panel.AddChild(actions);
        return panel;
    }

    private void BuildDialogs()
    {
        _openDialog = JsonDialog("OpenTrackProject", "Open Track Project", FileDialog.FileModeEnum.OpenFile);
        _openDialog.CurrentDir = _trackLibrary!.RootPath;
        _openDialog.FileSelected += OpenProject;
        _openDialog.Canceled += () => SetStatus("Open canceled; current project preserved.", false);
        AddChild(_openDialog);
        _saveDialog = JsonDialog("SaveTrackProject", "Save Track Project", FileDialog.FileModeEnum.SaveFile);
        _saveDialog.CurrentDir = _trackLibrary.RootPath;
        _saveDialog.FileSelected += SaveProject;
        _saveDialog.Canceled += () => SetStatus("Save canceled.", false);
        AddChild(_saveDialog);

        _unsavedDialog = new ConfirmationDialog
        {
            Name = "UnsavedChangesDialog",
            Title = "Unsaved changes",
            DialogText = "Discard unsaved Track Project changes?",
            OkButtonText = "Discard",
            Size = new Vector2I(520, 170),
        };
        _unsavedDialog.Confirmed += ContinuePendingAction;
        _unsavedDialog.Canceled += () => { _pendingAction = PendingAction.None; _pendingOpenPath = null; };
        AddChild(_unsavedDialog);

        _newFolderName = new LineEdit { Name = "NewFolderName", PlaceholderText = "folder-name" };
        _newFolderDialog = new ConfirmationDialog
        {
            Name = "NewTrackFolderDialog",
            Title = "Create Track Project Folder",
            DialogText = "Create a child folder in the selected Track Project folder:",
            Size = new Vector2I(520, 190),
        };
        _newFolderDialog.AddChild(_newFolderName);
        _newFolderDialog.Confirmed += CreateTrackFolder;
        AddChild(_newFolderDialog);
    }

    private void RefreshExerciseTree() => FillTree(_exerciseTree!, "exercises", _exerciseLibrary!);

    private void RefreshTrackTree() => FillTree(_trackTree!, "tracks", _trackLibrary!);

    private static void FillTree(Tree tree, string rootName, SandboxedJsonLibrary library)
    {
        tree.Clear();
        TreeItem root = tree.CreateItem();
        root.SetText(0, rootName);
        root.SetMetadata(0, "D|");
        var folders = new Dictionary<string, TreeItem>(StringComparer.OrdinalIgnoreCase) { [string.Empty] = root };
        foreach (JsonLibraryEntry entry in library.EnumerateEntries())
        {
            string parentPath = Path.GetDirectoryName(entry.RelativePath) ?? string.Empty;
            TreeItem parent = folders.GetValueOrDefault(parentPath, root);
            TreeItem item = tree.CreateItem(parent);
            item.SetText(0, entry.IsDirectory ? $"📁 {entry.DisplayName}" : entry.DisplayName);
            item.SetMetadata(0, $"{(entry.IsDirectory ? 'D' : 'F')}|{entry.RelativePath}");
            if (entry.IsDirectory) folders[entry.RelativePath] = item;
        }
    }

    private void OnExerciseSelected()
    {
        if (ReadTreeSelection(_exerciseTree, out bool directory, out string path) && !directory)
        {
            _selectedExercisePath = path;
            SetStatus($"Selected Exercise Definition: {path}", false);
        }
    }

    private void AddSelectedExercise()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_selectedExercisePath))
                throw new InvalidOperationException("Select an Exercise JSON first.");
            string file = _exerciseLibrary!.ResolveExistingJson(_selectedExercisePath);
            ExerciseDefinitionLoadResult load = ExerciseDefinitionStore.LoadFromFileWithDiagnostics(file);
            string id = _document.AddInstance(_selectedExercisePath, load.Definition);
            _canvas!.SelectInstance(id);
            MarkChanged();
            foreach (string warning in load.Warnings) GD.PushWarning(warning);
            SetStatus($"Added '{load.Definition.Exercise.Name}' as {id} at the area origin.", false);
        }
        catch (Exception exception)
        {
            SetStatus($"Add failed: {exception.Message}", true);
            GD.PushError($"Unable to add Exercise Definition: {exception}");
        }
    }

    private void OnTrackItemSelected()
    {
        if (!ReadTreeSelection(_trackTree, out bool directory, out string path)) return;
        _selectedTrackFolder = directory ? path : Path.GetDirectoryName(path) ?? string.Empty;
    }

    private void OnTrackItemActivated()
    {
        if (!ReadTreeSelection(_trackTree, out bool directory, out string path) || directory) return;
        _pendingOpenPath = path;
        RequestAction(PendingAction.OpenLibrary);
    }

    private static bool ReadTreeSelection(Tree? tree, out bool directory, out string relativePath)
    {
        directory = false;
        relativePath = string.Empty;
        TreeItem? selected = tree?.GetSelected();
        if (selected is null) return false;
        string metadata = selected.GetMetadata(0).AsString();
        if (metadata.Length < 2 || metadata[1] != '|') return false;
        directory = metadata[0] == 'D';
        relativePath = metadata[2..];
        return true;
    }

    private void ShowNewFolderDialog()
    {
        _newFolderName!.Text = string.Empty;
        _newFolderDialog!.PopupCentered();
        _newFolderName.GrabFocus();
    }

    private void CreateTrackFolder()
    {
        try
        {
            _selectedTrackFolder = _trackLibrary!.CreateFolder(_selectedTrackFolder, _newFolderName!.Text);
            RefreshTrackTree();
            SetStatus($"Created res://tracks/{_selectedTrackFolder.Replace('\\', '/')}", false);
        }
        catch (Exception exception)
        {
            SetStatus($"Folder creation failed: {exception.Message}", true);
            GD.PushError($"Track folder creation failed: {exception}");
        }
    }

    private void RequestAction(PendingAction action)
    {
        _pendingAction = action;
        if (_dirty) _unsavedDialog!.PopupCentered(); else ContinuePendingAction();
    }

    private void ContinuePendingAction()
    {
        PendingAction action = _pendingAction;
        _pendingAction = PendingAction.None;
        if (action == PendingAction.New)
        {
            ReplaceDocument(TrackProjectDocument.CreateNew(), null, dirty: true);
            SetStatus("New Track Project created.", false);
        }
        else if (action == PendingAction.OpenDialog)
        {
            _openDialog!.PopupCenteredRatio(0.82f);
        }
        else if (action == PendingAction.OpenLibrary && _pendingOpenPath is not null)
        {
            string path = _pendingOpenPath;
            _pendingOpenPath = null;
            OpenProject(_trackLibrary!.ResolveExistingJson(path));
        }
    }

    private void Save()
    {
        if (_currentFilePath is null) ShowSaveAs(); else SaveProject(_currentFilePath);
    }

    private void ShowSaveAs()
    {
        try
        {
            _saveDialog!.CurrentDir = _trackLibrary!.ResolveFolder(_selectedTrackFolder);
            _saveDialog.CurrentFile = SandboxedJsonLibrary.SuggestFileName(_document.Project.Track.Id, "track");
            _saveDialog.PopupCenteredRatio(0.82f);
        }
        catch (Exception exception) { SetStatus($"Save As failed: {exception.Message}", true); }
    }

    private void SaveProject(string path)
    {
        try
        {
            string requested = ToFilesystemPath(path);
            string directory = Path.GetDirectoryName(requested) ?? _trackLibrary!.RootPath;
            string target = _trackLibrary!.ResolveSaveJson(
                _trackLibrary.ToRelative(directory), Path.GetFileName(requested));
            TrackProjectStore.SaveToFile(_document.Project, target, _exerciseLibrary!);
            _currentFilePath = target;
            SetDirty(false);
            RefreshTrackTree();
            SetStatus($"Saved Track Project to '{target}'.", false);
        }
        catch (Exception exception)
        {
            SetStatus($"Save failed: {exception.Message}", true);
            GD.PushError($"Unable to save Track Project '{path}': {exception}");
        }
    }

    private void OpenProject(string path)
    {
        try
        {
            string file = _trackLibrary!.ResolveExistingJson(ToFilesystemPath(path));
            // The candidate root and every safe dependency path are processed before
            // replacing the live document. Bad dependencies become placeholders.
            TrackProjectLoadResult loaded = TrackProjectStore.LoadFromFile(file, _exerciseLibrary!);
            ReplaceDocument(new TrackProjectDocument(loaded.Project, loaded.Definitions), file, dirty: false);
            foreach (string warning in loaded.Warnings) GD.PushWarning(warning);
            SetStatus(loaded.Warnings.Count == 0
                ? $"Loaded Track Project from '{file}'."
                : $"Loaded with {loaded.Warnings.Count} unresolved/migration warning(s).", loaded.Warnings.Count > 0);
        }
        catch (Exception exception)
        {
            SetStatus($"Open failed: {exception.Message}", true);
            GD.PushError($"Unable to open Track Project '{path}': {exception}");
        }
    }

    private void ReplaceDocument(TrackProjectDocument document, string? filePath, bool dirty)
    {
        _document = document;
        _currentFilePath = filePath;
        _canvas!.SetDocument(document);
        SynchronizeAllUi();
        SetDirty(dirty);
    }

    private void OnMetadataEdited()
    {
        if (_updatingUi) return;
        _document.Project.Track.Id = _trackId!.Text.Trim();
        _document.Project.Track.Name = _trackName!.Text.Trim();
        MarkChanged();
    }

    private void OnAreaEdited()
    {
        if (_updatingUi) return;
        _document.Project.Area.Width = (float)_areaWidth!.Value;
        _document.Project.Area.Length = (float)_areaLength!.Value;
        MarkChanged();
    }

    private void OnTransformEdited()
    {
        if (_updatingUi || _canvas!.SelectedInstanceId is not string id) return;
        TrackProjectInstanceDto? instance = _document.FindInstance(id);
        if (instance is null) return;

        // Numeric fields edit size magnitudes. Mirror state lives in the signs
        // and is changed only by the explicit buttons, so ordinary size edits
        // cannot accidentally remove a reflection.
        float scaleX = MathF.CopySign((float)_scaleX!.Value, instance.Scale.X);
        float scaleY = MathF.CopySign((float)_scaleY!.Value, instance.Scale.Y);
        if (_document.SetTransform(id,
            new Point2Dto { X = (float)_positionX!.Value, Y = (float)_positionY!.Value },
            (float)_rotation!.Value,
            new Point2Dto { X = scaleX, Y = scaleY }))
        {
            MarkChanged();
        }
    }

    private void MirrorSelected(bool vertical)
    {
        if (_canvas!.SelectedInstanceId is not string id)
        {
            SetStatus("Select an instance before mirroring.", true);
            return;
        }

        // Horizontal means left/right (X sign); vertical means top/bottom (Y sign).
        bool changed = vertical
            ? _document.ToggleVerticalMirror(id)
            : _document.ToggleHorizontalMirror(id);
        if (!changed) return;
        MarkChanged();
        SetStatus(vertical
            ? $"Instance '{id}' mirrored vertically (Y)."
            : $"Instance '{id}' mirrored horizontally (X).", false);
    }

    private void RotateBy(float degrees)
    {
        if (_canvas!.SelectedInstanceId is null) return;
        _rotation!.Value += degrees;
    }

    private void OnCanvasChanged()
    {
        SetDirty(true);
        SynchronizeSelectionUi();
    }

    private void Reorder(bool up)
    {
        if (_canvas!.SelectedInstanceId is not string id) return;
        bool changed = up ? _document.MoveUp(id) : _document.MoveDown(id);
        if (changed) MarkChanged();
    }

    private bool DeleteSelected()
    {
        if (_canvas?.SelectedInstanceId is not string id || !_document.DeleteInstance(id)) return false;
        _canvas.SelectInstance(null);
        MarkChanged();
        SetStatus($"Deleted instance '{id}'. Exercise Definition file was not changed.", false);
        return true;
    }

    private void OnRouteSelected(long index)
    {
        if (_updatingUi || index < 0 || index >= _document.Project.Instances.Length) return;
        _canvas!.SelectInstance(_document.Project.Instances[index].InstanceId);
    }

    private void SynchronizeAllUi()
    {
        _updatingUi = true;
        _trackId!.Text = _document.Project.Track.Id;
        _trackName!.Text = _document.Project.Track.Name;
        _areaWidth!.Value = _document.Project.Area.Width;
        _areaLength!.Value = _document.Project.Area.Length;
        _updatingUi = false;
        SynchronizeRouteList();
        SynchronizeSelectionUi();
    }

    private void SynchronizeRouteList()
    {
        _updatingUi = true;
        _routeList!.Clear();
        for (int index = 0; index < _document.Project.Instances.Length; index++)
        {
            TrackProjectInstanceDto instance = _document.Project.Instances[index];
            string warning = _document.FindDefinition(instance.InstanceId) is null ? " ⚠" : string.Empty;
            _routeList.AddItem($"{index + 1}. {_document.GetDisplayName(instance)} [{instance.InstanceId}]{warning}");
            if (instance.InstanceId == _canvas!.SelectedInstanceId) _routeList.Select(index);
        }
        _updatingUi = false;
    }

    private void SynchronizeSelectionUi()
    {
        _updatingUi = true;
        TrackProjectInstanceDto? instance = _canvas?.SelectedInstanceId is string id
            ? _document.FindInstance(id)
            : null;
        bool enabled = instance is not null;
        _selectionTitle!.Text = enabled ? instance!.InstanceId : "None";
        _exercisePath!.Text = enabled ? $"exercisePath: {instance!.ExercisePath}" : string.Empty;
        foreach (SpinBox spin in new[] { _positionX!, _positionY!, _rotation!, _scaleX!, _scaleY! }) spin.Editable = enabled;
        _mirrorVerticalButton!.Disabled = !enabled;
        _mirrorHorizontalButton!.Disabled = !enabled;
        if (enabled)
        {
            _positionX!.Value = instance!.Position.X;
            _positionY!.Value = instance.Position.Y;
            _rotation!.Value = instance.RotationDeg;
            _scaleX!.Value = MathF.Abs(instance.Scale.X);
            _scaleY!.Value = MathF.Abs(instance.Scale.Y);
        }
        _updatingUi = false;
        SynchronizeRouteList();
    }

    private void MarkChanged()
    {
        SetDirty(true);
        SynchronizeRouteList();
        SynchronizeSelectionUi();
        _canvas!.QueueRedraw();
    }

    private void SetDirty(bool dirty)
    {
        _dirty = dirty;
        _dirtyLabel!.Text = dirty ? "Modified" : "Saved";
        _dirtyLabel.Modulate = dirty ? new Color(1, 0.78f, 0.2f) : new Color(0.55f, 0.95f, 0.62f);
        _fileLabel!.Text = _currentFilePath ?? $"Untitled — {_document.Project.Track.Name}";
    }

    private void SetStatus(string message, bool error)
    {
        _statusLabel!.Text = message;
        _statusLabel.Modulate = error ? new Color(1, 0.48f, 0.42f) : new Color(0.82f, 0.88f, 0.94f);
    }

    private static Button CreateButton(string name, string text, Action action)
    {
        var button = new Button { Name = name, Text = text };
        button.Pressed += action;
        return button;
    }

    private static Label Section(string text)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", 17);
        return label;
    }

    private static Control Row(string label, Control editor)
    {
        var row = new HBoxContainer();
        row.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(100, 0) });
        editor.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(editor);
        return row;
    }

    private static SpinBox Spin(string name, double min, double max, double step, string suffix) => new()
    {
        Name = name,
        MinValue = min,
        MaxValue = max,
        Step = step,
        Suffix = suffix,
        AllowGreater = false,
        AllowLesser = false,
    };

    private static FileDialog JsonDialog(string name, string title, FileDialog.FileModeEnum mode) => new()
    {
        Name = name,
        Title = title,
        Access = FileDialog.AccessEnum.Filesystem,
        FileMode = mode,
        UseNativeDialog = false,
        Size = new Vector2I(900, 600),
        Filters = ["*.json ; Track Project JSON"],
    };

    private static string ToFilesystemPath(string path) =>
        path.StartsWith("res://", StringComparison.Ordinal) ? ProjectSettings.GlobalizePath(path) : path;
}
