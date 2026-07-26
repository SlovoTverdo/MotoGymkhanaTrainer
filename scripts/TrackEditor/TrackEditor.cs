using Godot;
using MotoGymkhanaTrainer.ExerciseEditor;
using MotoGymkhanaTrainer.Tracks;

namespace MotoGymkhanaTrainer.TrackEditor;

/// <summary>Separate application scene for authoring Venue-bound Track Project v3 files.</summary>
public partial class TrackEditor : Control
{
    private enum PendingAction { None, New, OpenDialog, OpenLibrary }

    private TrackProjectDocument _document = null!;
    private SandboxedJsonLibrary? _exerciseLibrary;
    private SandboxedJsonLibrary? _venueLibrary;
    private SandboxedJsonLibrary? _trackLibrary;
    private SandboxedJsonLibrary? _exportLibrary;
    private TrackEditorCanvas? _canvas;
    private ExerciseLibraryTree? _exerciseTree;
    private Tree? _trackTree;
    private ItemList? _routeList;
    private LineEdit? _trackId;
    private LineEdit? _trackName;
    private Label? _venueSummary;
    private Label? _selectionTitle;
    private Label? _exercisePath;
    private SpinBox? _positionX;
    private SpinBox? _positionY;
    private SpinBox? _rotation;
    private SpinBox? _scaleX;
    private SpinBox? _scaleY;
    private Button? _mirrorVerticalButton;
    private Button? _mirrorHorizontalButton;
    private Button? _duplicateButton;
    private CheckButton? _lockInstanceToggle;
    private Button? _undoButton;
    private Button? _redoButton;
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
    private ConfirmationDialog? _newTrackDialog;
    private AcceptDialog? _noVenuesDialog;
    private OptionButton? _newTrackVenue;
    private LineEdit? _newTrackId;
    private LineEdit? _newTrackName;
    private LineEdit? _newFolderName;
    private string? _currentFilePath;
    private string _selectedExercisePath = string.Empty;
    private string _selectedTrackFolder = string.Empty;
    private string? _pendingOpenPath;
    private PendingAction _pendingAction;
    private bool _dirty;
    private bool _updatingUi;
    private TrackCompilationResult _compilation = new();
    private readonly TrackProjectHistory _history = new(100);
    private readonly HashSet<string> _lockedInstanceIds = new(StringComparer.Ordinal);
    private string? _activeTransactionDescription;
    private Key? _keyboardTransformKey;

    public override void _Ready()
    {
        _exerciseLibrary = new SandboxedJsonLibrary(
            ProjectSettings.GlobalizePath("res://exercises"), "Exercise library", "res://exercises/");
        _venueLibrary = new SandboxedJsonLibrary(
            ProjectSettings.GlobalizePath("res://venues"), "Venue library", "res://venues/");
        _trackLibrary = new SandboxedJsonLibrary(
            ProjectSettings.GlobalizePath("res://tracks"), "Track Project library", "res://tracks/");
        _exportLibrary = new SandboxedJsonLibrary(
            ProjectSettings.GlobalizePath("res://exports/tracks"), "Track export library", "res://exports/tracks/");
        BuildUi();
        SetDocumentUiEnabled(false);
        SetStatus("Create a New Track by selecting a Venue, or open Track Project v3.", false);
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey key)
        {
            return;
        }

