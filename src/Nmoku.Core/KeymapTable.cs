namespace Nmoku.Core;

/// <summary>
/// 快捷键单一数据源（04-contracts §4.2.1 KeyBinding schema 落地）：游戏侧 ShortcutRouter 的
/// context 门控、F1 帮助屏、docs 快捷键对照表全部由本表派生，杜绝三处漂移。
/// 物理键位本体在 Godot InputMap（project.godot / 运行时注册），此处登记语义、生效状态与展示键位；
/// PrimaryKey（首键）供同 context 冲突自检（§6.5）。Actions 以 "@" 开头为纯文档行（无对应 action）。
/// </summary>
public sealed record KeymapEntry
{
    /// <summary>共享同一键位语义的 InputMap action 组（多对一时合并为一行）。</summary>
    public required string[] Actions { get; init; }
    public required string Keys { get; init; }
    public required string Description { get; init; }
    /// <summary>生效状态（§4.2.3 context）；空数组 = 全局 ALL。</summary>
    public GameState[] Contexts { get; init; } = [];
    public required string Category { get; init; }
    /// <summary>L2 浮层（帮助/存档菜单）打开时仍可用（如 F1、Esc 关浮层）。</summary>
    public bool AllowWhenOverlay { get; init; }
    /// <summary>浮层专属行（Esc/F1 关闭浮层）：不参与状态作用域的路由与冲突检测，只在浮层打开时生效。</summary>
    public bool OverlayOnly { get; init; }

    public string PrimaryKey => Keys.Split(" / ")[0].Trim();
    public bool Global => Contexts.Length == 0;
}

public static class KeymapTable
{
    public static readonly KeymapEntry[] Entries =
    [
        // ---- 全局 ----
        new() { Actions = ["toggle_help"], Keys = "F1", Description = "快捷键帮助屏", Category = "全局", AllowWhenOverlay = true },
        new() { Actions = ["toggle_theme"], Keys = "T", Description = "明暗主题切换（M4 第二轮实装）", Category = "全局", AllowWhenOverlay = true },
        new() { Actions = ["toggle_mute"], Keys = "M", Description = "静音开关（M5 实装）", Category = "全局", AllowWhenOverlay = true },

        // ---- 标题 ----
        new() { Actions = ["confirm"], Keys = "Space / Enter", Description = "开始新对局（进行中的保留局会先确认）", Contexts = [GameState.Title], Category = "标题" },
        new() { Actions = ["load_latest"], Keys = "L", Description = "读最新存档", Contexts = [GameState.Title, GameState.Paused], Category = "标题" },

        // ---- 引导（两种入口）----
        new() { Actions = ["confirm"], Keys = "Space / Enter", Description = "开始游戏 / 回到对局（按入口）", Contexts = [GameState.GuideFromTitle, GameState.GuideFromGame], Category = "引导" },
        new() { Actions = ["ui_cancel"], Keys = "Esc", Description = "返回标题 / 回到对局（按入口）", Contexts = [GameState.GuideFromTitle, GameState.GuideFromGame], Category = "引导" },

        // ---- 对局 ----
        new() { Actions = ["pause"], Keys = "Esc / R", Description = "打开暂停菜单", Contexts = [GameState.Playing], Category = "对局" },
        new() { Actions = ["quick_save"], Keys = "Ctrl+S", Description = "快存到快存槽（Ctrl+L 读回）", Contexts = [GameState.Playing], Category = "对局" },
        new() { Actions = ["undo"], Keys = "Ctrl+Z", Description = "悔棋（PvE 自动回到人类回合）", Contexts = [GameState.Playing], Category = "对局" },
        new() { Actions = ["layer_prev", "layer_next"], Keys = "A / D", Description = "循环切换焦点层", Contexts = [GameState.Playing], Category = "对局" },
        new() { Actions = ["mirror_2d"], Keys = "F", Description = "2D 面板水平镜像", Contexts = [GameState.Playing], Category = "对局" },
        new() { Actions = ["toggle_helper_lines"], Keys = "H", Description = "辅助线开关", Contexts = [GameState.Playing], Category = "对局" },
        new() { Actions = ["toggle_colorblind"], Keys = "V", Description = "色弱配色", Contexts = [GameState.Playing], Category = "对局" },
        new() { Actions = ["@mouse_place"], Keys = "鼠标点击 2D 面板", Description = "落子（悬停显示辅助线）", Contexts = [GameState.Playing], Category = "对局" },

        // ---- 对局 + 复盘（相机，决策 #9）----
        new() { Actions = ["rotate_left", "rotate_right"], Keys = "Q / E", Description = "±90° 旋转", Contexts = [GameState.Playing, GameState.Replay], Category = "相机与视图" },
        new() { Actions = ["zoom_in", "zoom_out"], Keys = "W / S 或 ↑ / ↓", Description = "缩放", Contexts = [GameState.Playing, GameState.Replay], Category = "相机与视图" },
        new() { Actions = ["@mouse_drag"], Keys = "左键拖 / 中键拖", Description = "无级旋转 / 视角平移", Contexts = [GameState.Playing, GameState.Replay], Category = "相机与视图" },
        new() { Actions = ["@mouse_wheel"], Keys = "滚轮", Description = "径向缩放", Contexts = [GameState.Playing, GameState.Replay], Category = "相机与视图" },
        new() { Actions = ["pan_left", "pan_right", "pan_up", "pan_down"], Keys = "Shift+WASD / Shift+方向键", Description = "视角平移", Contexts = [GameState.Playing, GameState.Replay], Category = "相机与视图" },
        new() { Actions = ["spacing_compress", "spacing_expand"], Keys = "Z / C", Description = "层间距压缩 / 拉大", Contexts = [GameState.Playing, GameState.Replay], Category = "相机与视图" },
        new() { Actions = ["spacing_reset"], Keys = "X", Description = "层间距复位", Contexts = [GameState.Playing, GameState.Replay], Category = "相机与视图" },
        new() { Actions = ["toggle_grid_all"], Keys = "O", Description = "网格全亮开关", Contexts = [GameState.Playing, GameState.Replay], Category = "相机与视图" },

        // ---- 复盘 ----
        new() { Actions = ["replay_prev", "replay_next"], Keys = "← / →", Description = "步进出 / 入", Contexts = [GameState.Replay], Category = "复盘" },
        new() { Actions = ["exit_replay"], Keys = "Esc / R", Description = "退出复盘（回标题，棋局清理）", Contexts = [GameState.Replay], Category = "复盘" },

        // ---- 暂停（选项旁键位提示与源项目 esc-menu 对齐：继续 Esc/重开 R/保存 S/加载 L/说明 G/标题 B；
        //      读档按钮打开存档菜单（无直接键）；L 键仍是读最新（源语义））----
        new() { Actions = ["resume"], Keys = "Esc", Description = "继续游戏", Contexts = [GameState.Paused], Category = "暂停" },
        new() { Actions = ["restart"], Keys = "R", Description = "重新开始（确认框）", Contexts = [GameState.Paused], Category = "暂停" },
        new() { Actions = ["save_first_empty"], Keys = "S", Description = "保存进度（首个空槽，满则快存槽）", Contexts = [GameState.Paused, GameState.GuideFromGame], Category = "暂停" },
        new() { Actions = ["quick_load"], Keys = "Ctrl+L", Description = "读取快存槽（恢复检查点，对局中 Esc 后可用）", Contexts = [GameState.Paused], Category = "暂停" },
        new() { Actions = ["return_to_title"], Keys = "B", Description = "返回标题（对局保留现场）", Contexts = [GameState.Paused], Category = "暂停" },
        new() { Actions = ["open_guide"], Keys = "G", Description = "打开引导", Contexts = [GameState.Paused], Category = "暂停" },

        // ---- 浮层（帮助/存档菜单打开时，OverlayOnly：只在浮层作用域生效）----
        new() { Actions = ["ui_cancel"], Keys = "Esc", Description = "关闭当前浮层", Category = "浮层", AllowWhenOverlay = true, OverlayOnly = true },
        new() { Actions = ["toggle_help"], Keys = "F1", Description = "关闭帮助屏", Category = "浮层", AllowWhenOverlay = true, OverlayOnly = true },
    ];

