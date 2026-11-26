using Godot;

namespace Nmoku.Game.Shell;

/// <summary>
/// 开始界面风格化晶体与焦散着色器集（借鉴 caustic-gem / motion-cube 宝石材质语言）：
/// 1) CrystalNShader：5 阶高保色明度色板 + 窄带 Bayer 4×4 有序抖动 + 内部折射微虹彩 + 等宽棱边高光；
/// 2) GemStoneShader：弧面宝石棋子（黑曜星光蓝 / 冰瓷月光石），带弧面底缘回光与星芒/高光；
/// 3) CausticGroundShader：硬边分层地面焦散盘（底影 + 双彩瓣 + 内环 + 亮核 + Bayer 收边）。
/// </summary>
internal static class PreTitleShaders
{
    public static Shader CreateCrystalNShader() => new()
    {
        Code = """
shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_opaque;

uniform vec3 col_dark : source_color = vec3(0.08, 0.14, 0.24);
uniform vec3 col_shadow_mid : source_color = vec3(0.18, 0.32, 0.52);
uniform vec3 col_mid : source_color = vec3(0.42, 0.64, 0.88);
uniform vec3 col_bright : source_color = vec3(0.80, 0.90, 1.00);
uniform vec3 col_highlight : source_color = vec3(1.00, 1.00, 1.00);

uniform vec3 fire_a : source_color = vec3(0.38, 0.92, 0.98);
uniform vec3 fire_b : source_color = vec3(0.76, 0.62, 1.00);
uniform vec3 border_color : source_color = vec3(0.92, 0.97, 1.00);
uniform vec3 light_dir_world = vec3(0.46, 0.82, 0.42);

uniform float border_width = 0.042;
uniform float hover_glow = 0.0;
uniform float alpha_fade = 1.0;
uniform float polish_sweep = 1.0;

varying vec3 v_world_normal;
varying vec3 v_world_pos;
varying vec2 v_face_uv;
varying vec2 v_face_size;

float bayer4x4(vec2 p) {
    vec2 c = mod(floor(p), 4.0);
    float x0 = mod(c.x, 2.0);
    float y0 = mod(c.y, 2.0);
    float x1 = floor(c.x * 0.5);
    float y1 = floor(c.y * 0.5);
    float d0 = abs(x0 - y0) * 2.0 + y0;
    float d1 = abs(x1 - y1) * 2.0 + y1;
    return (d0 * 4.0 + d1 + 0.5) / 16.0;
}

vec3 dither_mix(vec3 a, vec3 b, float t, float bayer) {
    return (t > bayer) ? b : a;
}

vec3 sample_crystal_ramp(float val, float bayer) {
    float v = clamp(val, 0.0, 1.0) * 4.0;
    int seg = int(floor(v));
    float f = fract(v);
    // 窄带 Bayer 4x4 抖动（仅在色阶交界 24% 过渡，保留 76% 利落干净的切面大色块）
    float dt = smoothstep(0.38, 0.62, f);
    if (seg <= 0) return dither_mix(col_dark, col_shadow_mid, dt, bayer);
    if (seg == 1) return dither_mix(col_shadow_mid, col_mid, dt, bayer);
    if (seg == 2) return dither_mix(col_mid, col_bright, dt, bayer);
    if (seg == 3) return dither_mix(col_bright, col_highlight, dt, bayer);
    return col_highlight;
}

float smooth_studio_lum(vec3 d) {
    vec3 L_key = normalize(light_dir_world);
    vec3 L_fill = normalize(vec3(-0.70, 0.52, 0.46));
    float key_t = (dot(d, L_key) * 0.5 + 0.5) * 0.54;
    float fill_t = (dot(d, L_fill) * 0.5 + 0.5) * 0.28;
    float sky = clamp(d.y * 0.25 + 0.56, 0.28, 0.86) * 0.28;
    return clamp(key_t + fill_t + sky, 0.14, 1.0);
}

void vertex() {
    v_world_normal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
    v_world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    v_face_uv = UV;
    v_face_size = UV2;
}

void fragment() {
    if (alpha_fade <= 0.01) discard;

    float bayer = bayer4x4(FRAGCOORD.xy);
    vec3 n_w = normalize(v_world_normal);
    vec3 v_w = normalize(CAMERA_POSITION_WORLD - v_world_pos);
    vec3 l_w = normalize(light_dir_world);
    vec3 h_w = normalize(l_w + v_w);

    // 晶体单次折射 + 平滑环境受光（杜绝临界角闪烁）
    vec3 r_dir = normalize(refract(-v_w, n_w, 1.0 / 1.62));
    vec3 refl_dir = reflect(-v_w, n_w);
    vec3 opt_dir = normalize(mix(r_dir, refl_dir, 0.34));
    float env_lum = smooth_studio_lum(opt_dir);

    float ndl = clamp(dot(n_w, l_w) * 0.5 + 0.5, 0.0, 1.0);
    float ndv = clamp(abs(dot(n_w, v_w)), 0.0, 1.0);
    float fresnel = pow(1.0 - ndv, 2.3);
    float spec = pow(max(dot(n_w, h_w), 0.0), 42.0);

    // 切面斜向微渐变（凸显雕塑切面的光流与纵深）
    float facet_grad = clamp((v_world_pos.y + 1.8) * 0.24 + (v_world_pos.x * 0.08), 0.0, 1.0);
    float shade_val = clamp(
        ndl * 0.42 + env_lum * 0.42 + facet_grad * 0.12 + fresnel * 0.16 + hover_glow * 0.10,
        0.04, 0.98
    );

    vec3 col = sample_crystal_ramp(shade_val, bayer);

    // 克制平滑的内部折射虹彩火彩（沿折射角与掠射缘柔和浮现）
    float disp_wave = sin(dot(r_dir, vec3(3.1, 2.4, -2.7)) + v_world_pos.y * 0.9);
    vec3 fire_tint = mix(fire_a, fire_b, disp_wave * 0.5 + 0.5);
    float fire_mask = smoothstep(0.25, 0.78, fresnel + (1.0 - ndl) * 0.25) * 0.32;
    if (fire_mask > 0.04) {
        col = dither_mix(col, mix(col, fire_tint, 0.65), fire_mask, bayer);
    }

    // 镜面亮斑
    if (spec > 0.28) {
        col = dither_mix(col, col_highlight, smoothstep(0.28, 0.72, spec) * 0.85, bayer);
    }

    // 等宽物理距离棱边高光（来自 UV + UV2 的精确面尺寸）
    float dx = min(v_face_uv.x, 1.0 - v_face_uv.x) * max(v_face_size.x, 0.05);
    float dy = min(v_face_uv.y, 1.0 - v_face_uv.y) * max(v_face_size.y, 0.05);
    float edge_dist = min(dx, dy);
    if (edge_dist < border_width) {
        float edge_t = 1.0 - smoothstep(border_width * 0.35, border_width, edge_dist);
        col = mix(col, border_color, edge_t * (0.68 + hover_glow * 0.22));
    }

    // 入场/悬停轻柔抛光扫光
    if (polish_sweep < 0.999) {
        float sweep_u = clamp((v_world_pos.y - v_world_pos.x * 0.35 + 2.0) / 4.0, 0.0, 1.0);
        float band = 1.0 - smoothstep(0.0, 0.14, abs(sweep_u - polish_sweep));
        col = mix(col, col_highlight, band * 0.75);
    }

    ALBEDO = clamp(col, 0.0, 1.0);
    ALPHA = clamp(alpha_fade * (0.86 + fresnel * 0.14), 0.0, 1.0);
}
""",
    };

