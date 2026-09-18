namespace Nmoku.Core.AI;

public enum AIDifficulty
{
    Easy,
    Medium,
    Hard,
}

/// <summary>
/// 启发式 AI（02-rules §4）：13 方向虚拟落子评分，total = attack + 1.1 * defense。
/// M1 提供同步求解 + 可取消异步封装；思考时间预算按里程碑约定不在 M1 校验（04 §6.2 除时间预算）。
/// </summary>
public sealed class AIEngine
{
    /// <summary>RUSH_FOUR 及以上即视为"必须立即处理"的威胁分（02-rules §4.5）。</summary>
    public const double ImmediateThreatScore = 10_000;

    private readonly Random _random;
    private CancellationTokenSource? _inFlight;

    public AIDifficulty Difficulty { get; set; }

    public int BoardSize { get; private set; } = GameConfig.DefaultBoardSize;
    public int Layers { get; private set; } = GameConfig.DefaultLayerCount;
    public int WinLength { get; private set; } = GameConfig.WinLength;

    public AIEngine(AIDifficulty difficulty = AIDifficulty.Medium, int? seed = null)
    {
        Difficulty = difficulty;
        _random = seed.HasValue ? new Random(seed.Value) : new Random();
    }

    /// <summary>04-contracts §2.2.1：可重复调用，每次全量重置内部状态。</summary>
    public void Initialize(int boardSize, int layers, int winLength = GameConfig.WinLength)
    {
        BoardSize = boardSize;
        Layers = layers;
        WinLength = winLength;
        Cancel();
    }

    /// <summary>同步求解（测试与控制台自弈用）；棋盘满或无合法位返回 null。</summary>
    public Coordinate3D? GetBestMove(Board board, PlayerColor player)
    {
        var candidates = GetCandidates(board, player);
        if (candidates.Count == 0)
        {
            return null;
        }

        if (Difficulty == AIDifficulty.Easy)
        {
            return GetEasyMove(board, player);
        }

        return GetScoredMove(board, player, candidates, CancellationToken.None);
    }

    /// <summary>异步求解（04-contracts §2.2.2/2.2.3）：在池线程计算，协作式取消后尽快返回 null。</summary>
    public Task<Coordinate3D?> GetBestMoveAsync(Board board, PlayerColor player,
        CancellationToken cancellationToken = default)
    {
        Cancel();
        _inFlight = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _inFlight.Token;
        return Task.Run(() =>
        {
            try
            {
                var candidates = GetCandidates(board, player);
                if (token.IsCancellationRequested || candidates.Count == 0)
                {
                    return null;
                }
                return Difficulty == AIDifficulty.Easy
                    ? GetEasyMove(board, player)
                    : GetScoredMove(board, player, candidates, token);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            // 不把 token 交给 Task.Run：取消契约要求"返回 null"而非抛异常（§2.2.3）
        });
    }

    /// <summary>取消进行中的求解；幂等，可随时调用（§2.2.3）。</summary>
    public void Cancel()
    {
        _inFlight?.Cancel();
        _inFlight = null;
    }

    // ---- 评分核心（02-rules §4.2–§4.4） ----

    /// <summary>在 (x,y,z) 虚拟落 player 子的 13 方向模式总分。</summary>
    public double EvaluateCell(Board board, int x, int y, int z, PlayerColor player)
    {
        double total = 0;
        CellState me = player.ToCell();

        foreach (var dir in Rules.Directions)
        {
            int count = 1;
            int openEnds = 0;

            int px = x + dir.X, py = y + dir.Y, pz = z + dir.Z;
            while (board.InBounds(px, py, pz))
            {
                CellState v = board.Get(px, py, pz);
                if (v == me)
                {
                    count++;
                    px += dir.X;
                    py += dir.Y;
                    pz += dir.Z;
                }
                else
                {
                    if (v == CellState.Empty)
                    {
                        openEnds++;
                    }
                    break;
                }
            }

            int nx = x - dir.X, ny = y - dir.Y, nz = z - dir.Z;
            while (board.InBounds(nx, ny, nz))
            {
                CellState v = board.Get(nx, ny, nz);
                if (v == me)
                {
                    count++;
                    nx -= dir.X;
                    ny -= dir.Y;
                    nz -= dir.Z;
                }
                else
                {
                    if (v == CellState.Empty)
                    {
                        openEnds++;
                    }
                    break;
                }
            }

            total += PatternScore(count, openEnds);
        }

        return total;
    }

    /// <summary>模式分数表（02-rules §4.3），指数级差保证"堵胜 > 造弱威胁"。</summary>
    public double PatternScore(int count, int openEnds)
    {
        if (count >= WinLength) return 1_000_000; // FIVE

        if (count == 4)
        {
            if (openEnds == 2) return 100_000; // LIVE_FOUR
            if (openEnds == 1) return 10_000;  // RUSH_FOUR
            return 0;
        }
        if (count == 3)
        {
            if (openEnds == 2) return 10_000;  // LIVE_THREE
            if (openEnds == 1) return 1_000;   // SLEEP_THREE
            return 0;
        }
        if (count == 2)
        {
            if (openEnds == 2) return 1_000;   // LIVE_TWO
            if (openEnds == 1) return 100;     // SLEEP_TWO
            return 0;
        }
        if (count == 1)
        {
            return openEnds >= 1 ? 10 : 0;     // ONE
        }
        return 0;
    }

    public double ScoreCandidate(Board board, Coordinate3D cell, PlayerColor aiPlayer)
    {
        double attack = EvaluateCell(board, cell.X, cell.Y, cell.Z, aiPlayer);
        double defense = EvaluateCell(board, cell.X, cell.Y, cell.Z, aiPlayer.Opponent());
        return attack + 1.1 * defense;
    }

    // ---- 候选集（02-rules §4.6） ----

    internal List<Coordinate3D> GetCandidates(Board board, PlayerColor player)
    {
        if (board.MoveCount() == 0)
        {
            int center = board.BoardSize / 2;
            return [new Coordinate3D(center, center, board.Layers / 2)];
        }

        int radius = Difficulty == AIDifficulty.Hard ? 2 : 1;
        return GetNearbyEmptyCells(board, radius);
    }

    internal static List<Coordinate3D> GetNearbyEmptyCells(Board board, int radius)
    {
        var visited = new HashSet<Coordinate3D>();
        var result = new List<Coordinate3D>();

        board.ForEach((x, y, z, state) =>
        {
            if (state == CellState.Empty)
            {
                return;
            }
            for (int dz = -radius; dz <= radius; dz++)
            {
                for (int dy = -radius; dy <= radius; dy++)
                {
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        if (dx == 0 && dy == 0 && dz == 0)
                        {
                            continue;
                        }
                        var cell = new Coordinate3D(x + dx, y + dy, z + dz);
                        if (!board.InBounds(cell.X, cell.Y, cell.Z)
                            || !board.IsEmpty(cell.X, cell.Y, cell.Z)
                            || !visited.Add(cell))
                        {
                            continue;
                        }
                        result.Add(cell);
                    }
                }
            }
        });

        return result;
    }

