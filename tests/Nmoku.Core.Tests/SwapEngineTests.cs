namespace Nmoku.Core.Tests;

/// <summary>test-and-cicd-plan §4.4：SWAP2 开局状态机。调用约定（读源自 main.ts:341）：
/// getNextTurnState 接收"刚行棋方"；swap 分支下 nextPlayer=刚行棋方且 shell 保持不变，
/// 由黑连下两手——与官方 Swap2 节奏一致。</summary>
public class SwapEngineTests
{
    private static readonly LevelConfig EmptyLevel = new() { Id = "sandbox" };

    [Fact]
    public void GetNextTurnState_AfterFirstMove_EntersSwapPhaseWithSamePlayer()
    {
        var engine = new SwapEngine();
        var history = new List<Move> { new(6, 6, 0, PlayerColor.Black) };

        var ts = engine.GetNextTurnState(history, PlayerColor.Black, EmptyLevel);

        Assert.True(ts.IsSwapPhase);
        Assert.Equal(PlayerColor.Black, ts.NextPlayer);
        Assert.NotEmpty(ts.Message);
    }

    [Fact]
    public void GetNextTurnState_WithPrePlacedPieces_NeverEntersSwapPhase()
    {
        var engine = new SwapEngine();
        var level = new LevelConfig
        {
            Id = "residual",
            InitialMoves = [new Move(2, 3, 0, PlayerColor.White)],
        };
        var history = new List<Move> { new(6, 6, 0, PlayerColor.Black) };

        var ts = engine.GetNextTurnState(history, PlayerColor.Black, level);

        Assert.False(ts.IsSwapPhase);
        Assert.Equal(PlayerColor.White, ts.NextPlayer);
    }

    [Fact]
    public void GetNextTurnState_AfterSecondMove_ResumesStrictAlternation()
    {
        var engine = new SwapEngine();
        var history = new List<Move>
        {
            new(6, 6, 0, PlayerColor.Black),
            new(7, 7, 0, PlayerColor.Black),
        };

        var ts = engine.GetNextTurnState(history, PlayerColor.Black, EmptyLevel);

        Assert.False(ts.IsSwapPhase);
        Assert.Equal(PlayerColor.White, ts.NextPlayer);
    }

    [Fact]
    public void ApplySwap_InvertsColorsPositionsAndCountPreserved()
    {
        var board = new Board(9, 3);
        var history = new List<Move>
        {
            new(4, 4, 1, PlayerColor.Black),
            new(5, 5, 1, PlayerColor.White),
        };
        board.Set(4, 4, 1, CellState.Black);
        board.Set(5, 5, 1, CellState.White);

        SwapEngine.ApplySwap(history, board);

        Assert.Equal(2, history.Count);
        Assert.Equal(PlayerColor.White, history[0].Player);
        Assert.Equal(PlayerColor.Black, history[1].Player);
        Assert.Equal(CellState.White, board.Get(4, 4, 1));
        Assert.Equal(CellState.Black, board.Get(5, 5, 1));
    }

    [Fact]
    public void FullSwapFlow_SwapBranch_ColorsExchangeAndGameContinues()
    {
        var engine = new SwapEngine();
        var board = new Board(9, 2);
        var history = new List<Move>();
        var current = PlayerColor.Black;

        // 黑首子
        board.Set(4, 4, 0, current);
        history.Add(new Move(4, 4, 0, current));
        var ts = engine.GetNextTurnState(history, current, EmptyLevel);
        Assert.True(ts.IsSwapPhase);

        // White 选择交换：全盘换色
        SwapEngine.ApplySwap(history, board);
        Assert.Equal(CellState.White, board.Get(4, 4, 0));

        // 交换后节奏：黑色（原白方持有）连下两手
        current = ts.NextPlayer; // Black
        Assert.True(engine.IsLegalMove(3, 3, 0, current, history, board));
        board.Set(3, 3, 0, current);
        history.Add(new Move(3, 3, 0, current));
        ts = engine.GetNextTurnState(history, current, EmptyLevel);
        Assert.False(ts.IsSwapPhase);
        Assert.Equal(PlayerColor.White, ts.NextPlayer);
    }

    [Fact]
    public void FullSwapFlow_PassBranch_KeepsColorsAndTurns()
    {
        var engine = new SwapEngine();
        var board = new Board(9, 2);
        var history = new List<Move> { new(4, 4, 0, PlayerColor.Black) };
        board.Set(4, 4, 0, CellState.Black);

        var ts = engine.GetNextTurnState(history, PlayerColor.Black, EmptyLevel);
        Assert.True(ts.IsSwapPhase);

        // Pass：不换色，shell 保持 currentPlayer，仍由黑落第 2 手（官方 Swap2 节奏）
        Assert.Equal(CellState.Black, board.Get(4, 4, 0));
        Assert.Equal(PlayerColor.Black, ts.NextPlayer);

        board.Set(5, 5, 0, CellState.Black);
        history.Add(new Move(5, 5, 0, PlayerColor.Black));
        ts = engine.GetNextTurnState(history, PlayerColor.Black, EmptyLevel);
        Assert.Equal(PlayerColor.White, ts.NextPlayer);
    }

    [Fact]
    public void IsLegalMove_DuringPlacement_RejectsOccupiedAndOutOfBounds()
    {
        var engine = new SwapEngine();
        var board = new Board();
        board.Set(6, 6, 0, CellState.Black);
        Assert.False(engine.IsLegalMove(6, 6, 0, PlayerColor.White, [], board));
        Assert.False(engine.IsLegalMove(99, 0, 0, PlayerColor.White, [], board));
        Assert.True(engine.IsLegalMove(7, 6, 0, PlayerColor.White, [], board));
    }
}
