class_name RuntimeGeometry
extends RefCounted

## Render-only geometry helpers shared by trajectory and marking construction.
## Exported points stay in domain X/Y; +Y maps to Godot -Z to match the C# Viewer.

const DASHED_LENGTH_METERS := 0.75
const DASHED_GAP_METERS := 0.4
const DOTTED_LENGTH_METERS := 0.08
const DOTTED_GAP_METERS := 0.24
const DOTTED_SPACING_METERS := 0.32


static func domain_to_godot(point: Dictionary, height: float = 0.0) -> Vector3:
	return Vector3(float(point["x"]), height, -float(point["y"]))


static func godot_to_domain(position: Vector3) -> Dictionary:
	return {"x": position.x, "y": -position.z}


static func sample_cubic_bezier(segment: Dictionary, subdivision_count: int = 32) -> Array:
	var points: Array = []
	for index in range(subdivision_count + 1):
		var t := float(index) / float(subdivision_count)
		var inverse := 1.0 - t
		var p0: Dictionary = segment["start"]
		var p1: Dictionary = segment["control1"]
		var p2: Dictionary = segment["control2"]
		var p3: Dictionary = segment["end"]
		points.append({
			"x": inverse * inverse * inverse * float(p0["x"]) +
				3.0 * inverse * inverse * t * float(p1["x"]) +
				3.0 * inverse * t * t * float(p2["x"]) + t * t * t * float(p3["x"]),
			"y": inverse * inverse * inverse * float(p0["y"]) +
				3.0 * inverse * inverse * t * float(p1["y"]) +
				3.0 * inverse * t * t * float(p2["y"]) + t * t * t * float(p3["y"]),
		})
	return points


static func trajectory_entry_pose(segments: Array) -> Dictionary:
	for segment in segments:
		var start: Dictionary
		var next: Dictionary
		if segment["type"] == "polyline" and segment["points"].size() >= 2:
			start = segment["points"][0]
			next = segment["points"][1]
		elif segment["type"] == "cubicBezier":
			start = segment["start"]
			next = segment["control1"]
		else:
			continue
		var direction := Vector2(
			float(next["x"]) - float(start["x"]),
			float(next["y"]) - float(start["y"])
		)
		if direction.length_squared() <= 0.00001:
			return {"ok": false}
		return {"ok": true, "start": start, "direction": direction.normalized()}
	return {"ok": false}


static func create_marking_strokes(points: Array, style: String) -> Array:
	var strokes: Array = []
	if points.size() < 2:
		return strokes
	if style == "solid":
		for index in range(points.size() - 1):
			strokes.append([points[index], points[index + 1]])
		return strokes

	var visible_length := DOTTED_LENGTH_METERS if style == "dotted" else DASHED_LENGTH_METERS
	var gap_length := DOTTED_GAP_METERS if style == "dotted" else DASHED_GAP_METERS
	var drawing := true
	var remaining := visible_length
	for index in range(points.size() - 1):
		var start := Vector2(float(points[index]["x"]), float(points[index]["y"]))
		var end := Vector2(float(points[index + 1]["x"]), float(points[index + 1]["y"]))
		var length := start.distance_to(end)
		if length <= 0.000001:
			continue
		var local := 0.0
		while local < length - 0.000001:
			var step := minf(remaining, length - local)
			var next := local + step
			if drawing and step > 0.000001:
				var a := start.lerp(end, local / length)
				var b := start.lerp(end, next / length)
				strokes.append([{"x": a.x, "y": a.y}, {"x": b.x, "y": b.y}])
			local = next
			remaining -= step
			if remaining <= 0.000001:
				drawing = not drawing
				remaining = visible_length if drawing else gap_length
	return strokes


static func create_marking_geometry(sampled: Dictionary, style: String) -> Dictionary:
	var points: Array = sampled["points"]
	var strokes: Array = []
	var dots: Array = []
	if points.size() < 2:
		return {"strokes": strokes, "dots": dots}
	if style == "solid":
		for index in range(points.size() - 1):
			strokes.append([_domain(points[index]), _domain(points[index + 1])])
		return {"strokes": strokes, "dots": dots}
	if style == "dotted":
		var distance := 0.0
		while distance <= float(sampled["totalLength"]) + 0.000001:
			dots.append(_domain(_point_at_distance(sampled, distance)))
			distance += DOTTED_SPACING_METERS
		return {"strokes": strokes, "dots": dots}

	var drawing := true
	var remaining := DASHED_LENGTH_METERS
	for index in range(points.size() - 1):
		var start: Vector2 = points[index]
		var end: Vector2 = points[index + 1]
		var length := start.distance_to(end)
		if length <= 0.000001: continue
		var local := 0.0
		while local < length - 0.000001:
			var step := minf(remaining, length - local)
			var next := local + step
			if drawing and step > 0.000001:
				strokes.append([_domain(start.lerp(end, local / length)), _domain(start.lerp(end, next / length))])
			local = next
			remaining -= step
			if remaining <= 0.000001:
				drawing = not drawing
				remaining = DASHED_LENGTH_METERS if drawing else DASHED_GAP_METERS
	return {"strokes": strokes, "dots": dots}


