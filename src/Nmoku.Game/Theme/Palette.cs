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
    };

    /// <summary>水绿宣纸 light（M4 第二轮首发套，migration-plan §3.4 已确认方向）：宣纸底、墨色字、水绿强调。</summary>
    public static readonly Palette Light = new()
    {
        Background = new Color(0.93f, 0.92f, 0.87f),      // 宣纸
        AmbientLight = Colors.White,
        StoneBlack = new Color(0.16f, 0.17f, 0.18f),      // 墨
        StoneWhite = new Color(0.98f, 0.97f, 0.93f),      // 贝白
        GridLine = new Color(0.38f, 0.42f, 0.40f),
        ConnectorLine = new Color(0.38f, 0.42f, 0.40f, 0.35f),
        HoverHighlight = new Color(0.20f, 0.55f, 0.45f, 0.50f),  // 水绿
        LineSelf = new Color(0.00f, 0.55f, 0.25f),        // 深绿（纸上可读）
        LineEnemy = new Color(0.80f, 0.15f, 0.12f),       // 朱红
        StarPoint = new Color(0.70f, 0.15f, 0.12f),
        LastMoveRing = new Color(0.85f, 0.25f, 0.15f),    // 朱
        LineSelfColorblind = new Color(0.10f, 0.45f, 0.85f),   // 蓝（纸上加深）
        LineEnemyColorblind = new Color(0.72f, 0.48f, 0.00f),  // 琥珀（纸上加深）
        // 明暗反转：浅底上黑子易见需低透明、白子难见需高透明（Dark 的镜像）
        GhostBlack = 0.14f,
        GhostWhite = 0.34f,
        GhostMarkedBlack = 0.60f,
        GhostMarkedWhite = 0.85f,
        // 2D 自绘棋子：白子取受光乳白（与 3D 观感一致）+ 灰绿描边定边（浅底上纯白不可见——M4 二轮反馈）
        StoneWhite2D = new Color(0.88f, 0.87f, 0.79f),
        StoneBlack2D = new Color(0.14f, 0.15f, 0.16f),
        StoneOutlineBlack2D = new Color(0.08f, 0.09f, 0.10f),
        StoneOutlineWhite2D = new Color(0.50f, 0.52f, 0.48f),
        TextOutline = new Color(0.97f, 0.96f, 0.92f, 0.90f), // 浅底深字纸色描边
        PanelBg = new Color(0.97f, 0.96f, 0.92f, 0.97f),
        PanelBorder = new Color(0.55f, 0.58f, 0.54f, 0.85f),
        TextPrimary = new Color(0.30f, 0.33f, 0.36f),     // 深字偏浅（纸底上纯墨过重——M4 二轮反馈）
        TextDim = new Color(0.54f, 0.57f, 0.55f),
        ButtonBg = new Color(0.88f, 0.89f, 0.84f),
        ButtonHover = new Color(0.80f, 0.85f, 0.79f),
        Accent = new Color(0.20f, 0.55f, 0.45f),          // 水绿
        Error = new Color(0.75f, 0.20f, 0.18f),
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
}
