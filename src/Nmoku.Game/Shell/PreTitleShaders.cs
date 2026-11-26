using Godot;

namespace Nmoku.Game.Shell;

/// <summary>
/// 开始界面着色器：基于真实雕刻多切面法线的平滑晶体折光着色（无像素抖动、无焦散底盘）。
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
}
