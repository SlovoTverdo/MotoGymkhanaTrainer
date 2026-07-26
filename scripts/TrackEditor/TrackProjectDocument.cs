using MotoGymkhanaTrainer.ExerciseEditor;
using MotoGymkhanaTrainer.Tracks;

namespace MotoGymkhanaTrainer.TrackEditor;

/// <summary>
/// Single source of persisted Track Project data. Resolved definitions are a
/// replaceable read-only cache and are never included by the serializer.
/// </summary>
public sealed class TrackProjectDocument
{
    private readonly Dictionary<string, ExerciseDefinitionDto> _definitions;

    public TrackProjectDocument(
        TrackProjectDto project,
        IReadOnlyDictionary<string, ExerciseDefinitionDto>? definitions = null)
    {
        Project = project;
        _definitions = definitions is null
            ? new Dictionary<string, ExerciseDefinitionDto>(StringComparer.Ordinal)
            : new Dictionary<string, ExerciseDefinitionDto>(definitions, StringComparer.Ordinal);
    }

    public TrackProjectDto Project { get; }

    public static TrackProjectDocument CreateNew(
        string id = "new-track",
        string name = "New Track",
        float width = 100.0f,
        float length = 40.0f)
    {
        return new TrackProjectDocument(new TrackProjectDto
        {
            Track = new TrackProjectMetadataDto { Id = id, Name = name },
            Area = new TrackProjectAreaDto { Width = width, Length = length },
            Instances = [],
            TransitionOverrides = [],
        });
    }

    /// <summary>Adds a reference at the area origin; the definition itself is never copied into JSON.</summary>
    public string AddInstance(string exercisePath, ExerciseDefinitionDto definition)
    {
        string instanceId = CreateUniqueInstanceId();
        var instances = Project.Instances.ToList();
        instances.Add(new TrackProjectInstanceDto
        {
            InstanceId = instanceId,
            ExercisePath = exercisePath.Replace(Path.DirectorySeparatorChar, '/'),
            Position = new Point2Dto(),
            RotationDeg = 0.0f,
            Scale = new Point2Dto { X = 1.0f, Y = 1.0f },
        });
        Project.Instances = [.. instances];
        _definitions[instanceId] = definition;
        return instanceId;
    }

    /// <summary>
    /// Inserts a copy immediately after the source. Only persisted instance data
    /// is duplicated; transition overrides belong to route pairs and are not copied.
    /// </summary>
    public string? DuplicateInstance(string sourceInstanceId, Point2Dto offset)
    {
        int sourceIndex = Array.FindIndex(Project.Instances,
            instance => instance.InstanceId == sourceInstanceId);
        if (sourceIndex < 0 || !float.IsFinite(offset.X) || !float.IsFinite(offset.Y))
        {
            return null;
        }

        TrackProjectInstanceDto source = Project.Instances[sourceIndex];
        string instanceId = CreateUniqueInstanceId();
        var duplicate = new TrackProjectInstanceDto
        {
            InstanceId = instanceId,
            ExercisePath = source.ExercisePath,
            Position = new Point2Dto
            {
                X = source.Position.X + offset.X,
                Y = source.Position.Y + offset.Y,
            },
            RotationDeg = source.RotationDeg,
            Scale = CopyPoint(source.Scale),
        };
        var instances = Project.Instances.ToList();
        instances.Insert(sourceIndex + 1, duplicate);
        Project.Instances = [.. instances];
        if (_definitions.TryGetValue(sourceInstanceId, out ExerciseDefinitionDto? definition))
        {
            _definitions[instanceId] = definition;
        }

        return instanceId;
    }

    public TrackProjectInstanceDto? FindInstance(string instanceId) =>
        Project.Instances.FirstOrDefault(instance => instance.InstanceId == instanceId);

    public ExerciseDefinitionDto? FindDefinition(string instanceId) =>
        _definitions.GetValueOrDefault(instanceId);

    /// <summary>
    /// Replaces only the runtime dependency cache after a library refresh. The
    /// persisted Track Project remains the source of instance order/transforms.
    /// </summary>
    public void ReplaceDefinitions(IReadOnlyDictionary<string, ExerciseDefinitionDto> definitions)
    {
        _definitions.Clear();
        foreach ((string instanceId, ExerciseDefinitionDto definition) in definitions)
        {
            _definitions[instanceId] = definition;
        }
    }