    private Coordinate3D? GetEasyMove(Board board, PlayerColor player)
    {
        // Easy = 仅堵对方 RUSH_FOUR+ 抢点，否则在空位随机（02-rules §4.5/§4.6）
        var threat = FindImmediateThreat(board, player.Opponent());
        if (threat != null)
        {
            return threat;
        }
        // 随机空位（全棋盘范围）
        var empties = GetAllEmptyCells(board);
        return empties.Count == 0 ? null : empties[_random.Next(empties.Count)];
    }

    private Coordinate3D? GetScoredMove(Board board, PlayerColor player,
        List<Coordinate3D> candidates, CancellationToken token)
    {
        double best = double.NegativeInfinity;
        var bestTies = new List<Coordinate3D>();
        int processed = 0;

        foreach (var cell in candidates)
        {
            double total = ScoreCandidate(board, cell, player);
            if (total > best)
            {
                best = total;
                bestTies.Clear();
                bestTies.Add(cell);
            }
            else if (total == best)
            {
                bestTies.Add(cell);
            }

            // 每 256 个候选检查一次取消（04 §6.2：<100ms 响应）
            if ((++processed & 0xFF) == 0 && token.IsCancellationRequested)
            {
                return null;
            }
        }

        return bestTies.Count == 0 ? null : bestTies[_random.Next(bestTies.Count)];
    }

    /// <summary>对方落子即可达 RUSH_FOUR+ 的点位（02-rules §4.5）。</summary>
    public Coordinate3D? FindImmediateThreat(Board board, PlayerColor opponent)
    {
        for (int z = 0; z < board.Layers; z++)
        {
            for (int y = 0; y < board.BoardSize; y++)
            {
                for (int x = 0; x < board.BoardSize; x++)
                {
                    if (!board.IsEmpty(x, y, z))
                    {
                        continue;
                    }
                    if (EvaluateCell(board, x, y, z, opponent) >= ImmediateThreatScore)
                    {
                        return new Coordinate3D(x, y, z);
                    }
                }
            }
        }
        return null;
    }

    private static List<Coordinate3D> GetAllEmptyCells(Board board)
    {
        var result = new List<Coordinate3D>();
        board.ForEach((x, y, z, state) =>
        {
            if (state == CellState.Empty)
            {
                result.Add(new Coordinate3D(x, y, z));
            }
        });
        return result;
    }
}
