namespace MotoGymkhanaTrainer.TrackEditor;

/// <summary>
/// Bounded snapshot history for the persisted Track Project state. Runtime
/// definitions, selection, viewport and locks never enter these snapshots.
/// </summary>
public sealed class TrackProjectHistory
{
    private sealed record State(long Revision, string Snapshot, string Description);

    private readonly int _capacity;
    private readonly List<State> _states = [];
    private int _position;
    private long _nextRevision;
    private long? _savedRevision;

    public TrackProjectHistory(int capacity = 100)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
    }

    public bool CanUndo => _position > 0;
    public bool CanRedo => _position + 1 < _states.Count;
    public bool IsDirty => _states.Count == 0 || _savedRevision != _states[_position].Revision;
    public string CurrentSnapshot => _states[_position].Snapshot;

    /// <summary>Starts history for a newly created or freshly opened document.</summary>
    public void Reset(string snapshot, bool saved)
    {
        _states.Clear();
        _position = 0;
        var state = new State(++_nextRevision, snapshot, "Initial state");
        _states.Add(state);
        _savedRevision = saved ? state.Revision : null;
    }

    /// <summary>Adds one logical operation, discarding redo after a branch edit.</summary>
    public bool Commit(string snapshot, string description)
    {
        if (_states.Count > 0 && string.Equals(CurrentSnapshot, snapshot, StringComparison.Ordinal))
        {
            return false;
        }

        if (CanRedo)
        {
            _states.RemoveRange(_position + 1, _states.Count - _position - 1);
        }

        _states.Add(new State(++_nextRevision, snapshot, description));
        _position = _states.Count - 1;

        // Capacity counts logical operations, therefore the current state plus
        // at most capacity predecessors are retained.
        while (_states.Count > _capacity + 1)
        {
            _states.RemoveAt(0);
            _position--;
        }

        return true;
    }

    public string? Undo()
    {
        if (!CanUndo) return null;
        _position--;
        return CurrentSnapshot;
    }

    public string? Redo()
    {
        if (!CanRedo) return null;
        _position++;
        return CurrentSnapshot;
    }

    /// <summary>Marks the current revision, not a numeric list position, as saved.</summary>
    public void MarkSaved() => _savedRevision = _states[_position].Revision;
}
