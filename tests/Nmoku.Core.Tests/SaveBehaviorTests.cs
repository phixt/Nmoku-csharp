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

    [Fact]
    public void Validate_MidLayerRowWidthMismatch_ReturnsError()
    {
        var data = NewSave();
        data.BoardState[1][5] = new int[4]; // y>0 行错配：旧实现只查 layer[0] 会放行
        Assert.Equal(SaveValidator.InvalidStructure, SaveValidator.Validate(data));
    }

    [Fact]
    public void Validate_JsonElementMidRowWidthMismatch_ReturnsError()
    {
        // 第 0 层第 1 行宽度 2 ≠ boardSize 3：首行正常，旧实现放行
        const string json = """
            {"version":"2.0.0","boardSize":3,"layers":2,"moves":[],
             "boardState":[[[0,0,0],[0,0],[0,0,0]],[[0,0,0],[0,0,0],[0,0,0]]]}
            """;
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal(SaveValidator.InvalidStructure, SaveValidator.Validate(doc.RootElement));
    }

    // ---- 偶数阶存档不合法（批9 定案 2026-09-26：全链路封闭偶数阶）----

    [Fact]
    public void Validate_EvenBoardSize_ReturnsEvenError()
    {
        var data = NewSave(boardSize: 10);
        Assert.Equal(SaveValidator.InvalidEvenBoardSize, SaveValidator.Validate(data));
    }

    [Fact]
    public void Validate_JsonElementEvenBoardSize_ReturnsEvenError()
    {
        using var doc = System.Text.Json.JsonDocument.Parse(SaveData.Serialize(NewSave(boardSize: 10)));
        Assert.Equal(SaveValidator.InvalidEvenBoardSize, SaveValidator.Validate(doc.RootElement));
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
        // 但校验层判废（修复批 ②-8）：越界手在复盘重放时会「静默消失」→ 整档不受理
        Assert.Equal(SaveValidator.InvalidMove, SaveValidator.Validate(migrated));
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

    // ---- 统一读档入口对畸形输入的契约：一律 null，不抛异常（SaveArchive 契约） ----

    [Theory]
    [InlineData("""{"version":"2.0.0","moves":"x","boardState":[]}""")]      // moves 类型错
    [InlineData("""{"version":"2.0.0","moves":[1,2],"boardState":[]}""")]   // moves 元素非对象
    [InlineData("""{"version":"2.0.0","timestamp":1.5,"moves":[]}""")]      // timestamp 非整数
    [InlineData("""{"version":"2.0.0","boardSize":99999,"layers":99999}""")]// 尺寸越界
    [InlineData("""{"version":"2.0.0","boardSize":0,"layers":6}""")]        // 尺寸为零
    [InlineData("""{"version":"2.0.0","layers":99999,"moves":[]}""")]       // 层数越界
    [InlineData("""{"moves":[{"x":0,"y":0,"z":0,"player":1}],"boardSize":99999}""")] // v1 迁移路径尺寸越界
    public void Normalize_StructurallyMalformedJson_ReturnsNullWithoutThrowing(string json)
    {
        Assert.Null(SaveMigrator.Normalize(json));
    }

    [Fact]
    public void TryParsePackage_StructurallyMalformedJson_ReturnsNullWithoutThrowing()
    {
        // 统一读档入口（剪贴板/文件导入的唯一闸门）：契约是"无法识别/校验失败一律 null，不抛异常"
        const string json = """{"version":"2.0.0","moves":"x","boardState":[]}""";
        Assert.Null(SaveArchive.TryParsePackage(json));
        Assert.Null(SaveArchive.TryParse(json));
    }

    [Fact]
    public void Normalize_NonObjectElement_ReturnsNull()
    {
        using var doc = System.Text.Json.JsonDocument.Parse("""[1,2,3]""");
        Assert.Null(SaveMigrator.Normalize(doc.RootElement));
    }

    [Fact]
    public void Normalize_OversizedBoardDimensions_DoesNotAllocate()
    {
        // 尺寸闸门：盘面按声明值分配，越界值必须先判废——否则百字节输入即换来 GB 级分配
        const string json = """{"version":"2.0.0","boardSize":99999,"layers":99999,"moves":[]}""";
        Assert.Null(SaveMigrator.Normalize(json));
    }

    // ---- 垃圾 JSON 不再迁成合法空档（修复批 ②-6）----

    [Theory]
    [InlineData("""{"foo":"bar"}""")]
    [InlineData("""{"id":"x","timestamp":1757000000000}""")]
    [InlineData("""{"id":"x","moves":null}""")]
    [InlineData("""{"id":"x","moves":{}}""")]
    public void Normalize_GarbageJsonWithoutMovesArray_ReturnsNull(string json)
    {
        // 此前：无 version 的任意 JSON 都被迁成「合法空存档」，且缺失 timestamp 被补成当前时刻
        // → 该垃圾档成为「最新档」，用户「读最新」读到空盘
        Assert.Null(SaveMigrator.Normalize(json));
    }

    [Fact]
    public void Normalize_V1WithEmptyMovesArray_IsAcceptedAsEmptyArchive()
    {
        // 边界：v1 档带空 moves 数组 = 合法空对局（源存档恒有 moves 数组，只是可能为空）
        var migrated = SaveMigrator.Normalize("""{"moves":[]}""");

        Assert.NotNull(migrated);
        Assert.Empty(migrated!.Moves);
        Assert.Equal("2.0.0", migrated.Version);
        Assert.Null(SaveValidator.Validate(migrated));
    }

    [Fact]
    public void TryParsePackage_GarbageJson_ReturnsNull()
    {
        Assert.Null(SaveArchive.TryParsePackage("""{"foo":"bar"}"""));
    }

    // ---- 值域与规则白名单（修复批 ②-8）----

    private static SaveData ValidArchive()
    {
        var data = NewSave(boardSize: 9, layers: 3);
        data.Moves.Add(new SaveMove { X = 1, Y = 2, Z = 1, Player = 1 });
        data.BoardState[1][2][1] = 1;
        return data;
    }

    [Fact]
    public void Validate_MoveCoordinateOutOfRange_ReturnsInvalidMove()
    {
        var data = ValidArchive();
        data.Moves[0].X = 99; // 超出 boardSize
        Assert.Equal(SaveValidator.InvalidMove, SaveValidator.Validate(data));

        data.Moves[0].X = 1;
        data.Moves[0].Z = 9; // 超出 layers
        Assert.Equal(SaveValidator.InvalidMove, SaveValidator.Validate(data));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(-1)]
    public void Validate_MovePlayerOutOfDomain_ReturnsInvalidMove(int player)
    {
        var data = ValidArchive();
        data.Moves[0].Player = player; // 只允许 1 黑 / 2 白
        Assert.Equal(SaveValidator.InvalidMove, SaveValidator.Validate(data));
    }

    [Fact]
    public void Validate_BoardStateCellValueOutOfDomain_ReturnsInvalidCellValue()
    {
        var data = ValidArchive();
        data.BoardState[0][0][0] = 9; // 越界格值 → (CellState) 强转成永久封锁的僵尸格
        Assert.Equal(SaveValidator.InvalidCellValue, SaveValidator.Validate(data));
    }

    [Fact]
    public void Validate_RulesRenju_ReturnsUnsupportedRules()
    {
        // renju 引擎未实装（RuleEngineFactory 抛 NotSupportedException）：此类档永不可载入
        var data = ValidArchive();
        data.Rules = "renju";
        Assert.Equal(SaveValidator.UnsupportedRules, SaveValidator.Validate(data));

        using var doc = System.Text.Json.JsonDocument.Parse(SaveData.Serialize(data));
        Assert.Equal(SaveValidator.UnsupportedRules, SaveValidator.Validate(doc.RootElement));
    }

    [Fact]
    public void TryParsePackage_RenjuArchive_ReturnsNull()
    {
        // 导入闸门：renju 档此前能通过导入、直到读档才炸（且炸在半加载之后）
        var data = ValidArchive();
        data.Rules = "renju";
        Assert.Null(SaveArchive.TryParsePackage(SaveData.Serialize(data)));
    }

    [Theory]
    [InlineData("gomoku")]
    [InlineData("GOMOKU")]
    [InlineData("swap2")]
    public void Validate_SupportedRules_Accepted(string rules)
    {
        var data = ValidArchive();
        data.Rules = rules;
        Assert.Null(SaveValidator.Validate(data));
    }
}
