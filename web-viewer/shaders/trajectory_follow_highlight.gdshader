shader_type spatial;
render_mode unshaded, cull_disabled, depth_prepass_alpha;

uniform int trajectory_render_mode;
uniform float current_route_distance;
uniform vec4 base_trajectory_color : source_color;
uniform vec4 highlight_color : source_color;
uniform float behind_fade_length;
uniform float highlight_length;
uniform float highlight_transition_length;
uniform float normal_preview_length;
uniform float forward_fade_length;

void fragment() {
	vec4 display_color = base_trajectory_color;
	float visibility_alpha = 1.0;

	if (trajectory_render_mode == 1) {
		// UV2.x stores meters along RoutePath. It is independent of world-space
		// proximity, so close loops cannot affect which route section is visible.
		float relative_distance = UV2.x - current_route_distance;
		visibility_alpha = smoothstep(-behind_fade_length, 0.0, relative_distance);

		if (relative_distance >= 0.0 && relative_distance <= highlight_length) {
			display_color = highlight_color;
		} else if (relative_distance > highlight_length) {
			float transition_amount = smoothstep(
				highlight_length,
				highlight_length + highlight_transition_length,
				relative_distance
			);
			display_color = mix(highlight_color, base_trajectory_color, transition_amount);
		}

		float forward_fade_start = highlight_length + highlight_transition_length + normal_preview_length;
		float forward_fade_end = forward_fade_start + forward_fade_length;
		visibility_alpha *= 1.0 - smoothstep(
			forward_fade_start,
			forward_fade_end,
			relative_distance
		);
	}

	ALBEDO = display_color.rgb;
	ALPHA = display_color.a * visibility_alpha;
}
