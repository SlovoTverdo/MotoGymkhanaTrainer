using Godot;
using MotoGymkhanaTrainer.ExerciseEditor;
using MotoGymkhanaTrainer.Tracks;

namespace MotoGymkhanaTrainer.TrackEditor;

/// <summary>Separate application scene for authoring Track Project v2 files.</summary>
public partial class TrackEditor : Control
{
    private enum PendingAction { None, New, OpenDialog, OpenLibrary }

    private TrackProjectDocument _document = TrackProjectDocument.CreateNew();
    private SandboxedJsonLibrary? _exerciseLibrary;
    private SandboxedJsonLibrary? _trackLibrary;
    private SandboxedJsonLibrary? _exportLibrary;
    private TrackEditorCanvas? _canvas;
    private ExerciseLibraryTree? _exerciseTree;
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
    private Label? _transitionTitle;
    private Label? _transitionPair;
    private Label? _transitionMode;
    private Label? _transitionStart;
    private Label? _transitionEnd;
    private SpinBox? _control1X;
    private SpinBox? _control1Y;
    private SpinBox? _control2X;
    private SpinBox? _control2Y;
    private SpinBox? _control1OffsetX;
    private SpinBox? _control1OffsetY;
    private SpinBox? _control2OffsetX;
    private SpinBox? _control2OffsetY;
    private Button? _resetTransitionButton;
    private Button? _removeOrphanedButton;
    private Label? _fileLabel;
    private Label? _dirtyLabel;
    private Label? _statusLabel;
    private RichTextLabel? _validationLabel;
    private CheckButton? _showTransitions;
    private FileDialog? _openDialog;
    private FileDialog? _saveDialog;
    private FileDialog? _exportDialog;
    private ConfirmationDialog? _unsavedDialog;
    private ConfirmationDialog? _newFolderDialog;
    private ConfirmationDialog? _removeOrphanedDialog;
    private LineEdit? _newFolderName;
    private string? _currentFilePath;
    private string _selectedExercisePath = string.Empty;
    private string _selectedTrackFolder = string.Empty;
    private string? _pendingOpenPath;
    private PendingAction _pendingAction;
    private bool _dirty;
    private bool _updatingUi;
    private TrackCompilationResult _compilation = new();

