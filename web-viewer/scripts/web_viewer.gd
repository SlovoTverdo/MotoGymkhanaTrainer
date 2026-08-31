extends Node3D

const TrajectoryHighlightRendererScript := preload("res://scripts/trajectory_highlight_renderer.gd")
const CONE_MODEL_PATH := "res://Assets/Models/Traffic Cone_Textured.glb"
const FALLBACK_TRACK_PATH := "res://tracks/default-track.json"
const MarkingPathContract := preload("res://scripts/marking_path.gd")
const MAXIMUM_PROJECTION_SPACING := 0.35
const MARKING_SURFACE_OFFSET := 0.025
const TRAJECTORY_SURFACE_OFFSET := 0.04
const DIRECTION_SURFACE_OFFSET := 0.015
const TRAJECTORY_WIDTH := 0.08
const DIRECTION_INTERVAL := 5.0
const DIRECTION_LENGTH := 0.5
const DIRECTION_HALF_WIDTH := 0.2
const DIRECTION_LINE_WIDTH := 0.045
const CONE_TOP_HEIGHT := 0.79
const CONE_TOPPER_HEIGHT := 0.14

var _fallback_environment: Environment
var _projection: SurfaceProjectionService
var _track: Dictionary
var _external_warning := ""
var _loading := false
var _input_state := ViewerInputState.new()
var _route_path := RoutePath.new()
var _route_follow := RouteFollowController.new()
var _trajectory_renderer = null
var _direction_markers_root: Node3D
var _follow_smoke_reloaded := false

@onready var _world_environment: WorldEnvironment = $WorldEnvironment
@onready var _venue_root: Node3D = $VenueRoot
@onready var _track_root: Node3D = $TrackRoot
@onready var _character: WebViewerCharacter = $ViewerCharacter
@onready var _http_request: HTTPRequest = $TrackRequest
@onready var _loading_panel: Control = $UI/Loading
@onready var _loading_label: Label = $UI/Loading/Panel/Margin/Layout/Message
@onready var _error_panel: Control = $UI/Error
@onready var _error_label: Label = $UI/Error/Panel/Margin/Layout/Message
@onready var _warning_label: Label = $UI/Warning
@onready var _mode_label: Label = $UI/Mode
@onready var _controls_label: Label = $UI/Controls
@onready var _touch_controls_toggle: Button = $UI/TouchControlsToggle
@onready var _mobile_controls: WebMobileControls = $UI/MobileControls
@onready var _route_follow_ui: RouteFollowUI = $UI/RouteFollowUI


func _ready() -> void:
	_fallback_environment = _world_environment.environment
	_character.set_input_state(_input_state)
	_character.set_route_follow_controller(_route_follow)
	_mobile_controls.configure(_input_state, _character.movement_mode)
	_route_follow_ui.configure(_route_follow)
	_route_follow.state_changed.connect(_on_route_follow_state_changed)
	_http_request.request_completed.connect(_on_track_request_completed)
	_character.mode_changed.connect(_on_mode_changed)
	_character.pointer_capture_changed.connect(_on_pointer_capture_changed)
	_touch_controls_toggle.pressed.connect(_toggle_touch_controls)
	_mobile_controls.status_message.connect(_on_mobile_status_message)
	_route_follow_ui.follow_requested.connect(_on_follow_requested)
	_route_follow_ui.exit_requested.connect(_on_follow_exit_requested)
	_route_follow_ui.restart_requested.connect(_on_follow_restart_requested)
	_route_follow_ui.step_backward_requested.connect(_on_follow_step_backward_requested)
	_route_follow_ui.play_pause_requested.connect(_on_follow_play_pause_requested)
	_route_follow_ui.step_forward_requested.connect(_on_follow_step_forward_requested)
	_route_follow_ui.speed_down_requested.connect(_on_follow_speed_down_requested)
	_route_follow_ui.speed_up_requested.connect(_on_follow_speed_up_requested)
	_route_follow_ui.look_forward_requested.connect(_on_follow_look_forward_requested)
	_error_panel.visible = false
	_warning_label.visible = false
	_mode_label.text = "Mode: Walk"
	_controls_label.text = "Click to capture mouse · WASD move · Shift faster · F Walk/Fly · Follow button · T touch UI · Esc release"
	var touch_setting := _argument_value("--touch-controls", "auto").to_lower()
	var show_touch_controls := DisplayServer.is_touchscreen_available()
	if touch_setting == "show":
		show_touch_controls = true
	elif touch_setting == "hide":
		show_touch_controls = false
	_set_touch_controls_visible(show_touch_controls)
	_route_follow_ui.set_route_available(false, "Track projection is still loading.")
	_route_follow_ui.visible = false
	call_deferred("_begin_loading")


