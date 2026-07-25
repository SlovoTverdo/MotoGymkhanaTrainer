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
        float width = 60.0f,
        float length = 100.0f)
    {
        return new TrackProjectDocument(new TrackProjectDto
        {
            Track = new TrackProjectMetadataDto { Id = id, Name = name },
            Area = new TrackProjectAreaDto { Width = width, Length = length },
            Instances = [],
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
}