    public override void _Ready()
    {
        _exerciseLibrary = new SandboxedJsonLibrary(
            ProjectSettings.GlobalizePath("res://exercises"), "Exercise library", "res://exercises/");
        _trackLibrary = new SandboxedJsonLibrary(
            ProjectSettings.GlobalizePath("res://tracks"), "Track Project library", "res://tracks/");
        _exportLibrary = new SandboxedJsonLibrary(
            ProjectSettings.GlobalizePath("res://exports/tracks"), "Track export library", "res://exports/tracks/");
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
        _canvas.ExerciseDropped += AddExerciseAt;
        _canvas.TransitionControlPointDragged += OnTransitionControlPointDragged;
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
        _validationLabel = new RichTextLabel
        {
            Name = "CompilationDiagnostics",
            BbcodeEnabled = true,
            FitContent = false,
            ScrollActive = true,
            CustomMinimumSize = new Vector2(0, 72),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        page.AddChild(_validationLabel);
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
        toolbar.AddChild(CreateButton("ExportViewerButton", "Export for Viewer", ShowExportDialog));
        _showTransitions = new CheckButton
        {
            Name = "ShowTransitions",
            Text = "Show transitions",
            ButtonPressed = true,
        };
        _showTransitions.Toggled += visible =>
            _canvas?.SetTransitionPreview(_compilation.Transitions, visible);
        toolbar.AddChild(_showTransitions);
        _removeOrphanedButton = CreateButton(
            "RemoveOrphanedOverrides", "Remove Orphaned Overrides", ShowRemoveOrphanedConfirmation);
        toolbar.AddChild(_removeOrphanedButton);
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
        panel.AddChild(Section("Exercise Library — res://exercises/"));
        var exerciseActions = new HBoxContainer();
        exerciseActions.AddChild(CreateButton("RefreshExercises", "Refresh", RefreshExerciseTree));
        exerciseActions.AddChild(CreateButton("AddToTrack", "Add to Track", AddSelectedExercise));
        panel.AddChild(exerciseActions);
        _exerciseTree = new ExerciseLibraryTree
        {
            Name = "ExerciseTree",
            HideRoot = false,
            CustomMinimumSize = new Vector2(0, 260),
        };
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
        _scaleX = Spin("ScaleX", -10, 10, 0.05, string.Empty);
        _scaleY = Spin("ScaleY", -10, 10, 0.05, string.Empty);
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
        var mirrorButtons = new VBoxContainer { Name = "MirrorButtons" };
        _mirrorHorizontalButton = CreateButton(
            "MirrorHorizontal", "Отразить зеркально горизонтально (X)", () => MirrorSelected(vertical: false));
        _mirrorVerticalButton = CreateButton(
            "MirrorVertical", "Отразить зеркально вертикально (Y)", () => MirrorSelected(vertical: true));
        mirrorButtons.AddChild(_mirrorHorizontalButton);
        mirrorButtons.AddChild(_mirrorVerticalButton);
        panel.AddChild(mirrorButtons);

        panel.AddChild(Section("Selected transition"));
        _transitionTitle = new Label { Name = "TransitionId", Text = "None" };
        _transitionPair = new Label { Name = "TransitionPair", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _transitionMode = new Label { Name = "TransitionMode" };
        _transitionStart = new Label { Name = "TransitionStart" };
        _transitionEnd = new Label { Name = "TransitionEnd" };
        panel.AddChild(_transitionTitle);
        panel.AddChild(_transitionPair);
        panel.AddChild(_transitionMode);
        panel.AddChild(_transitionStart);
        _control1X = TransitionSpin("Control1X");
        _control1Y = TransitionSpin("Control1Y");
        _control2X = TransitionSpin("Control2X");
        _control2Y = TransitionSpin("Control2Y");
        _control1OffsetX = TransitionSpin("Control1OffsetX");
        _control1OffsetY = TransitionSpin("Control1OffsetY");
        _control2OffsetX = TransitionSpin("Control2OffsetX");
        _control2OffsetY = TransitionSpin("Control2OffsetY");
        _control1X.ValueChanged += _ => OnTransitionCoordinatesEdited(1, absolute: true);
        _control1Y.ValueChanged += _ => OnTransitionCoordinatesEdited(1, absolute: true);
        _control2X.ValueChanged += _ => OnTransitionCoordinatesEdited(2, absolute: true);
        _control2Y.ValueChanged += _ => OnTransitionCoordinatesEdited(2, absolute: true);
        _control1OffsetX.ValueChanged += _ => OnTransitionCoordinatesEdited(1, absolute: false);
        _control1OffsetY.ValueChanged += _ => OnTransitionCoordinatesEdited(1, absolute: false);
        _control2OffsetX.ValueChanged += _ => OnTransitionCoordinatesEdited(2, absolute: false);
        _control2OffsetY.ValueChanged += _ => OnTransitionCoordinatesEdited(2, absolute: false);
        panel.AddChild(Row("Control 1 X", _control1X));
        panel.AddChild(Row("Control 1 Y", _control1Y));
        panel.AddChild(Row("Control 2 X", _control2X));
        panel.AddChild(Row("Control 2 Y", _control2Y));
        panel.AddChild(_transitionEnd);
        panel.AddChild(Row("C1 offset X", _control1OffsetX));
        panel.AddChild(Row("C1 offset Y", _control1OffsetY));
        panel.AddChild(Row("C2 offset X", _control2OffsetX));
        panel.AddChild(Row("C2 offset Y", _control2OffsetY));
        _resetTransitionButton = CreateButton(
            "ResetTransition", "Reset to Automatic", ResetSelectedTransition);
        panel.AddChild(_resetTransitionButton);
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
        _exportDialog = JsonDialog("ExportViewerTrack", "Export Track for Viewer", FileDialog.FileModeEnum.SaveFile);
        _exportDialog.CurrentDir = _exportLibrary!.RootPath;
        _exportDialog.FileSelected += ExportForViewer;
        _exportDialog.Canceled += () => SetStatus("Export canceled; Track Project was not changed.", false);
        AddChild(_exportDialog);

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

        _removeOrphanedDialog = new ConfirmationDialog
        {
            Name = "RemoveOrphanedOverridesDialog",
            Title = "Remove orphaned transition overrides",
            DialogText = "Permanently remove all orphaned manual transition overrides?",
            OkButtonText = "Remove",
            Size = new Vector2I(560, 180),
        };
        _removeOrphanedDialog.Confirmed += RemoveOrphanedOverrides;
        AddChild(_removeOrphanedDialog);
    }

    private void RefreshExerciseTree()
    {
        FillTree(_exerciseTree!, "exercises", _exerciseLibrary!);
        if (_document.Project.Instances.Length == 0)
        {
            RefreshCompilation();
            return;
        }

        try
        {
            // Refresh is also the explicit dependency reload operation. Only the
            // runtime cache changes; project transforms/order and dirty state do not.
            string json = TrackProjectStore.Serialize(_document.Project, _exerciseLibrary!);
            TrackProjectLoadResult reloaded = TrackProjectStore.LoadFromJson(
                json, "current Track Project", _exerciseLibrary!);
            _document.ReplaceDefinitions(reloaded.Definitions);
            foreach (string warning in reloaded.Warnings) GD.PushWarning(warning);
            RefreshCompilation();
            SynchronizeRouteList();
            _canvas?.QueueRedraw();
        }
        catch (Exception exception)
        {
            SetStatus($"Exercise refresh failed: {exception.Message}", true);
            GD.PushError($"Unable to refresh Exercise dependencies: {exception}");
        }
    }

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
        AddExerciseAt(_selectedExercisePath, new Point2Dto());
    }

    private void AddExerciseAt(string relativePath, Point2Dto position)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                throw new InvalidOperationException("Select an Exercise JSON first.");
            string file = _exerciseLibrary!.ResolveExistingJson(relativePath);
            ExerciseDefinitionLoadResult load = ExerciseDefinitionStore.LoadFromFileWithDiagnostics(file);
            string id = _document.AddInstance(relativePath, load.Definition);
            _document.MoveInstance(id, position);
            _canvas!.SelectInstance(id);
            MarkChanged();
            foreach (string warning in load.Warnings) GD.PushWarning(warning);
            SetStatus(
                $"Added '{load.Definition.Exercise.Name}' as {id} at ({position.X:0.##}, {position.Y:0.##}) m.",
                false);
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
        RefreshCompilation();
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

        float scaleX = (float)_scaleX!.Value;
        float scaleY = (float)_scaleY!.Value;
        if (MathF.Abs(scaleX) < 0.1f || MathF.Abs(scaleY) < 0.1f)
        {
            // A scale sign is persisted as mirror state, therefore only zero is
            // invalid. Restore the last valid transform instead of silently
            // converting a requested negative value to +0.1.
            _updatingUi = true;
            _scaleX.Value = instance.Scale.X;
            _scaleY.Value = instance.Scale.Y;
            _updatingUi = false;
            SetStatus("Scale X/Y must be <= -0.1 or >= 0.1; zero is not allowed.", true);
            return;
        }

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
        RefreshCompilation();
        SynchronizeSelectionUi();
    }

