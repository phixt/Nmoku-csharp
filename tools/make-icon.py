# 一次性工具：生成 Nmoku 游戏图标（程序化绘制，可重跑调整）。
# 产物：
#   assets-src/icon/icon-master.png  —— 512px 主图（源，入库）
#   src/Nmoku.Game/icon.png          —— 256px（project.godot config/icon 用）
#   assets-src/icon/nmoku.ico        —— 多尺寸 ICO（导出预设 application/icon 用）
# 用法：python tools/make-icon.py
import math
from PIL import Image, ImageDraw

S = 512  # 主画布
N = 5    # 图示 5×5 小棋盘
LAYERS = 3
SPACING = 0.95  # 层间距（逻辑单位）


def project(x, y, z, scale, cx, cy):
    """等距投影：棋盘平面 (x,y) 层高 z（0=底层）→ 屏幕坐标。"""
    px, py = x - N / 2, y - N / 2
    iso_x = (px - py) * math.cos(math.radians(30))
    iso_y = (px + py) * math.sin(math.radians(30)) - z * SPACING
    return cx + iso_x * scale, cy + iso_y * scale


def stone(d, grid_xy, z, player, scale, cx, cy):
    """棋子：球面感（体色+左上高光），等距坐标 grid_xy=(i,j)。"""
    i, j = grid_xy
    r = scale * 0.34
    px, py = project(i, j, z, scale, cx, cy)
    if player == 'b':
        body, hi, edge = (34, 34, 38, 255), (116, 116, 126, 255), (0, 0, 0, 255)
    else:
        body, hi, edge = (238, 232, 224, 255), (255, 255, 255, 255), (90, 90, 96, 255)
    d.ellipse([px - r, py - r, px + r, py + r], fill=body, outline=edge, width=3)
    hr = r * 0.30
    hx, hy = px - r * 0.32, py - r * 0.36
    d.ellipse([hx - hr / 2, hy - hr / 2, hx + hr / 2, hy + hr / 2], fill=hi)


def main():
    # 网格对角宽 = N×cos30×scale（iso_x 跨度 ±N/2×cos30），高 = N×sin30×scale + 层距；
    # scale 取画布 1/10.5 留边距
    scale = S / 10.5
    cx, cy = S / 2, S / 2 + scale * 0.6

    # 1) 圆角渐变底
    top, bottom = (26, 46, 54), (14, 22, 30)
    base = Image.new('RGBA', (S, S), (0, 0, 0, 0))
    bd = ImageDraw.Draw(base)
    for yy in range(S):
        t = yy / S
        color = tuple(int(top[i] + (bottom[i] - top[i]) * t) for i in range(3)) + (255,)
        bd.line([(0, yy), (S, yy)], fill=color)

    # 2) 内容层：三层等距网格 + 棋子（整体最后套圆角，绝不溢出）
    art = Image.new('RGBA', (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(art)
    layer_colors = [(150, 184, 188, 255), (128, 162, 170, 170), (108, 142, 152, 110)]
    for z in range(LAYERS):
        color = layer_colors[z]
        lw = 5 if z == 0 else 3
        for i in range(N + 1):
            d.line([project(i, 0, z, scale, cx, cy), project(i, N, z, scale, cx, cy)], fill=color, width=lw)
            d.line([project(0, i, z, scale, cx, cy), project(N, i, z, scale, cx, cy)], fill=color, width=lw)

    # 五连：中层对角线 (i, 4-i)，等距投影下呈水平菱形线，黑白相间
    for i in range(N):
        stone(d, (i, N - 1 - i), 1, 'b' if i % 2 == 0 else 'w', scale, cx, cy)
    # 顶层/底层各一颗：暗示跨层维度
    stone(d, (1, 1), 2, 'b', scale, cx, cy)
    stone(d, (3, 3), 0, 'w', scale, cx, cy)

    # 3) 合成：底与内容各自套圆角后 alpha 合成（paste 是替换语义，透明像素会擦底）
    mask = Image.new('L', (S, S), 0)
    ImageDraw.Draw(mask).rounded_rectangle([8, 8, S - 8, S - 8], radius=100, fill=255)
    transparent = Image.new('RGBA', (S, S), (0, 0, 0, 0))
    base_c = Image.composite(base, transparent, mask)
    art_c = Image.composite(art, transparent, mask)
    img = Image.alpha_composite(base_c, art_c)

    img.save('assets-src/icon/icon-master.png')
    img.resize((256, 256), Image.LANCZOS).save('src/Nmoku.Game/icon.png')
    img.save('src/Nmoku.Game/assets/icon/nmoku.ico', sizes=[(256, 256), (128, 128), (64, 64), (32, 32), (16, 16)])
    print('icon done: icon-master.png / icon.png / nmoku.ico')


if __name__ == '__main__':
    main()
