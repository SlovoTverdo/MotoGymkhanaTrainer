using MotoGymkhanaTrainer.Tracks;

namespace MotoGymkhanaTrainer.ExerciseEditor;

/// <summary>Identifies one rendered section between adjacent trajectory anchors.</summary>
public readonly record struct TrajectorySectionLocation(int SegmentIndex, int SectionIndex);

/// <summary>Editable control point of a cubic Bezier segment.</summary>
public enum BezierControlKind
{
    Control1,
    Control2,
}

/// <summary>Domain outcome of deleting a trajectory anchor.</summary>
public enum TrajectoryAnchorDeleteResult
{
    Deleted,
    MinimumBlocked,
    InvalidSelection,
}

/// <summary>
/// Owns the editable Exercise Definition. Canvas-only state such as pan, zoom,
/// active tool, construction progress and selection is intentionally not stored here.
/// </summary>
public sealed class ExerciseDocument
{
    private const string DefaultTrajectoryId = "trajectory-segment-001";

    private enum AnchorOccurrenceKind
    {
        PolylinePoint,
        BezierStart,
        BezierEnd,
    }

    private readonly record struct AnchorOccurrence(
        int SegmentIndex,
        AnchorOccurrenceKind Kind,
        int PointIndex = -1);

    /// <summary>Creates a document around already validated domain data.</summary>
    public ExerciseDocument(ExerciseDefinitionDto definition)
    {
        Definition = definition;
        SynchronizeEndpointsFromTrajectory();
    }

    /// <summary>The single source of truth for serializable exercise data.</summary>
    public ExerciseDefinitionDto Definition { get; }

    /// <summary>Number of conceptual anchors across all ordered segments.</summary>
    public int TrajectoryPointCount => BuildAnchorBindings().Count;

    /// <summary>Number of persisted trajectory segments.</summary>
    public int TrajectorySegmentCount => Definition.Trajectory.Segments.Length;

    /// <summary>Creates the default valid two-point exercise used by New.</summary>
    public static ExerciseDocument CreateNew(
        string id = "new-exercise",
        string name = "New Exercise",
        float width = 10.0f,
        float length = 10.0f)
    {
        Point2Dto entry = new() { X = 0.0f, Y = -length * 0.35f };
        Point2Dto exit = new() { X = 0.0f, Y = length * 0.35f };
        var definition = new ExerciseDefinitionDto
        {
            Exercise = new ExerciseMetadataDto { Id = id, Name = name, Version = 1 },
            Bounds = new ExerciseBoundsDto { Width = width, Length = length },
            EntryPoint = CopyPoint(entry),
            ExitPoint = CopyPoint(exit),
            Trajectory = CreateSinglePolyline(DefaultTrajectoryId, [entry, exit]),
        };

        return new ExerciseDocument(definition);
    }

    /// <summary>Adds a standard red cone and returns its generated stable id.</summary>
    public string AddCone(Point2Dto position)
    {
        string id = CreateUniqueConeId();
        var cones = Definition.Cones.ToList();
        cones.Add(new ConeDto
        {
            Id = id,
            Position = CopyPoint(position),
            Color = "red",
            Type = "standard",
        });
        Definition.Cones = [.. cones];
        return id;
    }

    /// <summary>Moves a cone without applying a transform to any other object.</summary>
    public bool MoveCone(string id, Point2Dto position)
    {
        int index = FindConeIndex(id);
        if (index < 0)
        {
            return false;
        }

        ConeDto cone = Definition.Cones[index];
        Definition.Cones[index] = CopyCone(cone, position: position);
        return true;
    }

    /// <summary>Changes the navigation color of one cone.</summary>
    public bool SetConeColor(string id, string color)
    {
        int index = FindConeIndex(id);
        if (index < 0)
        {
            return false;
        }

        ConeDto cone = Definition.Cones[index];
        Definition.Cones[index] = CopyCone(cone, color: color);
        return true;
    }

    /// <summary>Removes one cone by id.</summary>
    public bool DeleteCone(string id)
    {
        int index = FindConeIndex(id);
        if (index < 0)
        {
            return false;
        }

        var cones = Definition.Cones.ToList();
        cones.RemoveAt(index);
        Definition.Cones = [.. cones];
        return true;
    }

    /// <summary>Returns a cone by id, or <see langword="null"/> if it no longer exists.</summary>
    public ConeDto? FindCone(string id) => Definition.Cones.FirstOrDefault(cone => cone.Id == id);

    /// <summary>
    /// Returns the cone already occupying a snapped local position. The small
    /// tolerance protects the editor from harmless float serialization noise.
    /// </summary>
    public ConeDto? FindConeAt(Point2Dto position, float toleranceMeters = 0.0001f) =>
        Definition.Cones.LastOrDefault(cone =>
            MathF.Abs(cone.Position.X - position.X) <= toleranceMeters &&
            MathF.Abs(cone.Position.Y - position.Y) <= toleranceMeters);