    private void OnTransitionControlPointDragged(
        string transitionId,
        int controlIndex,
        Point2Dto absolutePoint)
    {
        CompiledTransition? transition = _compilation.Transitions.FirstOrDefault(
            item => item.TransitionId == transitionId);
        if (transition is null ||
            !_document.SetTransitionControlPoint(transition, controlIndex, absolutePoint))
        {
            return;
        }

        MarkChanged();
    }

    private void OnTransitionCoordinatesEdited(int controlIndex, bool absolute)
    {
        if (_updatingUi || CurrentSelectedTransition() is not CompiledTransition transition)
        {
            return;
        }

        Point2Dto point;
        if (absolute)
        {
            point = controlIndex == 1
                ? new Point2Dto { X = (float)_control1X!.Value, Y = (float)_control1Y!.Value }
                : new Point2Dto { X = (float)_control2X!.Value, Y = (float)_control2Y!.Value };
        }
        else
        {
            Point2Dto endpoint = controlIndex == 1 ? transition.Start : transition.End;
            point = controlIndex == 1
                ? new Point2Dto
                {
                    X = endpoint.X + (float)_control1OffsetX!.Value,
                    Y = endpoint.Y + (float)_control1OffsetY!.Value,
                }
                : new Point2Dto
                {
                    X = endpoint.X + (float)_control2OffsetX!.Value,
                    Y = endpoint.Y + (float)_control2OffsetY!.Value,
                };
        }

        if (_document.SetTransitionControlPoint(transition, controlIndex, point))
        {
            MarkChanged();
        }
    }

