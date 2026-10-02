namespace Nmoku.Game.Audio;

/// <summary>
/// BGM 调配偏好（user://settings.cfg「music」节；P3 设置页音乐子页读写同一组键）：
/// <c>slot_&lt;槽位&gt;</c> = "auto"（默认，按池策略选曲）| "off"（该槽位静默）| 曲目 Id（锁定该曲）；
/// <c>play_mode</c> = "random"（默认，不与上一首重复）| "sequence"（按池内文件序轮换）；
/// <c>global_mode</c> = "scene"（默认，随场景切）| "lock"（锁定当前曲，切场景不打断）。
/// 改槽位覆盖后需重触发 Start 才生效（P3 设置页负责；引擎每次 Start 现读）。
/// </summary>
internal static class MusicPrefs
{
    private const string Section = "music";

    public static string SlotOverride(string slot) =>
        SettingsStore.Get(Section, "slot_" + slot, "auto").AsString();

    public static string PlayMode() =>
        SettingsStore.Get(Section, "play_mode", "random").AsString();

    public static string GlobalMode() =>
        SettingsStore.Get(Section, "global_mode", "scene").AsString();
}
