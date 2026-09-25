using Nmoku.Core;
using Xunit;

namespace Nmoku.Core.Tests;

/// <summary>快捷键单一数据源（04-contracts §4.2 + §6.5）。</summary>
public class KeymapTableTests
{
    /// <summary>project.godot [input] 全部自定义 action（M0 建立 + M2/M3/M4 追加）；新增 action 必须登记进表。</summary>
    private static readonly string[] AllInputMapActions =
    [
        "pause", "resume", "confirm", "load_latest", "quick_save", "save_first_empty", "restart",
        "return_to_title", "open_guide", "toggle_theme", "toggle_mute", "undo",
        "layer_prev", "layer_next", "rotate_left", "rotate_right", "zoom_in", "zoom_out",
        "replay_prev", "replay_next", "exit_replay",
        "spacing_compress", "spacing_expand", "spacing_reset",
        "toggle_helper_lines", "toggle_colorblind", "pan_up", "pan_down",
        "pan_left", "pan_right", "toggle_grid_all", "toggle_help",
    ];

    [Fact]
    public void FindConflicts_AnyState_PrimaryKeysDoNotOverlap()
    {
        Assert.Empty(KeymapTable.FindConflicts());
    }

    [Fact]
    public void Entries_CoverEveryInputMapAction()
    {
        var documented = KeymapTable.Entries.SelectMany(e => e.Actions).ToHashSet();
        var missing = AllInputMapActions.Where(a => !documented.Contains(a)).ToList();
        Assert.True(missing.Count == 0, $"未登记进 KeymapTable 的 action: {string.Join(", ", missing)}");
    }

    [Fact]
    public void Entries_FieldsArePopulated()
    {
        Assert.All(KeymapTable.Entries, e =>
        {
            Assert.NotEmpty(e.Actions);
            Assert.NotEmpty(e.Keys);
            Assert.NotEmpty(e.Description);
            Assert.NotEmpty(e.Category);
        });
    }

    // ---- §4.2.3 context 门控语义抽查 ----

    [Theory]
    [InlineData("pause", GameState.Playing, true)]
    [InlineData("pause", GameState.Paused, false)] // 修复同键跨态双触发的前提：context 互斥
    [InlineData("resume", GameState.Paused, true)]
    [InlineData("resume", GameState.Playing, false)]
    [InlineData("confirm", GameState.Title, true)]
    [InlineData("confirm", GameState.Playing, false)]
    [InlineData("load_latest", GameState.Paused, true)]
    [InlineData("load_latest", GameState.Playing, false)]
    [InlineData("quick_save", GameState.Playing, true)]
    [InlineData("replay_prev", GameState.Replay, true)]
    [InlineData("replay_prev", GameState.Playing, false)]
    [InlineData("rotate_left", GameState.Replay, true)] // 相机键对局+复盘共享
    [InlineData("undo", GameState.GameOver, false)]
    public void Allows_StateGating_MatchesContract(string action, GameState state, bool expected)
    {
        Assert.Equal(expected, KeymapTable.Allows(action, state));
    }

    [Fact]
    public void Allows_GlobalActions_AnyState()
    {
        Assert.All(Enum.GetValues<GameState>(), s =>
        {
            Assert.True(KeymapTable.Allows("toggle_help", s));
            Assert.True(KeymapTable.Allows("toggle_theme", s));
        });
    }

    [Fact]
    public void Allows_UnknownAction_PassesThrough()
    {
        Assert.True(KeymapTable.Allows("not_a_registered_action", GameState.Playing));
    }

    [Fact]
    public void AllowsWhenOverlayOpen_OnlyGlobalRowsOptIn()
    {
        Assert.True(KeymapTable.AllowsWhenOverlayOpen("toggle_help"));
        Assert.True(KeymapTable.AllowsWhenOverlayOpen("ui_cancel"));
        Assert.False(KeymapTable.AllowsWhenOverlayOpen("pause"));
        Assert.False(KeymapTable.AllowsWhenOverlayOpen("layer_next"));
    }

    [Fact]
    public void Allows_SaveFirstEmpty_PausedAndGuideOnly()
    {
        // S 键 = 源 saveGame：暂停与对局内引导可用；Playing 下 S 属于缩放（zoom_out），不得触发保存
        Assert.True(KeymapTable.Allows("save_first_empty", GameState.Paused));
        Assert.True(KeymapTable.Allows("save_first_empty", GameState.GuideFromGame));
        Assert.False(KeymapTable.Allows("save_first_empty", GameState.Playing));
        Assert.False(KeymapTable.Allows("save_first_empty", GameState.Title));
    }

    [Fact]
    public void HintKeyOf_ReturnsPrimaryKeyForMenuHints()
    {
        // 暂停菜单六项的键位提示（与源 esc-menu kbd 对齐）：来自本表而非菜单硬编码
        Assert.Equal("Esc", KeymapTable.HintKeyOf("resume"));
        Assert.Equal("R", KeymapTable.HintKeyOf("restart"));
        Assert.Equal("S", KeymapTable.HintKeyOf("save_first_empty"));
        Assert.Equal("L", KeymapTable.HintKeyOf("load_latest"));
        Assert.Equal("G", KeymapTable.HintKeyOf("open_guide"));
        Assert.Equal("B", KeymapTable.HintKeyOf("return_to_title"));
    }

    [Fact]
    public void HintKeyOf_UnknownAction_ReturnsNull()
    {
        Assert.Null(KeymapTable.HintKeyOf("not_a_registered_action"));
    }
}
