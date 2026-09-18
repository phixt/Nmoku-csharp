namespace Nmoku.Core.Tests;

/// <summary>01-core §1 + test-and-cicd-plan §4.1 清单。</summary>
public class BoardTests
{
    [Fact]
    public void DefaultConstructor_Is13x13x6_AllEmpty()
    {
        var board = new Board();
        Assert.Equal(13, board.BoardSize);
        Assert.Equal(6, board.Layers);
        for (int z = 0; z < 6; z++)
            for (int y = 0; y < 13; y++)
                for (int x = 0; x < 13; x++)
                    Assert.Equal(CellState.Empty, board.Get(x, y, z));
    }

    [Fact]
    public void SetGet_HighestValidIndex_RoundTrips()
    {
        var board = new Board();
        board.Set(12, 12, 5, CellState.White);
        Assert.Equal(CellState.White, board.Get(12, 12, 5));
    }

    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(13, 0, 0)]
    [InlineData(0, 13, 0)]
    [InlineData(0, 0, 6)]
    public void Get_OutOfBounds_ReturnsEmpty(int x, int y, int z)
    {
        var board = new Board();
        board.Set(0, 0, 0, CellState.Black); // 保证不是"全空所以碰巧对"
        Assert.Equal(CellState.Empty, board.Get(x, y, z));
    }

    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(13, 0, 0)]
    [InlineData(0, 13, 0)]
    [InlineData(0, 0, 6)]
    public void Set_OutOfBounds_IsIgnored(int x, int y, int z)
    {
        var board = new Board();
        board.Set(x, y, z, CellState.Black);
        Assert.Equal(0, board.MoveCount());
    }

    [Fact]
    public void Index_ZIsPlaneMajor_NoCrossTalk()
    {
        var board = new Board();
        board.Set(0, 0, 0, CellState.Black);
        board.Set(0, 0, 5, CellState.White);
        Assert.Equal(CellState.Black, board.Get(0, 0, 0));
        Assert.Equal(CellState.White, board.Get(0, 0, 5));
        Assert.Equal(0, board.Index(0, 0, 0));
        Assert.Equal(5 * 169 + 3 * 13 + 7, board.Index(7, 3, 5));
    }

    [Fact]
    public void Reset_ClearsAllCells()
    {
        var board = new Board(9, 2);
        board.Set(1, 2, 0, CellState.Black);
        board.Set(8, 8, 1, CellState.White);
        board.Reset();
        Assert.Equal(0, board.MoveCount());
    }

    [Fact]
    public void ForEach_VisitsEveryCellExactlyOnce()
    {
        var board = new Board(9, 4);
        int visits = 0;
        board.ForEach((x, y, z, _) => visits++);
        Assert.Equal(9 * 9 * 4, visits);
    }

    [Fact]
    public void CustomSize_9x4_BoundsChange()
    {
        var board = new Board(9, 4);
        Assert.True(board.InBounds(8, 8, 3));
        Assert.False(board.InBounds(9, 0, 0));
        Assert.False(board.InBounds(0, 9, 0));
        Assert.False(board.InBounds(0, 0, 4));
        board.Set(9, 0, 0, CellState.Black); // 越界忽略
        Assert.Equal(0, board.MoveCount());
    }

    [Fact]
    public void CountPieces_And_DetectCurrentPlayer_FollowsDoc()
    {
        var board = new Board();
        Assert.Equal(PlayerColor.Black, board.DetectCurrentPlayer()); // 0:0 先黑
        board.Set(0, 0, 0, PlayerColor.Black);
        board.Set(1, 1, 0, PlayerColor.Black);
        Assert.Equal((2, 0), board.CountPieces());
        Assert.Equal(PlayerColor.White, board.DetectCurrentPlayer()); // 黑多轮白
    }
}