func _unhandled_input(event: InputEvent) -> void:
	if event.is_action_pressed("toggle_touch_controls"):
		_toggle_touch_controls()
		get_viewport().set_input_as_handled()


func _begin_loading() -> void:
	if _loading:
		return
	_loading = true
	_show_loading("Loading published Track…")
	if "--embedded-only" in OS.get_cmdline_user_args():
		_load_embedded_fallback("External Track skipped by --embedded-only.")
		return

	var url := _resolve_external_track_url()
	if url.is_empty():
		_load_embedded_fallback("Unable to resolve the current page URL.")
		return
	var request_error := _http_request.request(url)
	if request_error != OK:
		_load_embedded_fallback("External Track request could not start (%s)." % error_string(request_error))


func _on_track_request_completed(
	result: int,
	response_code: int,
	_headers: PackedStringArray,
	body: PackedByteArray
) -> void:
	if result != HTTPRequest.RESULT_SUCCESS:
		_load_embedded_fallback("External Track request failed (result %d)." % result)
		return
	if response_code < 200 or response_code >= 300:
		_load_embedded_fallback("External Track returned HTTP %d." % response_code)
		return

	var parsed := TrackV4Parser.parse_json(body.get_string_from_utf8(), "external tracks/default-track.json")
	if not parsed["ok"]:
		_load_embedded_fallback(parsed["error"])
		return
	await _build_runtime(parsed["track"], "external Track")


func _load_embedded_fallback(reason: String) -> void:
	_external_warning = reason
	_show_warning("Published Track unavailable: %s Using embedded fallback." % reason)
	var fallback_path := _argument_value("--fallback-track", FALLBACK_TRACK_PATH)
	if not FileAccess.file_exists(fallback_path):
		_show_blocking_error("Embedded fallback '%s' is missing." % fallback_path)
		return
	var file := FileAccess.open(fallback_path, FileAccess.READ)
	if file == null:
		_show_blocking_error("Embedded fallback could not be opened (%s)." % error_string(FileAccess.get_open_error()))
		return
	var parsed := TrackV4Parser.parse_json(file.get_as_text(), fallback_path)
	if not parsed["ok"]:
		_show_blocking_error("External Track failed and embedded fallback is invalid: %s" % parsed["error"])
		return
	await _build_runtime(parsed["track"], "embedded fallback")


func _build_runtime(track: Dictionary, source_label: String) -> void:
	_show_loading("Building Venue…")
	_mobile_controls.cancel_active_input()
	_character.suspend_for_reload()
	_clear_runtime()
	_world_environment.environment = _fallback_environment
	_track = track

	_create_area(track["area"])
	_create_venue_objects(track["venueObjects"])
	_apply_panorama(track["panorama"])

	# Venue collision bodies must enter the active physics space before any ray.
	await get_tree().physics_frame
	_projection = SurfaceProjectionService.new(get_world_3d())
	_character.set_projection_service(_projection)

	var cone_scene := _load_packed_scene(CONE_MODEL_PATH)
	if cone_scene == null:
		_show_blocking_error("Required cone model '%s' is unavailable." % CONE_MODEL_PATH)
		return

	_create_cones(track["cones"], cone_scene)
	_create_markings(track["markings"])
	var route_projection := _create_trajectory(track["trajectory"]["segments"])
	_route_path.build(route_projection["points"])
	if not route_projection["valid"]:
		_route_path.invalidate(route_projection["message"])
	_create_trajectory_renderer(route_projection)
	_route_follow.configure(_route_path)
	_route_follow_ui.set_route_available(_route_path.is_valid(), _route_path.validation_message)

	var entry := RuntimeGeometry.trajectory_entry_pose(track["trajectory"]["segments"])
	var start: Dictionary = entry.get("start", {"x": 0.0, "y": 0.0})
	var direction: Vector2 = entry.get("direction", Vector2(0.0, 1.0))
	if not _character.place_at_domain_start(start, direction):
		_show_blocking_error("Viewer character could not find a safe walkable spawn.")
		return

	_loading = false
	_loading_panel.visible = false
	_error_panel.visible = false
	_touch_controls_toggle.visible = true
	_route_follow_ui.visible = true
	_mode_label.text = "Mode: %s" % _character.movement_mode
	if _external_warning.is_empty():
		_show_warning("Loaded %s: %s" % [source_label, track["track"]["name"]], false)
	else:
		_warning_label.visible = true
	print(
		"Loaded '%s' from %s: %d objects, %d cones, %d markings, %d trajectory segments, %d projection warnings." % [
			track["track"]["name"], source_label, track["venueObjects"].size(), track["cones"].size(),
			track["markings"].size(), track["trajectory"]["segments"].size(), _projection.diagnostics.size()
		]
	)
	if "--follow-smoke-test" in OS.get_cmdline_user_args():
		await _run_follow_smoke_test()
		return
	if "--smoke-test" in OS.get_cmdline_user_args():
		print("Touch controls visible: %s; orientation hint visible: %s; Follow UI visible: %s; entry visible: %s; entry rect: %s / %s." % [
			_mobile_controls.visible, $UI/MobileControls/OrientationHint.visible,
			_route_follow_ui.visible, $UI/RouteFollowUI/DesktopEntryButton.visible,
			$UI/RouteFollowUI/DesktopEntryButton.position, $UI/RouteFollowUI/DesktopEntryButton.size
		])
		get_tree().quit(0)


