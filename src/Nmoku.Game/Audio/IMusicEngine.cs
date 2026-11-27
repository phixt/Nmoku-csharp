namespace Nmoku.Game.Audio;

/// <summary>
/// BGM 场景键（Main.cs 路由兼容薄壳，语义对齐源 BGMTrack）。槽位已字符串化（2026-10-02）：
/// 新场景零枚举接入——丢 <c>bgm_&lt;slot&gt;.ogg</c> 文件后经 AudioManager.PlayBgm("&lt;slot&gt;")
/// 调用即生效，无需扩此枚举。
/// </summary>
public enum BgmTrack
{
    Menu,
    Game,
    Guide,
    Pause,
}

/// <summary>当前 BGM 播放实时状态快照（供 BGM 浏览界面进度条与曲目高亮使用）。</summary>
public sealed record BgmPlaybackStatus(
    string? Slot,
    string? TrackId,
    double PositionSec,
    double LengthSec,
    bool IsPlaying);

/// <summary>
/// 音乐引擎接口（§3.5）：采样版 <see cref="SampleMusicEngine"/> 为默认装配，
/// 合成器版 <see cref="SynthMusicEngine"/> 留壳挂后续版本。槽位=场景键字符串（解耦场景绑定）。
/// </summary>
public interface IMusicEngine
{
    /// <summary>切换/开始播放槽位；同槽且在播应直接返回（源 playBGM 同轨守卫，B20 ②）。</summary>
    void Start(string slot);

    /// <summary>停止播放（立即，退出/卸载用；场景内切换走 Start 内建交叉淡化）。</summary>
    void Stop();

    /// <summary>动态强度 0–1（audio_var.md §五：源默认 0 不驱动任何参数；采样版留空）。</summary>
    void SetIntensity(float value);

    /// <summary>
    /// 声明某槽位将进入新一轮会话（如开新局/重开/切关卡）：在多曲池中触发选曲轮换或亲和曲切换，
    /// 并让新局曲目从头播放；暂停/引导往返不调用此方法，保持会话内同曲续播与优雅回归。
    /// </summary>
    void PrepareSlotSession(string slot, bool rotate = true, string? preferredTrackId = null)
    {
    }

    /// <summary>设置页修改音乐偏好（定曲/静默/全局锁定）后，对当前槽位即时重估生效。</summary>
    void RefreshCurrentSlot()
    {
    }

    /// <summary>BGM 浏览界面直选试听：直接交叉淡化切至指定曲目（从头起播）。</summary>
    void PlayTrackDirect(string slot, string trackId)
    {
    }

    /// <summary>BGM 浏览界面拖动进度条跳转。</summary>
    void SeekActive(double seconds)
    {
    }

    /// <summary>读取当前主在播轨位状态。</summary>
    BgmPlaybackStatus GetPlaybackStatus() => new(null, null, 0, 0, false);
}