static func _point_at_distance(sampled: Dictionary, distance: float) -> Vector2:
	var points: Array = sampled["points"]
	var cumulative: Array = sampled["cumulativeDistances"]
	var clamped := clampf(distance, 0.0, float(sampled["totalLength"]))
	for index in range(1, cumulative.size()):
		if clamped <= float(cumulative[index]):
			var span: float = float(cumulative[index]) - float(cumulative[index - 1])
			var weight := 0.0 if span <= 0.000001 else (clamped - float(cumulative[index - 1])) / span
			return (points[index - 1] as Vector2).lerp(points[index], weight)
	return points[-1]


static func _domain(point: Vector2) -> Dictionary:
	return {"x": point.x, "y": point.y}


static func create_path_segment(
	name: String,
	start: Vector3,
	end: Vector3,
	start_normal: Vector3,
	end_normal: Vector3,
	width: float,
	material: Material
) -> MeshInstance3D:
	var direction := end - start
	var length := direction.length()
	if length <= 0.000001:
		return null
	var forward := direction / length
	var normal := (start_normal + end_normal).normalized()
	if normal.length_squared() <= 0.00001 or absf(normal.dot(forward)) > 0.98:
		normal = Vector3.UP
	var right := normal.cross(forward).normalized()
	var adjusted_normal := forward.cross(right).normalized()
	var box := BoxMesh.new()
	box.size = Vector3(width, 0.008, length)
	box.material = material
	var mesh := MeshInstance3D.new()
	mesh.name = name
	mesh.mesh = box
	mesh.position = (start + end) * 0.5
	mesh.basis = Basis(right, adjusted_normal, forward)
	mesh.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	return mesh


static func create_projected_ribbon(
	name: String,
	strokes: Array,
	width: float,
	material: Material
) -> MeshInstance3D:
	var surface := SurfaceTool.new()
	surface.begin(Mesh.PRIMITIVE_TRIANGLES)
	var triangle_count := 0
	var half_width := width * 0.5
	for points in strokes:
		for index in range(points.size() - 1):
			var start: Vector3 = points[index]["position"]
			var end: Vector3 = points[index + 1]["position"]
			var direction := end - start
			var length := direction.length()
			if length <= 0.000001: continue
			var forward := direction / length
			var normal: Vector3 = (points[index]["normal"] + points[index + 1]["normal"]).normalized()
			if normal.length_squared() <= 0.00001 or absf(normal.dot(forward)) > 0.98:
				normal = Vector3.UP
			var right := normal.cross(forward).normalized() * half_width
			var adjusted_normal := forward.cross(right.normalized()).normalized()
			_add_ribbon_triangle(surface, adjusted_normal, start - right, start + right, end + right)
			_add_ribbon_triangle(surface, adjusted_normal, start - right, end + right, end - right)
			triangle_count += 2
	if triangle_count == 0: return null
	var instance := MeshInstance3D.new()
	instance.name = name
	instance.mesh = surface.commit()
	instance.material_override = material
	instance.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	return instance


static func _add_ribbon_triangle(surface: SurfaceTool, normal: Vector3, a: Vector3, b: Vector3, c: Vector3) -> void:
	for vertex in [a, b, c]:
		surface.set_normal(normal)
		surface.add_vertex(vertex)


static func color_from_value(value: String, fallback: Color = Color.WHITE) -> Color:
	var named := {
		"red": "#F21F14", "blue": "#1452FF", "yellow": "#FFD10D",
		"green": "#1AD938", "orange": "#FF6B14", "white": "#FFFFFF",
	}
	var normalized := value.strip_edges()
	if named.has(normalized.to_lower()):
		normalized = named[normalized.to_lower()]
	if normalized.is_valid_html_color():
		return Color.from_string(normalized, fallback)
	return fallback