    /// <summary>Adds a valid line/polyline and returns its deterministic id.</summary>
    public string AddMarking(string type, IEnumerable<Point2Dto> points)
    {
        Point2Dto[] copiedPoints = points.Select(CopyPoint).ToArray();
        if (type is not ("line" or "polyline") || copiedPoints.Length < 2 ||
            (type == "line" && copiedPoints.Length != 2))
        {
            throw new ArgumentException("A marking must be line/polyline with valid point count.", nameof(points));
        }

        string id = CreateUniqueMarkingId();
        var markings = Definition.Markings.ToList();
        markings.Add(new MarkingDto
        {
            Id = id,
            Path = PathEditing.FromPolyline(copiedPoints),
            Color = "#FFD10D",
            WidthMeters = 0.08f,
            Style = "solid",
            VisibleInViewer = true,
        });
        Definition.Markings = [.. markings];
        return id;
    }

    /// <summary>Adds a marking only when its Path already contains usable geometry.</summary>
    public string AddMarking(PathDefinition path)
    {
        IReadOnlyList<string> errors = PathValidator.Validate(path, "marking.path");
        if (errors.Count > 0)
            throw new ArgumentException(string.Join(" ", errors), nameof(path));

        string id = CreateUniqueMarkingId();
        Definition.Markings =
        [
            .. Definition.Markings,
            new MarkingDto
            {
                Id = id,
                Path = PathEditing.CopyPath(path),
                Color = "#FFD10D",
                WidthMeters = 0.08f,
                Style = "solid",
                VisibleInViewer = true,
            },
        ];
        return id;
    }

    /// <summary>Returns one marking by id without creating canvas geometry.</summary>
    public MarkingDto? FindMarking(string id) =>
        Definition.Markings.FirstOrDefault(marking => marking.Id == id);

    /// <summary>Moves one persisted marking anchor in local exercise metres.</summary>
    public bool MoveMarkingPoint(string id, int pointIndex, Point2Dto position)
    {
        MarkingDto? marking = FindMarking(id);
        return marking is not null && PathEditing.TryMoveVertex(marking.Path, pointIndex, position);
    }

    /// <summary>Moves one addressed Path coordinate; adjacent starts remain implicit.</summary>
    public bool MoveMarkingCoordinate(
        string id,
        int segmentIndex,
        MarkingPathCoordinateKind kind,
        Point2Dto position)
    {
        MarkingDto? marking = FindMarking(id);
        if (marking is null) return false;
        PathDefinition candidate = PathEditing.CopyPath(marking.Path);
        if (!PathEditing.MoveCoordinate(candidate, segmentIndex, kind, position) ||
            PathValidator.Validate(candidate, $"marking '{id}'.path").Count > 0) return false;
        marking.Path = candidate;
        return true;
    }

    /// <summary>Replaces a marking path with a translated deep copy.</summary>
    public bool MoveMarking(string id, float deltaX, float deltaY)
    {
        MarkingDto? marking = FindMarking(id);
        if (marking is null || !float.IsFinite(deltaX) || !float.IsFinite(deltaY)) return false;
        marking.Path = PathEditing.Translate(marking.Path, deltaX, deltaY);
        return true;
    }

    /// <summary>Restores one immutable Path snapshot during drag cancellation.</summary>
    public bool ReplaceMarkingPath(string id, PathDefinition path)
    {
        MarkingDto? marking = FindMarking(id);
        if (marking is null) return false;
        marking.Path = PathEditing.CopyPath(path);
        return true;
    }

    /// <summary>Appends one line or initially straight cubic and returns its segment index.</summary>
    public int AppendMarkingSegment(string id, Point2Dto end, bool cubic)
    {
        MarkingDto? marking = FindMarking(id);
        if (marking is null) return -1;
        return cubic ? PathEditing.AppendCubic(marking.Path, end) : PathEditing.AppendLine(marking.Path, end);
    }

    /// <summary>Explicitly converts one marking segment while retaining its index.</summary>
    public bool ConvertMarkingSegment(string id, int segmentIndex, bool toCubic)
    {
        MarkingDto? marking = FindMarking(id);
        return marking is not null && (toCubic
            ? PathEditing.ConvertLineToCubic(marking.Path, segmentIndex)
            : PathEditing.ConvertCubicToLine(marking.Path, segmentIndex));
    }

    /// <summary>Splits one marking segment at an interior parameter.</summary>
    public bool SplitMarkingSegment(string id, int segmentIndex, float parameter)
    {
        MarkingDto? marking = FindMarking(id);
        return marking is not null && PathEditing.SplitSegment(marking.Path, segmentIndex, parameter);
    }

    /// <summary>
    /// Deletes a segment. The only segment removes its owning marking so an empty
    /// serialized Path can never remain in the document.
    /// </summary>
    public bool DeleteMarkingSegment(string id, int segmentIndex, out bool markingDeleted)
    {
        markingDeleted = false;
        MarkingDto? marking = FindMarking(id);
        if (marking is null || (uint)segmentIndex >= (uint)marking.Path.Segments.Length) return false;
        if (marking.Path.Segments.Length == 1)
        {
            markingDeleted = DeleteMarking(id);
            return markingDeleted;
        }

        return PathEditing.DeleteSegment(marking.Path, segmentIndex);
    }