    public string GetDisplayName(TrackProjectInstanceDto instance) =>
        FindDefinition(instance.InstanceId)?.Exercise.Name ?? $"Unresolved: {instance.ExercisePath}";

    public bool MoveInstance(string instanceId, Point2Dto position)
    {
        TrackProjectInstanceDto? instance = FindInstance(instanceId);
        if (instance is null || !float.IsFinite(position.X) || !float.IsFinite(position.Y))
        {
            return false;
        }

        instance.Position = CopyPoint(position);
        return true;
    }

    public bool SetTransform(string instanceId, Point2Dto position, float rotationDeg, Point2Dto scale)
    {
        TrackProjectInstanceDto? instance = FindInstance(instanceId);
        if (instance is null || !float.IsFinite(position.X) || !float.IsFinite(position.Y) ||
            !float.IsFinite(rotationDeg) || !float.IsFinite(scale.X) || !float.IsFinite(scale.Y) ||
            MathF.Abs(scale.X) < 0.0001f || MathF.Abs(scale.Y) < 0.0001f)
        {
            return false;
        }

        instance.Position = CopyPoint(position);
        instance.RotationDeg = rotationDeg;
        instance.Scale = CopyPoint(scale);
        return true;
    }

    /// <summary>
    /// Toggles left/right mirroring without changing the instance size.
    /// A negative X scale is the persisted representation of this reflection.
    /// </summary>
    public bool ToggleHorizontalMirror(string instanceId) => ToggleScaleSign(instanceId, xAxis: true);

    /// <summary>
    /// Toggles top/bottom mirroring without changing the instance size.
    /// A negative Y scale is the persisted representation of this reflection.
    /// </summary>
    public bool ToggleVerticalMirror(string instanceId) => ToggleScaleSign(instanceId, xAxis: false);

    public bool MoveUp(string instanceId) => MoveBy(instanceId, -1);

    public bool MoveDown(string instanceId) => MoveBy(instanceId, 1);

    public bool DeleteInstance(string instanceId)
    {
        int index = Array.FindIndex(Project.Instances, instance => instance.InstanceId == instanceId);
        if (index < 0)
        {
            return false;
        }

        var instances = Project.Instances.ToList();
        instances.RemoveAt(index);
        Project.Instances = [.. instances];
        _definitions.Remove(instanceId);
        return true;
    }

    /// <summary>Finds the sole override for an oriented instance pair, if present.</summary>
    public TransitionOverrideDto? FindTransitionOverride(string fromInstanceId, string toInstanceId) =>
        Project.TransitionOverrides.FirstOrDefault(item =>
            item.FromInstanceId == fromInstanceId && item.ToInstanceId == toInstanceId);

    /// <summary>
    /// Creates an override on first real edit, then updates one absolute handle.
    /// Both initial offsets come from the compiled automatic curve, while the edited
    /// coordinate is converted back to an offset from its current derived endpoint.
    /// </summary>
    public bool SetTransitionControlPoint(
        CompiledTransition transition,
        int controlIndex,
        Point2Dto absolutePoint)
    {
        if (controlIndex is not (1 or 2) || !IsFinite(absolutePoint))
        {
            return false;
        }

        TransitionOverrideDto? item = FindTransitionOverride(
            transition.FromInstanceId, transition.ToInstanceId);
        if (item is null)
        {
            item = new TransitionOverrideDto
            {
                TransitionId = transition.TransitionId,
                FromInstanceId = transition.FromInstanceId,
                ToInstanceId = transition.ToInstanceId,
                Control1Offset = Subtract(transition.Control1, transition.Start),
                Control2Offset = Subtract(transition.Control2, transition.End),
            };
            Project.TransitionOverrides = [.. Project.TransitionOverrides, item];
        }

        if (controlIndex == 1)
        {
            item.Control1Offset = Subtract(absolutePoint, transition.Start);
        }
        else
        {
            item.Control2Offset = Subtract(absolutePoint, transition.End);
        }

        return true;
    }

