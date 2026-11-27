using Godot;
using Nmoku.Core;
using Nmoku.Core.Audio;
using Nmoku.Game.Audio;
using Nmoku.Game.Theme;

namespace Nmoku.Game.Shell;

/// <summary>
/// 音乐与音效中心（L2 浮层，路由 id = "audio-hub"；标题/暂停/设置页均可直达）：
/// <para>
/// · 合并原设置页的全部音频项：音效/音乐独立开关、音量滑条、全局静音联动、对局音乐与切屏策略；
/// · BGM 浏览与试听：展示 5 首正式交付曲目，支持点击交叉淡化切曲、实时播放进度与拖动跳转、一键恢复当前场景曲；
/// · 音效试听：交替试听黑/白子玄石玉石双敲，以及真音源胜利/失败短乐句（现场体验 BGM 闪避）。
/// </para>
/// </summary>
public sealed partial class AudioHubScreen : Control
{
    public event Action? CloseRequested;
    public event Action? RestoreSceneBgmRequested;

    private Button _masterMuteBtn = null!;
    private Button _sfxMuteBtn = null!;
    private Button _bgmMuteBtn = null!;
    private Label _nowPlayingLabel = null!;
    private Label _timeLabel = null!;
    private HSlider _seekSlider = null!;
    private AudioManager? _subscribedAudio;
    private readonly List<TrackCardEntry> _cards = [];
    private bool _seeking;
    private bool _updatingSeekSlider;
    private bool _nextTestStoneBlack = true;
    private string? _lastHighlightedTrackId = "__init__";

    private sealed record TrackCardEntry(
        BgmTrackMeta Meta,
        Button Button,
        StyleBoxFlat NormalBox,
        StyleBoxFlat ActiveBox,
        Label Badge);

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;