func _run_follow_smoke_test() -> void:
	var failures: Array[String] = []
	if _follow_smoke_reloaded:
		if _trajectory_renderer == null or _trajectory_renderer.render_mode != TrajectoryHighlightRendererScript.FULL_ROUTE:
			failures.append("Track reload did not reset trajectory rendering to FULL_ROUTE")
		if _trajectory_renderer != null and not is_zero_approx(_trajectory_renderer.current_route_distance):
			failures.append("Track reload retained the old trajectory distance")
		if not is_zero_approx(_route_follow.route_distance_meters):
			failures.append("Track reload retained the old Follow controller distance")
	if not _route_path.is_valid():
		failures.append("projected RoutePath is invalid: %s" % _route_path.validation_message)
	else:
		if not _character.enter_follow_from_start():
			failures.append("Follow could not enter from Walk")
		else:
			if _trajectory_renderer == null or _trajectory_renderer.render_mode != TrajectoryHighlightRendererScript.FOLLOW_WINDOW:
				failures.append("Follow entry did not enable the trajectory window")
			if _direction_markers_root != null and _direction_markers_root.visible:
				failures.append("direction arrows remained visible in Follow")
			if not is_equal_approx(
				$ViewerCharacter/Head.rotation_degrees.x,
				RouteFollowController.INITIAL_FOLLOW_PITCH_DEGREES
			):
				failures.append("Follow entry did not apply the initial downward camera pitch")
			_route_follow.pause()
			var camera: Camera3D = $ViewerCharacter/Head/Camera3D
			var expected_eye := _route_path.sample_position(0.0) + Vector3.UP * _character.get_walk_eye_height()
			if camera.global_position.distance_to(expected_eye) > 0.002:
				failures.append("initial Follow camera eye does not match route position plus shared eye height")
			_route_follow.step_forward()
			_character.refresh_follow_pose()
			if not is_equal_approx(_route_follow.route_distance_meters, 1.0):
				failures.append("Follow step did not advance exactly 1 meter")
			if _trajectory_renderer != null and not is_equal_approx(_trajectory_renderer.current_route_distance, 1.0):
				failures.append("trajectory window did not receive stepped route distance")
			_route_follow.add_user_look(Vector2(-10.0, 5.0))
			_character.refresh_follow_pose()
			if not is_equal_approx(_route_follow.follow_look_yaw_offset_degrees, 10.0):
				failures.append("Follow look offset was not retained")
			_route_follow.restart()
			_character.refresh_follow_pose()
			if not is_zero_approx(_route_follow.route_distance_meters) or _route_follow.is_playing:
				failures.append("paused Restart did not preserve pause at route start")
			if _mobile_controls.visible:
				if $UI/MobileControls/MovementJoystick.visible:
					failures.append("mobile joystick remained visible in Follow")
				if not $UI/RouteFollowUI/MobilePanel.visible:
					failures.append("mobile Follow panel is hidden")
			_character.exit_follow_to_safe_mode()
			if _character.movement_mode not in [WebViewerCharacter.MODE_WALK, WebViewerCharacter.MODE_FLY]:
				failures.append("Follow exit did not choose a free mode")
			if _trajectory_renderer != null and _trajectory_renderer.render_mode != TrajectoryHighlightRendererScript.FULL_ROUTE:
				failures.append("Follow exit did not restore the full trajectory")

		# Exercise the same entry API from Fly. The physics frame consumes the
		# existing Walk/Fly request through ViewerInputState.
		if _character.movement_mode == WebViewerCharacter.MODE_WALK:
			_input_state.request_mode_toggle()
			await get_tree().physics_frame
		if _character.movement_mode != WebViewerCharacter.MODE_FLY:
			failures.append("test setup could not enter Fly")
		elif not _character.enter_follow_from_start():
			failures.append("Follow could not enter from Fly")
		else:
			_route_follow.pause()
			_character.exit_follow_to_safe_mode()

	if failures.is_empty() and not _follow_smoke_reloaded:
		# Exercise the complete runtime reload order once: old renderer/path state
		# is cleared, projection is rebuilt, and the second smoke pass starts from
		# a new FULL_ROUTE mesh at distance zero.
		_follow_smoke_reloaded = true
		await _build_runtime(_track, "Follow smoke Track reload")
		return
	if failures.is_empty():
		print("Route Follow integration smoke checks passed (route %.2f m, %d points)." % [
			_route_path.total_length, _route_path.points.size()
		])
		get_tree().quit(0)
		return
	for failure in failures:
		push_error(failure)
	get_tree().quit(1)


