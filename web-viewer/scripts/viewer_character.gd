class_name WebViewerCharacter
extends CharacterBody3D

signal mode_changed(mode: String, message: String)
signal pointer_capture_changed(captured: bool)

const MODE_WALK := "Walk"
const MODE_FLY := "Fly"
const WALKABLE_MASK := SurfaceProjectionService.WALKABLE_SURFACE_MASK
const WORLD_OBSTACLE_MASK := 2
const CHARACTER_LAYER := 8
const SPAWN_OFFSETS := [
	Vector2.ZERO,
	Vector2(0.75, 0.0), Vector2(-0.75, 0.0),
	Vector2(0.0, 0.75), Vector2(0.0, -0.75),
	Vector2(0.75, 0.75), Vector2(-0.75, 0.75),
	Vector2(0.75, -0.75), Vector2(-0.75, -0.75),
]

@export var move_speed := 5.0
@export var shift_multiplier := 3.0
@export var mouse_sensitivity := 0.12
@export var ground_acceleration := 20.0
@export var gravity_multiplier := 1.0
@export var floor_snap_meters := 0.3
@export var spawn_surface_clearance := 0.03
@export_range(0.02, 0.05, 0.005) var fly_floor_clearance := 0.03
@export var fly_surface_ray_above := 50.0
@export var fly_surface_ray_bottom := -10.0

var movement_mode := MODE_WALK

var _input_state: ViewerInputState
var _projection: SurfaceProjectionService
var _gravity := 9.8
var _pitch_degrees := 0.0
var _yaw_degrees := 0.0
var _spawn_domain := Vector2.ZERO
var _spawn_direction := Vector2(0.0, 1.0)
var _has_spawn := false
var _fly_surface_missing := false

@onready var _collision_shape: CollisionShape3D = $CollisionShape3D
@onready var _capsule: CapsuleShape3D = $CollisionShape3D.shape
@onready var _head: Node3D = $Head
@onready var _camera: Camera3D = $Head/Camera3D


func _ready() -> void:
	collision_layer = CHARACTER_LAYER
	collision_mask = WALKABLE_MASK | WORLD_OBSTACLE_MASK
	up_direction = Vector3.UP
	floor_max_angle = deg_to_rad(50.0)
	floor_snap_length = floor_snap_meters
	safe_margin = 0.04
	motion_mode = CharacterBody3D.MOTION_MODE_GROUNDED
	_gravity = float(ProjectSettings.get_setting("physics/3d/default_gravity", 9.8)) * gravity_multiplier
	_pitch_degrees = _head.rotation_degrees.x
	_yaw_degrees = rotation_degrees.y
	Input.mouse_mode = Input.MOUSE_MODE_VISIBLE
	set_physics_process(false)


func set_input_state(input_state: ViewerInputState) -> void:
	_input_state = input_state


func suspend_for_reload() -> void:
	velocity = Vector3.ZERO
	_projection = null
	clear_runtime_input()
	set_physics_process(false)


func clear_runtime_input() -> void:
	if _input_state != null:
		_input_state.clear_all()
	_release_pointer()


func set_projection_service(projection: SurfaceProjectionService) -> void:
	_projection = projection


func place_at_domain_start(start: Dictionary, direction: Vector2, remember_spawn: bool = true) -> bool:
	var safe := _find_safe_walk_position(start)
	if not safe["ok"]:
		return false
	if remember_spawn:
		_spawn_domain = Vector2(float(start["x"]), float(start["y"]))
		_spawn_direction = direction
		_has_spawn = true
	global_position = safe["position"]
	_apply_start_orientation(direction)
	_enter_walk_mode()
	set_physics_process(true)
	return true


func get_walk_eye_height() -> float:
	## The actual hierarchy is the source of truth. This includes both Head and
	## Camera3D local offsets instead of assuming root Y equals camera-eye Y.
	return _camera_height_from_root()


func _unhandled_input(event: InputEvent) -> void:
	if _input_state == null:
		return
	if event.is_action_pressed("toggle_walk_fly"):
		_input_state.request_mode_toggle()
		get_viewport().set_input_as_handled()
		return

	if event is InputEventMouseMotion and Input.mouse_mode == Input.MOUSE_MODE_CAPTURED:
		_input_state.add_look_delta(event.relative * mouse_sensitivity)
		return

	if event is InputEventKey and event.pressed and event.keycode == KEY_ESCAPE:
		_release_pointer()
		get_viewport().set_input_as_handled()
		return

	if event is InputEventMouseButton and event.pressed:
		Input.mouse_mode = Input.MOUSE_MODE_CAPTURED
		pointer_capture_changed.emit(true)


