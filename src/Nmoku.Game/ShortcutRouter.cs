using Nmoku.Core;

namespace Nmoku.Game;

/// <summary>
/// 快捷键 context 门控（04-contracts §4.2.3/§4.2.4）。
/// 规则本体已收敛到 Core KeymapTable（单一数据源：门控/帮助屏/文档同源，冲突检测有 Core 单测）。
/// </summary>
public static class ShortcutRouter
{
    /// <summary>action 在当前状态下是否生效；未登记 action 放行（由调用方自管）。</summary>
    public static bool Allows(string action, GameState state) => KeymapTable.Allows(action, state);
}