func _clear_runtime() -> void:
	# Reset the old visual state before its nodes are queued for deletion so a
	# reload cannot leak Follow uniforms or hidden arrows into the next Track.
	if _trajectory_renderer != null:
		_trajectory_renderer.set_render_mode(TrajectoryHighlightRendererScript.FULL_ROUTE, 0.0)
	if _direction_markers_root != null:
		_direction_markers_root.visible = true
	_trajectory_renderer = null
	_direction_markers_root = null
	_route_follow.clear()
	_route_path.clear()
	_route_follow_ui.set_route_available(false, "Track projection is not available.")
	for root in [_venue_root, _track_root]:
		for child in root.get_children():
			root.remove_child(child)
			child.queue_free()
	_projection = null


func _create_area(area: Dictionary) -> void:
	var root := Node3D.new()
	root.name = "Surface"
	var material := StandardMaterial3D.new()
	material.albedo_color = Color(0.24, 0.27, 0.29)
	material.roughness = 0.95
	var plane := PlaneMesh.new()
	plane.size = Vector2(float(area["width"]), float(area["length"]))
	plane.subdivide_width = maxi(0, int(area["width"]) - 1)
	plane.subdivide_depth = maxi(0, int(area["length"]) - 1)
	plane.material = material
	var visual := MeshInstance3D.new()
	visual.name = "TrainingArea"
	visual.mesh = plane
	root.add_child(visual)

	var body := StaticBody3D.new()
	body.name = "WalkableSurfaceBody"
	body.collision_layer = 1
	body.collision_mask = 0
	var shape := CollisionShape3D.new()
	shape.name = "CollisionShape3D"
	shape.position = Vector3(0.0, -0.1, 0.0)
	var box := BoxShape3D.new()
	box.size = Vector3(float(area["width"]), 0.2, float(area["length"]))
	shape.shape = box
	body.add_child(shape)
	root.add_child(body)
	_venue_root.add_child(root)


func _create_venue_objects(objects: Array) -> void:
	var root := Node3D.new()
	root.name = "Objects"
	var missing_collision := {}
	for item in objects:
		if not item["visibleInViewer"]:
			continue
		# Imported GLB runtime is intentionally desktop-only in this iteration.
		# The exported wrapper path remains diagnostic data but is never loaded here.
		if item.get("objectType", "") == "imported":
			push_warning("Venue object '%s' uses unsupported imported asset '%s'; skipped." % [
				item["id"], item["assetPath"]])
			continue
		var scene := _load_packed_scene(item["assetPath"])
		if scene == null:
			push_error("Venue object '%s' asset '%s' is missing; skipped." % [item["id"], item["assetPath"]])
			continue
		var instance := scene.instantiate()
		if not instance is Node3D:
			push_error("Venue object '%s' root is not Node3D; skipped." % item["id"])
			instance.queue_free()
			continue
		instance.name = item["id"]
		instance.position = RuntimeGeometry.domain_to_godot(item["position"], float(item["elevation"]))
		instance.rotation = Vector3(0.0, deg_to_rad(float(item["rotationDeg"])), 0.0)
		instance.scale = Vector3(float(item["scale"]["x"]), float(item["scale"]["y"]), float(item["scale"]["z"]))
		if not item["collisionEnabled"]:
			_disable_collision_recursively(instance)
		elif not _contains_enabled_collision(instance):
			if not missing_collision.has(item["assetPath"]):
				missing_collision[item["assetPath"]] = []
			missing_collision[item["assetPath"]].append(item["id"])
		root.add_child(instance)
	for asset_path in missing_collision:
		push_warning("Venue asset '%s' has collisionEnabled instances but no enabled collision nodes." % asset_path)
	_venue_root.add_child(root)


