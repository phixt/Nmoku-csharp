using Godot;

namespace Nmoku.Game.Theme;

/// <summary>
/// 语义调色板（migration-plan §3.4 / 决策 #3）：颜色一律按语义字段取用，视图内禁止出现颜色字面量。
/// M4 第二轮：明暗双套（Dark=现有暗色；Light=水绿宣纸）+ <see cref="Active"/> 运行时当前套 +
/// <see cref="WithColorblind"/>（红绿→黄蓝，随明暗套各自定义，V 键与 T 键正交组合）。
/// 壳屏在挂载时读取 Active；主题切换后由 Main 经 SceneRouter.Rebuild 重建已挂载屏（game 基座除外，
/// 其视图走 ApplyPalette 通道）。
/// </summary>
public sealed class Palette
{
    /// <summary>暗色（深邃午夜底 + 清晰墨曜黑子 + 清透蓝调浮板与按钮）。声明须在 Active 之前（静态初始化顺序）。</summary>
    public static readonly Palette Dark = new()
    {
        Background = new Color(0.055f, 0.075f, 0.106f),   // #0E131B 深邃午夜底（保深度同时让黑子清晰可辨）
        AmbientLight = Colors.White,
        StoneBlack = new Color(0.185f, 0.215f, 0.268f),   // #2F3744 墨曜黑子（深底上轮廓分明、立体感足）
        StoneWhite = new Color(0.935f, 0.945f, 0.965f),   // 温润瓷白
        FocusGridLine = new Color(0.760f, 0.860f, 0.960f),// #C2DCF5 焦点层高明晰冷银线
        GridLine = new Color(0.430f, 0.555f, 0.680f),     // #6E8EAD 非焦点层清晰冷蓝灰
        ConnectorLine = new Color(0.450f, 0.575f, 0.700f, 0.45f),
        HoverHighlight = new Color(0.34f, 0.62f, 1.00f, 0.60f),
        LineSelf = new Color(0f, 1f, 0f),        // 0x00FF00（R4-6）
        LineEnemy = new Color(1f, 0f, 0f),       // 0xFF0000
        StarPoint = new Color(0.85f, 0.16f, 0.16f),
        LastMoveRing = new Color(1f, 0.12f, 0.12f),
        LineSelfColorblind = new Color(0f, 0.533f, 1f),  // 0x0088FF
        LineEnemyColorblind = new Color(1f, 0.8f, 0f),   // 0xFFCC00
        GhostBlack = 0.44f,                      // 非焦点未被标记：深底黑子保持良好可读性
        GhostWhite = 0.18f,
        GhostMarkedBlack = 0.80f,                // 辅助线标记到的子：强化显示
        GhostMarkedWhite = 0.54f,
        // 2D 自绘棋子（深底黑子提亮墨灰体色 + 冷银描边确保一眼可辨）
        StoneWhite2D = new Color(0.86f, 0.88f, 0.91f),
        StoneBlack2D = new Color(0.155f, 0.180f, 0.220f),
        StoneOutlineBlack2D = new Color(0.52f, 0.60f, 0.70f),
        StoneOutlineWhite2D = new Color(0.94f, 0.96f, 0.98f),
        TextOutline = new Color(0.02f, 0.03f, 0.05f, 0.88f),
        PanelBg = new Color(0.078f, 0.106f, 0.160f, 0.96f), // #141B29 清透靛蓝浮板（去灰暗）
        PanelBorder = new Color(0.24f, 0.36f, 0.52f, 0.80f),
        TextPrimary = new Color(0.925f, 0.945f, 0.975f),    // #ECF1F9 高对比柔白
        TextDim = new Color(0.575f, 0.655f, 0.760f),        // #93A7C2 清晰次级字
        ButtonBg = new Color(0.106f, 0.153f, 0.235f),       // #1B273C 深海青蓝按钮（告别灰扑扑）
        ButtonHover = new Color(0.157f, 0.235f, 0.365f),    // #283C5D 朗润悬停蓝
        Accent = new Color(0.32f, 0.60f, 1.00f),            // #5299FF 澄澈星蓝
        Error = new Color(1.00f, 0.38f, 0.34f),
        // 键帽作用域色（F1 快捷键页）
        KeycapGame = new Color(0.290f, 0.478f, 0.494f),
        KeycapReplay = new Color(0.176f, 0.416f, 0.310f),
        KeycapPause = new Color(0.118f, 0.227f, 0.373f),
        KeycapCtrl = new Color(0.541f, 0.353f, 0.157f),
        KeycapShift = new Color(0.369f, 0.231f, 0.420f),
        KeycapGlobal = new Color(0.290f, 0.353f, 0.333f),
        KeycapIdle = new Color(0.145f, 0.195f, 0.220f),
        KeycapShadow = new Color(1f, 1f, 1f, 0.10f),
    };

