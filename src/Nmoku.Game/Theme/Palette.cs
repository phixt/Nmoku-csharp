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
    /// <summary>暗色（M0–M4R 既有视觉基线）。声明须在 Active 之前（静态初始化顺序）。</summary>
    public static readonly Palette Dark = new()
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
        LineSelfColorblind = new Color(0f, 0.533f, 1f),  // 0x0088FF
        LineEnemyColorblind = new Color(1f, 0.8f, 0f),   // 0xFFCC00
        GhostBlack = 0.30f,                      // 非焦点未被标记：暗背景黑子需更高才可辨（人类反馈 2026-09-19）
        GhostWhite = 0.14f,
        GhostMarkedBlack = 0.70f,                // 辅助线标记到的子：强化显示
        GhostMarkedWhite = 0.50f,
        // 2D 自绘棋子（M4 二轮反馈：2D 白子对比不足、须随主题适配；Dark 值=原 Darkened 派生结果，观感不变）
        StoneWhite2D = new Color(0.69f, 0.70f, 0.71f),
        StoneBlack2D = new Color(0.04f, 0.04f, 0.05f),
        StoneOutlineBlack2D = new Color(0.07f, 0.07f, 0.08f),
        StoneOutlineWhite2D = new Color(0.92f, 0.93f, 0.95f),
        TextOutline = new Color(0f, 0f, 0f, 0.85f), // 状态字描边（暗底白字黑描边）
        PanelBg = new Color(0.13f, 0.14f, 0.17f, 0.97f),
        PanelBorder = new Color(0.45f, 0.50f, 0.55f, 0.80f),
        TextPrimary = new Color(0.92f, 0.93f, 0.95f),
        TextDim = new Color(0.62f, 0.66f, 0.70f),
        ButtonBg = new Color(0.20f, 0.22f, 0.26f),
        ButtonHover = new Color(0.28f, 0.31f, 0.36f),
        Accent = new Color(0.30f, 0.55f, 1.00f),
        Error = new Color(1.00f, 0.35f, 0.30f),
        // 键帽作用域色（F1 快捷键页；源 panels.css 键盘模拟配色 + Ctrl/Shift 自定层，不叠层规则）
        KeycapGame = new Color(0.290f, 0.478f, 0.494f),    // 源暗 base #4A7A7E
        KeycapReplay = new Color(0.176f, 0.416f, 0.310f),  // 源复盘 #2D6A4F
        KeycapPause = new Color(0.118f, 0.227f, 0.373f),   // 源暂停 #1E3A5F
        KeycapCtrl = new Color(0.541f, 0.353f, 0.157f),    // Ctrl 层 赭（暗套加深）
        KeycapShift = new Color(0.369f, 0.231f, 0.420f),   // Shift 层 紫（暗套加深）
        KeycapGlobal = new Color(0.290f, 0.353f, 0.333f),  // 全局 中性灰绿
        KeycapIdle = new Color(0.165f, 0.227f, 0.243f),    // 非活跃（源暗 inactive #2A3A3E）
        KeycapShadow = new Color(1f, 1f, 1f, 0.10f),       // 暗底用浅色硬阴影（假环境光，人类拍板）
    };

    /// <summary>
    /// 浅水绿 light（B17 对齐源项目，2026-09-25 重校）：底/网格/石子取源 Types.ts Theme 与
    /// RightPanel 渐变端色，面板/文字取源 CSS（body #F2F7F2 系、强调 #2C4A4E）；辅助线自方深绿、
    /// ghost 透明度与色弱组为本项目 B9 校准值保持不变。
    /// </summary>
    public static readonly Palette Light = new()
    {
        Background = new Color(0.910f, 0.957f, 0.961f),   // 源画布底 #E8F4F5（浅水绿）
        AmbientLight = Colors.White,
        StoneBlack = new Color(0.133f, 0.133f, 0.140f),   // 源黑石 #222222
        StoneWhite = new Color(0.941f, 0.910f, 0.878f),   // 源白石渐变顶 #F0E8E0（暖象牙）
        GridLine = new Color(0.627f, 0.753f, 0.769f),     // 源网格 #A0C0C4
        ConnectorLine = new Color(0.627f, 0.753f, 0.769f, 0.35f),
        HoverHighlight = new Color(0.20f, 0.55f, 0.45f, 0.50f),  // 水绿（本项目悬停语义）
        LineSelf = new Color(0.00f, 0.55f, 0.25f),        // 深绿（纸上可读，B9 自方语义）
        LineEnemy = new Color(0.816f, 0.008f, 0.106f),    // 源辅助线 #D0021B
        StarPoint = new Color(0.70f, 0.15f, 0.12f),
        LastMoveRing = new Color(0.85f, 0.25f, 0.15f),    // 朱（本项目最后手环）
        LineSelfColorblind = new Color(0.10f, 0.45f, 0.85f),   // 蓝（纸上加深）
        LineEnemyColorblind = new Color(0.72f, 0.48f, 0.00f),  // 琥珀（纸上加深）
        // 明暗反转：浅底上黑子易见需低透明、白子难见需高透明（Dark 的镜像）
        GhostBlack = 0.14f,
        GhostWhite = 0.34f,
        GhostMarkedBlack = 0.60f,
        GhostMarkedWhite = 0.85f,
        // 2D 自绘棋子：端色取源 RightPanel 渐变（白 #F0E8E0→#D0C8C0 描边 #555 / 黑 #222 描边 #000）
        StoneWhite2D = new Color(0.906f, 0.867f, 0.835f), // 渐变中点 #E7DDD5
        StoneBlack2D = new Color(0.133f, 0.133f, 0.140f),
        StoneOutlineBlack2D = new Color(0.00f, 0.00f, 0.00f),
        StoneOutlineWhite2D = new Color(0.333f, 0.333f, 0.333f),
        TextOutline = new Color(0.97f, 0.96f, 0.92f, 0.90f), // 浅底深字纸色描边
        PanelBg = new Color(0.955f, 0.970f, 0.960f, 0.97f),  // 源 body #F2F7F2 系面板
        PanelBorder = new Color(0.72f, 0.78f, 0.76f, 0.85f),
        TextPrimary = new Color(0.102f, 0.180f, 0.227f),  // 源 text-on-base #1A2E3A
        TextDim = new Color(0.353f, 0.478f, 0.494f),      // 源次级字 #5A7A7E
        ButtonBg = new Color(0.925f, 0.945f, 0.935f),     // #ECF1EE 浅底按钮
        ButtonHover = new Color(0.784f, 0.878f, 0.894f),  // 源 hover #C8E0E4
        Accent = new Color(0.173f, 0.290f, 0.306f),       // 源强调/主按钮 #2C4A4E
        Error = new Color(0.545f, 0.227f, 0.227f),        // 源危险 #8B3A3A
        KeycapGame = new Color(0.910f, 0.957f, 0.961f),   // 源亮 base #E8F4F5（靠描边+阴影与同色底区分）
        KeycapReplay = new Color(0.176f, 0.416f, 0.310f), // 源复盘 #2D6A4F
        KeycapPause = new Color(0.118f, 0.227f, 0.373f),  // 源暂停 #1E3A5F
        KeycapCtrl = new Color(0.722f, 0.447f, 0.173f),   // Ctrl 层 赭 #B8722C
        KeycapShift = new Color(0.478f, 0.306f, 0.549f),  // Shift 层 紫 #7A4E8C
        KeycapGlobal = new Color(0.604f, 0.659f, 0.639f), // 全局 灰绿 #9AA8A3
        KeycapIdle = new Color(1f, 1f, 1f),               // 非活跃（源亮 inactive #FFF）
        KeycapShadow = new Color(0f, 0f, 0f, 0.14f),      // 亮底用深色硬阴影（源 0 4px 0）
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