func _apply_panorama(panorama: Dictionary) -> void:
	if not panorama["enabled"]:
		return
	var texture_path: String = panorama["texturePath"]
	if not ResourceLoader.exists(texture_path, "Texture2D"):
		push_error("Panorama texture '%s' is unavailable; fallback environment remains active." % texture_path)
		return
	var texture := ResourceLoader.load(texture_path, "Texture2D") as Texture2D
	if texture == null:
		push_error("Panorama texture '%s' failed to load; fallback environment remains active." % texture_path)
		return
	var panorama_material := PanoramaSkyMaterial.new()
	panorama_material.panorama = texture
	panorama_material.energy_multiplier = float(panorama["energyMultiplier"])
	var sky := Sky.new()
	sky.sky_material = panorama_material
	var environment := Environment.new()
	environment.background_mode = Environment.BG_SKY
	environment.sky = sky
	environment.sky_rotation = Vector3(0.0, deg_to_rad(float(panorama["rotationDeg"])), 0.0)
	_world_environment.environment = environment


func _create_cones(cones: Array, cone_scene: PackedScene) -> void:
	var venue_cones := Node3D.new()
	venue_cones.name = "Cones"
	var exercise_cones := Node3D.new()
	exercise_cones.name = "Cones"
	for cone in cones:
		var root := Node3D.new()
		root.name = cone["id"]
		root.position = _projection.project_cone_position(cone["position"], cone["id"])
		var model := cone_scene.instantiate()
		model.name = "TrafficConeModel"
		_disable_collision_recursively(model)
		root.add_child(model)
		if cone["color"].to_lower() != "none":
			var material := StandardMaterial3D.new()
			material.albedo_color = RuntimeGeometry.color_from_value(cone["color"], Color(1.0, 0.35, 0.03))
			material.roughness = 0.75
			var cylinder := CylinderMesh.new()
			cylinder.top_radius = 0.008
			cylinder.bottom_radius = 0.06
			cylinder.height = CONE_TOPPER_HEIGHT
			cylinder.radial_segments = 20
			cylinder.material = material
			var topper := MeshInstance3D.new()
			topper.name = "ColorTopper"
			topper.mesh = cylinder
			topper.position = Vector3(0.0, CONE_TOP_HEIGHT + CONE_TOPPER_HEIGHT * 0.5, 0.0)
			root.add_child(topper)
		if cone["id"].begins_with("venue--cone--"):
			venue_cones.add_child(root)
		else:
			exercise_cones.add_child(root)
	_venue_root.add_child(venue_cones)
	_track_root.add_child(exercise_cones)


func _create_markings(markings: Array) -> void:
	var venue_markings := Node3D.new()
	venue_markings.name = "Markings"
	var exercise_markings := Node3D.new()
	exercise_markings.name = "Markings"
	for marking in markings:
		if marking.has("_validationError"):
			push_warning(marking["_validationError"])
			continue
		if not marking["visibleInViewer"]:
			continue
		var style: String = marking["style"]
		if style not in ["solid", "dashed", "dotted"]:
			push_warning("Marking '%s' has unsupported style '%s'; solid fallback used." % [marking["id"], style])
			style = "solid"
		var material := StandardMaterial3D.new()
		material.albedo_color = RuntimeGeometry.color_from_value(marking["color"])
		material.roughness = 0.85
		material.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
		var marking_root := Node3D.new()
		marking_root.name = marking["id"]
		var path_error: String = MarkingPathContract.validate(marking["path"], "marking '%s'.path" % marking["id"])
		if not path_error.is_empty():
			push_warning("Marking '%s' was skipped: %s" % [marking["id"], path_error])
			continue
		var sampled := MarkingPathContract.sample(marking["path"])
		var geometry := RuntimeGeometry.create_marking_geometry(sampled, style)
		var projected_strokes: Array = []
		for stroke in geometry["strokes"]:
			var projected := _projection.project_polyline(
				stroke, "Marking", marking["id"], MAXIMUM_PROJECTION_SPACING, MARKING_SURFACE_OFFSET
			)
			projected_strokes.append(projected)
		var ribbon := RuntimeGeometry.create_projected_ribbon(
			"Ribbon", projected_strokes, float(marking["widthMeters"]), material
		)
		if ribbon != null: marking_root.add_child(ribbon)
		_add_projected_marking_dots(marking_root, geometry["dots"], marking, material)
		if marking["id"].begins_with("venue--marking--"):
			venue_markings.add_child(marking_root)
		else:
			exercise_markings.add_child(marking_root)
	_venue_root.add_child(venue_markings)
	_track_root.add_child(exercise_markings)


