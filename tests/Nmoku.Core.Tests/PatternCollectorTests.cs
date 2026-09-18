namespace Nmoku.Core.Tests;

/// <summary>02-rules §5：单层去重收集，供辅助线"一份数据、两个渲染器"。</summary>
public class PatternCollectorTests
{
    [Fact]
    public void Collect_ThreeInRow_ReturnsSingleDedupedPattern()
    {
        var board = new Board(9, 2);
        board.Set(2, 4, 0, CellState.Black);
        board.Set(3, 4, 0, CellState.Black);
        board.Set(4, 4, 0, CellState.Black);

        var patterns = PatternCollector.Collect(board, 0);

        var horizontal = patterns.Single(p => p.Dx == 1 && p.Dy == 0);
        Assert.Equal(3, horizontal.Count);
        Assert.Equal(2, horizontal.OpenEnds);
        Assert.True(horizontal.Alive);
        // 起点去重：整盘同一条横三只被收集一次
        Assert.DoesNotContain(patterns, p => p.Dx == 1 && p.Dy == 0 && p.Count == 2);
    }

    [Fact]
    public void Collect_IsolatedStone_NoPatternFromIt()
    {
        var board = new Board(9, 2);
        board.Set(4, 4, 0, CellState.White);

        Assert.DoesNotContain(PatternCollector.Collect(board, 0), p => p.Cells.Count == 1);
    }

    [Fact]
    public void Collect_TwoOpposingRuns_KeepsColorsApart()
    {
        var board = new Board(9, 1);
        board.Set(1, 1, 0, CellState.Black);
        board.Set(2, 1, 0, CellState.Black);
        board.Set(5, 4, 0, CellState.White);
        board.Set(5, 5, 0, CellState.White);

        var patterns = PatternCollector.Collect(board, 0);

        Assert.Contains(patterns, p => p.Count == 2 && p.Dx == 1 && p.Dy == 0);   // 横向黑二
        Assert.Contains(patterns, p => p.Count == 2 && p.Dx == 0 && p.Dy == 1);   // 纵向白二
    }

    [Fact]
    public void Collect_EmptyLayer_ReturnsNothing()
    {
        Assert.Empty(PatternCollector.Collect(new Board(9, 2), 1));
    }

    [Fact]
    public void Collect_EndpointsMatchRunExtent()
    {
        // 白连子贴左边界：负向 openEnd 应为 0（出界非空位）
        var board = new Board(9, 1);
        board.Set(0, 0, 0, CellState.White);
        board.Set(1, 0, 0, CellState.White);

        var pattern = PatternCollector.Collect(board, 0).Single(p => p.Dx == 1);
        Assert.Equal(1, pattern.OpenEnds);
        Assert.Equal(new Coordinate3D(0, 0, 0), pattern.Cells[0]);
        Assert.Equal(new Coordinate3D(1, 0, 0), pattern.Cells[^1]);
    }
}
