extends SceneTree

const State := preload("res://scripts/viewer_input_state.gd")

var _failures: Array[String] = []


func _initialize() -> void:
	var state := State.new()
	state.set_keyboard_movement(Vector2(0.0, -1.0))
	_expect(state.movement.is_equal_approx(Vector2(0.0, -1.0)), "keyboard movement is preserved")

	state.set_touch_movement(Vector2(1.0, 0.0))
	_expect(is_equal_approx(state.movement.length(), 1.0), "combined movement is clamped to length 1")
	_expect(state.movement.x > 0.0 and state.movement.y < 0.0, "keyboard and joystick combine predictably")

	state.set_keyboard_movement(Vector2.ZERO)
	state.set_touch_movement(Vector2(0.35, 0.0))
	_expect(is_equal_approx(state.movement.length(), 0.35), "analog touch magnitude is retained")

	state.set_keyboard_fly_vertical(1.0)
	state.set_touch_fly_buttons(false, true)
	_expect(is_zero_approx(state.fly_vertical), "opposing keyboard and touch vertical inputs cancel")
	state.set_keyboard_fly_vertical(0.0)
	state.set_touch_fly_buttons(true, true)
	_expect(is_zero_approx(state.fly_vertical), "simultaneous touch Up and Down cancel")

	state.add_look_delta(Vector2(4.0, -2.0))
	_expect(state.consume_look_delta().is_equal_approx(Vector2(4.0, -2.0)), "look delta is consumed once")
	_expect(state.consume_look_delta().is_zero_approx(), "consumed look delta resets")

	state.request_mode_toggle()
	_expect(state.consume_mode_toggle_request(), "mode toggle request is consumed")
	_expect(not state.consume_mode_toggle_request(), "mode toggle request is one-shot")
	state.request_reset()
	_expect(state.consume_reset_request(), "reset request is consumed")

	state.set_touch_movement(Vector2.ONE)
	state.set_touch_fly_buttons(true, false)
	state.add_look_delta(Vector2.ONE)
	state.clear_touch()
	_expect(state.movement.is_zero_approx(), "touch clear resets movement")
	_expect(is_zero_approx(state.fly_vertical), "touch clear resets Fly vertical")
	_expect(state.look_delta.is_zero_approx(), "touch clear resets look")
	_finish()


func _expect(condition: bool, message: String) -> void:
	if not condition:
		_failures.append(message)


func _finish() -> void:
	if _failures.is_empty():
		print("ViewerInputState tests passed.")
		quit(0)
		return
	for failure in _failures:
		push_error(failure)
	quit(1)
