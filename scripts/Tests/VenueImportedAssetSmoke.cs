using Godot;
using MotoGymkhanaTrainer.VenueEditor;
using MotoGymkhanaTrainer.Viewer;

namespace MotoGymkhanaTrainer.Tests;

/// <summary>Godot-process smoke test for native GLTF import and generated collision.</summary>
public partial class VenueImportedAssetSmoke : Node
{
    public override async void _Ready()
    {
        var createdAssetIds = new List<string>();
        string? temporaryGltfDirectory = null;
        try
        {
            var library = new VenueImportedAssetLibrary();
            int before = library.Enumerate().Count;
            string source = ProjectSettings.GlobalizePath("res://venues/shared_assets/models/Fence_Post.glb");
            VenueImportedAssetResult imported = library.Import(source);
            TrackCreated(imported);
            Require(imported.Asset.AssetId.StartsWith("venue-object-", StringComparison.Ordinal), "stable asset ID prefix");
            Require(imported.Asset.SourcePath.StartsWith("res://Assets/Venue/Imported/", StringComparison.Ordinal), "relative managed source path");
            Require(imported.Asset.RuntimeScenePath.EndsWith("/scene.tscn", StringComparison.Ordinal), "relative runtime wrapper path");
            Require(imported.Asset.Footprint.Width > 0 && imported.Asset.Footprint.Length > 0, "positive measured footprint");

            VenueImportedAssetResult duplicate = library.Import(source);
            Require(duplicate.ReusedExisting && duplicate.Asset.AssetId == imported.Asset.AssetId, "duplicate content reuses stable ID");
            Require(library.Enumerate().Count == before + (imported.ReusedExisting ? 0 : 1), "duplicate creates no silent managed copy");
            library.SetUndoVisible(imported.Asset.AssetId, visible: false);
            Require(library.Find(imported.Asset.AssetId) is null,
                "ImportAsset undo remains hidden in the persistent managed library");
            VenueImportedAssetResult restoredImport = library.Import(source);
            Require(!restoredImport.ReusedExisting && restoredImport.Asset.AssetId == imported.Asset.AssetId,
                "redo/reimport restores the same stable asset instead of duplicating bytes");

            PackedScene? wrapper = ResourceLoader.Load<PackedScene>(imported.Asset.RuntimeScenePath);
            Require(wrapper is not null, "generated wrapper loads as PackedScene");
            Node instance = wrapper!.Instantiate();
            try
            {
                Require(Enumerate(instance).OfType<MeshInstance3D>().Any(), "wrapper retains visual mesh hierarchy");
                Require(Enumerate(instance).OfType<CollisionShape3D>().Any(item => item.Shape is ConcavePolygonShape3D),
                    "wrapper persists generated concave collision");
                AddChild(instance);
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                float centerX = (imported.Asset.Bounds.MinX + imported.Asset.Bounds.MaxX) * 0.5f;
                float centerZ = (imported.Asset.Bounds.MinZ + imported.Asset.Bounds.MaxZ) * 0.5f;
                var query = PhysicsRayQueryParameters3D.Create(
                    new Vector3(centerX, imported.Asset.Bounds.MaxY + 1, centerZ),
                    new Vector3(centerX, imported.Asset.Bounds.MinY - 1, centerZ),
                    ViewerPhysicsLayers.WorldObstacle);
                Require(GetViewport().World3D.DirectSpaceState.IntersectRay(query).Count > 0,
                    "generated collision enters the WorldObstacle physics space");
                RemoveChild(instance);
            }
            finally { instance.Free(); }

            VenueImportedAssetMetadata recalculated = library.Recalculate(imported.Asset.AssetId);
            Require(recalculated.AssetId == imported.Asset.AssetId, "recalculate preserves stable asset identity");
            Require(recalculated.Footprint.Width > 0 && recalculated.Footprint.Length > 0, "recalculate restores positive footprint");

            temporaryGltfDirectory = ProjectSettings.GlobalizePath("user://valid-gltf-import-smoke");
            Directory.CreateDirectory(temporaryGltfDirectory);
            string gltf = Path.Combine(temporaryGltfDirectory, "triangle.gltf");
            string binary = Path.Combine(temporaryGltfDirectory, "triangle.bin");
            File.WriteAllText(gltf, """
                {"asset":{"version":"2.0"},"scene":0,"scenes":[{"nodes":[0]}],"nodes":[{"mesh":0}],
                "meshes":[{"primitives":[{"attributes":{"POSITION":0}}]}],
                "buffers":[{"uri":"triangle.bin","byteLength":36}],
                "bufferViews":[{"buffer":0,"byteOffset":0,"byteLength":36,"target":34962}],
                "accessors":[{"bufferView":0,"byteOffset":0,"componentType":5126,"count":3,"type":"VEC3",
                "min":[0,0,0],"max":[1,0,1]}]}
                """);
            using (var writer = new BinaryWriter(File.Create(binary)))
                foreach (float coordinate in new[] { 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f }) writer.Write(coordinate);
            VenueImportedAssetResult externalGltf = library.Import(gltf);
            TrackCreated(externalGltf);
            string managedDependency = ProjectSettings.GlobalizePath(
                $"{VenueImportedAssetLibrary.ResourceRoot}/{externalGltf.Asset.AssetId}/triangle.bin");
            Require(File.Exists(managedDependency), "external glTF buffer is copied into its managed asset directory");
            Require(!externalGltf.Asset.SourcePath.Contains(temporaryGltfDirectory, StringComparison.OrdinalIgnoreCase),
                "external glTF import persists no absolute source path");
            VenueImportedAssetResult duplicateGltf = library.Import(gltf);
            Require(duplicateGltf.ReusedExisting && duplicateGltf.Asset.AssetId == externalGltf.Asset.AssetId,
                "unchanged glTF and dependencies reuse their stable asset ID");
            using (var writer = new BinaryWriter(File.Open(binary, FileMode.Open, System.IO.FileAccess.Write)))
                writer.Write(0.25f);
            VenueImportedAssetResult changedDependency = library.Import(gltf);
            TrackCreated(changedDependency);
            Require(!changedDependency.ReusedExisting && changedDependency.Asset.AssetId != externalGltf.Asset.AssetId,
                "changed external glTF dependency creates a distinct content identity");

            string traversalDirectory = Path.Combine(temporaryGltfDirectory, "traversal");
            Directory.CreateDirectory(traversalDirectory);
            string traversalGltf = Path.Combine(traversalDirectory, "escape.gltf");
            File.WriteAllText(traversalGltf,
                "{\"asset\":{\"version\":\"2.0\"},\"buffers\":[{\"uri\":\"../triangle.bin\",\"byteLength\":36}]}");
            int beforeTraversal = library.Enumerate().Count;
            bool traversalRejected = false;
            try { library.Import(traversalGltf); }
            catch (InvalidDataException) { traversalRejected = true; }
            Require(traversalRejected, "glTF dependency path traversal is rejected");
            Require(library.Enumerate().Count == beforeTraversal,
                "rejected glTF dependency creates no partial managed entry");

            string invalid = ProjectSettings.GlobalizePath("user://invalid-import-smoke.glb");
            File.WriteAllText(invalid, "not a glTF binary");
            int beforeInvalid = library.Enumerate().Count;
            bool rejected = false;
            try { library.Import(invalid); }
            catch (InvalidDataException) { rejected = true; }
            File.Delete(invalid);
            Require(rejected, "invalid GLB is rejected");
            Require(library.Enumerate().Count == beforeInvalid, "failed import exposes no partial asset entry");

            bool missingRejected = false;
            try { library.Import(ProjectSettings.GlobalizePath("user://missing-import-smoke.glb")); }
            catch (FileNotFoundException) { missingRejected = true; }
            Require(missingRejected, "missing GLB is rejected clearly");
            GD.Print("VENUE_IMPORTED_ASSET_SMOKE_PASS");
        }
        catch (Exception exception)
        {
            GD.PushError($"VENUE_IMPORTED_ASSET_SMOKE_FAIL: {exception}");
            GetTree().Quit(1);
            return;
        }
        finally
        {
            foreach (string createdAssetId in createdAssetIds)
            {
                string directory = ProjectSettings.GlobalizePath(
                    $"{VenueImportedAssetLibrary.ResourceRoot}/{createdAssetId}");
                if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
            }
            if (temporaryGltfDirectory is not null && Directory.Exists(temporaryGltfDirectory))
                Directory.Delete(temporaryGltfDirectory, recursive: true);
        }
        GetTree().Quit();

        void TrackCreated(VenueImportedAssetResult result)
        {
            if (!result.ReusedExisting) createdAssetIds.Add(result.Asset.AssetId);
        }
    }

    private static IEnumerable<Node> Enumerate(Node root)
    {
        yield return root;
        foreach (Node child in root.GetChildren())
            foreach (Node nested in Enumerate(child)) yield return nested;
    }

    private static void Require(bool condition, string description)
    {
        if (!condition) throw new InvalidDataException($"Smoke assertion failed: {description}.");
        GD.Print($"PASS: {description}");
    }
}
