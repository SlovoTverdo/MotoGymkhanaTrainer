class_name TrajectoryHighlightRenderer
extends MeshInstance3D

## Owns the single global trajectory ribbon and its Follow visualization state.
## Route distance is stored in UV2.x; UV, vertex color, and normals remain free
## for the trajectory's existing visual contract.

const FULL_ROUTE := 0
const FOLLOW_WINDOW := 1

const BASE_TRAJECTORY_COLOR := Color(0.1, 0.9, 0.95, 1.0)
const HIGHLIGHT_COLOR := Color(0.15, 1.0, 0.25, 1.0)
const BEHIND_FADE_LENGTH_METERS := 0.75
const HIGHLIGHT_LENGTH_METERS := 6.0
const HIGHLIGHT_TRANSITION_LENGTH_METERS := 8.0
const NORMAL_PREVIEW_LENGTH_METERS := 12.0
const FORWARD_FADE_LENGTH_METERS := 5.0

const SHADER_PATH := "res://shaders/trajectory_follow_highlight.gdshader"
const REQUIRED_SHADER_UNIFORMS := [
	"trajectory_render_mode",
	"current_route_distance",
	"base_trajectory_color",
	"highlight_color",
	"behind_fade_length",
	"highlight_length",
	"highlight_transition_length",
	"normal_preview_length",
	"forward_fade_length",
]

var render_mode := FULL_ROUTE
var current_route_distance := 0.0
var supports_follow_window := false
var diagnostic_warning := ""

var _shader_material: ShaderMaterial
var _base_material: StandardMaterial3D


func build_from_route(route_path: RoutePath, projected_segments: Array, width_meters: float) -> bool:
	mesh = null
	supports_follow_window = false
	diagnostic_warning = ""
	if route_path == null or route_path.points.size() < 2:
		diagnostic_warning = "Trajectory ribbon requires a built RoutePath."
		return false
	if width_meters <= 0.0 or not is_finite(width_meters):
		diagnostic_warning = "Trajectory ribbon width must be finite and positive."
		return false

	var arrays := _build_mesh_arrays(route_path, projected_segments, width_meters)
	if arrays.is_empty():
		diagnostic_warning = "Projected trajectory points could not be mapped to RoutePath distances."
		return false

	var ribbon := ArrayMesh.new()
	ribbon.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, arrays)
	mesh = ribbon
	cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	_create_materials()
	set_render_mode(FULL_ROUTE, 0.0)
	return true


func set_render_mode(mode: int, route_distance_meters: float) -> void:
	render_mode = FOLLOW_WINDOW if mode == FOLLOW_WINDOW and supports_follow_window else FULL_ROUTE
	current_route_distance = maxf(route_distance_meters, 0.0) if is_finite(route_distance_meters) else 0.0
	# FULL_ROUTE uses the same opaque StandardMaterial contract as the previous
	# renderer. Only Follow enters the transparent shader pass.
	material_override = _shader_material if render_mode == FOLLOW_WINDOW else _base_material
	if _shader_material == null:
		return
	_shader_material.set_shader_parameter("trajectory_render_mode", render_mode)
	_shader_material.set_shader_parameter("current_route_distance", current_route_distance)


func update_route_distance(route_distance_meters: float) -> void:
	current_route_distance = maxf(route_distance_meters, 0.0) if is_finite(route_distance_meters) else 0.0
	if _shader_material != null and render_mode == FOLLOW_WINDOW:
		_shader_material.set_shader_parameter("current_route_distance", current_route_distance)


