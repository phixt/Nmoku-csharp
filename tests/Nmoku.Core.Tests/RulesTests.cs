namespace Nmoku.Core.Tests;

/// <summary>02-rules §1 + test-and-cicd-plan §4.2 —— 胜负判定，重中之重。</summary>
public class RulesTests
{
    private static Board BoardWith(params (int x, int y, int z, CellState s)[] stones)
    {
        var board = new Board();
        foreach (var (x, y, z, s) in stones)
        {
            board.Set(x, y, z, s);
        }
        return board;
    }

    [Fact]
    public void Directions_Has13Entries_MatchingDocOrder()
    {
        Assert.Equal(13, Rules.Directions.Length);
        Assert.Equal(new Coordinate3D(1, 0, 0), Rules.Directions[0]);
        Assert.Equal(new Coordinate3D(0, 1, 0), Rules.Directions[1]);
        Assert.Equal(new Coordinate3D(0, 0, 1), Rules.Directions[2]);
        Assert.Equal(new Coordinate3D(1, 1, 1), Rules.Directions[9]);
        Assert.Equal(new Coordinate3D(1, -1, -1), Rules.Directions[12]);
    }

    [Fact]
    public void CheckWinner_HorizontalFive_ReturnsBlack()
    {
        var board = BoardWith((0, 7, 2, CellState.Black), (1, 7, 2, CellState.Black),
            (2, 7, 2, CellState.Black), (3, 7, 2, CellState.Black), (4, 7, 2, CellState.Black));
        Assert.Equal(CellState.Black, Rules.CheckWinner(board, 2, 7, 2));
    }