func _notification(what: int) -> void:
	if what == NOTIFICATION_APPLICATION_FOCUS_OUT or what == NOTIFICATION_WM_WINDOW_FOCUS_OUT:
		clear_runtime_input()


func _physics_process(delta: float) -> void:
	if _input_state == null:
		return
	_update_desktop_input()
	if _input_state.consume_reset_request():
		_reset_to_safe_spawn()
		return
	if _input_state.consume_mode_toggle_request():
		_toggle_movement_mode()
	_apply_pending_look()
	if movement_mode == MODE_FLY:
		_process_fly(delta)
	else:
		_process_walk(delta)


func _update_desktop_input() -> void:
	_input_state.set_keyboard_movement(
		Input.get_vector("move_left", "move_right", "move_forward", "move_backward")
	)
	# Vertical intent is invalid in Walk mode. Keeping it at zero prevents a
	# held key from being carried into the first Fly physics frame.
	if movement_mode == MODE_FLY:
		_input_state.set_keyboard_fly_vertical(Input.get_axis("fly_down", "fly_up"))
	else:
		_input_state.set_keyboard_fly_vertical(0.0)
	_input_state.fast_move = Input.is_action_pressed("move_fast")


func _apply_pending_look() -> void:
	var look := _input_state.consume_look_delta()
	if look.is_zero_approx():
		return
	_yaw_degrees -= look.x
	_pitch_degrees = clampf(_pitch_degrees - look.y, -85.0, 85.0)
	rotation_degrees = Vector3(0.0, _yaw_degrees, 0.0)
	_head.rotation_degrees = Vector3(_pitch_degrees, 0.0, 0.0)


func _process_walk(delta: float) -> void:
	var input := _input_state.movement
	var right := global_transform.basis.x
	var backward := global_transform.basis.z
	right.y = 0.0
	backward.y = 0.0
	var direction := right.normalized() * input.x + backward.normalized() * input.y
	if direction.length_squared() > 1.0:
		direction = direction.normalized()
	var speed := move_speed * (shift_multiplier if _input_state.fast_move else 1.0)
	var desired := direction * speed
	velocity.x = move_toward(velocity.x, desired.x, ground_acceleration * delta)
	velocity.z = move_toward(velocity.z, desired.z, ground_acceleration * delta)
	velocity.y = minf(velocity.y, 0.0) if is_on_floor() else velocity.y - _gravity * delta
	move_and_slide()


func _process_fly(delta: float) -> void:
	var input := _input_state.movement
	var view_basis := _head.global_transform.basis
	var direction := view_basis.x * input.x + view_basis.z * input.y + Vector3.UP * _input_state.fly_vertical
	if direction.length_squared() > 1.0:
		direction = direction.normalized()
	var speed := move_speed * (shift_multiplier if _input_state.fast_move else 1.0)
	velocity = direction * speed
	var proposed_root := global_position + velocity * delta
	proposed_root = _clamp_fly_root_to_surface(proposed_root)
	global_position = proposed_root


func _clamp_fly_root_to_surface(proposed_root: Vector3) -> Vector3:
	if _projection == null:
		return proposed_root
	var camera_offset_y := _camera_height_from_root()
	var proposed_camera_y := proposed_root.y + camera_offset_y
	var query_top := maxf(_projection.projection_top_y, proposed_camera_y + fly_surface_ray_above)
	var surface := _projection.query_walkable_surface(
		Vector2(proposed_root.x, proposed_root.z), query_top, fly_surface_ray_bottom, [get_rid()]
	)
	var surface_y := _projection.fallback_y
	if surface["hit"]:
		surface_y = float(surface["position"].y)
		_fly_surface_missing = false
	elif not _fly_surface_missing:
		_fly_surface_missing = true
		push_warning("Fly height query found no WalkableSurface; Venue base Y=%.3f is used until a surface recovers." % surface_y)

	var minimum_root_y := calculate_minimum_root_y(
		surface_y, get_walk_eye_height(), fly_floor_clearance, camera_offset_y
	)
	var minimum_camera_y := minimum_root_y + camera_offset_y
	if proposed_camera_y < minimum_camera_y:
		proposed_root.y = minimum_root_y
		if velocity.y < 0.0:
			velocity.y = 0.0
	return proposed_root


