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
    CubicAdjacentBlocked,
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

    /// <summary>Starts replacement construction while always retaining a valid two-point polyline.</summary>
    public void StartTrajectoryAt(Point2Dto position)
    {
        string id = Definition.Trajectory.Segments.FirstOrDefault()?.Id ?? DefaultTrajectoryId;
        Definition.Trajectory = CreateSinglePolyline(id, [position, position]);
        SynchronizeEndpointsFromTrajectory();
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
    /// Inserts a midpoint after a conceptual anchor only when the following section
    /// is persisted as a polyline. Bezier splitting is intentionally outside this MVP.
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
        if (segment.Type != "polyline")
        {
            return -2;
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

    /// <summary>Deletes an anchor only through continuity-preserving line operations.</summary>
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

        IReadOnlyList<TrajectorySectionLocation> sections = GetTrajectorySections();
        if ((anchorIndex > 0 && IsCubic(sections[anchorIndex - 1])) ||
            (anchorIndex < sections.Count && IsCubic(sections[anchorIndex])))
        {
            return TrajectoryAnchorDeleteResult.CubicAdjacentBlocked;
        }

        List<List<AnchorOccurrence>> bindings = BuildAnchorBindings();
        List<AnchorOccurrence> occurrences = bindings[anchorIndex];
        if (occurrences.Count == 2)
        {
            DeleteSharedPolylineAnchor(occurrences[0], occurrences[1]);
        }
        else
        {
            DeleteSinglePolylineAnchor(occurrences[0]);
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

    private bool IsCubic(TrajectorySectionLocation location)
    {
        return GetTrajectorySegment(location.SegmentIndex).Type == "cubicBezier";
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

    private void DeleteSinglePolylineAnchor(AnchorOccurrence occurrence)
    {
        TrajectorySegmentDto segment = GetTrajectorySegment(occurrence.SegmentIndex);
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

    private static Point2Dto GetSegmentStart(TrajectorySegmentDto segment)
    {
        return segment.Type == "polyline" ? segment.Points![0] : segment.Start!;
    }

    private static Point2Dto GetSegmentEnd(TrajectorySegmentDto segment)
    {
        return segment.Type == "polyline" ? segment.Points![^1] : segment.End!;
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
