class_name ViewerInputState
extends RefCounted

## Shared, runtime-only input state consumed by WebViewerCharacter. Desktop and
## touch producers retain separate contributions so combining them is explicit.

var movement := Vector2.ZERO
var look_delta := Vector2.ZERO
var fly_vertical := 0.0
var fast_move := false
var reset_requested := false
var mode_toggle_requested := false

var _keyboard_movement := Vector2.ZERO
var _touch_movement := Vector2.ZERO
var _keyboard_fly_vertical := 0.0
var _touch_fly_up := false
var _touch_fly_down := false


func set_keyboard_movement(value: Vector2) -> void:
	_keyboard_movement = value.limit_length(1.0)
	_rebuild_movement()


func set_touch_movement(value: Vector2) -> void:
	_touch_movement = value.limit_length(1.0)
	_rebuild_movement()


func set_keyboard_fly_vertical(value: float) -> void:
	_keyboard_fly_vertical = clampf(value, -1.0, 1.0)
	_rebuild_fly_vertical()


func set_touch_fly_buttons(up_pressed: bool, down_pressed: bool) -> void:
	_touch_fly_up = up_pressed
	_touch_fly_down = down_pressed
	_rebuild_fly_vertical()


func add_look_delta(value: Vector2) -> void:
	look_delta += value


func consume_look_delta() -> Vector2:
	var result := look_delta
	look_delta = Vector2.ZERO
	return result


func request_reset() -> void:
	reset_requested = true


func consume_reset_request() -> bool:
	var result := reset_requested
	reset_requested = false
	return result


func request_mode_toggle() -> void:
	mode_toggle_requested = true


func consume_mode_toggle_request() -> bool:
	var result := mode_toggle_requested
	mode_toggle_requested = false
	return result


func clear_touch() -> void:
	_touch_movement = Vector2.ZERO
	_touch_fly_up = false
	_touch_fly_down = false
	look_delta = Vector2.ZERO
	_rebuild_movement()
	_rebuild_fly_vertical()


func clear_all(clear_requests: bool = true) -> void:
	_keyboard_movement = Vector2.ZERO
	_touch_movement = Vector2.ZERO
	_keyboard_fly_vertical = 0.0
	_touch_fly_up = false
	_touch_fly_down = false
	movement = Vector2.ZERO
	look_delta = Vector2.ZERO
	fly_vertical = 0.0
	fast_move = false
	if clear_requests:
		reset_requested = false
		mode_toggle_requested = false


func _rebuild_movement() -> void:
	movement = (_keyboard_movement + _touch_movement).limit_length(1.0)


func _rebuild_fly_vertical() -> void:
	var touch_vertical := float(int(_touch_fly_up) - int(_touch_fly_down))
	fly_vertical = clampf(_keyboard_fly_vertical + touch_vertical, -1.0, 1.0)