    /// <summary>Adds a point to a polyline under construction.</summary>
    public int AppendMarkingPoint(string id, Point2Dto position)
    {
        MarkingDto? marking = FindMarking(id);
        if (marking is null || !PathEditing.IsAllLine(marking.Path))
        {
            return -1;
        }

        return PathEditing.AppendVertex(marking.Path, position);
    }

    /// <summary>Inserts a midpoint after an existing polyline point.</summary>
    public int InsertMarkingPointAfter(string id, int pointIndex)
    {
        MarkingDto? marking = FindMarking(id);
        return marking is null ? -1 : PathEditing.InsertVertexAfter(marking.Path, pointIndex);
    }

    /// <summary>Deletes only an internal polyline point and preserves the two-point minimum.</summary>
    public bool DeleteMarkingPoint(string id, int pointIndex)
    {
        MarkingDto? marking = FindMarking(id);
        return marking is not null && PathEditing.DeleteInternalVertex(marking.Path, pointIndex);
    }

    /// <summary>Removes one complete marking.</summary>
    public bool DeleteMarking(string id)
    {
        int index = Array.FindIndex(Definition.Markings, marking => marking.Id == id);
        if (index < 0)
        {
            return false;
        }

        var markings = Definition.Markings.ToList();
        markings.RemoveAt(index);
        Definition.Markings = [.. markings];
        return true;
    }

    /// <summary>Applies validated marking presentation properties without touching geometry.</summary>
    public bool SetMarkingProperties(string id, string color, float widthMeters, string style, bool visibleInViewer)
    {
        MarkingDto? marking = FindMarking(id);
        if (marking is null || widthMeters <= 0.0f || !float.IsFinite(widthMeters) ||
            !MarkingGeometry.TryNormalizeColor(color, allowLegacyNames: false, out string canonical) ||
            !MarkingGeometry.IsSupportedStyle(style))
        {
            return false;
        }

        marking.Color = canonical;
        marking.WidthMeters = widthMeters;
        marking.Style = style;
        marking.VisibleInViewer = visibleInViewer;
        return true;
    }

