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
    }

    private static readonly string[] ConeColors = ["red", "blue", "yellow", "orange"];

    private ExerciseDocument _document = ExerciseDocument.CreateNew();
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
    private Button? _startTrajectoryButton;
    private Button? _finishTrajectoryButton;
    private Label? _fileLabel;
    private Label? _dirtyLabel;
    private Label? _statusLabel;
    private FileDialog? _openDialog;
    private FileDialog? _saveDialog;
    private ConfirmationDialog? _unsavedDialog;
    private string? _currentFilePath;
    private bool _dirty;
    private bool _synchronizingUi;
    private PendingDestructiveAction _pendingAction;

    /// <inheritdoc />
    public override void _Ready()
    {
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
        toolbar.AddChild(_trajectoryToolButton);

        _startTrajectoryButton = CreateButton("StartTrajectoryButton", "Start Trajectory", BeginTrajectoryBuild);
        _finishTrajectoryButton = CreateButton("FinishTrajectoryButton", "Finish Trajectory", FinishTrajectoryBuild);
        _finishTrajectoryButton.Disabled = true;
        toolbar.AddChild(_startTrajectoryButton);
        toolbar.AddChild(_finishTrajectoryButton);
        toolbar.AddChild(CreateButton("DeleteButton", "Delete selected", () => DeleteSelectedObject()));
        toolbar.AddChild(new VSeparator());

        _fileLabel = new Label
        {
            Name = "CurrentFile",
            Text = "Untitled exercise",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        toolbar.AddChild(_fileLabel);

        _dirtyLabel = new Label
        {
            Name = "DirtyIndicator",
            Text = "Modified",
            VerticalAlignment = VerticalAlignment.Center,
        };
        toolbar.AddChild(_dirtyLabel);
        return toolbar;
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

        var help = new Label
        {
            Name = "CanvasHelp",
            Text = "Wheel: zoom\nMiddle mouse: pan\nDrag: 0.25 m snap for cones, anchors and handles\n" +
                "Trajectory: click an anchor, handle or section\nDelete: remove selected cone or permitted anchor",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        inspector.AddChild(help);
        scroll.AddChild(inspector);
        return scroll;
    }

    private void BuildDialogs()
    {
        string defaultDirectory = ProjectSettings.GlobalizePath("res://examples");
        _openDialog = CreateJsonDialog("OpenExerciseDialog", "Open Exercise Definition", FileDialog.FileModeEnum.OpenFile);
        _openDialog.CurrentDir = defaultDirectory;
        _openDialog.FileSelected += OpenSelectedFile;
        _openDialog.Canceled += () => SetStatus("Open canceled. Current document was not changed.", false);
        AddChild(_openDialog);

        _saveDialog = CreateJsonDialog("SaveExerciseDialog", "Save Exercise Definition", FileDialog.FileModeEnum.SaveFile);
        _saveDialog.CurrentDir = defaultDirectory;
        _saveDialog.CurrentFile = "exercise.json";
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
            SetStatus("Operation canceled. Unsaved document was preserved.", false);
        };
        AddChild(_unsavedDialog);
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
    }

    private void Save()
    {
        if (string.IsNullOrWhiteSpace(_currentFilePath))
        {
            _saveDialog!.PopupCenteredRatio(0.82f);
            return;
        }

        SaveSelectedFile(_currentFilePath);
    }

    private void SaveSelectedFile(string path)
    {
        try
        {
            if (_canvas?.TryFinishTrajectoryBuild() == false)
            {
                SetStatus("Save blocked: finish the trajectory with at least two clicked points.", true);
                return;
            }

            string filesystemPath = ToFilesystemPath(path);
            _document.SynchronizeEndpointsFromTrajectory();
            ExerciseDefinitionStore.SaveToFile(_document.Definition, filesystemPath);
            _currentFilePath = filesystemPath;
            SetDirty(false);
            SetStatus($"Saved Exercise Definition to '{filesystemPath}'.", false);
            GD.Print($"Saved Exercise Definition '{_document.Definition.Exercise.Id}' to '{filesystemPath}'.");
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
            string filesystemPath = ToFilesystemPath(path);
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

    private void OnCanvasDocumentChanged()
    {
        SetDirty(true);
        SynchronizeDocumentUi();
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

        SetDirty(true);
        SynchronizeDocumentUi();
        SetStatus(
            result == SelectionDeleteResult.DeletedCone
                ? "Selected cone deleted."
                : "Selected trajectory point deleted.",
            false);
        return true;
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
            _trajectoryPointProperties is null || _trajectorySegmentProperties is null)
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
                _trajectoryPointIndexLabel!.Text = pointIndex.ToString();
                _trajectoryPointRoleLabel!.Text = role;
                _trajectoryPointXEdit!.Value = point.X;
                _trajectoryPointYEdit!.Value = point.Y;
                break;
            case ExerciseSelectionKind.TrajectorySegment:
            case ExerciseSelectionKind.BezierControl:
                SynchronizeTrajectorySegmentUi();
                break;
            default:
                _selectionTitle.Text = "Selection: none";
                _coneProperties.Visible = false;
                _trajectoryPointProperties.Visible = false;
                _trajectorySegmentProperties.Visible = false;
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

        _canvas?.SetTool(tool);

        string message = tool switch
        {
            ExerciseEditorTool.Select => "Select tool active.",
            ExerciseEditorTool.AddCone => "Add Cone tool active.",
            _ => "Edit Trajectory tool active. Select/drag anchors or press Start Trajectory.",
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
}
