namespace Nmoku.Core.Tests;

/// <summary>04-contracts §6.1 IRuleEngine 合规清单 + test plan §4.4。</summary>
public class RuleEngineTests
{
    [Theory]
    [MemberData(nameof(Engines))]
    public void CheckWin_OutOfBounds_ReturnsFalse(IRuleEngine engine)
    {
        var board = new Board();
        Assert.False(engine.CheckWin(-1, 0, 0, PlayerColor.Black, board));
        Assert.False(engine.CheckWin(13, 0, 0, PlayerColor.Black, board));
        Assert.False(engine.CheckWin(0, 0, 99, PlayerColor.Black, board));
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public void CheckWin_EmptyCell_ReturnsFalse(IRuleEngine engine)
    {
        var board = new Board();
        Assert.False(engine.CheckWin(5, 5, 0, PlayerColor.Black, board));
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public void CheckWin_All13Directions_IdentifyWins(IRuleEngine engine)
    {
        foreach (var dir in Rules.Directions)
        {
            // 起点按方向符号取反向角落，保证 5 子连珠完整落在 13×13×6 界内
            var origin = new Coordinate3D(
                dir.X < 0 ? 4 : 0,
                dir.Y < 0 ? 4 : 0,
                dir.Z < 0 ? 4 : 0);
            var fresh = new Board();
            for (int i = 0; i < 5; i++)
            {
                fresh.Set(origin.X + i * dir.X, origin.Y + i * dir.Y, origin.Z + i * dir.Z,
                    CellState.Black);
            }
            Assert.True(engine.CheckWin(origin.X + 4 * dir.X, origin.Y + 4 * dir.Y,
                    origin.Z + 4 * dir.Z, PlayerColor.Black, fresh),
                $"方向 ({dir.X},{dir.Y},{dir.Z}) 五连应判胜");
        }
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public void IsLegalMove_OccupiedCell_Rejected(IRuleEngine engine)
    {
        var board = new Board();
        board.Set(3, 3, 0, CellState.Black);
        Assert.False(engine.IsLegalMove(3, 3, 0, PlayerColor.White, [], board));
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public void IsLegalMove_OutOfBounds_Rejected(IRuleEngine engine)
    {
        var board = new Board();
        Assert.False(engine.IsLegalMove(-1, 0, 0, PlayerColor.Black, [], board));
        Assert.False(engine.IsLegalMove(0, 0, 6, PlayerColor.Black, [], board));
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public void GetNextTurnState_NeverReturnsInvalidPlayer(IRuleEngine engine)
    {
        var history = new List<Move> { new(6, 6, 0, PlayerColor.Black) };
        foreach (var current in new[] { PlayerColor.Black, PlayerColor.White })
        {
            var ts = engine.GetNextTurnState(history, current);
            Assert.True(ts.NextPlayer is PlayerColor.Black or PlayerColor.White);
        }
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public void GetNextTurnState_TriggersSwap2OnlyWhenAppropriate(IRuleEngine engine)
    {
        // 恰好 1 手 + 无预置子 → swap
        var oneMove = new List<Move> { new(6, 6, 0, PlayerColor.Black) };
        Assert.Equal(engine is SwapEngine, engine.GetNextTurnState(oneMove, PlayerColor.Black).IsSwapPhase);
        // 2 手 → 不 swap
        oneMove.Add(new Move(7, 7, 0, PlayerColor.White));
        Assert.False(engine.GetNextTurnState(oneMove, PlayerColor.White).IsSwapPhase);
        // 有预置子 → 恒不 swap
        var level = new LevelConfig { Id = "residual", InitialMoves = [new Move(1, 1, 0, PlayerColor.Black)] };
        var fresh = new List<Move> { new(6, 6, 0, PlayerColor.Black) };
        Assert.False(engine.GetNextTurnState(fresh, PlayerColor.Black, level).IsSwapPhase);
    }

    public static TheoryData<IRuleEngine> Engines => new()
    {
        new StandardEngine(),
        new SwapEngine(),
    };
}
