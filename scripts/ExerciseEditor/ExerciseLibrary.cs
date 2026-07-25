namespace MotoGymkhanaTrainer.ExerciseEditor;

/// <summary>One folder or Exercise JSON shown by the library browser.</summary>
public sealed record ExerciseLibraryEntry(
    string RelativePath,
    string DisplayName,
    bool IsDirectory,
    int Depth);

/// <summary>
/// Sandboxed filesystem gateway for the user-organized Exercise library.
/// Library paths are deliberately separate from exercise.id: moving or saving a
/// definition never changes its stable domain identity, and editing the identity
/// never silently renames an existing file.
/// </summary>
public sealed class ExerciseLibrary
{
    private readonly string _rootWithSeparator;

    /// <summary>Creates the root when necessary and pins all later operations to it.</summary>
    public ExerciseLibrary(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new ArgumentException("Exercise library root must not be empty.", nameof(rootPath));
        }

        RootPath = Path.GetFullPath(rootPath);
        Directory.CreateDirectory(RootPath);
        _rootWithSeparator = RootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
    }

    /// <summary>Canonical absolute filesystem path corresponding to res://exercises/.</summary>
    public string RootPath { get; }

    /// <summary>Returns folders and JSON files recursively in deterministic display order.</summary>
    public IReadOnlyList<ExerciseLibraryEntry> EnumerateEntries()
    {
        var result = new List<ExerciseLibraryEntry>();
        EnumerateFolder(RootPath, depth: 0, result);
        return result;
    }

    /// <summary>Creates one child folder after validating its single path component.</summary>
    public string CreateFolder(string parentRelativePath, string folderName)
    {
        ValidateLeafName(folderName, "folder");
        string parent = ResolveFolder(parentRelativePath);
        string candidate = ResolveInside(Path.Combine(ToRelative(parent), folderName.Trim()));
        if (Directory.Exists(candidate))
        {
            throw new IOException(
                $"Exercise library folder '{folderName.Trim()}' already exists in the selected folder.");
        }

        Directory.CreateDirectory(candidate);
        return ToRelative(candidate);
    }

    /// <summary>Resolves a selected relative folder and rejects files or traversal.</summary>
    public string ResolveFolder(string relativePath)
    {
        string candidate = ResolveInside(relativePath);
        if (!Directory.Exists(candidate))
        {
            throw new DirectoryNotFoundException($"Exercise library folder '{relativePath}' does not exist.");
        }

        return candidate;
    }

    /// <summary>Resolves an existing library JSON without permitting an escape from the root.</summary>
    public string ResolveExistingJson(string path)
    {
        string candidate = ResolveUserPath(path);
        ValidateJsonExtension(candidate);
        if (!File.Exists(candidate))
        {
            throw new FileNotFoundException("Exercise JSON does not exist.", candidate);
        }

        return candidate;
    }

    /// <summary>Resolves a target JSON file; only its selected folder may be created separately.</summary>
    public string ResolveSaveJson(string folderRelativePath, string fileName)
    {
        ValidateLeafName(fileName, "file");
        ValidateJsonExtension(fileName);
        string folder = ResolveFolder(folderRelativePath);
        return ResolveInside(Path.Combine(ToRelative(folder), fileName.Trim()));
    }

    /// <summary>Validates an absolute or relative FileDialog path against the library sandbox.</summary>
    public string ResolveUserPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidDataException("Exercise library path is empty.");
        }

        string candidate = Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : ResolveInside(path);
        EnsureInsideRoot(candidate);
        EnsureNoReparsePoints(candidate);
        return candidate;
    }

    /// <summary>Returns a safe filename suggestion derived from exercise.id.</summary>
    public static string SuggestFileName(string exerciseId)
    {
        string source = string.IsNullOrWhiteSpace(exerciseId) ? "exercise" : exerciseId.Trim();
        char[] invalid = Path.GetInvalidFileNameChars();
        var characters = source.Select(character =>
            invalid.Contains(character) || char.IsWhiteSpace(character) ? '-' : character).ToArray();
        string stem = new string(characters).Trim('.', '-', ' ');
        return $"{(string.IsNullOrWhiteSpace(stem) ? "exercise" : stem)}.json";
    }

    /// <summary>Returns a library-relative path using platform separators.</summary>
    public string ToRelative(string absolutePath)
    {
        string candidate = Path.GetFullPath(absolutePath);
        EnsureInsideRoot(candidate);
        string relative = Path.GetRelativePath(RootPath, candidate);
        return relative == "." ? string.Empty : relative;
    }

    private string ResolveInside(string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
        {
            throw new InvalidDataException("Exercise library paths must be relative to res://exercises/.");
        }

        string candidate = Path.GetFullPath(Path.Combine(RootPath, relativePath ?? string.Empty));
        EnsureInsideRoot(candidate);
        EnsureNoReparsePoints(candidate);
        return candidate;
    }

    private void EnsureInsideRoot(string candidate)
    {
        if (!candidate.Equals(RootPath, StringComparison.OrdinalIgnoreCase) &&
            !candidate.StartsWith(_rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Path traversal outside res://exercises/ is not allowed.");
        }
    }

    private void EnsureNoReparsePoints(string candidate)
    {
        string relative = Path.GetRelativePath(RootPath, candidate);
        string current = RootPath;
        foreach (string component in relative.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, component);
            if ((Directory.Exists(current) || File.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException(
                    "Filesystem links are not allowed inside res://exercises/ because they can escape the sandbox.");
            }
        }
    }

    private void EnumerateFolder(string folder, int depth, ICollection<ExerciseLibraryEntry> output)
    {
        IEnumerable<string> directories = Directory.EnumerateDirectories(folder)
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase);
        foreach (string directory in directories)
        {
            // Junctions/symlinks are omitted so a filesystem alias cannot bypass
            // the lexical root check and expose files outside the library sandbox.
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            {
                continue;
            }

            output.Add(new ExerciseLibraryEntry(
                ToRelative(directory),
                Path.GetFileName(directory),
                IsDirectory: true,
                depth));
            EnumerateFolder(directory, depth + 1, output);
        }

        foreach (string file in Directory.EnumerateFiles(folder, "*.json")
                     .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
        {
            output.Add(new ExerciseLibraryEntry(
                ToRelative(file),
                Path.GetFileName(file),
                IsDirectory: false,
                depth));
        }
    }

    private static void ValidateLeafName(string value, string kind)
    {
        string trimmed = value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed is "." or ".." ||
            trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            trimmed.Contains(Path.DirectorySeparatorChar) || trimmed.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new InvalidDataException($"Invalid Exercise library {kind} name '{value}'.");
        }
    }

    private static void ValidateJsonExtension(string path)
    {
        if (!string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Exercise library files must use the .json extension.");
        }
    }
}
