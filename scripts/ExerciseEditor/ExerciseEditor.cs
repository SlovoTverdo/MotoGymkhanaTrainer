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
    private readonly EditorSnapshotHistory _history = new();
    private readonly Dictionary<string, MarkingSelection> _historySelections = new(StringComparer.Ordinal);
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
    private Button? _cubicMarkingToolButton;
    private Button? _splitMarkingToolButton;
    private Button? _undoButton;
    private Button? _redoButton;
    private Button? _startTrajectoryButton;
    private Button? _finishTrajectoryButton;
    private Button? _finishMarkingButton;
    private Label? _markingIdLabel;
    private Label? _markingTypeLabel;
    private Label? _markingSegmentCountLabel;
    private Label? _markingLengthLabel;
    private VBoxContainer? _markingSegmentProperties;
    private Label? _markingSegmentIndexLabel;
    private Label? _markingSegmentTypeLabel;
    private Label? _markingHandleKindLabel;
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
        if (@event is not InputEventKey { Pressed: true, Echo: false } key) return;
        bool handled = false;
        if (key.CtrlPressed && key.Keycode == Key.Z) handled = Undo();
        else if (key.CtrlPressed && key.Keycode == Key.Y) handled = Redo();
        else if (key.Keycode == Key.Delete) handled = DeleteSelectedObject();
        else if (key.Keycode == Key.Escape) handled = _canvas?.HandleEscape() == true;
        else if (key.Keycode is Key.Enter or Key.KpEnter) handled = _canvas?.HandleEnter() == true;
        else if (!key.CtrlPressed && key.Keycode == Key.V) { SetTool(ExerciseEditorTool.Select); handled = true; }
        else if (!key.CtrlPressed && key.Keycode == Key.L) { SetTool(ExerciseEditorTool.AppendLine); handled = true; }
        else if (!key.CtrlPressed && key.Keycode == Key.B) { SetTool(ExerciseEditorTool.AppendCubicBezier); handled = true; }
        if (handled)
        {
            GetViewport().SetInputAsHandled();
            SynchronizeToolButtons();
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
        _undoButton = CreateButton("UndoButton", "Undo", () => Undo());
        _redoButton = CreateButton("RedoButton", "Redo", () => Redo());
        toolbar.AddChild(_undoButton);
        toolbar.AddChild(_redoButton);
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
            Name = "CreateMarkingToolButton",
            Text = "Create Marking",
            ToggleMode = true,
            ButtonGroup = toolGroup,
        };
        _lineMarkingToolButton.Pressed += () => SetTool(ExerciseEditorTool.CreateMarking);
        toolbar.AddChild(_lineMarkingToolButton);

        _polylineMarkingToolButton = new Button
        {
            Name = "AppendLineMarkingToolButton",
            Text = "Append Line (L)",
            ToggleMode = true,
            ButtonGroup = toolGroup,
        };
        _polylineMarkingToolButton.Pressed += () => SetTool(ExerciseEditorTool.AppendLine);
        toolbar.AddChild(_polylineMarkingToolButton);

        _cubicMarkingToolButton = new Button
        {
            Name = "AppendCubicMarkingToolButton",
            Text = "Append Curve (B)",
            ToggleMode = true,
            ButtonGroup = toolGroup,
        };
        _cubicMarkingToolButton.Pressed += () => SetTool(ExerciseEditorTool.AppendCubicBezier);
        toolbar.AddChild(_cubicMarkingToolButton);

        _splitMarkingToolButton = new Button
        {
            Name = "SplitMarkingToolButton",
            Text = "Split",
            ToggleMode = true,
            ButtonGroup = toolGroup,
        };
        _splitMarkingToolButton.Pressed += () => SetTool(ExerciseEditorTool.SplitMarkingSegment);
        toolbar.AddChild(_splitMarkingToolButton);

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
        _markingSegmentCountLabel = new Label { Name = "MarkingSegmentCount" };
        _markingLengthLabel = new Label { Name = "MarkingApproximateLength" };
        _markingProperties.AddChild(CreateLabeledControl("Segments", _markingSegmentCountLabel));
        _markingProperties.AddChild(CreateLabeledControl("Approx. length", _markingLengthLabel));

        _markingColorEdit = new ColorPickerButton { Name = "MarkingColor", EditAlpha = false };
        _markingColorEdit.ColorChanged += _ => OnMarkingPropertiesEdited();
        _markingProperties.AddChild(CreateLabeledControl("Color", _markingColorEdit));

        _markingWidthEdit = CreateCoordinateSpinBox("MarkingWidth", 0.001, 10.0);
        _markingWidthEdit.Step = 0.01;
        _markingWidthEdit.CustomArrowStep = 0.01;
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

        _markingSegmentProperties = new VBoxContainer { Name = "MarkingSegmentProperties", Visible = false };
        _markingSegmentIndexLabel = new Label { Name = "MarkingSegmentIndex" };
        _markingSegmentTypeLabel = new Label { Name = "MarkingSegmentType" };
        _markingSegmentProperties.AddChild(CreateLabeledControl("Segment", _markingSegmentIndexLabel));
        _markingSegmentProperties.AddChild(CreateLabeledControl("Segment type", _markingSegmentTypeLabel));
        _markingSegmentProperties.AddChild(CreateButton("ConvertMarkingToCurveButton", "Convert to Curve", () => ConvertSelectedMarking(true)));
        _markingSegmentProperties.AddChild(CreateButton("ConvertMarkingToLineButton", "Convert to Line", () => ConvertSelectedMarking(false)));
        _markingSegmentProperties.AddChild(CreateButton("SplitMarkingSegmentButton", "Split at midpoint", SplitSelectedMarking));
        _markingSegmentProperties.AddChild(CreateButton("DeleteMarkingSegmentButton", "Delete segment", () => DeleteSelectedObject()));
        _markingProperties.AddChild(_markingSegmentProperties);

        _markingPointProperties = new VBoxContainer { Name = "MarkingHandleProperties", Visible = false };
        _markingHandleKindLabel = new Label { Name = "MarkingHandleKind" };
        _markingPointIndexLabel = new Label { Name = "MarkingHandleSegment" };
        _markingPointXEdit = CreateCoordinateSpinBox("MarkingPointX");
        _markingPointYEdit = CreateCoordinateSpinBox("MarkingPointY");
        _markingPointXEdit.ValueChanged += _ => OnMarkingPointEdited();
        _markingPointYEdit.ValueChanged += _ => OnMarkingPointEdited();
        _markingPointProperties.AddChild(CreateLabeledControl("Handle", _markingHandleKindLabel));
        _markingPointProperties.AddChild(CreateLabeledControl("Segment", _markingPointIndexLabel));
        _markingPointProperties.AddChild(CreateLabeledControl("X", _markingPointXEdit));
        _markingPointProperties.AddChild(CreateLabeledControl("Y", _markingPointYEdit));
        _markingProperties.AddChild(_markingPointProperties);
        _markingProperties.AddChild(CreateButton("DeleteMarkingButton", "Delete marking", DeleteSelectedMarking));
        inspector.AddChild(_markingProperties);

        var help = new Label
        {
            Name = "CanvasHelp",
            Text = "V: Select, L: Append Line, B: Append Curve\nWheel: zoom; middle mouse: pan\n" +
                "Drag: 0.25 m grid; hold Ctrl to disable snapping\nEnter/double-click: finish; Escape: cancel/select\n" +
                "Ctrl+Z/Ctrl+Y: Undo/Redo; Delete: selected segment/object",
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
            CommitHistory("Synchronize trajectory endpoints");
            ExerciseDefinitionStore.SaveToFile(_document.Definition, filesystemPath);
            _currentFilePath = filesystemPath;
            _history.MarkSaved();
            SetDirty(_history.IsDirty);
            SynchronizeHistoryUi();
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

    private void ReplaceDocument(ExerciseDocument document, string? filePath, bool dirty, bool resetHistory = true)
    {
        _document = document;
        _currentFilePath = filePath;
        _canvas?.SetDocument(document, resetView: resetHistory);
        if (resetHistory)
        {
            string snapshot = ExerciseDefinitionStore.Serialize(document.Definition);
            _history.Reset(snapshot, saved: !dirty);
            _historySelections.Clear();
            _historySelections[snapshot] = MarkingSelection.None;
        }
        SynchronizeDocumentUi();
        SetDirty(resetHistory ? dirty : _history.IsDirty);
        SynchronizeHistoryUi();
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
            _canvas.SelectionKind is not (ExerciseSelectionKind.Marking or ExerciseSelectionKind.MarkingSegment or ExerciseSelectionKind.MarkingHandle))
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
        _canvas.RefreshMarking(_canvas.SelectedMarkingId);
    }

    private void OnMarkingPointEdited()
    {
        if (_synchronizingUi || _canvas?.SelectionKind != ExerciseSelectionKind.MarkingHandle)
        {
            return;
        }

        _document.MoveMarkingCoordinate(
            _canvas.SelectedMarkingId,
            _canvas.SelectedMarkingSegmentIndex,
            _canvas.SelectedMarkingHandle switch
            {
                MarkingHandleKind.PathStart => MarkingPathCoordinateKind.PathStart,
                MarkingHandleKind.SegmentEnd => MarkingPathCoordinateKind.SegmentEnd,
                MarkingHandleKind.Control1 => MarkingPathCoordinateKind.Control1,
                MarkingHandleKind.Control2 => MarkingPathCoordinateKind.Control2,
                _ => throw new InvalidOperationException(),
            },
            new Point2Dto
            {
                X = (float)_markingPointXEdit!.Value,
                Y = (float)_markingPointYEdit!.Value,
            });
        MarkDocumentChanged();
        _canvas.RefreshMarking(_canvas.SelectedMarkingId);
    }

    private void OnCanvasDocumentChanged(string description)
    {
        CommitHistory(description);
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

        CommitHistory("Delete selected object");
        SynchronizeDocumentUi();
        string message = result switch
        {
            SelectionDeleteResult.DeletedCone => "Selected cone deleted.",
            SelectionDeleteResult.DeletedMarking => "Selected marking deleted.",
            SelectionDeleteResult.DeletedMarkingSegment => "Selected marking segment deleted.",
            _ => "Selected trajectory point deleted.",
        };
        SetStatus(message, false);
        return true;
    }

    private void DeleteSelectedMarking()
    {
        if (_canvas is null ||
            _canvas.SelectionKind is not (ExerciseSelectionKind.Marking or ExerciseSelectionKind.MarkingSegment or ExerciseSelectionKind.MarkingHandle))
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

        SynchronizeDocumentUi();
        SetStatus("Trajectory point inserted between adjacent anchors.", false);
    }

    private void SplitSelectedMarking()
    {
        if (_canvas?.SplitSelectedMarkingSegment() != true) return;
        SynchronizeDocumentUi();
        SetStatus("Selected marking segment split at its midpoint.", false);
    }

    private void ConvertSelectedMarking(bool toCubic)
    {
        if (_canvas?.ConvertSelectedMarkingSegment(toCubic) != true)
        {
            SetStatus(toCubic ? "Select a line marking segment." : "Select a cubic marking segment.", true);
            return;
        }
        SynchronizeDocumentUi();
        SetStatus(toCubic ? "Marking segment converted to Curve." : "Marking segment converted to Line.", false);
    }

    private void ConvertSelectedToCubic()
    {
        if (_canvas?.ConvertSelectedToCubic() != true)
        {
            SetStatus("Select a straight trajectory section before converting it.", true);
            return;
        }

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
        CommitHistory("Edit Exercise Definition");
        _canvas?.QueueRedraw();
    }

    private void CommitHistory(string description)
    {
        string snapshot = ExerciseDefinitionStore.Serialize(_document.Definition);
        if (_history.Commit(snapshot, description))
            _historySelections[snapshot] = _canvas?.MarkingSelection ?? MarkingSelection.None;
        SetDirty(_history.IsDirty);
        SynchronizeHistoryUi();
    }

    private bool Undo() => RestoreHistory(_history.Undo(), "Undo");

    private bool Redo() => RestoreHistory(_history.Redo(), "Redo");

    private bool RestoreHistory(string? snapshot, string action)
    {
        if (snapshot is null) return false;
        ExerciseDefinitionDto definition = ExerciseDefinitionStore.LoadFromJson(snapshot, $"{action} history");
        ReplaceDocument(new ExerciseDocument(definition), _currentFilePath, dirty: _history.IsDirty, resetHistory: false);
        if (_historySelections.TryGetValue(snapshot, out MarkingSelection selection) && selection.HasMarking &&
            _document.FindMarking(selection.MarkingId) is { } marking)
        {
            selection = selection.Sanitize(marking.Path);
            int segment = selection.SegmentIndex;
            if (selection.HandleKind == MarkingHandleKind.PathStart)
                _canvas?.SelectMarkingHandle(selection.MarkingId, -1, selection.HandleKind);
            else if ((uint)segment < (uint)marking.Path.Segments.Length)
            {
                if (selection.HasHandle) _canvas?.SelectMarkingHandle(selection.MarkingId, segment, selection.HandleKind);
                else _canvas?.SelectMarkingSegment(selection.MarkingId, segment);
            }
            else _canvas?.SelectMarking(selection.MarkingId);
        }
        SetStatus(action, false);
        return true;
    }

    private void SynchronizeHistoryUi()
    {
        if (_undoButton is not null) _undoButton.Disabled = !_history.CanUndo;
        if (_redoButton is not null) _redoButton.Disabled = !_history.CanRedo;
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
            case ExerciseSelectionKind.MarkingSegment:
            case ExerciseSelectionKind.MarkingHandle:
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

        string suffix = _canvas.SelectionKind switch
        {
            ExerciseSelectionKind.MarkingHandle => $" / segment {_canvas.SelectedMarkingSegmentIndex} / {_canvas.SelectedMarkingHandle}",
            ExerciseSelectionKind.MarkingSegment => $" / segment {_canvas.SelectedMarkingSegmentIndex}",
            _ => string.Empty,
        };
        _selectionTitle!.Text = $"Selection: {marking.Id}{suffix}";
        _coneProperties!.Visible = false;
        _trajectoryPointProperties!.Visible = false;
        _trajectorySegmentProperties!.Visible = false;
        _markingProperties!.Visible = true;
        _markingIdLabel!.Text = marking.Id;
        _markingTypeLabel!.Text = PathEditing.IsAllLine(marking.Path) ? "line path" : "curved path";
        _markingSegmentCountLabel!.Text = marking.Path.Segments.Length.ToString();
        _markingLengthLabel!.Text = $"{PathSampler.Sample(marking.Path).TotalLength:0.###} m";
        _markingColorEdit!.Color = ParseCanonicalColor(marking.Color);
        _markingWidthEdit!.Value = marking.WidthMeters;
        _markingStyleEdit!.Selected = marking.Style switch
        {
            "dashed" => 1,
            "dotted" => 2,
            _ => 0,
        };
        _markingVisibleEdit!.ButtonPressed = marking.VisibleInViewer;

        bool segmentSelected = _canvas.SelectedMarkingSegmentIndex >= 0;
        _markingSegmentProperties!.Visible = segmentSelected;
        if (segmentSelected)
        {
            _markingSegmentIndexLabel!.Text = _canvas.SelectedMarkingSegmentIndex.ToString();
            _markingSegmentTypeLabel!.Text = marking.Path.Segments[_canvas.SelectedMarkingSegmentIndex] is CubicBezierPathSegmentDefinition
                ? "cubicBezier" : "line";
        }

        bool handleSelected = _canvas.SelectionKind == ExerciseSelectionKind.MarkingHandle;
        _markingPointProperties!.Visible = handleSelected;
        if (handleSelected)
        {
            int segmentIndex = _canvas.SelectedMarkingSegmentIndex;
            Point2Dto point = _canvas.SelectedMarkingHandle switch
            {
                MarkingHandleKind.PathStart => marking.Path.Start,
                MarkingHandleKind.SegmentEnd => marking.Path.Segments[segmentIndex].EndPoint,
                MarkingHandleKind.Control1 => ((CubicBezierPathSegmentDefinition)marking.Path.Segments[segmentIndex]).Control1,
                MarkingHandleKind.Control2 => ((CubicBezierPathSegmentDefinition)marking.Path.Segments[segmentIndex]).Control2,
                _ => marking.Path.Start,
            };
            _markingHandleKindLabel!.Text = _canvas.SelectedMarkingHandle.ToString();
            _markingPointIndexLabel!.Text = segmentIndex < 0 ? "Path start" : segmentIndex.ToString();
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
            SynchronizeToolButtons();
            return;
        }

        _canvas?.SetTool(tool);

        string message = tool switch
        {
            ExerciseEditorTool.Select => "Select tool active.",
            ExerciseEditorTool.AddCone => "Add Cone tool active.",
            ExerciseEditorTool.EditTrajectory =>
                "Edit Trajectory tool active. Select/drag anchors or press Start Trajectory.",
            ExerciseEditorTool.CreateMarking => "Create Marking active. Click start, then line endpoints; Enter finishes.",
            ExerciseEditorTool.AppendLine => "Append Line active. Select a marking, then click its new endpoint.",
            ExerciseEditorTool.AppendCubicBezier => "Append Curve active. Select a marking, then click its endpoint.",
            _ => "Split active. Click a marking segment at the desired split position.",
        };
        SetStatus(message, false);
        SynchronizeToolButtons();
        SynchronizeTrajectoryBuildUi();
    }

    private void SynchronizeToolButtons()
    {
        if (_canvas is null) return;
        _selectToolButton?.SetPressedNoSignal(_canvas.Tool == ExerciseEditorTool.Select);
        _addConeToolButton?.SetPressedNoSignal(_canvas.Tool == ExerciseEditorTool.AddCone);
        _trajectoryToolButton?.SetPressedNoSignal(_canvas.Tool == ExerciseEditorTool.EditTrajectory);
        _lineMarkingToolButton?.SetPressedNoSignal(_canvas.Tool == ExerciseEditorTool.CreateMarking);
        _polylineMarkingToolButton?.SetPressedNoSignal(_canvas.Tool == ExerciseEditorTool.AppendLine);
        _cubicMarkingToolButton?.SetPressedNoSignal(_canvas.Tool == ExerciseEditorTool.AppendCubicBezier);
        _splitMarkingToolButton?.SetPressedNoSignal(_canvas.Tool == ExerciseEditorTool.SplitMarkingSegment);
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
            // Zero disables Range quantization so a later edit of the other axis
            // cannot overwrite a precise Ctrl-drag coordinate. Arrow buttons still
            // retain the normal grid-sized editing increment.
            Step = 0.0,
            CustomArrowStep = 0.25,
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