func _add_projected_marking_dots(parent: Node3D, dots: Array, marking: Dictionary, material: Material) -> void:
	if dots.is_empty(): return
	var transforms: Array[Transform3D] = []
	for dot in dots:
		var projected := _projection.project_point(
			dot, "Marking", marking["id"], MARKING_SURFACE_OFFSET
		)
		var up: Vector3 = projected["normal"]
		up = up.normalized()
		if up.length_squared() <= 0.00001: up = Vector3.UP
		var right := up.cross(Vector3.RIGHT).normalized() if absf(up.dot(Vector3.RIGHT)) < 0.95 \
			else up.cross(Vector3.FORWARD).normalized()
		var forward := right.cross(up).normalized()
		transforms.append(Transform3D(Basis(right, up, forward), projected["position"]))
	var disc := CylinderMesh.new()
	disc.top_radius = float(marking["widthMeters"]) * 0.5
	disc.bottom_radius = disc.top_radius
	disc.height = 0.008
	disc.radial_segments = 12
	disc.rings = 1
	disc.material = material
	var multimesh := MultiMesh.new()
	multimesh.transform_format = MultiMesh.TRANSFORM_3D
	multimesh.mesh = disc
	multimesh.instance_count = transforms.size()
	for index in transforms.size(): multimesh.set_instance_transform(index, transforms[index])
	var instance := MultiMeshInstance3D.new()
	instance.name = "Dots"
	instance.multimesh = multimesh
	instance.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	parent.add_child(instance)


func _create_trajectory(segments: Array) -> Dictionary:
	var root := Node3D.new()
	root.name = "Trajectory"
	var material := StandardMaterial3D.new()
	material.albedo_color = TrajectoryHighlightRendererScript.BASE_TRAJECTORY_COLOR
	material.roughness = 0.8
	material.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	_direction_markers_root = Node3D.new()
	_direction_markers_root.name = "DirectionMarkers"
	root.add_child(_direction_markers_root)
	var previous_end: Variant = null
	var previous_id := ""
	var route_points: Array = []
	var projected_segments: Array = []
	var route_valid := true
	var route_message := ""
	for segment in segments:
		var points: Array = segment["points"] if segment["type"] == "polyline" else RuntimeGeometry.sample_cubic_bezier(segment)
		if previous_end != null:
			var gap := Vector2(float(previous_end["x"]), float(previous_end["y"])).distance_to(
				Vector2(float(points[0]["x"]), float(points[0]["y"]))
			)
			if gap > 0.01:
				push_warning("Trajectory discontinuity between '%s' and '%s': %.3f m." % [previous_id, segment["id"], gap])
				route_valid = false
				route_message = "Trajectory is discontinuous between '%s' and '%s'." % [previous_id, segment["id"]]
		var projected := _projection.project_polyline(
			points, "TrajectorySegment", segment["id"], MAXIMUM_PROJECTION_SPACING, TRAJECTORY_SURFACE_OFFSET
		)
		projected_segments.append(projected)
		_add_direction_markers(_direction_markers_root, projected, material)
		for projected_point in projected:
			route_points.append(projected_point["position"])
			if not projected_point["hit"] and route_valid:
				route_valid = false
				route_message = "Trajectory segment '%s' contains a projection fallback." % segment["id"]
		previous_end = points[points.size() - 1]
		previous_id = segment["id"]
	_track_root.add_child(root)
	return {
		"points": route_points,
		"projected_segments": projected_segments,
		"root": root,
		"valid": route_valid,
		"message": route_message,
	}


func _create_trajectory_renderer(route_projection: Dictionary) -> void:
	var root: Node3D = route_projection["root"]
	var renderer = TrajectoryHighlightRendererScript.new()
	renderer.name = "Ribbon"
	if renderer.build_from_route(_route_path, route_projection["projected_segments"], TRAJECTORY_WIDTH):
		_trajectory_renderer = renderer
		root.add_child(renderer)
		if not renderer.diagnostic_warning.is_empty():
			push_warning(renderer.diagnostic_warning)
		return

	# Geometry mapping failure must not block Follow camera movement. Preserve
	# the former full-route BoxMesh rendering as the diagnostic fallback.
	var fallback_root := Node3D.new()
	fallback_root.name = "FullRouteFallback"
	var fallback_material := StandardMaterial3D.new()
	fallback_material.albedo_color = TrajectoryHighlightRendererScript.BASE_TRAJECTORY_COLOR
	fallback_material.roughness = 0.8
	fallback_material.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	for projected in route_projection["projected_segments"]:
		_add_projected_path(fallback_root, projected, TRAJECTORY_WIDTH, fallback_material, "Path")
	root.add_child(fallback_root)
	push_warning("%s Full-route BoxMesh fallback is active; Follow camera remains available." % renderer.diagnostic_warning)


