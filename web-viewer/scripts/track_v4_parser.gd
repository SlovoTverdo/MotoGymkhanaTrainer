class_name TrackV4Parser
extends RefCounted

## Strict parser for the self-contained Exported Track v5 runtime contract.
## Editor documents and transforms are intentionally outside this module.

const SUPPORTED_FORMAT_VERSION := 5
const SUPPORTED_TRAJECTORY_TYPES := {"polyline": true, "cubicBezier": true}
const MarkingPathContract := preload("res://scripts/marking_path.gd")


static func parse_json(json_text: String, source_name: String) -> Dictionary:
	if json_text.strip_edges().is_empty():
		return _failure("Track '%s' is empty." % source_name)

	var json := JSON.new()
	var parse_error := json.parse(json_text)
	if parse_error != OK:
		return _failure(
			"Track '%s' contains invalid JSON at line %d: %s" %
			[source_name, json.get_error_line(), json.get_error_message()]
		)

	if not json.data is Dictionary:
		return _failure("Track '%s' root must be a dictionary." % source_name)

	var validation_error := validate(json.data, source_name)
	if not validation_error.is_empty():
		return _failure(validation_error)
	return {"ok": true, "track": json.data, "error": ""}


static func validate(track: Dictionary, source_name: String = "<track>") -> String:
	var required_dictionaries := ["track", "venue", "area", "panorama", "trajectory"]
	var required_arrays := ["venueObjects", "elements", "cones", "markings", "checkpoints"]

	if not track.has("formatVersion") or not _is_number(track["formatVersion"]):
		return _contract_error(source_name, "formatVersion must be a number")
	if int(track["formatVersion"]) != SUPPORTED_FORMAT_VERSION:
		return _contract_error(
			source_name,
			"unsupported formatVersion %s; expected %d" %
			[str(track["formatVersion"]), SUPPORTED_FORMAT_VERSION]
		)

	for key in required_dictionaries:
		if not track.has(key) or not track[key] is Dictionary:
			return _contract_error(source_name, "%s must be a dictionary" % key)
	for key in required_arrays:
		if not track.has(key) or not track[key] is Array:
			return _contract_error(source_name, "%s must be an array" % key)
	if not track["trajectory"].has("segments") or not track["trajectory"]["segments"] is Array:
		return _contract_error(source_name, "trajectory.segments must be an array")

	var error := _validate_metadata(track["track"], "track", source_name)
	if not error.is_empty(): return error
	error = _validate_metadata(track["venue"], "venue", source_name)
	if not error.is_empty(): return error

	var area: Dictionary = track["area"]
	if not _positive_number(area.get("width")) or not _positive_number(area.get("length")):
		return _contract_error(source_name, "area width and length must be finite positive numbers")

	error = _validate_panorama(track["panorama"], source_name)
	if not error.is_empty(): return error
	error = _validate_venue_objects(track["venueObjects"], source_name)
	if not error.is_empty(): return error
	error = _validate_elements(track["elements"], source_name)
	if not error.is_empty(): return error
	error = _validate_cones(track["cones"], source_name)
	if not error.is_empty(): return error
	error = _validate_markings(track["markings"], source_name)
	if not error.is_empty(): return error
	error = _validate_trajectory(track["trajectory"]["segments"], source_name)
	if not error.is_empty(): return error
	error = _validate_checkpoints(track["checkpoints"], source_name)
	if not error.is_empty(): return error
	return _validate_unique_ids(track, source_name)


static func is_safe_resource_path(path: Variant, required_extension: String = "") -> bool:
	if not path is String or path.is_empty() or not path.begins_with("res://"):
		return false
	if "\\" in path or ":" in path.substr(6):
		return false
	var resource_path: String = path
	var relative: String = resource_path.substr(6)
	for part in relative.split("/", true):
		if part.is_empty() or part == "." or part == "..":
			return false
	return required_extension.is_empty() or resource_path.to_lower().ends_with(required_extension.to_lower())


static func _validate_metadata(value: Dictionary, prefix: String, source_name: String) -> String:
	if not _non_empty_string(value.get("id")) or not _non_empty_string(value.get("name")):
		return _contract_error(source_name, "%s.id and %s.name must be non-empty strings" % [prefix, prefix])
	return ""


static func _validate_panorama(panorama: Dictionary, source_name: String) -> String:
	if not panorama.get("enabled") is bool:
		return _contract_error(source_name, "panorama.enabled must be a boolean")
	if not _finite_number(panorama.get("rotationDeg")):
		return _contract_error(source_name, "panorama.rotationDeg must be finite")
	if not _finite_number(panorama.get("energyMultiplier")) or float(panorama["energyMultiplier"]) < 0.0:
		return _contract_error(source_name, "panorama.energyMultiplier must be finite and non-negative")
	var texture_path: Variant = panorama.get("texturePath")
	if not texture_path is String:
		return _contract_error(source_name, "panorama.texturePath must be a string")
	if panorama["enabled"] and not is_safe_resource_path(texture_path):
		return _contract_error(source_name, "enabled panorama requires a safe res:// texturePath")
	if not texture_path.is_empty() and not is_safe_resource_path(texture_path):
		return _contract_error(source_name, "panorama.texturePath must be a safe res:// path")
	return ""


