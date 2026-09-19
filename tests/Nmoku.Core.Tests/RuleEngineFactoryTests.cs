namespace Nmoku.Core.Tests;

/// <summary>RuleSet → 引擎映射单点（M3 接线前置），含 winLength 绑定与未实装规则快速暴露。</summary>
public class RuleEngineFactoryTests
{
    [Fact]
    public void Create_Gomoku_ReturnsStandardEngineWithWinLengthBound()
    {
        var engine = RuleEngineFactory.Create(RuleSet.Gomoku, winLength: 4);
        var board = new Board();
        for (int i = 0; i < 4; i++)
        {
            board.Set(i, 0, 0, CellState.Black);
        }

        Assert.IsType<StandardEngine>(engine);
        Assert.True(engine.CheckWin(3, 0, 0, PlayerColor.Black, board)); // 4 连即胜：winLength=4 已生效
    }

    [Fact]
    public void Create_Swap2_ReturnsSwapEngineWithSwapPhase()
    {
        var engine = RuleEngineFactory.Create(RuleSet.Swap2);
        var history = new List<Move> { new(6, 6, 0, PlayerColor.Black) };

        Assert.IsType<SwapEngine>(engine);
        Assert.True(engine.GetNextTurnState(history, PlayerColor.Black).IsSwapPhase);
    }

    [Fact]
    public void Create_Renju_ThrowsNotSupported()
    {
        Assert.Throws<NotSupportedException>(() => RuleEngineFactory.Create(RuleSet.Renju));
    }

    [Fact]
    public void Create_UndefinedRuleSet_ThrowsArgumentOutOfRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RuleEngineFactory.Create((RuleSet)999));
    }
}
