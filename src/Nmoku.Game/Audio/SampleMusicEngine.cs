using Godot;

namespace Nmoku.Game.Audio;

/// <summary>
/// 采样版音乐引擎（§3.5 默认装配）：assets/audio/bgm_*.ogg 四轨循环，Bgm 总线。
/// 当前资产为 tools/AudioBake 生成的占位循环；正式 BGM 走 Strudel 谱面渲染替换
/// （assets-src/music/strudel/README.md 渲染管线）。
/// </summary>
public sealed partial class SampleMusicEngine : Node, IMusicEngine
{
    private AudioStreamPlayer _player = null!;
    private readonly Dictionary<BgmTrack, AudioStream> _streams = [];
    private BgmTrack? _current;

    public override void _Ready()
    {
        _player = new AudioStreamPlayer { Bus = "Bgm" };
        AddChild(_player);
        foreach (BgmTrack track in Enum.GetValues<BgmTrack>())
        {
            string path = $"res://assets/audio/bgm_{track.ToString().ToLowerInvariant()}.ogg";
            var stream = GD.Load<AudioStream>(path);
            if (stream is AudioStreamOggVorbis ogg)
            {
                ogg.Loop = true; // 循环在运行时声明，避免依赖导入元数据
            }
            _streams[track] = stream;
        }
    }

    public void Start(BgmTrack track)
    {
        if (_current == track && _player.Playing)
        {
            return;
        }
        _current = track;
        _player.Stream = _streams[track];
        _player.Play();
    }

    public void Stop()
    {
        _player.Stop();
        _current = null;
    }

    public void SetIntensity(float value)
    {
        // 采样版不驱动参数（源 intensity 默认 0 无效果；合成器回填时按 audio_var.md §五实现）
    }
}
