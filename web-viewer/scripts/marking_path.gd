class_name MarkingPath
extends RefCounted

## Deterministic Exported Track v5 marking Path validation and adaptive sampling.
## Tolerances intentionally mirror the desktop PathSamplingSettings values.

const MAX_CHORD_METERS := 0.35
const FLATNESS_TOLERANCE_METERS := 0.02
const MAX_RECURSION_DEPTH := 12
const DUPLICATE_TOLERANCE_METERS := 0.000001


static func validate(path: Variant, prefix: String = "path") -> String:
	if not path is Dictionary:
		return "%s must be a dictionary" % prefix
	var error := _validate_point(path.get("start"), "%s.start" % prefix)
	if not error.is_empty(): return error
	var segments: Variant = path.get("segments")
	if not segments is Array or segments.is_empty():
		return "%s.segments must be a non-empty array" % prefix
	for index in segments.size():
		var segment: Variant = segments[index]
		var segment_prefix := "%s.segments[%d]" % [prefix, index]
		if not segment is Dictionary:
			return "%s must be a dictionary" % segment_prefix
		var type: Variant = segment.get("type")
		if type == "line":
			error = _validate_point(segment.get("end"), "%s.end" % segment_prefix)
		elif type == "cubicBezier":
			for field in ["control1", "control2", "end"]:
				error = _validate_point(segment.get(field), "%s.%s" % [segment_prefix, field])
				if not error.is_empty(): return error
		else:
			return "%s.type '%s' is unsupported" % [segment_prefix, str(type)]
		if not error.is_empty(): return error
	var sampled := sample(path)
	if sampled["points"].size() < 2 or float(sampled["totalLength"]) <= DUPLICATE_TOLERANCE_METERS:
		return "%s does not produce usable geometry" % prefix
	return ""


static func sample(path: Dictionary) -> Dictionary:
	var points: Array[Vector2] = []
	_append_distinct(points, _point(path["start"]))
	var current: Vector2 = points[0]
	for segment in path["segments"]:
		if segment["type"] == "line":
			_append_distinct(points, _point(segment["end"]))
			current = _point(segment["end"])
		else:
			var end := _point(segment["end"])
			_sample_cubic(points, current, _point(segment["control1"]),
				_point(segment["control2"]), end, 0)
			current = end
	var cumulative: Array[float] = []
	var total := 0.0
	for index in points.size():
		if index > 0: total += points[index - 1].distance_to(points[index])
		cumulative.append(total)
	return {"points": points, "cumulativeDistances": cumulative, "totalLength": total}


static func evaluate_cubic(start: Vector2, control1: Vector2, control2: Vector2, end: Vector2, t: float) -> Vector2:
	var inverse := 1.0 - t
	return inverse * inverse * inverse * start + 3.0 * inverse * inverse * t * control1 + \
		3.0 * inverse * t * t * control2 + t * t * t * end


static func _sample_cubic(output: Array[Vector2], start: Vector2, control1: Vector2,
		control2: Vector2, end: Vector2, depth: int) -> void:
	if depth >= MAX_RECURSION_DEPTH or _is_flat_enough(start, control1, control2, end):
		_append_distinct(output, end)
		return
	var p01 := (start + control1) * 0.5
	var p12 := (control1 + control2) * 0.5
	var p23 := (control2 + end) * 0.5
	var p012 := (p01 + p12) * 0.5
	var p123 := (p12 + p23) * 0.5
	var middle := (p012 + p123) * 0.5
	_sample_cubic(output, start, p01, p012, middle, depth + 1)
	_sample_cubic(output, middle, p123, p23, end, depth + 1)


static func _is_flat_enough(start: Vector2, control1: Vector2, control2: Vector2, end: Vector2) -> bool:
	var chord := start.distance_to(end)
	if chord > MAX_CHORD_METERS: return false
	var control_polygon := start.distance_to(control1) + control1.distance_to(control2) + control2.distance_to(end)
	if control_polygon - chord > FLATNESS_TOLERANCE_METERS: return false
	return maxf(_distance_to_line(control1, start, end), _distance_to_line(control2, start, end)) <= \
		FLATNESS_TOLERANCE_METERS


static func _distance_to_line(point: Vector2, start: Vector2, end: Vector2) -> float:
	var chord := end - start
	var length := chord.length()
	if length <= DUPLICATE_TOLERANCE_METERS: return point.distance_to(start)
	return absf(chord.cross(point - start)) / length


static func _append_distinct(output: Array[Vector2], point: Vector2) -> void:
	if output.is_empty() or output[-1].distance_to(point) > DUPLICATE_TOLERANCE_METERS:
		output.append(point)


static func _validate_point(value: Variant, prefix: String) -> String:
	if not value is Dictionary or not _finite(value.get("x")) or not _finite(value.get("y")):
		return "%s must contain finite x/y numbers" % prefix
	return ""


static func _finite(value: Variant) -> bool:
	return (value is int or value is float) and is_finite(float(value))


static func _point(value: Dictionary) -> Vector2:
	return Vector2(float(value["x"]), float(value["y"]))
