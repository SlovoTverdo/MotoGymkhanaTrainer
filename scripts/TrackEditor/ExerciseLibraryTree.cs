using Godot;

namespace MotoGymkhanaTrainer.TrackEditor;

/// <summary>
/// Exercise library tree that exposes only Exercise JSON files as drag data.
/// Folder expansion and selection remain ordinary editor UI state.
/// </summary>
public partial class ExerciseLibraryTree : Tree
{
    internal const string DragPrefix = "exercise-json|";

    /// <inheritdoc />
    public override Variant _GetDragData(Vector2 atPosition)
    {
        TreeItem? item = GetItemAtPosition(atPosition);
        if (item is null)
        {
            return default;
        }

        string metadata = item.GetMetadata(0).AsString();
        if (!metadata.StartsWith("F|", StringComparison.Ordinal) || metadata.Length <= 2)
        {
            return default;
        }

        SetSelected(item, 0);
        string relativePath = metadata[2..];
        var preview = new Label
        {
            Text = $"Add {Path.GetFileName(relativePath)}",
            Modulate = new Color(1.0f, 1.0f, 1.0f, 0.9f),
        };
        SetDragPreview(preview);

        // Only a library-relative path crosses the drag boundary. The receiver
        // resolves it through SandboxedJsonLibrary before touching the document.
        return $"{DragPrefix}{relativePath}";
    }

    /// <summary>Extracts a library-relative Exercise path from a local drag payload.</summary>
    internal static bool TryReadExercisePath(Variant data, out string relativePath)
    {
        relativePath = string.Empty;
        if (data.VariantType != Variant.Type.String)
        {
            return false;
        }

        string value = data.AsString();
        if (!value.StartsWith(DragPrefix, StringComparison.Ordinal) || value.Length <= DragPrefix.Length)
        {
            return false;
        }

        relativePath = value[DragPrefix.Length..];
        return true;
    }
}
