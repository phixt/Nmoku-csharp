namespace Nmoku.Core;

/// <summary>
/// 游戏壳 7 态（01-core §3.1）。与存档 schema 的 <see cref="GameStatus"/>（00-domain §2.7）是两层概念：
/// GameStatus 描述对局数据本身（playing/finished），GameState 描述整个壳的 UI 生命周期。
/// </summary>
public enum GameState
{
    Title,
    Playing,
    Paused,
    GameOver,
    Replay,
    GuideFromTitle,
    GuideFromGame,
}

/// <summary>状态转移触发器（01-core §3.4 Trigger 列 + GuideFromGame 的 Resume/NewGame 拆分）。</summary>
public enum GameTrigger
{
    /// <summary>开始游戏：按钮 / Space / Enter（Title、GuideFromTitle → Playing）。</summary>
    StartGame,
    /// <summary>打开引导：Title → GuideFromTitle；Paused → GuideFromGame。</summary>
    OpenGuide,
    /// <summary>引导返回：GuideFromTitle → Title。</summary>
    GuideBack,
    /// <summary>打开暂停菜单：ESC / R（Playing → Paused）。</summary>
    Pause,
    /// <summary>继续：Paused / GuideFromGame → Playing。</summary>
    Resume,
    /// <summary>新对局（调用方先经确认框）：Paused / GuideFromGame / GameOver → Playing，重开棋盘。</summary>
    NewGame,
    /// <summary>胜负判定：Playing → GameOver。</summary>
    WinDetected,
    /// <summary>返回标题：Paused / GameOver → Title。</summary>
    ReturnToTitle,
    /// <summary>读最新存档：Title / Paused → Playing。前置条件"存在有效档"由调用方校验。</summary>
    LoadLatest,
    /// <summary>进入复盘（存档槽按钮）：任意态 → Replay。前置条件"有效档已加载"由调用方校验。</summary>
    EnterReplay,
    /// <summary>退出复盘：Replay → Title（清理后，01-core §3.4）。</summary>
    ExitReplay,
}

/// <summary>
/// 7 态状态机（01-core §3.4 全转移表逐行实现）。纯函数、无副作用：
/// 条件列（有效存档存在/已加载、重开需确认）由调用方在 Fire 前把关，机器只负责拓扑合法性。
/// </summary>
public static class GameStateMachine
{
    /// <summary>
    /// 查询转移结果；不在 §3.4 表内的组合返回 null（调用方必须拒绝并按需报错）。
    /// </summary>
    public static GameState? Transition(GameState from, GameTrigger trigger)
    {
        return from switch
        {
            GameState.Title => trigger switch
            {
                GameTrigger.StartGame => GameState.Playing,
                GameTrigger.LoadLatest => GameState.Playing,
                GameTrigger.OpenGuide => GameState.GuideFromTitle,
                _ => null,
            },
            GameState.GuideFromTitle => trigger switch
            {
                GameTrigger.StartGame => GameState.Playing,
                GameTrigger.GuideBack => GameState.Title,
                _ => null,
            },
            GameState.Playing => trigger switch
            {
                GameTrigger.Pause => GameState.Paused,
                GameTrigger.WinDetected => GameState.GameOver,
                _ => null,
            },
            GameState.Paused => trigger switch
            {
                GameTrigger.Resume => GameState.Playing,
                GameTrigger.NewGame => GameState.Playing,
                GameTrigger.LoadLatest => GameState.Playing,
                GameTrigger.ReturnToTitle => GameState.Title,
                GameTrigger.OpenGuide => GameState.GuideFromGame,
                _ => null,
            },
            GameState.GuideFromGame => trigger switch
            {
                GameTrigger.Resume => GameState.Playing,
                GameTrigger.NewGame => GameState.Playing,
                _ => null,
            },
            GameState.GameOver => trigger switch
            {
                GameTrigger.NewGame => GameState.Playing,
                GameTrigger.ReturnToTitle => GameState.Title,
                _ => null,
            },
            GameState.Replay => trigger switch
            {
                GameTrigger.ExitReplay => GameState.Title,
                GameTrigger.EnterReplay => GameState.Replay, // "Any → REPLAY"：复盘期间可换另一档
                _ => null,
            },
            _ => null,
        };
    }

    public static bool IsLegal(GameState from, GameTrigger trigger) =>
        Transition(from, trigger) != null;
}