    /// <summary>
    /// 浅水绿 light：温润淡青底配克制墨青文字（不过度死黑，保持 AA+ 清晰度）与高明晰深青棋盘网格。
    /// </summary>
    public static readonly Palette Light = new()
    {
        Background = new Color(0.922f, 0.957f, 0.961f),   // #EBF4F5 通透浅水绿
        AmbientLight = Colors.White,
        StoneBlack = new Color(0.145f, 0.160f, 0.180f),   // 温润墨石
        StoneWhite = new Color(0.965f, 0.955f, 0.940f),   // 暖白象牙
        FocusGridLine = new Color(0.215f, 0.365f, 0.400f),// #375D66 焦点层清晰墨青线（浅底高对比）
        GridLine = new Color(0.425f, 0.560f, 0.590f),     // #6C8F96 非焦点层明晰青灰（替代过淡 #A0C0C4）
        ConnectorLine = new Color(0.350f, 0.490f, 0.525f, 0.48f),
        HoverHighlight = new Color(0.16f, 0.52f, 0.48f, 0.55f),
        LineSelf = new Color(0.00f, 0.55f, 0.25f),
        LineEnemy = new Color(0.816f, 0.008f, 0.106f),
        StarPoint = new Color(0.72f, 0.16f, 0.13f),
        LastMoveRing = new Color(0.85f, 0.25f, 0.15f),
        LineSelfColorblind = new Color(0.10f, 0.45f, 0.85f),
        LineEnemyColorblind = new Color(0.72f, 0.48f, 0.00f),
        // 明暗反转：浅底上黑子易见需低透明、白子难见需高透明
        GhostBlack = 0.16f,
        GhostWhite = 0.36f,
        GhostMarkedBlack = 0.62f,
        GhostMarkedWhite = 0.86f,
        StoneWhite2D = new Color(0.935f, 0.915f, 0.895f),
        StoneBlack2D = new Color(0.145f, 0.160f, 0.180f),
        StoneOutlineBlack2D = new Color(0.06f, 0.08f, 0.10f),
        StoneOutlineWhite2D = new Color(0.36f, 0.42f, 0.44f),
        TextOutline = new Color(0.97f, 0.98f, 0.98f, 0.92f),
        PanelBg = new Color(0.975f, 0.990f, 0.992f, 0.98f),
        PanelBorder = new Color(0.68f, 0.79f, 0.82f, 0.90f),
        TextPrimary = new Color(0.227f, 0.322f, 0.357f),  // #3A525B 温雅青墨（不死黑，7.3:1 清晰度）
        TextDim = new Color(0.365f, 0.478f, 0.510f),      // #5D7A82 柔和青灰（4.4:1）
        ButtonBg = new Color(0.985f, 0.996f, 0.996f),     // 清透莹白瓷钮（告别灰扑扑）
        ButtonHover = new Color(0.865f, 0.940f, 0.950f),  // 淡青水色悬停
        Accent = new Color(0.176f, 0.415f, 0.455f),       // #2D6A74 清朗青碧强调色
        Error = new Color(0.620f, 0.245f, 0.245f),
        KeycapGame = new Color(0.624f, 0.796f, 0.816f),
        KeycapReplay = new Color(0.176f, 0.416f, 0.310f),
        KeycapPause = new Color(0.118f, 0.227f, 0.373f),
        KeycapCtrl = new Color(0.722f, 0.447f, 0.173f),
        KeycapShift = new Color(0.478f, 0.306f, 0.549f),
        KeycapGlobal = new Color(0.604f, 0.659f, 0.639f),
        KeycapIdle = new Color(1f, 1f, 1f),
        KeycapShadow = new Color(0f, 0f, 0f, 0.14f),
    };

    /// <summary>当前生效套（运行时可切换；默认暗色）。</summary>
    public static Palette Active { get; set; } = Dark;

