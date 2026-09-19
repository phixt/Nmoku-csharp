namespace Nmoku.Core.Tests;

/// <summary>§10.5 R4-5：悬停辅助线扫描——跳空、连走、敌停、落点/截断标记、门控与角色归属。</summary>
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
    public void Scan_NeighborAlly_LineFromHoverAndMarkerAtPiece()
    {
        var board = BoardWith((6, 5, 0, CellState.Black));
        var r = AuxScan.Scan(board, PlayerColor.Black, H, includePlanar: true, includeSpace: false);

        var line = Assert.Single(r.PlanarLines);
        Assert.Equal(H, line.Start);
        Assert.Equal(new Coordinate3D(6, 5, 0), line.End);
        Assert.Equal(AuxRole.Self, line.Role);

        var marker = Assert.Single(r.PlanarMarkers);
        Assert.Equal(new Coordinate3D(6, 5, 0), marker.Position);
        Assert.Equal(AuxRole.Self, marker.Role); // 落点/收尾同点已去重
    }

    [Fact]
    public void Scan_SkipsEmptyCells_LineJumpsTheGap()
    {
        var board = BoardWith((7, 5, 0, CellState.Black)); // (6,5,0) 为空
        var r = AuxScan.Scan(board, PlayerColor.Black, H, true, false);

        var line = Assert.Single(r.PlanarLines);
        Assert.Equal(H, line.Start);
        Assert.Equal(new Coordinate3D(7, 5, 0), line.End); // 越过空格直连
    }

    [Fact]
    public void Scan_AllyThenEnemy_EnemyLineOnTopWithBothMarkers()
    {
        var board = BoardWith((6, 5, 0, CellState.Black), (7, 5, 0, CellState.White));
        var r = AuxScan.Scan(board, PlayerColor.Black, H, true, false);

        Assert.Equal(2, r.PlanarLines.Count);
        Assert.Equal(AuxRole.Self, r.PlanarLines[0].Role);   // hover→己子
        Assert.Equal(AuxRole.Enemy, r.PlanarLines[1].Role);  // 己子→敌子
        Assert.Equal(new Coordinate3D(7, 5, 0), r.PlanarLines[1].End);

        Assert.Contains(r.PlanarMarkers, m => m.Position.Equals(new Coordinate3D(7, 5, 0)) && m.Role == AuxRole.Enemy);
        Assert.Contains(r.PlanarMarkers, m => m.Position.Equals(new Coordinate3D(6, 5, 0)) && m.Role == AuxRole.Self);
        Assert.Equal(2, r.PlanarMarkers.Count);
    }

    [Fact]
    public void Scan_EnemyFirst_NoTrailingSelfMarker()
    {
        var board = BoardWith((6, 5, 0, CellState.White));
        var r = AuxScan.Scan(board, PlayerColor.Black, H, true, false);

        var line = Assert.Single(r.PlanarLines);
        Assert.Equal(AuxRole.Enemy, line.Role);
        var marker = Assert.Single(r.PlanarMarkers);
        Assert.Equal(AuxRole.Enemy, marker.Role); // prev==hover 不补己方标记
    }

    [Fact]
    public void Scan_AllyChain_TruncatesAtMaxCells_WithTrailingMarker()
    {
        var board = BoardWith(
            (6, 5, 0, CellState.Black), (7, 5, 0, CellState.Black),
            (8, 5, 0, CellState.Black), (9, 5, 0, CellState.Black),
            (10, 5, 0, CellState.Black)); // 第 5 子超出 4 格窗口
        var r = AuxScan.Scan(board, PlayerColor.Black, H, true, false);

        Assert.Equal(4, r.PlanarLines.Count); // hover→6→7→8→9
        Assert.DoesNotContain(r.PlanarLines, l => l.End.Equals(new Coordinate3D(10, 5, 0)));

        var marker = Assert.Single(r.PlanarMarkers);
        Assert.Equal(new Coordinate3D(9, 5, 0), marker.Position); // 截断处最后非空格
        Assert.Equal(AuxRole.Self, marker.Role);
    }

    [Fact]
    public void Scan_EmptyBeyondAlly_LandingMarkerAtLastAlly()
    {
        var board = BoardWith((6, 5, 0, CellState.Black), (7, 5, 0, CellState.Black));
        var r = AuxScan.Scan(board, PlayerColor.Black, H, true, false);

        var marker = Assert.Single(r.PlanarMarkers);
        Assert.Equal(new Coordinate3D(7, 5, 0), marker.Position); // 空位前的最后己子
        Assert.Equal(AuxRole.Self, marker.Role);
    }

    [Fact]
    public void Scan_AllFarBeyondMaxCells_ReturnsEmpty()
    {
        var board = BoardWith((10, 5, 0, CellState.Black)); // 距悬停 5 格
        var r = AuxScan.Scan(board, PlayerColor.Black, H, true, true);
        Assert.Empty(r.PlanarLines);
        Assert.Empty(r.SpaceLines);
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
    public void Scan_SpaceGating_AndLayerAxisCoverage()
    {
        var board = BoardWith((5, 5, 1, CellState.Black));

        var planarOnly = AuxScan.Scan(board, PlayerColor.Black, H, true, false);
        Assert.Empty(planarOnly.SpaceLines);
        Assert.Empty(planarOnly.PlanarLines); // 层轴子不在平面方向上

        var both = AuxScan.Scan(board, PlayerColor.Black, H, false, true);
        Assert.Empty(both.PlanarLines); // 平面被门控
        Assert.Contains(both.SpaceLines, l => l.End.Equals(new Coordinate3D(5, 5, 1)) && l.Role == AuxRole.Self);
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
