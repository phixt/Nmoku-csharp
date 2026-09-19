using Nmoku.Core;

namespace Nmoku.Game;

/// <summary>
/// 快捷键 context 门控（04-contracts §4.2.2/§4.2.3）。表外组合一律放行给默认处理的动作
/// 不在此登记；未列出的 action 视为全状态可用（如 M5 的 toggle_theme/toggle_mute）。
/// 匹配即吞掉事件的原则由 Main 的处理顺序保证。
/// </summary>
public static class ShortcutRouter
{
    /// <summary>action 在当前状态下是否生效。</summary>
    public static bool Allows(string action, GameState state)
    {
        switch (action)
        {
            // ---- 状态机与壳（§4.2.2 标准表）----
            case "pause":
                return state == GameState.Playing;
            case "resume":
                return state == GameState.Paused;
            case "confirm":
                return state is GameState.Title or GameState.GuideFromTitle or GameState.GuideFromGame;
            case "load_latest":
                return state is GameState.Title or GameState.Paused;
            case "quick_save":
            case "undo":
                return state == GameState.Playing;
            case "restart":
            case "return_to_title":
            case "open_guide":
                return state == GameState.Paused;

            // ---- 对局玩法（M3）----
            case "layer_prev":
            case "layer_next":
            case "toggle_helper_lines":
            case "toggle_colorblind":
            case "mirror_2d":
                return state == GameState.Playing;

            // ---- 相机/视觉（PLAYING + REPLAY，§4.2.2 rotate/zoom 行 + 决策 #9 平移）----
            case "rotate_left":
            case "rotate_right":
            case "zoom_in":
            case "zoom_out":
            case "pan_left":
            case "pan_right":
            case "pan_up":
            case "pan_down":
            case "spacing_compress":
            case "spacing_expand":
            case "spacing_reset":
            case "toggle_grid_all":
                return state is GameState.Playing or GameState.Replay;

            // ---- 复盘（§4.2.2）----
            case "replay_prev":
            case "replay_next":
            case "exit_replay":
                return state == GameState.Replay;

            default:
                return true; // 未登记动作（含 M5 全局键）不拦截
        }
    }
}
