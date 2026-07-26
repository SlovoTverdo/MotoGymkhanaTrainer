using Godot;
using MotoGymkhanaTrainer.Tracks;

namespace MotoGymkhanaTrainer.Viewer;

/// <summary>Loads an exported track snapshot and builds its Viewer geometry.</summary>
public partial class TrackViewer : Node3D
{
    private const string TrackExportRoot = "res://exports/tracks";
    private const string ConeModelPath = "res://Assets/Models/Traffic Cone_Textured.glb";
    private const string ToggleGridAction = "toggle_grid";
    private const string ToggleTrajectoryAction = "toggle_trajectory";
    private const float TopperHeight = 0.14f;
    private const float ConeModelTopHeight = 0.79f;
    private const float BaseGridLineThickness = 0.015f;
    private const float GridLineHeight = 0.006f;
    private const float PathSegmentHeight = 0.008f;
    private const float MarkingSurfaceOffset = 0.025f;
    private const float TrajectorySurfaceOffset = 0.04f;
    private const float DirectionMarkerSurfaceOffset = 0.015f;
    private const float MaximumProjectionSpacingMeters = 0.35f;
    private const float TrajectoryWidth = 0.08f;
    private const float TrajectoryDirectionMarkerIntervalMeters = 5.0f;
    private const float DirectionMarkerLength = 0.5f;
    private const float DirectionMarkerHalfWidth = 0.2f;
    private const float DirectionMarkerLineWidth = 0.045f;
    private const int CubicBezierSubdivisionCount = 32;
    private const float TrajectoryContinuityToleranceMeters = 0.01f;

    private PackedScene? _coneModel;
    private Node3D? _runtimeTrackRoot;
    private Node3D? _gridOverlay;
    private Node3D? _trajectoryOverlay;
    private Label? _trackNameLabel;
    private Label? _statusLabel;
    private Label? _movementModeLabel;
    private CheckBox? _trajectoryToggle;
    private FileDialog? _trackFileDialog;
    private SandboxedJsonLibrary? _trackExportLibrary;
    private WorldEnvironment? _worldEnvironment;
    private Godot.Environment? _fallbackEnvironment;
    private SurfaceProjectionService? _surfaceProjection;
    private bool _trajectoryVisible = true;
    private bool _loading;
    private bool _debugCollisions;

    /// <inheritdoc />
    public override void _Ready()
    {
        _worldEnvironment = GetNode<WorldEnvironment>("../WorldEnvironment");
        _fallbackEnvironment = _worldEnvironment.Environment ?? throw new InvalidOperationException(
            "Viewer fallback WorldEnvironment has no Environment resource.");
        _trackExportLibrary = new SandboxedJsonLibrary(
            ProjectSettings.GlobalizePath(TrackExportRoot),
            "Viewer track export library",
            $"{TrackExportRoot}/");
        CreateViewerUi();
        FirstPersonCamera controller = GetNode<FirstPersonCamera>("../ViewerCharacter");
        controller.MovementStatusChanged += OnMovementStatusChanged;

        try
        {
            _coneModel = LoadConeModel(ConeModelPath);
        }
        catch (Exception exception)
        {
            ReportLoadFailure(ConeModelPath, exception);
            return;
        }

        string? startupTrack = FindStartupTrackPath(OS.GetCmdlineUserArgs());
        if (startupTrack is null)
        {
            SetStatus("Select an exported Track JSON file.", new Color(0.85f, 0.85f, 0.85f));
        }
        else
        {
            // Viewer receives only a self-contained exported Track path. The
            // regular sandbox validation below still applies to process arguments.
            OnTrackFileSelected(startupTrack);
        }
    }

    /// <summary>Extracts the path-only preview contract from arguments after "--".</summary>
    public static string? FindStartupTrackPath(IReadOnlyList<string> arguments)
    {
        for (int index = 0; index + 1 < arguments.Count; index++)
        {
            if (arguments[index] == "--track" && !string.IsNullOrWhiteSpace(arguments[index + 1]))
            {
                return arguments[index + 1];
            }
        }

        return null;
    }

    /// <inheritdoc />
    public override void _Input(InputEvent @event)
    {
        bool handled = false;

        if (_gridOverlay is not null && @event.IsActionPressed(ToggleGridAction))
        {
            _gridOverlay.Visible = !_gridOverlay.Visible;
            handled = true;
        }

        if (_trajectoryOverlay is not null && @event.IsActionPressed(ToggleTrajectoryAction))
        {
            SetTrajectoryVisible(!_trajectoryVisible);
            handled = true;
        }

        if (@event is InputEventKey key && key.Pressed && !key.Echo && key.Keycode == Key.F10)
        {
            _debugCollisions = !_debugCollisions;
            GetTree().DebugCollisionsHint = _debugCollisions;
            SetStatus(
                $"Physics debug collision shapes: {(_debugCollisions ? "ON" : "OFF")}.",
                new Color(0.7f, 0.85f, 1.0f));
            handled = true;
        }

        if (handled)
        {
            GetViewport().SetInputAsHandled();
        }
    }

