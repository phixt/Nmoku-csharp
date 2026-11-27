using Godot;
using Nmoku.Core.Audio;

namespace Nmoku.Game.Audio;

/// <summary>
/// 采样版音乐引擎（§3.5 默认装配）：曲源 <see cref="MusicLibrary"/>（槽位发现式加载），
/// Bgm 总线。平滑切换（2026-10-02，B21）：双播放器交叉淡化——旧轨 0.5s 淡出、新轨 0.6s 淡入
/// 并行重叠（源 playBGM/startBGMPattern 的 WebAudio linearRamp 语义，B20 ②）；位置记忆（切走的
/// 轨记住播放进度，切回续播不重头）；优雅回归（切回还在淡出的轨=淡出反转为淡入）。快速连切由
/// 「瞬时音量重定目标」仲裁，同帧多请求=最后者胜，不产生重启爆音。
/// 多曲会话机制（2026-11-27，B31）：同一会话内（暂停/引导/标题保留局往返）固定当前曲目保证位置记忆与
/// 优雅回归；新局经 <see cref="PrepareSlotSession"/> 触发亲和选曲与池内轮换并从头起播。
/// 正式 BGM 由 programmed-music REAPER 真音源管线交付（5 轨无缝循环立体声 OGG）。
/// </summary>
public sealed partial class SampleMusicEngine : Node, IMusicEngine
{
    private sealed class Lane
    {
        public AudioStreamPlayer Player = null!;
        public string Slot = "";      // 该轨位当前挂的槽位（场景键）
        public string? TrackId;       // 该轨位当前挂的曲目 Id（bgm_<slot>[_<n>]）；null=空
        public float LinVolume;       // 瞬时线性音量（淡化在玩家级，音量/静音/闪避在总线级，正交）
        public float TargetVolume;
        /// <summary>挂着曲子（在播或淡出中）即占用；空闲才可指派。</summary>
        public bool Busy => Player.Playing || TargetVolume > 0f;
    }

    private const float FadeOutSec = 0.5f; // 源 playBGM：旧 bgmGain linearRamp→0（AudioManager.ts:795）
    private const float FadeInSec = 0.6f;  // 源 startBGMPattern：新 bgmGain 0→target（AudioManager.ts:722）

    private readonly MusicLibrary _library = new();
    private readonly Lane[] _lanes = [new Lane(), new Lane()];
    private readonly Dictionary<string, double> _resumeSec = [];        // 位置记忆：切回续播
    private readonly Dictionary<string, string> _sessionIdBySlot = [];  // 会话当前曲：暂停/引导往返不乱切
    private readonly Dictionary<string, string> _lastIdBySlot = [];     // 池选取去重/顺序游标
    private readonly Dictionary<string, string?> _pendingRotate = [];   // 下次进入该槽位触发新局轮换（值=亲和曲 Id）
    private readonly HashSet<string> _warnedEmptySlots = [];
    private readonly Random _rng = new();
    private string? _currentSlot;

