using Nmoku.Core.AI;
using Nmoku.Core.Audio;

namespace Nmoku.Core.Tests;

public class AudioPolicyTests
{
    [Fact]
    public void ResolveGameOverSfx_PveHumanWins_ReturnsWin()
    {
        Assert.Equal(GameOverSfx.Win, AudioPolicy.ResolveGameOverSfx(pve: true, winner: 1, aiColor: PlayerColor.White));
        Assert.Equal(GameOverSfx.Win, AudioPolicy.ResolveGameOverSfx(pve: true, winner: 2, aiColor: PlayerColor.Black));
    }

    [Fact]
    public void ResolveGameOverSfx_PveAiWins_ReturnsLose()
    {
        Assert.Equal(GameOverSfx.Lose, AudioPolicy.ResolveGameOverSfx(pve: true, winner: 2, aiColor: PlayerColor.White));
        Assert.Equal(GameOverSfx.Lose, AudioPolicy.ResolveGameOverSfx(pve: true, winner: 1, aiColor: PlayerColor.Black));
    }

    [Fact]
    public void ResolveGameOverSfx_PvpEitherWins_ReturnsWin()
    {
        Assert.Equal(GameOverSfx.Win, AudioPolicy.ResolveGameOverSfx(pve: false, winner: 1, aiColor: PlayerColor.White));
        Assert.Equal(GameOverSfx.Win, AudioPolicy.ResolveGameOverSfx(pve: false, winner: 2, aiColor: PlayerColor.White));
    }

    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, 0)]
    [InlineData(true, -1)]
    [InlineData(false, 3)]
    public void ResolveGameOverSfx_DrawOrInvalidWinner_ReturnsNone(bool pve, int winner)
    {
        Assert.Equal(GameOverSfx.None, AudioPolicy.ResolveGameOverSfx(pve, winner, PlayerColor.White));
    }

    [Fact]
    public void PreferAdvancedGameTrack_StandardGomokuMedium_ReturnsFalse()
    {
        var standard = new LevelConfig { Id = "classic", BoardSize = 13, Layers = 6, WinLength = 5, Rules = RuleSet.Gomoku };
        Assert.False(AudioPolicy.PreferAdvancedGameTrack(standard, pve: false, AIDifficulty.Medium));
        Assert.False(AudioPolicy.PreferAdvancedGameTrack(standard, pve: true, AIDifficulty.Easy));
        Assert.False(AudioPolicy.PreferAdvancedGameTrack(standard, pve: true, AIDifficulty.Medium));
    }

    [Fact]
    public void PreferAdvancedGameTrack_HardAiOrSwap2OrResidualOrLarge_ReturnsTrue()
    {
        var standard = new LevelConfig { Id = "classic", BoardSize = 13, Layers = 6, WinLength = 5, Rules = RuleSet.Gomoku };
        var swap2 = new LevelConfig { Id = "custom", BoardSize = 13, Layers = 6, WinLength = 5, Rules = RuleSet.Swap2 };
        var residual = new LevelConfig { Id = "residual", BoardSize = 13, Layers = 6, WinLength = 5, Rules = RuleSet.Gomoku };
        var six = new LevelConfig { Id = "custom", BoardSize = 13, Layers = 6, WinLength = 6, Rules = RuleSet.Gomoku };
        var tall = new LevelConfig { Id = "custom", BoardSize = 13, Layers = 8, WinLength = 5, Rules = RuleSet.Gomoku };

        Assert.True(AudioPolicy.PreferAdvancedGameTrack(standard, pve: true, AIDifficulty.Hard));
        Assert.True(AudioPolicy.PreferAdvancedGameTrack(swap2, pve: false, AIDifficulty.Medium));
        Assert.True(AudioPolicy.PreferAdvancedGameTrack(residual, pve: false, AIDifficulty.Medium));
        Assert.True(AudioPolicy.PreferAdvancedGameTrack(six, pve: false, AIDifficulty.Medium));
        Assert.True(AudioPolicy.PreferAdvancedGameTrack(tall, pve: false, AIDifficulty.Medium));
    }

    [Fact]
    public void SelectNextTrackId_WithinSameSession_KeepsCurrentTrackAcrossPauseAndReturn()
    {
        string[] pool = ["bgm_game", "bgm_game_2"];
        string? pick = AudioPolicy.SelectNextTrackId(
            pool,
            currentSessionId: "bgm_game",
            lastId: "bgm_game",
            slotOverride: "auto",
            playMode: "random",
            rotate: false,
            preferredId: "bgm_game_2");

        Assert.Equal("bgm_game", pick);
    }

    [Fact]
    public void SelectNextTrackId_NewMatchRotate_AlternatesTwoTrackPool()
    {
        string[] pool = ["bgm_game", "bgm_game_2"];

        // First standard match picks standard track
        string? first = AudioPolicy.SelectNextTrackId(pool, null, null, "auto", "random", rotate: true, preferredId: "bgm_game");
        Assert.Equal("bgm_game", first);

        // Second standard match rotates to bgm_game_2 rather than repeating bgm_game forever
        string? second = AudioPolicy.SelectNextTrackId(pool, first, first, "auto", "random", rotate: true, preferredId: "bgm_game");
        Assert.Equal("bgm_game_2", second);

        // Third standard match returns to bgm_game
        string? third = AudioPolicy.SelectNextTrackId(pool, second, second, "auto", "random", rotate: true, preferredId: "bgm_game");
        Assert.Equal("bgm_game", third);
    }

    [Fact]
    public void SelectNextTrackId_OverrideOffOrPinned_RespectsPreference()
    {
        string[] pool = ["bgm_game", "bgm_game_2"];
        Assert.Null(AudioPolicy.SelectNextTrackId(pool, "bgm_game", "bgm_game", "off", "random", rotate: false));
        Assert.Equal("bgm_game_2", AudioPolicy.SelectNextTrackId(pool, "bgm_game", "bgm_game", "bgm_game_2", "random", rotate: false));
        // Unknown pinned track falls back to auto
        Assert.Equal("bgm_game", AudioPolicy.SelectNextTrackId(pool, "bgm_game", "bgm_game", "bgm_missing", "random", rotate: false));
    }

    [Fact]
    public void SelectNextTrackId_SequenceMode_AdvancesInOrder()
    {
        string[] pool = ["bgm_game", "bgm_game_2", "bgm_game_3"];
        Assert.Equal("bgm_game_2", AudioPolicy.SelectNextTrackId(pool, null, "bgm_game", "auto", "sequence", rotate: true));
        Assert.Equal("bgm_game_3", AudioPolicy.SelectNextTrackId(pool, null, "bgm_game_2", "auto", "sequence", rotate: true));
        Assert.Equal("bgm_game", AudioPolicy.SelectNextTrackId(pool, null, "bgm_game_3", "auto", "sequence", rotate: true));
    }

    [Fact]
    public void SfxDebounceGate_SameChannelWithin65MsBlocked_DifferentChannelAllowed()
    {
        var gate = new SfxDebounceGate(65.0);
        Assert.True(gate.TryAcquire((int)PlayerColor.Black, 100.0));
        Assert.False(gate.TryAcquire((int)PlayerColor.Black, 140.0)); // 40ms < 65ms → blocked
        Assert.True(gate.TryAcquire((int)PlayerColor.White, 115.0));  // AI white reply 15ms later → allowed
        Assert.True(gate.TryAcquire((int)PlayerColor.Black, 165.0));  // 65ms exact → allowed
        Assert.False(gate.TryAcquire((int)PlayerColor.White, 179.9)); // 64.9ms < 65ms → blocked
        Assert.True(gate.TryAcquire((int)PlayerColor.White, 180.0));  // 65ms → allowed
    }

    [Fact]
    public void BgmDuckEnvelope_DucksFastAndRecoversSmoothly()
    {
        var env = new BgmDuckEnvelope(duckLevel: 0.22f, attackSec: 0.12f, releaseSec: 1.0f);
        Assert.Equal(1f, env.Current);

        // Attack halfway
        float midAttack = env.Step(0.06f, holdDucked: true);
        Assert.InRange(midAttack, 0.50f, 0.70f);

        // Fully ducked
        float bottom = env.Step(0.20f, holdDucked: true);
        Assert.Equal(0.22f, bottom, precision: 3);

        // Release halfway
        float midRelease = env.Step(0.50f, holdDucked: false);
        Assert.InRange(midRelease, 0.55f, 0.70f);

        // Fully recovered
        float top = env.Step(0.80f, holdDucked: false);
        Assert.Equal(1f, top, precision: 3);
    }

    [Fact]
    public void BgmCatalog_ContainsAllFiveFormalTracksAndResolvesById()
    {
        Assert.Equal(5, AudioPolicy.BgmCatalog.Count);
        Assert.Equal("game", AudioPolicy.FindTrackMeta("bgm_game_2")?.Slot);
        Assert.Equal("menu", AudioPolicy.FindTrackMeta("bgm_menu")?.Slot);
        Assert.Null(AudioPolicy.FindTrackMeta("bgm_unknown"));
    }
}
