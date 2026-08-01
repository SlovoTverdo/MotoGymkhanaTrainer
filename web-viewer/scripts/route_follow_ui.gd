class_name RouteFollowUI
extends Control

signal follow_requested
signal exit_requested
signal restart_requested
signal step_backward_requested
signal play_pause_requested
signal step_forward_requested
signal speed_down_requested
signal speed_up_requested
signal look_forward_requested

const SAFE_MARGIN := 16.0
const COMPACT_BREAKPOINT := 560.0
const DESKTOP_PANEL_WIDTH := 520.0
const DESKTOP_PANEL_HEIGHT := 164.0
const COMPACT_PANEL_WIDTH := 450.0
const COMPACT_PANEL_HEIGHT := 194.0
const PORTRAIT_PANEL_HEIGHT := 292.0

var _controller: RouteFollowController
var _mode := WebViewerCharacter.MODE_WALK
var _touch_controls_visible := false
var _route_available := false
var _compact_layout := false

@onready var _desktop_entry: Button = $DesktopEntryButton
@onready var _mobile_entry: Button = $MobileEntryButton
@onready var _desktop_panel: PanelContainer = $DesktopPanel
@onready var _mobile_panel: PanelContainer = $MobilePanel
@onready var _desktop_play: Button = $DesktopPanel/Margin/Layout/Actions/PlayPause
@onready var _mobile_play: Button = $MobilePanel/Margin/Layout/Primary/PlayPause
@onready var _desktop_speed: Label = $DesktopPanel/Margin/Layout/Status/Speed
@onready var _mobile_speed: Label = $MobilePanel/Margin/Layout/Secondary/Speed
@onready var _desktop_progress: Label = $DesktopPanel/Margin/Layout/Status/Progress
@onready var _mobile_progress: Label = $MobilePanel/Margin/Layout/Progress
@onready var _desktop_finished: Label = $DesktopPanel/Margin/Layout/Status/Finished
@onready var _mobile_finished: Label = $MobilePanel/Margin/Layout/Finished
@onready var _unavailable_label: Label = $UnavailableLabel


func _ready() -> void:
	mouse_filter = Control.MOUSE_FILTER_IGNORE
	_connect_button(_desktop_entry, follow_requested)
	_connect_button(_mobile_entry, follow_requested)
	_connect_button($DesktopPanel/Margin/Layout/Actions/Exit, exit_requested)
	_connect_button($DesktopPanel/Margin/Layout/Actions/Restart, restart_requested)
	_connect_button($DesktopPanel/Margin/Layout/Actions/StepBack, step_backward_requested)
	_connect_button(_desktop_play, play_pause_requested)
	_connect_button($DesktopPanel/Margin/Layout/Actions/StepForward, step_forward_requested)
	_connect_button($DesktopPanel/Margin/Layout/Actions/SpeedDown, speed_down_requested)
	_connect_button($DesktopPanel/Margin/Layout/Actions/SpeedUp, speed_up_requested)
	_connect_button($DesktopPanel/Margin/Layout/Actions/LookForward, look_forward_requested)
	_connect_button($MobilePanel/Margin/Layout/Primary/Exit, exit_requested)
	_connect_button($MobilePanel/Margin/Layout/Primary/Restart, restart_requested)
	_connect_button($MobilePanel/Margin/Layout/Primary/StepBack, step_backward_requested)
	_connect_button(_mobile_play, play_pause_requested)
	_connect_button($MobilePanel/Margin/Layout/Primary/StepForward, step_forward_requested)
	_connect_button($MobilePanel/Margin/Layout/Secondary/SpeedDown, speed_down_requested)
	_connect_button($MobilePanel/Margin/Layout/Secondary/SpeedUp, speed_up_requested)
	_connect_button($MobilePanel/Margin/Layout/Secondary/LookForward, look_forward_requested)
	get_viewport().size_changed.connect(_apply_responsive_layout)
	_apply_responsive_layout()
	_update_visibility()


