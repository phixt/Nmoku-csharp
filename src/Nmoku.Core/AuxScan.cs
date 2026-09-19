namespace Nmoku.Core;

/// <summary>辅助线元素的角色（己方/敌方），配色由展示端调色板解析（§10.5 R4-6，数据与颜色解耦）。</summary>
public enum AuxRole
{
    Self,
    Enemy,
}

/// <summary>一条辅助线段（逻辑坐标；平面族共层，空间族跨层）。</summary>
public readonly record struct AuxLine(Coordinate3D Start, Coordinate3D End, AuxRole Role);

/// <summary>一个辅助线标记点：首个非空子（己=延伸提示，敌=阻挡提示）。</summary>
public readonly record struct AuxMarker(Coordinate3D Position, AuxRole Role);

/// <summary>
/// 悬停辅助线扫描结果。2D 面板取 PlanarLines+PlanarMarkers；
/// 3D 面板载荷 = SpaceLines + PlanarMarkers + SpaceMarkers（§10.5 R4-11，与源 AuxData3D 对应）。
/// </summary>
public sealed record AuxScanResult(
    IReadOnlyList<AuxLine> PlanarLines,
    IReadOnlyList<AuxMarker> PlanarMarkers,
    IReadOnlyList<AuxLine> SpaceLines,
    IReadOnlyList<AuxMarker> SpaceMarkers)
{
    public static readonly AuxScanResult Empty = new([], [], [], []);
}

/// <summary>
/// 悬停辅助线扫描（**B9 简化语义**，人类指示 2026-09-19 弃源逻辑）：
/// 每方向从悬停格起最多 HelperLineMaxCells 步，遇**首个非空子即停**——恰好一条线（悬停→该子，
/// 可越过空格，但永不穿过棋子）+ 一个标记（角色=子色）。源的跳空续走/己方连走/截断标记
/// 会使跨层斜线视觉穿透中间层（己方只剩最远标记/敌方己敌混杂），不再保留。
/// </summary>
public static class AuxScan
{
    /// <summary>2D 战术线方向（源 DIRS_2D）：4 平面方向，正反向各扫一次。</summary>
    private static readonly Coordinate3D[] PlanarDirs =
    [
        new(1, 0, 0), new(0, 1, 0), new(1, 1, 0), new(1, -1, 0),
    ];

    public static AuxScanResult Scan(Board board, PlayerColor player, Coordinate3D hover,
        bool includePlanar, bool includeSpace)
    {
        var planarLines = new List<AuxLine>();
        var planarMarkers = new List<AuxMarker>();
        var spaceLines = new List<AuxLine>();
        var spaceMarkers = new List<AuxMarker>();

        if (includePlanar)
        {
            foreach (var dir in PlanarDirs)
            {
                ScanDir(board, player, hover, dir, planarLines, planarMarkers);
                ScanDir(board, player, hover, dir * -1, planarLines, planarMarkers);
            }
        }

        if (includeSpace)
        {
            foreach (var dir in Rules.Directions)
            {
                ScanDir(board, player, hover, dir, spaceLines, spaceMarkers);
                ScanDir(board, player, hover, dir * -1, spaceLines, spaceMarkers);
            }
        }

        return new AuxScanResult(planarLines, planarMarkers, spaceLines, spaceMarkers);
    }

    private static void ScanDir(Board board, PlayerColor player, Coordinate3D hover,
        Coordinate3D dir, List<AuxLine> lines, List<AuxMarker> markers)
    {
        int maxSteps = GameConfig.HelperLineMaxCells;

        for (int step = 1; step <= maxSteps; step++)
        {
            var p = hover + dir * step;
            if (!board.InBounds(p))
            {
                return;
            }
            var state = board.Get(p);
            if (state == CellState.Empty)
            {
                continue; // 空格越过（线不穿过任何棋子）
            }
            var role = state == player.ToCell() ? AuxRole.Self : AuxRole.Enemy;
            lines.Add(new AuxLine(hover, p, role));
            markers.Add(new AuxMarker(p, role));
            return; // 首个非空子即停
        }
    }
}
