namespace Nmoku.Core;

/// <summary>标准gomoku/自由规则（02-rules §2.4 StandardEngine）：严格轮流，无特殊阶段。</summary>
public sealed class StandardEngine : IRuleEngine
{
    private readonly int _winLength;

    public StandardEngine(int winLength = GameConfig.WinLength)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(winLength, 1);
        _winLength = winLength;
    }

    public bool CheckWin(int x, int y, int z, PlayerColor player, Board board) =>
        Rules.CheckWinner(board, x, y, z, _winLength) == player.ToCell();

    public bool IsLegalMove(int x, int y, int z, PlayerColor player,
        IReadOnlyList<Move> history, Board board) =>
        board.InBounds(x, y, z) && board.IsEmpty(x, y, z);

    public TurnState GetNextTurnState(IReadOnlyList<Move> history, PlayerColor currentPlayer,
        LevelConfig? levelConfig = null) =>
        new(currentPlayer.Opponent(), false, "");
}
