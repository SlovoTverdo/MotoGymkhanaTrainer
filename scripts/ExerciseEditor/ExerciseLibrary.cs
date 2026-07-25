namespace MotoGymkhanaTrainer.ExerciseEditor;

/// <summary>One folder or Exercise JSON shown by the library browser.</summary>
public sealed record ExerciseLibraryEntry(
    string RelativePath,
    string DisplayName,
    bool IsDirectory,
    int Depth);

/// <summary>
/// Exercise-specific facade over the shared sandbox. Exercise paths remain
/// separate from exercise.id, so editing identity never renames an existing file.
/// </summary>
public sealed class ExerciseLibrary
{
    private readonly SandboxedJsonLibrary _library;

    /// <summary>Creates res://exercises/ when necessary.</summary>
    public ExerciseLibrary(string rootPath)
    {
        _library = new SandboxedJsonLibrary(rootPath, "Exercise library", "res://exercises/");
    }

    public string RootPath => _library.RootPath;

    public IReadOnlyList<ExerciseLibraryEntry> EnumerateEntries() =>
        _library.EnumerateEntries()
            .Select(entry => new ExerciseLibraryEntry(
                entry.RelativePath, entry.DisplayName, entry.IsDirectory, entry.Depth))
            .ToArray();

    public string CreateFolder(string parentRelativePath, string folderName) =>
        _library.CreateFolder(parentRelativePath, folderName);

    public string ResolveFolder(string relativePath) => _library.ResolveFolder(relativePath);

    public string ResolveExistingJson(string path) => _library.ResolveExistingJson(path);

    public string ResolveSaveJson(string folderRelativePath, string fileName) =>
        _library.ResolveSaveJson(folderRelativePath, fileName);

    public string ResolveUserPath(string path) => _library.ResolveUserPath(path);

    public static string SuggestFileName(string exerciseId) =>
        SandboxedJsonLibrary.SuggestFileName(exerciseId, "exercise");

    public string ToRelative(string absolutePath) => _library.ToRelative(absolutePath);
}
