extends SceneTree

const MobileControls := preload("res://scripts/mobile_controls.gd")

var _failures: Array[String] = []


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	var radius := 100.0
	var dead_zone := 0.15
	_expect(MobileControls.calculate_joystick_value(Vector2.ZERO, radius, dead_zone).is_zero_approx(), "joystick center is zero")
	_expect(MobileControls.calculate_joystick_value(Vector2(10.0, 0.0), radius, dead_zone).is_zero_approx(), "dead zone suppresses small movement")

	var right: Vector2 = MobileControls.calculate_joystick_value(Vector2(radius, 0.0), radius, dead_zone)
	_expect(right.is_equal_approx(Vector2.RIGHT), "right edge produces full strafe")
	var forward: Vector2 = MobileControls.calculate_joystick_value(Vector2(0.0, -radius), radius, dead_zone)
	_expect(forward.is_equal_approx(Vector2.UP), "joystick up produces forward input")
	var backward: Vector2 = MobileControls.calculate_joystick_value(Vector2(0.0, radius), radius, dead_zone)
	_expect(backward.is_equal_approx(Vector2.DOWN), "joystick down produces backward input")

	var diagonal: Vector2 = MobileControls.calculate_joystick_value(Vector2(radius, -radius), radius, dead_zone)
	_expect(is_equal_approx(diagonal.length(), 1.0), "diagonal is limited to length 1")
	_expect(diagonal.x > 0.0 and diagonal.y < 0.0, "diagonal preserves both axes")

	var half: Vector2 = MobileControls.calculate_joystick_value(Vector2(radius * 0.5, 0.0), radius, dead_zone)
	_expect(half.length() > 0.0 and half.length() < 1.0, "joystick preserves analog magnitude outside the dead zone")
	_expect(MobileControls.calculate_joystick_value(Vector2.ONE, 0.0, dead_zone).is_zero_approx(), "zero radius is safe")

	var minimum_root := WebViewerCharacter.calculate_minimum_root_y(2.5, 1.7, 0.03, 1.9)
	_expect(is_equal_approx(minimum_root, 2.33), "Fly clamp accounts for Camera local offset")
	var fallback_root := WebViewerCharacter.calculate_minimum_root_y(0.0, 1.7, 0.03, 1.7)
	_expect(is_equal_approx(fallback_root, 0.03), "Venue base fallback preserves Walk eye height")

	var fixture := _create_mobile_fixture()
	get_root().add_child(fixture)
	await process_frame
	var state := ViewerInputState.new()
	fixture.configure(state, WebViewerCharacter.MODE_WALK)
	fixture.set_controls_visible(true)
	fixture._set_joystick_rect(180.0, 20.0)
	fixture._center_joystick()
	_expect(fixture._joystick.size.is_equal_approx(Vector2(180.0, 180.0)), "joystick layout has a non-zero square hit rectangle")
	_expect(fixture._joystick.position.is_equal_approx(Vector2(20.0, 520.0)), "joystick layout is inside the lower-left viewport corner")
	_expect(fixture._joystick_knob.position.is_equal_approx(Vector2(54.0, 54.0)), "landscape layout centers joystick knob")
	fixture._set_joystick_rect(160.0, 16.0)
	fixture._center_joystick()
	_expect(fixture._joystick_knob.position.is_equal_approx(Vector2(44.0, 44.0)), "portrait resize recenters joystick knob")
	fixture._set_joystick_rect(180.0, 20.0)
	fixture._center_joystick()

	var movement_press := InputEventScreenTouch.new()
	movement_press.index = 11
	movement_press.pressed = true
	movement_press.position = Vector2(90.0, 90.0)
	fixture._on_joystick_gui_input(movement_press)
	_expect(state.movement.is_zero_approx(), "local joystick center touch remains neutral")
	var movement_drag := InputEventScreenDrag.new()
	movement_drag.index = 11
	movement_drag.position = fixture._joystick.global_position + Vector2(90.0, 12.0)
	fixture._input(movement_drag)
	_expect(state.movement.y < -0.9, "viewport-space drag is converted to full forward movement")
	var look_press := InputEventScreenTouch.new()
	look_press.index = 22
	look_press.pressed = true
	look_press.position = Vector2(20.0, 20.0)
	fixture._on_look_gui_input(look_press)
	_expect(fixture.movement_touch_id == 11 and fixture.look_touch_id == 22, "movement and look own independent touch IDs")

	var look_drag := InputEventScreenDrag.new()
	look_drag.index = 22
	look_drag.relative = Vector2(18.0, -7.0)
	fixture._input(look_drag)
	_expect(not state.look_delta.is_zero_approx(), "owned look drag adds touch look delta")

	var movement_release := InputEventScreenTouch.new()
	movement_release.index = 11
	movement_release.pressed = false
	fixture._input(movement_release)
	_expect(fixture.look_touch_id == 22, "movement release does not release look ownership")
	_expect(state.movement.is_zero_approx(), "movement release centers joystick state")

	fixture._on_joystick_gui_input(movement_press)
	var look_release := InputEventScreenTouch.new()
	look_release.index = 22
	look_release.pressed = false
	fixture._input(look_release)
	_expect(fixture.movement_touch_id == 11, "look release does not release movement ownership")
	fixture.cancel_active_input()
	_expect(fixture.movement_touch_id == -1 and fixture.look_touch_id == -1, "cancellation clears both ownership IDs")
	_expect(state.movement.is_zero_approx() and state.look_delta.is_zero_approx(), "cancellation clears pending touch input")
	fixture.set_mode(WebViewerCharacter.MODE_FOLLOW)
	_expect(not fixture._joystick.visible, "Follow hides the movement joystick")
	_expect(not fixture._mode_button.visible and not fixture._reset_button.visible, "Follow hides ordinary mode/reset buttons")
	_expect(not fixture._fly_buttons.visible, "Follow hides Fly vertical buttons")
	_expect(fixture._look_area.visible, "Follow keeps LookArea available")
	fixture.queue_free()
	_finish()


func _create_mobile_fixture() -> WebMobileControls:
	var mobile := WebMobileControls.new()
	mobile.name = "MobileControls"
	mobile.size = Vector2(1280.0, 720.0)

	var look := Control.new()
	look.name = "LookArea"
	mobile.add_child(look)
	var joystick := Control.new()
	joystick.name = "MovementJoystick"
	joystick.size = Vector2(180.0, 180.0)
	mobile.add_child(joystick)
	var base := Panel.new()
	base.name = "Base"
	joystick.add_child(base)
	var knob := Panel.new()
	knob.name = "Knob"
	knob.size = Vector2(72.0, 72.0)
	joystick.add_child(knob)

	var top := HBoxContainer.new()
	top.name = "TopButtons"
	mobile.add_child(top)
	for button_name in ["ModeButton", "ResetButton", "FullscreenButton"]:
		var button := Button.new()
		button.name = button_name
		top.add_child(button)
	var fly := VBoxContainer.new()
	fly.name = "FlyButtons"
	mobile.add_child(fly)
	for button_name in ["FlyUpButton", "FlyDownButton"]:
		var button := Button.new()
		button.name = button_name
		fly.add_child(button)
	var hint := Label.new()
	hint.name = "OrientationHint"
	mobile.add_child(hint)
	var help := Label.new()
	help.name = "MobileHelp"
	mobile.add_child(help)
	return mobile


func _expect(condition: bool, message: String) -> void:
	if not condition:
		_failures.append(message)


func _finish() -> void:
	if _failures.is_empty():
		print("MobileControls tests passed.")
		quit(0)
		return
	for failure in _failures:
		push_error(failure)
	quit(1)