func _build_mesh_arrays(route_path: RoutePath, projected_segments: Array, width_meters: float) -> Array:
	var vertices := PackedVector3Array()
	var normals := PackedVector3Array()
	var uv2 := PackedVector2Array()
	var indices := PackedInt32Array()
	var half_width := width_meters * 0.5
	var route_search_index := 0

	for segment in projected_segments:
		if not segment is Array or segment.size() < 2:
			continue
		var segment_positions := PackedVector3Array()
		var segment_normals := PackedVector3Array()
		var mapped_distances := PackedFloat32Array()
		for point_index in range(segment.size()):
			var projected_point: Dictionary = segment[point_index]
			var position: Vector3 = projected_point.get("position", Vector3(NAN, NAN, NAN))
			while route_search_index < route_path.points.size() - 1 \
				and route_path.points[route_search_index].distance_to(position) > RoutePath.POINT_EPSILON_METERS:
				route_search_index += 1
			if route_path.points[route_search_index].distance_to(position) > RoutePath.POINT_EPSILON_METERS:
				return []
			if not segment_positions.is_empty() \
				and segment_positions[-1].distance_to(position) <= RoutePath.POINT_EPSILON_METERS:
				continue
			segment_positions.append(position)
			segment_normals.append(_safe_surface_normal(projected_point.get("normal", Vector3.UP)))
			mapped_distances.append(route_path.cumulative_distances[route_search_index])

		if segment_positions.size() < 2:
			continue
		var segment_vertex_base := vertices.size()
		for point_index in range(segment_positions.size()):
			var tangent := _ribbon_tangent(segment_positions, point_index)
			var surface_normal := segment_normals[point_index]
			var right := _safe_ribbon_right(surface_normal, tangent)
			var section_half_width := _miter_half_width(
				segment_positions, point_index, surface_normal, right, half_width
			)
			# A joined strip uses one cross-section per central point. Both sides,
			# including segment ends/caps, receive the same RoutePath distance.
			vertices.append(segment_positions[point_index] - right * section_half_width)
			vertices.append(segment_positions[point_index] + right * section_half_width)
			normals.append(surface_normal)
			normals.append(surface_normal)
			uv2.append(Vector2(mapped_distances[point_index], 0.0))
			uv2.append(Vector2(mapped_distances[point_index], 1.0))
		for point_index in range(segment_positions.size() - 1):
			var section_start := segment_vertex_base + point_index * 2
			var section_end := section_start + 2
			indices.append(section_start)
			indices.append(section_end)
			indices.append(section_start + 1)
			indices.append(section_start + 1)
			indices.append(section_end)
			indices.append(section_end + 1)

	if vertices.is_empty():
		return []
	var arrays: Array = []
	arrays.resize(Mesh.ARRAY_MAX)
	arrays[Mesh.ARRAY_VERTEX] = vertices
	arrays[Mesh.ARRAY_NORMAL] = normals
	arrays[Mesh.ARRAY_TEX_UV2] = uv2
	arrays[Mesh.ARRAY_INDEX] = indices
	return arrays


func _create_materials() -> void:
	_base_material = StandardMaterial3D.new()
	_base_material.albedo_color = BASE_TRAJECTORY_COLOR
	_base_material.roughness = 0.8
	_base_material.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED

	var shader_resource := ResourceLoader.load(SHADER_PATH, "Shader") as Shader
	if shader_resource != null and _has_required_uniforms(shader_resource):
		_shader_material = ShaderMaterial.new()
		_shader_material.shader = shader_resource
		_apply_zone_uniforms()
		supports_follow_window = true
		return

	_shader_material = null
	diagnostic_warning = "Trajectory highlight shader is unavailable; full-route material fallback is active."


func _apply_zone_uniforms() -> void:
	_shader_material.set_shader_parameter("base_trajectory_color", BASE_TRAJECTORY_COLOR)
	_shader_material.set_shader_parameter("highlight_color", HIGHLIGHT_COLOR)
	_shader_material.set_shader_parameter("behind_fade_length", BEHIND_FADE_LENGTH_METERS)
	_shader_material.set_shader_parameter("highlight_length", HIGHLIGHT_LENGTH_METERS)
	_shader_material.set_shader_parameter("highlight_transition_length", HIGHLIGHT_TRANSITION_LENGTH_METERS)
	_shader_material.set_shader_parameter("normal_preview_length", NORMAL_PREVIEW_LENGTH_METERS)
	_shader_material.set_shader_parameter("forward_fade_length", FORWARD_FADE_LENGTH_METERS)