    public override void _Ready()
    {
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
            if (!Mathf.IsEqualApprox(lane.LinVolume, lane.TargetVolume))
            {
                float rate = 1f / (lane.TargetVolume > lane.LinVolume ? FadeInSec : FadeOutSec) * d;
                lane.LinVolume = lane.TargetVolume > lane.LinVolume
                    ? Mathf.Min(lane.TargetVolume, lane.LinVolume + rate)
                    : Mathf.Max(lane.TargetVolume, lane.LinVolume - rate);
                // 归零量化：淡出步进若落入 0~ε 残差区间（如 Lin=1e-6），下一帧的
                // IsEqualApprox(Lin, Target) 判等早退会跳过步进，而停播判据要求 Lin<=0 严格成立
                // → 轨位永挂 -80dB 静默循环。必须在产出残差的同一帧钳到精确 0。
                if (lane.TargetVolume <= 0f && lane.LinVolume < 0.001f)
                {
                    lane.LinVolume = 0f;
                }
                lane.Player.VolumeDb = LinearToDb(lane.LinVolume);
            }
            // 停播检查不得放在音量相等早退之后：同帧「指派又转淡出」的轨位（Lin=Target=0 但在播）
            // 会被早退永久跳过，以 -80dB 静默循环挂住（全量自测抓到）
            if (lane.TargetVolume <= 0f && lane.LinVolume <= 0f && lane.Player.Playing)
            {
                SavePosition(lane);
                lane.Player.Stop();
                lane.Player.Stream = null;
                lane.TrackId = null;
                lane.Slot = "";
            }
        }
    }

    public void PrepareSlotSession(string slot, bool rotate = true, string? preferredTrackId = null)
    {
        if (!rotate)
        {
            _pendingRotate.Remove(slot);
            return;
        }
        _pendingRotate[slot] = preferredTrackId;
        // 若此刻正处于该槽位（例如 GameOver 直接点击「重新开始」仍在 game 轨），立即平滑轮换
        if (_currentSlot == slot)
        {
            Start(slot);
        }
    }

    public void RefreshCurrentSlot()
    {
        if (_currentSlot is { } slot)
        {
            Start(slot);
        }
    }

    public void PlayTrackDirect(string slot, string trackId)
    {
        var pool = _library.Pool(slot);
        var pick = pool.FirstOrDefault(t => string.Equals(t.Id, trackId, StringComparison.Ordinal));
        if (pick == null)
        {
            return;
        }
        var active = _lanes.FirstOrDefault(l => l.Player.Playing && l.TargetVolume >= 1f);
        if (active != null && active.TrackId == pick.Id)
        {
            _currentSlot = slot;
            _sessionIdBySlot[slot] = pick.Id;
            _lastIdBySlot[slot] = pick.Id;
            return;
        }
        _pendingRotate.Remove(slot);
        _resumeSec.Remove(pick.Id); // 试听浏览从头起播
        StartPickedTrack(slot, pick);
    }

    public void SeekActive(double seconds)
    {
        var active = _lanes.FirstOrDefault(l => l.Player.Playing && l.TargetVolume >= 1f && l.Player.Stream != null);
        if (active == null)
        {
            return;
        }
        double len = active.Player.Stream!.GetLength();
        if (len <= 0.1)
        {
            return;
        }
        double clamped = Math.Clamp(seconds, 0.0, Math.Max(0.0, len - 0.15));
        active.Player.Seek((float)clamped);
    }

    public BgmPlaybackStatus GetPlaybackStatus()
    {
        var active = _lanes.FirstOrDefault(l => l.Player.Playing && l.TargetVolume >= 1f && l.Player.Stream != null)
                     ?? _lanes.FirstOrDefault(l => l.Player.Playing && l.Player.Stream != null);
        if (active == null || active.Player.Stream == null)
        {
            return new BgmPlaybackStatus(_currentSlot, null, 0, 0, false);
        }
        return new BgmPlaybackStatus(
            active.Slot,
            active.TrackId,
            active.Player.GetPlaybackPosition(),
            active.Player.Stream.GetLength(),
            active.Player.Playing);
    }

    public void Start(string slot)
    {
        var pool = _library.Pool(slot);
        if (pool.Count == 0)
        {
            if (_warnedEmptySlots.Add(slot))
            {
                GD.PushWarning($"[BGM] 槽位 {slot} 无音频文件（bgm_{slot}*.ogg），保持当前播放");
            }
            return; // 缺文件容忍：新槽位丢文件前不切、不炸
        }
        string ovr = MusicPrefs.SlotOverride(slot);
        var active = _lanes.FirstOrDefault(l => l.Player.Playing && l.TargetVolume >= 1f);
        if (ovr == "off")
        {
            _pendingRotate.Remove(slot);
            _sessionIdBySlot.Remove(slot);
            if (_currentSlot == slot || active?.Slot == slot)
            {
                foreach (var l in _lanes)
                {
                    if (l.Slot == slot)
                    {
                        l.TargetVolume = 0f; // 该槽位静默：交叉淡出收尾
                    }
                }
                if (_currentSlot == slot)
                {
                    _currentSlot = null;
                }
            }
            return;
        }

        bool forceRotate = _pendingRotate.Remove(slot, out string? preferredId);
        bool pinnedSwitch = active != null
                            && _currentSlot == slot
                            && ovr != "auto"
                            && active.TrackId != ovr
                            && pool.Any(t => t.Id == ovr);

        if (_currentSlot == slot && active != null && !forceRotate && !pinnedSwitch)
        {
            return; // 同槽在播守卫（源 playBGM 同轨守卫，B20 ②：复盘回局/暂停往返不重播不换曲）
        }
        if (MusicPrefs.GlobalMode() == "lock" && active != null && !pinnedSwitch)
        {
            return; // 全局锁定（设置页可配）：切场景不打断当前曲
        }

        var pick = Resolve(slot, pool, ovr, forceRotate, preferredId);
        if (pick == null)
        {
            return;
        }
        // 新局轮换命中同一首且已在播（如用户定曲或单曲池）→ 保持平稳在播，不强切
        if (_currentSlot == slot && active != null && active.TrackId == pick.Id)
        {
            _sessionIdBySlot[slot] = pick.Id;
            _lastIdBySlot[slot] = pick.Id;
            return;
        }
        if (forceRotate)
        {
            // 新局切到的新曲目从 0:00 起播，不沿用上一局残留进度
            _resumeSec.Remove(pick.Id);
        }

        StartPickedTrack(slot, pick);
    }

    private void StartPickedTrack(string slot, MusicLibrary.Track pick)
    {
        // 落点选择：还挂着目标曲的在播轨（优雅回归优先，避免同曲自重叠）> 空闲轨 > 音量最低轨（抢滩，先存其进度）
        var lane = _lanes.FirstOrDefault(l => l.TrackId == pick.Id && l.Player.Playing)
                   ?? _lanes.FirstOrDefault(l => !l.Busy);
        var graceful = false;
        if (lane == null)
        {
            lane = _lanes.OrderBy(l => l.LinVolume).First();
            SavePosition(lane);
        }
        else if (lane.TrackId == pick.Id && lane.Player.Playing)
        {
            graceful = true; // 优雅回归：目标曲仍在淡出，反转即可，进度不丢
        }
        if (!graceful)
        {
            lane.Slot = slot;
            lane.TrackId = pick.Id;
            lane.Player.Stream = pick.Stream;
            lane.LinVolume = 0f;
            lane.Player.VolumeDb = -80f;
            lane.Player.Play();
            if (_resumeSec.TryGetValue(pick.Id, out var resume) && resume > 0.05)
            {
                lane.Player.Seek((float)resume); // 位置记忆：切回续播
            }
        }
        else
        {
            lane.Slot = slot;
        }
        foreach (var other in _lanes)
        {
            if (other != lane)
            {
                other.TargetVolume = 0f; // 旧轨淡出，淡完由 _Process 停播并记进度
            }
        }
        lane.TargetVolume = 1f;
        _sessionIdBySlot[slot] = pick.Id;
        _lastIdBySlot[slot] = pick.Id;
        _currentSlot = slot;
    }

    public void Stop()
    {
        foreach (var lane in _lanes)
        {
            SavePosition(lane);
            lane.Player.Stop();
            lane.Player.Stream = null;
            lane.TrackId = null;
            lane.Slot = "";
            lane.LinVolume = 0f;
            lane.TargetVolume = 0f;
        }
        _pendingRotate.Clear();
        _currentSlot = null;
    }

    public void SetIntensity(float value)
    {
        // 采样版不驱动参数（源 intensity 默认 0 无效果；合成器回填时按 audio_var.md §五实现）
    }

    /// <summary>槽位选曲：委托 Core 纯函数 <see cref="AudioPolicy.SelectNextTrackId"/>。</summary>
    private MusicLibrary.Track? Resolve(
        string slot,
        IReadOnlyList<MusicLibrary.Track> pool,
        string ovr,
        bool rotate,
        string? preferredId)
    {
        string[] poolIds = new string[pool.Count];
        for (int i = 0; i < pool.Count; i++)
        {
            poolIds[i] = pool[i].Id;
        }
        string? sessionId = _sessionIdBySlot.GetValueOrDefault(slot);
        string? lastId = _lastIdBySlot.GetValueOrDefault(slot);
        string? chosenId = AudioPolicy.SelectNextTrackId(
            poolIds,
            sessionId,
            lastId,
            ovr,
            MusicPrefs.PlayMode(),
            rotate,
            preferredId,
            n => _rng.Next(n));
        return chosenId == null ? null : pool.FirstOrDefault(t => t.Id == chosenId);
    }

    private void SavePosition(Lane lane)
    {
        if (lane.Player.Playing && lane.Player.Stream != null && lane.TrackId != null)
        {
            _resumeSec[lane.TrackId] = lane.Player.GetPlaybackPosition();
        }
    }

    private static float LinearToDb(float v) => v <= 0.0001f ? -80f : Mathf.LinearToDb(v);

    /// <summary>自测投影（m5-bgm-crossfade 回归锁）：轨位瞬时快照，不依赖真实音频输出。</summary>
    internal sealed record LaneSnapshot(string Slot, string? TrackId, float LinVolume, float TargetVolume, bool Playing);

    internal LaneSnapshot[] LanesForTests => _lanes
        .Select(l => new LaneSnapshot(l.Slot, l.TrackId, l.LinVolume, l.TargetVolume, l.Player.Playing))
        .ToArray();

    internal string? CurrentSlotForTests => _currentSlot;

    internal MusicLibrary LibraryForTests => _library;
}
