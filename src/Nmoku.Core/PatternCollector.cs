namespace Nmoku.Core;

/// <summary>
/// 单层唯一连子模式收集（02-rules §5.2）：以"负方向 neighbor 非同色"为起点去重，
/// 供辅助线渲染器共用一份数据（展示端 4 格上限由 GameConfig.HelperLineMaxCells 约束渲染器）。
/// </summary>
public static class PatternCollector
{
    public static IReadOnlyList<Pattern3D> Collect(Board board, int layerZ)
    {
        var results = new List<Pattern3D>();

        for (int y = 0; y < board.BoardSize; y++)
        {
            for (int x = 0; x < board.BoardSize; x++)
            {
                CellState state = board.Get(x, y, layerZ);
                if (state == CellState.Empty)
                {
                    continue;
                }

                foreach (var dir in Rules.Directions)
                {
                    int bx = x - dir.X, by = y - dir.Y, bz = layerZ - dir.Z;
                    if (board.InBounds(bx, by, bz) && board.Get(bx, by, bz) == state)
                    {
                        continue; // 该模式已在更早的起点被收集
                    }

                    var cells = new List<Coordinate3D> { new(x, y, layerZ) };
                    int qx = x + dir.X, qy = y + dir.Y, qz = layerZ + dir.Z;
                    while (board.InBounds(qx, qy, qz) && board.Get(qx, qy, qz) == state)
                    {
                        cells.Add(new Coordinate3D(qx, qy, qz));
                        qx += dir.X;
                        qy += dir.Y;
                        qz += dir.Z;
                    }

                    int openEnds = 0;
                    if (board.InBounds(qx, qy, qz) && board.IsEmpty(qx, qy, qz))
                    {
                        openEnds++;
                    }
                    if (board.InBounds(bx, by, bz) && board.IsEmpty(bx, by, bz))
                    {
                        openEnds++;
                    }

                    if (cells.Count >= 2)
                    {
                        results.Add(new Pattern3D
                        {
                            Dx = dir.X,
                            Dy = dir.Y,
                            Dz = dir.Z,
                            Count = cells.Count,
                            OpenEnds = openEnds,
                            Cells = cells,
                        });
                    }
                }
            }
        }

        return results;
    }
}
