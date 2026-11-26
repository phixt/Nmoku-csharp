using System.Collections.Immutable;

namespace Nmoku.Core;

/// <summary>
/// 胜负判定与模式检测（02-rules §1）：13 独立方向、双向扫描、winLength 可配。
/// </summary>
public static class Rules
{
    /// <summary>13 个独立方向向量，顺序与 02-rules §1.2 一致（不可变，防调用方误改全局规则表）。</summary>
    public static readonly ImmutableArray<Coordinate3D> Directions =
    [
        new(1, 0, 0), new(0, 1, 0), new(0, 0, 1),                      // 轴向
        new(1, 1, 0), new(1, -1, 0),                                   // 面-diagonal XY
        new(1, 0, 1), new(1, 0, -1),                                   // 面-diagonal XZ
        new(0, 1, 1), new(0, 1, -1),                                   // 面-diagonal YZ
        new(1, 1, 1), new(1, 1, -1), new(1, -1, 1), new(1, -1, -1),    // 体-diagonal
    ];

    /// <summary>
    /// 以 (x,y,z) 刚落下之子为中心做 13 方向连子检测（02-rules §1.3）。
    /// 返回获胜方颜色；空位、越界或未达 winLength 返回 Empty(0)。
    /// </summary>
    public static CellState CheckWinner(Board board, int x, int y, int z,
        int winLength = GameConfig.WinLength)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentOutOfRangeException.ThrowIfLessThan(winLength, 1);
        if (!board.InBounds(x, y, z))
        {
            return CellState.Empty;
        }

        CellState state = board.Get(x, y, z);
        if (state == CellState.Empty)
        {
            return CellState.Empty;
        }

        foreach (var dir in Directions)
        {
            int count = 1;

            for (int px = x + dir.X, py = y + dir.Y, pz = z + dir.Z;
                 board.InBounds(px, py, pz) && board.Get(px, py, pz) == state;
                 px += dir.X, py += dir.Y, pz += dir.Z)
            {
                count++;
            }

            for (int nx = x - dir.X, ny = y - dir.Y, nz = z - dir.Z;
                 board.InBounds(nx, ny, nz) && board.Get(nx, ny, nz) == state;
                 nx -= dir.X, ny -= dir.Y, nz -= dir.Z)
            {
                count++;
            }

            if (count >= winLength)
            {
                return state;
            }
        }

        return CellState.Empty;
    }

    /// <summary>单方向双向扫描连子数与开放端（02-rules §1.6 classifyRun）。</summary>
    public static (int Count, int OpenEnds) ClassifyRun(Board board, int x, int y, int z,
        Coordinate3D dir, CellState state)
    {
        int count = 1;
        int openEnds = 0;

        int px = x + dir.X, py = y + dir.Y, pz = z + dir.Z;
        while (board.InBounds(px, py, pz) && board.Get(px, py, pz) == state)
        {
            count++;
            px += dir.X;
            py += dir.Y;
            pz += dir.Z;
        }
        if (board.InBounds(px, py, pz) && board.IsEmpty(px, py, pz))
        {
            openEnds++;
        }

        int nx = x - dir.X, ny = y - dir.Y, nz = z - dir.Z;
        while (board.InBounds(nx, ny, nz) && board.Get(nx, ny, nz) == state)
        {
            count++;
            nx -= dir.X;
            ny -= dir.Y;
            nz -= dir.Z;
        }
        if (board.InBounds(nx, ny, nz) && board.IsEmpty(nx, ny, nz))
        {
            openEnds++;
        }

        return (count, openEnds);
    }

    /// <summary>
    /// 以 (x,y,z) 为中心列出所有 count>=2 的连子模式（01-core §5.3 checkPatterns3D）。
    /// 空位返回空列表。
    /// </summary>
    public static IReadOnlyList<Pattern3D> CheckPatterns3D(Board board, int x, int y, int z)
    {
        CellState state = board.Get(x, y, z);
        List<Pattern3D> results = [];
        if (state == CellState.Empty)
        {
            return results;
        }

        foreach (var dir in Directions)
        {
            var (count, openEnds) = ClassifyRun(board, x, y, z, dir, state);
            if (count >= 2)
            {
                results.Add(new Pattern3D
                {
                    Dx = dir.X,
                    Dy = dir.Y,
                    Dz = dir.Z,
                    Count = count,
                    OpenEnds = openEnds,
                    Cells = CollectRunCells(board, x, y, z, dir, state),
                });
            }
        }

        return results;
    }

    private static List<Coordinate3D> CollectRunCells(Board board, int x, int y, int z,
        Coordinate3D dir, CellState state)
    {
        var cells = new List<Coordinate3D> { new(x, y, z) };
        for (int px = x + dir.X, py = y + dir.Y, pz = z + dir.Z;
             board.InBounds(px, py, pz) && board.Get(px, py, pz) == state;
             px += dir.X, py += dir.Y, pz += dir.Z)
        {
            cells.Add(new Coordinate3D(px, py, pz));
        }
        var front = new List<Coordinate3D>();
        for (int nx = x - dir.X, ny = y - dir.Y, nz = z - dir.Z;
             board.InBounds(nx, ny, nz) && board.Get(nx, ny, nz) == state;
             nx -= dir.X, ny -= dir.Y, nz -= dir.Z)
        {
            front.Add(new Coordinate3D(nx, ny, nz));
        }
        front.Reverse();
        front.AddRange(cells);
        return front;
    }
}