    /// <summary>§4.2.4：action 在 state 下是否激活。未登记 action 放行（由调用方自管，如 ui_cancel 分支）；
    /// OverlayOnly 行不走状态路由（浮层作用域由 Main 的逐层冒泡分支处理）。</summary>
    public static bool Allows(string action, GameState state)
    {
        foreach (var entry in Entries)
        {
            if (entry.OverlayOnly || !entry.Actions.Contains(action))
            {
                continue;
            }
            return entry.Global || entry.Contexts.Contains(state);
        }
        return true;
    }

    /// <summary>浮层（帮助/存档菜单）打开时仍可用的 action（逐层冒泡：浮层只响应这些键）。
    /// 同 action 多行时取"任一允许"（如 ui_cancel 的引导行与浮层行）。</summary>
    public static bool AllowsWhenOverlayOpen(string action) =>
        Entries.Any(e => e.Actions.Contains(action) && e.AllowWhenOverlay);

    /// <summary>帮助屏数据：按类别分组（保持声明顺序）。</summary>
    public static IEnumerable<(string Category, KeymapEntry[] Rows)> ByCategory() =>
        Entries
            .GroupBy(e => e.Category)
            .Select(g => (g.Key, g.ToArray()));

    /// <summary>菜单按钮键位提示用：按 action 查首条非纯文档、非浮层行，取其 PrimaryKey
    /// （如 PauseMenu 各选项旁的 kbd 字样，与源 esc-menu 对齐）。</summary>
    public static string? HintKeyOf(string action) =>
        Entries.FirstOrDefault(e => !e.OverlayOnly && e.Actions.Contains(action))?.PrimaryKey;

    /// <summary>同一作用域内不同绑定主键冲突检测（§6.5）；返回违规描述，空 = 无冲突。
    /// 两个作用域分别检测：状态作用域（无浮层）与浮层作用域（OverlayOnly 行）。</summary>
    public static List<string> FindConflicts()
    {
        var violations = new List<string>();
        void Check(IEnumerable<KeymapEntry> scope, string scopeName, GameState? state)
        {
            var applicable = scope
                .Where(e => !e.Actions.Any(a => a.StartsWith('@')))
                .GroupBy(e => e.PrimaryKey)
                .Where(g => g.Count() > 1);
            foreach (var group in applicable)
            {
                var who = string.Join("、", group.SelectMany(e => e.Actions).Distinct());
                violations.Add($"{scopeName}{(state.HasValue ? $"[{state}]" : "")}: 键 [{group.Key}] 被 {who} 同时声明");
            }
        }
        var stateScope = Entries.Where(e => !e.OverlayOnly);
        foreach (GameState state in Enum.GetValues<GameState>())
        {
            Check(stateScope.Where(e => e.Global || e.Contexts.Contains(state)), "状态", state);
        }
        Check(Entries.Where(e => e.OverlayOnly), "浮层", null);
        return violations;
    }
}
