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
}
