extends SceneTree

const Parser := preload("res://scripts/track_v4_parser.gd")
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

	var duplicate_id := base.duplicate(true)
	duplicate_id["cones"][1]["id"] = duplicate_id["cones"][0]["id"]
	_expect(not _validate(duplicate_id), "duplicate runtime ID must fail")

	var unsupported_segment := base.duplicate(true)
	unsupported_segment["trajectory"]["segments"][0]["type"] = "arc"
	_expect(not _validate(unsupported_segment), "unsupported trajectory type must fail")

	var invalid_finite := base.duplicate(true)
	invalid_finite["area"]["length"] = INF
	_expect(not _validate(invalid_finite), "non-finite number must fail")

	_finish()


func _validate(track: Dictionary) -> bool:
	return Parser.validate(track, "test").is_empty()


func _expect(condition: bool, message: String) -> void:
	if not condition:
		_failures.append(message)


func _finish() -> void:
	if _failures.is_empty():
		print("TrackV4Parser tests passed.")
		quit(0)
		return
	for failure in _failures:
		push_error(failure)
	quit(1)
