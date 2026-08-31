using MotoGymkhanaTrainer.Tracks;

namespace MotoGymkhanaTrainer.VenueEditor;

/// <summary>Mutation facade around the one persisted Venue Definition DTO.</summary>
public sealed class VenueDocument
{
    public VenueDocument(VenueDefinitionDto definition) => Definition = definition;
    public VenueDefinitionDto Definition { get; private set; }

    /// <summary>Creates the documented empty Venue defaults.</summary>
    public static VenueDocument CreateNew(string id = "new-venue", string name = "New Venue") => new(new VenueDefinitionDto
    {
        Venue = new VenueMetadataDto { Id = id, Name = name },
        Area = new VenueAreaDto { Width = 60, Length = 100 },
        Panorama = new VenuePanoramaDto { Enabled = false, TexturePath = string.Empty, RotationDeg = 0, EnergyMultiplier = 1 },
    });

    public void Replace(VenueDefinitionDto definition) => Definition = definition;
    public VenueObjectInstanceDto? FindObject(string? id) => Definition.Objects.FirstOrDefault(item => item.ObjectId == id);
    public ConeDto? FindCone(string? id) => Definition.Cones.FirstOrDefault(item => item.Id == id);
    public MarkingDto? FindMarking(string? id) => Definition.Markings.FirstOrDefault(item => item.Id == id);

    /// <summary>Adds a measured scene reference at the area centre/origin.</summary>
    public VenueObjectInstanceDto AddObject(
        string assetPath,
        FootprintDto footprint,
        Point2Dto? position = null,
        string? objectType = null,
        string? assetId = null,
        string? collisionMode = null)
    {
        ArgumentNullException.ThrowIfNull(footprint);
        var item = new VenueObjectInstanceDto
        {
            ObjectId = NextId("venue-object", Definition.Objects.Select(value => value.ObjectId)),
            Name = Path.GetFileNameWithoutExtension(assetPath),
            AssetPath = assetPath,
            ObjectType = objectType,
            AssetId = assetId,
            CollisionMode = collisionMode,
            CollisionEnabled = collisionMode != "none",
            Position = position is null ? new Point2Dto() : Copy(position),
            Footprint = new FootprintDto
            {
                Width = footprint.Width,
                Length = footprint.Length,
                CenterX = footprint.CenterX,
                CenterY = footprint.CenterY,
            },
        };
        Definition.Objects = [.. Definition.Objects, item];
        return item;
    }

    /// <summary>Copies only persisted properties and assigns a stable fresh id and editor offset.</summary>
    public VenueObjectInstanceDto DuplicateObject(string id)
    {
        VenueObjectInstanceDto source = FindObject(id) ?? throw new InvalidOperationException("Object is not selected.");
        var copy = new VenueObjectInstanceDto
        {
            ObjectId = NextId("venue-object", Definition.Objects.Select(value => value.ObjectId)),
            Name = source.Name,
            AssetPath = source.AssetPath,
            ObjectType = source.ObjectType,
            AssetId = source.AssetId,
            Position = source.ObjectType == "imported"
                ? Copy(source.Position)
                : new Point2Dto { X = source.Position.X + 1, Y = source.Position.Y + 1 },
            Elevation = source.Elevation,
            RotationDeg = source.RotationDeg,
            Scale = new Scale3Dto { X = source.Scale.X, Y = source.Scale.Y, Z = source.Scale.Z },
            Footprint = new FootprintDto
            {
                Width = source.Footprint.Width,
                Length = source.Footprint.Length,
                CenterX = source.Footprint.CenterX,
                CenterY = source.Footprint.CenterY,
            },
            CollisionEnabled = source.CollisionEnabled,
            CollisionMode = source.CollisionMode,
            VisibleInViewer = source.VisibleInViewer,
        };
        int index = Array.IndexOf(Definition.Objects, source);
        Definition.Objects = [.. Definition.Objects.Take(index + 1), copy, .. Definition.Objects.Skip(index + 1)];
        return copy;
    }

    public bool DeleteObject(string id)
    {
        int before = Definition.Objects.Length;
        Definition.Objects = Definition.Objects.Where(value => value.ObjectId != id).ToArray();
        return Definition.Objects.Length != before;
    }
    public void MoveObject(string id, Point2Dto position) => (FindObject(id) ?? throw Missing(id)).Position = Copy(position);

