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
    private const float MarkingElevation = 0.012f;
    private const float TrajectoryElevation = 0.024f;
    private const float PathSegmentHeight = 0.008f;
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
    private CheckBox? _trajectoryToggle;
    private FileDialog? _trackFileDialog;
    private SandboxedJsonLibrary? _trackExportLibrary;
    private bool _trajectoryVisible = true;

    /// <inheritdoc />
    public override void _Ready()
    {
        _trackExportLibrary = new SandboxedJsonLibrary(
            ProjectSettings.GlobalizePath(TrackExportRoot),
            "Viewer track export library",
            $"{TrackExportRoot}/");
        CreateViewerUi();

        try
        {
            _coneModel = LoadConeModel(ConeModelPath);
        }
        catch (Exception exception)
        {
            ReportLoadFailure(ConeModelPath, exception);
            return;
        }

        SetStatus("Select an exported Track JSON file.", new Color(0.85f, 0.85f, 0.85f));
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

        layout.AddChild(_trackNameLabel);
        layout.AddChild(openTrackButton);
        layout.AddChild(_trajectoryToggle);
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

    private void OnTrackFileSelected(string path)
    {
        try
        {
            string filesystemPath = path.StartsWith("res://", StringComparison.OrdinalIgnoreCase)
                ? ProjectSettings.GlobalizePath(path)
                : path;
            string safePath = _trackExportLibrary!.ResolveExistingJson(filesystemPath);
            TryReplaceTrack(safePath);
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

    private bool TryReplaceTrack(string path)
    {
        try
        {
            /*
             * Loading and contract validation happen before any live node changes.
             * Building the candidate off-tree also ensures an asset/render failure
             * cannot partially dismantle the currently visible valid track.
             */
            TrackSnapshotDto track = LoadTrack(path);
            if (!TrajectoryGeometry.TryGetEntryPose(
                    track.Trajectory,
                    out Point2Dto trajectoryStart,
                    out Point2Dto trajectoryDirection))
            {
                throw new InvalidDataException(
                    "The first renderable trajectory segment has no valid entry direction for camera placement.");
            }

            PackedScene coneModel = _coneModel ?? throw new InvalidOperationException(
                "The traffic cone model is not available.");
            Node3D candidate = CreateRuntimeTrack(track, coneModel);

            ReplaceRuntimeTrack(candidate);
            GetNode<FirstPersonCamera>("../CameraRig").PlaceAtTrajectoryStart(
                trajectoryStart,
                trajectoryDirection);
            string displayName = string.IsNullOrWhiteSpace(track.Track.Name)
                ? track.Track.Id
                : track.Track.Name;
            _trackNameLabel!.Text = displayName;
            SetStatus(
                $"Loaded '{displayName}' from {Path.GetFileName(path)}.",
                new Color(0.55f, 0.95f, 0.62f));

            GD.Print(
                $"Loaded track '{displayName}' with {track.Cones.Length} cones, " +
                $"{track.Markings.Length} markings and {track.Trajectory.Segments.Length} trajectory segments.");
            return true;
        }
        catch (Exception exception)
        {
            ReportLoadFailure(path, exception);
            return false;
        }
    }

    private static Node3D CreateRuntimeTrack(TrackSnapshotDto track, PackedScene coneModel)
    {
        var root = new Node3D { Name = "RuntimeTrackCandidate" };
        root.AddChild(CreateGround(track.Area));
        root.AddChild(CreateGridOverlay(track.Area));
        root.AddChild(CreateMarkings(track.Markings));
        root.AddChild(CreateTrajectory(track.Trajectory));

        foreach (ConeDto cone in track.Cones)
        {
            root.AddChild(CreateCone(cone, coneModel));
        }

        return root;
    }

    private void ReplaceRuntimeTrack(Node3D candidate)
    {
        Node3D newGridOverlay = candidate.GetNode<Node3D>("GridOverlay");
        Node3D newTrajectoryOverlay = candidate.GetNode<Node3D>("Trajectory");

        // Attach the complete candidate before removing the old root. No frame is
        // rendered between these synchronous operations, so replacement is atomic.
        AddChild(candidate);
        Node3D? previousRoot = _runtimeTrackRoot;
        if (previousRoot is not null)
        {
            RemoveChild(previousRoot);
            previousRoot.Free();
        }

        candidate.Name = "RuntimeTrack";
        _runtimeTrackRoot = candidate;
        _gridOverlay = newGridOverlay;
        _trajectoryOverlay = newTrajectoryOverlay;
        SetTrajectoryVisible(_trajectoryVisible);
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

    private static MeshInstance3D CreateGround(AreaDto area)
    {
        var groundMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.24f, 0.27f, 0.29f),
            Roughness = 0.95f,
        };

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

        return ground;
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

    private static Node3D CreateMarkings(IEnumerable<MarkingDto> markings)
    {
        var root = new Node3D { Name = "Markings" };

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
                MeshInstance3D? visual = CreatePathSegment(
                    $"Stroke_{strokeIndex++}",
                    stroke.Start,
                    stroke.End,
                    marking.WidthMeters,
                    MarkingElevation,
                    material);
                if (visual is not null)
                {
                    markingRoot.AddChild(visual);
                }
            }

            root.AddChild(markingRoot);
        }

        return root;
    }

    private static Node3D CreateTrajectory(TrajectoryDto trajectory)
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
            AddPathSegments(
                segmentRoot,
                renderPoints,
                TrajectoryWidth,
                TrajectoryElevation,
                material);
            AddDirectionMarkers(segmentRoot, renderPoints, material);
            root.AddChild(segmentRoot);

            previousEnd = renderPoints[^1];
            previousSegmentId = segmentId;
        }

        return root;
    }

    private static void AddDirectionMarkers(
        Node3D parent,
        IReadOnlyList<Point2Dto> points,
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
            Point2Dto startPoint = points[pointIndex];
            Point2Dto endPoint = points[pointIndex + 1];
            var start = new Vector2(startPoint.X, startPoint.Y);
            var end = new Vector2(endPoint.X, endPoint.Y);
            Vector2 delta = end - start;
            float segmentLength = delta.Length();

            if (segmentLength <= Mathf.Epsilon)
            {
                continue;
            }

            Vector2 direction = delta / segmentLength;
            float distanceAlongSegment = distanceToNextMarker;

            while (distanceAlongSegment <= segmentLength)
            {
                Vector2 markerPosition = start + direction * distanceAlongSegment;
                markersRoot.AddChild(CreateDirectionMarker(
                    markerIndex++,
                    markerPosition,
                    direction,
                    material));
                distanceAlongSegment += TrajectoryDirectionMarkerIntervalMeters;
            }

            distanceToNextMarker = distanceAlongSegment - segmentLength;
        }

        parent.AddChild(markersRoot);
    }

    private static Node3D CreateDirectionMarker(
        int index,
        Vector2 center,
        Vector2 direction,
        Material material)
    {
        var marker = new Node3D { Name = $"Direction_{index}" };
        Vector2 perpendicular = new(-direction.Y, direction.X);
        Vector2 tip = center + direction * (DirectionMarkerLength / 2.0f);
        Vector2 tailCenter = center - direction * (DirectionMarkerLength / 2.0f);
        Vector2 leftTail = tailCenter + perpendicular * DirectionMarkerHalfWidth;
        Vector2 rightTail = tailCenter - perpendicular * DirectionMarkerHalfWidth;
        float elevation = TrajectoryElevation + PathSegmentHeight;

        // A shallow two-stroke chevron remains readable from both overhead and
        // low camera angles while preserving the physical trajectory underneath.
        MeshInstance3D? leftStroke = CreatePathSegment(
            "LeftStroke",
            ToDomainPoint(leftTail),
            ToDomainPoint(tip),
            DirectionMarkerLineWidth,
            elevation,
            material);
        MeshInstance3D? rightStroke = CreatePathSegment(
            "RightStroke",
            ToDomainPoint(rightTail),
            ToDomainPoint(tip),
            DirectionMarkerLineWidth,
            elevation,
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

    private static Point2Dto ToDomainPoint(Vector2 point)
    {
        return new Point2Dto { X = point.X, Y = point.Y };
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

    private static void AddPathSegments(
        Node3D parent,
        IReadOnlyList<Point2Dto> points,
        float width,
        float elevation,
        Material material)
    {
        for (int index = 0; index < points.Count - 1; index++)
        {
            MeshInstance3D? segment = CreatePathSegment(
                $"Segment_{index}",
                points[index],
                points[index + 1],
                width,
                elevation,
                material);

            if (segment is not null)
            {
                parent.AddChild(segment);
            }
        }
    }

    private static MeshInstance3D? CreatePathSegment(
        string name,
        Point2Dto start,
        Point2Dto end,
        float width,
        float elevation,
        Material material)
    {
        Vector3 startPosition = DomainCoordinateMapper.ToGodot(start, elevation);
        Vector3 endPosition = DomainCoordinateMapper.ToGodot(end, elevation);
        Vector3 direction = endPosition - startPosition;
        float length = direction.Length();

        if (length <= Mathf.Epsilon)
        {
            return null;
        }

        // BoxMesh extends along local Z. This yaw aligns that axis with the
        // resolved world-space segment while keeping the strip flat on the area.
        float yaw = Mathf.Atan2(direction.X, direction.Z);
        return new MeshInstance3D
        {
            Name = name,
            Mesh = new BoxMesh
            {
                Size = new Vector3(width, PathSegmentHeight, length),
                Material = material,
            },
            Position = (startPosition + endPosition) / 2.0f,
            Rotation = new Vector3(0.0f, yaw, 0.0f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }

    private static Node3D CreateCone(ConeDto cone, PackedScene coneModel)
    {
        var root = new Node3D
        {
            Name = string.IsNullOrWhiteSpace(cone.Id) ? "Cone" : cone.Id,
            Position = DomainCoordinateMapper.ToGodot(cone.Position),
        };

        Node3D model = coneModel.Instantiate<Node3D>();
        model.Name = "TrafficConeModel";
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