    private void ResetSelectedTransition()
    {
        if (CurrentSelectedTransition() is not CompiledTransition transition ||
            !_document.ResetTransition(transition.FromInstanceId, transition.ToInstanceId))
        {
            return;
        }

        MarkChanged();
        SetStatus($"Transition '{transition.TransitionId}' reset to automatic.", false);
    }

    private void ShowRemoveOrphanedConfirmation()
    {
        int count = _document.GetOrphanedTransitionOverrides().Count;
        if (count == 0)
        {
            SetStatus("There are no orphaned transition overrides.", false);
            return;
        }

        _removeOrphanedDialog!.DialogText =
            $"Permanently remove {count} orphaned manual transition override(s)?";
        _removeOrphanedDialog.PopupCentered();
    }

    private void RemoveOrphanedOverrides()
    {
        int removed = _document.RemoveOrphanedTransitionOverrides();
        if (removed == 0)
        {
            return;
        }

        MarkChanged();
        SetStatus($"Removed {removed} orphaned transition override(s).", false);
    }

    private void Reorder(bool up)
    {
        if (_canvas!.SelectedInstanceId is not string id) return;
        bool changed = up ? _document.MoveUp(id) : _document.MoveDown(id);
        if (changed) MarkChanged();
    }

    private bool DeleteSelected()
    {
        if (_canvas?.SelectedInstanceId is not string id) return false;
        int relatedOverrides = _document.CountRelatedTransitionOverrides(id);
        if (!_document.DeleteInstance(id)) return false;
        _canvas.SelectInstance(null);
        MarkChanged();
        SetStatus(relatedOverrides == 0
            ? $"Deleted instance '{id}'. Exercise Definition file was not changed."
            : $"Deleted instance '{id}'; {relatedOverrides} related override(s) were kept orphaned.",
            relatedOverrides > 0);
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
            _scaleX!.Value = instance.Scale.X;
            _scaleY!.Value = instance.Scale.Y;
        }
        SynchronizeTransitionProperties();
        _removeOrphanedButton!.Disabled = _document.GetOrphanedTransitionOverrides().Count == 0;
        _updatingUi = false;
        SynchronizeRouteList();
    }

    private void SynchronizeTransitionProperties()
    {
        CompiledTransition? transition = CurrentSelectedTransition();
        bool enabled = transition is not null;
        _transitionTitle!.Text = enabled ? transition!.TransitionId : "None";
        _transitionPair!.Text = enabled
            ? $"from: {transition!.FromInstanceId}\nto: {transition.ToInstanceId}"
            : string.Empty;
        _transitionMode!.Text = enabled ? $"Mode: {transition!.SourceMode}" : string.Empty;
        _transitionStart!.Text = enabled
            ? $"Start: {FormatPoint(transition!.Start)} (read-only)"
            : string.Empty;
        _transitionEnd!.Text = enabled
            ? $"End: {FormatPoint(transition!.End)} (read-only)"
            : string.Empty;
        foreach (SpinBox spin in TransitionSpins())
        {
            spin.Editable = enabled;
        }

        _resetTransitionButton!.Disabled = !enabled ||
            transition!.SourceMode != TransitionSourceMode.Override;
        if (!enabled)
        {
            return;
        }

        Point2Dto offset1 = Subtract(transition!.Control1, transition.Start);
        Point2Dto offset2 = Subtract(transition.Control2, transition.End);
        _control1X!.Value = transition.Control1.X;
        _control1Y!.Value = transition.Control1.Y;
        _control2X!.Value = transition.Control2.X;
        _control2Y!.Value = transition.Control2.Y;
        _control1OffsetX!.Value = offset1.X;
        _control1OffsetY!.Value = offset1.Y;
        _control2OffsetX!.Value = offset2.X;
        _control2OffsetY!.Value = offset2.Y;
    }

    private CompiledTransition? CurrentSelectedTransition() =>
        _canvas?.SelectedTransitionId is string id
            ? _compilation.Transitions.FirstOrDefault(item => item.TransitionId == id)
            : null;

    private void MarkChanged()
    {
        SetDirty(true);
        RefreshCompilation();
        SynchronizeRouteList();
        SynchronizeSelectionUi();
        _canvas!.QueueRedraw();
    }

    private void RefreshCompilation()
    {
        _compilation = TrackCompiler.Compile(_document);
        _canvas?.SetTransitionPreview(
            _compilation.Transitions,
            _showTransitions?.ButtonPressed ?? true);
        if (_validationLabel is null) return;

        var lines = new List<string>
        {
            $"[b]Export validation:[/b] {_compilation.Errors.Count} error(s), {_compilation.Warnings.Count} warning(s)",
        };
        lines.AddRange(_compilation.Errors.Select(item => $"[color=#ff786e]ERROR: {EscapeBbcode(item.Message)}[/color]"));
        lines.AddRange(_compilation.Warnings.Select(item => $"[color=#ffd166]WARNING: {EscapeBbcode(item.Message)}[/color]"));
        _validationLabel.Text = string.Join("\n", lines);
    }

    private void ShowExportDialog()
    {
        RefreshCompilation();
        if (!_compilation.CanExport)
        {
            SetStatus($"Export blocked: {_compilation.Errors.Count} validation error(s).", true);
            return;
        }

        try
        {
            _exportDialog!.CurrentDir = _exportLibrary!.RootPath;
            _exportDialog.CurrentFile = SandboxedJsonLibrary.SuggestFileName(
                _document.Project.Track.Id, "track-export");
            _exportDialog.PopupCenteredRatio(0.82f);
        }
        catch (Exception exception)
        {
            SetStatus($"Export dialog failed: {exception.Message}", true);
        }
    }

    private void ExportForViewer(string path)
    {
        try
        {
            RefreshCompilation();
            if (!_compilation.CanExport || _compilation.Snapshot is null)
                throw new InvalidDataException($"Export blocked by {_compilation.Errors.Count} validation error(s).");

            string requested = ToFilesystemPath(path);
            string directory = Path.GetDirectoryName(requested) ?? _exportLibrary!.RootPath;
            string target = _exportLibrary!.ResolveSaveJson(
                _exportLibrary.ToRelative(directory), Path.GetFileName(requested));
            TrackExportStore.SaveToFile(_compilation.Snapshot, target);
            // Export is a derived snapshot operation, not a Track Project save;
            // therefore _dirty is intentionally left unchanged.
            SetStatus(
                $"Exported Viewer Track to '{target}' with {_compilation.Warnings.Count} warning(s).",
                false);
        }
        catch (Exception exception)
        {
            SetStatus($"Export failed: {exception.Message}", true);
            GD.PushError($"Unable to export Viewer Track '{path}': {exception}");
        }
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

    private static SpinBox TransitionSpin(string name) => Spin(name, -10000, 10000, 0.05, " m");

    private IEnumerable<SpinBox> TransitionSpins() =>
    [
        _control1X!, _control1Y!, _control2X!, _control2Y!,
        _control1OffsetX!, _control1OffsetY!, _control2OffsetX!, _control2OffsetY!,
    ];

    private static string FormatPoint(Point2Dto point) => $"({point.X:0.###}, {point.Y:0.###}) m";

    private static Point2Dto Subtract(Point2Dto left, Point2Dto right) =>
        new() { X = left.X - right.X, Y = left.Y - right.Y };

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

    private static string EscapeBbcode(string value) => value.Replace("[", "(").Replace("]", ")");
}
