class_name SurfaceProjectionService
extends RefCounted

## Centralized downward-query policy. Projection results are runtime-only and
## are never written back to Track v5.

const WALKABLE_SURFACE_MASK := 1

var projection_top_y := 50.0
var projection_bottom_y := -10.0
var surface_visual_offset := 0.03
var fallback_y := 0.0
var diagnostics: Array[String] = []

var _world: World3D
var _warned_sources := {}


func _init(world: World3D) -> void:
	_world = world


func project_point(
	point: Dictionary,
	source_type: String,
	source_id: String,
	visual_offset: float = -1.0,
	ray_start_y: float = NAN
) -> Dictionary:
	var mapped := RuntimeGeometry.domain_to_godot(point)
	return project_godot_xz(
		Vector2(mapped.x, mapped.z), source_type, source_id, visual_offset, ray_start_y
	)


func project_godot_xz(
	godot_xz: Vector2,
	source_type: String,
	source_id: String,
	visual_offset: float = -1.0,
	ray_start_y: float = NAN
) -> Dictionary:
	var offset := surface_visual_offset if visual_offset < 0.0 else visual_offset
	var top := projection_top_y if is_nan(ray_start_y) else ray_start_y
	if not is_finite(top) or top <= projection_bottom_y:
		return {"position": Vector3(godot_xz.x, fallback_y + offset, godot_xz.y), "normal": Vector3.UP, "hit": false}

	var surface := query_walkable_surface(godot_xz, top, projection_bottom_y)
	if surface["hit"]:
		var normal: Vector3 = surface["normal"]
		return {"position": surface["position"] + normal * offset, "normal": normal, "hit": true}

	_warn_once(source_type, source_id, godot_xz)
	return {
		"position": Vector3(godot_xz.x, fallback_y + offset, godot_xz.y),
		"normal": Vector3.UP,
		"hit": false,
	}


func query_walkable_surface(
	godot_xz: Vector2,
	ray_start_y: float = NAN,
	ray_end_y: float = NAN,
	exclude: Array[RID] = []
) -> Dictionary:
	## Raw WalkableSurface query shared by projection and the Fly lower-bound.
	## It deliberately performs no visual offset and no per-frame diagnostics.
	var top := projection_top_y if is_nan(ray_start_y) else ray_start_y
	var bottom := projection_bottom_y if is_nan(ray_end_y) else ray_end_y
	if not is_finite(top) or not is_finite(bottom) or top <= bottom:
		return {"position": Vector3(godot_xz.x, fallback_y, godot_xz.y), "normal": Vector3.UP, "hit": false}
	var query := PhysicsRayQueryParameters3D.create(
		Vector3(godot_xz.x, top, godot_xz.y),
		Vector3(godot_xz.x, bottom, godot_xz.y),
		WALKABLE_SURFACE_MASK
	)
	query.exclude = exclude
	query.collide_with_areas = false
	query.collide_with_bodies = true
	query.hit_from_inside = false
	var hit := _world.direct_space_state.intersect_ray(query)
	if hit.is_empty():
		return {"position": Vector3(godot_xz.x, fallback_y, godot_xz.y), "normal": Vector3.UP, "hit": false}
	return {
		"position": hit["position"],
		"normal": (hit["normal"] as Vector3).normalized(),
		"hit": true,
	}


func project_polyline(
	points: Array,
	source_type: String,
	source_id: String,
	maximum_spacing_meters: float,
	visual_offset: float
) -> Array:
	var projected: Array = []
	for point in subdivide_polyline(points, maximum_spacing_meters):
		projected.append(project_point(point, source_type, source_id, visual_offset))
	return projected


func project_cone_position(point: Dictionary, cone_id: String) -> Vector3:
	return project_point(point, "Cone", cone_id, 0.005)["position"]


static func subdivide_polyline(points: Array, maximum_spacing_meters: float) -> Array:
	if points.is_empty():
		return []
	var samples: Array = [points[0].duplicate(true)]
	for index in range(points.size() - 1):
		var start := Vector2(float(points[index]["x"]), float(points[index]["y"]))
		var end := Vector2(float(points[index + 1]["x"]), float(points[index + 1]["y"]))
		var length := start.distance_to(end)
		if length <= 0.000001:
			continue
		var interval_count := maxi(1, ceili(length / maximum_spacing_meters))
		for interval in range(1, interval_count + 1):
			var point := start.lerp(end, float(interval) / float(interval_count))
			samples.append({"x": point.x, "y": point.y})
	return samples


func _warn_once(source_type: String, source_id: String, godot_xz: Vector2) -> void:
	var key := "%s:%s" % [source_type, source_id]
	if _warned_sources.has(key):
		return
	_warned_sources[key] = true
	var message := "%s '%s' has no WalkableSurface at X=%.3f, Y=%.3f; fallback Y=%.3f was used." % [
		source_type, source_id, godot_xz.x, -godot_xz.y, fallback_y
	]
	diagnostics.append(message)
	push_warning(message)
