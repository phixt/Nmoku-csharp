namespace Nmoku.Core;

/// <summary>
/// RuleSet → IRuleEngine 实例映射（M3 GameManager 接线单点，避免 Game 侧 if-else 散落）。
/// 新规则引擎在此登记；TODO 远期挂账：Connect6（需接口演进）、Renju 禁手。
/// </summary>
public static class RuleEngineFactory
{
    /// <summary>创建与规则集绑定 winLength 的引擎。</summary>
    public static IRuleEngine Create(RuleSet rules, int winLength = GameConfig.WinLength) => rules switch
    {
        RuleSet.Gomoku => new StandardEngine(winLength),
        RuleSet.Swap2 => new SwapEngine(winLength),
        RuleSet.Renju => throw new NotSupportedException("Renju 禁手引擎未实装（TODO 远期挂账，优先级低于 Connect6）"),
        _ => throw new ArgumentOutOfRangeException(nameof(rules), rules, "未知规则集"),
    };
}
