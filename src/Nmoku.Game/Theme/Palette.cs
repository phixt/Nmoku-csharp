using Godot;

namespace Nmoku.Game.Theme;

/// <summary>
/// 语义调色板（M4 Palette 系统的奠基，migration-plan 决策 #3 / §3.4）：
/// 颜色一律按语义字段取用，视图内禁止出现颜色字面量。
/// 后续扩展点：M3 辅助线增 LineOwn/LineOpp + 色弱组（红绿→黄蓝）；T 键明暗切换；M4 调色板资源化 + 自定义导入。
/// </summary>
public sealed class Palette
{
    /// <summary>首发暗色（M4 明暗系统接入前的基础组）。</summary>
    public static readonly Palette Default = new()
    {
        Background = new Color(0.10f, 0.11f, 0.13f),
        AmbientLight = Colors.White,
        StoneBlack = new Color(0.09f, 0.09f, 0.10f),
        StoneWhite = new Color(0.92f, 0.93f, 0.95f),
        GridLine = new Color(0.62f, 0.66f, 0.70f),
        ConnectorLine = new Color(0.62f, 0.66f, 0.70f, 0.35f), // 同源 GHOST_CONNECTOR_OPACITY
        HoverHighlight = new Color(0.30f, 0.55f, 1.00f, 0.55f),
        LineSelf = new Color(0f, 1f, 0f),        // 0x00FF00（R4-6）
        LineEnemy = new Color(1f, 0f, 0f),       // 0xFF0000
        StarPoint = new Color(0.8f, 0f, 0f),     // 0xCC0000
        LastMoveRing = new Color(1f, 0f, 0f),    // 0xFF0000
    };

    /// <summary>色弱组（V 键切换）：辅助线红绿 → 黄蓝（§10.5 R4-6），星位红→蓝，最后手环红→黄。</summary>
    public static readonly Palette Colorblind = new()
    {
        Background = Default.Background,
        AmbientLight = Default.AmbientLight,
        StoneBlack = Default.StoneBlack,
        StoneWhite = Default.StoneWhite,
        GridLine = Default.GridLine,
        ConnectorLine = Default.ConnectorLine,
        HoverHighlight = Default.HoverHighlight,
        LineSelf = new Color(0f, 0.533f, 1f),    // 0x0088FF
        LineEnemy = new Color(1f, 0.8f, 0f),     // 0xFFCC00
        StarPoint = new Color(0f, 0.533f, 1f),
        LastMoveRing = new Color(1f, 0.8f, 0f),
    };

    public Color Background { get; init; }
    public Color AmbientLight { get; init; }
    public Color StoneBlack { get; init; }
    public Color StoneWhite { get; init; }
    public Color GridLine { get; init; }
    public Color ConnectorLine { get; init; }
    public Color HoverHighlight { get; init; }
    public Color LineSelf { get; init; }
    public Color LineEnemy { get; init; }
    public Color StarPoint { get; init; }
    public Color LastMoveRing { get; init; }
}