static func calculate_minimum_root_y(
	surface_y: float,
	walk_eye_height: float,
	clearance: float,
	camera_offset_from_root: float
) -> float:
	return surface_y + walk_eye_height + clearance - camera_offset_from_root


func _toggle_movement_mode() -> void:
	if movement_mode == MODE_WALK:
		movement_mode = MODE_FLY
		velocity = Vector3.ZERO
		_collision_shape.disabled = true
		collision_layer = 0
		collision_mask = 0
		mode_changed.emit(movement_mode, "Mode: Fly — Space/Ctrl or touch Up/Down moves vertically.")
		return

	var safe := _find_safe_walk_position(
		RuntimeGeometry.godot_to_domain(global_position), global_position.y + floor_snap_meters
	)
	if not safe["ok"]:
		mode_changed.emit(movement_mode, "Fly → Walk rejected: no safe walkable surface below.")
		return
	global_position = safe["position"]
	_enter_walk_mode()


func _enter_walk_mode() -> void:
	movement_mode = MODE_WALK
	_collision_shape.disabled = false
	collision_layer = CHARACTER_LAYER
	collision_mask = WALKABLE_MASK | WORLD_OBSTACLE_MASK
	velocity = Vector3.ZERO
	_fly_surface_missing = false
	if _input_state != null:
		_input_state.set_keyboard_fly_vertical(0.0)
	apply_floor_snap()
	mode_changed.emit(movement_mode, "Mode: Walk — F or the mode button toggles Fly.")


func _reset_to_safe_spawn() -> void:
	if _input_state != null:
		_input_state.clear_all()
	velocity = Vector3.ZERO
	if not _has_spawn:
		mode_changed.emit(movement_mode, "Reset unavailable: no safe spawn has been established.")
		return
	var start := {"x": _spawn_domain.x, "y": _spawn_domain.y}
	if not place_at_domain_start(start, _spawn_direction, false):
		mode_changed.emit(movement_mode, "Reset failed: safe spawn is no longer available.")


func _apply_start_orientation(direction: Vector2) -> void:
	var world_forward := Vector3(direction.x, 0.0, -direction.y).normalized()
	if world_forward.length_squared() > 0.00001:
		_yaw_degrees = rad_to_deg(atan2(-world_forward.x, -world_forward.z))
	_pitch_degrees = 0.0
	rotation_degrees = Vector3(0.0, _yaw_degrees, 0.0)
	_head.rotation_degrees = Vector3(_pitch_degrees, 0.0, 0.0)


func _camera_height_from_root() -> float:
	return to_local(_camera.global_position).y


func _find_safe_walk_position(requested: Dictionary, ray_start_y: float = NAN) -> Dictionary:
	if _projection == null:
		return {"ok": false}
	for offset in SPAWN_OFFSETS:
		var candidate := {
			"x": float(requested["x"]) + offset.x,
			"y": float(requested["y"]) + offset.y,
		}
		var projected := _projection.project_point(
			candidate, "CharacterSpawn", "CharacterSpawn", 0.0, ray_start_y
		)
		if not projected["hit"]:
			continue
		var foot: Vector3 = projected["position"] + projected["normal"] * spawn_surface_clearance
		if _can_occupy(foot):
			return {"ok": true, "position": foot}
	return {"ok": false}


func _can_occupy(foot_position: Vector3) -> bool:
	var query := PhysicsShapeQueryParameters3D.new()
	query.shape = _capsule
	query.transform = Transform3D(global_transform.basis, foot_position) * _collision_shape.transform
	query.collision_mask = WORLD_OBSTACLE_MASK
	query.collide_with_areas = false
	query.collide_with_bodies = true
	query.margin = safe_margin
	return get_world_3d().direct_space_state.intersect_shape(query, 1).is_empty()


func _release_pointer() -> void:
	if Input.mouse_mode != Input.MOUSE_MODE_VISIBLE:
		Input.mouse_mode = Input.MOUSE_MODE_VISIBLE
		pointer_capture_changed.emit(false)
