# 生成 Nmoku 游戏图标（参照原 TS 3×3×3 立体棋盘 + 跨层红柱 + 蓝/黑绿/白红辅助线，
# 适配亮色水绿主题、圆角矩形外框、3D 渲染微光质感，并由棋盘左右纵边与空间对角连线勾勒出立体 "N"）。
# 产物：
#   assets-src/icon/icon-master.png       —— 512px 主图（源，入库）
#   src/Nmoku.Game/icon.png               —— 256px（project.godot config/icon 用）
#   src/Nmoku.Game/assets/icon/nmoku.ico  —— 多尺寸 ICO（Windows 应用图标）
#   assets-src/icon/nmoku.ico             —— 同源归档副本
# 用法：python tools/make-icon.py
import math
from PIL import Image, ImageDraw, ImageFilter

S = 2048  # 4× 超采样画布，最终 LANCZOS 下采样至 512 / 256 / ICO 各尺寸
OUT_MASTER = 512

# 3/4 透视相机：(-1,-1,+1)->(-1,+1,+1)->(0,0,0)->(+1,-1,-1)->(+1,+1,-1) 构成平衡对称的 3D 立体 N
YAW_DEG = 32.0
PITCH_DEG = 17.0
CAM_DIST = 8.6
SCALE = S * 0.228
CX = S * 0.520
CY = S * 0.502


def _camera_basis():
    yaw = math.radians(YAW_DEG)
    pitch = math.radians(PITCH_DEG)
    cam_pos = (
        CAM_DIST * math.cos(pitch) * math.sin(yaw),
        CAM_DIST * math.sin(pitch),
        CAM_DIST * math.cos(pitch) * math.cos(yaw),
    )
    flen = math.sqrt(sum(c * c for c in cam_pos))
    fwd = tuple(-c / flen for c in cam_pos)
    rx, rz = -fwd[2], fwd[0]
    rlen = math.hypot(rx, rz)
    right = (rx / rlen, 0.0, rz / rlen)
    up = (
        right[1] * fwd[2] - right[2] * fwd[1],
        right[2] * fwd[0] - right[0] * fwd[2],
        right[0] * fwd[1] - right[1] * fwd[0],
    )
    return cam_pos, right, up, fwd


CAM_POS, CAM_R, CAM_U, CAM_F = _camera_basis()


def project(x: float, y: float, z: float):
    dx = x - CAM_POS[0]
    dy = y - CAM_POS[1]
    dz = z - CAM_POS[2]
    vx = dx * CAM_R[0] + dy * CAM_R[1] + dz * CAM_R[2]
    vy = dx * CAM_U[0] + dy * CAM_U[1] + dz * CAM_U[2]
    vz = dx * CAM_F[0] + dy * CAM_F[1] + dz * CAM_F[2]
    persp = CAM_DIST / max(1.0, vz)
    return CX + vx * persp * SCALE, CY - vy * persp * SCALE


