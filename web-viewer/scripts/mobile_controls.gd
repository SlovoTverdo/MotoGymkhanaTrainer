class_name WebMobileControls
extends Control

signal status_message(message: String, warning: bool)

@export_range(0.05, 0.30, 0.01) var joystick_dead_zone := 0.15
@export_range(0.01, 0.30, 0.005) var touch_look_sensitivity := 0.08

var movement_touch_id := -1
var look_touch_id := -1

var _input_state: ViewerInputState
var _mode := WebViewerCharacter.MODE_WALK
var _fly_up_pressed := false
var _fly_down_pressed := false
var _movement_mouse_active := false

@onready var _joystick: Control = $MovementJoystick
@onready var _joystick_knob: Control = $MovementJoystick/Knob
@onready var _look_area: Control = $LookArea
@onready var _top_buttons: HBoxContainer = $TopButtons
@onready var _mode_button: Button = $TopButtons/ModeButton
@onready var _reset_button: Button = $TopButtons/ResetButton
@onready var _fullscreen_button: Button = $TopButtons/FullscreenButton
@onready var _fly_buttons: Control = $FlyButtons
@onready var _fly_up_button: Button = $FlyButtons/FlyUpButton
@onready var _fly_down_button: Button = $FlyButtons/FlyDownButton
@onready var _orientation_hint: Label = $OrientationHint
@onready var _mobile_help: Label = $MobileHelp


func _ready() -> void:
	mouse_filter = Control.MOUSE_FILTER_IGNORE
	_joystick.gui_input.connect(_on_joystick_gui_input)
	_look_area.gui_input.connect(_on_look_gui_input)
	_mode_button.pressed.connect(_on_mode_pressed)
	_reset_button.pressed.connect(_on_reset_pressed)
	_fullscreen_button.pressed.connect(_on_fullscreen_pressed)
	_fly_up_button.button_down.connect(_on_fly_up_down)
	_fly_up_button.button_up.connect(_on_fly_up_up)
	_fly_down_button.button_down.connect(_on_fly_down_down)
	_fly_down_button.button_up.connect(_on_fly_down_up)
	get_viewport().size_changed.connect(_on_viewport_size_changed)
	_update_mode_ui()
	_apply_responsive_layout()
	_center_joystick()


func configure(input_state: ViewerInputState, mode: String) -> void:
	_input_state = input_state
	set_mode(mode)


func set_controls_visible(show_controls: bool) -> void:
	if visible == show_controls:
		_apply_responsive_layout()
		return
	cancel_active_input()
	visible = show_controls
	_apply_responsive_layout()


func set_mode(mode: String) -> void:
	if _mode != mode:
		cancel_active_input()
	_mode = mode
	if _mode != WebViewerCharacter.MODE_FLY:
		_release_fly_buttons()
	_update_mode_ui()
	_apply_responsive_layout()


func cancel_active_input() -> void:
	movement_touch_id = -1
	look_touch_id = -1
	_movement_mouse_active = false
	_release_fly_buttons()
	_center_joystick()
	if _input_state != null:
		_input_state.clear_touch()


func _notification(what: int) -> void:
	if what == NOTIFICATION_APPLICATION_FOCUS_OUT or what == NOTIFICATION_WM_WINDOW_FOCUS_OUT:
		cancel_active_input()


func _input(event: InputEvent) -> void:
	if not visible or _input_state == null:
		return
	# Once a finger is owned, route it by index before GUI hit testing. This
	# guarantees that crossing a sibling Control or releasing outside the
	# original rectangle cannot leave movement/look stuck.
	if event is InputEventScreenDrag:
		if event.index == movement_touch_id:
			_update_joystick(_joystick_global_to_local(event.position))
			get_viewport().set_input_as_handled()
		elif event.index == look_touch_id:
			_input_state.add_look_delta(event.relative * touch_look_sensitivity)
			get_viewport().set_input_as_handled()
		return
	if event is InputEventScreenTouch and not event.pressed:
		if event.index == movement_touch_id:
			_release_joystick()
			get_viewport().set_input_as_handled()
		elif event.index == look_touch_id:
			look_touch_id = -1
			get_viewport().set_input_as_handled()


