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

    /// <summary>返回下一回合状态；nextPlayer 恒为 Black/White 之一（04-contracts §2.1.3）。</summary>
    TurnState GetNextTurnState(IReadOnlyList<Move> history, PlayerColor currentPlayer,
        LevelConfig? levelConfig = null);
}
