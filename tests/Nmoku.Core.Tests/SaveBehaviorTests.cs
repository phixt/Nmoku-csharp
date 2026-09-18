using Nmoku.Core.Saves;

namespace Nmoku.Core.Tests;

/// <summary>test-and-cicd-plan §4.3 + 04-contracts §6.3：校验/迁移/Base64/槽位门面。</summary>
public class SaveBehaviorTests
{
    private static SaveData NewSave(int boardSize = 9, int layers = 3, long timestamp = 1000)
    {
        var boardState = Enumerable.Range(0, layers)
            .Select(_ => Enumerable.Range(0, boardSize).Select(_ => new int[boardSize]).ToArray())
            .ToArray();
        return new SaveData
        {
            Id = Guid.NewGuid().ToString(),
            Version = "2.0.0",
            Timestamp = timestamp,
            BoardState = boardState,
            BoardSize = boardSize,
            Layers = layers,
        };
    }

    // ---- validateSaveData ----

    [Fact]
    public void Validate_GoodFixture_ReturnsNull()
    {
        using var doc = System.Text.Json.JsonDocument.Parse(Fixture.Text("v2_standard_inprogress.json"));
        Assert.Null(SaveValidator.Validate(doc.RootElement));
    }

    [Fact]
    public void Validate_MissingVersion_ReturnsError()
    {
        using var doc = System.Text.Json.JsonDocument.Parse(Fixture.Text("v1_legacy_no_version.json"));
        Assert.Equal(SaveValidator.InvalidStructure, SaveValidator.Validate(doc.RootElement));
    }

    [Fact]
    public void Validate_WrongDims_ReturnsError()
    {
        using var doc = System.Text.Json.JsonDocument.Parse(Fixture.Text("corrupt_wrong_dims.json"));
        Assert.Equal(SaveValidator.InvalidStructure, SaveValidator.Validate(doc.RootElement));
    }

    [Fact]
    public void Validate_NonObject_ReturnsError()
    {
        using var doc = System.Text.Json.JsonDocument.Parse("[1,2,3]");
        Assert.Equal(SaveValidator.InvalidStructure, SaveValidator.Validate(doc.RootElement));
    }

    [Fact]
    public void Validate_RowLengthMismatch_ReturnsError()
    {
        var data = NewSave();
        data.BoardState[0][0] = new int[4]; // 行宽 ≠ boardSize
        Assert.Equal(SaveValidator.InvalidStructure, SaveValidator.Validate(data));
    }

    // ---- migrateData（v1 → v2） ----

    [Fact]
    public void Migrate_V1WithThreeMoves_RebuildsBoardState()
    {
        var migrated = SaveMigrator.Normalize(Fixture.Text("v1_legacy_no_version.json"))!;

        Assert.Equal("2.0.0", migrated.Version);
        Assert.NotEmpty(migrated.Id);
        var (black, white) = (0, 0);
        foreach (var m in migrated.Moves)
        {
            Assert.Equal(m.Player, migrated.BoardState[m.Z][m.Y][m.X]);
            if (m.Player == 1) black++; else white++;
        }
        Assert.Equal(2, black);
        Assert.Equal(1, white);
    }

    [Fact]
    public void Migrate_V1Minimal_FillsDefaults13x6()
    {
        var migrated = SaveMigrator.Normalize(Fixture.Text("v1_legacy_minimal.json"))!;

        Assert.Equal(13, migrated.BoardSize);
        Assert.Equal(6, migrated.Layers);
        Assert.Equal("gomoku", migrated.Rules);
        Assert.Equal("pvp", migrated.GameMode);
        Assert.Equal(3.5, migrated.LayerSpacing);
        Assert.Equal(2, migrated.Moves.Count);
    }

    [Fact]
    public void Migrate_V1WithOutOfRangeMove_SkipsIllegalCoordinate()
    {
        const string json = """
            {"moves":[{"x":0,"y":0,"z":0,"player":1},{"x":99,"y":0,"z":0,"player":2}]}
            """;
        var migrated = SaveMigrator.Normalize(json)!;
        Assert.Equal(2, migrated.Moves.Count); // 历史保留原样
        Assert.Equal(1, migrated.BoardState[0][0][0]);
        Assert.Equal(0, migrated.BoardState[0][0][1]); // 越界子不写上盘
    }