func configure(controller: RouteFollowController) -> void:
	_controller = controller
	if not _controller.state_changed.is_connected(_update_state):
		_controller.state_changed.connect(_update_state)
	_update_state()


func set_route_available(available: bool, reason: String = "") -> void:
	_route_available = available
	for button in [_desktop_entry, _mobile_entry]:
		button.disabled = not available
		button.tooltip_text = "" if available else reason
		button.text = "Следовать по трассе" if available else "Follow недоступен"
	_unavailable_label.text = "Follow недоступен: %s" % reason
	_unavailable_label.visible = not available
	_update_visibility()


func set_mode(mode: String) -> void:
	_mode = mode
	_update_visibility()
	_update_state()


func set_touch_controls_visible(show_controls: bool) -> void:
	_touch_controls_visible = show_controls
	_update_visibility()


func _connect_button(button: Button, semantic_signal: Signal) -> void:
	# A pointer/touch route control must not retain keyboard focus. Otherwise
	# Space activates the last clicked button instead of Follow Play/Pause.
	button.focus_mode = Control.FOCUS_NONE
	button.pressed.connect(func() -> void: semantic_signal.emit())


func _update_visibility() -> void:
	if not is_instance_valid(_desktop_entry):
		return
	var following := _mode == WebViewerCharacter.MODE_FOLLOW
	var use_compact_controls := _touch_controls_visible or _compact_layout
	_desktop_entry.visible = not following and not use_compact_controls
	_mobile_entry.visible = not following and use_compact_controls
	_desktop_panel.visible = following and not use_compact_controls
	_mobile_panel.visible = following and use_compact_controls


func _update_state() -> void:
	if _controller == null or not is_instance_valid(_desktop_play):
		return
	var play_text := "Pause" if _controller.is_playing else "Play"
	_desktop_play.text = play_text
	_mobile_play.text = play_text
	var speed_text := "Speed %.2fx" % _controller.speed_multiplier
	_desktop_speed.text = speed_text
	_mobile_speed.text = speed_text
	var percent := _controller.progress_ratio() * 100.0
	var progress_text := "%.1f / %.1f m  (%.0f%%)" % [
		_controller.route_distance_meters, _controller.total_length, percent
	]
	_desktop_progress.text = progress_text
	_mobile_progress.text = progress_text
	_desktop_finished.visible = _controller.route_finished
	_mobile_finished.visible = _controller.route_finished


