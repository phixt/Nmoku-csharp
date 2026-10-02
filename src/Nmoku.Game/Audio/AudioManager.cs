using Godot;

namespace Nmoku.Game.Audio;

/// <summary>音效种类（源 SoundName：click/win/lose/hover；B20 ①：全源仅 click/win 有触发点）。</summary>
public enum SfxKind
{
    Click,
    Win,
    Lose,
    Hover,
}

/// <summary>
/// 音频管理器（§3.5）：SFX 池（Sfx 总线）+ BGM 引擎装配 + 主音量/静音 + 偏好持久化。
/// 语义对齐源 AudioManager（B20 ③）：默认 sfx=0.6/bgm=0.3、0–1 钳制、mute=Master 全局翻转。
/// 偏好存 user://settings.cfg（audio 节，经 SettingsStore）。
/// </summary>
public sealed partial class AudioManager : Node
{
    public static AudioManager? Instance { get; private set; }

    private const float DefaultSfxVolume = 0.6f; // 源 AudioManager.ts:219
    private const float DefaultBgmVolume = 0.3f; // 源 AudioManager.ts:220

    private IMusicEngine _music = null!;
    private readonly Dictionary<SfxKind, AudioStreamPlayer> _sfx = [];

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
        LoadSettings();
        ApplyVolumes();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationPredelete)
        {
            // 退出前停播：播放中的 AudioStreamPlayer 会让 OGG playback 对象残留到进程退出（泄露警告）
            _music.Stop();
        }
        base._Notification(what);
    }

    public void PlayBgm(BgmTrack track) => PlayBgm(track.ToString().ToLowerInvariant());

    /// <summary>槽位直调（新场景零枚举接入；文件约定与选曲策略见 <see cref="MusicLibrary"/>）。</summary>
    public void PlayBgm(string slot) => _music.Start(slot);

    public void StopBgm() => _music.Stop();

    /// <summary>自测投影：headless 回归锁直接观测音乐引擎轨位状态。</summary>
    internal SampleMusicEngine? MusicForTests => _music as SampleMusicEngine;

    /// <summary>动态强度（audio_var.md §五；透传引擎，当前采样版为空实现）。</summary>
    public void SetIntensity(float value) => _music.SetIntensity(value);

    /// <summary>播放音效：静音时忽略；重入=从头重播（源 audio.currentTime=0 语义）。</summary>
    public void PlaySfx(SfxKind kind)
    {
        if (Muted || !_sfx.TryGetValue(kind, out var player))
        {
            return;
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

    private void ApplyVolumes()
    {
        AudioServer.SetBusMute(AudioServer.GetBusIndex("Master"), Muted);
        AudioServer.SetBusVolumeDb(AudioServer.GetBusIndex("Sfx"), LinearToDb(SfxVolume));
        AudioServer.SetBusVolumeDb(AudioServer.GetBusIndex("Bgm"), LinearToDb(BgmVolume));
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
        SettingsStore.Set("audio", "volume_sfx", SfxVolume);
        SettingsStore.Set("audio", "volume_bgm", BgmVolume);
        SettingsStore.Set("audio", "muted", Muted);
    }
}
