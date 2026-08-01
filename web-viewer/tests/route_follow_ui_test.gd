extends SceneTree

var _failures: Array[String] = []


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	var packed := load("res://scenes/main.tscn") as PackedScene
	var detached_scene := packed.instantiate()
	var ui_owner := detached_scene.get_node("UI")
	var follow_ui: RouteFollowUI = detached_scene.get_node("UI/RouteFollowUI")
	ui_owner.remove_child(follow_ui)
	follow_ui.owner = null
	detached_scene.queue_free()
	get_root().add_child(follow_ui)
	await process_frame

	follow_ui.set_route_available(true)
	await _resize_to(follow_ui, Vector2(1280.0, 720.0))
	var desktop_entry: Button = follow_ui.get_node("DesktopEntryButton")
	_expect(desktop_entry.visible, "wide desktop uses the desktop Follow entry")
	_expect_rect_inside(desktop_entry.get_rect(), Vector2(1280.0, 720.0), "desktop entry")
	_expect(is_equal_approx(desktop_entry.position.x, 16.0), "desktop entry is left aligned")

	follow_ui.set_mode(WebViewerCharacter.MODE_FOLLOW)
	var desktop_panel: PanelContainer = follow_ui.get_node("DesktopPanel")
	_expect(desktop_panel.visible, "wide desktop uses the desktop Follow panel")
	_expect_rect_inside(desktop_panel.get_rect(), Vector2(1280.0, 720.0), "desktop panel")
	_expect(is_equal_approx(desktop_panel.position.x, 16.0), "desktop panel is left aligned")
	_expect(desktop_panel.size.x <= 520.0, "desktop panel no longer spans the viewport")

	await _resize_to(follow_ui, Vector2(360.0, 640.0))
	var mobile_panel: PanelContainer = follow_ui.get_node("MobilePanel")
	_expect(mobile_panel.visible and not desktop_panel.visible, "narrow viewport uses compact Follow controls")
	_expect_rect_inside(mobile_panel.get_rect(), Vector2(360.0, 640.0), "portrait compact panel")
	_expect(is_equal_approx(mobile_panel.position.x, 16.0), "portrait compact panel is left aligned")

	follow_ui.set_mode(WebViewerCharacter.MODE_WALK)
	var mobile_entry: Button = follow_ui.get_node("MobileEntryButton")
	_expect(mobile_entry.visible, "narrow viewport uses compact Follow entry")
	_expect_rect_inside(mobile_entry.get_rect(), Vector2(360.0, 640.0), "portrait compact entry")
	_expect(is_equal_approx(mobile_entry.position.x, 16.0), "compact entry is left aligned")

	follow_ui.set_touch_controls_visible(true)
	follow_ui.set_mode(WebViewerCharacter.MODE_FOLLOW)
	await _resize_to(follow_ui, Vector2(640.0, 360.0))
	_expect(mobile_panel.visible, "touch layout uses compact Follow panel in landscape")
	_expect_rect_inside(mobile_panel.get_rect(), Vector2(640.0, 360.0), "landscape compact panel")
	_expect(mobile_panel.size.x <= 450.0, "landscape compact panel leaves the center/right view clear")

	follow_ui.queue_free()
	_finish()


func _resize_to(follow_ui: RouteFollowUI, layout_size: Vector2) -> void:
	follow_ui.set_anchors_and_offsets_preset(Control.PRESET_TOP_LEFT)
	follow_ui.size = layout_size
	follow_ui._apply_responsive_layout(layout_size)
	await process_frame
	await process_frame


func _expect_rect_inside(rect: Rect2, viewport_size: Vector2, label: String) -> void:
	_expect(rect.position.x >= 0.0 and rect.position.y >= 0.0, "%s starts inside viewport" % label)
	_expect(
		rect.end.x <= viewport_size.x + 0.01 and rect.end.y <= viewport_size.y + 0.01,
		"%s ends inside viewport (rect %s, viewport %s)" % [label, rect, viewport_size]
	)


func _expect(condition: bool, message: String) -> void:
	if not condition:
		_failures.append(message)


func _finish() -> void:
	if _failures.is_empty():
		print("RouteFollowUI layout tests passed.")
		quit(0)
		return
	for failure in _failures:
		push_error(failure)
	quit(1)
