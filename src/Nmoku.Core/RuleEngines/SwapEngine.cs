namespace Nmoku.Core;

/// <summary>
/// SWAP2 开局规则引擎（02-rules §2.4/§3）：继承标准胜负与合法性，
/// 黑首子后由白决定"交换/继续"；带初始预置子的关卡永不进入交换阶段。
/// </summary>
public sealed class SwapEngine : IRuleEngine
{
    private readonly int _winLength;

    public SwapEngine(int winLength = GameConfig.WinLength) => _winLength = winLength;

    public bool CheckWin(int x, int y, int z, PlayerColor player, Board board) =>
        Rules.CheckWinner(board, x, y, z, _winLength) == player.ToCell();

    public bool IsLegalMove(int x, int y, int z, PlayerColor player,
        IReadOnlyList<Move> history, Board board) =>
        board.InBounds(x, y, z) && board.IsEmpty(x, y, z);

    /// <summary>
    /// swap 分支（history 恰 1 手且无预置子）返回 NextPlayer=刚行棋方、IsSwapPhase=true；
    /// 其余分支为刚行棋方的对手（02-rules §2.4/§3）。
    /// </summary>
    public TurnState GetNextTurnState(IReadOnlyList<Move> history, PlayerColor currentPlayer,
        LevelConfig? levelConfig = null)
    {
        // 预置子守卫（00-domain §4.7）：残局/教学/读档开局一律跳过 swap 阶段
        if (levelConfig is { InitialMoves.Count: > 0 })
        {
            return new TurnState(currentPlayer.Opponent(), false, "");
        }

        if (history.Count == 1)
        {
            // swap 决策期：nextPlayer=刚行棋方（指针不动）；决策者是 Message 受众（白方），
            // 由 shell 弹窗收集并决定是否 ApplySwap；决策后仍由刚行棋方落第 2 子（源 main.ts:341）
            return new TurnState(currentPlayer, true, Text.Swap.ChooseSwapOrDefault);
        }

        return new TurnState(currentPlayer.Opponent(), false, "");
    }

    /// <summary>
    /// 执行交换（02-rules §2.4 applySwap）：历史内所有 Move 原地换色，
    /// 棋盘按换色后的历史重建；不增删历史条目。
    /// </summary>
    public static void ApplySwap(IList<Move> history, Board board)
    {
        foreach (Move move in history)
        {
            move.Player = move.Player.Opponent();
        }
        board.Reset();
        foreach (Move move in history)
        {
            board.Set(move.X, move.Y, move.Z, move.Player.ToCell());
        }
    }
}
