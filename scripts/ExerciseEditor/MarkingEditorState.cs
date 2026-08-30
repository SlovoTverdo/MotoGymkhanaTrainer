namespace MotoGymkhanaTrainer;

using MotoGymkhanaTrainer.Tracks;

/// <summary>Editor-only kind of a selected marking handle.</summary>
public enum MarkingHandleKind
{
    None,
    PathStart,
    SegmentEnd,
    Control1,
    Control2,
}

/// <summary>
/// Stable marking selection identity. It contains domain identity and indices only;
/// canvas nodes and overlay handles are deliberately excluded.
/// </summary>
public readonly record struct MarkingSelection(
    string MarkingId,
    int SegmentIndex,
    MarkingHandleKind HandleKind)
{
    public static MarkingSelection None => new(string.Empty, -1, MarkingHandleKind.None);

    public bool HasMarking => !string.IsNullOrWhiteSpace(MarkingId);

    public bool HasSegment => HasMarking && SegmentIndex >= 0;

    public bool HasHandle => HasMarking && HandleKind != MarkingHandleKind.None;

    /// <summary>Recovers the nearest valid selection after a structural Path edit.</summary>
    public MarkingSelection Sanitize(PathDefinition path)
    {
        if (!HasMarking || path.Segments.Length == 0) return None;
        if (HandleKind == MarkingHandleKind.PathStart)
            return this with { SegmentIndex = -1 };
        int index = Math.Clamp(SegmentIndex, 0, path.Segments.Length - 1);
        MarkingHandleKind handle = HandleKind;
        if (handle is MarkingHandleKind.Control1 or MarkingHandleKind.Control2 &&
            path.Segments[index] is not CubicBezierPathSegmentDefinition)
            handle = MarkingHandleKind.None;
        return new MarkingSelection(MarkingId, index, handle);
    }
}
