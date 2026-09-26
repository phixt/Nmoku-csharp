using Nmoku.Core.Saves;

namespace Nmoku.Core;

/// <summary>
/// 事件总线（04-contracts §5）：强类型静态 C# event，回调同步调用、按发布顺序送达。
/// 外部模块只订阅不发布（发布由 Core 内部逻辑负责）。
/// </summary>
public static class EventBus
{
    public static event Action<bool>? ThemeToggled;
    public static event Action<int>? LayerChanged;
    public static event Action? BoardUpdated;
    public static event Action? GameReset;
    public static event Action<IReadOnlyList<SaveData?>>? SaveUpdated;
    public static event Action<bool>? ColorblindModeToggled;
    public static event Action? AiMoveRequest;
    public static event Action<string, float>? PlaySound;
    public static event Action<string>? LanguageChanged;

    public static void EmitThemeToggled(bool isDark) => ThemeToggled?.Invoke(isDark);
    public static void EmitLayerChanged(int layerIndex) => LayerChanged?.Invoke(layerIndex);
    public static void EmitBoardUpdated() => BoardUpdated?.Invoke();
    public static void EmitGameReset() => GameReset?.Invoke();
    public static void EmitSaveUpdated(IReadOnlyList<SaveData?> slots) => SaveUpdated?.Invoke(slots);
    public static void EmitColorblindModeToggled(bool enabled) => ColorblindModeToggled?.Invoke(enabled);
    public static void EmitAiMoveRequest() => AiMoveRequest?.Invoke();
    public static void EmitPlaySound(string soundId, float volume = 1f) => PlaySound?.Invoke(soundId, volume);
    public static void EmitLanguageChanged(string lang) => LanguageChanged?.Invoke(lang);

    /// <summary>测试辅助：清空全部订阅。</summary>
    public static void ClearAll()
    {
        ThemeToggled = null;
        LayerChanged = null;
        BoardUpdated = null;
        GameReset = null;
        SaveUpdated = null;
        ColorblindModeToggled = null;
        AiMoveRequest = null;
        PlaySound = null;
        LanguageChanged = null;
    }
}
