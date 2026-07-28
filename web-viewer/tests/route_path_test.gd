extends SceneTree

const RoutePathScript := preload("res://scripts/route_path.gd")

var _failures: Array[String] = []


func _initialize() -> void:
	_test_valid_polyline_and_sampling()
	_test_cleanup_and_invalid_values()
	_test_two_point_and_almost_zero_routes()
	_test_look_ahead_at_end()
	if _failures.is_empty():
		print("RoutePath deterministic checks passed.")
		quit(0)
	else:
		for failure in _failures:
			push_error(failure)
		quit(1)


func _test_valid_polyline_and_sampling() -> void:
	var route := RoutePathScript.new()
	route.build([Vector3.ZERO, Vector3(3.0, 0.0, 0.0), Vector3(3.0, 4.0, 0.0)])
	_expect(route.is_valid(), "valid polyline must build")
	_expect_close(route.total_length, 7.0, "total length")
	_expect(route.cumulative_distances.size() == 3, "cumulative distance count")
	_expect_close(route.cumulative_distances[1], 3.0, "first cumulative distance")
	_expect_vec(route.sample_position(0.0), Vector3.ZERO, "sample at zero")
	_expect_vec(route.sample_position(1.5), Vector3(1.5, 0.0, 0.0), "sample in middle")
	_expect_vec(route.sample_position(7.0), Vector3(3.0, 4.0, 0.0), "sample at end")
	_expect_close(route.clamp_distance(-5.0), 0.0, "clamp below zero")
	_expect_close(route.clamp_distance(99.0), 7.0, "clamp beyond end")


func _test_cleanup_and_invalid_values() -> void:
	var route := RoutePathScript.new()
	route.build([
		Vector3.ZERO,
		Vector3.ZERO,
		Vector3(0.0001, 0.0, 0.0),
		Vector3(NAN, 0.0, 0.0),
		Vector3(2.0, 0.0, 0.0),
	])
	_expect(route.is_valid(), "duplicates and invalid points must be filtered")
	_expect(route.points.size() == 2, "filtered route point count")
	_expect_close(route.total_length, 2.0, "filtered route length")


func _test_two_point_and_almost_zero_routes() -> void:
	var two_point := RoutePathScript.new()
	two_point.build([Vector3.ZERO, Vector3(0.0, 0.0, 2.0)])
	_expect(two_point.is_valid(), "two-point route must be valid")
	_expect_vec(two_point.sample_position(1.0), Vector3(0.0, 0.0, 1.0), "two-point interpolation")

	var almost_zero := RoutePathScript.new()
	almost_zero.build([Vector3.ZERO, Vector3(0.05, 0.0, 0.0)])
	_expect(not almost_zero.is_valid(), "almost-zero route must be invalid")

	var minimum_boundary := RoutePathScript.new()
	minimum_boundary.build([Vector3.ZERO, Vector3(0.1, 0.0, 0.0)])
	_expect(not minimum_boundary.is_valid(), "route at minimum-length boundary must be invalid")


func _test_look_ahead_at_end() -> void:
	var route := RoutePathScript.new()
	route.build([Vector3.ZERO, Vector3(0.0, 0.0, -2.0)])
	_expect_vec(route.sample_direction(2.0, 1.0), Vector3(0.0, 0.0, -1.0), "look-ahead near end")


func _expect(condition: bool, label: String) -> void:
	if not condition:
		_failures.append(label)


func _expect_close(actual: float, expected: float, label: String) -> void:
	if not is_equal_approx(actual, expected):
		_failures.append("%s: expected %.4f, got %.4f" % [label, expected, actual])


func _expect_vec(actual: Vector3, expected: Vector3, label: String) -> void:
	if not actual.is_equal_approx(expected):
		_failures.append("%s: expected %s, got %s" % [label, expected, actual])
