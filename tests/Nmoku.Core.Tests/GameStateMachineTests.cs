using Nmoku.Core;
using Xunit;

namespace Nmoku.Core.Tests;

/// <summary>7 态状态机（01-core §3.4 转移表全量核对）。</summary>
public class GameStateMachineTests
{
    /// <summary>§3.4 全表：每行一条合法边 (from, trigger, to)。与文档逐行对应，新增边须先改文档。</summary>
    private static readonly (GameState From, GameTrigger Trigger, GameState To)[] LegalEdges =
    [
        (GameState.Title, GameTrigger.StartGame, GameState.Playing),
        (GameState.Title, GameTrigger.LoadLatest, GameState.Playing),
        (GameState.Title, GameTrigger.OpenGuide, GameState.GuideFromTitle),
        (GameState.GuideFromTitle, GameTrigger.StartGame, GameState.Playing),
        (GameState.GuideFromTitle, GameTrigger.GuideBack, GameState.Title),
        (GameState.Playing, GameTrigger.Pause, GameState.Paused),
        (GameState.Playing, GameTrigger.WinDetected, GameState.GameOver),
        (GameState.Paused, GameTrigger.Resume, GameState.Playing),
        (GameState.Paused, GameTrigger.NewGame, GameState.Playing),
        (GameState.Paused, GameTrigger.LoadLatest, GameState.Playing),
        (GameState.Paused, GameTrigger.ReturnToTitle, GameState.Title),
        (GameState.Paused, GameTrigger.OpenGuide, GameState.GuideFromGame),
        (GameState.GuideFromGame, GameTrigger.Resume, GameState.Playing),
        (GameState.GuideFromGame, GameTrigger.NewGame, GameState.Playing),
        (GameState.GameOver, GameTrigger.NewGame, GameState.Playing),
        (GameState.GameOver, GameTrigger.ReturnToTitle, GameState.Title),
        (GameState.Replay, GameTrigger.ExitReplay, GameState.Title),
        (GameState.Replay, GameTrigger.EnterReplay, GameState.Replay),
    ];

    public static IEnumerable<object[]> AllLegalEdges() =>
        LegalEdges.Select(e => new object[] { e.From, e.Trigger, e.To });

    [Theory]
    [MemberData(nameof(AllLegalEdges))]
    public void Transition_LegalEdge_ReturnsTarget(GameState from, GameTrigger trigger, GameState to)
    {
        Assert.Equal(to, GameStateMachine.Transition(from, trigger));
    }

    [Fact]
    public void Transition_EveryUnlistedCombination_IsRejected()
    {
        var legal = LegalEdges.Select(e => (e.From, e.Trigger)).ToHashSet();
        foreach (GameState from in Enum.GetValues<GameState>())
        {
            foreach (GameTrigger trigger in Enum.GetValues<GameTrigger>())
            {
                bool expectLegal = legal.Contains((from, trigger));
                Assert.Equal(expectLegal, GameStateMachine.IsLegal(from, trigger));
            }
        }
    }

    [Fact]
    public void Transition_TableCoversAllStates_EveryStateHasAtLeastOneEdge()
    {
        var states = LegalEdges.SelectMany(e => new[] { e.From, e.To }).ToHashSet();
        Assert.Equal(Enum.GetValues<GameState>().ToHashSet(), states);
    }

    // ---- 语义回归：文档中最容易被误读的几条 ----

    [Fact]
    public void Transition_PlayingToTitleDirectly_IsRejected()
    {
        // §3.3 图：Playing 不能直达 Title，必须经 Paused（B 键）
        Assert.Null(GameStateMachine.Transition(GameState.Playing, GameTrigger.ReturnToTitle));
    }

    [Fact]
    public void Transition_TitleToPaused_IsRejected()
    {
        Assert.Null(GameStateMachine.Transition(GameState.Title, GameTrigger.Pause));
    }

    [Fact]
    public void Transition_GuideFromGame_ToTitle_IsRejected()
    {
        // §3.4：GUIDE_FROM_GAME 只能回 PLAYING（Resume/NewGame），无直达 Title 边
        Assert.Null(GameStateMachine.Transition(GameState.GuideFromGame, GameTrigger.ReturnToTitle));
    }

    [Fact]
    public void Transition_ReplayExitsToTitle_NotToPreviousState()
    {
        // §3.4 明确 "Returns to TITLE after cleanup"，而非回到进入前的状态
        Assert.Equal(GameState.Title, GameStateMachine.Transition(GameState.Replay, GameTrigger.ExitReplay));
    }
}
