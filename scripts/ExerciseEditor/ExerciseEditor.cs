using Godot;
using MotoGymkhanaTrainer.Tracks;

namespace MotoGymkhanaTrainer.ExerciseEditor;

/// <summary>Coordinates the Exercise Editor document, transient UI state and file operations.</summary>
public partial class ExerciseEditor : Control
{
    private enum PendingDestructiveAction
    {
        None,
        New,
        Open,
        OpenLibrary,
    }

    private static readonly string[] ConeColors = ["red", "blue", "yellow", "orange", "none"];

    private ExerciseDocument _document = ExerciseDocument.CreateNew();
    private ExerciseLibrary? _library;
    private Tree? _libraryTree;
    private string _selectedLibraryFolder = string.Empty;
    private string? _pendingLibraryOpenPath;
    private ExerciseEditorCanvas? _canvas;
    private LineEdit? _exerciseIdEdit;
    private LineEdit? _exerciseNameEdit;
    private SpinBox? _boundsWidthEdit;
    private SpinBox? _boundsLengthEdit;
    private SpinBox? _entryXEdit;
    private SpinBox? _entryYEdit;
    private SpinBox? _exitXEdit;
    private SpinBox? _exitYEdit;
    private Label? _selectionTitle;
    private VBoxContainer? _coneProperties;
    private VBoxContainer? _trajectoryPointProperties;
    private VBoxContainer? _trajectorySegmentProperties;
    private VBoxContainer? _bezierProperties;
    private VBoxContainer? _markingProperties;
    private VBoxContainer? _markingPointProperties;
    private SpinBox? _coneXEdit;
    private SpinBox? _coneYEdit;
    private OptionButton? _coneColorEdit;
    private Label? _trajectoryPointIndexLabel;
    private Label? _trajectoryPointRoleLabel;
    private SpinBox? _trajectoryPointXEdit;
    private SpinBox? _trajectoryPointYEdit;
    private Label? _trajectorySegmentIdLabel;
    private Label? _trajectorySegmentTypeLabel;
    private SpinBox? _bezierStartXEdit;
    private SpinBox? _bezierStartYEdit;
    private SpinBox? _bezierControl1XEdit;
    private SpinBox? _bezierControl1YEdit;
    private SpinBox? _bezierControl2XEdit;
    private SpinBox? _bezierControl2YEdit;
    private SpinBox? _bezierEndXEdit;
    private SpinBox? _bezierEndYEdit;
    private Button? _convertToCubicButton;
    private Button? _convertToLineButton;
    private Button? _selectToolButton;
    private Button? _addConeToolButton;
    private Button? _trajectoryToolButton;
    private Button? _lineMarkingToolButton;
    private Button? _polylineMarkingToolButton;
    private Button? _startTrajectoryButton;
    private Button? _finishTrajectoryButton;
    private Button? _finishMarkingButton;
    private Label? _markingIdLabel;
    private Label? _markingTypeLabel;
    private ColorPickerButton? _markingColorEdit;
    private SpinBox? _markingWidthEdit;
    private OptionButton? _markingStyleEdit;
    private CheckBox? _markingVisibleEdit;
    private Label? _markingPointIndexLabel;
    private SpinBox? _markingPointXEdit;
    private SpinBox? _markingPointYEdit;
    private Label? _fileLabel;
    private Label? _dirtyLabel;
    private Label? _statusLabel;
    private FileDialog? _openDialog;
    private FileDialog? _saveDialog;
    private ConfirmationDialog? _unsavedDialog;
    private ConfirmationDialog? _newFolderDialog;
    private LineEdit? _newFolderNameEdit;
    private string? _currentFilePath;
    private bool _dirty;
    private bool _synchronizingUi;
    private PendingDestructiveAction _pendingAction;

    /// <inheritdoc />
    public override void _Ready()
    {
        _library = new ExerciseLibrary(ProjectSettings.GlobalizePath("res://exercises"));
        BuildUi();
        ReplaceDocument(ExerciseDocument.CreateNew(), filePath: null, dirty: true);
        SetStatus("New Exercise Definition created. Edit its identity and geometry, then save.", false);
    }

