using Godot;
using Nmoku.Core;
using Nmoku.Core.Audio;

namespace Nmoku.Game.Audio;

/// <summary>音效种类（源 SoundName：click/win/lose/hover；正式版 Win/Lose 已接入真实管弦短乐句）。</summary>
public enum SfxKind
{
    Click,
    Win,
    Lose,
    Hover,
}

/// <summary>
/// 音频管理器（§3.5 / §10 B31）：
/// - SFX 池（Sfx 总线）：黑白子分色双声道复音池（65ms 同色防抖 + ±3% 自然音高微抖）+ 胜负互斥短乐句；
/// - BGM 引擎装配（Bgm 总线）：5 轨正式立体声 OGG 交叉淡化、会话续播、新局轮换 + 胜负短乐句期间平滑闪避；
/// - 主音量/静音（Master 总线）+ 偏好持久化（user://settings.cfg audio 节，经 SettingsStore）。
/// 默认 sfx=0.6 / bgm=0.3（B20 ③，与 programmed-music 试听台响度基线一致）。
/// </summary>
public sealed partial class AudioManager : Node
{
    public static AudioManager? Instance { get; private set; }

    private const float DefaultSfxVolume = 0.6f; // 源 AudioManager.ts:219
    private const float DefaultBgmVolume = 0.3f; // 源 AudioManager.ts:220
    private const int StoneVoiceCount = 2;       // 每色 2 声部复音，快速连续落子不硬截尾音
    private const float StonePitchMin = 0.97f;
    private const float StonePitchSpan = 0.06f;  // 0.97..1.03（对齐试听台 jitter）

    private IMusicEngine _music = null!;
    private readonly Dictionary<SfxKind, AudioStreamPlayer> _sfx = [];
    private readonly AudioStreamPlayer[] _blackStoneVoices = new AudioStreamPlayer[StoneVoiceCount];
    private readonly AudioStreamPlayer[] _whiteStoneVoices = new AudioStreamPlayer[StoneVoiceCount];
    private readonly SfxDebounceGate _stoneDebounce = new(AudioPolicy.StoneDebounceMs);
    private readonly BgmDuckEnvelope _bgmDuck = new();
    private readonly Random _rng = new();
    private int _blackVoiceIdx;
    private int _whiteVoiceIdx;
    private float _lastAppliedBgmDuck = 1f;

    public float SfxVolume { get; private set; } = DefaultSfxVolume;
    public float BgmVolume { get; private set; } = DefaultBgmVolume;
    public bool Muted { get; private set; }

    /// <summary>静音翻转后触发（M 键/设置页两路统一出口）：设置页按钮文本联动刷新。</summary>
    public event Action? MuteChanged;

    public override void _Ready()
    {
        Instance = this;
        _music = new SampleMusicEngine();
        AddChild((Node)_music);

        foreach (SfxKind kind in Enum.GetValues<SfxKind>())
        {
            var player = new AudioStreamPlayer { Bus = "Sfx" };
            player.Stream = GD.Load<AudioStream>($"res://assets/audio/sfx_{kind.ToString().ToLowerInvariant()}.wav");
            AddChild(player);
            _sfx[kind] = player;
        }

        var fallbackClick = _sfx[SfxKind.Click].Stream;
        var blackStream = LoadOptionalStream("res://assets/audio/sfx_stone_black.wav") ?? fallbackClick;
        var whiteStream = LoadOptionalStream("res://assets/audio/sfx_stone_white.wav") ?? fallbackClick;
        for (int i = 0; i < StoneVoiceCount; i++)
        {
            _blackStoneVoices[i] = new AudioStreamPlayer { Bus = "Sfx", Stream = blackStream };
            _whiteStoneVoices[i] = new AudioStreamPlayer { Bus = "Sfx", Stream = whiteStream };
            AddChild(_blackStoneVoices[i]);
            AddChild(_whiteStoneVoices[i]);
        }

        LoadSettings();
        ApplyVolumes();
    }