    /// <summary>Changes only an imported instance's shared asset binding and cached footprint.</summary>
    public void RelinkImportedObject(string id, VenueImportedAssetMetadata asset)
    {
        VenueObjectInstanceDto item = FindObject(id) ?? throw Missing(id);
        if (item.ObjectType != "imported") throw new InvalidOperationException("Only imported Venue objects can be relinked.");
        item.AssetId = asset.AssetId;
        item.AssetPath = asset.RuntimeScenePath;
        item.Footprint = Copy(asset.Footprint);
        item.CollisionMode = asset.CollisionMode;
        item.CollisionEnabled = asset.CollisionMode == "generated";
    }

    /// <summary>Refreshes shared cached geometry for every instance without changing instance transforms.</summary>
    public int ApplyImportedAssetMetadata(VenueImportedAssetMetadata asset)
    {
        int updated = 0;
        foreach (VenueObjectInstanceDto item in Definition.Objects.Where(value =>
                     value.ObjectType == "imported" && value.AssetId == asset.AssetId))
        {
            item.AssetPath = asset.RuntimeScenePath;
            item.Footprint = Copy(asset.Footprint);
            updated++;
        }
        return updated;
    }

    /// <summary>Maps the persisted imported collision policy to the compatibility boolean.</summary>
    public void SetImportedCollisionMode(string id, string mode)
    {
        VenueObjectInstanceDto item = FindObject(id) ?? throw Missing(id);
        if (item.ObjectType != "imported") throw new InvalidOperationException("Only imported Venue objects have a collision mode.");
        if (mode is not ("generated" or "none")) throw new ArgumentOutOfRangeException(nameof(mode));
        item.CollisionMode = mode;
        item.CollisionEnabled = mode == "generated";
    }

    public ConeDto AddCone(Point2Dto position)
    {
        var cone = new ConeDto { Id = NextId("venue-cone", Definition.Cones.Select(value => value.Id)), Position = Copy(position), Type = "standard", Color = "orange" };
        Definition.Cones = [.. Definition.Cones, cone];
        return cone;
    }
    public bool DeleteCone(string id)
    {
        int before = Definition.Cones.Length;
        Definition.Cones = Definition.Cones.Where(value => value.Id != id).ToArray();
        return Definition.Cones.Length != before;
    }
    public void MoveCone(string id, Point2Dto position)
    {
        int index = Array.FindIndex(Definition.Cones, value => value.Id == id);
        if (index < 0) throw Missing(id);
        ConeDto source = Definition.Cones[index];
        Definition.Cones[index] = new ConeDto { Id = source.Id, Position = Copy(position), Color = source.Color, Type = source.Type };
    }
    public void SetConeColor(string id, string color)
    {
        int index = Array.FindIndex(Definition.Cones, value => value.Id == id);
        if (index < 0) throw Missing(id);
        ConeDto source = Definition.Cones[index];
        Definition.Cones[index] = new ConeDto { Id = source.Id, Position = Copy(source.Position), Color = color, Type = source.Type };
    }

    public MarkingDto AddMarking(string type, IReadOnlyList<Point2Dto> points)
    {
        var marking = new MarkingDto
        {
            Id = NextId("venue-marking", Definition.Markings.Select(value => value.Id)),
            Path = PathEditing.FromPolyline(points),
            Color = "#FFFFFF",
            WidthMeters = 0.08f,
            Style = "solid",
            VisibleInViewer = true,
        };
        Definition.Markings = [.. Definition.Markings, marking];
        return marking;
    }

    /// <summary>Adds a fully formed, validated Path without persisting transient empty geometry.</summary>
    public MarkingDto AddMarking(PathDefinition path)
    {
        IReadOnlyList<string> errors = PathValidator.Validate(path, "marking.path");
        if (errors.Count > 0) throw new ArgumentException(string.Join(" ", errors), nameof(path));
        var marking = new MarkingDto
        {
            Id = NextId("venue-marking", Definition.Markings.Select(value => value.Id)),
            Path = PathEditing.CopyPath(path),
            Color = "#FFFFFF",
            WidthMeters = 0.08f,
            Style = "solid",
            VisibleInViewer = true,
        };
        Definition.Markings = [.. Definition.Markings, marking];
        return marking;
    }
    public bool DeleteMarking(string id)
    {
        int before = Definition.Markings.Length;
        Definition.Markings = Definition.Markings.Where(value => value.Id != id).ToArray();
        return Definition.Markings.Length != before;
    }
    public void MoveMarkingPoint(string id, int index, Point2Dto point)
    {
        MarkingDto marking = FindMarking(id) ?? throw Missing(id);
        if (!PathEditing.TryMoveVertex(marking.Path, index, point))
            throw new InvalidOperationException("Curved marking path vertices are read-only in this iteration.");
    }
    public void InsertMarkingPointAfter(string id, int index)
    {
        MarkingDto marking = FindMarking(id) ?? throw Missing(id);
        if (PathEditing.InsertVertexAfter(marking.Path, index) < 0)
            throw new InvalidOperationException("Only an all-line path supports point insertion.");
    }
    public void DeleteMarkingPoint(string id, int index)
    {
        MarkingDto marking = FindMarking(id) ?? throw Missing(id);
        if (!PathEditing.DeleteInternalVertex(marking.Path, index))
            throw new InvalidOperationException("Only an internal vertex of an all-line path can be deleted.");
    }

