extends Node3D

const CONE_MODEL_PATH := "res://Assets/Models/Traffic Cone_Textured.glb"
const FALLBACK_TRACK_PATH := "res://tracks/default-track.json"
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


func _ready() -> void:
	_fallback_environment = _world_environment.environment
	_character.set_input_state(_input_state)
	_mobile_controls.configure(_input_state, _character.movement_mode)
	_http_request.request_completed.connect(_on_track_request_completed)
	_character.mode_changed.connect(_on_mode_changed)
	_character.pointer_capture_changed.connect(_on_pointer_capture_changed)
	_touch_controls_toggle.pressed.connect(_toggle_touch_controls)
	_mobile_controls.status_message.connect(_on_mobile_status_message)
	_error_panel.visible = false
	_warning_label.visible = false
	_mode_label.text = "Mode: Walk"
	_controls_label.text = "Click to capture mouse · WASD move · Shift faster · F Walk/Fly · Space/Ctrl fly · T touch UI · Esc release"
	var touch_setting := _argument_value("--touch-controls", "auto").to_lower()
	var show_touch_controls := DisplayServer.is_touchscreen_available()
	if touch_setting == "show":
		show_touch_controls = true
	elif touch_setting == "hide":
		show_touch_controls = false
	_set_touch_controls_visible(show_touch_controls)
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
	_create_trajectory(track["trajectory"]["segments"])

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
	if "--smoke-test" in OS.get_cmdline_user_args():
		print("Touch controls visible: %s; orientation hint visible: %s." % [
			_mobile_controls.visible, $UI/MobileControls/OrientationHint.visible
		])
		get_tree().quit(0)


func _clear_runtime() -> void:
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
		if not marking["visibleInViewer"]:
			continue
		if marking["type"] != "line" and marking["type"] != "polyline":
			push_warning("Marking '%s' has unsupported type '%s'; skipped." % [marking["id"], marking["type"]])
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
		var stroke_index := 0
		for stroke in RuntimeGeometry.create_marking_strokes(marking["points"], style):
			var projected := _projection.project_polyline(
				stroke, "Marking", marking["id"], MAXIMUM_PROJECTION_SPACING, MARKING_SURFACE_OFFSET
			)
			_add_projected_path(marking_root, projected, float(marking["widthMeters"]), material, "Stroke_%d" % stroke_index)
			stroke_index += 1
		if marking["id"].begins_with("venue--marking--"):
			venue_markings.add_child(marking_root)
		else:
			exercise_markings.add_child(marking_root)
	_venue_root.add_child(venue_markings)
	_track_root.add_child(exercise_markings)


func _create_trajectory(segments: Array) -> void:
	var root := Node3D.new()
	root.name = "Trajectory"
	var material := StandardMaterial3D.new()
	material.albedo_color = Color(0.1, 0.9, 0.95)
	material.roughness = 0.8
	material.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	var previous_end: Variant = null
	var previous_id := ""
	for segment in segments:
		var points: Array = segment["points"] if segment["type"] == "polyline" else RuntimeGeometry.sample_cubic_bezier(segment)
		if previous_end != null:
			var gap := Vector2(float(previous_end["x"]), float(previous_end["y"])).distance_to(
				Vector2(float(points[0]["x"]), float(points[0]["y"]))
			)
			if gap > 0.01:
				push_warning("Trajectory discontinuity between '%s' and '%s': %.3f m." % [previous_id, segment["id"], gap])
		var projected := _projection.project_polyline(
			points, "TrajectorySegment", segment["id"], MAXIMUM_PROJECTION_SPACING, TRAJECTORY_SURFACE_OFFSET
		)
		var segment_root := Node3D.new()
		segment_root.name = segment["id"]
		_add_projected_path(segment_root, projected, TRAJECTORY_WIDTH, material, "Path")
		_add_direction_markers(segment_root, projected, material)
		root.add_child(segment_root)
		previous_end = points[points.size() - 1]
		previous_id = segment["id"]
	_track_root.add_child(root)


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
	_character.suspend_for_reload()
	push_error(message)
	if "--smoke-test" in OS.get_cmdline_user_args():
		get_tree().quit(1)


func _on_mode_changed(mode: String, message: String) -> void:
	_mode_label.text = "Mode: %s" % mode
	_mobile_controls.set_mode(mode)
	_show_warning(message, false)


func _on_pointer_capture_changed(captured: bool) -> void:
	if captured:
		_controls_label.text = "WASD move · Shift faster · F Walk/Fly · Space/Ctrl fly · T touch UI · Esc release"
	else:
		_controls_label.text = "Click Viewer to capture mouse · WASD move · Shift faster · F Walk/Fly · T touch UI"


func _toggle_touch_controls() -> void:
	_set_touch_controls_visible(not _mobile_controls.visible)


func _set_touch_controls_visible(show_controls: bool) -> void:
	_character.clear_runtime_input()
	_mobile_controls.set_controls_visible(show_controls)
	_controls_label.visible = not show_controls
	_touch_controls_toggle.text = "Hide Touch UI" if show_controls else "Show Touch UI"


func _on_mobile_status_message(message: String, warning: bool) -> void:
	_show_warning(message, warning)
