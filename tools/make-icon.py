# 生成 Nmoku 游戏图标（干净利落的现代矢量风格：亮色水绿圆角矩形 + 3×3×3 竖直立体棋盘 +
# 跨层绛红立柱 + 棋盘左右纵边与空间对角连线勾勒的立体 "N" + 蓝/黑绿/白红矢量辅助线）。
# 产物：
#   assets-src/icon/icon-master.png       —— 512px 主图（源，入库）
#   src/Nmoku.Game/icon.png               —— 256px（project.godot config/icon 用）
#   src/Nmoku.Game/assets/icon/nmoku.ico  —— 多尺寸 ICO（Windows 应用图标）
#   assets-src/icon/nmoku.ico             —— 同源归档副本
# 用法：python tools/make-icon.py
import math
from PIL import Image, ImageDraw

S = 2048  # 4× 超采样矢量栅格化，LANCZOS 下采样保全尺寸锐利抗锯齿
OUT_MASTER = 512

# 3/4 透视相机：(-1,-1,+1)->(-1,+1,+1)->(0,0,0)->(+1,-1,-1)->(+1,+1,-1) 构成平衡对称的 3D 立体 N
YAW_DEG = 32.0
PITCH_DEG = 17.0
CAM_DIST = 8.6
SCALE = S * 0.232
CX = S * 0.518
CY = S * 0.504


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