    /// <summary>Moves one Path coordinate while preserving implicit segment starts.</summary>
    public bool MoveMarkingCoordinate(string id, int segmentIndex, MarkingPathCoordinateKind kind, Point2Dto point)
    {
        MarkingDto? marking = FindMarking(id);
        if (marking is null) return false;
        PathDefinition candidate = PathEditing.CopyPath(marking.Path);
        if (!PathEditing.MoveCoordinate(candidate, segmentIndex, kind, point) ||
            PathValidator.Validate(candidate, $"marking '{id}'.path").Count > 0) return false;
        marking.Path = candidate;
        return true;
    }

    /// <summary>Restores a deep Path snapshot, used by canceled drag transactions.</summary>
    public bool ReplaceMarkingPath(string id, PathDefinition path)
    {
        MarkingDto? marking = FindMarking(id);
        if (marking is null) return false;
        marking.Path = PathEditing.CopyPath(path);
        return true;
    }

    /// <summary>Translates all Path coordinates by one common domain-space delta.</summary>
    public bool MoveMarking(string id, float deltaX, float deltaY)
    {
        MarkingDto? marking = FindMarking(id);
        if (marking is null || !float.IsFinite(deltaX) || !float.IsFinite(deltaY)) return false;
        marking.Path = PathEditing.Translate(marking.Path, deltaX, deltaY);
        return true;
    }

    /// <summary>Appends a line or initially straight cubic and returns its new segment index.</summary>
    public int AppendMarkingSegment(string id, Point2Dto end, bool cubic)
    {
        MarkingDto? marking = FindMarking(id);
        if (marking is null) return -1;
        return cubic ? PathEditing.AppendCubic(marking.Path, end) : PathEditing.AppendLine(marking.Path, end);
    }

    public bool ConvertMarkingSegment(string id, int segmentIndex, bool toCubic)
    {
        MarkingDto? marking = FindMarking(id);
        return marking is not null && (toCubic
            ? PathEditing.ConvertLineToCubic(marking.Path, segmentIndex)
            : PathEditing.ConvertCubicToLine(marking.Path, segmentIndex));
    }

    public bool SplitMarkingSegment(string id, int segmentIndex, float parameter)
    {
        MarkingDto? marking = FindMarking(id);
        return marking is not null && PathEditing.SplitSegment(marking.Path, segmentIndex, parameter);
    }

    /// <summary>Deletes one segment; deleting the only segment removes its marking.</summary>
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

    /// <summary>Applies validated presentation properties without touching Path geometry.</summary>
    public bool SetMarkingProperties(string id, string color, float widthMeters, string style, bool visibleInViewer)
    {
        MarkingDto? marking = FindMarking(id);
        if (marking is null || widthMeters <= 0 || !float.IsFinite(widthMeters) ||
            !MarkingGeometry.TryNormalizeColor(color, false, out string canonical) ||
            !MarkingGeometry.IsSupportedStyle(style)) return false;
        marking.Color = canonical;
        marking.WidthMeters = widthMeters;
        marking.Style = style;
        marking.VisibleInViewer = visibleInViewer;
        return true;
    }

    private static string NextId(string prefix, IEnumerable<string> existing)
    {
        var set = existing.ToHashSet(StringComparer.Ordinal);
        for (int index = 1; ; index++)
        {
            string candidate = $"{prefix}-{index:000}";
            if (!set.Contains(candidate)) return candidate;
        }
    }
    private static Point2Dto Copy(Point2Dto point) => new() { X = point.X, Y = point.Y };
    private static FootprintDto Copy(FootprintDto footprint) => new()
    {
        Width = footprint.Width,
        Length = footprint.Length,
        CenterX = footprint.CenterX,
        CenterY = footprint.CenterY,
    };
    private static InvalidOperationException Missing(string id) => new($"Venue item '{id}' no longer exists.");
}
