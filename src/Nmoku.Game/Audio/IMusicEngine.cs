namespace Nmoku.Game.Audio;

/// <summary>BGM 轨道（migration-plan §3.5：menu/game/guide/pause 四轨；语义对齐源 BGMTrack）。</summary>
public enum BgmTrack
{
    Menu,
    Game,
    Guide,
    Pause,
}

/// <summary>
/// 音乐引擎接口（§3.5）：采样版 <see cref="SampleMusicEngine"/> 为默认装配，
/// 合成器版 <see cref="SynthMusicEngine"/> 留壳挂后续版本。
/// </summary>
public interface IMusicEngine
{
    /// <summary>切换/开始播放轨道；同轨且在播应直接返回（源 playBGM 同轨守卫，B20 ②）。</summary>
    void Start(BgmTrack track);

    /// <summary>停止播放。</summary>
    void Stop();

    /// <summary>动态强度 0–1（audio_var.md §五：源默认 0 不驱动任何参数；采样版留空）。</summary>
    void SetIntensity(float value);
}
