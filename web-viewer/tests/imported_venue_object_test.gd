extends SceneTree

const WebViewer := preload("res://scripts/web_viewer.gd")


func _initialize() -> void:
	var viewer := WebViewer.new()
	var venue_root := Node3D.new()
	viewer.add_child(venue_root)
	viewer._venue_root = venue_root
	viewer._create_venue_objects([{
		"id": "imported-smoke",
		"visibleInViewer": true,
		"objectType": "imported",
		"assetId": "venue-object-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
		"assetPath": "res://Assets/Venue/Imported/venue-object-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/scene.tscn",
		"collisionMode": "generated",
	}])
	var objects_root := venue_root.get_node_or_null("Objects")
	if objects_root == null or objects_root.get_child_count() != 0:
		push_error("Imported Venue object was not skipped cleanly.")
		viewer.free()
		quit(1)
		return
	print("Imported Venue object graceful-skip test passed.")
	viewer.free()
	quit()