func _on_joystick_gui_input(event: InputEvent) -> void:
	if _input_state == null:
		return
	if event is InputEventScreenTouch and event.pressed and movement_touch_id == -1:
		movement_touch_id = event.index
		# Control.gui_input provides event.position relative to this Control.
		# Global drag routing in _input() performs its own canvas conversion.
		_update_joystick(event.position)
		_joystick.accept_event()
		return
	# Mouse support exists only for desktop layout testing. Mouse-to-touch
	# emulation is disabled in this project, preventing duplicate processing.
	if event is InputEventMouseButton and event.button_index == MOUSE_BUTTON_LEFT:
		_movement_mouse_active = event.pressed
		if event.pressed:
			_update_joystick(event.position)
		else:
			_release_joystick()
		_joystick.accept_event()
	elif event is InputEventMouseMotion and _movement_mouse_active:
		_update_joystick(event.position)
		_joystick.accept_event()


func _on_look_gui_input(event: InputEvent) -> void:
	# Button Controls are later siblings and receive their own touches instead
	# of this area. Mouse events deliberately pass through to desktop capture.
	if event is InputEventScreenTouch and event.pressed and look_touch_id == -1:
		look_touch_id = event.index
		_look_area.accept_event()


func _joystick_global_to_local(global_position: Vector2) -> Vector2:
	return _joystick.get_global_transform_with_canvas().affine_inverse() * global_position


func _update_joystick(local_position: Vector2) -> void:
	var center := _joystick.size * 0.5
	var radius := _joystick_radius()
	var offset := (local_position - center).limit_length(radius)
	var value := calculate_joystick_value(offset, radius, joystick_dead_zone)
	_input_state.set_touch_movement(value)
	_joystick_knob.position = center + offset - _joystick_knob.size * 0.5


func _release_joystick() -> void:
	movement_touch_id = -1
	_movement_mouse_active = false
	if _input_state != null:
		_input_state.set_touch_movement(Vector2.ZERO)
	_center_joystick()


func _center_joystick() -> void:
	if not is_instance_valid(_joystick) or not is_instance_valid(_joystick_knob):
		return
	_joystick_knob.position = (_joystick.size - _joystick_knob.size) * 0.5


func _joystick_radius() -> float:
	return maxf(1.0, minf(_joystick.size.x, _joystick.size.y) * 0.5 - minf(_joystick_knob.size.x, _joystick_knob.size.y) * 0.25)


static func calculate_joystick_value(offset: Vector2, radius: float, dead_zone: float) -> Vector2:
	if radius <= 0.0:
		return Vector2.ZERO
	var normalized := offset / radius
	var magnitude := minf(normalized.length(), 1.0)
	var clamped_dead_zone := clampf(dead_zone, 0.0, 0.95)
	if magnitude <= clamped_dead_zone:
		return Vector2.ZERO
	var scaled_magnitude := (magnitude - clamped_dead_zone) / (1.0 - clamped_dead_zone)
	return normalized.normalized() * scaled_magnitude


func _on_mode_pressed() -> void:
	if _input_state != null:
		_input_state.request_mode_toggle()


func _on_reset_pressed() -> void:
	cancel_active_input()
	if _input_state != null:
		_input_state.request_reset()


func _on_fly_up_down() -> void:
	_fly_up_pressed = true
	_update_touch_vertical()


func _on_fly_up_up() -> void:
	_fly_up_pressed = false
	_update_touch_vertical()


func _on_fly_down_down() -> void:
	_fly_down_pressed = true
	_update_touch_vertical()


func _on_fly_down_up() -> void:
	_fly_down_pressed = false
	_update_touch_vertical()


func _update_touch_vertical() -> void:
	if _input_state != null:
		_input_state.set_touch_fly_buttons(_fly_up_pressed, _fly_down_pressed)


func _release_fly_buttons() -> void:
	_fly_up_pressed = false
	_fly_down_pressed = false
	if _input_state != null:
		_input_state.set_touch_fly_buttons(false, false)