    public static Shader CreateGemStoneShader() => new()
    {
        Code = """
shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_opaque;

uniform vec3 col_dark : source_color = vec3(0.05, 0.07, 0.14);
uniform vec3 col_shadow_mid : source_color = vec3(0.11, 0.16, 0.28);
uniform vec3 col_mid : source_color = vec3(0.22, 0.30, 0.46);
uniform vec3 col_bright : source_color = vec3(0.56, 0.74, 0.94);
uniform vec3 col_highlight : source_color = vec3(0.96, 0.98, 1.00);
uniform vec3 rim_accent : source_color = vec3(0.45, 0.78, 1.00);
uniform vec3 light_dir_world = vec3(0.46, 0.82, 0.42);
uniform float is_white_stone = 0.0;

varying vec3 v_world_normal;
varying vec3 v_world_pos;
varying vec3 v_local_normal;

float bayer4x4(vec2 p) {
    vec2 c = mod(floor(p), 4.0);
    float x0 = mod(c.x, 2.0);
    float y0 = mod(c.y, 2.0);
    float x1 = floor(c.x * 0.5);
    float y1 = floor(c.y * 0.5);
    float d0 = abs(x0 - y0) * 2.0 + y0;
    float d1 = abs(x1 - y1) * 2.0 + y1;
    return (d0 * 4.0 + d1 + 0.5) / 16.0;
}

vec3 dither_mix(vec3 a, vec3 b, float t, float bayer) {
    return (t > bayer) ? b : a;
}

vec3 sample_ramp(float val, float bayer) {
    float v = clamp(val, 0.0, 1.0) * 4.0;
    int seg = int(floor(v));
    float f = fract(v);
    float dt = smoothstep(0.36, 0.64, f);
    if (seg <= 0) return dither_mix(col_dark, col_shadow_mid, dt, bayer);
    if (seg == 1) return dither_mix(col_shadow_mid, col_mid, dt, bayer);
    if (seg == 2) return dither_mix(col_mid, col_bright, dt, bayer);
    if (seg == 3) return dither_mix(col_bright, col_highlight, dt, bayer);
    return col_highlight;
}

// caustic-gem 星光弧面带（Moon & Phillips 几何光学微丝星光）
float star_band(vec3 n_w, vec3 h_w, vec3 fiber_w) {
    vec3 t_perp = fiber_w - h_w * dot(fiber_w, h_w);
    float lp = length(t_perp);
    if (lp < 1e-4) return 0.0;
    t_perp /= lp;
    float dist = abs(dot(n_w, t_perp));
    float front = smoothstep(-0.05, 0.25, dot(n_w, h_w));
    return clamp(1.0 - dist, 0.0, 1.0) * front;
}

void vertex() {
    v_local_normal = NORMAL;
    v_world_normal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
    v_world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
    float bayer = bayer4x4(FRAGCOORD.xy);
    vec3 n_w = normalize(v_world_normal);
    vec3 v_w = normalize(CAMERA_POSITION_WORLD - v_world_pos);
    vec3 l_w = normalize(light_dir_world);
    vec3 h_w = normalize(l_w + v_w);

    float ndl = dot(n_w, l_w);
    float ndv = max(dot(n_w, v_w), 0.0);
    float ndh = max(dot(n_w, h_w), 0.0);
    float rim = pow(1.0 - ndv, 2.2);

    float body_val = (is_white_stone > 0.5)
        ? clamp(ndl * 0.44 + 0.52 - rim * 0.14, 0.05, 0.96)
        : clamp(ndl * 0.36 + 0.26 - ndv * 0.08, 0.02, 0.62);

    vec3 col = sample_ramp(body_val, bayer);

    // 弧面宝石下缘内部折光回亮（caustic-gem Cabochon 招牌质感）
    if (rim > 0.44 && n_w.y < 0.38) {
        float r_t = smoothstep(0.44, 0.80, rim) * (is_white_stone > 0.5 ? 0.55 : 0.72);
        col = dither_mix(col, rim_accent, r_t, bayer);
    }

    // 黑曜石微星光十字丝绒光泽 / 白月光石温润晕光
    if (is_white_stone < 0.5) {
        mat3 rot = mat3(MODEL_MATRIX);
        float b1 = star_band(n_w, h_w, normalize(rot * vec3(1.0, 0.0, 0.0)));
        float b2 = star_band(n_w, h_w, normalize(rot * vec3(0.0, 1.0, 0.0)));
        float star = max(smoothstep(0.92, 0.985, b1), smoothstep(0.92, 0.985, b2)) * pow(ndh, 1.4);
        if (star > 0.18) {
            col = dither_mix(col, col_bright, smoothstep(0.18, 0.72, star), bayer);
        }
    }

    // 弧面顶部镜面光丸
    float spec_pill = pow(ndh, 46.0);
    if (spec_pill > 0.52) {
        col = col_highlight;
    } else if (spec_pill > 0.22) {
        col = dither_mix(col, col_bright, smoothstep(0.22, 0.52, spec_pill), bayer);
    }

    ALBEDO = clamp(col, 0.0, 1.0);
    ALPHA = 1.0;
}
""",
    };

