namespace Nmoku.Core;

/// <summary>
/// 键帽作用域层（F1 快捷键页，2026-09-25 人类拍板规则）：功能由颜色区分、不叠层——
/// 每枚键帽只有一种作用域色。基础三层 游戏/复盘/暂停；复合键的修饰键（Ctrl/Shift）各自单独成层；
/// 全局键（T/M/F1）中性层。"对局+复盘"双域等重叠场景取主域（含 Playing 即 Game），不做渐变混合；
/// 同键跨层冲突的少量例外（Esc/R/S）由视图侧上下分层小字提示，不改本分类。
/// </summary>
public enum KeycapScope
{
    Game,
    Replay,
    PauseMenu,
    Ctrl,
    Shift,
    Global,
}

public static class KeycapScopeMap
{
    /// <summary>按 KeymapEntry 派生键帽层：修饰键优先（Ctrl/Shift 层），其次全局行，再按主域
    /// （含 Playing=Game；否则含 Replay=Replay；Title/Paused/Guide 归 PauseMenu）。</summary>
    public static KeycapScope ScopeOf(KeymapEntry entry)
    {
        string pk = entry.PrimaryKey;
        if (pk.StartsWith("Ctrl+", StringComparison.OrdinalIgnoreCase))
        {
            return KeycapScope.Ctrl;
        }
        if (pk.StartsWith("Shift+", StringComparison.OrdinalIgnoreCase))
        {
            return KeycapScope.Shift;
        }
        if (entry.Global || (entry.AllowWhenOverlay && !entry.OverlayOnly))
        {
            return KeycapScope.Global;
        }
        if (entry.Contexts.Contains(GameState.Playing))
        {
            return KeycapScope.Game;
        }
        if (entry.Contexts.Contains(GameState.Replay))
        {
            return KeycapScope.Replay;
        }
        return KeycapScope.PauseMenu;
    }

    /// <summary>图例文案（顺序即枚举声明序，视图图例与说明列表共用）。</summary>
    public static string DisplayName(KeycapScope scope) => scope switch
    {
        KeycapScope.Game => "对局",
        KeycapScope.Replay => "复盘",
        KeycapScope.PauseMenu => "暂停 / 菜单",
        KeycapScope.Ctrl => "Ctrl 组合",
        KeycapScope.Shift => "Shift 组合",
        KeycapScope.Global => "全局",
        _ => throw new ArgumentOutOfRangeException(nameof(scope)),
    };
}
