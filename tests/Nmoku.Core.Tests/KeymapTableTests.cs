using Nmoku.Core;
using Xunit;

namespace Nmoku.Core.Tests;

/// <summary>快捷键单一数据源（04-contracts §4.2 + §6.5）。</summary>
public class KeymapTableTests
{
    /// <summary>
    /// 运行时注册（不进 project.godot）的 action：F1 帮助屏——Main._Ready 动态注册
    /// （理由：避免 --import 回写 project.godot 的风险）。
    /// </summary>
    private static readonly string[] RuntimeRegisteredActions = ["toggle_help"];

    /// <summary>
    /// 引擎内置 action（ui_* 前缀）：project.godot 不登记（用引擎默认绑定，
    /// 如 ui_cancel=Esc），但运行时存在于 InputMap。
    /// </summary>
    private const string BuiltinActionPrefix = "ui_";

    [Fact]
    public void FindConflicts_AnyState_PrimaryKeysDoNotOverlap()
    {
        Assert.Empty(KeymapTable.FindConflicts());
    }

    [Fact]
    public void Entries_CoverEveryInputMapAction()
    {
        // 护栏自维护化（修复批 ③-6）：清单由 project.godot 的 [input] 节**生成**——
        // 此前是硬编码 32 项且早已漂移（漏 quick_load/mirror_2d），新增 action 无人能发现。
        var documented = KeymapTable.Entries.SelectMany(e => e.Actions).ToHashSet();
        var missing = ProjectGodot.InputMapActions()
            .Where(a => !documented.Contains(a))
            .OrderBy(a => a, StringComparer.Ordinal)
            .ToList();
        Assert.True(missing.Count == 0, $"未登记进 KeymapTable 的 action: {string.Join(", ", missing)}");
    }

    [Fact]
    public void Entries_ReferenceOnlyExistingActions()
    {
        // 反向护栏：表里登记的 action 必须真实存在（project.godot / 运行时注册 / 引擎内置 ui_*），
        // 否则 F1 帮助屏会广告一个根本不存在的键位。
        var existing = ProjectGodot.InputMapActions();
        foreach (string runtime in RuntimeRegisteredActions)
        {
            existing.Add(runtime);
        }
        var stale = KeymapTable.Entries
            .SelectMany(e => e.Actions)
            .Where(a => !a.StartsWith('@')) // 纯文档行（鼠标操作）无对应 action
            .Distinct()
            .Where(a => !existing.Contains(a) && !a.StartsWith(BuiltinActionPrefix))
            .OrderBy(a => a, StringComparer.Ordinal)
            .ToList();
        Assert.True(stale.Count == 0, $"KeymapTable 登记了不存在的 action: {string.Join(", ", stale)}");
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

    /// <summary>同一 action 登记多行时取「任一行允许」——confirm 同时登记标题行与引导行，
    /// 首行命中即返回会让引导行永不可达（引导屏 Space/Enter 静默失效，F1 帮助屏却在广告该键位）。</summary>
    [Theory]
    [InlineData("confirm", GameState.Title, true)]
    [InlineData("confirm", GameState.GuideFromTitle, true)]
    [InlineData("confirm", GameState.GuideFromGame, true)]
    [InlineData("confirm", GameState.Playing, false)]
    [InlineData("confirm", GameState.Paused, false)]
    [InlineData("confirm", GameState.GameOver, false)]
    [InlineData("confirm", GameState.Replay, false)]
    public void Allows_MultiRowSameAction_AnyMatchingRowSatisfies(string action, GameState state, bool expected)
    {
        Assert.Equal(expected, KeymapTable.Allows(action, state));
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