    [Fact]
    public void CheckWinner_VerticalFive_ReturnsBlack()
    {
        var board = BoardWith((7, 0, 2, CellState.Black), (7, 1, 2, CellState.Black),
            (7, 2, 2, CellState.Black), (7, 3, 2, CellState.Black), (7, 4, 2, CellState.Black));
        Assert.Equal(CellState.Black, Rules.CheckWinner(board, 7, 4, 2));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CheckWinner_BothPlanarDiagonals_ReturnWhite(bool mainDiagonal)
    {
        var board = new Board();
        for (int i = 0; i < 5; i++)
        {
            int y = mainDiagonal ? i : 4 - i;
            board.Set(i, y, 2, CellState.White);
        }
        // 两条平面对角线都穿过中心 (2,2)
        Assert.Equal(CellState.White, Rules.CheckWinner(board, 2, 2, 2));
    }

    [Fact]
    public void CheckWinner_CrossLayerSpaceDiagonal_ReturnsBlack()
    {
        // (0,0,0)→(4,4,4)：3D 五子棋独有的体对角线
        var board = new Board();
        for (int i = 0; i < 5; i++)
        {
            board.Set(i, i, i, CellState.Black);
        }
        Assert.Equal(CellState.Black, Rules.CheckWinner(board, 4, 4, 4));
    }

    [Fact]
    public void CheckWinner_ThroughLayerColumn_ReturnsWhite()
    {
        // z 向直连 (3,3,0..4)
        var board = new Board();
        for (int z = 0; z < 5; z++)
        {
            board.Set(3, 3, z, CellState.White);
        }
        Assert.Equal(CellState.White, Rules.CheckWinner(board, 3, 3, 0));
    }

    [Fact]
    public void CheckWinner_CrossLayerFaceDiagonalXz_ReturnsBlack()
    {
        // (1,0,-1) 面-diagonal：x+1 且 z-1
        var board = new Board();
        for (int i = 0; i < 5; i++)
        {
            board.Set(2 + i, 6, 4 - i, CellState.Black);
        }
        Assert.Equal(CellState.Black, Rules.CheckWinner(board, 4, 6, 2));
    }

    [Fact]
    public void CheckWinner_OnlyFour_ReturnsEmpty()
    {
        var board = BoardWith((0, 0, 0, CellState.Black), (1, 0, 0, CellState.Black),
            (2, 0, 0, CellState.Black), (3, 0, 0, CellState.Black));
        Assert.Equal(CellState.Empty, Rules.CheckWinner(board, 3, 0, 0));
    }

    [Fact]
    public void CheckWinner_BlockedByOpponent_ReturnsEmpty()
    {
        // B B B W B —— 中间被隔断
        var board = BoardWith((0, 5, 1, CellState.Black), (1, 5, 1, CellState.Black),
            (2, 5, 1, CellState.Black), (3, 5, 1, CellState.White), (4, 5, 1, CellState.Black));
        Assert.Equal(CellState.Empty, Rules.CheckWinner(board, 4, 5, 1));
    }

    [Fact]
    public void CheckWinner_SixInRow_StillWins()
    {
        var board = new Board();
        for (int i = 0; i < 6; i++)
        {
            board.Set(i, 2, 3, CellState.White);
        }
        Assert.Equal(CellState.White, Rules.CheckWinner(board, 5, 2, 3));
    }

    [Fact]
    public void CheckWinner_CustomWinLength7_FiveNotEnoughSevenWins()
    {
        var board = new Board();
        for (int i = 0; i < 5; i++)
        {
            board.Set(i, 0, 0, CellState.Black);
        }
        Assert.Equal(CellState.Empty, Rules.CheckWinner(board, 4, 0, 0, winLength: 7));

        board.Set(5, 0, 0, CellState.Black);
        board.Set(6, 0, 0, CellState.Black);
        Assert.Equal(CellState.Black, Rules.CheckWinner(board, 6, 0, 0, winLength: 7));
    }

    [Fact]
    public void CheckWinner_EmptyCell_ReturnsEmpty()
    {
        var board = new Board();
        Assert.Equal(CellState.Empty, Rules.CheckWinner(board, 5, 5, 0));
    }

    [Fact]
    public void CheckWinner_OutOfBounds_ReturnsEmpty()
    {
        var board = new Board();
        Assert.Equal(CellState.Empty, Rules.CheckWinner(board, -1, 0, 0));
        Assert.Equal(CellState.Empty, Rules.CheckWinner(board, 13, 0, 0));
    }

    [Fact]
    public void CheckWinner_EdgeColumn_OneEndExactlyOutOfBounds()
    {
        // 贴边列 (0,0,0..4)：正向往外一步即出界，扫描不得越界读崩
        var board = new Board();
        for (int z = 0; z < 5; z++)
        {
            board.Set(0, 0, z, CellState.Black);
        }
        Assert.Equal(CellState.Black, Rules.CheckWinner(board, 0, 0, 4));
    }

    [Fact]
    public void CheckPatterns3D_BothEndsOpen_IsAlive()
    {
        var board = BoardWith((3, 3, 0, CellState.Black), (4, 3, 0, CellState.Black),
            (5, 3, 0, CellState.Black));
        var patterns = Rules.CheckPatterns3D(board, 4, 3, 0);
        var horizontal = patterns.Single(p => p.Dx == 1 && p.Dy == 0 && p.Dz == 0);
        Assert.Equal(3, horizontal.Count);
        Assert.Equal(2, horizontal.OpenEnds);
        Assert.True(horizontal.Alive);
        Assert.Equal(3, horizontal.Cells.Count);
    }

    [Fact]
    public void CheckPatterns3D_EdgeBlocked_NotAlive()
    {
        var board = BoardWith((0, 0, 0, CellState.White), (1, 0, 0, CellState.White));
        var patterns = Rules.CheckPatterns3D(board, 0, 0, 0);
        var horizontal = patterns.Single(p => p.Dx == 1 && p.Dy == 0 && p.Dz == 0);
        Assert.Equal(2, horizontal.Count);
        Assert.Equal(1, horizontal.OpenEnds); // 负方向贴边即断
        Assert.False(horizontal.Alive);
    }

    [Fact]
    public void CheckPatterns3D_EmptyCell_ReturnsNothing()
    {
        var board = new Board();
        Assert.Empty(Rules.CheckPatterns3D(board, 6, 6, 0));
    }

    [Fact]
    public void ClassifyRun_CountsOpenEndsAtBothSides()
    {
        // W W 被右侧黑堵、左侧空 → (2, 1)
        var board = BoardWith((5, 5, 0, CellState.White), (6, 5, 0, CellState.White),
            (7, 5, 0, CellState.Black));
        var (count, openEnds) = Rules.ClassifyRun(board, 6, 5, 0, new Coordinate3D(1, 0, 0),
            CellState.White);
        Assert.Equal(2, count);
        Assert.Equal(1, openEnds);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    public void CheckWinner_NonPositiveWinLength_Throws(int winLength)
    {
        var board = BoardWith((0, 0, 0, CellState.Black));
        Assert.Throws<ArgumentOutOfRangeException>(() => Rules.CheckWinner(board, 0, 0, 0, winLength));
        Assert.Throws<ArgumentOutOfRangeException>(() => new StandardEngine(winLength));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SwapEngine(winLength));
    }

    [Fact]
    public void AuxScan_OutOfBoundsHover_ReturnsEmpty()
    {
        var board = new Board(5, 2);
        board.Set(0, 0, 0, CellState.Black);
        var res = AuxScan.Scan(board, PlayerColor.Black, new Coordinate3D(-1, 0, 0), true, true);
        Assert.Empty(res.PlanarLines);
        Assert.Empty(res.SpaceLines);
    }

    [Fact]
    public void KeycapParser_NullOrEmpty_ReturnsEmpty()
    {
        Assert.Empty(KeycapParser.Parse(null));
        Assert.Empty(KeycapParser.Parse(""));
    }
}
