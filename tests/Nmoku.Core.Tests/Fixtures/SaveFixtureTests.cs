using System.Text;
using System.Text.Json;

namespace Nmoku.Core.Tests.Fixtures;

/// <summary>
/// 夹具守卫测试：只用 JsonDocument 做形状断言（不依赖尚未实现的 SaveData 类型），
/// 保证样本自身的一致性；M1/M4 实现真实反序列化后再补行为级测试。
/// </summary>
public class SaveFixtureTests
{
    private static string FixturePath(string name) =>
        System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    private static JsonDocument Load(string name) =>
        JsonDocument.Parse(File.ReadAllText(FixturePath(name)));

    private static void AssertV2Shape(JsonElement root)
    {
        Assert.Equal("2.0.0", root.GetProperty("version").GetString());
        int boardSize = root.GetProperty("boardSize").GetInt32();
        int layers = root.GetProperty("layers").GetInt32();
        var boardState = root.GetProperty("boardState");
        Assert.Equal(layers, boardState.GetArrayLength());
        foreach (var layer in boardState.EnumerateArray())
        {
            Assert.Equal(boardSize, layer.GetArrayLength());
            foreach (var row in layer.EnumerateArray())
                Assert.Equal(boardSize, row.GetArrayLength());
        }
        foreach (var m in root.GetProperty("moves").EnumerateArray())
        {
            Assert.InRange(m.GetProperty("x").GetInt32(), 0, boardSize - 1);
            Assert.InRange(m.GetProperty("y").GetInt32(), 0, boardSize - 1);
            Assert.InRange(m.GetProperty("z").GetInt32(), 0, layers - 1);
            Assert.InRange(m.GetProperty("player").GetInt32(), 1, 2);
        }
    }

    [Theory]
    [InlineData("v2_standard_inprogress.json")]
    [InlineData("v2_swap2_pve.json")]
    [InlineData("v2_finished_black_win.json")]
    public void V2Fixture_IsSelfConsistent_Parses(string file)
    {
        using var doc = Load(file);
        AssertV2Shape(doc.RootElement);
    }

    [Fact]
    public void V2Fixture_BoardState_MatchesMoves()
    {
        using var doc = Load("v2_standard_inprogress.json");
        var root = doc.RootElement;
        foreach (var m in root.GetProperty("moves").EnumerateArray())
        {
            int cell = root.GetProperty("boardState")
                [m.GetProperty("z").GetInt32()]
                [m.GetProperty("y").GetInt32()]
                [m.GetProperty("x").GetInt32()]
                .GetInt32();
            Assert.Equal(m.GetProperty("player").GetInt32(), cell);
        }
    }

    [Fact]
    public void V1Legacy_NoVersion_LacksVersionAndBoardState_ForcesMigration()
    {
        using var doc = Load("v1_legacy_no_version.json");
        Assert.False(doc.RootElement.TryGetProperty("version", out _));
        Assert.False(doc.RootElement.TryGetProperty("boardState", out _));
        Assert.Equal(3, doc.RootElement.GetProperty("moves").GetArrayLength());
    }

    [Fact]
    public void V1Legacy_Minimal_OnlyHasMoves()
    {
        using var doc = Load("v1_legacy_minimal.json");
        Assert.Equal(2, doc.RootElement.GetProperty("moves").GetArrayLength());
        Assert.False(doc.RootElement.TryGetProperty("boardSize", out _));
    }

    [Fact]
    public void LegacyMonolith_IsArrayWithNullSlot()
    {
        using var doc = Load("legacy_monolith_v3.json");
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.Equal(3, doc.RootElement.GetArrayLength());
        Assert.Equal(JsonValueKind.Null, doc.RootElement[1].ValueKind);
    }

    [Fact]
    public void CorruptWrongDims_BoardLengthMismatchValidatorContract()
    {
        using var doc = Load("corrupt_wrong_dims.json");
        var root = doc.RootElement;
        Assert.NotEqual(root.GetProperty("layers").GetInt32(),
                        root.GetProperty("boardState").GetArrayLength());
    }

    [Fact]
    public void ClipboardBase64_DecodesToStandardFixture()
    {
        var b64 = File.ReadAllText(FixturePath("clipboard_v2_standard.b64.txt")).Trim();
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(b64));
        using var decoded = JsonDocument.Parse(json);
        AssertV2Shape(decoded.RootElement);
        using var original = Load("v2_standard_inprogress.json");
        Assert.Equal(original.RootElement.GetProperty("id").GetString(),
                     decoded.RootElement.GetProperty("id").GetString());
    }

    [Fact]
    public void SlotIndex_HasQuickSaveAtSlot5()
    {
        using var doc = Load("slot_index_example.json");
        var slots = doc.RootElement.EnumerateArray().ToList();
        Assert.Equal(2, slots.Count);
        Assert.Equal(5, slots[^1].GetProperty("index").GetInt32());
        Assert.True(slots[^1].GetProperty("timestamp").GetInt64() >
                    slots[0].GetProperty("timestamp").GetInt64());
    }
}
