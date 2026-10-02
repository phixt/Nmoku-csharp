using Godot;

namespace Nmoku.Game.Audio;

/// <summary>
/// 采样版音乐引擎（§3.5 默认装配）：assets/audio/bgm_*.ogg 四轨循环，Bgm 总线。
/// 平滑切换（2026-10-02，B21）：双播放器交叉淡化——旧轨 0.5s 淡出、新轨 0.6s 淡入并行重叠
/// （源 playBGM/startBGMPattern 的 WebAudio linearRamp 语义，B20 ②）；位置记忆（切走的轨记住
/// 播放进度，切回续播不重头）；优雅回归（切回还在淡出的轨=淡出反转为淡入）。快速连切由
/// 「瞬时音量重定目标」仲裁，同帧多请求=最后者胜，不产生重启爆音。
/// 当前资产为 tools/AudioBake 生成的占位循环；正式 BGM 走 Strudel 谱面渲染替换
/// （assets-src/music/strudel/README.md 渲染管线）。
/// </summary>
public sealed partial class SampleMusicEngine : Node, IMusicEngine
{
    private sealed class Lane
    {
        public AudioStreamPlayer Player = null!;
        public BgmTrack Track;
        public float LinVolume;     // 瞬时线性音量（淡化在玩家级，音量/静音在总线级，正交）
        public float TargetVolume;
        /// <summary>挂着曲子（在播或淡出中）即占用；空闲才可指派。</summary>
        public bool Busy => Player.Playing || TargetVolume > 0f;
    }

    private const float FadeOutSec = 0.5f; // 源 playBGM：旧 bgmGain linearRamp→0（AudioManager.ts:795）
    private const float FadeInSec = 0.6f;  // 源 startBGMPattern：新 bgmGain 0→target（AudioManager.ts:722）

    private readonly Lane[] _lanes = [new Lane(), new Lane()];
    private readonly Dictionary<BgmTrack, AudioStream> _streams = [];
    private readonly Dictionary<BgmTrack, double> _resumeSec = []; // 位置记忆：切回续播
    private BgmTrack? _current;

    public override void _Ready()
    {
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
        foreach (var lane in _lanes)
        {
            lane.Player = new AudioStreamPlayer { Bus = "Bgm", VolumeDb = -80f };
            AddChild(lane.Player);
        }
    }

    public override void _Process(double delta)
    {
        float d = (float)delta;
        foreach (var lane in _lanes)
        {
            if (Mathf.IsEqualApprox(lane.LinVolume, lane.TargetVolume))
            {
                continue;
            }
            float rate = 1f / (lane.TargetVolume > lane.LinVolume ? FadeInSec : FadeOutSec) * d;
            lane.LinVolume = lane.TargetVolume > lane.LinVolume
                ? Mathf.Min(lane.TargetVolume, lane.LinVolume + rate)
                : Mathf.Max(lane.TargetVolume, lane.LinVolume - rate);
            lane.Player.VolumeDb = LinearToDb(lane.LinVolume);
            if (lane.TargetVolume <= 0f && lane.LinVolume <= 0f && lane.Player.Playing)
            {
                SavePosition(lane);
                lane.Player.Stop();
            }
        }
    }

    public void Start(BgmTrack track)
    {
        if (!_streams.TryGetValue(track, out var stream))
        {
            return;
        }
        var active = _lanes.FirstOrDefault(l => l.Player.Playing && l.TargetVolume >= 1f);
        if (active != null && _current == track && active.Player.Stream == stream)
        {
            return; // 同轨在播守卫（源 playBGM 同名轨直接 return，B20 ②）
        }
        // 落点选择：还挂着目标曲的在播轨（优雅回归优先，避免同曲自重叠）> 空闲轨 > 音量最低轨（抢滩，先存其进度）
        var lane = _lanes.FirstOrDefault(l => l.Player.Stream == stream && l.Player.Playing)
                   ?? _lanes.FirstOrDefault(l => !l.Busy);
        var graceful = false;
        if (lane == null)
        {
            lane = _lanes.OrderBy(l => l.LinVolume).First();
            SavePosition(lane);
        }
        else if (lane.Player.Stream == stream && lane.Player.Playing)
        {
            graceful = true; // 优雅回归：目标曲仍在淡出，反转即可，进度不丢
        }
        if (!graceful)
        {
            lane.Track = track;
            lane.Player.Stream = stream;
            lane.LinVolume = 0f;
            lane.Player.VolumeDb = -80f;
            lane.Player.Play();
            if (_resumeSec.TryGetValue(track, out var resume) && resume > 0.05)
            {
                lane.Player.Seek((float)resume); // 位置记忆：切回续播
            }
        }
        foreach (var other in _lanes)
        {
            if (other != lane)
            {
                other.TargetVolume = 0f; // 旧轨淡出，淡完由 _Process 停播并记进度
            }
        }
        lane.TargetVolume = 1f;
        _current = track;
    }

    public void Stop()
    {
        foreach (var lane in _lanes)
        {
            SavePosition(lane);
            lane.Player.Stop();
            lane.Player.Stream = null;
            lane.LinVolume = 0f;
            lane.TargetVolume = 0f;
        }
        _current = null;
    }

    public void SetIntensity(float value)
    {
        // 采样版不驱动参数（源 intensity 默认 0 无效果；合成器回填时按 audio_var.md §五实现）
    }

    private void SavePosition(Lane lane)
    {
        if (lane.Player.Playing && lane.Player.Stream != null)
        {
            _resumeSec[lane.Track] = lane.Player.GetPlaybackPosition();
        }
    }

    private static float LinearToDb(float v) => v <= 0.0001f ? -80f : Mathf.LinearToDb(v);

    /// <summary>自测投影（m5-bgm-crossfade 回归锁）：轨位瞬时快照，不依赖真实音频输出。</summary>
    internal sealed record LaneSnapshot(string TrackName, float LinVolume, float TargetVolume, bool Playing);

    internal LaneSnapshot[] LanesForTests => _lanes
        .Select(l => new LaneSnapshot(l.Track.ToString(), l.LinVolume, l.TargetVolume, l.Player.Playing))
        .ToArray();

    internal BgmTrack? CurrentTrackForTests => _current;
}
