namespace Nmoku.Core;

/// <summary>
/// 三维棋盘数据模型（01-core §1）：扁平 byte[]，z-major 行主序，
/// 越界读返回 Empty、越界写忽略。
/// </summary>
public sealed class Board
{
    private readonly byte[] _data;

    public int BoardSize { get; }
    public int Layers { get; }

    public Board(int boardSize = GameConfig.DefaultBoardSize, int layers = GameConfig.DefaultLayerCount)
    {
        BoardSize = boardSize;
        Layers = layers;
        _data = new byte[boardSize * boardSize * layers];
    }

    public bool InBounds(int x, int y, int z) =>
        x >= 0 && x < BoardSize && y >= 0 && y < BoardSize && z >= 0 && z < Layers;

    /// <summary>INDEX = z*s² + y*s + x（01-core §1.3）。</summary>
    public int Index(int x, int y, int z) => z * BoardSize * BoardSize + y * BoardSize + x;

    public CellState Get(int x, int y, int z) =>
        InBounds(x, y, z) ? (CellState)_data[Index(x, y, z)] : CellState.Empty;

    /// <summary>越界写入被静默忽略（01-core §1.3 Set 伪代码）。</summary>
    public void Set(int x, int y, int z, CellState state)
    {
        if (InBounds(x, y, z))
        {
            _data[Index(x, y, z)] = (byte)state;
        }
    }

    public void Set(int x, int y, int z, PlayerColor player) => Set(x, y, z, player.ToCell());

    public void ForEach(Action<int, int, int, CellState> callback)
    {
        for (int z = 0; z < Layers; z++)
        {
            for (int y = 0; y < BoardSize; y++)
            {
                for (int x = 0; x < BoardSize; x++)
                {
                    callback(x, y, z, (CellState)_data[Index(x, y, z)]);
                }
            }
        }
    }

    public void Reset() => Array.Clear(_data);

    /// <summary>返回 (黑子数, 白子数)（01-core §1.5）。</summary>
    public (int Black, int White) CountPieces()
    {
        int black = 0, white = 0;
        foreach (byte v in _data)
        {
            if (v == (byte)CellState.Black) black++;
            else if (v == (byte)CellState.White) white++;
        }
        return (black, white);
    }

    /// <summary>由子数推断当前应行棋方：黑多则轮白，否则轮黑（01-core §1.5）。</summary>
    public PlayerColor DetectCurrentPlayer()
    {
        var (black, white) = CountPieces();
        return black <= white ? PlayerColor.Black : PlayerColor.White;
    }

    /// <summary>已落子总数；盘满判断用 <see cref="IsFull"/>。</summary>
    public int MoveCount()
    {
        var (black, white) = CountPieces();
        return black + white;
    }

    /// <summary>棋盘是否已满（00-domain §4.9 判和依据：盘满且无胜者时判和，判和决策在 Game 侧）。</summary>
    public bool IsFull() => MoveCount() == _data.Length;

    public bool IsEmpty(int x, int y, int z) => Get(x, y, z) == CellState.Empty;
}