func _has_required_uniforms(shader_resource: Shader) -> bool:
	var available_uniforms: Array[String] = []
	for uniform_info in shader_resource.get_shader_uniform_list():
		available_uniforms.append(str(uniform_info.get("name", "")))
	for uniform_name in REQUIRED_SHADER_UNIFORMS:
		if uniform_name not in available_uniforms:
			return false
	return true


static func evaluate_follow_zones(relative_distance: float) -> Dictionary:
	var alpha := _smoothstep(-BEHIND_FADE_LENGTH_METERS, 0.0, relative_distance)
	var color := BASE_TRAJECTORY_COLOR
	if relative_distance >= 0.0 and relative_distance <= HIGHLIGHT_LENGTH_METERS:
		color = HIGHLIGHT_COLOR
	elif relative_distance > HIGHLIGHT_LENGTH_METERS:
		color = HIGHLIGHT_COLOR.lerp(
			BASE_TRAJECTORY_COLOR,
			_smoothstep(
				HIGHLIGHT_LENGTH_METERS,
				HIGHLIGHT_LENGTH_METERS + HIGHLIGHT_TRANSITION_LENGTH_METERS,
				relative_distance
			)
		)
	var fade_start := HIGHLIGHT_LENGTH_METERS + HIGHLIGHT_TRANSITION_LENGTH_METERS + NORMAL_PREVIEW_LENGTH_METERS
	alpha *= 1.0 - _smoothstep(fade_start, fade_start + FORWARD_FADE_LENGTH_METERS, relative_distance)
	return {"alpha": clampf(alpha, 0.0, 1.0), "color": color}


static func _smoothstep(edge_start: float, edge_end: float, value: float) -> float:
	if is_equal_approx(edge_start, edge_end):
		return 0.0 if value < edge_start else 1.0
	var amount := clampf((value - edge_start) / (edge_end - edge_start), 0.0, 1.0)
	return amount * amount * (3.0 - 2.0 * amount)


static func _safe_surface_normal(value: Vector3) -> Vector3:
	return value.normalized() if value.length_squared() > 0.000001 else Vector3.UP


static func _safe_ribbon_right(surface_normal: Vector3, tangent: Vector3) -> Vector3:
	var right := surface_normal.cross(tangent)
	if right.length_squared() <= 0.000001:
		right = Vector3.UP.cross(tangent)
	if right.length_squared() <= 0.000001:
		right = Vector3.RIGHT
	return right.normalized()


static func _ribbon_tangent(positions: PackedVector3Array, point_index: int) -> Vector3:
	var incoming := Vector3.ZERO
	var outgoing := Vector3.ZERO
	if point_index > 0:
		incoming = (positions[point_index] - positions[point_index - 1]).normalized()
	if point_index + 1 < positions.size():
		outgoing = (positions[point_index + 1] - positions[point_index]).normalized()
	var tangent := incoming + outgoing
	if tangent.length_squared() <= 0.000001:
		tangent = outgoing if outgoing.length_squared() > 0.000001 else incoming
	return tangent.normalized() if tangent.length_squared() > 0.000001 else Vector3.FORWARD


static func _miter_half_width(
	positions: PackedVector3Array,
	point_index: int,
	surface_normal: Vector3,
	miter_right: Vector3,
	half_width: float
) -> float:
	if point_index <= 0 or point_index + 1 >= positions.size():
		return half_width
	var outgoing := (positions[point_index + 1] - positions[point_index]).normalized()
	var outgoing_right := _safe_ribbon_right(surface_normal, outgoing)
	# Cap the miter at 2x width so a near reversal cannot create a long spike.
	var alignment := maxf(absf(miter_right.dot(outgoing_right)), 0.5)
	return half_width / alignment
