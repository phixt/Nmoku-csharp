using Godot;

namespace Nmoku.Game.Audio;

/// <summary>
/// 合成器版音乐引擎（§3.5 留壳）：AudioStreamGenerator 复刻源 Web Audio 和弦合成器
/// （audio_var.md §一/§二：CHORDS/BGM_PATTERNS/场景 DSP/颤音），挂后续版本回填。
/// 回填前不装配——AudioManager 默认装配 <see cref="SampleMusicEngine"/>。
/// </summary>
public sealed partial class SynthMusicEngine : Node, IMusicEngine
{
    public void Start(string slot)
    {
    }

    public void Stop()
    {
    }

    public void SetIntensity(float value)
    {
    }
}
