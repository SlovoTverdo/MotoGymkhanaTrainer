class_name RoutePath
extends RefCounted

## Runtime-only route built from the already projected global Track trajectory.
## Distances are measured along the 3D polyline in meters and never serialized.

const POINT_EPSILON_METERS := 0.001
const MINIMUM_ROUTE_LENGTH_METERS := 0.1

var points: Array[Vector3] = []
var cumulative_distances: PackedFloat32Array = PackedFloat32Array()
var total_length := 0.0
var validation_message := "Route has not been built."

var _is_valid := false
var _last_valid_forward := Vector3.FORWARD


func build(source_points: Array) -> void:
	clear()
	var rejected_non_finite := 0
	for value in source_points:
		var point := _extract_point(value)
		if not _is_finite_vector(point):
			rejected_non_finite += 1
			continue
		if not points.is_empty() and points[-1].distance_to(point) <= POINT_EPSILON_METERS:
			continue
		points.append(point)

	if points.size() < 2:
		validation_message = "Route requires at least two distinct finite projected points."
		return

	var filtered: Array[Vector3] = [points[0]]
	for index in range(1, points.size()):
		if filtered[-1].distance_to(points[index]) > POINT_EPSILON_METERS:
			filtered.append(points[index])
	points = filtered
	if points.size() < 2:
		validation_message = "Route contains no usable segments."
		return

	cumulative_distances.resize(points.size())
	cumulative_distances[0] = 0.0
	for index in range(1, points.size()):
		var segment_length := points[index - 1].distance_to(points[index])
		total_length += segment_length
		cumulative_distances[index] = total_length
		if segment_length > POINT_EPSILON_METERS:
			_last_valid_forward = (points[index] - points[index - 1]) / segment_length

	if total_length <= MINIMUM_ROUTE_LENGTH_METERS or is_equal_approx(total_length, MINIMUM_ROUTE_LENGTH_METERS):
		validation_message = "Route total length is too small."
		return

	_is_valid = true
	validation_message = ""
	if rejected_non_finite > 0:
		validation_message = "%d non-finite route point(s) were ignored." % rejected_non_finite


func clear() -> void:
	points.clear()
	cumulative_distances = PackedFloat32Array()
	total_length = 0.0
	_is_valid = false
	validation_message = "Route has not been built."
	_last_valid_forward = Vector3.FORWARD


func invalidate(reason: String) -> void:
	_is_valid = false
	validation_message = reason


func is_valid() -> bool:
	return _is_valid


func clamp_distance(distance_meters: float) -> float:
	if not is_finite(distance_meters):
		return 0.0
	return clampf(distance_meters, 0.0, total_length)


func sample_position(distance_meters: float) -> Vector3:
	if points.is_empty():
		return Vector3.ZERO
	if points.size() == 1 or total_length <= 0.0:
		return points[0]
	var clamped := clamp_distance(distance_meters)
	if clamped >= total_length:
		return points[-1]
	var segment_index := _find_segment(clamped)
	var start_distance := cumulative_distances[segment_index]
	var end_distance := cumulative_distances[segment_index + 1]
	var segment_length := end_distance - start_distance
	if segment_length <= POINT_EPSILON_METERS:
		return points[segment_index]
	return points[segment_index].lerp(
		points[segment_index + 1],
		(clamped - start_distance) / segment_length
	)


func sample_direction(distance_meters: float, look_ahead_meters: float) -> Vector3:
	if not _is_valid:
		return _last_valid_forward
	var current_distance := clamp_distance(distance_meters)
	var ahead_distance := clamp_distance(current_distance + maxf(look_ahead_meters, POINT_EPSILON_METERS))
	var direction := sample_position(ahead_distance) - sample_position(current_distance)
	if direction.length_squared() <= POINT_EPSILON_METERS * POINT_EPSILON_METERS:
		var behind_distance := clamp_distance(current_distance - maxf(look_ahead_meters, POINT_EPSILON_METERS))
		direction = sample_position(current_distance) - sample_position(behind_distance)
	if direction.length_squared() <= POINT_EPSILON_METERS * POINT_EPSILON_METERS:
		return _last_valid_forward
	_last_valid_forward = direction.normalized()
	return _last_valid_forward


func _find_segment(distance_meters: float) -> int:
	## Find the largest cumulative distance not greater than the sample distance.
	var low := 0
	var high := cumulative_distances.size() - 2
	while low <= high:
		var middle := (low + high) / 2
		if cumulative_distances[middle + 1] <= distance_meters:
			low = middle + 1
		else:
			high = middle - 1
	return clampi(low, 0, cumulative_distances.size() - 2)


static func _extract_point(value: Variant) -> Vector3:
	if value is Vector3:
		return value
	if value is Dictionary and value.has("position") and value["position"] is Vector3:
		return value["position"]
	return Vector3(NAN, NAN, NAN)


static func _is_finite_vector(value: Vector3) -> bool:
	return is_finite(value.x) and is_finite(value.y) and is_finite(value.z)
