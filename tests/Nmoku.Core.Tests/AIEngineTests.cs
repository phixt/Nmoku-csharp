using Nmoku.Core.AI;

namespace Nmoku.Core.Tests;

/// <summary>test-and-cicd-plan §4.5 + 04-contracts §6.2（除时间预算）。</summary>
public class AIEngineTests
{
    [Fact]
    public void GetBestMove_EmptyBoard_ReturnsCenter()
    {
        var ai = new AIEngine(AIDifficulty.Medium, seed: 1);
        ai.Initialize(13, 6, 5);

        var move = ai.GetBestMove(new Board(), PlayerColor.Black);

        Assert.Equal(new Coordinate3D(6, 6, 3), move);
    }

    [Theory]
    [InlineData(AIDifficulty.Easy)]
    [InlineData(AIDifficulty.Medium)]
    [InlineData(AIDifficulty.Hard)]
    public void GetBestMove_AlwaysReturnsLegalCoordinate(AIDifficulty difficulty)
    {
        var ai = new AIEngine(difficulty, seed: 7);
        ai.Initialize(9, 3, 5);
        var board = new Board(9, 3);
        // 预置一些子，含 (4,4,1) 被占
        board.Set(4, 4, 1, CellState.Black);
        board.Set(4, 5, 1, CellState.White);
        board.Set(3, 4, 1, CellState.Black);

        var move = ai.GetBestMove(board, PlayerColor.White)!;

        Assert.NotNull(move);
        Assert.True(board.InBounds(move.Value.X, move.Value.Y, move.Value.Z));
        Assert.True(board.IsEmpty(move.Value.X, move.Value.Y, move.Value.Z));
    }

    [Fact]
    public void GetBestMove_HardWithOwnFour_BlocksOpponentRushFour()
    {
        // 白有 RUSH_FOUR（左端贴边被断、右端 (4,2,0) 是唯一防点）；黑应落该点
        var board = new Board(13, 6);
        for (int x = 0; x < 4; x++)
        {
            board.Set(x, 2, 0, CellState.White);
        }
        // 黑自己的弱形状，不构成五连威胁
        board.Set(9, 9, 3, CellState.Black);
        board.Set(10, 9, 3, CellState.Black);

        var ai = new AIEngine(AIDifficulty.Hard, seed: 3);
        ai.Initialize(13, 6, 5);

        var move = ai.GetBestMove(board, PlayerColor.Black);

        Assert.Equal(new Coordinate3D(4, 2, 0), move);
    }

    [Fact]
    public void GetBestMove_Easy_BlocksOpponentRushFour()
    {
        var board = new Board(13, 6);
        for (int x = 1; x <= 4; x++)
        {
            board.Set(x, 5, 2, CellState.White);
        }
        board.Set(0, 5, 2, CellState.Black); // 左端被黑堵 → 唯一防点 (5,5,2)

        var ai = new AIEngine(AIDifficulty.Easy, seed: 3);
        ai.Initialize(13, 6, 5);

        var move = ai.GetBestMove(board, PlayerColor.Black);

        Assert.Equal(new Coordinate3D(5, 5, 2), move);
    }

    [Fact]
    public void GetBestMove_FullBoard_ReturnsNull()
    {
        var board = new Board(5, 1);
        board.ForEach((x, y, z, _) => board.Set(x, y, z, CellState.Black));
        var ai = new AIEngine(AIDifficulty.Medium, seed: 1);
        ai.Initialize(5, 1, 5);

        Assert.Null(ai.GetBestMove(board, PlayerColor.White));
    }

    [Fact]
    public async Task CancelledToken_Request_ReturnsNullPromptly()
    {
        var ai = new AIEngine(AIDifficulty.Hard, seed: 1);
        ai.Initialize(13, 6, 5);
        var board = new Board();
        board.Set(6, 6, 3, CellState.Black);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync(); // 预先取消：求解入口即短路，结果确定

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = await ai.GetBestMoveAsync(board, PlayerColor.White, cts.Token);
        sw.Stop();

        Assert.Null(result);
        Assert.True(sw.ElapsedMilliseconds < 100, $"取消响应超时: {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public void Cancel_NoInFlightRequest_IsIdempotentNoOp()
    {
        var ai = new AIEngine(AIDifficulty.Medium, seed: 1);
        ai.Initialize(9, 2, 5);
        ai.Cancel();
        ai.Cancel();
    }

    [Fact]
    public void Initialize_CalledRepeatedly_IsIdempotent()
    {
        var ai = new AIEngine(AIDifficulty.Medium, seed: 1);
        ai.Initialize(9, 2, 5);
        ai.Initialize(13, 6, 5);
        Assert.Equal(13, ai.BoardSize);
        var move = ai.GetBestMove(new Board(), PlayerColor.Black);
        Assert.Equal(new Coordinate3D(6, 6, 3), move);
    }

    [Fact]
    public void EvaluateCell_FiveInRowVirtualScoresAsFive()
    {
        var board = new Board();
        for (int x = 0; x < 4; x++)
        {
            board.Set(x, 3, 1, CellState.Black);
        }
        var ai = new AIEngine(AIDifficulty.Hard, seed: 1);
        ai.Initialize(13, 6, 5);

        double score = ai.EvaluateCell(board, 4, 3, 1, PlayerColor.Black);

        Assert.True(score >= 1_000_000);
    }

    [Fact]
    public void AIVersusAI_HundredMoves_AllLegalAndNoException()
    {
        // M1 验收：AI vs AI 自洽对局（种子固定保证可复现）
        var board = new Board();
        var black = new AIEngine(AIDifficulty.Medium, seed: 11);
        var white = new AIEngine(AIDifficulty.Hard, seed: 22);
        black.Initialize(13, 6, 5);
        white.Initialize(13, 6, 5);
        var engine = new StandardEngine();
        var history = new List<Move>();
        var current = PlayerColor.Black;

        for (int i = 0; i < 100; i++)
        {
            var ai = current == PlayerColor.Black ? black : white;
            var move = ai.GetBestMove(board, current);
            Assert.NotNull(move); // 100 手内远未满盘
            Assert.True(engine.IsLegalMove(move!.Value.X, move!.Value.Y, move!.Value.Z,
                current, history, board), $"第 {i} 手非法: {move}");

            board.Set(move!.Value.X, move.Value.Y, move.Value.Z, current);
            history.Add(new Move(move.Value.X, move.Value.Y, move.Value.Z, current));

            if (engine.CheckWin(move.Value.X, move.Value.Y, move.Value.Z, current, board))
            {
                break; // 正常终局
            }
            current = current.Opponent();
        }

        Assert.True(history.Count >= 9); // 至少展开了像样的对局
    }
}
