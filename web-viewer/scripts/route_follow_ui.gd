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

var _controller: RouteFollowController
var _mode := WebViewerCharacter.MODE_WALK
var _touch_controls_visible := false
var _route_available := false

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
	_desktop_entry.visible = not following and not _touch_controls_visible
	_mobile_entry.visible = not following and _touch_controls_visible
	_desktop_panel.visible = following and not _touch_controls_visible
	_mobile_panel.visible = following and _touch_controls_visible


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


func _apply_responsive_layout() -> void:
	if not is_instance_valid(_mobile_panel):
		return
	var portrait := get_viewport_rect().size.y > get_viewport_rect().size.x
	var primary_buttons := $MobilePanel/Margin/Layout/Primary.get_children()
	if portrait:
		for button in primary_buttons:
			button.custom_minimum_size = Vector2(56.0, 56.0)
		$MobilePanel/Margin/Layout/Secondary/SpeedDown.custom_minimum_size = Vector2(56.0, 56.0)
		$MobilePanel/Margin/Layout/Secondary/Speed.custom_minimum_size = Vector2(72.0, 56.0)
		$MobilePanel/Margin/Layout/Secondary/SpeedUp.custom_minimum_size = Vector2(56.0, 56.0)
		$MobilePanel/Margin/Layout/Secondary/LookForward.custom_minimum_size = Vector2(94.0, 56.0)
	else:
		var primary_widths := [68.0, 76.0, 68.0, 84.0, 68.0]
		for index in range(primary_buttons.size()):
			primary_buttons[index].custom_minimum_size = Vector2(primary_widths[index], 54.0)
		$MobilePanel/Margin/Layout/Secondary/SpeedDown.custom_minimum_size = Vector2(82.0, 50.0)
		$MobilePanel/Margin/Layout/Secondary/Speed.custom_minimum_size = Vector2(112.0, 50.0)
		$MobilePanel/Margin/Layout/Secondary/SpeedUp.custom_minimum_size = Vector2(82.0, 50.0)
		$MobilePanel/Margin/Layout/Secondary/LookForward.custom_minimum_size = Vector2(116.0, 50.0)
	_mobile_panel.anchor_left = 0.02 if portrait else 0.12
	_mobile_panel.anchor_right = 0.98 if portrait else 0.88
	_mobile_panel.offset_left = 0.0
	_mobile_panel.offset_right = 0.0
	_mobile_panel.offset_top = -252.0 if portrait else -194.0
	_mobile_panel.offset_bottom = -12.0