    private void CreateViewerUi()
    {
        var canvas = new CanvasLayer { Name = "ViewerUI" };
        var panel = new PanelContainer
        {
            Name = "TrackPanel",
            Position = new Vector2(16.0f, 16.0f),
            CustomMinimumSize = new Vector2(330.0f, 0.0f),
        };
        var margin = new MarginContainer { Name = "Margin" };
        margin.AddThemeConstantOverride("margin_left", 12);
        margin.AddThemeConstantOverride("margin_top", 10);
        margin.AddThemeConstantOverride("margin_right", 12);
        margin.AddThemeConstantOverride("margin_bottom", 10);

        var layout = new VBoxContainer { Name = "Layout" };
        layout.AddThemeConstantOverride("separation", 8);

        _trackNameLabel = new Label
        {
            Name = "TrackName",
            Text = "No track loaded",
        };
        _trackNameLabel.AddThemeFontSizeOverride("font_size", 20);

        var openTrackButton = new Button
        {
            Name = "OpenTrackButton",
            Text = "Open Track",
        };
        openTrackButton.Pressed += OnOpenTrackPressed;

        _trajectoryToggle = new CheckBox
        {
            Name = "TrajectoryToggle",
            Text = "Show trajectory",
            ButtonPressed = true,
        };
        _trajectoryToggle.Toggled += OnTrajectoryVisibilityToggled;

        _statusLabel = new Label
        {
            Name = "LoadStatus",
            Text = "Select an exported Track JSON file.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(300.0f, 42.0f),
        };

        _movementModeLabel = new Label
        {
            Name = "MovementMode",
            Text = "Mode: Walk · F toggles Fly · F10 collision debug",
        };

        layout.AddChild(_trackNameLabel);
        layout.AddChild(openTrackButton);
        layout.AddChild(_trajectoryToggle);
        layout.AddChild(_movementModeLabel);
        layout.AddChild(_statusLabel);
        margin.AddChild(layout);
        panel.AddChild(margin);
        canvas.AddChild(panel);

        _trackFileDialog = new FileDialog
        {
            Name = "TrackFileDialog",
            Title = "Open exported Track JSON",
            Access = FileDialog.AccessEnum.Resources,
            FileMode = FileDialog.FileModeEnum.OpenFile,
            UseNativeDialog = false,
            Size = new Vector2I(900, 600),
            Filters = ["*.json ; Track JSON files"],
            CurrentDir = TrackExportRoot,
            // Godot prevents navigation above this virtual root. The selected
            // path is validated again by SandboxedJsonLibrary before loading.
            RootSubfolder = TrackExportRoot,
            FolderCreationEnabled = false,
        };
        _trackFileDialog.FileSelected += OnTrackFileSelected;
        _trackFileDialog.Canceled += OnTrackDialogCanceled;
        canvas.AddChild(_trackFileDialog);
        AddChild(canvas);
    }

    private void OnOpenTrackPressed()
    {
        // The camera normally captures the pointer for mouse-look. Release it so
        // the operating-system-style file browser is immediately interactive.
        Input.MouseMode = Input.MouseModeEnum.Visible;
        _trackFileDialog?.PopupCenteredRatio(0.8f);
    }

    private async void OnTrackFileSelected(string path)
    {
        try
        {
            string filesystemPath = path.StartsWith("res://", StringComparison.OrdinalIgnoreCase)
                ? ProjectSettings.GlobalizePath(path)
                : path;
            string safePath = _trackExportLibrary!.ResolveExistingJson(filesystemPath);
            await TryReplaceTrackAsync(safePath);
        }
        catch (Exception exception)
        {
            ReportLoadFailure(path, exception);
        }
    }

    private void OnTrackDialogCanceled()
    {
        SetStatus("Open canceled. Current track was not changed.", new Color(0.85f, 0.85f, 0.85f));
    }

    private void OnTrajectoryVisibilityToggled(bool visible)
    {
        SetTrajectoryVisible(visible);
    }

    private void SetTrajectoryVisible(bool visible)
    {
        _trajectoryVisible = visible;

        if (_trajectoryOverlay is not null)
        {
            _trajectoryOverlay.Visible = visible;
        }

        _trajectoryToggle?.SetPressedNoSignal(visible);
    }

