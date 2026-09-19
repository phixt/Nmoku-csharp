namespace Nmoku.Core;

/// <summary>单元格状态，数值与存档 schema 直接兼容（01-core §1.2）。</summary>
public enum CellState : byte
{
    Empty = 0,
    Black = 1,
    White = 2,
}

/// <summary>玩家颜色（00-domain §2.3）。BLACK 为先手。</summary>
public enum PlayerColor
{
    Black = 1,
    White = 2,
}

public static class PlayerColorExtensions
{
    public static PlayerColor Opponent(this PlayerColor p) =>
        p == PlayerColor.Black ? PlayerColor.White : PlayerColor.Black;

    public static CellState ToCell(this PlayerColor p) => (CellState)(int)p;

    public static PlayerColor ToPlayer(this CellState c) => (PlayerColor)(int)c;
}

/// <summary>棋盘三维逻辑坐标（00-domain §2.1）：x=列，y=行，z=层。</summary>
public readonly record struct Coordinate3D(int X, int Y, int Z);

/// <summary>
/// 一手落子（01-core §2.1）。不可变为其默认语义，唯一允许的原地变更是
/// Swap2 换色（00-domain §1.3），因此用 class 而非 record。
/// </summary>
public sealed class Move
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public PlayerColor Player { get; set; }
    public long? Timestamp { get; set; }
    public double? Evaluation { get; set; }

    public Move(int x, int y, int z, PlayerColor player, long? timestamp = null, double? evaluation = null)
    {
        X = x;
        Y = y;
        Z = z;
        Player = player;
        Timestamp = timestamp;
        Evaluation = evaluation;
    }

    public Coordinate3D Coordinate => new(X, Y, Z);
}

/// <summary>
/// 回合状态（02-rules §2.2）。IsSwapPhase=true 表示处于换色决策期：
/// NextPlayer 保持刚行棋方不变，Message 的受众（白方）负责在弹窗中决策，
/// shell 在决策完成前不得推进回合（源 main.ts:341 "keep currentPlayer unchanged"）。
/// </summary>
public readonly record struct TurnState(PlayerColor NextPlayer, bool IsSwapPhase, string Message);

/// <summary>一条连珠模式（01-core §5.3），count 为同色连子数，openEnds∈{0,1,2}。</summary>
public sealed class Pattern3D
{
    public required int Dx { get; init; }
    public required int Dy { get; init; }
    public required int Dz { get; init; }
    public required int Count { get; init; }
    public required int OpenEnds { get; init; }
    public bool Alive => OpenEnds >= 2;
    public required IReadOnlyList<Coordinate3D> Cells { get; init; }
}

/// <summary>游戏状态（00-domain §2.7）。</summary>
public enum GameStatus
{
    NotStarted,
    InProgress,
    Paused,
    Finished,
}

/// <summary>规则集。SaveData.rules 用 "gomoku"/"swap2"（04-contracts §3.3）；
/// LevelConfig 中的 "standard" 与 Gomoku 同义。</summary>
public enum RuleSet
{
    Gomoku,
    Renju,
    Swap2,
}

/// <summary>棋盘尺寸与初始局面配置（04-contracts §3.3 LevelConfig）。</summary>
public sealed record LevelConfig
{
    public required string Id { get; init; }
    public string Name { get; init; } = "";
    public int BoardSize { get; init; } = GameConfig.DefaultBoardSize;
    public int Layers { get; init; } = GameConfig.DefaultLayerCount;
    public int WinLength { get; init; } = GameConfig.WinLength;
    public RuleSet Rules { get; init; } = RuleSet.Gomoku;

    /// <summary>预置子；非空则 Swap2 阶段被整体跳过（00-domain §4.7）。</summary>
    public IReadOnlyList<Move> InitialMoves { get; init; } = [];
}

/// <summary>复盘游标（01-core §2.4）：currentIndex 表示可见的手数。</summary>
public sealed class ReplayState
{
    public required IReadOnlyList<Move> Moves { get; init; }
    public int CurrentIndex { get; set; }

    public void ReplayOnto(Board board) => ReplayMoves(board, Moves, CurrentIndex);

    public static void ReplayMoves(Board board, IReadOnlyList<Move> moves, int targetCount)
    {
        board.Reset();
        int n = Math.Clamp(targetCount, 0, moves.Count);
        for (int i = 0; i < n; i++)
        {
            board.Set(moves[i].X, moves[i].Y, moves[i].Z, moves[i].Player.ToCell());
        }
    }
}

/// <summary>槽位元数据（04-contracts §3.1.2）。</summary>
public sealed record SlotMeta
{
    public int Index { get; init; }
    public required string Id { get; init; }
    public long Timestamp { get; init; }
    public int BoardSize { get; init; }
    public int Layers { get; init; }
    public int MovesCount { get; init; }
}