func _apply_responsive_layout(layout_size := Vector2.ZERO) -> void:
	if not is_instance_valid(_mobile_panel):
		return
	var viewport_size := get_viewport_rect().size if layout_size.is_zero_approx() else layout_size
	var portrait := viewport_size.y > viewport_size.x
	_compact_layout = viewport_size.x < COMPACT_BREAKPOINT
	var primary_buttons := $MobilePanel/Margin/Layout/Primary.get_children()
	var very_narrow := viewport_size.x < 350.0
	$MobilePanel/Margin/Layout/Primary.columns = 3 if portrait else 5
	$MobilePanel/Margin/Layout/Secondary.columns = 2 if portrait else 4
	if portrait and very_narrow:
		for button in primary_buttons:
			button.custom_minimum_size = Vector2(48.0, 52.0)
		$MobilePanel/Margin/Layout/Primary.add_theme_constant_override("separation", 4)
		$MobilePanel/Margin/Layout/Secondary.add_theme_constant_override("separation", 4)
		$MobilePanel/Margin/Layout/Secondary/SpeedDown.custom_minimum_size = Vector2(48.0, 52.0)
		$MobilePanel/Margin/Layout/Secondary/Speed.custom_minimum_size = Vector2(60.0, 52.0)
		$MobilePanel/Margin/Layout/Secondary/SpeedUp.custom_minimum_size = Vector2(48.0, 52.0)
		$MobilePanel/Margin/Layout/Secondary/LookForward.custom_minimum_size = Vector2(84.0, 52.0)
	elif portrait:
		for button in primary_buttons:
			button.custom_minimum_size = Vector2(56.0, 56.0)
		$MobilePanel/Margin/Layout/Primary.add_theme_constant_override("separation", 6)
		$MobilePanel/Margin/Layout/Secondary.add_theme_constant_override("separation", 6)
		$MobilePanel/Margin/Layout/Secondary/SpeedDown.custom_minimum_size = Vector2(56.0, 56.0)
		$MobilePanel/Margin/Layout/Secondary/Speed.custom_minimum_size = Vector2(72.0, 56.0)
		$MobilePanel/Margin/Layout/Secondary/SpeedUp.custom_minimum_size = Vector2(56.0, 56.0)
		$MobilePanel/Margin/Layout/Secondary/LookForward.custom_minimum_size = Vector2(94.0, 56.0)
	else:
		var primary_widths := [68.0, 76.0, 68.0, 84.0, 68.0]
		for index in range(primary_buttons.size()):
			primary_buttons[index].custom_minimum_size = Vector2(primary_widths[index], 54.0)
		$MobilePanel/Margin/Layout/Primary.add_theme_constant_override("separation", 6)
		$MobilePanel/Margin/Layout/Secondary.add_theme_constant_override("separation", 6)
		$MobilePanel/Margin/Layout/Secondary/SpeedDown.custom_minimum_size = Vector2(82.0, 50.0)
		$MobilePanel/Margin/Layout/Secondary/Speed.custom_minimum_size = Vector2(112.0, 50.0)
		$MobilePanel/Margin/Layout/Secondary/SpeedUp.custom_minimum_size = Vector2(82.0, 50.0)
		$MobilePanel/Margin/Layout/Secondary/LookForward.custom_minimum_size = Vector2(116.0, 50.0)
	# Follow UI deliberately occupies the lower-left corner instead of the
	# trajectory's central vanishing area. Portrait uses all available width;
	# landscape keeps a bounded panel so most of the route remains unobscured.
	_set_bottom_left_rect(_desktop_entry, 204.0, 48.0)
	_set_bottom_left_rect(_mobile_entry, 224.0, 58.0)
	_set_bottom_left_rect(_desktop_panel, DESKTOP_PANEL_WIDTH, DESKTOP_PANEL_HEIGHT)
	if portrait or viewport_size.x < COMPACT_PANEL_WIDTH + SAFE_MARGIN * 2.0:
		_mobile_panel.anchor_left = 0.0
		_mobile_panel.anchor_right = 1.0
		_mobile_panel.offset_left = SAFE_MARGIN
		_mobile_panel.offset_right = -SAFE_MARGIN
		_mobile_panel.offset_top = -SAFE_MARGIN - PORTRAIT_PANEL_HEIGHT
		_mobile_panel.offset_bottom = -SAFE_MARGIN
	else:
		_set_bottom_left_rect(_mobile_panel, COMPACT_PANEL_WIDTH, COMPACT_PANEL_HEIGHT)
	_unavailable_label.anchor_left = 0.0
	_unavailable_label.anchor_right = 0.0
	_unavailable_label.anchor_top = 1.0
	_unavailable_label.anchor_bottom = 1.0
	_unavailable_label.offset_left = SAFE_MARGIN
	_unavailable_label.offset_right = SAFE_MARGIN + COMPACT_PANEL_WIDTH
	_unavailable_label.offset_top = -148.0
	_unavailable_label.offset_bottom = -84.0
	_unavailable_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_LEFT
	_update_visibility()


func _set_bottom_left_rect(control: Control, width: float, height: float) -> void:
	control.anchor_left = 0.0
	control.anchor_top = 1.0
	control.anchor_right = 0.0
	control.anchor_bottom = 1.0
	control.offset_left = SAFE_MARGIN
	control.offset_top = -SAFE_MARGIN - height
	control.offset_right = SAFE_MARGIN + width
	control.offset_bottom = -SAFE_MARGIN
