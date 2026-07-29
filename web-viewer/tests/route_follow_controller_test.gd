extends SceneTree

const RoutePathScript := preload("res://scripts/route_path.gd")
const FollowControllerScript := preload("res://scripts/route_follow_controller.gd")

var _failures: Array[String] = []


func _initialize() -> void:
	_test_invalid_route()
	_test_play_pause_speed_and_finish()
	_test_restart_steps_and_look()
	_finish()


func _make_route():
	var route := RoutePathScript.new()
	route.build([Vector3.ZERO, Vector3(0.0, 0.0, -10.0)])
	return route


func _test_invalid_route() -> void:
	var controller := FollowControllerScript.new()
	controller.configure(RoutePathScript.new())
	_expect(not controller.follow_from_start(), "invalid route rejects Follow")
	_expect(not controller.is_playing, "invalid route stays paused")


func _test_play_pause_speed_and_finish() -> void:
	var controller := FollowControllerScript.new()
	controller.configure(_make_route())
	_expect(controller.follow_from_start(), "valid route enters Follow")
	_expect_close(
		controller.follow_look_pitch_offset_degrees,
		FollowControllerScript.INITIAL_FOLLOW_PITCH_DEGREES,
		"Follow entry starts 20 degrees down"
	)
	controller.advance(1.0)
	_expect_close(controller.route_distance_meters, 2.5, "constant 1x speed")
	controller.toggle_play_pause()
	controller.advance(1.0)
	_expect_close(controller.route_distance_meters, 2.5, "pause preserves distance")
	controller.toggle_play_pause()
	controller.speed_up()
	_expect_close(controller.speed_multiplier, 2.0, "2x preset")
	controller.advance(2.0)
	_expect_close(controller.route_distance_meters, 10.0, "finish clamps to route length")
	_expect(controller.route_finished, "finish state is set")
	_expect(not controller.is_playing, "finish stops playback")
	controller.advance(1.0)
	_expect_close(controller.route_distance_meters, 10.0, "route does not loop")


func _test_restart_steps_and_look() -> void:
	var controller := FollowControllerScript.new()
	controller.configure(_make_route())
	controller.follow_from_start()
	controller.toggle_play_pause()
	controller.step_forward()
	_expect_close(controller.route_distance_meters, 1.0, "step forward uses meters")
	controller.step_backward()
	_expect_close(controller.route_distance_meters, 0.0, "step backward uses meters")
	controller.step_backward()
	_expect_close(controller.route_distance_meters, 0.0, "backward step clamps")
	controller.add_user_look(Vector2(-12.0, 1000.0))
	_expect_close(controller.follow_look_yaw_offset_degrees, 12.0, "follow yaw offset")
	_expect_close(controller.follow_look_pitch_offset_degrees, -85.0, "follow pitch clamp")
	controller.restart()
	_expect_close(controller.route_distance_meters, 0.0, "restart distance")
	_expect_close(controller.follow_look_yaw_offset_degrees, 0.0, "restart yaw")
	_expect_close(controller.follow_look_pitch_offset_degrees, 0.0, "restart pitch returns to route forward")
	_expect(not controller.is_playing, "restart preserves paused state")
	controller.speed_down()
	_expect_close(controller.speed_multiplier, 0.5, "0.5x preset")
	controller.speed_down()
	_expect_close(controller.speed_multiplier, 0.25, "0.25x preset")


func _expect(condition: bool, label: String) -> void:
	if not condition:
		_failures.append(label)


func _expect_close(actual: float, expected: float, label: String) -> void:
	if not is_equal_approx(actual, expected):
		_failures.append("%s: expected %.4f, got %.4f" % [label, expected, actual])


func _finish() -> void:
	if _failures.is_empty():
		print("RouteFollowController deterministic checks passed.")
		quit(0)
		return
	for failure in _failures:
		push_error(failure)
	quit(1)