static func _validate_venue_objects(objects: Array, source_name: String) -> String:
	for index in objects.size():
		var item: Variant = objects[index]
		var prefix := "venueObjects[%d]" % index
		if not item is Dictionary:
			return _contract_error(source_name, "%s must be a dictionary" % prefix)
		if not _non_empty_string(item.get("id")) or not _non_empty_string(item.get("name")):
			return _contract_error(source_name, "%s identity and name are required" % prefix)
		if not is_safe_resource_path(item.get("assetPath"), ".tscn"):
			return _contract_error(source_name, "%s.assetPath must be a safe res:// .tscn path" % prefix)
		var error := _validate_point2(item.get("position"), "%s.position" % prefix, source_name)
		if not error.is_empty(): return error
		if not _finite_number(item.get("elevation")) or not _finite_number(item.get("rotationDeg")):
			return _contract_error(source_name, "%s elevation and rotation must be finite" % prefix)
		error = _validate_scale3(item.get("scale"), "%s.scale" % prefix, source_name)
		if not error.is_empty(): return error
		if not item.get("footprint") is Dictionary or \
				not _positive_number(item["footprint"].get("width")) or \
				not _positive_number(item["footprint"].get("length")):
			return _contract_error(source_name, "%s.footprint must have positive width and length" % prefix)
		if not item.get("collisionEnabled") is bool or not item.get("visibleInViewer") is bool:
			return _contract_error(source_name, "%s visibility/collision flags must be booleans" % prefix)
	return ""


static func _validate_elements(elements: Array, source_name: String) -> String:
	for index in elements.size():
		var item: Variant = elements[index]
		var prefix := "elements[%d]" % index
		if not item is Dictionary or not _non_empty_string(item.get("instanceId")) or \
				not _non_empty_string(item.get("definitionId")):
			return _contract_error(source_name, "%s identity is invalid" % prefix)
		if not _safe_relative_json_path(item.get("exercisePath")):
			return _contract_error(source_name, "%s.exercisePath is unsafe" % prefix)
		var error := _validate_point2(item.get("position"), "%s.position" % prefix, source_name)
		if not error.is_empty(): return error
		if not _finite_number(item.get("rotationDeg")):
			return _contract_error(source_name, "%s.rotationDeg must be finite" % prefix)
		var scale: Variant = item.get("scale")
		if not scale is Dictionary or not _finite_nonzero(scale.get("x")) or not _finite_nonzero(scale.get("y")):
			return _contract_error(source_name, "%s.scale must contain finite non-zero x/y" % prefix)
	return ""


static func _validate_cones(cones: Array, source_name: String) -> String:
	for index in cones.size():
		var cone: Variant = cones[index]
		if not cone is Dictionary or not _non_empty_string(cone.get("id")):
			return _contract_error(source_name, "cones[%d].id must be non-empty" % index)
		var error := _validate_point2(cone.get("position"), "cones[%d].position" % index, source_name)
		if not error.is_empty(): return error
		if not cone.get("color") is String or not cone.get("type") is String:
			return _contract_error(source_name, "cones[%d] color/type must be strings" % index)
	return ""


static func _validate_markings(markings: Array, source_name: String) -> String:
	var marking_ids := {}
	for index in markings.size():
		var marking: Variant = markings[index]
		var prefix := "markings[%d]" % index
		if not marking is Dictionary:
			markings[index] = {"id": "invalid-marking-%d" % index, "_validationError":
				_contract_error(source_name, "%s must be an object and was skipped" % prefix)}
			continue
		if not _non_empty_string(marking.get("id")):
			marking["id"] = "invalid-marking-%d" % index
			marking["_validationError"] = _contract_error(source_name, "%s.id must be non-empty; marking was skipped" % prefix)
			continue
		if marking_ids.has(marking["id"]):
			marking["_validationError"] = _contract_error(source_name, "marking '%s' has a duplicated id and was skipped" % marking["id"])
			continue
		var error: String = MarkingPathContract.validate(marking.get("path"), "%s.path" % prefix)
		if not error.is_empty():
			marking["_validationError"] = _contract_error(source_name, "marking '%s' was skipped: %s" % [marking["id"], error])
			continue
		if not _positive_number(marking.get("widthMeters")):
			marking["_validationError"] = _contract_error(source_name, "marking '%s' was skipped: widthMeters must be positive" % marking["id"])
			continue
		if not _is_canonical_color(marking.get("color")) or marking.get("style") not in ["solid", "dashed", "dotted"] or \
				not marking.get("visibleInViewer") is bool:
			marking["_validationError"] = _contract_error(source_name, "marking '%s' was skipped: color/style/visibility values are invalid" % marking["id"])
			continue
		marking_ids[marking["id"]] = true
	return ""


