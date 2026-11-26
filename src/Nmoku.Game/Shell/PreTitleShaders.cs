using Godot;

namespace Nmoku.Game.Shell;

/// <summary>
/// 开始界面着色器集（无 Bayer 像素抖动、无网格交错，纯净光滑的切面晶体与柔和地面焦散）：
/// 1) CrystalNShader：基于真实雕刻多切面法线的平滑晶体折光着色（5 阶高保色渐变 + 柔和折射色散 + 掠射高光）；
/// 2) CausticGroundShader：仅封面态晶体 N 下方呈现的平滑焦散光斑（变形展开棋盘后完全消隐）。
/// </summary>
internal static class PreTitleShaders
{
    public static Shader CreateCrystalNShader() => new()
    {
        Code = """
shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_opaque;

uniform vec3 col_dark : source_color = vec3(0.08, 0.16, 0.30);
uniform vec3 col_shadow_mid : source_color = vec3(0.18, 0.34, 0.58);
uniform vec3 col_mid : source_color = vec3(0.38, 0.62, 0.90);
uniform vec3 col_bright : source_color = vec3(0.76, 0.90, 1.00);
uniform vec3 col_highlight : source_color = vec3(0.98, 0.99, 1.00);

uniform vec3 fire_a : source_color = vec3(0.42, 0.88, 0.98);
uniform vec3 fire_b : source_color = vec3(0.68, 0.58, 0.98);
uniform vec3 rim_tint : source_color = vec3(0.84, 0.94, 1.00);
uniform vec3 light_dir_world = vec3(0.48, 0.78, 0.44);

uniform float hover_glow = 0.0;
uniform float alpha_fade = 1.0;
uniform float polish_sweep = 1.0;

varying vec3 v_world_normal;
varying vec3 v_world_pos;

vec3 sample_crystal_ramp(float val) {
    float v = clamp(val, 0.0, 1.0) * 4.0;
    int seg = int(floor(v));
    float f = smoothstep(0.0, 1.0, fract(v));
    if (seg <= 0) return mix(col_dark, col_shadow_mid, f);
    if (seg == 1) return mix(col_shadow_mid, col_mid, f);
    if (seg == 2) return mix(col_mid, col_bright, f);
    if (seg == 3) return mix(col_bright, col_highlight, f);
    return col_highlight;
}

float studio_env_lum(vec3 d) {
    vec3 l_key = normalize(light_dir_world);
    vec3 l_fill = normalize(vec3(-0.76, 0.38, 0.52));
    vec3 l_rim = normalize(vec3(0.15, -0.58, -0.78));
    float key_t = pow(clamp(dot(d, l_key) * 0.5 + 0.5, 0.0, 1.0), 1.35) * 0.56;
    float fill_t = clamp(dot(d, l_fill) * 0.5 + 0.5, 0.0, 1.0) * 0.26;
    float rim_t = pow(clamp(dot(d, l_rim) * 0.5 + 0.5, 0.0, 1.0), 1.8) * 0.18;
    float sky = clamp(d.y * 0.30 + 0.52, 0.20, 0.88) * 0.16;
    return clamp(key_t + fill_t + rim_t + sky, 0.08, 1.0);
}

void vertex() {
    v_world_normal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
    v_world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
    if (alpha_fade <= 0.01) discard;

    vec3 n_w = normalize(v_world_normal);
    vec3 v_w = normalize(CAMERA_POSITION_WORLD - v_world_pos);
    vec3 l_key = normalize(light_dir_world);
    vec3 l_fill = normalize(vec3(-0.72, 0.34, 0.56));
    vec3 h_key = normalize(l_key + v_w);

    // 真实切面法线受光：不同雕刻斜面随角度自然呈现明暗反差
    float ndl_key = clamp(dot(n_w, l_key) * 0.5 + 0.5, 0.0, 1.0);
    float ndl_fill = clamp(dot(n_w, l_fill) * 0.5 + 0.5, 0.0, 1.0);
    float ndv = clamp(abs(dot(n_w, v_w)), 0.0, 1.0);
    float fresnel = pow(1.0 - ndv, 2.4);

    // 晶体平滑折射 + 镜面反射采样（无临界角闪烁）
    vec3 r_dir = normalize(refract(-v_w, n_w, 1.0 / 1.56));
    vec3 refl_dir = reflect(-v_w, n_w);
    float env_lum = studio_env_lum(normalize(mix(r_dir, refl_dir, 0.38)));

    // 切面内柔和光流渐变（凸显雕塑纵深）
    float grad = clamp((v_world_pos.y + 1.8) * 0.22 + v_world_pos.x * 0.07, 0.0, 1.0);
    float shade = clamp(
        ndl_key * 0.46 + ndl_fill * 0.14 + env_lum * 0.32 + grad * 0.09 + fresnel * 0.14 + hover_glow * 0.09,
        0.02, 0.99
    );

    vec3 col = sample_crystal_ramp(shade);

    // 克制平滑的晶体色散微光（随雕刻斜面的折射方向自然变化）
    float disp_wave = sin(dot(r_dir, vec3(2.8, 2.2, -2.5)) + v_world_pos.y * 0.65);
    vec3 fire_tint = mix(fire_a, fire_b, disp_wave * 0.5 + 0.5);
    float fire_mask = smoothstep(0.28, 0.86, fresnel + (1.0 - ndl_key) * 0.22) * 0.22;
    col = mix(col, mix(col, fire_tint, 0.58), fire_mask);

    // 切面镜面亮斑与菲涅尔掠射光泽
    float spec = pow(max(dot(n_w, h_key), 0.0), 48.0);
    col = mix(col, col_highlight, smoothstep(0.18, 0.72, spec) * 0.78);
    col = mix(col, rim_tint, smoothstep(0.50, 0.95, fresnel) * (0.28 + hover_glow * 0.18));

    // 入场轻柔抛光扫光
    if (polish_sweep < 0.999) {
        float sweep_u = clamp((v_world_pos.y - v_world_pos.x * 0.35 + 2.0) / 4.0, 0.0, 1.0);
        float band = 1.0 - smoothstep(0.0, 0.15, abs(sweep_u - polish_sweep));
        col = mix(col, col_highlight, band * 0.62);
    }

    ALBEDO = clamp(col, 0.0, 1.0);
    ALPHA = clamp(alpha_fade * (0.90 + fresnel * 0.10), 0.0, 1.0);
}
""",
    };