def draw_capsule(layer: Image.Image, p0, p1, width: float, color):
    """在独立 RGBA 层上正确 alpha 合成圆头抗锯齿线段。"""
    tmp = Image.new("RGBA", layer.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(tmp)
    w = max(1, int(round(width)))
    d.line([p0, p1], fill=color, width=w)
    r = width * 0.5
    for px, py in (p0, p1):
        d.ellipse([px - r, py - r, px + r, py + r], fill=color)
    layer.alpha_composite(tmp)


def draw_crystal_bar(layer: Image.Image, glow_layer: Image.Image, p0, p1, width: float,
                     lit_col, shadow_col, rim_col, glow_col):
    """
    平滑 3D 双切面晶体棱条（无锯齿/无圆点波纹）：
    沿笔画中脊分成受光半面（亮青碧）与背光半面（深青墨），边缘带细微晶莹棱光与柔和环境光晕。
    """
    dx = p1[0] - p0[0]
    dy = p1[1] - p0[1]
    length = math.hypot(dx, dy) or 1.0
    # 垂直法向：指向左上方（光源方向）
    nx, ny = -dy / length, dx / length
    if nx + ny > 0:
        nx, ny = -nx, -ny
    hw = width * 0.5

    # 1) 外微光层
    draw_capsule(glow_layer, p0, p1, width * 1.55, glow_col)

    # 2) 柔和托底投影（3D 悬浮感）
    sh = Image.new("RGBA", layer.size, (0, 0, 0, 0))
    sd = ImageDraw.Draw(sh)
    off_x, off_y = width * 0.14, width * 0.22
    sd.line([(p0[0] + off_x, p0[1] + off_y), (p1[0] + off_x, p1[1] + off_y)],
            fill=(16, 48, 56, 72), width=int(round(width * 1.08)))
    sh = sh.filter(ImageFilter.GaussianBlur(radius=width * 0.28))
    layer.alpha_composite(sh)

    # 3) 双切面多边形（上半受光面 + 下半背光面 + 两端微斜切圆帽）
    bar = Image.new("RGBA", layer.size, (0, 0, 0, 0))
    bd = ImageDraw.Draw(bar)

    # 端点轻微内缩并配半圆帽，确保 N 折角处顺滑严密
    r = hw
    for px, py in (p0, p1):
        bd.ellipse([px - r, py - r, px + r, py + r], fill=shadow_col)
        bd.ellipse([px - r * 0.92, py - r * 0.92, px + r * 0.92, py + r * 0.92], fill=lit_col)

    lit_poly = [
        p0, p1,
        (p1[0] + nx * hw, p1[1] + ny * hw),
        (p0[0] + nx * hw, p0[1] + ny * hw),
    ]
    shd_poly = [
        p0, p1,
        (p1[0] - nx * hw, p1[1] - ny * hw),
        (p0[0] - nx * hw, p0[1] - ny * hw),
    ]
    bd.polygon(shd_poly, fill=shadow_col)
    bd.polygon(lit_poly, fill=lit_col)

    # 中脊极细晶莹折光线
    bd.line([p0, p1], fill=rim_col, width=max(2, int(round(width * 0.14))))
    # 外缘微弱暗边定廓
    bd.line([(p0[0] - nx * hw, p0[1] - ny * hw), (p1[0] - nx * hw, p1[1] - ny * hw)],
            fill=(14, 52, 62, 165), width=max(2, int(round(width * 0.08))))
    bd.line([(p0[0] + nx * hw, p0[1] + ny * hw), (p1[0] + nx * hw, p1[1] + ny * hw)],
            fill=(190, 244, 246, 175), width=max(2, int(round(width * 0.07))))

    layer.alpha_composite(bar)


def render_3d_sphere_patch(radius: float, albedo, ambient, spec_col, spec_pow: float,
                           rim_col, rim_strength: float, border_col,
                           dot_col=None) -> Image.Image:
    """逐像素真实 3D 球面光照渲染（Lambert + 柔和环境回光 + Phong 高光 + Fresnel 轮廓光）。"""
    r = int(round(radius))
    pad = int(round(radius * 0.45))
    size = 2 * (r + pad)
    cx = cy = r + pad

    # 1) 柔和投影层
    patch = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    sh = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    sd = ImageDraw.Draw(sh)
    sr = radius * 1.02
    ox, oy = radius * 0.12, radius * 0.22
    sd.ellipse([cx - sr + ox, cy - sr + oy, cx + sr + ox, cy + sr + oy], fill=(24, 56, 66, 62))
    sh = sh.filter(ImageFilter.GaussianBlur(radius=radius * 0.18))
    patch.alpha_composite(sh)

    # 2) 逐像素 3D 球面着色（仅计算球体包围盒内像素，快速且完全光滑零色带）
    sphere = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    px = sphere.load()
    # 主光（左上前）+ 辅光（右下底回光）
    lx, ly, lz = -0.46, -0.52, 0.72
    ll = math.sqrt(lx * lx + ly * ly + lz * lz)
    lx, ly, lz = lx / ll, ly / ll, lz / ll
    # 半角向量 H = normalize(L + V), V = (0,0,1)
    hx, hy, hz = lx, ly, lz + 1.0
    hl = math.sqrt(hx * hx + hy * hy + hz * hz)
    hx, hy, hz = hx / hl, hy / hl, hz / hl

    fx, fy, fz = 0.55, 0.62, 0.56
    fl = math.sqrt(fx * fx + fy * fy + fz * fz)
    fx, fy, fz = fx / fl, fy / fl, fz / fl

    inv_r = 1.0 / radius
    x0 = max(0, cx - r - 2)
    x1 = min(size, cx + r + 3)
    y0 = max(0, cy - r - 2)
    y1 = min(size, cy + r + 3)

    for y in range(y0, y1):
        ny = (y - cy) * inv_r
        ny2 = ny * ny
        for x in range(x0, x1):
            nx = (x - cx) * inv_r
            r2 = nx * nx + ny2
            if r2 > 1.03:
                continue
            dist = math.sqrt(r2)
            # 边缘 1.5px 平滑抗锯齿
            aa = max(0.0, min(1.0, (1.0 - dist) * radius * 0.85 + 0.5))
            if aa <= 0.0:
                continue
            nz = math.sqrt(max(0.0, 1.0 - min(1.0, r2)))

            ndl = max(0.0, nx * lx + ny * ly + nz * lz)
            ndf = max(0.0, nx * fx + ny * fy + nz * fz)
            ndh = max(0.0, nx * hx + ny * hy + nz * hz)
            fresnel = (1.0 - nz) ** 2.6

            # 柔和漫反射 + 底部回光
            diff = 0.42 + 0.58 * (ndl ** 0.85) + 0.16 * ndf
            spec = (ndh ** spec_pow) * 0.82
            rim = fresnel * rim_strength

            cr = albedo[0] * diff + ambient[0] * (1.0 - ndl) * 0.25 + spec_col[0] * spec + rim_col[0] * rim
            cg = albedo[1] * diff + ambient[1] * (1.0 - ndl) * 0.25 + spec_col[1] * spec + rim_col[1] * rim
            cb = albedo[2] * diff + ambient[2] * (1.0 - ndl) * 0.25 + spec_col[2] * spec + rim_col[2] * rim

            # 极细外轮廓压边（防止浅色棋子融底）
            if dist > 0.935:
                bt = min(1.0, (dist - 0.935) / 0.065) * 0.72
                cr = cr * (1.0 - bt) + border_col[0] * bt
                cg = cg * (1.0 - bt) + border_col[1] * bt
                cb = cb * (1.0 - bt) + border_col[2] * bt

            px[x, y] = (
                max(0, min(255, int(round(cr)))),
                max(0, min(255, int(round(cg)))),
                max(0, min(255, int(round(cb)))),
                int(round(aa * 255)),
            )

    patch.alpha_composite(sphere)

    # 3) 辅助线标志圆点（绿 / 红）
    if dot_col is not None:
        dr = radius * 0.34
        dg = Image.new("RGBA", (size, size), (0, 0, 0, 0))
        ImageDraw.Draw(dg).ellipse([cx - dr * 1.4, cy - dr * 1.4, cx + dr * 1.4, cy + dr * 1.4],
                                   fill=(dot_col[0], dot_col[1], dot_col[2], 120))
        dg = dg.filter(ImageFilter.GaussianBlur(radius=dr * 0.35))
        patch.alpha_composite(dg)

        dot_layer = Image.new("RGBA", (size, size), (0, 0, 0, 0))
        dd = ImageDraw.Draw(dot_layer)
        dd.ellipse([cx - dr, cy - dr, cx + dr, cy + dr], fill=dot_col + (255,))
        # 圆点内微弱高光
        hr = dr * 0.38
        dd.ellipse([cx - dr * 0.28 - hr, cy - dr * 0.28 - hr,
                    cx - dr * 0.28 + hr, cy - dr * 0.28 + hr], fill=(255, 255, 255, 165))
        patch.alpha_composite(dot_layer)

    return patch


def paste_sphere(canvas: Image.Image, center, patch: Image.Image):
    x = int(round(center[0] - patch.width / 2))
    y = int(round(center[1] - patch.height / 2))
    canvas.alpha_composite(patch, (x, y))


def build_light_backdrop() -> Image.Image:
    """亮色水绿宣纸径向微光渐变底板。"""
    small = 512
    bg = Image.new("RGBA", (small, small))
    px = bg.load()
    c_center = (248, 253, 253)
    c_mid = (232, 245, 247)
    c_edge = (204, 226, 232)
    cx, cy = small * 0.48, small * 0.40
    max_r = small * 0.80
    for y in range(small):
        dy = y - cy
        for x in range(small):
            dx = x - cx
            t = min(1.0, math.hypot(dx, dy) / max_r)
            if t < 0.55:
                u = t / 0.55
                col = tuple(int(round(c_center[i] + (c_mid[i] - c_center[i]) * u)) for i in range(3))
            else:
                u = (t - 0.55) / 0.45
                col = tuple(int(round(c_mid[i] + (c_edge[i] - c_mid[i]) * u)) for i in range(3))
            px[x, y] = col + (255,)
    return bg.resize((S, S), Image.BICUBIC)


def draw_board_layer(layer: Image.Image, pt_fn, z: int, glass_fill, grid_col, lw: float):
    """以独立图层正确 alpha 合成单层半透明玻璃盘面与 3×3 网格。"""
    glass = Image.new("RGBA", layer.size, (0, 0, 0, 0))
    gd = ImageDraw.Draw(glass)
    poly = [pt_fn(-1, -1, z), pt_fn(1, -1, z), pt_fn(1, 1, z), pt_fn(-1, 1, z)]
    gd.polygon(poly, fill=glass_fill)
    layer.alpha_composite(glass)

    grid = Image.new("RGBA", layer.size, (0, 0, 0, 0))
    gr = ImageDraw.Draw(grid)
    w = max(1, int(round(lw)))
    r = lw * 0.5
    for v in (-1, 0, 1):
        for p0, p1 in ((pt_fn(-1, v, z), pt_fn(1, v, z)), (pt_fn(v, -1, z), pt_fn(v, 1, z))):
            gr.line([p0, p1], fill=grid_col, width=w)
            gr.ellipse([p0[0] - r, p0[1] - r, p0[0] + r, p0[1] + r], fill=grid_col)
            gr.ellipse([p1[0] - r, p1[1] - r, p1[0] + r, p1[1] + r], fill=grid_col)
    layer.alpha_composite(grid)


def render_icon() -> Image.Image:
    bg = build_light_backdrop()
    art = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    glow = Image.new("RGBA", (S, S), (0, 0, 0, 0))

    # 1) 柔和中心微光氛围（3D 渲染空气感）
    ambient = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    ad = ImageDraw.Draw(ambient)
    ad.ellipse([S * 0.16, S * 0.16, S * 0.84, S * 0.84], fill=(116, 206, 214, 48))
    ad.ellipse([S * 0.28, S * 0.28, S * 0.72, S * 0.72], fill=(255, 255, 255, 92))
    ambient = ambient.filter(ImageFilter.GaussianBlur(radius=S * 0.085))
    art.alpha_composite(ambient)

    def pt(x, y, z):
        return project(x, y, z)

    # 2) 后层 (z = -1)
    draw_board_layer(art, pt, -1, (76, 152, 166, 16), (130, 166, 176, 175), S * 0.0092)

    # 3) 4 根跨层绛红立柱（完整贯通三层，保留原 TS 标志性基因，适配亮色通透绛红）
    rail_w = S * 0.0145
    for rx, ry in ((-1, 1), (1, 1), (-1, -1), (1, -1)):
        p_back = pt(rx, ry, -1)
        p_front = pt(rx, ry, 1)
        draw_capsule(art, p_back, p_front, rail_w, (194, 52, 60, 228))
        draw_capsule(art, p_back, p_front, rail_w * 0.28, (246, 156, 152, 130))

    # 4) 中层 (z = 0)
    draw_board_layer(art, pt, 0, (64, 146, 160, 22), (98, 138, 150, 200), S * 0.0108)

    # 5) 空间 3D 晶体 N 后半部分：右后竖边 + 斜对角后半段
    n_w = S * 0.037
    # N 右竖 (+1,-1,-1) -> (+1,+1,-1)
    draw_crystal_bar(
        art, glow, pt(1, -1, -1), pt(1, 1, -1), n_w * 0.90,
        lit_col=(52, 146, 156, 255), shadow_col=(20, 82, 96, 255),
        rim_col=(188, 244, 246, 220), glow_col=(62, 190, 202, 85),
    )
    # N 空间斜对角后半段 (0,0,0) -> (+1,-1,-1)
    draw_crystal_bar(
        art, glow, pt(0, 0, 0), pt(1, -1, -1), n_w * 0.95,
        lit_col=(50, 148, 160, 255), shadow_col=(18, 84, 98, 255),
        rim_col=(192, 246, 248, 225), glow_col=(62, 194, 206, 95),
    )

    # 6) 前层 (z = +1)：先画前层网格，再在其上叠放 N 前半段、红绿辅助线与三枚立体球体，确保零遮挡清晰可见
    draw_board_layer(art, pt, 1, (54, 140, 156, 24), (58, 102, 114, 232), S * 0.0132)

    # N 空间斜对角前半段 (-1,+1,+1) -> (0,0,0)
    draw_crystal_bar(
        art, glow, pt(-1, 1, 1), pt(0, 0, 0), n_w,
        lit_col=(56, 156, 168, 255), shadow_col=(20, 86, 102, 255),
        rim_col=(204, 250, 250, 235), glow_col=(70, 204, 216, 105),
    )
    # N 左竖 (-1,-1,+1) -> (-1,+1,+1)
    draw_crystal_bar(
        art, glow, pt(-1, -1, 1), pt(-1, 1, 1), n_w * 1.02,
        lit_col=(58, 160, 172, 255), shadow_col=(18, 80, 96, 255),
        rim_col=(208, 250, 250, 240), glow_col=(70, 204, 216, 105),
    )

    # 7) 绿/红 3D 辅助线（鲜明清晰地从黑子、白子射向中心蓝球！）
    p_blue = pt(0, 0, 0)
    p_black = pt(-1, 0, 1)
    p_white = pt(0, -1, 1)

    aux_w = S * 0.0155
    # 柔和色晕 + 利落主线
    draw_capsule(glow, p_black, p_blue, aux_w * 2.1, (22, 184, 92, 105))
    draw_capsule(art, p_black, p_blue, aux_w, (18, 158, 76, 250))
    draw_capsule(art, p_black, p_blue, aux_w * 0.30, (172, 248, 198, 180))

    draw_capsule(glow, p_white, p_blue, aux_w * 2.1, (232, 46, 62, 105))
    draw_capsule(art, p_white, p_blue, aux_w, (216, 38, 52, 250))
    draw_capsule(art, p_white, p_blue, aux_w * 0.30, (255, 186, 192, 180))

    # 8) 中心星蓝悬停球（带柔和光晕，压在 N 对角中心与红绿线交汇处）
    br = S * 0.062
    bglow = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    ImageDraw.Draw(bglow).ellipse([p_blue[0] - br * 2.0, p_blue[1] - br * 2.0,
                                   p_blue[0] + br * 2.0, p_blue[1] + br * 2.0],
                                  fill=(42, 148, 255, 128))
    bglow = bglow.filter(ImageFilter.GaussianBlur(radius=br * 0.62))
    art.alpha_composite(bglow)

    blue_sphere = render_3d_sphere_patch(
        radius=br,
        albedo=(44, 148, 255), ambient=(18, 66, 150),
        spec_col=(235, 248, 255), spec_pow=28.0,
        rim_col=(140, 212, 255), rim_strength=0.45,
        border_col=(24, 92, 186),
    )
    paste_sphere(art, p_blue, blue_sphere)

    # 9) 墨曜黑子（左，绿点）与温润瓷白子（下，红点）——真实 3D 球面着色
    stone_r = S * 0.098
    black_sphere = render_3d_sphere_patch(
        radius=stone_r,
        albedo=(52, 62, 74), ambient=(16, 22, 30),
        spec_col=(220, 236, 248), spec_pow=34.0,
        rim_col=(128, 168, 184), rim_strength=0.38,
        border_col=(14, 22, 28),
        dot_col=(24, 184, 88),
    )
    white_sphere = render_3d_sphere_patch(
        radius=stone_r,
        albedo=(250, 248, 244), ambient=(174, 198, 206),
        spec_col=(255, 255, 255), spec_pow=26.0,
        rim_col=(255, 255, 255), rim_strength=0.25,
        border_col=(104, 134, 144),
        dot_col=(224, 42, 56),
    )
    paste_sphere(art, p_black, black_sphere)
    paste_sphere(art, p_white, white_sphere)

    # 10) 合成柔和微光层与主体层
    glow = glow.filter(ImageFilter.GaussianBlur(radius=S * 0.016))
    composed = Image.alpha_composite(bg, glow)
    composed = Image.alpha_composite(composed, art)

    # 11) 圆角矩形裁切（1A）+ 细腻顶部玻璃内高光 + 清晰淡青外描边（亮/暗桌面皆立边）
    pad = int(S * 0.024)
    radius = int(S * 0.215)
    mask = Image.new("L", (S, S), 0)
    ImageDraw.Draw(mask).rounded_rectangle([pad, pad, S - pad, S - pad], radius=radius, fill=255)

    out = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    out.paste(composed, (0, 0), mask)

    border_layer = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    bd = ImageDraw.Draw(border_layer)
    bd.rounded_rectangle([pad + 6, pad + 6, S - pad - 6, S - pad - 6],
                         radius=radius - 6, outline=(255, 255, 255, 195), width=10)
    bd.rounded_rectangle([pad, pad, S - pad, S - pad],
                         radius=radius, outline=(132, 174, 184, 240), width=8)
    out = Image.alpha_composite(out, border_layer)
    return out


def main():
    hi_res = render_icon()
    master = hi_res.resize((OUT_MASTER, OUT_MASTER), Image.LANCZOS)
    master.save("assets-src/icon/icon-master.png")
    hi_res.resize((256, 256), Image.LANCZOS).save("src/Nmoku.Game/icon.png")

    ico_sizes = [(256, 256), (128, 128), (64, 64), (48, 48), (32, 32), (16, 16)]
    master.save("src/Nmoku.Game/assets/icon/nmoku.ico", sizes=ico_sizes)
    master.save("assets-src/icon/nmoku.ico", sizes=ico_sizes)
    print("icon done: assets-src/icon/icon-master.png / src/Nmoku.Game/icon.png / nmoku.ico")


if __name__ == "__main__":
    main()