    /// <summary>Removes the persisted correction so compilation returns to automatic mode.</summary>
    public bool ResetTransition(string fromInstanceId, string toInstanceId)
    {
        int index = Array.FindIndex(Project.TransitionOverrides, item =>
            item.FromInstanceId == fromInstanceId && item.ToInstanceId == toInstanceId);
        if (index < 0)
        {
            return false;
        }

        Project.TransitionOverrides = Project.TransitionOverrides
            .Where((_, itemIndex) => itemIndex != index).ToArray();
        return true;
    }

    /// <summary>
    /// Returns overrides whose oriented pair is not adjacent in the current route.
    /// They remain persisted so reorder/delete never silently destroys manual work.
    /// </summary>
    public IReadOnlyList<TransitionOverrideDto> GetOrphanedTransitionOverrides()
    {
        var adjacentPairs = new HashSet<(string From, string To)>();
        for (int index = 0; index + 1 < Project.Instances.Length; index++)
        {
            adjacentPairs.Add((Project.Instances[index].InstanceId,
                Project.Instances[index + 1].InstanceId));
        }

        return Project.TransitionOverrides
            .Where(item => !adjacentPairs.Contains((item.FromInstanceId, item.ToInstanceId)))
            .ToArray();
    }

    /// <summary>Explicit destructive cleanup used only after UI confirmation.</summary>
    public int RemoveOrphanedTransitionOverrides()
    {
        IReadOnlyList<TransitionOverrideDto> orphaned = GetOrphanedTransitionOverrides();
        if (orphaned.Count == 0)
        {
            return 0;
        }

        var set = orphaned.ToHashSet();
        Project.TransitionOverrides = Project.TransitionOverrides.Where(item => !set.Contains(item)).ToArray();
        return orphaned.Count;
    }

    /// <summary>Counts manual overrides that mention an instance being deleted.</summary>
    public int CountRelatedTransitionOverrides(string instanceId) =>
        Project.TransitionOverrides.Count(item =>
            item.FromInstanceId == instanceId || item.ToInstanceId == instanceId);

    public bool IsOutsideArea(string instanceId)
    {
        TrackProjectInstanceDto? instance = FindInstance(instanceId);
        ExerciseDefinitionDto? definition = FindDefinition(instanceId);
        if (instance is null || definition is null)
        {
            return false;
        }

        Point2Dto[] corners = ExerciseInstanceGeometry.TransformBounds(
            definition.Bounds.Width,
            definition.Bounds.Length,
            instance.Position,
            instance.RotationDeg,
            instance.Scale);
        return ExerciseInstanceGeometry.IsOutsideArea(corners, Project.Area.Width, Project.Area.Length);
    }

    private bool MoveBy(string instanceId, int offset)
    {
        int index = Array.FindIndex(Project.Instances, instance => instance.InstanceId == instanceId);
        int target = index + offset;
        if (index < 0 || target < 0 || target >= Project.Instances.Length)
        {
            return false;
        }

        (Project.Instances[index], Project.Instances[target]) =
            (Project.Instances[target], Project.Instances[index]);
        return true;
    }

    private bool ToggleScaleSign(string instanceId, bool xAxis)
    {
        TrackProjectInstanceDto? instance = FindInstance(instanceId);
        if (instance is null)
        {
            return false;
        }

        instance.Scale = new Point2Dto
        {
            X = xAxis ? -instance.Scale.X : instance.Scale.X,
            Y = xAxis ? instance.Scale.Y : -instance.Scale.Y,
        };

        return true;
    }

    private string CreateUniqueInstanceId()
    {
        var existing = Project.Instances.Select(instance => instance.InstanceId)
            .ToHashSet(StringComparer.Ordinal);
        for (int number = 1; ; number++)
        {
            string candidate = $"exercise-instance-{number:000}";
            if (!existing.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    private static Point2Dto CopyPoint(Point2Dto point) => new() { X = point.X, Y = point.Y };

    private static Point2Dto Subtract(Point2Dto left, Point2Dto right) =>
        new() { X = left.X - right.X, Y = left.Y - right.Y };

    private static bool IsFinite(Point2Dto? point) =>
        point is not null && float.IsFinite(point.X) && float.IsFinite(point.Y);
}