func _update_mode_ui() -> void:
	if not is_instance_valid(_mode_button) or not is_instance_valid(_fly_buttons):
		return
	var following := _mode == WebViewerCharacter.MODE_FOLLOW
	_mode_button.text = "Mode: %s" % _mode
	_mode_button.visible = not following
	_reset_button.visible = not following
	_joystick.visible = visible and not following
	_fly_buttons.visible = visible and _mode == WebViewerCharacter.MODE_FLY
	_mobile_help.text = "Right: look · Follow controls move along the route" if following else "Left: move · Right: look · Mode: Walk/Fly"


func _on_viewport_size_changed() -> void:
	# Godot re-applies anchors automatically. Cancelling ownership avoids stale
	# coordinates after browser chrome changes or device rotation.
	cancel_active_input()
	_apply_responsive_layout()


func _update_orientation_hint() -> void:
	if not is_instance_valid(_orientation_hint):
		return
	var viewport_size := get_viewport_rect().size
	_orientation_hint.visible = visible and viewport_size.y > viewport_size.x
	_update_mode_ui()


func _apply_responsive_layout() -> void:
	if not is_instance_valid(_top_buttons):
		return
	var portrait := get_viewport_rect().size.y > get_viewport_rect().size.x
	var following := _mode == WebViewerCharacter.MODE_FOLLOW
	_look_area.anchor_left = 0.0 if following else 0.42
	_look_area.anchor_top = 0.0
	_look_area.anchor_right = 1.0
	_look_area.anchor_bottom = 1.0
	_look_area.offset_left = 0.0
	_look_area.offset_top = 0.0 if following else (220.0 if visible else 0.0)
	_look_area.offset_right = 0.0
	# RouteFollowUI is drawn after MobileControls and its buttons consume their
	# own touches. Keeping the Follow look area full-height avoids a dead strip
	# across the bottom now that the compact panel occupies only the left side.
	_look_area.offset_bottom = 0.0
	if portrait:
		_top_buttons.anchor_left = 0.0
		_top_buttons.anchor_right = 1.0
		_top_buttons.offset_left = 12.0
		_top_buttons.offset_right = -12.0
		_mode_button.custom_minimum_size = Vector2(86.0, 56.0)
		_reset_button.custom_minimum_size = Vector2(86.0, 56.0)
		_fullscreen_button.custom_minimum_size = Vector2(96.0, 56.0)
		_set_joystick_rect(160.0, 16.0)
		_mobile_help.visible = false
	else:
		_top_buttons.anchor_left = 1.0
		_top_buttons.anchor_right = 1.0
		_top_buttons.offset_left = -392.0
		_top_buttons.offset_right = -16.0
		_mode_button.custom_minimum_size = Vector2(116.0, 58.0)
		_reset_button.custom_minimum_size = Vector2(116.0, 58.0)
		_fullscreen_button.custom_minimum_size = Vector2(124.0, 58.0)
		_set_joystick_rect(180.0, 20.0)
		_mobile_help.visible = visible
	_center_joystick()
	_update_orientation_hint()


func _set_joystick_rect(control_size: float, margin: float) -> void:
	# Assign the complete anchored rectangle on every layout pass. Partial
	# offset updates are unreliable after Web canvas scaling or device rotation
	# and can leave a non-container Control with a zero/off-screen hit rectangle.
	_joystick.custom_minimum_size = Vector2(control_size, control_size)
	_joystick.anchor_left = 0.0
	_joystick.anchor_top = 0.5
	_joystick.anchor_right = 0.0
	_joystick.anchor_bottom = 0.5
	_joystick.offset_left = margin
	_joystick.offset_top = -control_size * 0.5
	_joystick.offset_right = margin + control_size
	_joystick.offset_bottom = control_size * 0.5


func _on_fullscreen_pressed() -> void:
	var current := DisplayServer.window_get_mode()
	var target := DisplayServer.WINDOW_MODE_WINDOWED if current == DisplayServer.WINDOW_MODE_FULLSCREEN else DisplayServer.WINDOW_MODE_FULLSCREEN
	DisplayServer.window_set_mode(target)
	await get_tree().process_frame
	if DisplayServer.window_get_mode() == target:
		status_message.emit("Fullscreen changed.", false)
	else:
		status_message.emit("Browser declined the fullscreen request.", true)