    private async Task<bool> TryReplaceTrackAsync(string path)
    {
        if (_loading)
        {
            SetStatus("A Track load is already in progress.", new Color(1.0f, 0.75f, 0.35f));
            return false;
        }

        _loading = true;
        Node3D? candidate = null;
        try
        {
            TrackSnapshotDto track = LoadTrack(path);
            bool hasEntryPose = TrajectoryGeometry.TryGetEntryPose(
                track.Trajectory,
                out Point2Dto trajectoryStart,
                out Point2Dto trajectoryDirection);
            if (!hasEntryPose)
            {
                trajectoryStart = new Point2Dto();
                trajectoryDirection = new Point2Dto { X = 0.0f, Y = 1.0f };
            }

            PackedScene coneModel = _coneModel ?? throw new InvalidOperationException(
                "The traffic cone model is not available.");
            candidate = CreateRuntimeVenue(track);
            Godot.Environment nextEnvironment = CreatePanoramaEnvironment(track.Panorama) ??
                _fallbackEnvironment ?? throw new InvalidOperationException(
                    "Viewer fallback environment is unavailable.");

            FirstPersonCamera controller = GetNode<FirstPersonCamera>("../ViewerCharacter");
            controller.SuspendForReload();
            _worldEnvironment!.Environment = _fallbackEnvironment;
            ReplaceRuntimeVenue(candidate);

            /*
             * Venue collision bodies must enter the active physics space before
             * any downward query. PhysicsFrame is deterministic synchronization;
             * no arbitrary timer delay is used.
             */
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

            _surfaceProjection = new SurfaceProjectionService(GetWorld3D());
            Node3D trackRoot = CreateProjectedTrack(track, coneModel, _surfaceProjection);
            candidate.AddChild(trackRoot);
            _trajectoryOverlay = trackRoot.GetNode<Node3D>("TrackGeometry/Trajectory");
            SetTrajectoryVisible(_trajectoryVisible);

            controller.SetProjectionService(_surfaceProjection);
            if (!controller.TryPlaceAtDomainStart(trajectoryStart, trajectoryDirection))
                throw new InvalidOperationException(
                    "Viewer character could not find a safe walkable spawn near the trajectory start or Venue centre.");

            _worldEnvironment.Environment = nextEnvironment;
            string displayName = string.IsNullOrWhiteSpace(track.Track.Name)
                ? track.Track.Id
                : track.Track.Name;
            _trackNameLabel!.Text = displayName;
            SetStatus(
                $"Loaded '{displayName}' from {Path.GetFileName(path)}.",
                new Color(0.55f, 0.95f, 0.62f));

            GD.Print(
                $"Loaded track '{displayName}' with {track.Cones.Length} cones, " +
                $"{track.Markings.Length} markings, {track.Trajectory.Segments.Length} trajectory segments and " +
                $"{_surfaceProjection.Diagnostics.Count} grouped projection warning(s).");
            return true;
        }
        catch (Exception exception)
        {
            if (candidate is not null)
            {
                if (candidate == _runtimeTrackRoot)
                {
                    RemoveChild(candidate);
                    _runtimeTrackRoot = null;
                    _gridOverlay = null;
                    _trajectoryOverlay = null;
                    _surfaceProjection = null;
                }
                candidate.Free();
            }
            ReportLoadFailure(path, exception);
            return false;
        }
        finally
        {
            _loading = false;
        }
    }

    private static Node3D CreateRuntimeVenue(TrackSnapshotDto track)
    {
        var root = new Node3D { Name = "RuntimeTrackCandidate" };
        var venueRoot = new Node3D { Name = "VenueRoot" };
        venueRoot.AddChild(CreateSurface(track.Area));
        venueRoot.AddChild(CreateGridOverlay(track.Area));
        venueRoot.AddChild(CreateVenueObjects(track.VenueObjects));
        root.AddChild(venueRoot);
        return root;
    }

    private static Node3D CreateProjectedTrack(
        TrackSnapshotDto track,
        PackedScene coneModel,
        SurfaceProjectionService projection)
    {
        Node3D venueRoot = new() { Name = "VenueGeometry" };
        venueRoot.AddChild(CreateConeCollection(
            "Cones", track.Cones.Where(IsVenueCone), coneModel, projection));
        venueRoot.AddChild(CreateMarkings(
            track.Markings.Where(IsVenueMarking), "Markings", projection));

        var trackRoot = new Node3D { Name = "TrackGeometry" };
        trackRoot.AddChild(CreateConeCollection(
            "ExerciseCones", track.Cones.Where(cone => !IsVenueCone(cone)), coneModel, projection));
        trackRoot.AddChild(CreateMarkings(
            track.Markings.Where(marking => !IsVenueMarking(marking)),
            "ExerciseMarkings",
            projection));
        trackRoot.AddChild(CreateTrajectory(track.Trajectory, projection));

        var result = new Node3D { Name = "ProjectedGeometry" };
        result.AddChild(venueRoot);
        result.AddChild(trackRoot);
        return result;
    }

