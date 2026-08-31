using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Godot;
using MotoGymkhanaTrainer.Viewer;

namespace MotoGymkhanaTrainer.VenueEditor;

/// <summary>
/// Imports GLB/glTF through Godot's native GLTFDocument API and stores a stable,
/// project-owned PackedScene wrapper plus cached authoring metadata.
/// </summary>
public sealed class VenueImportedAssetLibrary
{
    public const string ResourceRoot = "res://Assets/Venue/Imported";
    private const string MetadataFileName = "metadata.json";
    private const string RuntimeSceneFileName = "scene.tscn";
    private const string UndoHiddenFileName = ".undo-hidden";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _filesystemRoot;

    public VenueImportedAssetLibrary()
    {
        _filesystemRoot = ProjectSettings.GlobalizePath(ResourceRoot);
        Directory.CreateDirectory(_filesystemRoot);
    }

    /// <summary>Lists only complete metadata entries; partial directories are never exposed.</summary>
    public IReadOnlyList<VenueImportedAssetMetadata> Enumerate() => EnumerateInternal(includeUndoHidden: false);

    private IReadOnlyList<VenueImportedAssetMetadata> EnumerateInternal(bool includeUndoHidden)
    {
        if (!Directory.Exists(_filesystemRoot)) return [];
        var assets = new List<VenueImportedAssetMetadata>();
        foreach (string directory in Directory.EnumerateDirectories(_filesystemRoot))
        {
            if (!includeUndoHidden && File.Exists(Path.Combine(directory, UndoHiddenFileName))) continue;
            VenueImportedAssetMetadata? metadata = TryLoadMetadata(Path.Combine(directory, MetadataFileName));
            if (metadata is not null && IsValidMetadata(metadata, directory) &&
                File.Exists(ProjectSettings.GlobalizePath(metadata.RuntimeScenePath)))
                assets.Add(metadata);
        }
        return assets.OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.AssetId, StringComparer.Ordinal).ToArray();
    }

    /// <summary>
    /// Imports a filesystem source. Equal bytes reuse the existing stable asset
    /// identity instead of silently producing a duplicate managed copy.
    /// </summary>
    public VenueImportedAssetResult Import(string sourceFile)
    {
        string source = Path.GetFullPath(sourceFile);
        ValidateSource(source);
        ValidateFileSignature(source);
        string hash = ComputeContentHash(source);
        VenueImportedAssetMetadata? duplicate = EnumerateInternal(includeUndoHidden: true)
            .FirstOrDefault(item => string.Equals(item.ContentSha256, hash, StringComparison.Ordinal));
        if (duplicate is not null)
        {
            string hiddenMarker = Path.Combine(_filesystemRoot, duplicate.AssetId, UndoHiddenFileName);
            bool restoredUndo = File.Exists(hiddenMarker);
            if (restoredUndo) File.Delete(hiddenMarker);
            return new VenueImportedAssetResult(duplicate, !restoredUndo);
        }

        string assetId = $"venue-object-{Guid.NewGuid():N}";
        string directory = Path.Combine(_filesystemRoot, assetId);
        Directory.CreateDirectory(directory);
        try
        {
            string extension = Path.GetExtension(source).ToLowerInvariant();
            string managedSource = Path.Combine(directory, "source" + extension);
            File.Copy(source, managedSource, overwrite: false);
            CopyGltfDependencies(source, managedSource);
            VenueImportedAssetMetadata metadata = BuildAsset(
                assetId,
                Path.GetFileNameWithoutExtension(source),
                Path.GetFileName(source),
                hash,
                managedSource);
            SaveMetadata(metadata);
            return new VenueImportedAssetResult(metadata, false);
        }
        catch
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
            throw;
        }
    }

    /// <summary>Rebuilds bounds, footprint and generated collision without moving any instance.</summary>
    public VenueImportedAssetMetadata Recalculate(string assetId)
    {
        VenueImportedAssetMetadata existing = Find(assetId)
            ?? throw new InvalidDataException($"Imported Venue asset '{assetId}' is missing.");
        string source = ProjectSettings.GlobalizePath(existing.SourcePath);
        if (!File.Exists(source)) throw new FileNotFoundException("Managed imported source is missing.", source);
        VenueImportedAssetMetadata rebuilt = BuildAsset(
            existing.AssetId,
            existing.DisplayName,
            existing.SourceFile,
            ComputeContentHash(source),
            source);
        SaveMetadata(rebuilt);
        ResourceLoader.Load<PackedScene>(rebuilt.RuntimeScenePath, cacheMode: ResourceLoader.CacheMode.Replace);
        return rebuilt;
    }

    public VenueImportedAssetMetadata? Find(string assetId) => Enumerate()
        .FirstOrDefault(item => string.Equals(item.AssetId, assetId, StringComparison.Ordinal));

    /// <summary>
    /// Persists ImportAsset Undo/Redo without deleting shared managed bytes.
    /// A later import of the same content reactivates the same stable asset ID.
    /// </summary>
    public void SetUndoVisible(string assetId, bool visible)
    {
        if (!IsValidAssetId(assetId)) throw new ArgumentException("Imported Venue asset ID is invalid.", nameof(assetId));
        string directory = Path.Combine(_filesystemRoot, assetId);
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException($"Managed imported asset '{assetId}' is missing.");
        string marker = Path.Combine(directory, UndoHiddenFileName);
        if (visible)
        {
            if (File.Exists(marker)) File.Delete(marker);
        }
        else if (!File.Exists(marker)) File.WriteAllText(marker, "ImportAsset undone. Reimport the same content to restore this stable asset.\n", new UTF8Encoding(false));
    }

    /// <summary>Checks the canonical stable identity generated for managed Venue assets.</summary>
    public static bool IsValidAssetId(string? assetId) => assetId is not null &&
        assetId.StartsWith("venue-object-", StringComparison.Ordinal) &&
        assetId.Length == "venue-object-".Length + 32 &&
        assetId["venue-object-".Length..].All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static void ValidateSource(string source)
    {
        if (!File.Exists(source)) throw new FileNotFoundException("GLB/glTF source file does not exist.", source);
        string extension = Path.GetExtension(source);
        if (!extension.Equals(".glb", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".gltf", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Imported Venue asset must use the .glb or .gltf extension.");
    }

    private static void ValidateFileSignature(string source)
    {
        string extension = Path.GetExtension(source);
        using FileStream stream = File.OpenRead(source);
        if (extension.Equals(".glb", StringComparison.OrdinalIgnoreCase))
        {
            Span<byte> magic = stackalloc byte[4];
            if (stream.Read(magic) != magic.Length || magic[0] != (byte)'g' || magic[1] != (byte)'l' ||
                magic[2] != (byte)'T' || magic[3] != (byte)'F')
                throw new InvalidDataException("GLB header is invalid.");
            return;
        }
        int value;
        do value = stream.ReadByte(); while (value >= 0 && char.IsWhiteSpace((char)value));
        if (value != '{') throw new InvalidDataException("glTF JSON header is invalid.");
    }

    private VenueImportedAssetMetadata BuildAsset(
        string assetId,
        string displayName,
        string sourceFile,
        string hash,
        string managedSource)
    {
        string directory = Path.GetDirectoryName(managedSource)!;
        string sourcePath = ToResourcePath(managedSource);
        string runtimeScenePath = ToResourcePath(Path.Combine(directory, RuntimeSceneFileName));
        var document = new GltfDocument();
        var state = new GltfState();
        Error importError = document.AppendFromFile(managedSource, state);
        if (importError != Error.Ok)
            throw new InvalidDataException($"Godot GLTF importer rejected '{sourceFile}' ({importError}).");
        Node generated = document.GenerateScene(state)
            ?? throw new InvalidDataException($"Godot GLTF importer produced no scene for '{sourceFile}'.");
        try
        {
            if (generated is not Node3D importedRoot)
                throw new InvalidDataException("Imported GLB/glTF root must be Node3D.");
            RemoveRuntimeHelpers(importedRoot);
            VenueImportedBoundsMetadata bounds = MeasureBounds(importedRoot, sourcePath, out FootprintDto footprint);
            using var wrapper = new Node3D { Name = "ImportedVenueAsset" };
            generated.Name = "Model";
            wrapper.AddChild(generated);
            SetOwnerRecursive(generated, wrapper);
            AddGeneratedCollision(wrapper, importedRoot);
            var packed = new PackedScene();
            Error packError = packed.Pack(wrapper);
            if (packError != Error.Ok) throw new InvalidDataException($"Failed to pack imported scene ({packError}).");
            Error saveError = ResourceSaver.Save(packed, runtimeScenePath);
            if (saveError != Error.Ok) throw new InvalidDataException($"Failed to save imported runtime scene ({saveError}).");
            generated = null!; // wrapper owns and frees the generated hierarchy.
            return new VenueImportedAssetMetadata
            {
                AssetId = assetId,
                DisplayName = displayName,
                SourceFile = sourceFile,
                ContentSha256 = hash,
                SourcePath = sourcePath,
                RuntimeScenePath = runtimeScenePath,
                Bounds = bounds,
                Footprint = footprint,
                CollisionMode = "generated",
            };
        }
        finally
        {
            if (GodotObject.IsInstanceValid(generated)) generated.Free();
        }
    }

    private static VenueImportedBoundsMetadata MeasureBounds(
        Node3D root,
        string assetPath,
        out FootprintDto footprint)
    {
        VenueAssetVisualBounds[] visuals = Enumerate(root).OfType<VisualInstance3D>()
            .Select(visual => new VenueAssetVisualBounds(visual.GetAabb(), TransformToRoot(visual, root))).ToArray();
        footprint = VenueAssetFootprint.Calculate(visuals, assetPath);
        bool found = false;
        Vector3 minimum = new(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        Vector3 maximum = new(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
        foreach (VenueAssetVisualBounds visual in visuals)
        {
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 p = visual.Bounds.Position + new Vector3(
                    (corner & 1) == 0 ? 0 : visual.Bounds.Size.X,
                    (corner & 2) == 0 ? 0 : visual.Bounds.Size.Y,
                    (corner & 4) == 0 ? 0 : visual.Bounds.Size.Z);
                p = visual.ToAssetRoot * p;
                minimum = new Vector3(MathF.Min(minimum.X, p.X), MathF.Min(minimum.Y, p.Y), MathF.Min(minimum.Z, p.Z));
                maximum = new Vector3(MathF.Max(maximum.X, p.X), MathF.Max(maximum.Y, p.Y), MathF.Max(maximum.Z, p.Z));
                found = true;
            }
        }
        if (!found) throw new InvalidDataException($"Imported asset '{assetPath}' contains no visual geometry.");
        return new VenueImportedBoundsMetadata
        {
            MinX = minimum.X, MinY = minimum.Y, MinZ = minimum.Z,
            MaxX = maximum.X, MaxY = maximum.Y, MaxZ = maximum.Z,
        };
    }

    private static void AddGeneratedCollision(Node3D wrapper, Node3D importedRoot)
    {
        var body = new StaticBody3D
        {
            Name = "GeneratedCollision",
            CollisionLayer = ViewerPhysicsLayers.WorldObstacle,
            CollisionMask = 0,
        };
        wrapper.AddChild(body);
        body.Owner = wrapper;
        int index = 0;
        foreach (MeshInstance3D visual in Enumerate(importedRoot).OfType<MeshInstance3D>())
        {
            if (visual.Mesh is null || visual.Mesh.GetSurfaceCount() == 0) continue;
            Shape3D? shape = visual.Mesh.CreateTrimeshShape();
            if (shape is null) continue;
            var collision = new CollisionShape3D
            {
                Name = $"MeshCollision{++index}",
                Shape = shape,
                Transform = TransformToRoot(visual, wrapper),
            };
            body.AddChild(collision);
            collision.Owner = wrapper;
        }
        if (index == 0) throw new InvalidDataException("Imported asset produced no collision triangles.");
    }

    private static void RemoveRuntimeHelpers(Node root)
    {
        foreach (Node child in root.GetChildren().ToArray())
        {
            if (child is Camera3D or Light3D or AudioStreamPlayer3D)
            {
                root.RemoveChild(child);
                child.Free();
                continue;
            }
            RemoveRuntimeHelpers(child);
        }
    }

    private static IEnumerable<Node> Enumerate(Node root)
    {
        yield return root;
        foreach (Node child in root.GetChildren())
            foreach (Node descendant in Enumerate(child)) yield return descendant;
    }

    private static Transform3D TransformToRoot(Node3D node, Node3D root)
    {
        if (node == root) return Transform3D.Identity;
        Transform3D result = node.Transform;
        Node? ancestor = node.GetParent();
        while (ancestor is not null && ancestor != root)
        {
            if (ancestor is Node3D spatial) result = spatial.Transform * result;
            ancestor = ancestor.GetParent();
        }
        if (ancestor != root) throw new InvalidDataException("Imported visual is outside its asset root.");
        return result;
    }

    private static void SetOwnerRecursive(Node node, Node owner)
    {
        node.Owner = owner;
        foreach (Node child in node.GetChildren()) SetOwnerRecursive(child, owner);
    }

    private void SaveMetadata(VenueImportedAssetMetadata metadata)
    {
        string directory = Path.Combine(_filesystemRoot, metadata.AssetId);
        File.WriteAllText(Path.Combine(directory, MetadataFileName),
            JsonSerializer.Serialize(metadata, JsonOptions), new UTF8Encoding(false));
    }

    private static VenueImportedAssetMetadata? TryLoadMetadata(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<VenueImportedAssetMetadata>(File.ReadAllText(path, Encoding.UTF8))
                : null;
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            GD.PushWarning($"Imported Venue asset metadata '{path}' was skipped: {exception.Message}");
            return null;
        }
    }

    private static bool IsValidMetadata(VenueImportedAssetMetadata metadata, string directory)
    {
        string assetId = Path.GetFileName(directory);
        string expectedRoot = $"{ResourceRoot}/{assetId}";
        bool idValid = IsValidAssetId(assetId);
        bool sourcePathValid = metadata.SourcePath is not null &&
            (string.Equals(metadata.SourcePath, $"{expectedRoot}/source.glb", StringComparison.Ordinal) ||
             string.Equals(metadata.SourcePath, $"{expectedRoot}/source.gltf", StringComparison.Ordinal));
        bool numbersValid = metadata.Footprint is not null && metadata.Bounds is not null &&
            metadata.Footprint.Width > 0 && metadata.Footprint.Length > 0 &&
            float.IsFinite(metadata.Footprint.Width) && float.IsFinite(metadata.Footprint.Length) &&
            float.IsFinite(metadata.Footprint.CenterX) && float.IsFinite(metadata.Footprint.CenterY) &&
            float.IsFinite(metadata.Bounds.MinX) && float.IsFinite(metadata.Bounds.MinY) &&
            float.IsFinite(metadata.Bounds.MinZ) && float.IsFinite(metadata.Bounds.MaxX) &&
            float.IsFinite(metadata.Bounds.MaxY) && float.IsFinite(metadata.Bounds.MaxZ) &&
            metadata.Bounds.MinX <= metadata.Bounds.MaxX && metadata.Bounds.MinY <= metadata.Bounds.MaxY &&
            metadata.Bounds.MinZ <= metadata.Bounds.MaxZ;
        bool hashValid = metadata.ContentSha256 is not null && metadata.ContentSha256.Length == 64 &&
            metadata.ContentSha256.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
        bool valid = idValid && string.Equals(metadata.AssetId, assetId, StringComparison.Ordinal) &&
            sourcePathValid &&
            string.Equals(metadata.RuntimeScenePath, $"{expectedRoot}/{RuntimeSceneFileName}", StringComparison.Ordinal) &&
            string.Equals(metadata.CollisionMode, "generated", StringComparison.Ordinal) && numbersValid && hashValid;
        if (!valid) GD.PushWarning($"Imported Venue asset metadata '{assetId}' failed managed-path or value validation.");
        return valid;
    }

    private static string ComputeContentHash(string source)
    {
        if (!Path.GetExtension(source).Equals(".gltf", StringComparison.OrdinalIgnoreCase))
            return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source))).ToLowerInvariant();

        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(File.ReadAllBytes(source));
        foreach ((string relativePath, string dependencyPath) in EnumerateGltfDependencies(source))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(relativePath));
            hash.AppendData(File.ReadAllBytes(dependencyPath));
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static void CopyGltfDependencies(string source, string managedSource)
    {
        if (!Path.GetExtension(source).Equals(".gltf", StringComparison.OrdinalIgnoreCase)) return;
        string managedDirectory = Path.GetDirectoryName(managedSource)!;
        foreach ((string relativePath, string dependencyPath) in EnumerateGltfDependencies(source))
        {
            string destination = Path.GetFullPath(Path.Combine(managedDirectory,
                relativePath.Replace('/', Path.DirectorySeparatorChar)));
            string prefix = managedDirectory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!destination.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"glTF dependency '{relativePath}' escapes its managed asset directory.");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(dependencyPath, destination, overwrite: false);
        }
    }

    private static IReadOnlyList<(string RelativePath, string SourcePath)> EnumerateGltfDependencies(string source)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(source, Encoding.UTF8));
        var dependencies = new SortedDictionary<string, string>(StringComparer.Ordinal);
        CollectUris("buffers");
        CollectUris("images");
        return dependencies.Select(pair => (pair.Key, pair.Value)).ToArray();

        void CollectUris(string propertyName)
        {
            if (!document.RootElement.TryGetProperty(propertyName, out JsonElement entries) ||
                entries.ValueKind != JsonValueKind.Array) return;
            foreach (JsonElement entry in entries.EnumerateArray())
            {
                if (!entry.TryGetProperty("uri", out JsonElement uriElement) ||
                    uriElement.ValueKind != JsonValueKind.String) continue;
                string raw = uriElement.GetString() ?? string.Empty;
                if (raw.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;
                string relative = Uri.UnescapeDataString(raw).Replace('\\', '/');
                if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) ||
                    Uri.TryCreate(relative, UriKind.Absolute, out _))
                    throw new InvalidDataException($"glTF dependency URI '{raw}' must be a relative project asset path.");
                string sourceDirectory = Path.GetDirectoryName(source)!;
                string dependency = Path.GetFullPath(Path.Combine(sourceDirectory,
                    relative.Replace('/', Path.DirectorySeparatorChar)));
                string prefix = sourceDirectory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!dependency.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"glTF dependency URI '{raw}' escapes its source directory.");
                if (!File.Exists(dependency))
                    throw new FileNotFoundException($"glTF dependency '{raw}' does not exist.", dependency);
                dependencies[relative] = dependency;
            }
        }
    }

    private static string ToResourcePath(string filesystemPath)
    {
        string root = Path.GetFullPath(ProjectSettings.GlobalizePath("res://"));
        string candidate = Path.GetFullPath(filesystemPath);
        string prefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Managed imported asset escaped the project root.");
        return "res://" + Path.GetRelativePath(root, candidate).Replace('\\', '/');
    }
}