    /// <summary>色弱变体（V 键）：辅助线红绿→黄蓝，星位/最后手环随组；明暗两套各自的对比度校准。</summary>
    public Palette WithColorblind() => new()
    {
        Background = Background,
        AmbientLight = AmbientLight,
        StoneBlack = StoneBlack,
        StoneWhite = StoneWhite,
        FocusGridLine = FocusGridLine,
        GridLine = GridLine,
        ConnectorLine = ConnectorLine,
        HoverHighlight = HoverHighlight,
        LineSelf = LineSelfColorblind,
        LineEnemy = LineEnemyColorblind,
        StarPoint = LineSelfColorblind,
        LastMoveRing = LineEnemyColorblind,
        LineSelfColorblind = LineSelfColorblind,
        LineEnemyColorblind = LineEnemyColorblind,
        GhostBlack = GhostBlack,
        GhostWhite = GhostWhite,
        GhostMarkedBlack = GhostMarkedBlack,
        GhostMarkedWhite = GhostMarkedWhite,
        StoneWhite2D = StoneWhite2D,
        StoneBlack2D = StoneBlack2D,
        StoneOutlineBlack2D = StoneOutlineBlack2D,
        StoneOutlineWhite2D = StoneOutlineWhite2D,
        TextOutline = TextOutline,
        PanelBg = PanelBg,
        PanelBorder = PanelBorder,
        TextPrimary = TextPrimary,
        TextDim = TextDim,
        ButtonBg = ButtonBg,
        ButtonHover = ButtonHover,
        Accent = Accent,
        Error = Error,
        KeycapGame = KeycapGame,
        KeycapReplay = KeycapReplay,
        KeycapPause = KeycapPause,
        KeycapCtrl = KeycapCtrl,
        KeycapShift = KeycapShift,
        KeycapGlobal = KeycapGlobal,
        KeycapIdle = KeycapIdle,
        KeycapShadow = KeycapShadow,
    };

    /// <summary>按色弱开关取当前套的变体（壳层统一入口）。</summary>
    public static Palette For(bool colorblind) =>
        colorblind ? Active.WithColorblind() : Active;

    public Color Background { get; private init; }
    public Color AmbientLight { get; private init; }
    public Color StoneBlack { get; private init; }
    public Color StoneWhite { get; private init; }
    public Color FocusGridLine { get; private init; }
    public Color GridLine { get; private init; }
    public Color ConnectorLine { get; private init; }
    public Color HoverHighlight { get; private init; }
    public Color LineSelf { get; private init; }
    public Color LineEnemy { get; private init; }
    public Color StarPoint { get; private init; }
    public Color LastMoveRing { get; private init; }

    /// <summary>色弱组的辅助线/星位/最后手环配色（随明暗套分别校准）。</summary>
    public Color LineSelfColorblind { get; private init; }
    public Color LineEnemyColorblind { get; private init; }

    /// <summary>非焦点且未被辅助线标记的黑/白棋子透明度（明暗套数值反转）。</summary>
    public float GhostBlack { get; private init; }
    public float GhostWhite { get; private init; }

    /// <summary>非焦点但被辅助线标记到的黑/白棋子透明度（强化显示）。</summary>
    public float GhostMarkedBlack { get; private init; }
    public float GhostMarkedWhite { get; private init; }

    // ---- 2D 自绘棋子与文字描边（语义化，M4 二轮：随主题适配，禁止视图内硬编码色值）----

    public Color StoneWhite2D { get; private init; }
    public Color StoneBlack2D { get; private init; }
    public Color StoneOutlineBlack2D { get; private init; }
    public Color StoneOutlineWhite2D { get; private init; }
    /// <summary>状态字描边（暗底白字用深描边、浅底深字用纸色描边）。</summary>
    public Color TextOutline { get; private init; }

    // ---- UI 语义色（M4 壳界面：面板/文字/按钮/toast；色弱组沿用同值，红绿问题只涉及棋盘辅助线）----

    public Color PanelBg { get; private init; }
    public Color PanelBorder { get; private init; }
    public Color TextPrimary { get; private init; }
    public Color TextDim { get; private init; }
    public Color ButtonBg { get; private init; }
    public Color ButtonHover { get; private init; }
    public Color Accent { get; private init; }
    public Color Error { get; private init; }

    // ---- 键帽作用域色（F1 快捷键页；色弱组沿用同值，作用域靠色相区分与辅助线无关）----

    public Color KeycapGame { get; private init; }
    public Color KeycapReplay { get; private init; }
    public Color KeycapPause { get; private init; }
    public Color KeycapCtrl { get; private init; }
    public Color KeycapShift { get; private init; }
    public Color KeycapGlobal { get; private init; }
    public Color KeycapIdle { get; private init; }
    /// <summary>键帽硬阴影色：亮底深影/暗底浅影（暗底浅影=假环境光，非炫光）。</summary>
    public Color KeycapShadow { get; private init; }
}