func _add_projected_path(parent: Node3D, points: Array, width: float, material: Material, prefix: String) -> void:
	for index in range(points.size() - 1):
		var mesh := RuntimeGeometry.create_path_segment(
			"%s_%d" % [prefix, index], points[index]["position"], points[index + 1]["position"],
			points[index]["normal"], points[index + 1]["normal"], width, material
		)
		if mesh != null:
			parent.add_child(mesh)


func _add_direction_markers(parent: Node3D, points: Array, material: Material) -> void:
	var root := Node3D.new()
	root.name = "DirectionMarkers"
	var distance_to_next := DIRECTION_INTERVAL * 0.5
	var marker_index := 0
	for point_index in range(points.size() - 1):
		var start: Vector3 = points[point_index]["position"]
		var end: Vector3 = points[point_index + 1]["position"]
		var delta := end - start
		var length := delta.length()
		if length <= 0.000001:
			continue
		var direction := delta / length
		var distance := distance_to_next
		while distance <= length:
			var amount := distance / length
			var normal: Vector3 = points[point_index]["normal"].lerp(points[point_index + 1]["normal"], amount).normalized()
			root.add_child(_create_direction_marker(marker_index, start + direction * distance, direction, normal, material))
			marker_index += 1
			distance += DIRECTION_INTERVAL
		distance_to_next = distance - length
	parent.add_child(root)


func _create_direction_marker(index: int, center: Vector3, direction: Vector3, normal: Vector3, material: Material) -> Node3D:
	var marker := Node3D.new()
	marker.name = "Direction_%d" % index
	var tangent := direction.normalized()
	var surface_normal := normal.normalized()
	var perpendicular := surface_normal.cross(tangent).normalized()
	var offset := surface_normal * DIRECTION_SURFACE_OFFSET
	var tip := center + tangent * (DIRECTION_LENGTH * 0.5) + offset
	var tail := center - tangent * (DIRECTION_LENGTH * 0.5) + offset
	for data in [["Left", tail + perpendicular * DIRECTION_HALF_WIDTH], ["Right", tail - perpendicular * DIRECTION_HALF_WIDTH]]:
		var stroke := RuntimeGeometry.create_path_segment(
			data[0], data[1], tip, surface_normal, surface_normal, DIRECTION_LINE_WIDTH, material
		)
		if stroke != null:
			marker.add_child(stroke)
	return marker


func _disable_collision_recursively(node: Node) -> void:
	if node is CollisionShape3D or node is CollisionPolygon3D:
		node.disabled = true
	if node is CollisionObject3D:
		node.collision_layer = 0
		node.collision_mask = 0
		node.input_ray_pickable = false
	for child in node.get_children():
		_disable_collision_recursively(child)


func _contains_enabled_collision(node: Node) -> bool:
	if node is CollisionShape3D and not node.disabled:
		return true
	if node is CollisionPolygon3D and not node.disabled:
		return true
	for child in node.get_children():
		if _contains_enabled_collision(child):
			return true
	return false


func _load_packed_scene(path: String) -> PackedScene:
	if not ResourceLoader.exists(path, "PackedScene"):
		return null
	return ResourceLoader.load(path, "PackedScene") as PackedScene


func _resolve_external_track_url() -> String:
	var timestamp := int(Time.get_unix_time_from_system() * 1000.0)
	if OS.has_feature("web"):
		var script := "(() => { const u = new URL('tracks/default-track.json', window.location.href); u.searchParams.set('v', '%d'); return u.href; })()" % timestamp
		var resolved: Variant = JavaScriptBridge.eval(script, true)
		return str(resolved) if resolved != null else ""
	var base_url := _argument_value(
		"--base-url",
		str(ProjectSettings.get_setting("web_viewer/native_test_base_url", "http://127.0.0.1:8060/"))
	)
	if not base_url.ends_with("/"):
		base_url += "/"
	return "%stracks/default-track.json?v=%d" % [base_url, timestamp]


func _argument_value(name: String, fallback: String) -> String:
	var arguments := OS.get_cmdline_user_args()
	for index in range(arguments.size() - 1):
		if arguments[index] == name and not arguments[index + 1].is_empty():
			return arguments[index + 1]
	return fallback