    public static Shader CreateCausticGroundShader() => new()
    {
        Code = """
shader_type spatial;
render_mode unshaded, cull_disabled, blend_mix, depth_draw_never;

uniform vec3 shadow_color : source_color = vec3(0.05, 0.07, 0.12);
uniform vec3 lobe_left_color : source_color = vec3(0.34, 0.72, 0.98);
uniform vec3 lobe_right_color : source_color = vec3(0.58, 0.52, 0.96);
uniform vec3 inner_ring_color : source_color = vec3(0.76, 0.90, 1.00);
uniform vec3 core_color : source_color = vec3(0.96, 0.99, 1.00);

uniform float shadow_alpha = 0.24;
uniform float caustic_alpha = 0.48;
uniform float caustic_intensity = 1.0;
uniform float spin_angle = 0.0;

float organic_r(vec2 p, float angle_offset, float amp) {
    float a = atan(p.y, p.x);
    float r = length(p);
    float ripple = sin(a * 3.0 + spin_angle * 1.15 + angle_offset) * amp
                 + cos(a * 2.0 - spin_angle * 0.85 + angle_offset * 1.4) * (amp * 0.5);
    return r + ripple;
}

void fragment() {
    if (caustic_intensity <= 0.008) discard;

    vec2 uv = (UV - vec2(0.5)) * 2.0;
    uv /= vec2(1.04, 0.82);

    float base_r = length(uv);
    if (base_r > 1.0) discard;

    float outer_mask = 1.0 - smoothstep(0.58, 1.0, base_r);
    vec3 col = shadow_color;
    float alpha = shadow_alpha * outer_mask;

    vec2 cuv = uv / mix(0.65, 1.0, caustic_intensity);
    vec2 shift_dir = vec2(cos(spin_angle * 0.35 + 0.25), sin(spin_angle * 0.35 + 0.25) * 0.42);
    float r_left = organic_r((cuv + shift_dir * 0.20) * vec2(1.22, 1.15), 0.3, 0.036);
    float r_right = organic_r((cuv - shift_dir * 0.20) * vec2(1.22, 1.15), 2.1, 0.036);
    float r_inner = organic_r(cuv * vec2(1.52, 1.42), 1.1, 0.044);
    float r_core = organic_r((cuv - vec2(0.015, 0.015)) * vec2(2.60, 2.30), -0.8, 0.052);

    float t_left = 1.0 - smoothstep(0.42, 0.86, r_left);
    float t_right = 1.0 - smoothstep(0.42, 0.86, r_right);
    float t_inner = 1.0 - smoothstep(0.32, 0.78, r_inner);
    float t_core = 1.0 - smoothstep(0.18, 0.66, r_core);

    col = mix(col, lobe_left_color, t_left * 0.78);
    alpha = max(alpha, t_left * caustic_alpha * 0.75);

    col = mix(col, lobe_right_color, t_right * 0.72);
    alpha = max(alpha, t_right * caustic_alpha * 0.75);

    col = mix(col, inner_ring_color, t_inner * 0.82);
    alpha = max(alpha, t_inner * caustic_alpha * 0.92);

    col = mix(col, core_color, t_core * 0.90);
    alpha = max(alpha, t_core * min(caustic_alpha * 1.38, 0.86));

    ALBEDO = clamp(col, 0.0, 1.0);
    ALPHA = clamp(alpha * caustic_intensity, 0.0, 0.86);
}
""",
    };
}
