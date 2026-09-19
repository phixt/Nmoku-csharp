using Godot;

namespace Nmoku.Game.Theme;

/// <summary>
/// 语义调色板（M4 Palette 系统的奠基，migration-plan 决策 #3 / §3.4）：
/// 颜色一律按语义字段取用，视图内禁止出现颜色字面量。
/// 后续扩展点：M3 辅助线增 LineOwn/LineOpp + 色弱组（红绿→黄蓝）；T 键明暗切换；M4 调色板资源化 + 自定义导入。
/// </summary>
public sealed class Palette
{
    /// <summary>首发暗色（M4 明暗系统接入前唯一实例）。</summary>
    public static readonly Palette Default = new()
    {
        Background = new Color(0.10f, 0.11f, 0.13f),
        AmbientLight = Colors.White,
        StoneBlack = new Color(0.09f, 0.09f, 0.10f),
        StoneWhite = new Color(0.92f, 0.93f, 0.95f),
        GridLine = new Color(0.62f, 0.66f, 0.70f),
        ConnectorLine = new Color(0.62f, 0.66f, 0.70f, 0.35f), // 同源 GHOST_CONNECTOR_OPACITY
        HoverHighlight = new Color(0.30f, 0.55f, 1.00f, 0.55f),
    };

    public Color Background { get; init; }
    public Color AmbientLight { get; init; }
    public Color StoneBlack { get; init; }
    public Color StoneWhite { get; init; }
    public Color GridLine { get; init; }
    public Color ConnectorLine { get; init; }
    public Color HoverHighlight { get; init; }
}
