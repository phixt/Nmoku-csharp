using Nmoku.Core;
using Xunit;

namespace Nmoku.Core.Tests;

public class KeycapScopeMapTests
{
    private static KeymapEntry Entry(Func<KeymapEntry, KeymapEntry> build) => build(new KeymapEntry
    {
        Actions = ["test_action"],
        Keys = "X",
        Description = "测试",
        Category = "测试",
    });

    [Fact]
    public void ScopeOf_CtrlCompound_TakesPrecedenceOverContexts()
    {
        var entry = Entry(e => e with { Keys = "Ctrl+S", Contexts = [GameState.Playing] });
        Assert.Equal(KeycapScope.Ctrl, KeycapScopeMap.ScopeOf(entry));
    }

    [Fact]
    public void ScopeOf_ShiftCompound_ReturnsShift()
    {
        var entry = Entry(e => e with { Keys = "Shift+WASD / Shift+方向键", Contexts = [GameState.Playing, GameState.Replay] });
        Assert.Equal(KeycapScope.Shift, KeycapScopeMap.ScopeOf(entry));
    }

    [Fact]
    public void ScopeOf_GlobalRow_ReturnsGlobal()
    {
        var entry = Entry(e => e with { Keys = "T", Contexts = [] });
        Assert.Equal(KeycapScope.Global, KeycapScopeMap.ScopeOf(entry));
    }

    [Fact]
    public void ScopeOf_OverlayOnlyRow_ReturnsGlobal()
    {
        var entry = Entry(e => e with { Keys = "Esc", AllowWhenOverlay = true, OverlayOnly = true });
        Assert.Equal(KeycapScope.Global, KeycapScopeMap.ScopeOf(entry));
    }

    [Fact]
    public void ScopeOf_PlayingContext_ReturnsGame()
    {
        var entry = Entry(e => e with { Contexts = [GameState.Playing] });
        Assert.Equal(KeycapScope.Game, KeycapScopeMap.ScopeOf(entry));
    }

    [Fact]
    public void ScopeOf_PlayingPlusReplay_ReturnsGamePrimary()
    {
        // 不叠层规则：双域重叠取主域（含 Playing 即 Game），不做混合
        var entry = Entry(e => e with { Contexts = [GameState.Playing, GameState.Replay] });
        Assert.Equal(KeycapScope.Game, KeycapScopeMap.ScopeOf(entry));
    }

    [Fact]
    public void ScopeOf_ReplayOnlyContext_ReturnsReplay()
    {
        var entry = Entry(e => e with { Contexts = [GameState.Replay] });
        Assert.Equal(KeycapScope.Replay, KeycapScopeMap.ScopeOf(entry));
    }

    [Fact]
    public void ScopeOf_TitleAndPausedContexts_ReturnsPauseMenu()
    {
        var entry = Entry(e => e with { Contexts = [GameState.Title, GameState.Paused] });
        Assert.Equal(KeycapScope.PauseMenu, KeycapScopeMap.ScopeOf(entry));
    }

    [Fact]
    public void ScopeOf_AllKeymapEntries_AlwaysResolve()
    {
        Assert.All(KeymapTable.Entries, e => KeycapScopeMap.ScopeOf(e));
    }

    [Fact]
    public void DisplayName_AllScopes_NonEmptyAndUnique()
    {
        var names = Enum.GetValues<KeycapScope>().Select(KeycapScopeMap.DisplayName).ToArray();
        Assert.All(names, n => Assert.False(string.IsNullOrWhiteSpace(n)));
        Assert.Equal(names.Length, names.Distinct().Count());
    }
}

public class KeycapParserTests
{
    [Fact]
    public void Parse_PlainSingleLetter_ReturnsSingleGroup()
    {
        var groups = KeycapParser.Parse("S");
        var cap = Assert.Single(groups);
        Assert.Equal(["S"], cap.Caps);
        Assert.Equal("", cap.Modifier);
    }

    [Fact]
    public void Parse_SlashPair_SplitsIntoParallelCaps()
    {
        var groups = KeycapParser.Parse("Q / E");
        Assert.Equal(2, groups.Count);
        Assert.Equal(["Q"], groups[0].Caps);
        Assert.Equal(["E"], groups[1].Caps);
    }