    public static Shader CreateCausticGroundShader() => new()
    {
        Code = """
shader_type spatial;
render_mode unshaded, cull_disabled, blend_mix, depth_draw_never;

uniform vec3 shadow_color : source_color = vec3(0.12, 0.15, 0.22);
uniform vec3 lobe_left_color : source_color = vec3(0.44, 0.78, 0.98);
uniform vec3 lobe_right_color : source_color = vec3(0.68, 0.56, 0.98);
uniform vec3 accent_rim_color : source_color = vec3(0.36, 0.90, 0.78);
uniform vec3 inner_ring_color : source_color = vec3(0.80, 0.92, 1.00);
uniform vec3 core_color : source_color = vec3(1.00, 1.00, 1.00);

uniform float shadow_alpha = 0.30;
uniform float caustic_alpha = 0.60;
uniform float caustic_intensity = 0.92;
uniform float spin_angle = 0.0;
uniform float squareness = 0.0;

float bayer4x4(vec2 p) {
    vec2 c = mod(floor(p), 4.0);
    float x0 = mod(c.x, 2.0);
    float y0 = mod(c.y, 2.0);
    float x1 = floor(c.x * 0.5);
    float y1 = floor(c.y * 0.5);
    float d0 = abs(x0 - y0) * 2.0 + y0;
    float d1 = abs(x1 - y1) * 2.0 + y1;
    return (d0 * 4.0 + d1 + 0.5) / 16.0;
}

float organic_r(vec2 p, float angle_offset, float amp) {
    float a = atan(p.y, p.x);
    float r = length(p);
    float ripple = sin(a * 3.0 + spin_angle * 1.4 + angle_offset) * amp
                 + cos(a * 2.0 - spin_angle * 1.1 + angle_offset * 1.5) * (amp * 0.55);
    return r + ripple;
}

void fragment() {
    float bayer = bayer4x4(FRAGCOORD.xy);
    vec2 uv = (UV - vec2(0.5)) * 2.0;
    uv /= vec2(1.06, 0.84);

    float r_ellipse = length(uv);
    float cr = cos(spin_angle * 0.5);
    float sr = sin(spin_angle * 0.5);
    vec2 ruv = vec2(uv.x * cr - uv.y * sr, uv.x * sr + uv.y * cr);
    float r_rhombus = (abs(ruv.x) + abs(ruv.y)) * 0.80;
    float base_r = mix(r_ellipse, r_rhombus, squareness);

    if (base_r > 0.98) discard;

    // 外缘窄带 Bayer 利落收边
    if (base_r > 0.84) {
        float edge_t = 1.0 - smoothstep(0.84, 0.98, base_r);
        if (edge_t <= bayer * 0.65) discard;
    }

    vec3 col = shadow_color;
    float alpha = shadow_alpha;

    if (caustic_intensity > 0.02) {
        vec2 cuv = uv / mix(0.45, 1.0, caustic_intensity);
        vec2 shift_dir = normalize(vec2(cos(spin_angle * 0.35 + 0.25), sin(spin_angle * 0.35 + 0.25) * 0.45));
        float r_left = organic_r((cuv + shift_dir * 0.22) * vec2(1.18, 1.12), 0.3, 0.042);
        float r_right = organic_r((cuv - shift_dir * 0.22) * vec2(1.18, 1.12), 2.1, 0.042);
        float r_inner = organic_r(cuv * vec2(1.48, 1.40), 1.1, 0.052);
        float r_core = organic_r((cuv - vec2(0.02, 0.02)) * vec2(2.65, 2.35), -0.8, 0.068);

        float t_left = 1.0 - smoothstep(0.64, 0.82, r_left);
        float t_right = 1.0 - smoothstep(0.64, 0.82, r_right);
        float t_inner = 1.0 - smoothstep(0.58, 0.78, r_inner);
        float t_core = 1.0 - smoothstep(0.48, 0.68, r_core);

        bool in_left = t_left > bayer;
        bool in_right = t_right > bayer;
        bool in_inner = t_inner > bayer;
        bool in_core = t_core > bayer;

        if (in_left) {
            col = lobe_left_color;
            alpha = mix(shadow_alpha, caustic_alpha, 0.86);
        }
        if (in_right) {
            col = lobe_right_color;
            alpha = mix(shadow_alpha, caustic_alpha, 0.86);
        }
        if (in_left && in_right && !in_inner) {
            col = accent_rim_color;
            alpha = caustic_alpha;
        }
        if (in_inner) {
            col = inner_ring_color;
            alpha = caustic_alpha;
        }
        if (in_core) {
            col = core_color;
            alpha = clamp(caustic_alpha * 1.48, 0.0, 0.94);
        }
    }

    ALBEDO = clamp(col, 0.0, 1.0);
    ALPHA = clamp(alpha * caustic_intensity, 0.0, 0.92);
}
""",
    };
}