    private void ReplaceRuntimeVenue(Node3D candidate)
    {
        Node3D newGridOverlay = candidate.GetNode<Node3D>("VenueRoot/GridOverlay");
        Node3D? previousRoot = _runtimeTrackRoot;
        if (previousRoot is not null)
        {
            RemoveChild(previousRoot);
            previousRoot.Free();
        }

        AddChild(candidate);
        candidate.Name = "RuntimeTrack";
        _runtimeTrackRoot = candidate;
        _gridOverlay = newGridOverlay;
        _trajectoryOverlay = null;
        _surfaceProjection = null;
    }

    private void OnMovementStatusChanged(ViewerMovementMode mode, string message)
    {
        if (_movementModeLabel is not null)
            _movementModeLabel.Text =
                $"Mode: {mode} · F toggles · Fly vertical Space/Ctrl · F10 collision debug";
        SetStatus(message, new Color(0.7f, 0.85f, 1.0f));
    }

    private void ReportLoadFailure(string path, Exception exception)
    {
        SetStatus($"Load failed: {exception.Message}", new Color(1.0f, 0.5f, 0.45f));
        GD.PushError($"Unable to load track '{path}': {exception}");
    }

    private void SetStatus(string message, Color color)
    {
        if (_statusLabel is null)
        {
            return;
        }

        _statusLabel.Text = message;
        _statusLabel.Modulate = color;
    }

    private static TrackSnapshotDto LoadTrack(string path)
    {
        if (!Godot.FileAccess.FileExists(path))
        {
            throw new FileNotFoundException($"Track file '{path}' was not found.", path);
        }

        string json = Godot.FileAccess.GetFileAsString(path);
        Error fileError = Godot.FileAccess.GetOpenError();
        if (fileError != Error.Ok)
        {
            throw new IOException($"Track file '{path}' could not be read ({fileError}).");
        }

        return TrackLoader.LoadFromJson(json, path);
    }

    private static PackedScene LoadConeModel(string path)
    {
        PackedScene? coneModel = ResourceLoader.Load<PackedScene>(path);
        return coneModel ?? throw new InvalidDataException(
            $"Cone model '{path}' could not be loaded as a Godot scene.");
    }

