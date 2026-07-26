namespace MotoGymkhanaTrainer;

/// <summary>
/// Bounded revision-based history for a serialized persisted document. The
/// caller decides what enters the snapshot, keeping editor state out by design.
/// </summary>
public class EditorSnapshotHistory
{
    private sealed record State(long Revision, string Snapshot, string Description);
    private readonly int _capacity;
    private readonly List<State> _states = [];
    private int _position;
    private long _nextRevision;
    private long? _savedRevision;

    public EditorSnapshotHistory(int capacity = 100)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
    }

    public bool CanUndo => _position > 0;
    public bool CanRedo => _position + 1 < _states.Count;
    public bool IsDirty => _states.Count == 0 || _savedRevision != _states[_position].Revision;
    public string CurrentSnapshot => _states[_position].Snapshot;

    public void Reset(string snapshot, bool saved)
    {
        _states.Clear();
        _position = 0;
        var state = new State(++_nextRevision, snapshot, "Initial state");
        _states.Add(state);
        _savedRevision = saved ? state.Revision : null;
    }

    public bool Commit(string snapshot, string description)
    {
        if (_states.Count > 0 && string.Equals(CurrentSnapshot, snapshot, StringComparison.Ordinal)) return false;
        if (CanRedo) _states.RemoveRange(_position + 1, _states.Count - _position - 1);
        _states.Add(new State(++_nextRevision, snapshot, description));
        _position = _states.Count - 1;
        while (_states.Count > _capacity + 1)
        {
            _states.RemoveAt(0);
            _position--;
        }
        return true;
    }

    public string? Undo() => Move(-1);
    public string? Redo() => Move(1);
    public void MarkSaved() => _savedRevision = _states[_position].Revision;

    private string? Move(int offset)
    {
        int target = _position + offset;
        if (target < 0 || target >= _states.Count) return null;
        _position = target;
        return CurrentSnapshot;
    }
}