    [Fact]
    public void Parse_OrAlternatives_SplitsIntoSegments()
    {
        var groups = KeycapParser.Parse("W / S 或 ↑ / ↓");
        Assert.Equal(4, groups.Count);
        Assert.Equal(["W"], groups[0].Caps);
        Assert.Equal(["↑"], groups[2].Caps);
    }

    [Fact]
    public void Parse_Combo_FirstCapIsModifier()
    {
        var groups = KeycapParser.Parse("Ctrl+S");
        var cap = Assert.Single(groups);
        Assert.Equal(["Ctrl", "S"], cap.Caps);
        Assert.Equal("Ctrl", cap.Modifier);
        Assert.Equal(["S"], cap.Keys);
    }

    [Fact]
    public void Parse_ShiftGroupRows_ExpandGroupNames()
    {
        var groups = KeycapParser.Parse("Shift+WASD / Shift+方向键");
        Assert.Equal(2, groups.Count);
        Assert.Equal(["Shift", "W", "A", "S", "D"], groups[0].Caps);
        Assert.Equal(["Shift", "↑", "↓", "←", "→"], groups[1].Caps);
    }

    [Fact]
    public void Parse_SpaceEnter_ParsesNamedKeys()
    {
        var groups = KeycapParser.Parse("Space / Enter");
        Assert.Equal(2, groups.Count);
        Assert.Equal(["Space"], groups[0].Caps);
        Assert.Equal(["Enter"], groups[1].Caps);
    }

    [Fact]
    public void Parse_MouseDocRow_ReturnsEmpty()
    {
        Assert.Empty(KeycapParser.Parse("鼠标点击 2D 面板"));
        Assert.Empty(KeycapParser.Parse("左键拖 / 中键拖"));
    }

    [Fact]
    public void Parse_FunctionKey_ParsesSingleCap()
    {
        var cap = Assert.Single(KeycapParser.Parse("F1"));
        Assert.Equal(["F1"], cap.Caps);
    }

    [Fact]
    public void Parse_AllKeymapEntries_NeverThrowAndKnownCapsExpand()
    {
        Assert.All(KeymapTable.Entries, e => Assert.All(KeycapParser.Parse(e.Keys), g => Assert.NotEmpty(g.Caps)));
    }
}

public class KeyboardLayoutTests
{
    [Fact]
    public void Find_AllKeycapsOfKeymapTable_EveryPhysicalKeyFound()
    {
        // 排版防错锁：KeymapTable 全表键帽（字母/方向键/Space）必须都能在正式布局中定位，
        // 否则键盘模拟区会出现"有功能却没位置"的排版缺口
        var missing = new List<string>();
        foreach (var entry in KeymapTable.Entries)
        {
            foreach (var group in KeycapParser.Parse(entry.Keys))
            {
                foreach (var cap in group.Caps)
                {
                    bool physical = cap.Length == 1 || cap is "Space" or "↑" or "↓" or "←" or "→";
                    if (physical && KeyboardLayout.Find(cap) == null)
                    {
                        missing.Add($"{entry.Keys} -> {cap}");
                    }
                }
            }
        }
        Assert.True(missing.Count == 0, $"布局缺位: {string.Join(", ", missing)}");
    }

    [Fact]
    public void Find_AllNames_LocateSuccessfully()
    {
        Assert.All(KeyboardLayout.AllNames, n => Assert.NotNull(KeyboardLayout.Find(n)));
    }

    [Fact]
    public void AllNames_NoDuplicates()
    {
        var names = KeyboardLayout.AllNames.ToArray();
        Assert.Equal(names.Length, names.Distinct().Count());
    }

    [Fact]
    public void Find_LetterRows_StandardAnsiStagger()
    {
        Assert.Equal(0, KeyboardLayout.Find("Q")!.Value.Key.OffsetUnits);
        Assert.Equal(0.25, KeyboardLayout.Find("A")!.Value.Key.OffsetUnits);
        Assert.Equal(0.75, KeyboardLayout.Find("Z")!.Value.Key.OffsetUnits);
    }

    [Fact]
    public void Find_UnknownName_ReturnsNull()
    {
        Assert.Null(KeyboardLayout.Find("回车"));
        Assert.Null(KeyboardLayout.Find("F1")); // 功能键不在本布局（字母区+空格+方向键）
    }
}