func _show_loading(message: String) -> void:
	_loading_panel.visible = true
	_loading_label.text = message
	_error_panel.visible = false
	if is_instance_valid(_route_follow_ui):
		_route_follow_ui.visible = false


func _show_warning(message: String, warning: bool = true) -> void:
	_warning_label.text = message
	_warning_label.modulate = Color(1.0, 0.78, 0.3) if warning else Color(0.55, 0.95, 0.62)
	_warning_label.visible = true


func _show_blocking_error(message: String) -> void:
	_loading = false
	_loading_panel.visible = false
	_error_label.text = "Web Viewer could not start\n\n%s" % message
	_error_panel.visible = true
	_mobile_controls.set_controls_visible(false)
	_touch_controls_toggle.visible = false
	_route_follow_ui.visible = false
	_character.suspend_for_reload()
	push_error(message)
	if "--smoke-test" in OS.get_cmdline_user_args():
		get_tree().quit(1)


func _on_mode_changed(mode: String, message: String) -> void:
	_apply_trajectory_render_mode(mode)
	_mode_label.text = "Mode: %s" % mode
	_mobile_controls.set_mode(mode)
	_route_follow_ui.set_mode(mode)
	_controls_label.visible = mode != WebViewerCharacter.MODE_FOLLOW and not _mobile_controls.visible
	_show_warning(message, false)


func _on_route_follow_state_changed() -> void:
	# Playback, pause, steps, restart, and finish all publish through the same
	# controller signal. In the shader path this changes only one uniform.
	if _trajectory_renderer != null and _character.movement_mode == WebViewerCharacter.MODE_FOLLOW:
		_trajectory_renderer.update_route_distance(_route_follow.route_distance_meters)


func _apply_trajectory_render_mode(viewer_mode: String) -> void:
	var following := viewer_mode == WebViewerCharacter.MODE_FOLLOW
	if _trajectory_renderer != null:
		_trajectory_renderer.set_render_mode(
			TrajectoryHighlightRendererScript.FOLLOW_WINDOW if following else TrajectoryHighlightRendererScript.FULL_ROUTE,
			_route_follow.route_distance_meters if following else 0.0
		)
	if _direction_markers_root != null:
		# Iteration 1 keeps arrows on their existing BoxMesh contract. They are
		# hidden in Follow and immediately restored in Walk/Fly.
		_direction_markers_root.visible = not following


func _on_pointer_capture_changed(captured: bool) -> void:
	if captured:
		_controls_label.text = "WASD move · Shift faster · F Walk/Fly · Space/Ctrl fly · Follow: Space pause, Esc exit"
	else:
		_controls_label.text = "Click Viewer to capture mouse · WASD move · Shift faster · F Walk/Fly · Follow button"


func _toggle_touch_controls() -> void:
	_set_touch_controls_visible(not _mobile_controls.visible)


func _set_touch_controls_visible(show_controls: bool) -> void:
	_character.clear_runtime_input()
	_mobile_controls.set_controls_visible(show_controls)
	_route_follow_ui.set_touch_controls_visible(show_controls)
	_controls_label.visible = not show_controls and _character.movement_mode != WebViewerCharacter.MODE_FOLLOW
	_touch_controls_toggle.text = "Hide Touch UI" if show_controls else "Show Touch UI"


func _on_mobile_status_message(message: String, warning: bool) -> void:
	_show_warning(message, warning)


func _on_follow_requested() -> void:
	_mobile_controls.cancel_active_input()
	if not _character.enter_follow_from_start():
		_show_warning("Follow unavailable: %s" % _route_follow.validation_message(), true)


func _on_follow_exit_requested() -> void:
	_mobile_controls.cancel_active_input()
	_character.exit_follow_to_safe_mode()


func _on_follow_restart_requested() -> void:
	_route_follow.restart()
	_character.refresh_follow_pose()


func _on_follow_step_backward_requested() -> void:
	_route_follow.step_backward()
	_character.refresh_follow_pose()


func _on_follow_play_pause_requested() -> void:
	_route_follow.toggle_play_pause()


func _on_follow_step_forward_requested() -> void:
	_route_follow.step_forward()
	_character.refresh_follow_pose()


func _on_follow_speed_down_requested() -> void:
	_route_follow.speed_down()


func _on_follow_speed_up_requested() -> void:
	_route_follow.speed_up()


func _on_follow_look_forward_requested() -> void:
	_route_follow.reset_look()
	_character.refresh_follow_pose()
