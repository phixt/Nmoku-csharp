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
        GhostBlack = 0.30f,                      // 非焦点未被标记：暗背景黑子需更高才可辨（人类反馈 2026-09-19）
        GhostWhite = 0.14f,
        GhostMarkedBlack = 0.70f,                // 辅助线标记到的子：强化显示
        GhostMarkedWhite = 0.50f,
        PanelBg = new Color(0.13f, 0.14f, 0.17f, 0.97f),
        PanelBorder = new Color(0.45f, 0.50f, 0.55f, 0.80f),
        TextPrimary = new Color(0.92f, 0.93f, 0.95f),
        TextDim = new Color(0.62f, 0.66f, 0.70f),
        ButtonBg = new Color(0.20f, 0.22f, 0.26f),
        ButtonHover = new Color(0.28f, 0.31f, 0.36f),
        Accent = new Color(0.30f, 0.55f, 1.00f),
        Error = new Color(1.00f, 0.35f, 0.30f),
    };

    /// <summary>色弱组（V 键切换）：辅助线红绿 → 黄蓝（§10.5 R4-6），星位红→蓝，最后手环红→黄。棋子透明度同组。</summary>
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
        GhostBlack = Default.GhostBlack,
        GhostWhite = Default.GhostWhite,
        GhostMarkedBlack = Default.GhostMarkedBlack,
        GhostMarkedWhite = Default.GhostMarkedWhite,
        PanelBg = Default.PanelBg,
        PanelBorder = Default.PanelBorder,
        TextPrimary = Default.TextPrimary,
        TextDim = Default.TextDim,
        ButtonBg = Default.ButtonBg,
        ButtonHover = Default.ButtonHover,
        Accent = Default.Accent,
        Error = Default.Error,
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

    /// <summary>非焦点且未被辅助线标记的黑/白棋子透明度（浅色主题接入时黑白数值反转）。</summary>
    public float GhostBlack { get; init; }
    public float GhostWhite { get; init; }

    /// <summary>非焦点但被辅助线标记到的黑/白棋子透明度（强化显示）。</summary>
    public float GhostMarkedBlack { get; init; }
    public float GhostMarkedWhite { get; init; }

    // ---- UI 语义色（M4 壳界面：面板/文字/按钮/toast；色弱组沿用同值，红绿问题只涉及棋盘辅助线）----

    public Color PanelBg { get; init; }
    public Color PanelBorder { get; init; }
    public Color TextPrimary { get; init; }
    public Color TextDim { get; init; }
    public Color ButtonBg { get; init; }
    public Color ButtonHover { get; init; }
    public Color Accent { get; init; }
    public Color Error { get; init; }
}