    /// <inheritdoc />
    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Delete } &&
            DeleteSelectedObject())
        {
            GetViewport().SetInputAsHandled();
        }
    }

    private void BuildUi()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var page = new VBoxContainer { Name = "EditorLayout" };
        page.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        page.AddThemeConstantOverride("separation", 6);
        AddChild(page);

        page.AddChild(BuildToolbar());
        page.AddChild(BuildFileStatusBar());

        var body = new HSplitContainer
        {
            Name = "EditorBody",
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SplitOffsets = [325],
        };
        body.AddChild(BuildInspector());

        _canvas = new ExerciseEditorCanvas
        {
            Name = "ExerciseCanvas",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(500.0f, 400.0f),
        };
        _canvas.DocumentChanged += OnCanvasDocumentChanged;
        _canvas.SelectionChanged += SynchronizeSelectionUi;
        _canvas.TrajectoryBuildStateChanged += SynchronizeTrajectoryBuildUi;
        _canvas.MessageRequested += SetStatus;
        body.AddChild(_canvas);
        page.AddChild(body);

        _statusLabel = new Label
        {
            Name = "StatusMessage",
            Text = "Ready.",
            CustomMinimumSize = new Vector2(0.0f, 28.0f),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            ClipText = true,
            VerticalAlignment = VerticalAlignment.Center,
        };
        page.AddChild(_statusLabel);

        BuildDialogs();
    }

    private Control BuildToolbar()
    {
        var toolbar = new HBoxContainer
        {
            Name = "Toolbar",
            CustomMinimumSize = new Vector2(0.0f, 42.0f),
        };
        toolbar.AddThemeConstantOverride("separation", 8);

        toolbar.AddChild(CreateButton("NewButton", "New", RequestNew));
        toolbar.AddChild(CreateButton("OpenButton", "Open", RequestOpen));
        toolbar.AddChild(CreateButton("SaveButton", "Save", Save));
        toolbar.AddChild(new VSeparator());

        var toolGroup = new ButtonGroup();
        _selectToolButton = new Button
        {
            Name = "SelectToolButton",
            Text = "Select",
            ToggleMode = true,
            ButtonPressed = true,
            ButtonGroup = toolGroup,
        };
        _selectToolButton.Pressed += () => SetTool(ExerciseEditorTool.Select);
        toolbar.AddChild(_selectToolButton);

        _addConeToolButton = new Button
        {
            Name = "AddConeToolButton",
            Text = "Add Cone",
            ToggleMode = true,
            ButtonGroup = toolGroup,
        };
        _addConeToolButton.Pressed += () => SetTool(ExerciseEditorTool.AddCone);
        toolbar.AddChild(_addConeToolButton);

        _trajectoryToolButton = new Button
        {
            Name = "EditTrajectoryToolButton",
            Text = "Edit Trajectory",
            ToggleMode = true,
            ButtonGroup = toolGroup,
        };
        _trajectoryToolButton.Pressed += () => SetTool(ExerciseEditorTool.EditTrajectory);

        _lineMarkingToolButton = new Button
        {
            Name = "AddLineMarkingToolButton",
            Text = "Add Line",
            ToggleMode = true,
            ButtonGroup = toolGroup,
        };
        _lineMarkingToolButton.Pressed += () => SetTool(ExerciseEditorTool.AddLineMarking);
        toolbar.AddChild(_lineMarkingToolButton);

        _polylineMarkingToolButton = new Button
        {
            Name = "AddPolylineMarkingToolButton",
            Text = "Add Polyline",
            ToggleMode = true,
            ButtonGroup = toolGroup,
        };
        _polylineMarkingToolButton.Pressed += () => SetTool(ExerciseEditorTool.AddPolylineMarking);
        toolbar.AddChild(_polylineMarkingToolButton);

        _finishMarkingButton = CreateButton("FinishMarkingButton", "Finish Marking", FinishMarkingBuild);
        _finishMarkingButton.Disabled = true;
        toolbar.AddChild(_finishMarkingButton);

        _startTrajectoryButton = CreateButton("StartTrajectoryButton", "Start Trajectory", BeginTrajectoryBuild);
        _finishTrajectoryButton = CreateButton("FinishTrajectoryButton", "Finish Trajectory", FinishTrajectoryBuild);
        _finishTrajectoryButton.Disabled = true;
        toolbar.AddChild(_startTrajectoryButton);
        toolbar.AddChild(_trajectoryToolButton);
        toolbar.AddChild(_finishTrajectoryButton);
        toolbar.AddChild(CreateButton("DeleteButton", "Delete selected", () => DeleteSelectedObject()));
        return toolbar;
    }

    private Control BuildFileStatusBar()
    {
        var status = new HBoxContainer
        {
            Name = "FileStatusBar",
            CustomMinimumSize = new Vector2(0.0f, 28.0f),
        };
        _fileLabel = new Label
        {
            Name = "CurrentFile",
            Text = "Untitled exercise",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        status.AddChild(_fileLabel);

        _dirtyLabel = new Label
        {
            Name = "DirtyIndicator",
            Text = "Modified",
            VerticalAlignment = VerticalAlignment.Center,
        };
        status.AddChild(_dirtyLabel);
        return status;
    }

    private Control BuildInspector()
    {
        var scroll = new ScrollContainer
        {
            Name = "InspectorScroll",
            CustomMinimumSize = new Vector2(310.0f, 0.0f),
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        var inspector = new VBoxContainer
        {
            Name = "Inspector",
            CustomMinimumSize = new Vector2(290.0f, 0.0f),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        inspector.AddThemeConstantOverride("separation", 6);

        inspector.AddChild(BuildLibraryBrowser());

        inspector.AddChild(CreateSectionLabel("Exercise Definition"));
        _exerciseIdEdit = new LineEdit { Name = "ExerciseId", PlaceholderText = "exercise-id" };
        _exerciseIdEdit.TextChanged += _ => OnDocumentFieldsEdited();
        inspector.AddChild(CreateLabeledControl("Id", _exerciseIdEdit));

        _exerciseNameEdit = new LineEdit { Name = "ExerciseName", PlaceholderText = "Exercise name" };
        _exerciseNameEdit.TextChanged += _ => OnDocumentFieldsEdited();
        inspector.AddChild(CreateLabeledControl("Name", _exerciseNameEdit));

        inspector.AddChild(CreateSectionLabel("Bounds (metres, centred at origin)"));
        _boundsWidthEdit = CreateCoordinateSpinBox("BoundsWidth", 0.25, 1000.0);
        _boundsLengthEdit = CreateCoordinateSpinBox("BoundsLength", 0.25, 1000.0);
        _boundsWidthEdit.ValueChanged += _ => OnBoundsEdited();
        _boundsLengthEdit.ValueChanged += _ => OnBoundsEdited();
        inspector.AddChild(CreateLabeledControl("Width", _boundsWidthEdit));
        inspector.AddChild(CreateLabeledControl("Length", _boundsLengthEdit));

        inspector.AddChild(CreateSectionLabel("EntryPoint (local X/Y)"));
        _entryXEdit = CreateCoordinateSpinBox("EntryX");
        _entryYEdit = CreateCoordinateSpinBox("EntryY");
        _entryXEdit.Editable = false;
        _entryYEdit.Editable = false;
        inspector.AddChild(CreateLabeledControl("X", _entryXEdit));
        inspector.AddChild(CreateLabeledControl("Y", _entryYEdit));

        inspector.AddChild(CreateSectionLabel("ExitPoint (local X/Y)"));
        _exitXEdit = CreateCoordinateSpinBox("ExitX");
        _exitYEdit = CreateCoordinateSpinBox("ExitY");
        _exitXEdit.Editable = false;
        _exitYEdit.Editable = false;
        inspector.AddChild(CreateLabeledControl("X", _exitXEdit));
        inspector.AddChild(CreateLabeledControl("Y", _exitYEdit));

        _selectionTitle = CreateSectionLabel("Selection: none");
        _selectionTitle.Name = "SelectionTitle";
        inspector.AddChild(_selectionTitle);

        _coneProperties = new VBoxContainer { Name = "ConeProperties", Visible = false };
        _coneXEdit = CreateCoordinateSpinBox("ConeX");
        _coneYEdit = CreateCoordinateSpinBox("ConeY");
        _coneXEdit.ValueChanged += _ => OnConePositionEdited();
        _coneYEdit.ValueChanged += _ => OnConePositionEdited();
        _coneProperties.AddChild(CreateLabeledControl("X", _coneXEdit));
        _coneProperties.AddChild(CreateLabeledControl("Y", _coneYEdit));

        _coneColorEdit = new OptionButton { Name = "ConeColor" };
        foreach (string color in ConeColors)
        {
            _coneColorEdit.AddItem(color);
        }

        _coneColorEdit.ItemSelected += _ => OnConeColorEdited();
        _coneProperties.AddChild(CreateLabeledControl("Color", _coneColorEdit));
        _coneProperties.AddChild(CreateLabeledControl("Type", new Label { Name = "ConeType", Text = "standard" }));
        _coneProperties.AddChild(CreateButton("DeleteConeButton", "Delete cone", () => DeleteSelectedObject()));
        inspector.AddChild(_coneProperties);

        _trajectoryPointProperties = new VBoxContainer { Name = "TrajectoryPointProperties", Visible = false };
        _trajectoryPointIndexLabel = new Label { Name = "TrajectoryPointIndex", Text = "0" };
        _trajectoryPointRoleLabel = new Label { Name = "TrajectoryPointRole", Text = "Entry" };
        _trajectoryPointProperties.AddChild(CreateLabeledControl("Index", _trajectoryPointIndexLabel));
        _trajectoryPointProperties.AddChild(CreateLabeledControl("Role", _trajectoryPointRoleLabel));

        _trajectoryPointXEdit = CreateCoordinateSpinBox("TrajectoryPointX");
        _trajectoryPointYEdit = CreateCoordinateSpinBox("TrajectoryPointY");
        _trajectoryPointXEdit.ValueChanged += _ => OnTrajectoryPointPositionEdited();
        _trajectoryPointYEdit.ValueChanged += _ => OnTrajectoryPointPositionEdited();
        _trajectoryPointProperties.AddChild(CreateLabeledControl("X", _trajectoryPointXEdit));
        _trajectoryPointProperties.AddChild(CreateLabeledControl("Y", _trajectoryPointYEdit));
        _trajectoryPointProperties.AddChild(CreateButton(
            "InsertTrajectoryPointButton",
            "Insert Point After",
            InsertTrajectoryPointAfter));
        _trajectoryPointProperties.AddChild(CreateButton(
            "DeleteTrajectoryPointButton",
            "Delete trajectory point",
            () => DeleteSelectedObject()));
        inspector.AddChild(_trajectoryPointProperties);

        _trajectorySegmentProperties = new VBoxContainer { Name = "TrajectorySegmentProperties", Visible = false };
        _trajectorySegmentIdLabel = new Label { Name = "TrajectorySegmentId" };
        _trajectorySegmentTypeLabel = new Label { Name = "TrajectorySegmentType" };
        _trajectorySegmentProperties.AddChild(CreateLabeledControl("Segment id", _trajectorySegmentIdLabel));
        _trajectorySegmentProperties.AddChild(CreateLabeledControl("Type", _trajectorySegmentTypeLabel));

        _convertToCubicButton = CreateButton(
            "ConvertToCubicBezierButton",
            "Convert to Cubic Bezier",
            ConvertSelectedToCubic);
        _convertToLineButton = CreateButton(
            "ConvertToLineButton",
            "Convert to Line",
            ConvertSelectedToLine);
        _trajectorySegmentProperties.AddChild(_convertToCubicButton);
        _trajectorySegmentProperties.AddChild(_convertToLineButton);

        _bezierProperties = new VBoxContainer { Name = "CubicBezierProperties" };
        _bezierStartXEdit = CreateCoordinateSpinBox("BezierStartX");
        _bezierStartYEdit = CreateCoordinateSpinBox("BezierStartY");
        _bezierControl1XEdit = CreateCoordinateSpinBox("BezierControl1X");
        _bezierControl1YEdit = CreateCoordinateSpinBox("BezierControl1Y");
        _bezierControl2XEdit = CreateCoordinateSpinBox("BezierControl2X");
        _bezierControl2YEdit = CreateCoordinateSpinBox("BezierControl2Y");
        _bezierEndXEdit = CreateCoordinateSpinBox("BezierEndX");
        _bezierEndYEdit = CreateCoordinateSpinBox("BezierEndY");
        _bezierStartXEdit.Editable = false;
        _bezierStartYEdit.Editable = false;
        _bezierEndXEdit.Editable = false;
        _bezierEndYEdit.Editable = false;
        _bezierControl1XEdit.ValueChanged += _ => OnBezierControlEdited();
        _bezierControl1YEdit.ValueChanged += _ => OnBezierControlEdited();
        _bezierControl2XEdit.ValueChanged += _ => OnBezierControlEdited();
        _bezierControl2YEdit.ValueChanged += _ => OnBezierControlEdited();
        _bezierProperties.AddChild(CreateLabeledControl("Start X", _bezierStartXEdit));
        _bezierProperties.AddChild(CreateLabeledControl("Start Y", _bezierStartYEdit));
        _bezierProperties.AddChild(CreateLabeledControl("Control1 X", _bezierControl1XEdit));
        _bezierProperties.AddChild(CreateLabeledControl("Control1 Y", _bezierControl1YEdit));
        _bezierProperties.AddChild(CreateLabeledControl("Control2 X", _bezierControl2XEdit));
        _bezierProperties.AddChild(CreateLabeledControl("Control2 Y", _bezierControl2YEdit));
        _bezierProperties.AddChild(CreateLabeledControl("End X", _bezierEndXEdit));
        _bezierProperties.AddChild(CreateLabeledControl("End Y", _bezierEndYEdit));
        _trajectorySegmentProperties.AddChild(_bezierProperties);
        inspector.AddChild(_trajectorySegmentProperties);

        _markingProperties = new VBoxContainer { Name = "MarkingProperties", Visible = false };
        _markingIdLabel = new Label { Name = "MarkingId" };
        _markingTypeLabel = new Label { Name = "MarkingType" };
        _markingProperties.AddChild(CreateLabeledControl("Id", _markingIdLabel));
        _markingProperties.AddChild(CreateLabeledControl("Type", _markingTypeLabel));

        _markingColorEdit = new ColorPickerButton { Name = "MarkingColor", EditAlpha = false };
        _markingColorEdit.ColorChanged += _ => OnMarkingPropertiesEdited();
        _markingProperties.AddChild(CreateLabeledControl("Color", _markingColorEdit));

        _markingWidthEdit = CreateCoordinateSpinBox("MarkingWidth", 0.001, 10.0);
        _markingWidthEdit.Step = 0.01;
        _markingWidthEdit.ValueChanged += _ => OnMarkingPropertiesEdited();
        _markingProperties.AddChild(CreateLabeledControl("Width", _markingWidthEdit));

        _markingStyleEdit = new OptionButton { Name = "MarkingStyle" };
        _markingStyleEdit.AddItem("Solid");
        _markingStyleEdit.AddItem("Dashed");
        _markingStyleEdit.AddItem("Dotted");
        _markingStyleEdit.ItemSelected += _ => OnMarkingPropertiesEdited();
        _markingProperties.AddChild(CreateLabeledControl("Style", _markingStyleEdit));

        _markingVisibleEdit = new CheckBox { Name = "MarkingVisible", Text = "Visible in Viewer" };
        _markingVisibleEdit.Toggled += _ => OnMarkingPropertiesEdited();
        _markingProperties.AddChild(_markingVisibleEdit);

        _markingPointProperties = new VBoxContainer { Name = "MarkingPointProperties", Visible = false };
        _markingPointIndexLabel = new Label { Name = "MarkingPointIndex" };
        _markingPointXEdit = CreateCoordinateSpinBox("MarkingPointX");
        _markingPointYEdit = CreateCoordinateSpinBox("MarkingPointY");
        _markingPointXEdit.ValueChanged += _ => OnMarkingPointEdited();
        _markingPointYEdit.ValueChanged += _ => OnMarkingPointEdited();
        _markingPointProperties.AddChild(CreateLabeledControl("Point", _markingPointIndexLabel));
        _markingPointProperties.AddChild(CreateLabeledControl("X", _markingPointXEdit));
        _markingPointProperties.AddChild(CreateLabeledControl("Y", _markingPointYEdit));
        _markingPointProperties.AddChild(CreateButton(
            "InsertMarkingPointButton",
            "Insert Point After",
            InsertMarkingPointAfter));
        _markingPointProperties.AddChild(CreateButton(
            "DeleteMarkingPointButton",
            "Delete internal point",
            () => DeleteSelectedObject()));
        _markingProperties.AddChild(_markingPointProperties);
        _markingProperties.AddChild(CreateButton("DeleteMarkingButton", "Delete marking", DeleteSelectedMarking));
        inspector.AddChild(_markingProperties);

        var help = new Label
        {
            Name = "CanvasHelp",
            Text = "Wheel: zoom\nMiddle mouse: pan\nDrag: 0.25 m snap for cones, anchors and handles\n" +
                "Polyline marking: right-click or Finish Marking\nDelete: remove selected object or permitted anchor",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        inspector.AddChild(help);
        scroll.AddChild(inspector);
        return scroll;
    }

    private Control BuildLibraryBrowser()
    {
        var panel = new VBoxContainer { Name = "ExerciseLibrary" };
        panel.AddChild(CreateSectionLabel("Exercise Library — res://exercises/"));

        var actions = new HBoxContainer();
        actions.AddChild(CreateButton("RefreshLibraryButton", "Refresh", RefreshLibraryTree));
        actions.AddChild(CreateButton("NewLibraryFolderButton", "New Folder", ShowNewFolderDialog));
        actions.AddChild(CreateButton("SaveToLibraryFolderButton", "Save Here", SaveToSelectedLibraryFolder));
        actions.AddChild(CreateButton("SaveAsLibraryButton", "Save As", ShowSaveAsDialog));
        panel.AddChild(actions);

        _libraryTree = new Tree
        {
            Name = "ExerciseLibraryTree",
            Columns = 1,
            HideRoot = false,
            CustomMinimumSize = new Vector2(0.0f, 190.0f),
        };
        _libraryTree.ItemSelected += OnLibraryItemSelected;
        _libraryTree.ItemActivated += OnLibraryItemActivated;
        panel.AddChild(_libraryTree);
        RefreshLibraryTree();
        return panel;
    }

    private void RefreshLibraryTree()
    {
        if (_libraryTree is null || _library is null)
        {
            return;
        }

        _libraryTree.Clear();
        TreeItem root = _libraryTree.CreateItem();
        root.SetText(0, "exercises");
        root.SetMetadata(0, "D|");
        root.Collapsed = false;
        var folders = new Dictionary<string, TreeItem>(StringComparer.OrdinalIgnoreCase)
        {
            [string.Empty] = root,
        };

        foreach (ExerciseLibraryEntry entry in _library.EnumerateEntries())
        {
            string parentPath = Path.GetDirectoryName(entry.RelativePath) ?? string.Empty;
            if (!folders.TryGetValue(parentPath, out TreeItem? parent))
            {
                parent = root;
            }

            TreeItem item = _libraryTree.CreateItem(parent);
            item.SetText(0, entry.IsDirectory ? $"📁 {entry.DisplayName}" : entry.DisplayName);
            item.SetMetadata(0, $"{(entry.IsDirectory ? 'D' : 'F')}|{entry.RelativePath}");
            if (entry.IsDirectory)
            {
                folders[entry.RelativePath] = item;
            }
        }
    }

    private void OnLibraryItemSelected()
    {
        if (!TryReadSelectedLibraryItem(out bool directory, out string relativePath))
        {
            return;
        }

        _selectedLibraryFolder = directory
            ? relativePath
            : Path.GetDirectoryName(relativePath) ?? string.Empty;
        SetStatus(
            directory
                ? $"Selected library folder: res://exercises/{relativePath.Replace('\\', '/')}"
                : $"Selected Exercise JSON: {relativePath}",
            false);
    }

    private void OnLibraryItemActivated()
    {
        if (!TryReadSelectedLibraryItem(out bool directory, out string relativePath) || directory)
        {
            return;
        }

        _pendingLibraryOpenPath = relativePath;
        RequestDestructiveAction(PendingDestructiveAction.OpenLibrary);
    }

    private bool TryReadSelectedLibraryItem(out bool directory, out string relativePath)
    {
        directory = false;
        relativePath = string.Empty;
        TreeItem? selected = _libraryTree?.GetSelected();
        if (selected is null)
        {
            return false;
        }

        string metadata = selected.GetMetadata(0).AsString();
        if (metadata.Length < 2 || metadata[1] != '|')
        {
            return false;
        }

        directory = metadata[0] == 'D';
        relativePath = metadata[2..];
        return true;
    }

    private void BuildDialogs()
    {
        string defaultDirectory = _library!.RootPath;
        _openDialog = CreateJsonDialog("OpenExerciseDialog", "Open Exercise Definition", FileDialog.FileModeEnum.OpenFile);
        _openDialog.CurrentDir = defaultDirectory;
        _openDialog.FileSelected += OpenSelectedFile;
        _openDialog.Canceled += () => SetStatus("Open canceled. Current document was not changed.", false);
        AddChild(_openDialog);

        _saveDialog = CreateJsonDialog("SaveExerciseDialog", "Save Exercise Definition", FileDialog.FileModeEnum.SaveFile);
        _saveDialog.CurrentDir = defaultDirectory;
        _saveDialog.CurrentFile = ExerciseLibrary.SuggestFileName(_document.Definition.Exercise.Id);
        _saveDialog.FileSelected += SaveSelectedFile;
        _saveDialog.Canceled += () => SetStatus("Save canceled. Document remains modified.", false);
        AddChild(_saveDialog);

        _unsavedDialog = new ConfirmationDialog
        {
            Name = "UnsavedChangesDialog",
            Title = "Unsaved changes",
            DialogText = "The current Exercise Definition has unsaved changes. Discard them?",
            OkButtonText = "Discard",
            Size = new Vector2I(520, 170),
        };
        _unsavedDialog.Confirmed += ContinuePendingAction;
        _unsavedDialog.Canceled += () =>
        {
            _pendingAction = PendingDestructiveAction.None;
            _pendingLibraryOpenPath = null;
            SetStatus("Operation canceled. Unsaved document was preserved.", false);
        };
        AddChild(_unsavedDialog);

        _newFolderNameEdit = new LineEdit
        {
            Name = "NewFolderName",
            PlaceholderText = "folder-name",
            CustomMinimumSize = new Vector2(420.0f, 0.0f),
        };
        _newFolderDialog = new ConfirmationDialog
        {
            Name = "NewLibraryFolderDialog",
            Title = "Create Exercise Library Folder",
            DialogText = "Create a child folder in the selected library folder:",
            Size = new Vector2I(520, 190),
        };
        _newFolderDialog.AddChild(_newFolderNameEdit);
        _newFolderDialog.Confirmed += CreateLibraryFolder;
        AddChild(_newFolderDialog);
    }

    private void ShowNewFolderDialog()
    {
        _newFolderNameEdit!.Text = string.Empty;
        _newFolderDialog!.PopupCentered();
        _newFolderNameEdit.GrabFocus();
    }

    private void CreateLibraryFolder()
    {
        try
        {
            string relative = _library!.CreateFolder(_selectedLibraryFolder, _newFolderNameEdit!.Text);
            RefreshLibraryTree();
            _selectedLibraryFolder = relative;
            SetStatus($"Created library folder 'res://exercises/{relative.Replace('\\', '/')}'.", false);
        }
        catch (Exception exception)
        {
            SetStatus($"Folder creation failed: {exception.Message}", true);
            GD.PushError($"Exercise library folder creation failed: {exception}");
        }
    }

    private void RequestNew()
    {
        RequestDestructiveAction(PendingDestructiveAction.New);
    }

    private void RequestOpen()
    {
        RequestDestructiveAction(PendingDestructiveAction.Open);
    }

    private void RequestDestructiveAction(PendingDestructiveAction action)
    {
        _pendingAction = action;
        if (_dirty)
        {
            _unsavedDialog!.PopupCentered();
            return;
        }

        ContinuePendingAction();
    }

    private void ContinuePendingAction()
    {
        PendingDestructiveAction action = _pendingAction;
        _pendingAction = PendingDestructiveAction.None;
        if (action == PendingDestructiveAction.New)
        {
            ReplaceDocument(ExerciseDocument.CreateNew(), filePath: null, dirty: true);
            SetStatus("New Exercise Definition created.", false);
        }
        else if (action == PendingDestructiveAction.Open)
        {
            _openDialog!.PopupCenteredRatio(0.82f);
        }
        else if (action == PendingDestructiveAction.OpenLibrary && _pendingLibraryOpenPath is not null)
        {
            string relativePath = _pendingLibraryOpenPath;
            _pendingLibraryOpenPath = null;
            OpenSelectedFile(_library!.ResolveExistingJson(relativePath));
        }
    }

    private void Save()
    {
        if (string.IsNullOrWhiteSpace(_currentFilePath))
        {
            ShowSaveAsDialog();
            return;
        }

        SaveSelectedFile(_currentFilePath);
    }

    private void SaveToSelectedLibraryFolder()
    {
        // A new/copy save must expose the suggested filename instead of silently
        // committing it. This keeps exercise.id independent from the library path.
        ShowSaveAsDialog();
    }

    private void ShowSaveAsDialog()
    {
        try
        {
            _saveDialog!.CurrentDir = _library!.ResolveFolder(_selectedLibraryFolder);
            _saveDialog.CurrentFile = ExerciseLibrary.SuggestFileName(_document.Definition.Exercise.Id);
            _saveDialog.PopupCenteredRatio(0.82f);
        }
        catch (Exception exception)
        {
            SetStatus($"Save As failed: {exception.Message}", true);
        }
    }

    private void SaveSelectedFile(string path)
    {
        try
        {
            if (_canvas?.TryFinishTrajectoryBuild() == false || _canvas?.TryFinishMarkingBuild() == false)
            {
                SetStatus("Save blocked: finish the active trajectory or marking with at least two points.", true);
                return;
            }

            string requestedPath = ToFilesystemPath(path);
            string directory = Path.GetDirectoryName(requestedPath) ?? _library!.RootPath;
            string filesystemPath = _library!.ResolveSaveJson(
                _library.ToRelative(directory),
                Path.GetFileName(requestedPath));
            _document.SynchronizeEndpointsFromTrajectory();
            ExerciseDefinitionStore.SaveToFile(_document.Definition, filesystemPath);
            _currentFilePath = filesystemPath;
            SetDirty(false);
            SetStatus($"Saved Exercise Definition to '{filesystemPath}'.", false);
            GD.Print($"Saved Exercise Definition '{_document.Definition.Exercise.Id}' to '{filesystemPath}'.");
            RefreshLibraryTree();
        }
        catch (Exception exception)
        {
            SetStatus($"Save failed: {exception.Message}", true);
            GD.PushError($"Unable to save Exercise Definition '{path}': {exception}");
        }
    }

    private void OpenSelectedFile(string path)
    {
        try
        {
            string filesystemPath = _library!.ResolveExistingJson(ToFilesystemPath(path));
            // Parse and validate a complete candidate first. The live document is
            // replaced only after this call succeeds, so a damaged file is harmless.
            ExerciseDefinitionLoadResult loadResult =
                ExerciseDefinitionStore.LoadFromFileWithDiagnostics(filesystemPath);
            bool normalizedEndpoints = loadResult.Warnings.Count > 0;
            ReplaceDocument(new ExerciseDocument(loadResult.Definition), filesystemPath, dirty: normalizedEndpoints);

            if (normalizedEndpoints)
            {
                foreach (string warning in loadResult.Warnings)
                {
                    GD.PushWarning(warning);
                }

                SetStatus(
                    $"Loaded with warning: {string.Join(" ", loadResult.Warnings)} Save to persist trajectory-derived endpoints.",
                    true);
            }
            else
            {
                SetStatus($"Loaded Exercise Definition from '{filesystemPath}'.", false);
            }

            GD.Print(
                $"Loaded Exercise Definition '{loadResult.Definition.Exercise.Id}' from '{filesystemPath}' " +
                $"with {_document.TrajectoryPointCount} trajectory points.");
        }
        catch (Exception exception)
        {
            SetStatus($"Open failed: {exception.Message}", true);
            GD.PushError($"Unable to open Exercise Definition '{path}': {exception}");
        }
    }

    private void ReplaceDocument(ExerciseDocument document, string? filePath, bool dirty)
    {
        _document = document;
        _currentFilePath = filePath;
        _canvas?.SetDocument(document);
        SynchronizeDocumentUi();
        SetDirty(dirty);
    }

    private void OnDocumentFieldsEdited()
    {
        if (_synchronizingUi)
        {
            return;
        }

        _document.Definition.Exercise.Id = _exerciseIdEdit!.Text.Trim();
        _document.Definition.Exercise.Name = _exerciseNameEdit!.Text.Trim();
        MarkDocumentChanged();
    }

    private void OnBoundsEdited()
    {
        if (_synchronizingUi)
        {
            return;
        }

        _document.Definition.Bounds.Width = (float)_boundsWidthEdit!.Value;
        _document.Definition.Bounds.Length = (float)_boundsLengthEdit!.Value;
        MarkDocumentChanged();

        int outsideCount = _document.Definition.Cones.Count(cone =>
            MathF.Abs(cone.Position.X) > _document.Definition.Bounds.Width / 2.0f ||
            MathF.Abs(cone.Position.Y) > _document.Definition.Bounds.Length / 2.0f);
        if (outsideCount > 0)
        {
            SetStatus($"Bounds changed. {outsideCount} cone(s) remain outside; no objects were scaled or deleted.", false);
        }
    }

    private void OnConePositionEdited()
    {
        if (_synchronizingUi || _canvas?.SelectionKind != ExerciseSelectionKind.Cone)
        {
            return;
        }

        _document.MoveCone(
            _canvas.SelectedConeId,
            new Point2Dto { X = (float)_coneXEdit!.Value, Y = (float)_coneYEdit!.Value });
        MarkDocumentChanged();
    }

    private void OnConeColorEdited()
    {
        if (_synchronizingUi || _canvas?.SelectionKind != ExerciseSelectionKind.Cone)
        {
            return;
        }

        _document.SetConeColor(_canvas.SelectedConeId, _coneColorEdit!.GetItemText(_coneColorEdit.Selected));
        MarkDocumentChanged();
    }

    private void OnTrajectoryPointPositionEdited()
    {
        if (_synchronizingUi || _canvas?.SelectionKind != ExerciseSelectionKind.TrajectoryPoint)
        {
            return;
        }

        _document.MoveTrajectoryPoint(
            _canvas.SelectedTrajectoryPointIndex,
            new Point2Dto
            {
                X = (float)_trajectoryPointXEdit!.Value,
                Y = (float)_trajectoryPointYEdit!.Value,
            });
        MarkDocumentChanged();
        SynchronizeDocumentUi();
    }

    private void OnBezierControlEdited()
    {
        if (_synchronizingUi || _canvas is null ||
            _canvas.SelectionKind is not (ExerciseSelectionKind.TrajectorySegment or ExerciseSelectionKind.BezierControl))
        {
            return;
        }

        int segmentIndex = _canvas.SelectedTrajectorySegmentIndex;
        TrajectorySegmentDto segment = _document.GetTrajectorySegment(segmentIndex);
        if (segment.Type != "cubicBezier")
        {
            return;
        }

        _document.MoveBezierControl(
            segmentIndex,
            BezierControlKind.Control1,
            new Point2Dto
            {
                X = (float)_bezierControl1XEdit!.Value,
                Y = (float)_bezierControl1YEdit!.Value,
            });
        _document.MoveBezierControl(
            segmentIndex,
            BezierControlKind.Control2,
            new Point2Dto
            {
                X = (float)_bezierControl2XEdit!.Value,
                Y = (float)_bezierControl2YEdit!.Value,
            });
        MarkDocumentChanged();
    }

    private void OnMarkingPropertiesEdited()
    {
        if (_synchronizingUi || _canvas is null ||
            _canvas.SelectionKind is not (ExerciseSelectionKind.Marking or ExerciseSelectionKind.MarkingPoint))
        {
            return;
        }

        string style = _markingStyleEdit!.Selected switch
        {
            1 => "dashed",
            2 => "dotted",
            _ => "solid",
        };
        string color = $"#{_markingColorEdit!.Color.ToHtml(includeAlpha: false).ToUpperInvariant()}";
        if (!_document.SetMarkingProperties(
                _canvas.SelectedMarkingId,
                color,
                (float)_markingWidthEdit!.Value,
                style,
                _markingVisibleEdit!.ButtonPressed))
        {
            SetStatus("Invalid marking properties. Width must be greater than zero.", true);
            return;
        }

        MarkDocumentChanged();
    }

    private void OnMarkingPointEdited()
    {
        if (_synchronizingUi || _canvas?.SelectionKind != ExerciseSelectionKind.MarkingPoint)
        {
            return;
        }

        _document.MoveMarkingPoint(
            _canvas.SelectedMarkingId,
            _canvas.SelectedMarkingPointIndex,
            new Point2Dto
            {
                X = (float)_markingPointXEdit!.Value,
                Y = (float)_markingPointYEdit!.Value,
            });
        MarkDocumentChanged();
    }

    private void OnCanvasDocumentChanged()
    {
        SetDirty(true);
        SynchronizeDocumentUi();
        SynchronizeTrajectoryBuildUi();
        _canvas?.QueueRedraw();
    }

    private bool DeleteSelectedObject()
    {
        SelectionDeleteResult result = _canvas?.DeleteSelected() ?? SelectionDeleteResult.NothingSelected;
        if (result == SelectionDeleteResult.NothingSelected)
        {
            return false;
        }

        if (result == SelectionDeleteResult.TrajectoryMinimumBlocked)
        {
            SetStatus("Cannot delete trajectory point: a polyline must contain at least two points.", true);
            return true;
        }

        if (result == SelectionDeleteResult.CubicAdjacentBlocked)
        {
            SetStatus(
                "Cannot delete this anchor while a cubicBezier is adjacent. Convert the curve to a line first.",
                true);
            return true;
        }

        if (result == SelectionDeleteResult.MarkingPointDeleteBlocked)
        {
            SetStatus(
                "Only an internal point of a polyline with more than two points can be deleted.",
                true);
            return true;
        }

        SetDirty(true);
        SynchronizeDocumentUi();
        string message = result switch
        {
            SelectionDeleteResult.DeletedCone => "Selected cone deleted.",
            SelectionDeleteResult.DeletedMarking => "Selected marking deleted.",
            SelectionDeleteResult.DeletedMarkingPoint => "Selected marking point deleted.",
            _ => "Selected trajectory point deleted.",
        };
        SetStatus(message, false);
        return true;
    }

    private void DeleteSelectedMarking()
    {
        if (_canvas is null ||
            _canvas.SelectionKind is not (ExerciseSelectionKind.Marking or ExerciseSelectionKind.MarkingPoint))
        {
            return;
        }

        _canvas.SelectMarking(_canvas.SelectedMarkingId);
        DeleteSelectedObject();
    }

    private void InsertTrajectoryPointAfter()
    {
        if (_canvas?.InsertPointAfterSelected() != true)
        {
            return;
        }

        SetDirty(true);
        SynchronizeDocumentUi();
        SetStatus("Trajectory point inserted between adjacent anchors.", false);
    }

    private void InsertMarkingPointAfter()
    {
        if (_canvas?.InsertMarkingPointAfterSelected() != true)
        {
            return;
        }

        SetDirty(true);
        SynchronizeDocumentUi();
        SetStatus("Marking point inserted at the adjacent midpoint.", false);
    }

    private void ConvertSelectedToCubic()
    {
        if (_canvas?.ConvertSelectedToCubic() != true)
        {
            SetStatus("Select a straight trajectory section before converting it.", true);
            return;
        }

        SetDirty(true);
        SynchronizeDocumentUi();
        SetStatus("Selected section converted to cubicBezier with initially straight controls.", false);
    }

    private void ConvertSelectedToLine()
    {
        if (_canvas?.ConvertSelectedToLine() != true)
        {
            SetStatus("Select a cubicBezier section before converting it.", true);
            return;
        }

        SetDirty(true);
        SynchronizeDocumentUi();
        SetStatus("Selected cubicBezier converted to a line; adjacent polylines were normalized.", false);
    }

    private void BeginTrajectoryBuild()
    {
        _trajectoryToolButton?.SetPressedNoSignal(true);
        _canvas?.BeginTrajectoryBuild();
        SynchronizeTrajectoryBuildUi();
    }

    private void FinishTrajectoryBuild()
    {
        _canvas?.TryFinishTrajectoryBuild();
        SynchronizeTrajectoryBuildUi();
    }

    private void FinishMarkingBuild()
    {
        _canvas?.TryFinishMarkingBuild();
        SynchronizeTrajectoryBuildUi();
    }

    private void MarkDocumentChanged()
    {
        SetDirty(true);
        _canvas?.QueueRedraw();
    }

    private void SynchronizeDocumentUi()
    {
        _synchronizingUi = true;
        ExerciseDefinitionDto definition = _document.Definition;
        _exerciseIdEdit!.Text = definition.Exercise.Id;
        _exerciseNameEdit!.Text = definition.Exercise.Name;
        _boundsWidthEdit!.Value = definition.Bounds.Width;
        _boundsLengthEdit!.Value = definition.Bounds.Length;
        _entryXEdit!.Value = definition.EntryPoint.X;
        _entryYEdit!.Value = definition.EntryPoint.Y;
        _exitXEdit!.Value = definition.ExitPoint.X;
        _exitYEdit!.Value = definition.ExitPoint.Y;
        _synchronizingUi = false;
        SynchronizeSelectionUi();
    }

    private void SynchronizeSelectionUi()
    {
        if (_canvas is null || _selectionTitle is null || _coneProperties is null ||
            _trajectoryPointProperties is null || _trajectorySegmentProperties is null ||
            _markingProperties is null)
        {
            return;
        }

        _synchronizingUi = true;
        switch (_canvas.SelectionKind)
        {
            case ExerciseSelectionKind.Cone:
                ConeDto? cone = _document.FindCone(_canvas.SelectedConeId);
                _selectionTitle.Text = cone is null ? "Selection: none" : $"Selection: {cone.Id}";
                _coneProperties.Visible = cone is not null;
                _trajectoryPointProperties.Visible = false;
                _trajectorySegmentProperties.Visible = false;
                _markingProperties.Visible = false;
                if (cone is not null)
                {
                    _coneXEdit!.Value = cone.Position.X;
                    _coneYEdit!.Value = cone.Position.Y;
                    int colorIndex = Array.IndexOf(ConeColors, cone.Color);
                    _coneColorEdit!.Selected = Math.Max(0, colorIndex);
                }

                break;
            case ExerciseSelectionKind.TrajectoryPoint:
                int pointIndex = _canvas.SelectedTrajectoryPointIndex;
                Point2Dto point = _document.GetTrajectoryPoint(pointIndex);
                string role = pointIndex == 0
                    ? "Entry"
                    : pointIndex == _document.TrajectoryPointCount - 1
                        ? "Exit"
                        : "Intermediate";
                _selectionTitle.Text = $"Selection: trajectory point {pointIndex}";
                _coneProperties.Visible = false;
                _trajectoryPointProperties.Visible = true;
                _trajectorySegmentProperties.Visible = false;
                _markingProperties.Visible = false;
                _trajectoryPointIndexLabel!.Text = pointIndex.ToString();
                _trajectoryPointRoleLabel!.Text = role;
                _trajectoryPointXEdit!.Value = point.X;
                _trajectoryPointYEdit!.Value = point.Y;
                break;
            case ExerciseSelectionKind.TrajectorySegment:
            case ExerciseSelectionKind.BezierControl:
                SynchronizeTrajectorySegmentUi();
                break;
            case ExerciseSelectionKind.Marking:
            case ExerciseSelectionKind.MarkingPoint:
                SynchronizeMarkingUi();
                break;
            default:
                _selectionTitle.Text = "Selection: none";
                _coneProperties.Visible = false;
                _trajectoryPointProperties.Visible = false;
                _trajectorySegmentProperties.Visible = false;
                _markingProperties.Visible = false;
                break;
        }

        _synchronizingUi = false;
    }

    private void SynchronizeTrajectorySegmentUi()
    {
        int segmentIndex = _canvas!.SelectedTrajectorySegmentIndex;
        TrajectorySegmentDto segment = _document.GetTrajectorySegment(segmentIndex);
        string selectionSuffix = _canvas.SelectionKind == ExerciseSelectionKind.BezierControl
            ? $" / {_canvas.SelectedBezierControl}"
            : $" / section {_canvas.SelectedTrajectorySectionIndex}";
        _selectionTitle!.Text = $"Selection: {segment.Id}{selectionSuffix}";
        _coneProperties!.Visible = false;
        _trajectoryPointProperties!.Visible = false;
        _trajectorySegmentProperties!.Visible = true;
        _markingProperties!.Visible = false;
        _trajectorySegmentIdLabel!.Text = segment.Id;
        _trajectorySegmentTypeLabel!.Text = segment.Type;
        bool cubic = segment.Type == "cubicBezier";
        _convertToCubicButton!.Visible = !cubic;
        _convertToLineButton!.Visible = cubic;
        _bezierProperties!.Visible = cubic;
        if (!cubic)
        {
            return;
        }

        _bezierStartXEdit!.Value = segment.Start!.X;
        _bezierStartYEdit!.Value = segment.Start.Y;
        _bezierControl1XEdit!.Value = segment.Control1!.X;
        _bezierControl1YEdit!.Value = segment.Control1.Y;
        _bezierControl2XEdit!.Value = segment.Control2!.X;
        _bezierControl2YEdit!.Value = segment.Control2.Y;
        _bezierEndXEdit!.Value = segment.End!.X;
        _bezierEndYEdit!.Value = segment.End.Y;
    }

    private void SynchronizeMarkingUi()
    {
        MarkingDto? marking = _document.FindMarking(_canvas!.SelectedMarkingId);
        if (marking is null)
        {
            return;
        }

        _selectionTitle!.Text = _canvas.SelectionKind == ExerciseSelectionKind.MarkingPoint
            ? $"Selection: {marking.Id} / point {_canvas.SelectedMarkingPointIndex}"
            : $"Selection: {marking.Id}";
        _coneProperties!.Visible = false;
        _trajectoryPointProperties!.Visible = false;
        _trajectorySegmentProperties!.Visible = false;
        _markingProperties!.Visible = true;
        _markingIdLabel!.Text = marking.Id;
        _markingTypeLabel!.Text = marking.Type;
        _markingColorEdit!.Color = ParseCanonicalColor(marking.Color);
        _markingWidthEdit!.Value = marking.WidthMeters;
        _markingStyleEdit!.Selected = marking.Style switch
        {
            "dashed" => 1,
            "dotted" => 2,
            _ => 0,
        };
        _markingVisibleEdit!.ButtonPressed = marking.VisibleInViewer;

        bool pointSelected = _canvas.SelectionKind == ExerciseSelectionKind.MarkingPoint;
        _markingPointProperties!.Visible = pointSelected;
        if (pointSelected)
        {
            int pointIndex = _canvas.SelectedMarkingPointIndex;
            Point2Dto point = marking.Points[pointIndex];
            _markingPointIndexLabel!.Text = pointIndex.ToString();
            _markingPointXEdit!.Value = point.X;
            _markingPointYEdit!.Value = point.Y;
        }
    }

    private void SetTool(ExerciseEditorTool tool)
    {
        if (_canvas?.IsBuildingTrajectory == true && tool != ExerciseEditorTool.EditTrajectory &&
            !_canvas.TryFinishTrajectoryBuild())
        {
            // Keep the visible toggle aligned with the still-active construction
            // mode when fewer than two clicks make finishing impossible.
            _trajectoryToolButton?.SetPressedNoSignal(true);
            return;
        }

        if (_canvas?.IsBuildingMarking == true && tool != _canvas.Tool &&
            !_canvas.TryFinishMarkingBuild())
        {
            if (_canvas.Tool == ExerciseEditorTool.AddLineMarking)
            {
                _lineMarkingToolButton?.SetPressedNoSignal(true);
            }
            else
            {
                _polylineMarkingToolButton?.SetPressedNoSignal(true);
            }

            return;
        }

        _canvas?.SetTool(tool);

        string message = tool switch
        {
            ExerciseEditorTool.Select => "Select tool active.",
            ExerciseEditorTool.AddCone => "Add Cone tool active.",
            ExerciseEditorTool.EditTrajectory =>
                "Edit Trajectory tool active. Select/drag anchors or press Start Trajectory.",
            ExerciseEditorTool.AddLineMarking => "Add Line tool active. Click its first and second point.",
            _ => "Add Polyline tool active. Click points, then right-click or press Finish Marking.",
        };
        SetStatus(message, false);
        SynchronizeTrajectoryBuildUi();
    }

    private void SynchronizeTrajectoryBuildUi()
    {
        bool building = _canvas?.IsBuildingTrajectory == true;
        if (_startTrajectoryButton is not null)
        {
            _startTrajectoryButton.Disabled = building;
        }

        if (_finishTrajectoryButton is not null)
        {
            _finishTrajectoryButton.Disabled = !building;
        }

        if (_finishMarkingButton is not null)
        {
            _finishMarkingButton.Disabled = _canvas?.IsBuildingMarking != true;
        }
    }

    private void SetDirty(bool dirty)
    {
        _dirty = dirty;
        if (_dirtyLabel is not null)
        {
            _dirtyLabel.Text = dirty ? "Modified" : "Saved";
            _dirtyLabel.Modulate = dirty ? new Color(1.0f, 0.78f, 0.2f) : new Color(0.55f, 0.95f, 0.62f);
        }

        if (_fileLabel is not null)
        {
            _fileLabel.Text = _currentFilePath ?? $"Untitled — {_document.Definition.Exercise.Name}";
        }
    }

    private void SetStatus(string message, bool error)
    {
        if (_statusLabel is null)
        {
            return;
        }

        _statusLabel.Text = message;
        _statusLabel.Modulate = error ? new Color(1.0f, 0.48f, 0.42f) : new Color(0.82f, 0.88f, 0.94f);
    }

    private static Button CreateButton(string name, string text, Action action)
    {
        var button = new Button { Name = name, Text = text };
        button.Pressed += action;
        return button;
    }

    private static Label CreateSectionLabel(string text)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", 17);
        return label;
    }

    private static Control CreateLabeledControl(string labelText, Control editor)
    {
        var row = new HBoxContainer();
        row.AddChild(new Label { Text = labelText, CustomMinimumSize = new Vector2(90.0f, 0.0f) });
        editor.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(editor);
        return row;
    }

    private static SpinBox CreateCoordinateSpinBox(
        string name,
        double minimum = -10000.0,
        double maximum = 10000.0)
    {
        return new SpinBox
        {
            Name = name,
            MinValue = minimum,
            MaxValue = maximum,
            Step = 0.25,
            AllowGreater = false,
            AllowLesser = false,
            Suffix = " m",
        };
    }

    private static FileDialog CreateJsonDialog(string name, string title, FileDialog.FileModeEnum mode)
    {
        return new FileDialog
        {
            Name = name,
            Title = title,
            Access = FileDialog.AccessEnum.Filesystem,
            FileMode = mode,
            UseNativeDialog = false,
            Size = new Vector2I(900, 600),
            Filters = ["*.json ; Exercise Definition JSON"],
        };
    }

    private static string ToFilesystemPath(string path)
    {
        return path.StartsWith("res://", StringComparison.Ordinal)
            ? ProjectSettings.GlobalizePath(path)
            : path;
    }

    private static Color ParseCanonicalColor(string value)
    {
        if (!MarkingGeometry.TryNormalizeColor(value, allowLegacyNames: true, out string canonical))
        {
            return Colors.White;
        }

        return new Color(
            Convert.ToByte(canonical.Substring(1, 2), 16) / 255.0f,
            Convert.ToByte(canonical.Substring(3, 2), 16) / 255.0f,
            Convert.ToByte(canonical.Substring(5, 2), 16) / 255.0f);
    }
}
