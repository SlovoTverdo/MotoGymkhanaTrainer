namespace MotoGymkhanaTrainer.TrackEditor;

/// <summary>
/// Bounded snapshot history for the persisted Track Project state. Runtime
/// definitions, selection, viewport and locks never enter these snapshots.
/// </summary>
public sealed class TrackProjectHistory(int capacity = 100) : EditorSnapshotHistory(capacity);
