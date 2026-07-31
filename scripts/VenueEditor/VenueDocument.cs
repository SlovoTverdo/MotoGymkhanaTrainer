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
    public VenueObjectInstanceDto AddObject(string assetPath, FootprintDto footprint)
    {
        ArgumentNullException.ThrowIfNull(footprint);
        var item = new VenueObjectInstanceDto
        {
            ObjectId = NextId("venue-object", Definition.Objects.Select(value => value.ObjectId)),
            Name = Path.GetFileNameWithoutExtension(assetPath),
            AssetPath = assetPath,
            Footprint = new FootprintDto
            {
                Width = footprint.Width,
                Length = footprint.Length,
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
            Position = new Point2Dto { X = source.Position.X + 1, Y = source.Position.Y + 1 },
            Elevation = source.Elevation,
            RotationDeg = source.RotationDeg,
            Scale = new Scale3Dto { X = source.Scale.X, Y = source.Scale.Y, Z = source.Scale.Z },
            Footprint = new FootprintDto { Width = source.Footprint.Width, Length = source.Footprint.Length },
            CollisionEnabled = source.CollisionEnabled,
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
    private static InvalidOperationException Missing(string id) => new($"Venue item '{id}' no longer exists.");
}
