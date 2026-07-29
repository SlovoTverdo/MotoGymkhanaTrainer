extends SceneTree

const RoutePathScript := preload("res://scripts/route_path.gd")
const RendererScript := preload("res://scripts/trajectory_highlight_renderer.gd")

var _failures: Array[String] = []


func _initialize() -> void:
	_test_zone_math()
	_test_route_distance_vertex_mapping()
	_test_modes_update_without_mesh_rebuild()
	_finish()


func _test_zone_math() -> void:
	_expect_zone(-2.0, 0.0, RendererScript.BASE_TRAJECTORY_COLOR, "far behind")
	_expect_zone(-0.75, 0.0, RendererScript.BASE_TRAJECTORY_COLOR, "behind fade start")
	var before_zero: Dictionary = RendererScript.evaluate_follow_zones(-0.001)
	_expect(float(before_zero["alpha"]) > 0.99 and float(before_zero["alpha"]) < 1.0, "immediately before zero fades smoothly")
	_expect_color(before_zero["color"], RendererScript.BASE_TRAJECTORY_COLOR, "behind fade keeps base color")
	_expect_zone(0.0, 1.0, RendererScript.HIGHLIGHT_COLOR, "zero")
	_expect_zone(3.0, 1.0, RendererScript.HIGHLIGHT_COLOR, "highlight midpoint")
	_expect_zone(6.0, 1.0, RendererScript.HIGHLIGHT_COLOR, "highlight end")
	_expect_zone(10.0, 1.0, RendererScript.HIGHLIGHT_COLOR.lerp(RendererScript.BASE_TRAJECTORY_COLOR, 0.5), "transition midpoint")
	_expect_zone(14.0, 1.0, RendererScript.BASE_TRAJECTORY_COLOR, "transition end")
	_expect_zone(20.0, 1.0, RendererScript.BASE_TRAJECTORY_COLOR, "normal preview midpoint")
	_expect_zone(26.0, 1.0, RendererScript.BASE_TRAJECTORY_COLOR, "forward fade start")
	_expect_zone(28.5, 0.5, RendererScript.BASE_TRAJECTORY_COLOR, "forward fade midpoint")
	_expect_zone(31.0, 0.0, RendererScript.BASE_TRAJECTORY_COLOR, "forward fade end")
	_expect_zone(50.0, 0.0, RendererScript.BASE_TRAJECTORY_COLOR, "far ahead")


func _test_route_distance_vertex_mapping() -> void:
	var route := RoutePathScript.new()
	route.build([Vector3.ZERO, Vector3(0.0, 0.0, -2.0), Vector3(0.0, 0.0, -5.0)])
	var projected := [[
		_projected(Vector3.ZERO),
		_projected(Vector3(0.0, 0.0, -2.0)),
		_projected(Vector3(0.0, 0.0, -5.0)),
	]]
	var renderer := RendererScript.new()
	_expect(renderer.build_from_route(route, projected, 0.08), "ribbon builds from projected RoutePath points")
	if renderer.mesh == null:
		renderer.free()
		return
	var arrays := renderer.mesh.surface_get_arrays(0)
	var route_uv2: PackedVector2Array = arrays[Mesh.ARRAY_TEX_UV2]
	var expected := [0.0, 0.0, 2.0, 2.0, 5.0, 5.0]
	_expect(route_uv2.size() == expected.size(), "UV2 has one route-distance value per ribbon vertex")
	for index in range(mini(route_uv2.size(), expected.size())):
		_expect_close(route_uv2[index].x, expected[index], "UV2.x route distance at vertex %d" % index)
	for section_start in [0, 2, 4]:
		_expect_close(route_uv2[section_start].x, route_uv2[section_start + 1].x, "left/right vertices share route distance")
	renderer.mesh = null
	renderer.material_override = null
	renderer.free()


func _test_modes_update_without_mesh_rebuild() -> void:
	var route := RoutePathScript.new()
	route.build([Vector3.ZERO, Vector3(0.0, 0.0, -5.0)])
	var renderer := RendererScript.new()
	_expect(renderer.build_from_route(route, [[_projected(Vector3.ZERO), _projected(Vector3(0.0, 0.0, -5.0))]], 0.08), "mode-test ribbon builds")
	var original_mesh := renderer.mesh
	_expect(renderer.render_mode == RendererScript.FULL_ROUTE, "Walk/Fly default to full route")
	_expect(renderer.supports_follow_window, "Compatibility shader resource exposes Follow uniforms")
	renderer.set_render_mode(RendererScript.FOLLOW_WINDOW, 1.25)
	_expect(renderer.render_mode == RendererScript.FOLLOW_WINDOW, "Follow enables window mode")
	_expect_close(renderer.current_route_distance, 1.25, "Follow entry distance")
	renderer.update_route_distance(3.0)
	_expect_close(renderer.current_route_distance, 3.0, "playback/step distance update")
	_expect(renderer.mesh == original_mesh, "distance update does not rebuild mesh")
	renderer.set_render_mode(RendererScript.FULL_ROUTE, 0.0)
	_expect(renderer.render_mode == RendererScript.FULL_ROUTE, "Follow exit restores full route")
	_expect(renderer.mesh == original_mesh, "mode switch does not rebuild mesh")
	renderer.mesh = null
	renderer.material_override = null
	renderer.free()


func _projected(position: Vector3) -> Dictionary:
	return {"position": position, "normal": Vector3.UP, "hit": true}


func _expect_zone(relative_distance: float, expected_alpha: float, expected_color: Color, label: String) -> void:
	var result: Dictionary = RendererScript.evaluate_follow_zones(relative_distance)
	_expect_close(float(result["alpha"]), expected_alpha, "%s alpha" % label)
	_expect_color(result["color"], expected_color, "%s color" % label)


func _expect_color(actual: Color, expected: Color, label: String) -> void:
	if not actual.is_equal_approx(expected):
		_failures.append("%s: expected %s, got %s" % [label, expected, actual])


func _expect(condition: bool, label: String) -> void:
	if not condition:
		_failures.append(label)


func _expect_close(actual: float, expected: float, label: String) -> void:
	if not is_equal_approx(actual, expected):
		_failures.append("%s: expected %.4f, got %.4f" % [label, expected, actual])


func _finish() -> void:
	if _failures.is_empty():
		print("Trajectory highlight deterministic checks passed.")
		quit(0)
		return
	for failure in _failures:
		push_error(failure)
	quit(1)
