namespace Nmoku.Core;

/// <summary>规则引擎策略接口（04-contracts §2.1）。</summary>
public interface IRuleEngine
{
    /// <summary>纯读查询：刚落于 (x,y,z) 的 player 之子是否形成 winLength+ 连珠。
    /// 越界或空位返回 false。</summary>
    bool CheckWin(int x, int y, int z, PlayerColor player, Board board);

    /// <summary>基础合法性 = 界内且空位（02-rules §2.3），各规则集可加约束。</summary>
    bool IsLegalMove(int x, int y, int z, PlayerColor player,
        IReadOnlyList<Move> history, Board board);

    /// <summary>
    /// 返回下一回合状态（04-contracts §2.1.3）。调用约定（源 main.ts:341，测试锁定）：
    /// <paramref name="currentPlayer"/> 是<b>刚行棋方</b>，不是下一行动方。
    /// 标准分支 NextPlayer = 刚行棋方的对手；swap 分支（SwapEngine）NextPlayer = 刚行棋方本身——
    /// 决策期指针不动，由 Message 受众（白方）弹窗决策，shell 据此决定是否换色；
    /// 决策后仍由刚行棋方落第 2 子。
    /// </summary>
    TurnState GetNextTurnState(IReadOnlyList<Move> history, PlayerColor currentPlayer,
        LevelConfig? levelConfig = null);
}
