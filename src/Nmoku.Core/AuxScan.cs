namespace Nmoku.Core;

/// <summary>辅助线元素的角色（己方/敌方），配色由展示端调色板解析（§10.5 R4-6，数据与颜色解耦）。</summary>
public enum AuxRole
{
    Self,
    Enemy,
}

/// <summary>一条辅助线段（逻辑坐标；平面族共层，空间族跨层）。</summary>
public readonly record struct AuxLine(Coordinate3D Start, Coordinate3D End, AuxRole Role);

/// <summary>一个辅助线标记点：落点提示（Self）/敌子提醒（Enemy）。</summary>
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
/// 悬停辅助线扫描（§10.5 R4-5，源 RightPanel.scanDirection2D/3D 忠实重写）：
/// 每方向先跳过连续空格找首个非空子；随后己方连子续走、敌方画线即停、遇空位在最后非空格放落点标记、
/// HelperLineMaxCells 截断/出界在最后非空格放己方标记。线段颜色取目的格角色。
/// 悬停格本身须为空（展示端保证）；同位置重复标记已去重（源会重复添加，视觉等价）。
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

        // 1) 跳过连续空格，找方向上首个非空子
        int first = 1;
        while (true)
        {
            if (first > maxSteps || !board.InBounds(hover + dir * first))
            {
                return;
            }
            if (board.Get(hover + dir * first) != CellState.Empty)
            {
                break;
            }
            first++;
        }
        // 2) 沿方向走：己方续走、敌方画线即停、空位放落点标记即停、截断/出界收尾
        int step = first;
        var prev = hover;
        while (true)
        {
            if (step > maxSteps)
            {
                break;
            }
            var p = hover + dir * step;
            if (!board.InBounds(p))
            {
                break;
            }
            var state = board.Get(p);
            bool ally = state == player.ToCell();

            if (state != CellState.Empty)
            {
                lines.Add(new AuxLine(prev, p, ally ? AuxRole.Self : AuxRole.Enemy));
            }

            if (state == CellState.Empty)
            {
                AddMarker(markers, prev, AuxRole.Self);
                break;
            }
            if (!ally)
            {
                AddMarker(markers, p, AuxRole.Enemy);
                break;
            }

            prev = p;
            step++;
        }

        // 3) 收尾标记：最后非空格（prev==hover 说明首子即敌/无子，不补）
        if (!prev.Equals(hover))
        {
            AddMarker(markers, prev, AuxRole.Self);
        }
    }

    /// <summary>同位同角色去重（源在"落点标记 + 截断标记"处会重复添加，数据端收敛为一条）。</summary>
    private static void AddMarker(List<AuxMarker> markers, Coordinate3D position, AuxRole role)
    {
        var marker = new AuxMarker(position, role);
        if (markers.Count == 0 || !markers[^1].Equals(marker))
        {
            markers.Add(marker);
        }
    }
}