    private static Godot.Environment? CreatePanoramaEnvironment(PanoramaSnapshotDto panorama)
    {
        if (!panorama.Enabled) return null;
        Texture2D? texture = ResourceLoader.Load<Texture2D>(panorama.TexturePath);
        if (texture is null)
        {
            GD.PushError(
                $"Panorama texture '{panorama.TexturePath}' could not be loaded as Texture2D; fallback environment remains active.");
            return null;
        }

        var material = new PanoramaSkyMaterial
        {
            Panorama = texture,
            EnergyMultiplier = panorama.EnergyMultiplier,
        };
        var sky = new Sky { SkyMaterial = material };
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = sky,
            SkyRotation = new Vector3(0.0f, Mathf.DegToRad(panorama.RotationDeg), 0.0f),
        };
        return environment;
    }

    private static Node3D CreateVenueObjects(IEnumerable<VenueObjectSnapshotDto> objects)
    {
        var root = new Node3D { Name = "Objects" };
        var missingCollisionByAsset = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (VenueObjectSnapshotDto item in objects)
        {
            if (!item.VisibleInViewer) continue;
            try
            {
                PackedScene? scene = ResourceLoader.Load<PackedScene>(item.AssetPath);
                if (scene is null)
                {
                    GD.PushError($"Venue object '{item.Id}' asset '{item.AssetPath}' is missing or is not a PackedScene; object skipped.");
                    continue;
                }

                Node instance = scene.Instantiate();
                if (instance is not Node3D spatial)
                {
                    GD.PushError($"Venue object '{item.Id}' asset '{item.AssetPath}' root is not Node3D; object skipped.");
                    instance.Free();
                    continue;
                }

                spatial.Name = item.Id;
                spatial.Position = DomainCoordinateMapper.ToGodot(item.Position, item.Elevation);
                spatial.Rotation = new Vector3(0.0f, Mathf.DegToRad(item.RotationDeg), 0.0f);
                spatial.Scale = new Vector3(item.Scale.X, item.Scale.Y, item.Scale.Z);
                if (!item.CollisionEnabled)
                {
                    DisableCollisionRecursively(spatial);
                }
                else if (!ContainsEnabledCollision(spatial))
                {
                    if (!missingCollisionByAsset.TryGetValue(item.AssetPath, out List<string>? ids))
                    {
                        ids = [];
                        missingCollisionByAsset.Add(item.AssetPath, ids);
                    }
                    ids.Add(item.Id);
                }
                root.AddChild(spatial);
            }
            catch (Exception exception)
            {
                GD.PushError(
                    $"Venue object '{item.Id}' asset '{item.AssetPath}' failed at runtime and was skipped: {exception.Message}");
            }
        }

        foreach ((string assetPath, List<string> ids) in missingCollisionByAsset)
        {
            string sample = string.Join(", ", ids.Take(4));
            string remainder = ids.Count > 4 ? $" and {ids.Count - 4} more" : string.Empty;
            GD.PushWarning(
                $"Venue asset '{assetPath}' is collisionEnabled=true but contains no enabled " +
                $"CollisionShape3D/CollisionPolygon3D. Instances: {sample}{remainder}.");
        }

        return root;
    }

    private static bool ContainsEnabledCollision(Node node)
    {
        if (node is CollisionShape3D { Disabled: false } ||
            node is CollisionPolygon3D { Disabled: false })
            return true;
        foreach (Node child in node.GetChildren())
            if (ContainsEnabledCollision(child)) return true;
        return false;
    }

    /// <summary>Disables authored collision without creating or deleting collision geometry.</summary>
    public static void DisableCollisionRecursively(Node node)
    {
        if (node is CollisionShape3D shape) shape.Disabled = true;
        if (node is CollisionPolygon3D polygon) polygon.Disabled = true;
        if (node is CollisionObject3D collisionObject)
        {
            collisionObject.CollisionLayer = 0;
            collisionObject.CollisionMask = 0;
            collisionObject.InputRayPickable = false;
        }

        foreach (Node child in node.GetChildren()) DisableCollisionRecursively(child);
    }

    private static Node3D CreateConeCollection(
        string name,
        IEnumerable<ConeDto> cones,
        PackedScene coneModel,
        SurfaceProjectionService projection)
    {
        var root = new Node3D { Name = name };
        foreach (ConeDto cone in cones) root.AddChild(CreateCone(cone, coneModel, projection));
        return root;
    }

    private static bool IsVenueCone(ConeDto cone) =>
        cone.Id.StartsWith("venue--cone--", StringComparison.Ordinal);

    private static bool IsVenueMarking(MarkingDto marking) =>
        marking.Id.StartsWith("venue--marking--", StringComparison.Ordinal);

    private static Node3D CreateSurface(AreaDto area)
    {
        var groundMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.24f, 0.27f, 0.29f),
            Roughness = 0.95f,
        };

        var root = new Node3D { Name = "Surface" };
        var ground = new MeshInstance3D
        {
            Name = "TrainingArea",
            Mesh = new PlaneMesh
            {
                Size = new Vector2(area.Width, area.Length),
                SubdivideWidth = Math.Max(0, (int)area.Width - 1),
                SubdivideDepth = Math.Max(0, (int)area.Length - 1),
                Material = groundMaterial,
            },
            // Track Project and exported world geometry use the geometric centre
            // of the area as origin. PlaneMesh is centred already, so no offset is
            // applied here; domain X/Y still maps directly to Godot X/Z.
            Position = Vector3.Zero,
        };
        root.AddChild(ground);

        const float collisionThickness = 0.2f;
        var surfaceBody = new StaticBody3D
        {
            Name = "WalkableSurfaceBody",
            CollisionLayer = ViewerPhysicsLayers.WalkableSurface,
            CollisionMask = 0,
        };
        surfaceBody.AddChild(new CollisionShape3D
        {
            Name = "CollisionShape3D",
            Position = new Vector3(0.0f, -collisionThickness / 2.0f, 0.0f),
            Shape = new BoxShape3D
            {
                Size = new Vector3(area.Width, collisionThickness, area.Length),
            },
        });
        root.AddChild(surfaceBody);
        return root;
    }

    private static Node3D CreateGridOverlay(AreaDto area)
    {
        var grid = new Node3D { Name = "GridOverlay" };
        var gridMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.78f, 0.84f, 0.9f),
            Roughness = 0.9f,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };

        int minimumX = Mathf.CeilToInt(-area.Width / 2.0f);
        int maximumX = Mathf.FloorToInt(area.Width / 2.0f);
        int minimumY = Mathf.CeilToInt(-area.Length / 2.0f);
        int maximumY = Mathf.FloorToInt(area.Length / 2.0f);

        // Each line is a very shallow box rather than a renderer line. Its physical
        // width therefore remains stable and can represent the 1x/2x/3x hierarchy.
        for (int x = minimumX; x <= maximumX; x++)
        {
            grid.AddChild(CreateGridLine(
                $"GridX_{x}",
                new Vector3(GetGridLineThickness(x), GridLineHeight, area.Length),
                new Vector3(x, GridLineHeight / 2.0f, 0.0f),
                gridMaterial));
        }

        for (int y = minimumY; y <= maximumY; y++)
        {
            grid.AddChild(CreateGridLine(
                $"GridY_{y}",
                new Vector3(area.Width, GridLineHeight, GetGridLineThickness(y)),
                DomainCoordinateMapper.ToGodot(
                    new Point2Dto { X = 0.0f, Y = y },
                    GridLineHeight / 2.0f),
                gridMaterial));
        }

        // Labels sit just outside two adjacent edges. X/Y names preserve the domain
        // coordinate meaning; every position uses the same Y -> -Z mapping as
        // cones, markings and trajectory so the ruler cannot invert independently.
        for (int x = minimumX; x <= maximumX; x++)
        {
            if (x % 10 != 0) continue;
            grid.AddChild(CreateGridLabel(
                $"GridLabelX_{x}",
                $"X: {x}",
                DomainCoordinateMapper.ToGodot(
                    new Point2Dto { X = x, Y = area.Length / 2.0f + 0.55f },
                    0.32f)));
        }

        for (int y = minimumY; y <= maximumY; y++)
        {
            if (y % 10 != 0) continue;
            grid.AddChild(CreateGridLabel(
                $"GridLabelY_{y}",
                $"Y: {y}",
                DomainCoordinateMapper.ToGodot(
                    new Point2Dto { X = -area.Width / 2.0f - 0.55f, Y = y },
                    0.32f)));
        }

        return grid;
    }

    private static MeshInstance3D CreateGridLine(
        string name,
        Vector3 size,
        Vector3 position,
        Material material)
    {
        return new MeshInstance3D
        {
            Name = name,
            Mesh = new BoxMesh
            {
                Size = size,
                Material = material,
            },
            Position = position,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }

    private static Label3D CreateGridLabel(string name, string text, Vector3 position)
    {
        return new Label3D
        {
            Name = name,
            Text = text,
            Position = position,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            FontSize = 48,
            PixelSize = 0.01f,
            OutlineSize = 8,
            Modulate = new Color(0.92f, 0.95f, 1.0f),
            OutlineModulate = new Color(0.08f, 0.1f, 0.12f),
            Shaded = false,
        };
    }

    private static float GetGridLineThickness(int coordinateMeters)
    {
        if (coordinateMeters % 10 == 0)
        {
            return BaseGridLineThickness * 3.0f;
        }

        if (coordinateMeters % 5 == 0)
        {
            return BaseGridLineThickness * 2.0f;
        }

        return BaseGridLineThickness;
    }

    private static Node3D CreateMarkings(
        IEnumerable<MarkingDto> markings,
        string rootName,
        SurfaceProjectionService projection)
    {
        var root = new Node3D { Name = rootName };

        foreach (MarkingDto marking in markings)
        {
            if (!marking.VisibleInViewer)
            {
                // Hidden markings remain in JSON and editor data, but have no
                // runtime visual node by explicit exported-track instruction.
                continue;
            }

            string type = marking.Type.ToLowerInvariant();
            if (type is not ("line" or "polyline"))
            {
                // Geometry for future contract types is intentionally left undefined.
                GD.PushWarning($"Marking '{marking.Id}' has unsupported type '{marking.Type}' and was skipped.");
                continue;
            }

            string style = marking.Style;
            if (!MarkingGeometry.IsSupportedStyle(style))
            {
                GD.PushWarning(
                    $"Marking '{marking.Id}' has unsupported style '{marking.Style}'; solid fallback was used.");
                style = "solid";
            }

            var material = new StandardMaterial3D
            {
                AlbedoColor = ResolveMarkingColor(marking.Color),
                Roughness = 0.85f,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            };

            var markingRoot = new Node3D
            {
                Name = string.IsNullOrWhiteSpace(marking.Id) ? "Marking" : marking.Id,
            };

            int strokeIndex = 0;
            foreach (MarkingStroke stroke in MarkingGeometry.CreateStrokes(marking.Points, style))
            {
                ProjectedSurfacePoint[] projected = projection.ProjectPolyline(
                    [stroke.Start, stroke.End],
                    "Marking",
                    marking.Id,
                    MaximumProjectionSpacingMeters,
                    MarkingSurfaceOffset);
                var strokeRoot = new Node3D { Name = $"Stroke_{strokeIndex++}" };
                AddProjectedPathSegments(
                    strokeRoot,
                    projected,
                    marking.WidthMeters,
                    material);
                markingRoot.AddChild(strokeRoot);
            }

            root.AddChild(markingRoot);
        }

        return root;
    }

    private static Node3D CreateTrajectory(
        TrajectoryDto trajectory,
        SurfaceProjectionService projection)
    {
        var root = new Node3D { Name = "Trajectory" };
        var material = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.1f, 0.9f, 0.95f),
            Roughness = 0.8f,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };

        Point2Dto? previousEnd = null;
        string? previousSegmentId = null;

        for (int index = 0; index < trajectory.Segments.Length; index++)
        {
            TrajectorySegmentDto segment = trajectory.Segments[index];
            IReadOnlyList<Point2Dto> renderPoints;

            switch (segment.Type)
            {
                case "polyline":
                    renderPoints = segment.Points!;
                    break;

                case "cubicBezier":
                    renderPoints = DiscretizeCubicBezier(segment);
                    break;

                default:
                    GD.PushWarning(
                        $"Trajectory segment '{segment.Id}' has unsupported type " +
                        $"'{segment.Type}' and was skipped.");
                    // An unknown segment may bridge the surrounding known segments,
                    // so continuity cannot be assessed across it without guessing.
                    previousEnd = null;
                    previousSegmentId = null;
                    continue;
            }

            string segmentId = string.IsNullOrWhiteSpace(segment.Id)
                ? $"TrajectorySegment_{index}"
                : segment.Id;
            WarnIfTrajectoryDiscontinuous(
                previousSegmentId,
                previousEnd,
                segmentId,
                renderPoints[0]);

            var segmentRoot = new Node3D { Name = segmentId };
            ProjectedSurfacePoint[] projected = projection.ProjectPolyline(
                renderPoints,
                "TrajectorySegment",
                segmentId,
                MaximumProjectionSpacingMeters,
                TrajectorySurfaceOffset);
            AddProjectedPathSegments(
                segmentRoot,
                projected,
                TrajectoryWidth,
                material);
            AddDirectionMarkers(segmentRoot, projected, material);
            root.AddChild(segmentRoot);

            previousEnd = renderPoints[^1];
            previousSegmentId = segmentId;
        }

        return root;
    }

    private static void AddDirectionMarkers(
        Node3D parent,
        IReadOnlyList<ProjectedSurfacePoint> points,
        Material material)
    {
        var markersRoot = new Node3D { Name = "DirectionMarkers" };
        float distanceToNextMarker = TrajectoryDirectionMarkerIntervalMeters / 2.0f;
        int markerIndex = 0;

        /*
         * Markers are spaced by travelled distance through the temporary render
         * polyline. This works for both native polylines and sampled Bezier curves
         * without changing either DTO or the exported spline representation.
         */
        for (int pointIndex = 0; pointIndex < points.Count - 1; pointIndex++)
        {
            Vector3 start = points[pointIndex].Position;
            Vector3 end = points[pointIndex + 1].Position;
            Vector3 delta = end - start;
            float segmentLength = delta.Length();

            if (segmentLength <= Mathf.Epsilon)
            {
                continue;
            }

            Vector3 direction = delta / segmentLength;
            float distanceAlongSegment = distanceToNextMarker;

            while (distanceAlongSegment <= segmentLength)
            {
                float amount = distanceAlongSegment / segmentLength;
                Vector3 markerPosition = start + direction * distanceAlongSegment;
                Vector3 surfaceNormal = points[pointIndex].Normal.Lerp(
                    points[pointIndex + 1].Normal,
                    amount).Normalized();
                markersRoot.AddChild(CreateDirectionMarker(
                    markerIndex++,
                    markerPosition,
                    direction,
                    surfaceNormal,
                    material));
                distanceAlongSegment += TrajectoryDirectionMarkerIntervalMeters;
            }

            distanceToNextMarker = distanceAlongSegment - segmentLength;
        }

        parent.AddChild(markersRoot);
    }

    private static Node3D CreateDirectionMarker(
        int index,
        Vector3 center,
        Vector3 direction,
        Vector3 surfaceNormal,
        Material material)
    {
        var marker = new Node3D { Name = $"Direction_{index}" };
        Vector3 tangent = direction.Normalized();
        Vector3 normal = surfaceNormal.Normalized();
        Vector3 perpendicular = normal.Cross(tangent).Normalized();
        Vector3 offset = normal * DirectionMarkerSurfaceOffset;
        Vector3 tip = center + tangent * (DirectionMarkerLength / 2.0f) + offset;
        Vector3 tailCenter = center - tangent * (DirectionMarkerLength / 2.0f) + offset;
        Vector3 leftTail = tailCenter + perpendicular * DirectionMarkerHalfWidth;
        Vector3 rightTail = tailCenter - perpendicular * DirectionMarkerHalfWidth;

        // The chevron uses the projected 3D tangent and surface normal, so both
        // position and pitch follow ramps rather than remaining horizontal.
        MeshInstance3D? leftStroke = CreateProjectedPathSegment(
            "LeftStroke",
            leftTail,
            tip,
            normal,
            normal,
            DirectionMarkerLineWidth,
            material);
        MeshInstance3D? rightStroke = CreateProjectedPathSegment(
            "RightStroke",
            rightTail,
            tip,
            normal,
            normal,
            DirectionMarkerLineWidth,
            material);

        if (leftStroke is not null)
        {
            marker.AddChild(leftStroke);
        }

        if (rightStroke is not null)
        {
            marker.AddChild(rightStroke);
        }

        return marker;
    }

    private static Point2Dto[] DiscretizeCubicBezier(TrajectorySegmentDto segment)
    {
        // Sampling is shared with the Exercise Editor. The DTO retains only the
        // canonical four Bezier points from the persisted JSON contract.
        return TrajectoryGeometry.SampleCubicBezier(segment, CubicBezierSubdivisionCount);
    }

    private static void WarnIfTrajectoryDiscontinuous(
        string? previousSegmentId,
        Point2Dto? previousEnd,
        string currentSegmentId,
        Point2Dto currentStart)
    {
        if (previousEnd is null || previousSegmentId is null)
        {
            return;
        }

        float deltaX = currentStart.X - previousEnd.X;
        float deltaY = currentStart.Y - previousEnd.Y;
        float gapMeters = MathF.Sqrt(deltaX * deltaX + deltaY * deltaY);
        if (gapMeters > TrajectoryContinuityToleranceMeters)
        {
            GD.PushWarning(
                $"Trajectory discontinuity between '{previousSegmentId}' and " +
                $"'{currentSegmentId}': gap is {gapMeters:F3} m. Geometry was not modified.");
        }
    }

    private static void AddProjectedPathSegments(
        Node3D parent,
        IReadOnlyList<ProjectedSurfacePoint> points,
        float width,
        Material material)
    {
        for (int index = 0; index < points.Count - 1; index++)
        {
            MeshInstance3D? segment = CreateProjectedPathSegment(
                $"Segment_{index}",
                points[index].Position,
                points[index + 1].Position,
                points[index].Normal,
                points[index + 1].Normal,
                width,
                material);

            if (segment is not null)
            {
                parent.AddChild(segment);
            }
        }
    }

    private static MeshInstance3D? CreateProjectedPathSegment(
        string name,
        Vector3 start,
        Vector3 end,
        Vector3 startNormal,
        Vector3 endNormal,
        float width,
        Material material)
    {
        Vector3 direction = end - start;
        float length = direction.Length();

        if (length <= Mathf.Epsilon)
        {
            return null;
        }

        Vector3 forward = direction / length;
        Vector3 normal = (startNormal + endNormal).Normalized();
        if (normal.LengthSquared() <= 0.00001f ||
            MathF.Abs(normal.Dot(forward)) > 0.98f)
            normal = Vector3.Up;
        Vector3 right = normal.Cross(forward).Normalized();
        Vector3 adjustedNormal = forward.Cross(right).Normalized();
        return new MeshInstance3D
        {
            Name = name,
            Mesh = new BoxMesh
            {
                Size = new Vector3(width, PathSegmentHeight, length),
                Material = material,
            },
            Position = (start + end) / 2.0f,
            Basis = new Basis(right, adjustedNormal, forward),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }

    private static Node3D CreateCone(
        ConeDto cone,
        PackedScene coneModel,
        SurfaceProjectionService projection)
    {
        var root = new Node3D
        {
            Name = string.IsNullOrWhiteSpace(cone.Id) ? "Cone" : cone.Id,
            Position = projection.ProjectConePosition(cone.Position, cone.Id),
        };

        Node3D model = coneModel.Instantiate<Node3D>();
        model.Name = "TrafficConeModel";
        // Cone visuals are intentionally non-blocking in this iteration.
        DisableCollisionRecursively(model);
        root.AddChild(model);

        // "none" deliberately means the authored traffic-cone model stands on
        // its own. No invisible placeholder is created, so runtime scene state
        // also clearly reflects the absence of a navigation-color topper.
        if (string.Equals(cone.Color, "none", StringComparison.OrdinalIgnoreCase))
        {
            return root;
        }

        var topperMaterial = new StandardMaterial3D
        {
            AlbedoColor = ResolveConeColor(cone.Color),
            Roughness = 0.75f,
        };

        // Track color is a navigation marker. It belongs only to the small topper;
        // the traffic cone keeps the authored materials and textures from the GLB.
        root.AddChild(new MeshInstance3D
        {
            Name = "ColorTopper",
            Mesh = new CylinderMesh
            {
                TopRadius = 0.008f,
                BottomRadius = 0.06f,
                Height = TopperHeight,
                RadialSegments = 20,
                Material = topperMaterial,
            },
            Position = new Vector3(0.0f, ConeModelTopHeight + TopperHeight / 2.0f, 0.0f),
        });

        return root;
    }

    private static Color ResolveConeColor(string color)
    {
        return color.ToLowerInvariant() switch
        {
            "red" => new Color(0.88f, 0.08f, 0.05f),
            "blue" => new Color(0.05f, 0.25f, 0.9f),
            "yellow" => new Color(1.0f, 0.7f, 0.05f),
            _ => new Color(1.0f, 0.35f, 0.03f),
        };
    }

    private static Color ResolveMarkingColor(string color)
    {
        if (MarkingGeometry.TryNormalizeColor(color, allowLegacyNames: true, out string canonical))
        {
            return new Color(
                Convert.ToByte(canonical.Substring(1, 2), 16) / 255.0f,
                Convert.ToByte(canonical.Substring(3, 2), 16) / 255.0f,
                Convert.ToByte(canonical.Substring(5, 2), 16) / 255.0f);
        }

        GD.PushWarning($"Unknown marking color '{color}'; white fallback was used.");
        return Colors.White;
    }
}
