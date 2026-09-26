using Godot;
using Nmoku.Core;
using Nmoku.Game.Theme;

namespace Nmoku.Game.Views;

/// <summary>
/// 2D 棋子绘制共用体（批10 自 Board2DView 抽取）：体色圆 → 球面明暗暗环 → 左上高光光斑 →
/// 硬边细描边四层，盘面与行棋指示器共用同一质感。纹理静态缓存（明暗纹理为逐像素圆内遮罩，
/// 生成一次）；颜色全部来自 Palette 语义字段（禁止视图内派生色值）。
/// </summary>
public static class StonePainter
{
    private const float StoneRadius = 0.45f; // 格单位（盘面用；指示器直接传像素半径）

    private static GradientTexture2D? _glowTex;
    private static Texture2D? _shadeTex;

    /// <summary>盘面棋子：radius 以格单位给入，按 scale 换算。</summary>
    public static void DrawOnBoard(Control canvas, Vector2 pos, float scale, CellState state, float alpha,
        Palette palette) =>
        Draw(canvas, pos, StoneRadius * scale, state, alpha, palette);

    public static void Draw(Control canvas, Vector2 pos, float radius, CellState state, float alpha, Palette palette)
    {
        // 四层绘制（人类反馈 2026-09-26：按 3D 受光面质感渲染，去玩具感）。
        bool isBlack = state == CellState.Black;
        var body = isBlack ? palette.StoneBlack2D : palette.StoneWhite2D;
        canvas.DrawCircle(pos, radius, new Color(body.R, body.G, body.B, alpha),
            filled: true, width: -1f, antialiased: true);

        EnsureShadeTexture();
        var stoneRect = new Rect2(pos - new Vector2(radius, radius), new Vector2(radius * 2, radius * 2));
        canvas.DrawTextureRect(_shadeTex, stoneRect, tile: false, modulate: new Color(1, 1, 1, alpha));

        EnsureGlowTexture();
        float glowStrength = (isBlack ? 0.35f : 0.55f) * alpha;
        // 高光必须完整落在轮廓内：偏移幅 0.30√2·r ≈ 0.42r + 光斑半径 0.45r = 0.87r < r。
        // 旧参数（0.28 格偏移 + 0.55r 光斑 = 1.43r）会穿透圆形边框（M4R 验收反馈）。
        float glowDiameter = radius * 0.9f;
        var glowCenter = pos + new Vector2(-0.30f, -0.30f) * radius;
        var glowRect = new Rect2(glowCenter - new Vector2(glowDiameter / 2f, glowDiameter / 2f),
            new Vector2(glowDiameter, glowDiameter));
        canvas.DrawTextureRect(_glowTex, glowRect, tile: false, modulate: new Color(1, 1, 1, glowStrength));

        // 描边：1.5 逻辑宽 ×内容缩放 的 AA 弧 = "又厚又糊"（人类反馈）——改 1.0 硬边、64 段
        var outlineColor = isBlack ? palette.StoneOutlineBlack2D : palette.StoneOutlineWhite2D;
        canvas.DrawArc(pos, radius, 0, MathF.Tau, 64,
            new Color(outlineColor.R, outlineColor.G, outlineColor.B, MathF.Min(1f, alpha + 0.2f)), 1.0f, false);
    }

    private static void EnsureGlowTexture()
    {
        if (_glowTex != null)
        {
            return;
        }
        var gradient = new Gradient
        {
            Colors = [new Color(1, 1, 1, 0.9f), new Color(1, 1, 1, 0f)],
            Offsets = [0f, 1f],
        };
        _glowTex = new GradientTexture2D
        {
            Gradient = gradient,
            Fill = GradientTexture2D.FillEnum.Radial,
            FillFrom = new Vector2(0.5f, 0.5f),
            FillTo = new Vector2(1f, 0.5f),
            Width = 64,
            Height = 64,
        };
    }

    private static void EnsureShadeTexture()
    {
        if (_shadeTex != null)
        {
            return;
        }
        // 逐像素生成：方形径向渐变的圆外区域无法归零（各方向距光源距离不同，浅底下泄露为
        // "超界半透明深色方框"，人类反馈 2026-09-26）——必须按圆内遮罩生成，圆外强制透明。
        const int size = 256;
        var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        var light = new Vector2(0.35f, 0.30f);           // 与高光同侧：对侧边缘进入暗环
        float maxDist = (Vector2.One - light).Length();  // 与旧 FillTo 标度一致
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                var uv = new Vector2((x + 0.5f) / size, (y + 0.5f) / size);
                float t = (uv - light).Length() / maxDist;
                float alpha = t switch
                {
                    <= 0.62f => Mathf.Remap(t, 0f, 0.62f, 0f, 0.08f),
                    <= 0.93f => Mathf.Remap(t, 0.62f, 0.93f, 0.08f, 0.45f),
                    _ => Mathf.Remap(t, 0.93f, 1f, 0.45f, 0f),
                };
                // 圆内遮罩 + 2% 羽化（体色圆自身有 AA 边缘，硬切会出毛边）
                float edge = (0.5f - (uv - new Vector2(0.5f, 0.5f)).Length()) / 0.02f;
                alpha *= Mathf.Clamp(edge, 0f, 1f);
                image.SetPixel(x, y, new Color(0f, 0f, 0f, alpha));
            }
        }
        _shadeTex = ImageTexture.CreateFromImage(image);
    }
}