        var margin = new MarginContainer();
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 28);
        margin.AddThemeConstantOverride("margin_right", 28);
        margin.AddThemeConstantOverride("margin_top", 20);
        margin.AddThemeConstantOverride("margin_bottom", 20);
        AddChild(margin);

        var panel = new PanelContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        panel.AddThemeStyleboxOverride("panel", MakePanelBox(Palette.Active.PanelBg, Palette.Active.PanelBorder, 10, 18, 14));
        margin.AddChild(panel);

        var root = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        root.AddThemeConstantOverride("separation", 10);
        panel.AddChild(root);

        // ---- 顶栏 ----
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 8);
        root.AddChild(header);

        var title = new Label
        {
            Text = I18n.T(Text.Audio.HubTitle),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        title.AddThemeFontSizeOverride("font_size", 22);
        title.AddThemeColorOverride("font_color", Palette.Active.TextPrimary);
        header.AddChild(title);

        _masterMuteBtn = MakeButton(MasterMuteText(), () => AudioManager.Instance?.ToggleMute());
        _masterMuteBtn.Name = "MasterMuteButton";
        header.AddChild(_masterMuteBtn);

        var closeBtn = MakeButton(I18n.T(Text.Overlay.Cancel) + " (Esc)", () => CloseRequested?.Invoke());
        header.AddChild(closeBtn);

        // ---- 主内容双栏 ----
        var columns = new HBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        columns.AddThemeConstantOverride("separation", 14);
        root.AddChild(columns);

        columns.AddChild(BuildLeftControlsColumn());
        columns.AddChild(BuildRightLibraryColumn());

        _subscribedAudio = AudioManager.Instance;
        if (_subscribedAudio != null)
        {
            _subscribedAudio.MuteChanged += RefreshMuteStates;
        }
        RefreshMuteStates();
        RefreshPlaybackStatus();
    }

    public override void _ExitTree()
    {
        if (_subscribedAudio != null)
        {
            _subscribedAudio.MuteChanged -= RefreshMuteStates;
            _subscribedAudio = null;
        }
    }

    public override void _Process(double delta)
    {
        RefreshPlaybackStatus();
    }

    // ---- 左栏：独立开关 + 音量滑条 + 策略偏好 + 音效试听 ----

    private Control BuildLeftControlsColumn()
    {
        var scroll = new ScrollContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = 0.45f,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        var box = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        box.AddThemeConstantOverride("separation", 9);
        scroll.AddChild(box);

        box.AddChild(MakeSectionLabel(I18n.T(Text.Audio.SectionControls)));

        var audio = AudioManager.Instance;
        float sfx = audio?.SfxVolume ?? 0.6f;
        float bgm = audio?.BgmVolume ?? 0.3f;

        // 独立开关行（音效 / 音乐各自独立）
        var switchRow = new HBoxContainer();
        switchRow.AddThemeConstantOverride("separation", 8);
        _sfxMuteBtn = MakeButton(SfxMuteText(), () => AudioManager.Instance?.ToggleSfxMute());
        _sfxMuteBtn.Name = "SfxMuteButton";
        _sfxMuteBtn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _bgmMuteBtn = MakeButton(BgmMuteText(), () => AudioManager.Instance?.ToggleBgmMute());
        _bgmMuteBtn.Name = "BgmMuteButton";
        _bgmMuteBtn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        switchRow.AddChild(_sfxMuteBtn);
        switchRow.AddChild(_bgmMuteBtn);
        box.AddChild(switchRow);

        // 音量滑条（从原 SettingsScreen 合并迁入）
        box.AddChild(MakeVolumeRow("SfxVolumeSlider", I18n.T(Text.Settings.SfxVolume), sfx, v =>
            audio?.SetVolumes(v, audio.BgmVolume)));
        box.AddChild(MakeVolumeRow("BgmVolumeSlider", I18n.T(Text.Settings.BgmVolume), bgm, v =>
            audio?.SetVolumes(audio.SfxVolume, v)));

        box.AddChild(MakeGameMusicRow());
        box.AddChild(MakeBgmScopeRow());

        // 音效试听快捷区
        var testRow = new HBoxContainer();
        testRow.AddThemeConstantOverride("separation", 6);
        var stoneBtn = MakeButton(I18n.T(Text.Audio.TestStone), () =>
        {
            PlayerColor color = _nextTestStoneBlack ? PlayerColor.Black : PlayerColor.White;
            _nextTestStoneBlack = !_nextTestStoneBlack;
            AudioManager.Instance?.PlayStonePlace(color);
        });
        stoneBtn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var winBtn = MakeButton(I18n.T(Text.Audio.TestWin), () =>
            AudioManager.Instance?.PlayGameOver(GameOverSfx.Win));
        winBtn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var loseBtn = MakeButton(I18n.T(Text.Audio.TestLose), () =>
            AudioManager.Instance?.PlayGameOver(GameOverSfx.Lose));
        loseBtn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        testRow.AddChild(stoneBtn);
        testRow.AddChild(winBtn);
        testRow.AddChild(loseBtn);
        box.AddChild(testRow);

        return scroll;
    }

    // ---- 右栏：正在播放条 + 5 轨 BGM 浏览器 ----

    private Control BuildRightLibraryColumn()
    {
        var col = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = 0.55f,
        };
        col.AddThemeConstantOverride("separation", 8);

        var libHead = new HBoxContainer();
        libHead.AddThemeConstantOverride("separation", 8);
        var sec = MakeSectionLabel(I18n.T(Text.Audio.SectionLibrary));
        sec.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        libHead.AddChild(sec);
        var restoreBtn = MakeButton(I18n.T(Text.Audio.RestoreSceneBgm), () => RestoreSceneBgmRequested?.Invoke());
        restoreBtn.Name = "RestoreSceneBgmButton";
        restoreBtn.AddThemeFontSizeOverride("font_size", 12);
        libHead.AddChild(restoreBtn);
        col.AddChild(libHead);

        // 正在播放状态与进度条
        var nowPanel = new PanelContainer();
        nowPanel.AddThemeStyleboxOverride("panel",
            MakePanelBox(Palette.Active.Background.Lerp(Palette.Active.PanelBg, 0.55f), Palette.Active.PanelBorder, 8, 12, 8));
        var nowBox = new VBoxContainer();
        nowBox.AddThemeConstantOverride("separation", 5);
        nowPanel.AddChild(nowBox);

        var nowTop = new HBoxContainer();
        _nowPlayingLabel = new Label
        {
            Text = I18n.T(Text.Audio.NowStopped),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            ClipText = true,
        };
        _nowPlayingLabel.AddThemeFontSizeOverride("font_size", 13);
        _nowPlayingLabel.AddThemeColorOverride("font_color", Palette.Active.TextPrimary);
        _timeLabel = new Label { Text = "0:00 / 0:00" };
        _timeLabel.AddThemeFontSizeOverride("font_size", 12);
        _timeLabel.AddThemeColorOverride("font_color", Palette.Active.TextDim);
        nowTop.AddChild(_nowPlayingLabel);
        nowTop.AddChild(_timeLabel);
        nowBox.AddChild(nowTop);

        _seekSlider = new HSlider
        {
            Name = "BgmSeekSlider",
            MinValue = 0,
            MaxValue = 1,
            Step = 0.002,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _seekSlider.DragStarted += () => _seeking = true;
        _seekSlider.DragEnded += changed =>
        {
            _seeking = false;
            if (changed)
            {
                SeekToRatio((float)_seekSlider.Value);
            }
        };
        _seekSlider.ValueChanged += v =>
        {
            if (!_updatingSeekSlider && !_seeking)
            {
                SeekToRatio((float)v);
            }
        };
        nowBox.AddChild(_seekSlider);
        col.AddChild(nowPanel);

        // 5 轨曲目列表（ScrollContainer 防小屏/高倍率溢出）
        var scroll = new ScrollContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        var list = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        list.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(list);

        foreach (var meta in AudioPolicy.BgmCatalog)
        {
            list.AddChild(BuildTrackCard(meta));
        }
        col.AddChild(scroll);
        return col;
    }

    private Control BuildTrackCard(BgmTrackMeta meta)
    {
        var pal = Palette.Active;
        var normalBox = MakePanelBox(pal.Background.Lerp(pal.PanelBg, 0.45f), pal.PanelBorder, 8, 12, 8);
        var activeBox = MakePanelBox(pal.Accent.Lerp(pal.PanelBg, 0.80f), pal.Accent, 8, 12, 8);
        activeBox.BorderWidthLeft = 3;
        var hoverBox = MakePanelBox(pal.Accent.Lerp(pal.PanelBg, 0.88f), pal.Accent, 8, 12, 8);

        var btn = new Button
        {
            Name = "TrackCard_" + meta.TrackId,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 50),
            FocusMode = FocusModeEnum.None,
        };
        btn.AddThemeStyleboxOverride("normal", normalBox);
        btn.AddThemeStyleboxOverride("hover", hoverBox);
        btn.AddThemeStyleboxOverride("pressed", activeBox);
        btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());

        var content = new HBoxContainer
        {
            MouseFilter = MouseFilterEnum.Ignore,
        };
        content.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        content.OffsetLeft = 12;
        content.OffsetRight = -12;
        content.OffsetTop = 6;
        content.OffsetBottom = -6;
        content.AddThemeConstantOverride("separation", 8);
        btn.AddChild(content);

        var texts = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        texts.AddThemeConstantOverride("separation", 2);
        var titleLbl = new Label
        {
            Text = I18n.T(meta.TitleKey),
            ClipText = true,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        titleLbl.AddThemeFontSizeOverride("font_size", 14);
        titleLbl.AddThemeColorOverride("font_color", pal.TextPrimary);
        var descLbl = new Label
        {
            Text = I18n.T(meta.DescKey),
            ClipText = true,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        descLbl.AddThemeFontSizeOverride("font_size", 11);
        descLbl.AddThemeColorOverride("font_color", pal.TextDim);
        texts.AddChild(titleLbl);
        texts.AddChild(descLbl);
        content.AddChild(texts);

        var badge = new Label
        {
            Text = I18n.T(Text.Audio.PlayingBadge),
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            Visible = false,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        badge.AddThemeFontSizeOverride("font_size", 11);
        badge.AddThemeColorOverride("font_color", pal.Accent);
        content.AddChild(badge);

        btn.Pressed += () =>
        {
            AudioManager.Instance?.PreviewBgmTrack(meta.Slot, meta.TrackId);
            RefreshPlaybackStatus();
        };

        _cards.Add(new TrackCardEntry(meta, btn, normalBox, activeBox, badge));
        return btn;
    }

    private void SeekToRatio(float ratio)
    {
        var audio = AudioManager.Instance;
        if (audio == null)
        {
            return;
        }
        var status = audio.GetBgmStatus();
        if (status.IsPlaying && status.LengthSec > 0.1)
        {
            audio.SeekBgm(Mathf.Clamp(ratio, 0f, 1f) * status.LengthSec);
        }
    }

    private void RefreshPlaybackStatus()
    {
        var audio = AudioManager.Instance;
        if (audio == null)
        {
            return;
        }
        var status = audio.GetBgmStatus();
        if (status.IsPlaying && status.TrackId != null)
        {
            var meta = AudioPolicy.FindTrackMeta(status.TrackId);
            string displayName = meta != null ? I18n.T(meta.TitleKey) : status.TrackId;
            _nowPlayingLabel.Text = I18n.T(Text.Audio.NowPlaying, displayName);
            _timeLabel.Text = $"{FormatTime(status.PositionSec)} / {FormatTime(status.LengthSec)}";
            if (!_seeking && status.LengthSec > 0.1)
            {
                _updatingSeekSlider = true;
                _seekSlider.Value = Math.Clamp(status.PositionSec / status.LengthSec, 0.0, 1.0);
                _updatingSeekSlider = false;
            }
        }
        else
        {
            _nowPlayingLabel.Text = I18n.T(Text.Audio.NowStopped);
            _timeLabel.Text = "0:00 / 0:00";
            if (!_seeking)
            {
                _updatingSeekSlider = true;
                _seekSlider.Value = 0;
                _updatingSeekSlider = false;
            }
        }

        string? activeId = status.IsPlaying ? status.TrackId : null;
        if (activeId != _lastHighlightedTrackId)
        {
            _lastHighlightedTrackId = activeId;
            foreach (var card in _cards)
            {
                bool active = activeId != null && card.Meta.TrackId == activeId;
                card.Button.AddThemeStyleboxOverride("normal", active ? card.ActiveBox : card.NormalBox);
                card.Badge.Visible = active;
            }
        }
    }

    private static string FormatTime(double sec)
    {
        if (double.IsNaN(sec) || double.IsInfinity(sec) || sec < 0)
        {
            return "0:00";
        }
        int total = (int)Math.Floor(sec);
        return $"{total / 60}:{total % 60:00}";
    }

    // ---- 复用行构建（音量 / 下拉）----

    private Control MakeVolumeRow(string sliderName, string label, float value, Action<float> onChanged)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        row.AddChild(MakeRowLabel(label));

        var slider = new HSlider
        {
            Name = sliderName,
            MinValue = 0,
            MaxValue = 1,
            Step = 0.05,
            Value = value,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        var percent = new Label
        {
            Text = $"{(int)Mathf.Round(value * 100)}%",
            CustomMinimumSize = new Vector2(44, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        percent.AddThemeColorOverride("font_color", Palette.Active.TextDim);
        slider.ValueChanged += v =>
        {
            onChanged((float)v);
            percent.Text = $"{(int)Mathf.Round(v * 100)}%";
        };
        row.AddChild(slider);
        row.AddChild(percent);
        return row;
    }

    private Control MakeGameMusicRow()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        row.AddChild(MakeRowLabel(I18n.T(Text.Settings.GameMusic)));

        var select = new OptionButton
        {
            Name = "GameMusicSelect",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        string current = MusicPrefs.SlotOverride("game");
        var options = new[]
        {
            ("auto", I18n.T(Text.Settings.GameMusicAuto)),
            (AudioPolicy.StandardGameTrackId, I18n.T(Text.Settings.GameMusicStandard)),
            (AudioPolicy.AdvancedGameTrackId, I18n.T(Text.Settings.GameMusicAdvanced)),
            ("off", I18n.T(Text.Settings.GameMusicOff)),
        };
        bool matched = false;
        foreach (var (key, text) in options)
        {
            int idx = select.ItemCount;
            select.AddItem(text, idx);
            select.SetItemMetadata(idx, key);
            if (key == current)
            {
                select.Select(idx);
                matched = true;
            }
        }
        if (!matched)
        {
            select.Select(0);
        }
        select.ItemSelected += idx =>
        {
            string key = select.GetItemMetadata((int)idx).AsString();
            SettingsStore.Set("music", "slot_game", key);
            AudioManager.Instance?.RefreshCurrentBgm();
        };
        row.AddChild(select);
        return row;
    }

    private Control MakeBgmScopeRow()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        row.AddChild(MakeRowLabel(I18n.T(Text.Settings.BgmScope)));

        var select = new OptionButton
        {
            Name = "BgmScopeSelect",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        string current = MusicPrefs.GlobalMode();
        var options = new[]
        {
            ("scene", I18n.T(Text.Settings.BgmScopeScene)),
            ("lock", I18n.T(Text.Settings.BgmScopeLock)),
        };
        bool matched = false;
        foreach (var (key, text) in options)
        {
            int idx = select.ItemCount;
            select.AddItem(text, idx);
            select.SetItemMetadata(idx, key);
            if (key == current)
            {
                select.Select(idx);
                matched = true;
            }
        }
        if (!matched)
        {
            select.Select(0);
        }
        select.ItemSelected += idx =>
        {
            string key = select.GetItemMetadata((int)idx).AsString();
            SettingsStore.Set("music", "global_mode", key);
            AudioManager.Instance?.RefreshCurrentBgm();
        };
        row.AddChild(select);
        return row;
    }

    private void RefreshMuteStates()
    {
        if (_masterMuteBtn != null) _masterMuteBtn.Text = MasterMuteText();
        if (_sfxMuteBtn != null) _sfxMuteBtn.Text = SfxMuteText();
        if (_bgmMuteBtn != null) _bgmMuteBtn.Text = BgmMuteText();
    }

    private static string MasterMuteText() =>
        AudioManager.Instance?.Muted == true ? I18n.T(Text.Settings.MutedOn) : I18n.T(Text.Settings.MutedOff);

    private static string SfxMuteText() =>
        AudioManager.Instance?.SfxMuted == true ? I18n.T(Text.Audio.SfxOff) : I18n.T(Text.Audio.SfxOn);

    private static string BgmMuteText() =>
        AudioManager.Instance?.BgmMuted == true ? I18n.T(Text.Audio.BgmOff) : I18n.T(Text.Audio.BgmOn);

    private static Label MakeSectionLabel(string text)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", 13);
        label.AddThemeColorOverride("font_color", Palette.Active.Accent);
        return label;
    }

    private static Label MakeRowLabel(string text)
    {
        var label = new Label { Text = text, CustomMinimumSize = new Vector2(88, 0) };
        label.AddThemeColorOverride("font_color", Palette.Active.TextPrimary);
        return label;
    }

    private static Button MakeButton(string text, Action onPressed)
    {
        var button = new Button { Text = text };
        button.AddThemeColorOverride("font_color", Palette.Active.TextPrimary);
        button.Pressed += () => onPressed();
        return button;
    }

    private static StyleBoxFlat MakePanelBox(Color bg, Color border, int radius, int padH, int padV) => new()
    {
        BgColor = bg,
        BorderColor = border,
        BorderWidthLeft = 1,
        BorderWidthRight = 1,
        BorderWidthTop = 1,
        BorderWidthBottom = 1,
        CornerRadiusTopLeft = radius,
        CornerRadiusTopRight = radius,
        CornerRadiusBottomLeft = radius,
        CornerRadiusBottomRight = radius,
        ContentMarginLeft = padH,
        ContentMarginRight = padH,
        ContentMarginTop = padV,
        ContentMarginBottom = padV,
    };
}