static func _validate_trajectory(segments: Array, source_name: String) -> String:
	for index in segments.size():
		var segment: Variant = segments[index]
		var prefix := "trajectory.segments[%d]" % index
		if not segment is Dictionary or not _non_empty_string(segment.get("id")):
			return _contract_error(source_name, "%s.id must be non-empty" % prefix)
		var type: Variant = segment.get("type")
		if not type is String or not SUPPORTED_TRAJECTORY_TYPES.has(type):
			return _contract_error(source_name, "%s.type '%s' is unsupported" % [prefix, str(type)])
		if type == "polyline":
			var points: Variant = segment.get("points")
			if not points is Array or points.size() < 2:
				return _contract_error(source_name, "%s.points must contain at least two points" % prefix)
			for point_index in points.size():
				var error := _validate_point2(points[point_index], "%s.points[%d]" % [prefix, point_index], source_name)
				if not error.is_empty(): return error
		else:
			for point_name in ["start", "control1", "control2", "end"]:
				var error := _validate_point2(segment.get(point_name), "%s.%s" % [prefix, point_name], source_name)
				if not error.is_empty(): return error
	return ""


static func _validate_checkpoints(checkpoints: Array, source_name: String) -> String:
	for index in checkpoints.size():
		var item: Variant = checkpoints[index]
		var prefix := "checkpoints[%d]" % index
		if not item is Dictionary or not _non_empty_string(item.get("id")):
			return _contract_error(source_name, "%s.id must be non-empty" % prefix)
		if not _is_number(item.get("order")):
			return _contract_error(source_name, "%s.order must be numeric" % prefix)
		var error := _validate_point2(item.get("center"), "%s.center" % prefix, source_name)
		if not error.is_empty(): return error
		error = _validate_point2(item.get("direction"), "%s.direction" % prefix, source_name)
		if not error.is_empty(): return error
		if not _positive_number(item.get("width")):
			return _contract_error(source_name, "%s.width must be positive" % prefix)
	return ""


static func _validate_unique_ids(track: Dictionary, source_name: String) -> String:
	var seen := {}
	var ids: Array = []
	for item in track["venueObjects"]: ids.append(item["id"])
	for item in track["elements"]: ids.append(item["instanceId"])
	for item in track["cones"]: ids.append(item["id"])
	for item in track["trajectory"]["segments"]: ids.append(item["id"])
	for item in track["checkpoints"]: ids.append(item["id"])
	for id in ids:
		if seen.has(id):
			return _contract_error(source_name, "exported id '%s' is duplicated" % id)
		seen[id] = true
	for marking in track["markings"]:
		if marking.has("_validationError"): continue
		if seen.has(marking["id"]):
			marking["_validationError"] = _contract_error(
				source_name, "marking '%s' duplicated another exported id and was skipped" % marking["id"])
		else:
			seen[marking["id"]] = true
	return ""


static func _validate_point2(value: Variant, prefix: String, source_name: String) -> String:
	if not value is Dictionary or not _finite_number(value.get("x")) or not _finite_number(value.get("y")):
		return _contract_error(source_name, "%s must contain finite x/y numbers" % prefix)
	return ""


static func _validate_scale3(value: Variant, prefix: String, source_name: String) -> String:
	if not value is Dictionary or not _positive_number(value.get("x")) or \
			not _positive_number(value.get("y")) or not _positive_number(value.get("z")):
		return _contract_error(source_name, "%s must contain positive x/y/z" % prefix)
	return ""


static func _safe_relative_json_path(value: Variant) -> bool:
	if not value is String or value.is_empty() or value.begins_with("res://") or ":" in value or "\\" in value:
		return false
	if not value.to_lower().ends_with(".json"):
		return false
	for part in value.split("/", true):
		if part.is_empty() or part == "." or part == "..":
			return false
	return true


static func _non_empty_string(value: Variant) -> bool:
	return value is String and not value.strip_edges().is_empty()


static func _is_number(value: Variant) -> bool:
	return typeof(value) == TYPE_INT or typeof(value) == TYPE_FLOAT


static func _finite_number(value: Variant) -> bool:
	return _is_number(value) and is_finite(float(value))


static func _positive_number(value: Variant) -> bool:
	return _finite_number(value) and float(value) > 0.0


static func _is_canonical_color(value: Variant) -> bool:
	if not value is String or value.length() != 7 or not value.begins_with("#"):
		return false
	for index in range(1, 7):
		if value[index].to_lower() not in "0123456789abcdef":
			return false
	return value == value.to_upper()


static func _finite_nonzero(value: Variant) -> bool:
	return _finite_number(value) and not is_zero_approx(float(value))


static func _contract_error(source_name: String, message: String) -> String:
	return "Track '%s' is invalid: %s." % [source_name, message]


static func _failure(message: String) -> Dictionary:
	return {"ok": false, "track": {}, "error": message}