    /// <summary>Returns one conceptual trajectory anchor in local X/Y metres.</summary>
    public Point2Dto GetTrajectoryPoint(int index)
    {
        List<List<AnchorOccurrence>> bindings = BuildAnchorBindings();
        if (index < 0 || index >= bindings.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return CopyPoint(GetOccurrencePoint(bindings[index][0]));
    }

    /// <summary>Returns a persisted segment without creating canvas geometry.</summary>
    public TrajectorySegmentDto GetTrajectorySegment(int segmentIndex)
    {
        return Definition.Trajectory.Segments[segmentIndex];
    }

    /// <summary>Returns every ordered section; a polyline contributes one per point pair.</summary>
    public IReadOnlyList<TrajectorySectionLocation> GetTrajectorySections()
    {
        var sections = new List<TrajectorySectionLocation>();
        for (int segmentIndex = 0; segmentIndex < Definition.Trajectory.Segments.Length; segmentIndex++)
        {
            TrajectorySegmentDto segment = Definition.Trajectory.Segments[segmentIndex];
            int count = segment.Type == "polyline" ? segment.Points!.Length - 1 : 1;
            for (int sectionIndex = 0; sectionIndex < count; sectionIndex++)
            {
                sections.Add(new TrajectorySectionLocation(segmentIndex, sectionIndex));
            }
        }

        return sections;
    }

    /// <summary>Returns the two persisted anchors of one selected section.</summary>
    public (Point2Dto Start, Point2Dto End) GetSectionEndpoints(TrajectorySectionLocation location)
    {
        TrajectorySegmentDto segment = GetTrajectorySegment(location.SegmentIndex);
        return segment.Type == "polyline"
            ? (segment.Points![location.SectionIndex], segment.Points[location.SectionIndex + 1])
            : (segment.Start!, segment.End!);
    }

    /// <summary>
    /// Starts the legacy/programmatic replacement polyline. Existing polyline
    /// editing callers retain this operation while the interactive builder uses
    /// <see cref="StartSplineTrajectoryAt"/> for its new default.
    /// </summary>
    public void StartTrajectoryAt(Point2Dto position)
    {
        string id = Definition.Trajectory.Segments.FirstOrDefault()?.Id ?? DefaultTrajectoryId;
        Definition.Trajectory = CreateSinglePolyline(id, [position, position]);
        SynchronizeEndpointsFromTrajectory();
    }

    /// <summary>
    /// Starts interactive replacement construction with a temporarily degenerate
    /// cubic. Saving remains blocked until the second click supplies its real end.
    /// </summary>
    public void StartSplineTrajectoryAt(Point2Dto position)
    {
        string id = Definition.Trajectory.Segments.FirstOrDefault()?.Id ?? DefaultTrajectoryId;
        Definition.Trajectory = new TrajectoryDto
        {
            Segments = [CreateStraightCubic(id, position, position)],
        };
        SynchronizeEndpointsFromTrajectory();
    }

    /// <summary>Completes the first construction spline as a straight cubic.</summary>
    public int SetInitialTrajectorySplineEnd(Point2Dto position)
    {
        TrajectorySegmentDto segment = Definition.Trajectory.Segments[0];
        if (Definition.Trajectory.Segments.Length != 1 || segment.Type != "cubicBezier")
        {
            throw new InvalidOperationException("Initial construction requires one cubicBezier segment.");
        }

        Point2Dto start = CopyPoint(segment.Start!);
        Definition.Trajectory.Segments[0] = CreateStraightCubic(segment.Id, start, position);
        SynchronizeEndpointsFromTrajectory();
        return 1;
    }

    /// <summary>
    /// Appends a cubic whose first derivative matches the preceding segment.
    /// Mirroring the previous outgoing handle through the shared anchor gives
    /// C1 continuity for adjacent cubics without a separate smooth-mode state.
    /// </summary>
    public int AppendTrajectorySpline(Point2Dto position)
    {
        TrajectorySegmentDto previous = Definition.Trajectory.Segments[^1];
        Point2Dto start = CopyPoint(GetSegmentEnd(previous));
        Point2Dto outgoing = GetOutgoingTangentVector(previous);
        if (VectorLengthSquared(outgoing) <= 0.000001f)
        {
            outgoing = ScaleVector(Subtract(position, start), 1.0f / 3.0f);
        }

        var segment = new TrajectorySegmentDto
        {
            Id = CreateUniqueSegmentId(),
            Type = "cubicBezier",
            Start = CopyPoint(start),
            Control1 = Translate(start, outgoing.X, outgoing.Y),
            Control2 = Lerp(start, position, 2.0f / 3.0f),
            End = CopyPoint(position),
        };
        Definition.Trajectory.Segments = [.. Definition.Trajectory.Segments, segment];
        SynchronizeEndpointsFromTrajectory();
        return TrajectoryPointCount - 1;
    }

    /// <summary>
    /// Moves one conceptual anchor. A shared join is represented by two persisted
    /// endpoint fields, but this method is its only editing path and updates both.
    /// Bezier controls attached to the moved endpoint receive the same delta so the
    /// local curve shape does not unexpectedly change.
    /// </summary>
    public bool MoveTrajectoryPoint(int index, Point2Dto position)
    {
        List<List<AnchorOccurrence>> bindings = BuildAnchorBindings();
        if (index < 0 || index >= bindings.Count)
        {
            return false;
        }

        Point2Dto previous = GetOccurrencePoint(bindings[index][0]);
        float deltaX = position.X - previous.X;
        float deltaY = position.Y - previous.Y;
        foreach (AnchorOccurrence occurrence in bindings[index])
        {
            SetOccurrencePoint(occurrence, position, deltaX, deltaY);
        }

        SynchronizeEndpointsFromTrajectory();
        return true;
    }

    /// <summary>Adds an anchor to the replacement polyline built by the canvas.</summary>
    public int AppendTrajectoryPoint(Point2Dto position)
    {
        TrajectorySegmentDto segment = Definition.Trajectory.Segments[0];
        if (Definition.Trajectory.Segments.Length != 1 || segment.Type != "polyline")
        {
            throw new InvalidOperationException("Construction requires a single replacement polyline.");
        }

        segment.Points = [.. segment.Points!, CopyPoint(position)];
        SynchronizeEndpointsFromTrajectory();
        return segment.Points.Length - 1;
    }

    /// <summary>
    /// Inserts a midpoint after a conceptual anchor. Polyline sections receive a new
    /// point, while a cubic is split exactly at t=0.5 with de Casteljau construction.
    /// </summary>
    public int InsertTrajectoryPointAfter(int anchorIndex)
    {
        IReadOnlyList<TrajectorySectionLocation> sections = GetTrajectorySections();
        if (anchorIndex < 0 || anchorIndex >= sections.Count)
        {
            return -1;
        }

        TrajectorySectionLocation location = sections[anchorIndex];
        TrajectorySegmentDto segment = GetTrajectorySegment(location.SegmentIndex);
        if (segment.Type == "cubicBezier")
        {
            Point2Dto startControl = Lerp(segment.Start!, segment.Control1!, 0.5f);
            Point2Dto controlsMidpoint = Lerp(segment.Control1!, segment.Control2!, 0.5f);
            Point2Dto endControl = Lerp(segment.Control2!, segment.End!, 0.5f);
            Point2Dto leftControl = Lerp(startControl, controlsMidpoint, 0.5f);
            Point2Dto rightControl = Lerp(controlsMidpoint, endControl, 0.5f);
            Point2Dto midpoint = Lerp(leftControl, rightControl, 0.5f);

            string rightId = CreateUniqueSegmentId();
            ReplaceSegment(location.SegmentIndex,
            [
                CreateCubic(
                    segment.Id,
                    segment.Start!,
                    startControl,
                    leftControl,
                    midpoint),
                CreateCubic(
                    rightId,
                    midpoint,
                    rightControl,
                    endControl,
                    segment.End!),
            ]);
            SynchronizeEndpointsFromTrajectory();
            return anchorIndex + 1;
        }

        var points = segment.Points!.Select(CopyPoint).ToList();
        Point2Dto start = points[location.SectionIndex];
        Point2Dto end = points[location.SectionIndex + 1];
        points.Insert(location.SectionIndex + 1, new Point2Dto
        {
            X = (start.X + end.X) / 2.0f,
            Y = (start.Y + end.Y) / 2.0f,
        });
        segment.Points = [.. points];
        SynchronizeEndpointsFromTrajectory();
        return anchorIndex + 1;
    }

    /// <summary>
    /// Deletes an anchor while keeping the remaining ordered sections connected.
    /// Removing a join adjacent to a cubic preserves the outer endpoint derivatives
    /// over two equally weighted sections. This also makes deletion the exact inverse
    /// of the editor's midpoint split when no other edits occurred between them.
    /// </summary>
    public TrajectoryAnchorDeleteResult DeleteTrajectoryPoint(int anchorIndex)
    {
        int anchorCount = TrajectoryPointCount;
        if (anchorIndex < 0 || anchorIndex >= anchorCount)
        {
            return TrajectoryAnchorDeleteResult.InvalidSelection;
        }

        if (anchorCount <= 2)
        {
            return TrajectoryAnchorDeleteResult.MinimumBlocked;
        }

        List<List<AnchorOccurrence>> bindings = BuildAnchorBindings();
        List<AnchorOccurrence> occurrences = bindings[anchorIndex];
        if (occurrences.Count == 1)
        {
            DeleteEndpointOrInternalPolylineAnchor(occurrences[0]);
        }
        else
        {
            TrajectorySegmentDto left = GetTrajectorySegment(occurrences[0].SegmentIndex);
            TrajectorySegmentDto right = GetTrajectorySegment(occurrences[1].SegmentIndex);
            if (left.Type == "polyline" && right.Type == "polyline")
            {
                DeleteSharedPolylineAnchor(occurrences[0], occurrences[1]);
            }
            else
            {
                DeleteSharedAnchorWithCubic(occurrences[0], occurrences[1]);
            }
        }

        SynchronizeEndpointsFromTrajectory();
        return TrajectoryAnchorDeleteResult.Deleted;
    }

    /// <summary>
    /// Converts one selected straight section. A multi-point polyline is split into
    /// left/right pieces around the selected pair; pieces with fewer than two points
    /// are omitted. Every structurally created segment receives a deterministic id.
    /// </summary>
    public TrajectorySectionLocation? ConvertSectionToCubic(TrajectorySectionLocation location)
    {
        TrajectorySegmentDto source = GetTrajectorySegment(location.SegmentIndex);
        if (source.Type != "polyline" || location.SectionIndex < 0 ||
            location.SectionIndex >= source.Points!.Length - 1)
        {
            return null;
        }

        Point2Dto start = source.Points[location.SectionIndex];
        Point2Dto end = source.Points[location.SectionIndex + 1];
        var replacements = new List<TrajectorySegmentDto>();

        if (location.SectionIndex > 0)
        {
            replacements.Add(CreatePolyline(
                CreateUniqueSegmentId(),
                source.Points.Take(location.SectionIndex + 1)));
        }

        int bezierReplacementIndex = replacements.Count;
        replacements.Add(new TrajectorySegmentDto
        {
            Id = CreateUniqueSegmentId(replacements.Select(item => item.Id)),
            Type = "cubicBezier",
            Start = CopyPoint(start),
            Control1 = Lerp(start, end, 1.0f / 3.0f),
            Control2 = Lerp(start, end, 2.0f / 3.0f),
            End = CopyPoint(end),
        });

        if (location.SectionIndex + 1 < source.Points.Length - 1)
        {
            replacements.Add(CreatePolyline(
                CreateUniqueSegmentId(replacements.Select(item => item.Id)),
                source.Points.Skip(location.SectionIndex + 1)));
        }

        ReplaceSegment(location.SegmentIndex, replacements);
        SynchronizeEndpointsFromTrajectory();
        return new TrajectorySectionLocation(location.SegmentIndex + bezierReplacementIndex, 0);
    }

    /// <summary>Converts a Bezier to a line and normalizes adjacent straight segments.</summary>
    public TrajectorySectionLocation? ConvertCubicToLine(int segmentIndex)
    {
        TrajectorySegmentDto source = GetTrajectorySegment(segmentIndex);
        if (source.Type != "cubicBezier")
        {
            return null;
        }

        Point2Dto start = CopyPoint(source.Start!);
        Point2Dto end = CopyPoint(source.End!);
        ReplaceSegment(segmentIndex,
        [
            CreatePolyline(CreateUniqueSegmentId(), [start, end]),
        ]);
        NormalizeAdjacentPolylines();
        SynchronizeEndpointsFromTrajectory();
        return FindPolylineSection(start, end);
    }

    /// <summary>Moves one Bezier control handle without changing either endpoint.</summary>
    public bool MoveBezierControl(int segmentIndex, BezierControlKind control, Point2Dto position)
    {
        if (segmentIndex < 0 || segmentIndex >= TrajectorySegmentCount)
        {
            return false;
        }

        TrajectorySegmentDto segment = GetTrajectorySegment(segmentIndex);
        if (segment.Type != "cubicBezier")
        {
            return false;
        }

        if (control == BezierControlKind.Control1)
        {
            segment.Control1 = CopyPoint(position);
        }
        else
        {
            segment.Control2 = CopyPoint(position);
        }

        return true;
    }

    /// <summary>
    /// Makes serialized EntryPoint/ExitPoint projections of the first segment start
    /// and last segment end. They are never independently authored.
    /// </summary>
    public void SynchronizeEndpointsFromTrajectory()
    {
        TrajectorySegmentDto first = Definition.Trajectory.Segments[0];
        TrajectorySegmentDto last = Definition.Trajectory.Segments[^1];
        Definition.EntryPoint = CopyPoint(GetSegmentStart(first));
        Definition.ExitPoint = CopyPoint(GetSegmentEnd(last));
    }

    private List<List<AnchorOccurrence>> BuildAnchorBindings()
    {
        var bindings = new List<List<AnchorOccurrence>>();
        for (int segmentIndex = 0; segmentIndex < TrajectorySegmentCount; segmentIndex++)
        {
            TrajectorySegmentDto segment = GetTrajectorySegment(segmentIndex);
            int localAnchorCount = segment.Type == "polyline" ? segment.Points!.Length : 2;
            for (int localIndex = 0; localIndex < localAnchorCount; localIndex++)
            {
                // The start of every segment after the first is the same conceptual
                // anchor as the previous end. DTO fields are duplicated by the JSON
                // contract, but the editor exposes only this combined binding.
                if (segmentIndex == 0 || localIndex > 0)
                {
                    bindings.Add([]);
                }

                AnchorOccurrenceKind kind = segment.Type == "polyline"
                    ? AnchorOccurrenceKind.PolylinePoint
                    : localIndex == 0
                        ? AnchorOccurrenceKind.BezierStart
                        : AnchorOccurrenceKind.BezierEnd;
                bindings[^1].Add(new AnchorOccurrence(segmentIndex, kind, localIndex));
            }
        }

        return bindings;
    }

    private Point2Dto GetOccurrencePoint(AnchorOccurrence occurrence)
    {
        TrajectorySegmentDto segment = GetTrajectorySegment(occurrence.SegmentIndex);
        return occurrence.Kind switch
        {
            AnchorOccurrenceKind.PolylinePoint => segment.Points![occurrence.PointIndex],
            AnchorOccurrenceKind.BezierStart => segment.Start!,
            _ => segment.End!,
        };
    }

    private void SetOccurrencePoint(
        AnchorOccurrence occurrence,
        Point2Dto position,
        float deltaX,
        float deltaY)
    {
        TrajectorySegmentDto segment = GetTrajectorySegment(occurrence.SegmentIndex);
        switch (occurrence.Kind)
        {
            case AnchorOccurrenceKind.PolylinePoint:
                segment.Points![occurrence.PointIndex] = CopyPoint(position);
                break;
            case AnchorOccurrenceKind.BezierStart:
                segment.Start = CopyPoint(position);
                segment.Control1 = Translate(segment.Control1!, deltaX, deltaY);
                break;
            case AnchorOccurrenceKind.BezierEnd:
                segment.End = CopyPoint(position);
                segment.Control2 = Translate(segment.Control2!, deltaX, deltaY);
                break;
        }
    }

    private void DeleteSharedPolylineAnchor(AnchorOccurrence previous, AnchorOccurrence next)
    {
        TrajectorySegmentDto left = GetTrajectorySegment(previous.SegmentIndex);
        TrajectorySegmentDto right = GetTrajectorySegment(next.SegmentIndex);
        Point2Dto[] leftPoints = left.Points!;
        Point2Dto[] rightPoints = right.Points!;
        Point2Dto[] merged =
        [
            .. leftPoints.Take(leftPoints.Length - 1).Select(CopyPoint),
            .. rightPoints.Skip(1).Select(CopyPoint),
        ];
        left.Points = merged;
        var segments = Definition.Trajectory.Segments.ToList();
        segments.RemoveAt(next.SegmentIndex);
        Definition.Trajectory.Segments = [.. segments];
    }

    private void DeleteEndpointOrInternalPolylineAnchor(AnchorOccurrence occurrence)
    {
        TrajectorySegmentDto segment = GetTrajectorySegment(occurrence.SegmentIndex);
        if (segment.Type == "cubicBezier")
        {
            var cubicSegments = Definition.Trajectory.Segments.ToList();
            cubicSegments.RemoveAt(occurrence.SegmentIndex);
            Definition.Trajectory.Segments = [.. cubicSegments];
            return;
        }

        var points = segment.Points!.Select(CopyPoint).ToList();
        if (points.Count > 2)
        {
            points.RemoveAt(occurrence.PointIndex);
            segment.Points = [.. points];
            return;
        }

        // A two-point edge at the global start/end disappears as a whole; its shared
        // neighbour becomes the new Entry/Exit. The global minimum was checked above.
        var segments = Definition.Trajectory.Segments.ToList();
        segments.RemoveAt(occurrence.SegmentIndex);
        Definition.Trajectory.Segments = [.. segments];
    }

    private void DeleteSharedAnchorWithCubic(AnchorOccurrence previous, AnchorOccurrence next)
    {
        TrajectorySegmentDto left = GetTrajectorySegment(previous.SegmentIndex);
        TrajectorySegmentDto right = GetTrajectorySegment(next.SegmentIndex);
        Point2Dto start = GetSectionEndpoints(
            new TrajectorySectionLocation(previous.SegmentIndex,
                left.Type == "polyline" ? left.Points!.Length - 2 : 0)).Start;
        Point2Dto deleted = GetSegmentEnd(left);
        Point2Dto end = GetSectionEndpoints(
            new TrajectorySectionLocation(next.SegmentIndex, 0)).End;

        bool keepLeftPrefix = left.Type == "polyline" && left.Points!.Length > 2;
        bool keepRightSuffix = right.Type == "polyline" && right.Points!.Length > 2;
        string mergedId = !keepLeftPrefix
            ? left.Id
            : !keepRightSuffix
                ? right.Id
                : CreateUniqueSegmentId();
        Point2Dto leftEndpointControl = left.Type == "cubicBezier"
            ? left.Control1!
            : Lerp(start, deleted, 1.0f / 3.0f);
        Point2Dto rightEndpointControl = right.Type == "cubicBezier"
            ? right.Control2!
            : Lerp(deleted, end, 2.0f / 3.0f);
        Point2Dto control1 = Lerp(start, leftEndpointControl, 2.0f);
        Point2Dto control2 = Lerp(end, rightEndpointControl, 2.0f);

        var replacements = new List<TrajectorySegmentDto>();
        if (keepLeftPrefix)
        {
            replacements.Add(CreatePolyline(left.Id, left.Points!.Take(left.Points!.Length - 1)));
        }

        replacements.Add(CreateCubic(mergedId, start, control1, control2, end));
        if (keepRightSuffix)
        {
            replacements.Add(CreatePolyline(right.Id, right.Points!.Skip(1)));
        }

        var segments = Definition.Trajectory.Segments.ToList();
        segments.RemoveRange(previous.SegmentIndex, next.SegmentIndex - previous.SegmentIndex + 1);
        segments.InsertRange(previous.SegmentIndex, replacements);
        Definition.Trajectory.Segments = [.. segments];
    }

    private void ReplaceSegment(int index, IEnumerable<TrajectorySegmentDto> replacements)
    {
        var segments = Definition.Trajectory.Segments.ToList();
        segments.RemoveAt(index);
        segments.InsertRange(index, replacements);
        Definition.Trajectory.Segments = [.. segments];
    }

    private void NormalizeAdjacentPolylines()
    {
        var normalized = new List<TrajectorySegmentDto>();
        foreach (TrajectorySegmentDto segment in Definition.Trajectory.Segments)
        {
            if (normalized.Count > 0 && normalized[^1].Type == "polyline" && segment.Type == "polyline")
            {
                TrajectorySegmentDto previous = normalized[^1];
                // Consecutive lines share an endpoint by validation/editor invariants.
                // Preserve the first id and concatenate while omitting the duplicate join.
                previous.Points =
                [
                    .. previous.Points!.Select(CopyPoint),
                    .. segment.Points!.Skip(1).Select(CopyPoint),
                ];
                continue;
            }

            normalized.Add(segment);
        }

        Definition.Trajectory.Segments = [.. normalized];
    }

    private TrajectorySectionLocation? FindPolylineSection(Point2Dto start, Point2Dto end)
    {
        foreach (TrajectorySectionLocation location in GetTrajectorySections())
        {
            TrajectorySegmentDto segment = GetTrajectorySegment(location.SegmentIndex);
            if (segment.Type != "polyline")
            {
                continue;
            }

            (Point2Dto sectionStart, Point2Dto sectionEnd) = GetSectionEndpoints(location);
            if (PointsEqual(sectionStart, start) && PointsEqual(sectionEnd, end))
            {
                return location;
            }
        }

        return null;
    }

    private string CreateUniqueSegmentId(IEnumerable<string>? additionallyReserved = null)
    {
        var existing = Definition.Trajectory.Segments.Select(segment => segment.Id)
            .ToHashSet(StringComparer.Ordinal);
        if (additionallyReserved is not null)
        {
            existing.UnionWith(additionallyReserved);
        }

        for (int number = 1; ; number++)
        {
            string candidate = $"trajectory-segment-{number:000}";
            if (!existing.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    private static TrajectoryDto CreateSinglePolyline(string segmentId, IEnumerable<Point2Dto> points)
    {
        return new TrajectoryDto
        {
            Segments = [CreatePolyline(segmentId, points)],
        };
    }

    private static TrajectorySegmentDto CreatePolyline(string segmentId, IEnumerable<Point2Dto> points)
    {
        return new TrajectorySegmentDto
        {
            Id = string.IsNullOrWhiteSpace(segmentId) ? DefaultTrajectoryId : segmentId,
            Type = "polyline",
            Points = points.Select(CopyPoint).ToArray(),
        };
    }

    private static TrajectorySegmentDto CreateStraightCubic(
        string segmentId,
        Point2Dto start,
        Point2Dto end)
    {
        return new TrajectorySegmentDto
        {
            Id = string.IsNullOrWhiteSpace(segmentId) ? DefaultTrajectoryId : segmentId,
            Type = "cubicBezier",
            Start = CopyPoint(start),
            Control1 = Lerp(start, end, 1.0f / 3.0f),
            Control2 = Lerp(start, end, 2.0f / 3.0f),
            End = CopyPoint(end),
        };
    }

    private static TrajectorySegmentDto CreateCubic(
        string segmentId,
        Point2Dto start,
        Point2Dto control1,
        Point2Dto control2,
        Point2Dto end)
    {
        return new TrajectorySegmentDto
        {
            Id = string.IsNullOrWhiteSpace(segmentId) ? DefaultTrajectoryId : segmentId,
            Type = "cubicBezier",
            Start = CopyPoint(start),
            Control1 = CopyPoint(control1),
            Control2 = CopyPoint(control2),
            End = CopyPoint(end),
        };
    }

    private int FindConeIndex(string id) => Array.FindIndex(Definition.Cones, cone => cone.Id == id);

    private string CreateUniqueConeId()
    {
        var existing = Definition.Cones.Select(cone => cone.Id).ToHashSet(StringComparer.Ordinal);
        for (int number = 1; ; number++)
        {
            string candidate = $"cone-{number:000}";
            if (!existing.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    private string CreateUniqueMarkingId()
    {
        var existing = Definition.Markings.Select(marking => marking.Id).ToHashSet(StringComparer.Ordinal);
        for (int number = 1; ; number++)
        {
            string candidate = $"marking-{number:000}";
            if (!existing.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    private static Point2Dto GetSegmentStart(TrajectorySegmentDto segment)
    {
        return segment.Type == "polyline" ? segment.Points![0] : segment.Start!;
    }

    private static Point2Dto GetSegmentEnd(TrajectorySegmentDto segment)
    {
        return segment.Type == "polyline" ? segment.Points![^1] : segment.End!;
    }

    private static Point2Dto GetOutgoingTangentVector(TrajectorySegmentDto segment)
    {
        return segment.Type == "cubicBezier"
            ? Subtract(segment.End!, segment.Control2!)
            : Subtract(segment.Points![^1], segment.Points[^2]);
    }

    private static Point2Dto Lerp(Point2Dto start, Point2Dto end, float weight)
    {
        return new Point2Dto
        {
            X = start.X + (end.X - start.X) * weight,
            Y = start.Y + (end.Y - start.Y) * weight,
        };
    }

    private static Point2Dto Translate(Point2Dto point, float deltaX, float deltaY)
    {
        return new Point2Dto { X = point.X + deltaX, Y = point.Y + deltaY };
    }

    private static Point2Dto Subtract(Point2Dto left, Point2Dto right) =>
        new() { X = left.X - right.X, Y = left.Y - right.Y };

    private static Point2Dto ScaleVector(Point2Dto vector, float scale) =>
        new() { X = vector.X * scale, Y = vector.Y * scale };

    private static float VectorLengthSquared(Point2Dto vector) =>
        vector.X * vector.X + vector.Y * vector.Y;

    private static bool PointsEqual(Point2Dto left, Point2Dto right)
    {
        return MathF.Abs(left.X - right.X) <= 0.0001f && MathF.Abs(left.Y - right.Y) <= 0.0001f;
    }

    private static Point2Dto CopyPoint(Point2Dto point) => new() { X = point.X, Y = point.Y };

    private static ConeDto CopyCone(ConeDto source, Point2Dto? position = null, string? color = null)
    {
        return new ConeDto
        {
            Id = source.Id,
            Position = CopyPoint(position ?? source.Position),
            Color = color ?? source.Color,
            Type = source.Type,
        };
    }
}