def draw_segment(canvas: Image.Image, p0, p1, width: float, color):
    """干净利落的圆头扁平矢量线段（通过独立 RGBA 层正确 alpha 合成）。"""
    tmp = Image.new("RGBA", canvas.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(tmp)
    w = max(1, int(round(width)))
    r = width * 0.5
    d.line([p0, p1], fill=color, width=w)
    d.ellipse([p0[0] - r, p0[1] - r, p0[0] + r, p0[1] + r], fill=color)
    d.ellipse([p1[0] - r, p1[1] - r, p1[0] + r, p1[1] + r], fill=color)
    canvas.alpha_composite(tmp)


def draw_polyline(canvas: Image.Image, pts, width: float, color):
    """连续无接缝圆角矢量折线（单一层绘制，避免端点叠加加深）。"""
    tmp = Image.new("RGBA", canvas.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(tmp)
    w = max(1, int(round(width)))
    r = width * 0.5
    for i in range(len(pts) - 1):
        d.line([pts[i], pts[i + 1]], fill=color, width=w)
    for p in pts:
        d.ellipse([p[0] - r, p[1] - r, p[0] + r, p[1] + r], fill=color)
    canvas.alpha_composite(tmp)


def draw_grid_layer(canvas: Image.Image, z: int, color, width: float):
    """单层 3×3 纯净矢量网格（无玻璃底片、无杂光）。"""
    tmp = Image.new("RGBA", canvas.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(tmp)
    w = max(1, int(round(width)))
    r = width * 0.5
    for v in (-1, 0, 1):
        for p0, p1 in ((project(-1, v, z), project(1, v, z)), (project(v, -1, z), project(v, 1, z))):
            d.line([p0, p1], fill=color, width=w)
            d.ellipse([p0[0] - r, p0[1] - r, p0[0] + r, p0[1] + r], fill=color)
            d.ellipse([p1[0] - r, p1[1] - r, p1[0] + r, p1[1] + r], fill=color)
    canvas.alpha_composite(tmp)


def draw_flat_circle(canvas: Image.Image, center, radius: float, fill, outline=None, outline_w: float = 0,
                     dot_radius: float = 0, dot_fill=None):
    """纯净扁平矢量圆（可选外描边与内辅助圆点）。"""
    tmp = Image.new("RGBA", canvas.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(tmp)
    cx, cy = center
    r = radius
    if outline is not None and outline_w > 0:
        orad = r + outline_w * 0.5
        d.ellipse([cx - orad, cy - orad, cx + orad, cy + orad], fill=outline)
        irad = r - outline_w * 0.5
        d.ellipse([cx - irad, cy - irad, cx + irad, cy + irad], fill=fill)
    else:
        d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=fill)

    if dot_fill is not None and dot_radius > 0:
        d.ellipse([cx - dot_radius, cy - dot_radius, cx + dot_radius, cy + dot_radius], fill=dot_fill)

    canvas.alpha_composite(tmp)


def render_icon() -> Image.Image:
    # 1) 干净亮色水绿底板（与游戏浅色主题 #EBF4F5 一致）
    bg_color = (237, 246, 247, 255)  # #EDF6F7
    canvas = Image.new("RGBA", (S, S), bg_color)

    # 2) 后层 (z = -1) 网格
    draw_grid_layer(canvas, -1, (172, 198, 204, 255), S * 0.0095)

    # 3) 4 根跨层绛红立柱（保留原 TS 图标基因，纯平矢量）
    rail_w = S * 0.0145
    rail_col = (186, 46, 54, 255)  # #BA2E36 沉稳绛红
    for rx, ry in ((-1, 1), (1, 1), (-1, -1), (1, -1)):
        draw_segment(canvas, project(rx, ry, -1), project(rx, ry, 1), rail_w, rail_col)

    # 4) 中层 (z = 0) 网格
    draw_grid_layer(canvas, 0, (132, 166, 174, 255), S * 0.0110)

    # 5) 前层 (z = +1) 网格
    draw_grid_layer(canvas, 1, (76, 118, 128, 255), S * 0.0135)

    # 6) 立体矢量 "N"：前层左纵边 + 贯穿中心蓝球的空间斜对角连线 + 后层右纵边
    #    用极薄底色描边做干净的穿插留白，让 N 与其余格线层次分明
    n_pts = [
        project(-1, -1, 1),
        project(-1, 1, 1),
        project(1, -1, -1),
        project(1, 1, -1),
    ]
    n_w = S * 0.036
    draw_polyline(canvas, n_pts, n_w + S * 0.012, (237, 246, 247, 190))
    draw_polyline(canvas, n_pts, n_w, (34, 96, 108, 255))  # #22606C 利落深青碧

    # 7) 绿/红矢量辅助线（从黑子、白子直连中心蓝球）
    p_blue = project(0, 0, 0)
    p_black = project(-1, 0, 1)
    p_white = project(0, -1, 1)

    aux_w = S * 0.0165
    draw_segment(canvas, p_black, p_blue, aux_w + S * 0.008, (237, 246, 247, 210))
    draw_segment(canvas, p_black, p_blue, aux_w, (20, 158, 80, 255))   # 祖母绿
    draw_segment(canvas, p_white, p_blue, aux_w + S * 0.008, (237, 246, 247, 210))
    draw_segment(canvas, p_white, p_blue, aux_w, (218, 42, 54, 255))   # 朱红

    # 8) 中心星蓝悬停圆（带干净浅色间隔环，在 N 对角交汇处清晰独立）
    draw_flat_circle(
        canvas, p_blue, radius=S * 0.062,
        fill=(34, 134, 246, 255),
        outline=bg_color, outline_w=S * 0.012,
    )

    # 9) 黑子（左，绿点）与白子（下，红点）——干净扁平矢量
    stone_r = S * 0.096
    draw_flat_circle(
        canvas, p_black, radius=stone_r,
        fill=(32, 40, 48, 255),
        outline=(237, 246, 247, 235), outline_w=S * 0.010,
        dot_radius=stone_r * 0.36, dot_fill=(24, 184, 90, 255),
    )
    draw_flat_circle(
        canvas, p_white, radius=stone_r,
        fill=(255, 255, 255, 255),
        outline=(76, 110, 120, 255), outline_w=S * 0.011,
        dot_radius=stone_r * 0.36, dot_fill=(224, 44, 58, 255),
    )

    # 10) 圆角矩形裁切（1A）+ 干净单线外边框（在浅色与深色桌面皆清晰立边）
    pad = int(S * 0.024)
    radius = int(S * 0.215)
    mask = Image.new("L", (S, S), 0)
    ImageDraw.Draw(mask).rounded_rectangle([pad, pad, S - pad, S - pad], radius=radius, fill=255)

    out = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    out.paste(canvas, (0, 0), mask)

    border = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    ImageDraw.Draw(border).rounded_rectangle(
        [pad, pad, S - pad, S - pad],
        radius=radius, outline=(146, 182, 190, 255), width=10,
    )
    out = Image.alpha_composite(out, border)
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
