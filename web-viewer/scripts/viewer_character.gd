class_name WebViewerCharacter
extends CharacterBody3D

signal mode_changed(mode: String, message: String)
signal pointer_capture_changed(captured: bool)

const MODE_WALK := "Walk"
const MODE_FLY := "Fly"
const WALKABLE_MASK := 1
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

var movement_mode := MODE_WALK
var _projection: SurfaceProjectionService
var _gravity := 9.8
var _pitch_degrees := 0.0
var _yaw_degrees := 0.0

@onready var _collision_shape: CollisionShape3D = $CollisionShape3D
@onready var _capsule: CapsuleShape3D = $CollisionShape3D.shape
@onready var _head: Node3D = $Head


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


func suspend_for_reload() -> void:
	velocity = Vector3.ZERO
	_projection = null
	set_physics_process(false)


func set_projection_service(projection: SurfaceProjectionService) -> void:
	_projection = projection


func place_at_domain_start(start: Dictionary, direction: Vector2) -> bool:
	var safe := _find_safe_walk_position(start)
	if not safe["ok"]:
		return false
	global_position = safe["position"]
	var world_forward := Vector3(direction.x, 0.0, -direction.y).normalized()
	if world_forward.length_squared() > 0.00001:
		_yaw_degrees = rad_to_deg(atan2(-world_forward.x, -world_forward.z))
	rotation_degrees = Vector3(0.0, _yaw_degrees, 0.0)
	_head.rotation_degrees = Vector3(_pitch_degrees, 0.0, 0.0)
	_enter_walk_mode()
	set_physics_process(true)
	return true


func _unhandled_input(event: InputEvent) -> void:
	if event.is_action_pressed("toggle_walk_fly"):
		_toggle_movement_mode()
		get_viewport().set_input_as_handled()
		return

	if event is InputEventMouseMotion and Input.mouse_mode == Input.MOUSE_MODE_CAPTURED:
		_yaw_degrees -= event.relative.x * mouse_sensitivity
		_pitch_degrees = clampf(_pitch_degrees - event.relative.y * mouse_sensitivity, -85.0, 85.0)
		rotation_degrees = Vector3(0.0, _yaw_degrees, 0.0)
		_head.rotation_degrees = Vector3(_pitch_degrees, 0.0, 0.0)
		return

	if event is InputEventKey and event.pressed and event.keycode == KEY_ESCAPE:
		_release_pointer()
		get_viewport().set_input_as_handled()
		return

	if event is InputEventMouseButton and event.pressed:
		Input.mouse_mode = Input.MOUSE_MODE_CAPTURED
		pointer_capture_changed.emit(true)


func _notification(what: int) -> void:
	if what == NOTIFICATION_APPLICATION_FOCUS_OUT:
		_release_pointer()


func _physics_process(delta: float) -> void:
	if movement_mode == MODE_FLY:
		_process_fly(delta)
	else:
		_process_walk(delta)


func _process_walk(delta: float) -> void:
	var input := Input.get_vector("move_left", "move_right", "move_forward", "move_backward")
	var right := global_transform.basis.x
	var backward := global_transform.basis.z
	right.y = 0.0
	backward.y = 0.0
	var direction := (right.normalized() * input.x + backward.normalized() * input.y).normalized()
	var speed := move_speed * (shift_multiplier if Input.is_action_pressed("move_fast") else 1.0)
	var desired := direction * speed
	velocity.x = move_toward(velocity.x, desired.x, ground_acceleration * delta)
	velocity.z = move_toward(velocity.z, desired.z, ground_acceleration * delta)
	velocity.y = minf(velocity.y, 0.0) if is_on_floor() else velocity.y - _gravity * delta
	move_and_slide()


func _process_fly(delta: float) -> void:
	var input := Input.get_vector("move_left", "move_right", "move_forward", "move_backward")
	var vertical := Input.get_axis("fly_down", "fly_up")
	var view_basis := _head.global_transform.basis
	var direction := view_basis.x * input.x + view_basis.z * input.y + Vector3.UP * vertical
	if direction.length_squared() > 1.0:
		direction = direction.normalized()
	var speed := move_speed * (shift_multiplier if Input.is_action_pressed("move_fast") else 1.0)
	velocity = direction * speed
	global_position += velocity * delta


func _toggle_movement_mode() -> void:
	if movement_mode == MODE_WALK:
		movement_mode = MODE_FLY
		velocity = Vector3.ZERO
		_collision_shape.disabled = true
		collision_mask = 0
		mode_changed.emit(movement_mode, "Mode: Fly — Space/Ctrl move vertically.")
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
	apply_floor_snap()
	mode_changed.emit(movement_mode, "Mode: Walk — F toggles Fly.")


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