    public override void _Process(double delta)
    {
        bool holdDuck = !Muted && IsGameOverStingerPlaying();
        float duck = _bgmDuck.Step((float)delta, holdDuck);
        if (!Mathf.IsEqualApprox(duck, _lastAppliedBgmDuck))
        {
            _lastAppliedBgmDuck = duck;
            ApplyBgmBusVolume();
        }
    }

    public override void _ExitTree()
    {
        StopAllPlayback();
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationPredelete)
        {
            // 退出前停播：播放中的 AudioStreamPlayer 会让 OGG/WAV playback 对象残留到进程退出（泄露警告）
            StopAllPlayback();
        }
        base._Notification(what);
    }

    private void StopAllPlayback()
    {
        _music?.Stop();
        foreach (var player in _sfx.Values)
        {
            StopIfValid(player);
        }
        foreach (var player in _blackStoneVoices)
        {
            StopIfValid(player);
        }
        foreach (var player in _whiteStoneVoices)
        {
            StopIfValid(player);
        }
        _bgmDuck.Reset();
        _lastAppliedBgmDuck = 1f;
    }

    private static void StopIfValid(AudioStreamPlayer? player)
    {
        if (player != null && GodotObject.IsInstanceValid(player))
        {
            player.Stop();
        }
    }

    private static AudioStream? LoadOptionalStream(string path) =>
        ResourceLoader.Exists(path) ? GD.Load<AudioStream>(path) : null;

    public void PlayBgm(BgmTrack track) => PlayBgm(track.ToString().ToLowerInvariant());

    /// <summary>槽位直调（新场景零枚举接入；文件约定与选曲策略见 <see cref="MusicLibrary"/>）。</summary>
    public void PlayBgm(string slot) => _music.Start(slot);

    public void StopBgm() => _music.Stop();

    /// <summary>开新局/重开/切关卡时通知音乐引擎对 game 槽位执行亲和选曲与池内轮换。</summary>
    public void PrepareNewMatchBgm(bool preferAdvanced) =>
        _music.PrepareSlotSession(
            "game",
            rotate: true,
            preferredTrackId: preferAdvanced ? AudioPolicy.AdvancedGameTrackId : AudioPolicy.StandardGameTrackId);

    /// <summary>设置页修改音乐偏好后即时重估当前槽位。</summary>
    public void RefreshCurrentBgm() => _music.RefreshCurrentSlot();

    /// <summary>自测投影：headless 回归锁直接观测音乐引擎轨位与闪避状态。</summary>
    internal SampleMusicEngine? MusicForTests => _music as SampleMusicEngine;

    internal float BgmDuckForTests => _bgmDuck.Current;

    internal bool IsGameOverStingerPlayingForTests => IsGameOverStingerPlaying();

    /// <summary>动态强度（audio_var.md §五；透传引擎，当前采样版为空实现）。</summary>
    public void SetIntensity(float value) => _music.SetIntensity(value);

    /// <summary>
    /// 棋子落盘音效（§10 B31）：区分黑子（低沉玄石）与白子（清亮玉石）双敲采样；
    /// 按色独立 65ms 防抖（人类黑子与 AI 白子紧邻落子互不吞音）+ ±3% 自然音高微抖 + 复音不截尾。
    /// </summary>
    public void PlayStonePlace(PlayerColor color)
    {
        if (Muted)
        {
            return;
        }
        if (!_stoneDebounce.TryAcquire((int)color, Time.GetTicksMsec()))
        {
            return;
        }
        AudioStreamPlayer player;
        if (color == PlayerColor.Black)
        {
            player = _blackStoneVoices[_blackVoiceIdx];
            _blackVoiceIdx = (_blackVoiceIdx + 1) % StoneVoiceCount;
        }
        else
        {
            player = _whiteStoneVoices[_whiteVoiceIdx];
            _whiteVoiceIdx = (_whiteVoiceIdx + 1) % StoneVoiceCount;
        }
        player.PitchScale = StonePitchMin + (float)_rng.NextDouble() * StonePitchSpan;
        player.Play();
    }

    /// <summary>终局胜负短乐句（互斥播放 + 期间平滑闪避 BGM，平局 None 不播号角）。</summary>
    public void PlayGameOver(GameOverSfx outcome)
    {
        StopGameOverSfx();
        if (Muted || outcome == GameOverSfx.None)
        {
            return;
        }
        PlaySfx(outcome == GameOverSfx.Win ? SfxKind.Win : SfxKind.Lose);
    }

    /// <summary>离开结算模态（重开/回标题）时停掉残余 3s 胜负短乐句，让 BGM 平滑回升。</summary>
    public void StopGameOverSfx()
    {
        if (_sfx.TryGetValue(SfxKind.Win, out var win))
        {
            StopIfValid(win);
        }
        if (_sfx.TryGetValue(SfxKind.Lose, out var lose))
        {
            StopIfValid(lose);
        }
    }

    /// <summary>SWAP2 决策轻触反馈：换色微亮（1.08x）、保持执白柔和（0.95x）。</summary>
    public void PlaySwapResolved(bool swapped)
    {
        if (Muted || !_sfx.TryGetValue(SfxKind.Click, out var player))
        {
            return;
        }
        player.PitchScale = swapped ? 1.08f : 0.95f;
        player.Play();
    }

    /// <summary>播放音效：静音时忽略；重入=从头重播（源 audio.currentTime=0 语义）。</summary>
    public void PlaySfx(SfxKind kind)
    {
        if (Muted || !_sfx.TryGetValue(kind, out var player))
        {
            return;
        }
        if (kind == SfxKind.Win && _sfx.TryGetValue(SfxKind.Lose, out var lose))
        {
            StopIfValid(lose);
        }
        else if (kind == SfxKind.Lose && _sfx.TryGetValue(SfxKind.Win, out var win))
        {
            StopIfValid(win);
        }
        player.Play();
    }

    /// <summary>设置音量（0–1 钳制），即时生效并持久化。</summary>
    public void SetVolumes(float sfx, float bgm)
    {
        SfxVolume = Mathf.Clamp(sfx, 0f, 1f);
        BgmVolume = Mathf.Clamp(bgm, 0f, 1f);
        ApplyVolumes();
        SaveSettings();
    }

    /// <summary>静音翻转（源 mute()：Master 总线全局静音，M 键任意状态可用）。</summary>
    public void ToggleMute()
    {
        Muted = !Muted;
        ApplyVolumes();
        SaveSettings();
        MuteChanged?.Invoke();
    }

    private bool IsGameOverStingerPlaying()
    {
        bool win = _sfx.TryGetValue(SfxKind.Win, out var w) && GodotObject.IsInstanceValid(w) && w.Playing;
        bool lose = _sfx.TryGetValue(SfxKind.Lose, out var l) && GodotObject.IsInstanceValid(l) && l.Playing;
        return win || lose;
    }

    private void ApplyVolumes()
    {
        AudioServer.SetBusMute(AudioServer.GetBusIndex("Master"), Muted);
        AudioServer.SetBusVolumeDb(AudioServer.GetBusIndex("Sfx"), LinearToDb(SfxVolume));
        ApplyBgmBusVolume();
    }

    private void ApplyBgmBusVolume()
    {
        AudioServer.SetBusVolumeDb(AudioServer.GetBusIndex("Bgm"), LinearToDb(BgmVolume * _bgmDuck.Current));
    }

    private static float LinearToDb(float v) => v <= 0.0001f ? -80f : Mathf.LinearToDb(v);

    private void LoadSettings()
    {
        SfxVolume = Mathf.Clamp((float)SettingsStore.Get("audio", "volume_sfx", SfxVolume).AsSingle(), 0f, 1f);
        BgmVolume = Mathf.Clamp((float)SettingsStore.Get("audio", "volume_bgm", BgmVolume).AsSingle(), 0f, 1f);
        Muted = SettingsStore.Get("audio", "muted", Muted).AsBool();
    }

    private void SaveSettings()
    {
        SettingsStore.SetMany("audio",
            ("volume_sfx", SfxVolume),
            ("volume_bgm", BgmVolume),
            ("muted", Muted));
    }
}