    [Fact]
    public void Normalize_V2Fixture_PassesThroughWithoutRebuild()
    {
        var loaded = SaveMigrator.Normalize(Fixture.Text("v2_finished_black_win.json"))!;
        Assert.Equal("2.0.0", loaded.Version);
        Assert.Equal(9, loaded.Moves.Count);
        Assert.Equal(13, loaded.BoardSize);
        // v2_finished 夹具中黑在 y=6,z=3 横向五连
        using var doc = System.Text.Json.JsonDocument.Parse(Fixture.Text("v2_finished_black_win.json"));
        var board = ToBoard(loaded);
        Assert.Equal(CellState.Black, Rules.CheckWinner(board, 10, 6, 3));
    }

    private static Board ToBoard(SaveData data)
    {
        var board = new Board(data.BoardSize, data.Layers);
        for (int z = 0; z < data.Layers; z++)
            for (int y = 0; y < data.BoardSize; y++)
                for (int x = 0; x < data.BoardSize; x++)
                    board.Set(x, y, z, (CellState)data.BoardState[z][y][x]);
        return board;
    }

    [Fact]
    public void SplitMonolith_WithNullSlot_SkipsNullAndKeepsIndices()
    {
        // 夹具第 3 条目缺 id：源 tryMigrate 语义为跳过（!data.id → continue）
        var parts = SaveMigrator.SplitMonolith(Fixture.Text("legacy_monolith_v3.json"));

        Assert.Single(parts);
        Assert.Equal(0, parts[0].Index);
        Assert.Equal("2.0.0", parts[0].Save.Version);

        // null 不占 id 检查但占数组下标：第 3 条目（下标 2）落在槽 2
        const string json = """
            [null,{"id":"a","moves":[]},{"id":"b","moves":[]}]
            """;
        var withGap = SaveMigrator.SplitMonolith(json);
        Assert.Equal([1, 2], withGap.Select(p => p.Index));
    }

    // ---- Base64 剪贴板往返 ----

    [Fact]
    public void Base64_RoundTrip_PreservesAllFields()
    {
        var data = NewSave();
        data.Moves.Add(new SaveMove { X = 1, Y = 2, Z = 0, Player = 1, Timestamp = 7, Evaluation = 3.5 });
        data.AiDifficulty = "hard";

        var b64 = SaveData.SerializeToBase64(data);
        var parsed = SaveData.TryParseFromBase64(b64)!;

        Assert.Equal(data.Id, parsed.Id);
        Assert.Equal(data.Timestamp, parsed.Timestamp);
        Assert.Equal("hard", parsed.AiDifficulty);
        Assert.Equal(3.5, parsed.Moves[0].Evaluation);
        Assert.Equal(data.BoardState.Length, parsed.BoardState.Length);
    }

    [Fact]
    public void TryParseBase64_ClipboardFixture_ParsesStandardSave()
    {
        var b64 = Fixture.Text("clipboard_v2_standard.b64.txt").Trim();
        var parsed = SaveData.TryParseFromBase64(b64)!;
        Assert.NotNull(parsed);
        Assert.StartsWith("11111111", parsed.Id);
        Assert.Equal(5, parsed.Moves.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not base64 @@!!")]
    public void TryParseBase64_InvalidInput_ReturnsNull(string input)
    {
        Assert.Null(SaveData.TryParseFromBase64(input));
    }

    [Fact]
    public void TryParseBase64_Base64OfNonJson_ReturnsNull()
    {
        var b64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("<html>"));
        Assert.Null(SaveData.TryParseFromBase64(b64));
    }

    [Fact]
    public void TryParseBase64_MissingBoardSize_ReturnsNull()
    {
        const string json = """{"version":"2.0.0","moves":[]}""";
        var b64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json));
        Assert.Null(SaveData.TryParseFromBase64(b64));
    }
}
