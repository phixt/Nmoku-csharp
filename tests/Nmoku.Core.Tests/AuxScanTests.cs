namespace Nmoku.Core.Tests;

/// <summary>B9 简化语义：每方向只到首个非空子即停——一条线 + 一个标记，不穿透、不连走、不截断标记。</summary>
public class AuxScanTests
{
    private static readonly Coordinate3D H = new(5, 5, 0); // 悬停格（空）

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
    public void Scan_NeighborAlly_LineAndMarkerAtPiece()
    {
        var board = BoardWith((6, 5, 0, CellState.Black));
        var r = AuxScan.Scan(board, PlayerColor.Black, H, includePlanar: true, includeSpace: false);

        var line = Assert.Single(r.PlanarLines);
        Assert.Equal(H, line.Start);
        Assert.Equal(new Coordinate3D(6, 5, 0), line.End);
        Assert.Equal(AuxRole.Self, line.Role);

        var marker = Assert.Single(r.PlanarMarkers);
        Assert.Equal(new Coordinate3D(6, 5, 0), marker.Position);
        Assert.Equal(AuxRole.Self, marker.Role);
    }

    [Fact]
    public void Scan_LineCrossesEmptyCells_NeverAStone()
    {
        var board = BoardWith((7, 5, 0, CellState.Black)); // (6,5,0) 为空
        var r = AuxScan.Scan(board, PlayerColor.Black, H, true, false);

        var line = Assert.Single(r.PlanarLines);
        Assert.Equal(H, line.Start);
        Assert.Equal(new Coordinate3D(7, 5, 0), line.End); // 越过空格直达首子
    }

    [Fact]
    public void Scan_AllyThenEnemyBehind_OnlyFirstAllyShown()
    {
        // 己子后方的敌子（或任何子）不再出现——首子即停
        var board = BoardWith((6, 5, 0, CellState.Black), (7, 5, 0, CellState.White));
        var r = AuxScan.Scan(board, PlayerColor.Black, H, true, false);

        var line = Assert.Single(r.PlanarLines);
        Assert.Equal(new Coordinate3D(6, 5, 0), line.End);
        Assert.Equal(AuxRole.Self, line.Role);
        var marker = Assert.Single(r.PlanarMarkers);
        Assert.Equal(new Coordinate3D(6, 5, 0), marker.Position);
    }

    [Fact]
    public void Scan_AllyChain_OnlyNearestShown()
    {
        // 修复"己方只有最远的显示"：连子不再延伸到最远端
        var board = BoardWith(
            (6, 5, 0, CellState.Black), (7, 5, 0, CellState.Black), (8, 5, 0, CellState.Black));
        var r = AuxScan.Scan(board, PlayerColor.Black, H, true, false);

        var line = Assert.Single(r.PlanarLines);
        Assert.Equal(new Coordinate3D(6, 5, 0), line.End);
        var marker = Assert.Single(r.PlanarMarkers);
        Assert.Equal(new Coordinate3D(6, 5, 0), marker.Position);
    }

    [Fact]
    public void Scan_EnemyFirst_EnemyLineAndMarker()
    {
        var board = BoardWith((6, 5, 0, CellState.White));
        var r = AuxScan.Scan(board, PlayerColor.Black, H, true, false);

        var line = Assert.Single(r.PlanarLines);
        Assert.Equal(AuxRole.Enemy, line.Role);
        var marker = Assert.Single(r.PlanarMarkers);
        Assert.Equal(AuxRole.Enemy, marker.Role);
    }

    [Fact]
    public void Scan_RoleFollowsPlayerPerspective()
    {
        var board = BoardWith((6, 5, 0, CellState.White), (5, 6, 0, CellState.Black));
        var r = AuxScan.Scan(board, PlayerColor.White, H, true, false);

        Assert.Contains(r.PlanarLines, l => l.End.Equals(new Coordinate3D(6, 5, 0)) && l.Role == AuxRole.Self);
        Assert.Contains(r.PlanarLines, l => l.End.Equals(new Coordinate3D(5, 6, 0)) && l.Role == AuxRole.Enemy);
    }

    [Fact]
    public void Scan_StoneBeyondMaxCells_Ignored()
    {
        var board = BoardWith((10, 5, 0, CellState.Black)); // 距悬停 5 格
        var r = AuxScan.Scan(board, PlayerColor.Black, H, true, true);
        Assert.Empty(r.PlanarLines);
        Assert.Empty(r.SpaceLines);
    }

    [Fact]
    public void Scan_SpaceGating_AndLayerAxisCoverage()
    {
        var board = BoardWith((5, 5, 1, CellState.Black));

        var planarOnly = AuxScan.Scan(board, PlayerColor.Black, H, true, false);
        Assert.Empty(planarOnly.SpaceLines);
        Assert.Empty(planarOnly.PlanarLines); // 层轴子不在平面方向上

        var both = AuxScan.Scan(board, PlayerColor.Black, H, false, true);
        Assert.Empty(both.PlanarLines); // 平面被门控
        var line = Assert.Single(both.SpaceLines);
        Assert.Equal(new Coordinate3D(5, 5, 1), line.End); // 跨层首子
        Assert.Equal(AuxRole.Self, line.Role);
    }

    [Fact]
    public void Scan_NothingNearby_ReturnsEmpty()
    {
        var r = AuxScan.Scan(new Board(), PlayerColor.Black, H, true, true);
        Assert.Empty(r.PlanarLines);
        Assert.Empty(r.PlanarMarkers);
        Assert.Empty(r.SpaceLines);
        Assert.Empty(r.SpaceMarkers);
    }
}
