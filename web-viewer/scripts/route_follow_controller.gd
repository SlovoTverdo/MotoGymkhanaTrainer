class_name RouteFollowController
extends RefCounted

signal state_changed

## Owns route playback state only. ViewerCharacter remains the sole owner of
## the character/camera transforms and MobileControls retains touch ownership.

const SPEED_PRESETS := [0.25, 0.5, 1.0, 2.0]
const FOLLOW_PITCH_MIN_DEGREES := -85.0
const FOLLOW_PITCH_MAX_DEGREES := 85.0
const INITIAL_FOLLOW_PITCH_DEGREES := -20.0

var route_distance_meters := 0.0
var total_length := 0.0
var is_playing := false
var route_finished := false
var base_speed_mps := 2.5
var speed_multiplier := 1.0
var current_speed_preset := 2
var look_ahead_meters := 1.0
var step_distance_meters := 1.0
var last_valid_route_forward := Vector3.FORWARD
var follow_look_yaw_offset_degrees := 0.0
var follow_look_pitch_offset_degrees := 0.0

var _route_path: RoutePath


func configure(route_path: RoutePath) -> void:
	_route_path = route_path
	route_distance_meters = 0.0
	total_length = route_path.total_length if route_path != null else 0.0
	is_playing = false
	route_finished = false
	current_speed_preset = 2
	speed_multiplier = SPEED_PRESETS[current_speed_preset]
	last_valid_route_forward = Vector3.FORWARD
	reset_look(false)
	state_changed.emit()


func clear() -> void:
	_route_path = null
	route_distance_meters = 0.0
	total_length = 0.0
	is_playing = false
	route_finished = false
	current_speed_preset = 2
	speed_multiplier = SPEED_PRESETS[current_speed_preset]
	last_valid_route_forward = Vector3.FORWARD
	reset_look(false)
	state_changed.emit()


func has_valid_route() -> bool:
	return _route_path != null and _route_path.is_valid()


func validation_message() -> String:
	if _route_path == null:
		return "Projected route is not available."
	return _route_path.validation_message


func follow_from_start() -> bool:
	if not has_valid_route():
		return false
	route_distance_meters = 0.0
	route_finished = false
	is_playing = true
	reset_look(false)
	# Start Follow looking slightly down so the nearby highlighted trajectory is
	# visible immediately. Look Forward and Restart keep their documented 0° reset.
	follow_look_pitch_offset_degrees = INITIAL_FOLLOW_PITCH_DEGREES
	last_valid_route_forward = _route_path.sample_direction(0.0, look_ahead_meters)
	state_changed.emit()
	return true


func advance(delta: float) -> void:
	if not is_playing or not has_valid_route() or delta <= 0.0:
		return
	route_distance_meters = _route_path.clamp_distance(
		route_distance_meters + base_speed_mps * speed_multiplier * delta
	)
	if route_distance_meters >= total_length:
		route_distance_meters = total_length
		is_playing = false
		route_finished = true
	state_changed.emit()


func toggle_play_pause() -> void:
	if not has_valid_route():
		return
	if route_finished and route_distance_meters >= total_length:
		is_playing = false
	else:
		is_playing = not is_playing
	state_changed.emit()


func pause() -> void:
	if is_playing:
		is_playing = false
		state_changed.emit()


func restart() -> void:
	if not has_valid_route():
		return
	route_distance_meters = 0.0
	route_finished = false
	reset_look(false)
	last_valid_route_forward = _route_path.sample_direction(0.0, look_ahead_meters)
	state_changed.emit()


func step_backward() -> void:
	_step_by(-step_distance_meters)


func step_forward() -> void:
	_step_by(step_distance_meters)


func speed_down() -> void:
	_set_speed_preset(current_speed_preset - 1)


func speed_up() -> void:
	_set_speed_preset(current_speed_preset + 1)


func add_user_look(look_delta_degrees: Vector2) -> void:
	follow_look_yaw_offset_degrees -= look_delta_degrees.x
	follow_look_pitch_offset_degrees = clampf(
		follow_look_pitch_offset_degrees - look_delta_degrees.y,
		FOLLOW_PITCH_MIN_DEGREES,
		FOLLOW_PITCH_MAX_DEGREES
	)
	state_changed.emit()


func reset_look(emit_change: bool = true) -> void:
	follow_look_yaw_offset_degrees = 0.0
	follow_look_pitch_offset_degrees = 0.0
	if emit_change:
		state_changed.emit()


func sample_position() -> Vector3:
	return _route_path.sample_position(route_distance_meters) if has_valid_route() else Vector3.ZERO


func sample_direction() -> Vector3:
	if not has_valid_route():
		return last_valid_route_forward
	var direction := _route_path.sample_direction(route_distance_meters, look_ahead_meters)
	if direction.length_squared() > 0.000001:
		last_valid_route_forward = direction.normalized()
	return last_valid_route_forward


func progress_ratio() -> float:
	if total_length <= RoutePath.MINIMUM_ROUTE_LENGTH_METERS:
		return 0.0
	return clampf(route_distance_meters / total_length, 0.0, 1.0)


func _step_by(distance_delta: float) -> void:
	if not has_valid_route():
		return
	route_distance_meters = _route_path.clamp_distance(route_distance_meters + distance_delta)
	route_finished = route_distance_meters >= total_length
	state_changed.emit()


func _set_speed_preset(index: int) -> void:
	current_speed_preset = clampi(index, 0, SPEED_PRESETS.size() - 1)
	speed_multiplier = SPEED_PRESETS[current_speed_preset]
	state_changed.emit()
