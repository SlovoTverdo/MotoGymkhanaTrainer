extends SceneTree

const Parser := preload("res://scripts/track_v4_parser.gd")
const PathContract := preload("res://scripts/marking_path.gd")
const TRACK_PATH := "res://tracks/default-track.json"

var _failures: Array[String] = []


func _initialize() -> void:
	var text := FileAccess.get_file_as_string(TRACK_PATH)
	_expect(FileAccess.get_open_error() == OK, "fallback Track must be readable")
	var valid := Parser.parse_json(text, TRACK_PATH)
	_expect(valid["ok"], "fallback Track must validate: %s" % valid["error"])
	if not valid["ok"]:
		_finish()
		return

	var base: Dictionary = valid["track"]
	_expect(not Parser.parse_json("{", "invalid-json")["ok"], "invalid JSON must fail")

	var wrong_version := base.duplicate(true)
	wrong_version["formatVersion"] = 3
	_expect(not _validate(wrong_version), "unsupported formatVersion must fail")

	var missing_track := base.duplicate(true)
	missing_track.erase("track")
	_expect(not _validate(missing_track), "missing required dictionary must fail")

	var invalid_area := base.duplicate(true)
	invalid_area["area"]["width"] = 0
	_expect(not _validate(invalid_area), "non-positive area must fail")

	var unsafe_asset := base.duplicate(true)
	unsafe_asset["venueObjects"][0]["assetPath"] = "res://venues/../escape.tscn"
	_expect(not _validate(unsafe_asset), "unsafe res:// path must fail")

	var imported_asset := base.duplicate(true)
	imported_asset["venueObjects"][0]["objectType"] = "imported"
	imported_asset["venueObjects"][0]["assetId"] = "venue-object-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
	imported_asset["venueObjects"][0]["collisionMode"] = "generated"
	imported_asset["venueObjects"][0]["assetPath"] = \
		"res://Assets/Venue/Imported/venue-object-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/scene.tscn"
	_expect(_validate(imported_asset),
		"Track v5 parser must retain imported object diagnostics without requiring Web GLB runtime")

	var duplicate_id := base.duplicate(true)
	duplicate_id["cones"][1]["id"] = duplicate_id["cones"][0]["id"]
	_expect(not _validate(duplicate_id), "duplicate runtime ID must fail")

	var unsupported_segment := base.duplicate(true)
	unsupported_segment["trajectory"]["segments"][0]["type"] = "arc"
	_expect(not _validate(unsupported_segment), "unsupported trajectory type must fail")

	var invalid_finite := base.duplicate(true)
	invalid_finite["area"]["length"] = INF
	_expect(not _validate(invalid_finite), "non-finite number must fail")

	var invalid_marking := base.duplicate(true)
	invalid_marking["markings"] = [{"id": "bad-marking", "color": "#FFFFFF", "widthMeters": 0.1,
		"style": "solid", "visibleInViewer": true, "path": {"start": {"x": 0.0, "y": 0.0},
		"segments": [{"type": "arc", "end": {"x": 1.0, "y": 0.0}}]}}]
	_expect(_validate(invalid_marking) and invalid_marking["markings"][0].has("_validationError"),
		"invalid marking must be rejected individually without breaking Track validation")

	_test_marking_paths()

	_finish()


func _test_marking_paths() -> void:
	var line := {"start": {"x": 0.0, "y": 0.0}, "segments": [
		{"type": "line", "end": {"x": 3.0, "y": 4.0}}]}
	_expect(PathContract.validate(line).is_empty(), "line Path must parse")
	var line_sample := PathContract.sample(line)
	_expect(is_equal_approx(line_sample["totalLength"], 5.0), "line length must equal 5")

	var cubic := {"start": {"x": 0.0, "y": 0.0}, "segments": [{
		"type": "cubicBezier", "control1": {"x": 1.0, "y": 2.0},
		"control2": {"x": 2.0, "y": -2.0}, "end": {"x": 3.0, "y": 0.0}}]}
	_expect(PathContract.validate(cubic).is_empty(), "cubic Path must parse")
	var cubic_sample := PathContract.sample(cubic)
	_expect(cubic_sample["points"][0].is_equal_approx(Vector2.ZERO) and
		cubic_sample["points"][-1].is_equal_approx(Vector2(3, 0)), "cubic endpoints must be exact")
	_expect(float(cubic_sample["totalLength"]) > 3.0, "cubic approximate length must exceed its chord")
	var overshoot := {"start": {"x": 0.0, "y": 0.0}, "segments": [{
		"type": "cubicBezier", "control1": {"x": 10.0, "y": 0.0},
		"control2": {"x": -10.0, "y": 0.0}, "end": {"x": 0.1, "y": 0.0}}]}
	var overshoot_sample := PathContract.sample(overshoot)
	_expect(overshoot_sample["points"].size() > 2 and float(overshoot_sample["totalLength"]) > 1.0,
		"collinear cubic overshoot must not collapse to its endpoint chord")

	var unknown := cubic.duplicate(true)
	unknown["segments"][0]["type"] = "arc"
	_expect(not PathContract.validate(unknown).is_empty(), "unknown marking segment must fail")

	var joined := {"start": {"x": 0.0, "y": 0.0}, "segments": [
		{"type": "line", "end": {"x": 0.5, "y": 0.0}},
		{"type": "line", "end": {"x": 1.0, "y": 0.0}}]}
	var joined_sample := PathContract.sample(joined)
	_expect(joined_sample["points"].size() == 3, "line joins must not be duplicated")
	var geometry := RuntimeGeometry.create_marking_geometry(joined_sample, "dashed")
	_expect(geometry["strokes"].size() == 2 and
		is_equal_approx(float(geometry["strokes"][1][1]["x"]), 0.75),
		"dash phase must continue across segment boundaries")

	var transformed := joined.duplicate(true)
	for point in [transformed["start"], transformed["segments"][0]["end"], transformed["segments"][1]["end"]]:
		point["x"] = float(point["x"]) * 2.0 + 4.0
		point["y"] = float(point["y"]) * 3.0 - 1.0
	var transformed_sample := PathContract.sample(transformed)
	_expect(transformed_sample["points"][0].is_equal_approx(Vector2(4, -1)) and
		is_equal_approx(transformed_sample["totalLength"], 2.0), "transformed Path sample must use world geometry")


func _validate(track: Dictionary) -> bool:
	return Parser.validate(track, "test").is_empty()


func _expect(condition: bool, message: String) -> void:
	if not condition:
		_failures.append(message)


func _finish() -> void:
	if _failures.is_empty():
		print("TrackV5Parser and marking Path tests passed.")
		quit(0)
		return
	for failure in _failures:
		push_error(failure)
	quit(1)
