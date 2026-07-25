namespace MotoGymkhanaTrainer;

/// <summary>One folder or JSON document exposed by a sandboxed project library.</summary>
public sealed record JsonLibraryEntry(
    string RelativePath,
    string DisplayName,
    bool IsDirectory,
    int Depth);

/// <summary>
/// Sandboxed filesystem gateway shared by Exercise and Track Project libraries.
/// Every supplied path is normalized and checked against one pinned root. Existing
/// filesystem links are rejected because lexical path checks alone cannot prevent a
/// junction or symlink from escaping <see cref="RootPath"/>.
/// </summary>
public sealed class SandboxedJsonLibrary
{
    private readonly string _rootWithSeparator;
    private readonly string _displayName;
    private readonly string _resourceRoot;

    /// <summary>Creates the library root and pins all subsequent operations to it.</summary>
    public SandboxedJsonLibrary(string rootPath, string displayName, string resourceRoot)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || string.IsNullOrWhiteSpace(displayName) ||
            string.IsNullOrWhiteSpace(resourceRoot))
        {
            throw new ArgumentException("Library root, display name and resource root must be non-empty.");
        }

        RootPath = Path.GetFullPath(rootPath);
        Directory.CreateDirectory(RootPath);
        _displayName = displayName;
        _resourceRoot = resourceRoot.TrimEnd('/') + "/";
        _rootWithSeparator = RootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
    }

    /// <summary>Canonical absolute filesystem path of this library.</summary>
    public string RootPath { get; }

    /// <summary>Returns folders and JSON files recursively in deterministic display order.</summary>
    public IReadOnlyList<JsonLibraryEntry> EnumerateEntries()
    {
        var result = new List<JsonLibraryEntry>();
        EnumerateFolder(RootPath, depth: 0, result);
        return result;
    }

    /// <summary>Creates one child folder; nested structure comes from the selected parent.</summary>
    public string CreateFolder(string parentRelativePath, string folderName)
    {
        ValidateLeafName(folderName, "folder");
        string parent = ResolveFolder(parentRelativePath);
        string candidate = ResolveInside(Path.Combine(ToRelative(parent), folderName.Trim()));
        if (Directory.Exists(candidate))
        {
            throw new IOException(
                $"{_displayName} folder '{folderName.Trim()}' already exists in the selected folder.");
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
            throw new DirectoryNotFoundException($"{_displayName} folder '{relativePath}' does not exist.");
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
            throw new FileNotFoundException($"{_displayName} JSON does not exist.", candidate);
        }

        return candidate;
    }

    /// <summary>Resolves a target JSON path inside an already existing selected folder.</summary>
    public string ResolveSaveJson(string folderRelativePath, string fileName)
    {
        ValidateLeafName(fileName, "file");
        ValidateJsonExtension(fileName);
        string folder = ResolveFolder(folderRelativePath);
        return ResolveInside(Path.Combine(ToRelative(folder), fileName.Trim()));
    }

    /// <summary>Validates an absolute or relative FileDialog path against this library sandbox.</summary>
    public string ResolveUserPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidDataException($"{_displayName} path is empty.");
        }

        string candidate = Path.IsPathRooted(path) ? Path.GetFullPath(path) : ResolveInside(path);
        EnsureInsideRoot(candidate);
        EnsureNoReparsePoints(candidate);
        return candidate;
    }

    /// <summary>Returns a safe JSON filename suggestion derived from a stable domain id.</summary>
    public static string SuggestFileName(string domainId, string fallbackStem)
    {
        string source = string.IsNullOrWhiteSpace(domainId) ? fallbackStem : domainId.Trim();
        char[] invalid = Path.GetInvalidFileNameChars();
        var characters = source.Select(character =>
            invalid.Contains(character) || char.IsWhiteSpace(character) ? '-' : character).ToArray();
        string stem = new string(characters).Trim('.', '-', ' ');
        return $"{(string.IsNullOrWhiteSpace(stem) ? fallbackStem : stem)}.json";
    }

    /// <summary>Returns a normalized library-relative path.</summary>
    public string ToRelative(string absolutePath)
    {
        string candidate = Path.GetFullPath(absolutePath);
        EnsureInsideRoot(candidate);
        string relative = Path.GetRelativePath(RootPath, candidate);
        return relative == "." ? string.Empty : relative;
    }

    private string ResolveInside(string? relativePath)
    {
        if (Path.IsPathRooted(relativePath ?? string.Empty))
        {
            throw new InvalidDataException($"{_displayName} paths must be relative to {_resourceRoot}.");
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
            throw new InvalidDataException($"Path traversal outside {_resourceRoot} is not allowed.");
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
                    $"Filesystem links are not allowed inside {_resourceRoot} because they can escape the sandbox.");
            }
        }
    }

    private void EnumerateFolder(string folder, int depth, ICollection<JsonLibraryEntry> output)
    {
        foreach (string directory in Directory.EnumerateDirectories(folder)
                     .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
        {
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            {
                continue;
            }

            output.Add(new JsonLibraryEntry(ToRelative(directory), Path.GetFileName(directory), true, depth));
            EnumerateFolder(directory, depth + 1, output);
        }

        foreach (string file in Directory.EnumerateFiles(folder, "*.json")
                     .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
        {
            output.Add(new JsonLibraryEntry(ToRelative(file), Path.GetFileName(file), false, depth));
        }
    }

    private void ValidateLeafName(string? value, string kind)
    {
        string trimmed = value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed is "." or ".." ||
            trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            trimmed.Contains(Path.DirectorySeparatorChar) || trimmed.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new InvalidDataException($"Invalid {_displayName} {kind} name '{value}'.");
        }
    }

    private void ValidateJsonExtension(string path)
    {
        if (!string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"{_displayName} files must use the .json extension.");
        }
    }
}