        if (!key.Pressed && _keyboardTransformKey == key.Keycode)
        {
            _keyboardTransformKey = null;
            EndEditTransaction();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (!key.Pressed || IsEditingText())
        {
            return;
        }

        bool handled = false;
        if (key.CtrlPressed && key.Keycode == Key.Z)
        {
            if (!key.Echo) handled = key.ShiftPressed ? Redo() : Undo();
        }
        else if (key.CtrlPressed && key.Keycode == Key.Y)
        {
            if (!key.Echo) handled = Redo();
        }
        else if (key.CtrlPressed && key.Keycode == Key.D)
        {
            if (!key.Echo) handled = DuplicateSelected();
        }
        else if (key.Keycode == Key.Delete)
        {
            if (!key.Echo) handled = DeleteSelected();
        }
        else if (IsTransformKey(key.Keycode))
        {
            handled = ApplyKeyboardTransform(key);
        }

        if (handled)
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
        _canvas.EditTransactionStarted += BeginEditTransaction;
        _canvas.EditTransactionFinished += EndEditTransaction;
        _canvas.DuplicateRequested += _ => DuplicateSelected();
        _canvas.LockedTransformAttempted += id =>
            SetStatus($"Instance '{id}' is locked. Unlock it before transforming.", true);
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
        toolbar.AddChild(CreateButton("ReloadVenueButton", "Reload Venue", ReloadVenue));
        toolbar.AddChild(new VSeparator());
        _undoButton = CreateButton("UndoButton", "Undo", () => Undo());
        _redoButton = CreateButton("RedoButton", "Redo", () => Redo());
        toolbar.AddChild(_undoButton);
        toolbar.AddChild(_redoButton);
        toolbar.AddChild(new VSeparator());
        toolbar.AddChild(CreateButton("ExportViewerButton", "Export for Viewer", ShowExportDialog));
        toolbar.AddChild(CreateButton("ExportOpenViewerButton", "Export and Open in Viewer", ExportAndOpenInViewer));
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
        toolbar.AddChild(CreateButton("DuplicateButtonToolbar", "Duplicate Instance", () => DuplicateSelected()));
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
        WireEditTransaction(_trackId, "Edit track id");
        WireEditTransaction(_trackName, "Edit track name");
        panel.AddChild(Row("Id", _trackId));
        panel.AddChild(Row("Name", _trackName));
        _venueSummary = new Label { Name = "VenueSummary", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        panel.AddChild(Row("Venue", _venueSummary));

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
            WireEditTransaction(spin.GetLineEdit(), "Edit instance transform");
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
        _duplicateButton = CreateButton("DuplicateInstance", "Duplicate Instance", () => DuplicateSelected());
        panel.AddChild(_duplicateButton);
        _lockInstanceToggle = new CheckButton { Name = "LockInstance", Text = "Lock Instance" };
        _lockInstanceToggle.Toggled += OnLockToggled;
        panel.AddChild(_lockInstanceToggle);

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
        foreach (SpinBox spin in TransitionSpins())
        {
            WireEditTransaction(spin.GetLineEdit(), "Edit transition control point");
        }
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

        _newTrackVenue = new OptionButton { Name = "NewTrackVenue", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _newTrackId = new LineEdit { Name = "NewTrackId", Text = "new-track" };
        _newTrackName = new LineEdit { Name = "NewTrackName", Text = "New Track" };
        var newTrackFields = new VBoxContainer { Name = "NewTrackFields" };
        newTrackFields.AddChild(Row("1. Venue", _newTrackVenue));
        newTrackFields.AddChild(Row("2. Track ID", _newTrackId));
        newTrackFields.AddChild(Row("3. Track Name", _newTrackName));
        _newTrackDialog = new ConfirmationDialog
        {
            Name = "NewTrackDialog",
            Title = "New Track Project v3",
            DialogText = "Select a Venue before entering Track metadata.",
            OkButtonText = "Create Track",
            Size = new Vector2I(620, 270),
        };
        _newTrackDialog.AddChild(newTrackFields);
        _newTrackDialog.Confirmed += CreateNewTrack;
        AddChild(_newTrackDialog);

        _noVenuesDialog = new AcceptDialog
        {
            Name = "NoVenuesDialog",
            Title = "Venue library is empty",
            DialogText = "New Track cannot be created without a Venue Definition. Create one in Venue Editor, then try again.",
            Size = new Vector2I(620, 180),
        };
        AddChild(_noVenuesDialog);
    }

    private void RefreshExerciseTree()
    {
        FillTree(_exerciseTree!, "exercises", _exerciseLibrary!);
        if (_document is null) return;
        if (_document.Project.Instances.Length == 0)
        {
            RefreshCompilation();
            return;
        }

        try
        {
            // Refresh is also the explicit dependency reload operation. Only the
            // runtime cache changes; project transforms/order and dirty state do not.
            string json = TrackProjectStore.SerializeHistorySnapshot(_document.Project);
            TrackProjectLoadResult reloaded = TrackProjectStore.RestoreHistorySnapshot(
                json, _exerciseLibrary!, _document.Venue);
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
            if (_document is null)
                throw new InvalidOperationException("Create or open a Track before adding Exercises.");
            if (string.IsNullOrWhiteSpace(relativePath))
                throw new InvalidOperationException("Select an Exercise JSON first.");
            string file = _exerciseLibrary!.ResolveExistingJson(relativePath);
            ExerciseDefinitionLoadResult load = ExerciseDefinitionStore.LoadFromFileWithDiagnostics(file);
            string id = _document.AddInstance(relativePath, load.Definition);
            _document.MoveInstance(id, position);
            _canvas!.SelectInstance(id);
            MarkChanged("Add instance");
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
            ShowNewTrackDialog();
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

    private void ShowNewTrackDialog()
    {
        _newTrackVenue!.Clear();
        foreach (JsonLibraryEntry entry in _venueLibrary!.EnumerateEntries().Where(item => !item.IsDirectory))
        {
            _newTrackVenue.AddItem(entry.RelativePath.Replace('\\', '/'));
            _newTrackVenue.SetItemMetadata(_newTrackVenue.ItemCount - 1, entry.RelativePath.Replace('\\', '/'));
        }

        if (_newTrackVenue.ItemCount == 0)
        {
            _noVenuesDialog!.PopupCentered();
            SetStatus("New Track blocked: res://venues/ contains no Venue Definition JSON. Use Venue Editor first.", true);
            return;
        }

        _newTrackId!.Text = "new-track";
        _newTrackName!.Text = "New Track";
        _newTrackDialog!.PopupCentered();
    }

    private void CreateNewTrack()
    {
        try
        {
            if (_newTrackVenue!.Selected < 0) throw new InvalidOperationException("Select a Venue Definition first.");
            string venuePath = _newTrackVenue.GetItemMetadata(_newTrackVenue.Selected).AsString();
            string id = _newTrackId!.Text.Trim();
            string name = _newTrackName!.Text.Trim();
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
                throw new InvalidDataException("Track ID and Track Name must be non-empty.");

            // Resolve the complete candidate before replacing the current document.
            ResolvedVenue venue = ResolvedVenueLoader.Load(
                venuePath, _venueLibrary!, ProjectRoot(), ProbeVenueResource);
            ReplaceDocument(TrackProjectDocument.CreateNew(id, name, venuePath, venue), null, dirty: true);
            foreach (string warning in venue.Warnings) GD.PushWarning(warning);
            SetStatus($"New Track Project created for Venue '{venue.Definition.Venue.Name}'.", false);
        }
        catch (Exception exception)
        {
            SetStatus($"New Track failed: {exception.Message}", true);
            GD.PushError($"Unable to create Track Project: {exception}");
        }
    }

    private void ReloadVenue()
    {
        if (_document is null)
        {
            SetStatus("Open or create a Track Project before reloading Venue.", true);
            return;
        }

        try
        {
            bool wasDirty = _dirty;
            ResolvedVenue candidate = ResolvedVenueLoader.Load(
                _document.Project.VenuePath, _venueLibrary!, ProjectRoot(), ProbeVenueResource);
            _document.ReplaceVenue(candidate);
            _canvas!.SetDocument(_document, resetView: false);
            RefreshCompilation();
            SynchronizeAllUi();
            foreach (string warning in candidate.Warnings) GD.PushWarning(warning);
            UpdateDirtyIndicator();
            if (_dirty != wasDirty)
                GD.PushError("Reload Venue unexpectedly changed Track dirty state.");
            SetStatus($"Reloaded Venue '{candidate.Definition.Venue.Name}' without changing Track Project.", false);
        }
        catch (Exception exception)
        {
            // The live dependency is replaced only after a fully successful load.
            SetStatus($"Reload Venue failed; previous Venue preserved: {exception.Message}", true);
            GD.PushError($"Unable to reload Venue: {exception}");
        }
    }

    private static string ProjectRoot() => ProjectSettings.GlobalizePath("res://");

    private static bool ProbeVenueResource(string resourcePath, VenueResourceKind kind)
    {
        return kind switch
        {
            VenueResourceKind.PackedScene => ResourceLoader.Load<PackedScene>(resourcePath) is not null,
            VenueResourceKind.Texture2D => ResourceLoader.Load<Texture2D>(resourcePath) is not null,
            _ => false,
        };
    }

    private void Save()
    {
        if (_document is null) { SetStatus("No Track Project is open.", true); return; }
        if (_currentFilePath is null) ShowSaveAs(); else SaveProject(_currentFilePath);
    }

    private void ShowSaveAs()
    {
        if (_document is null) { SetStatus("No Track Project is open.", true); return; }
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
            TrackProjectStore.SaveToFile(
                _document.Project, target, _exerciseLibrary!, _venueLibrary!);
            _currentFilePath = target;
            _history.MarkSaved();
            UpdateDirtyIndicator();
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
            TrackProjectLoadResult loaded = TrackProjectStore.LoadFromFile(
                file, _exerciseLibrary!, _venueLibrary!, ProjectRoot(), ProbeVenueResource);
            ReplaceDocument(
                new TrackProjectDocument(loaded.Project, loaded.Venue, loaded.Definitions),
                file,
                dirty: false);
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
        SetDocumentUiEnabled(true);
        _currentFilePath = filePath;
        _activeTransactionDescription = null;
        _keyboardTransformKey = null;
        _lockedInstanceIds.Clear();
        _history.Reset(CaptureSnapshot(), saved: !dirty);
        _canvas!.SetDocument(document);
        _canvas.SetLockedInstances(_lockedInstanceIds);
        RefreshCompilation();
        SynchronizeAllUi();
        UpdateDirtyIndicator();
    }

    private void SetDocumentUiEnabled(bool enabled)
    {
        if (_trackId is not null) _trackId.Editable = enabled;
        if (_trackName is not null) _trackName.Editable = enabled;
        if (_canvas is not null) _canvas.MouseFilter = enabled ? MouseFilterEnum.Stop : MouseFilterEnum.Ignore;
    }

    private void OnMetadataEdited()
    {
        if (_updatingUi) return;
        _document.Project.Track.Id = _trackId!.Text.Trim();
        _document.Project.Track.Name = _trackName!.Text.Trim();
        MarkChanged("Edit track metadata");
    }

    private void OnTransformEdited()
    {
        if (_updatingUi || _canvas!.SelectedInstanceId is not string id) return;
        if (IsLocked(id))
        {
            SynchronizeSelectionUi();
            SetStatus($"Instance '{id}' is locked. Unlock it before editing its transform.", true);
            return;
        }
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
            MarkChanged("Edit instance transform");
        }
    }

    private void MirrorSelected(bool vertical)
    {
        if (_canvas!.SelectedInstanceId is not string id)
        {
            SetStatus("Select an instance before mirroring.", true);
            return;
        }
        if (IsLocked(id))
        {
            SetStatus($"Instance '{id}' is locked. Unlock it before mirroring.", true);
            return;
        }

        // Horizontal means left/right (X sign); vertical means top/bottom (Y sign).
        bool changed = vertical
            ? _document.ToggleVerticalMirror(id)
            : _document.ToggleHorizontalMirror(id);
        if (!changed) return;
        MarkChanged("Mirror instance");
        SetStatus(vertical
            ? $"Instance '{id}' mirrored vertically (Y)."
            : $"Instance '{id}' mirrored horizontally (X).", false);
    }

    private void RotateBy(float degrees)
    {
        if (_canvas!.SelectedInstanceId is not string id) return;
        if (IsLocked(id))
        {
            SetStatus($"Instance '{id}' is locked. Unlock it before rotating.", true);
            return;
        }
        _rotation!.Value += degrees;
    }

    private void OnCanvasChanged()
    {
        RefreshAfterMutation();
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

        MarkChanged("Edit transition handle");
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
            MarkChanged("Edit transition control point");
        }
    }

    private void ResetSelectedTransition()
    {
        if (CurrentSelectedTransition() is not CompiledTransition transition ||
            !_document.ResetTransition(transition.FromInstanceId, transition.ToInstanceId))
        {
            return;
        }

        MarkChanged("Reset transition to automatic");
        SetStatus($"Transition '{transition.TransitionId}' reset to automatic.", false);
    }

    private void ShowRemoveOrphanedConfirmation()
    {
        if (_document is null)
        {
            SetStatus("Create or open a Track first.", true);
            return;
        }
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

        MarkChanged("Remove orphaned transition overrides");
        SetStatus($"Removed {removed} orphaned transition override(s).", false);
    }

    private void Reorder(bool up)
    {
        if (_canvas!.SelectedInstanceId is not string id) return;
        bool changed = up ? _document.MoveUp(id) : _document.MoveDown(id);
        if (changed) MarkChanged("Reorder instances");
    }

    private bool DeleteSelected()
    {
        if (_canvas?.SelectedInstanceId is not string id) return false;
        int relatedOverrides = _document.CountRelatedTransitionOverrides(id);
        if (!_document.DeleteInstance(id)) return false;
        _canvas.SelectInstance(null);
        _lockedInstanceIds.Remove(id);
        _canvas.SetLockedInstances(_lockedInstanceIds);
        MarkChanged("Delete instance");
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
        _venueSummary!.Text =
            $"{_document.Venue.Definition.Venue.Name}\n{_document.Project.VenuePath}\n" +
            $"{_document.Venue.Definition.Area.Width:0.###} × {_document.Venue.Definition.Area.Length:0.###} m (read-only)";
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
        bool locked = enabled && IsLocked(instance!.InstanceId);
        _selectionTitle!.Text = enabled ? instance!.InstanceId : "None";
        _exercisePath!.Text = enabled ? $"exercisePath: {instance!.ExercisePath}" : string.Empty;
        foreach (SpinBox spin in new[] { _positionX!, _positionY!, _rotation!, _scaleX!, _scaleY! })
            spin.Editable = enabled && !locked;
        _mirrorVerticalButton!.Disabled = !enabled || locked;
        _mirrorHorizontalButton!.Disabled = !enabled || locked;
        _duplicateButton!.Disabled = !enabled;
        _lockInstanceToggle!.Disabled = !enabled;
        _lockInstanceToggle.SetPressedNoSignal(locked);
        _lockInstanceToggle.Text = locked ? "Unlock Instance" : "Lock Instance";
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

    private string CaptureSnapshot() => TrackProjectStore.SerializeHistorySnapshot(_document.Project);

    /// <summary>
    /// All persisted mutations converge here. During a mouse/key/property gesture
    /// the working DTO is redrawn immediately, but history is committed only once
    /// when that transaction ends.
    /// </summary>
    private void MarkChanged(string description)
    {
        if (_activeTransactionDescription is null)
        {
            _history.Commit(CaptureSnapshot(), description);
        }

        RefreshAfterMutation();
    }

    private void RefreshAfterMutation()
    {
        RefreshCompilation();
        SynchronizeRouteList();
        SynchronizeSelectionUi();
        _canvas!.QueueRedraw();
        UpdateDirtyIndicator();
    }

    private void BeginEditTransaction(string description)
    {
        _activeTransactionDescription ??= description;
        UpdateDirtyIndicator();
    }

    private void EndEditTransaction()
    {
        if (_activeTransactionDescription is null) return;
        string description = _activeTransactionDescription;
        _activeTransactionDescription = null;
        _history.Commit(CaptureSnapshot(), description);
        RefreshAfterMutation();
    }

    private bool Undo()
    {
        EndEditTransaction();
        string? snapshot = _history.Undo();
        if (snapshot is null) return false;
        RestoreHistorySnapshot(snapshot, "Undo");
        return true;
    }

    private bool Redo()
    {
        EndEditTransaction();
        string? snapshot = _history.Redo();
        if (snapshot is null) return false;
        RestoreHistorySnapshot(snapshot, "Redo");
        return true;
    }

    private void RestoreHistorySnapshot(string snapshot, string action)
    {
        string? selectedInstanceId = _canvas?.SelectedInstanceId;
        string? selectedTransitionId = _canvas?.SelectedTransitionId;
        TrackProjectLoadResult restored = TrackProjectStore.RestoreHistorySnapshot(
            snapshot, _exerciseLibrary!, _document.Venue);
        _document = new TrackProjectDocument(restored.Project, restored.Venue, restored.Definitions);
        _canvas!.SetDocument(_document, resetView: false);

        // Locks are editor state. They survive history navigation only while the
        // corresponding stable instance id still exists.
        _lockedInstanceIds.RemoveWhere(id => _document.FindInstance(id) is null);
        _canvas.SetLockedInstances(_lockedInstanceIds);
        RefreshCompilation();
        if (selectedInstanceId is not null && _document.FindInstance(selectedInstanceId) is not null)
            _canvas.SelectInstance(selectedInstanceId);
        else if (selectedTransitionId is not null &&
            _compilation.Transitions.Any(item => item.TransitionId == selectedTransitionId))
            _canvas.SelectTransition(selectedTransitionId);
        SynchronizeAllUi();
        UpdateDirtyIndicator();
        foreach (string warning in restored.Warnings) GD.PushWarning(warning);
        SetStatus($"{action} completed.", false);
    }

    private void UpdateDirtyIndicator()
    {
        bool workingDiffers = _activeTransactionDescription is not null &&
            !string.Equals(CaptureSnapshot(), _history.CurrentSnapshot, StringComparison.Ordinal);
        SetDirty(_history.IsDirty || workingDiffers);
        if (_undoButton is not null) _undoButton.Disabled = !_history.CanUndo;
        if (_redoButton is not null) _redoButton.Disabled = !_history.CanRedo;
    }

    private void WireEditTransaction(Control editor, string description)
    {
        editor.FocusEntered += () => BeginEditTransaction(description);
        editor.FocusExited += EndEditTransaction;
    }

    private bool DuplicateSelected()
    {
        if (_canvas?.SelectedInstanceId is not string sourceId) return false;
        string? duplicateId = _document.DuplicateInstance(sourceId,
            new Point2Dto { X = 1.0f, Y = 1.0f });
        if (duplicateId is null) return false;
        _lockedInstanceIds.Remove(duplicateId);
        _canvas.SetLockedInstances(_lockedInstanceIds);
        _canvas.SelectInstance(duplicateId);
        MarkChanged("Duplicate instance");
        SetStatus($"Duplicated '{sourceId}' as '{duplicateId}' at +1 m X / +1 m Y.", false);
        return true;
    }

    private void OnLockToggled(bool locked)
    {
        if (_updatingUi || _canvas?.SelectedInstanceId is not string id) return;
        if (locked) _lockedInstanceIds.Add(id); else _lockedInstanceIds.Remove(id);
        _canvas.SetLockedInstances(_lockedInstanceIds);
        SynchronizeSelectionUi();
        UpdateDirtyIndicator();
        SetStatus($"Instance '{id}' {(locked ? "locked" : "unlocked")}; this editor state is not saved.", false);
    }

    private bool IsLocked(string instanceId) => _lockedInstanceIds.Contains(instanceId);

    private bool ApplyKeyboardTransform(InputEventKey key)
    {
        if (_canvas?.SelectedInstanceId is not string id ||
            _document.FindInstance(id) is not TrackProjectInstanceDto instance)
            return false;
        if (IsLocked(id))
        {
            SetStatus($"Instance '{id}' is locked. Keyboard transform was ignored.", true);
            return true;
        }

        if (!key.Echo || _keyboardTransformKey != key.Keycode)
        {
            if (_keyboardTransformKey is not null) EndEditTransaction();
            _keyboardTransformKey = key.Keycode;
            BeginEditTransaction(key.Keycode is Key.Q or Key.E
                ? "Rotate instance with keyboard"
                : "Nudge instance with keyboard");
        }

        bool changed;
        if (key.Keycode is Key.Q or Key.E)
        {
            float amount = key.ShiftPressed ? 90.0f : 15.0f;
            if (key.Keycode == Key.Q) amount = -amount;
            changed = _document.SetTransform(id, instance.Position,
                instance.RotationDeg + amount, instance.Scale);
        }
        else
        {
            float step = key.AltPressed ? 0.05f : key.ShiftPressed ? 1.0f : 0.25f;
            Point2Dto delta = key.Keycode switch
            {
                Key.Left => new Point2Dto { X = -step },
                Key.Right => new Point2Dto { X = step },
                Key.Up => new Point2Dto { Y = step },
                Key.Down => new Point2Dto { Y = -step },
                _ => new Point2Dto(),
            };
            changed = _document.MoveInstance(id, new Point2Dto
            {
                X = instance.Position.X + delta.X,
                Y = instance.Position.Y + delta.Y,
            });
        }

        if (changed) RefreshAfterMutation();
        return true;
    }

    private bool IsEditingText()
    {
        Control? focused = GetViewport().GuiGetFocusOwner();
        return focused is LineEdit { Editable: true } or TextEdit { Editable: true };
    }

    private static bool IsTransformKey(Key key) =>
        key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Q or Key.E;

    private void RefreshCompilation()
    {
        if (_document is null)
        {
            _compilation = new TrackCompilationResult();
            _canvas?.SetTransitionPreview([], visible: false);
            if (_validationLabel is not null)
                _validationLabel.Text = "[b]Export validation:[/b] Create or open a Track first.";
            return;
        }
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
        if (_document is null)
        {
            SetStatus("Create or open a Track before export.", true);
            return;
        }
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
            if (_document is null)
                throw new InvalidOperationException("Create or open a Track before export.");
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

    private void ExportAndOpenInViewer()
    {
        try
        {
            if (_document is null)
            {
                SetStatus("Create or open a Track before Viewer preview.", true);
                return;
            }
            RefreshCompilation();
            if (!_compilation.CanExport || _compilation.Snapshot is null)
            {
                SetStatus($"Viewer preview blocked: {_compilation.Errors.Count} validation error(s).", true);
                return;
            }

            // This derived snapshot lives under a dedicated sandboxed folder. It
            // neither changes the production export target nor marks the project saved.
            string previewFolder = _exportLibrary!.ResolveUserPath("_preview");
            Directory.CreateDirectory(previewFolder);
            string previewPath = _exportLibrary.ResolveSaveJson(
                "_preview", Path.GetFileName(ViewerPreviewLauncher.PreviewRelativePath));
            TrackExportStore.SaveToFile(_compilation.Snapshot, previewPath);
            int processId = ViewerPreviewLauncher.Launch(previewPath);
            SetStatus(
                $"Viewer preview started (PID {processId}); {_compilation.Warnings.Count} warning(s).",
                false);
        }
        catch (Exception exception)
        {
            SetStatus($"Viewer preview launch failed: {exception.Message}", true);
            GD.PushError($"Unable to export or launch Viewer preview: {exception}");
        }
        finally
        {
            // Preview/export is not a Track Project edit and must preserve both
            // history position and saved revision.
            UpdateDirtyIndicator();
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
