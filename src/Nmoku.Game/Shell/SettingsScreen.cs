using Godot;
using Nmoku.Core;
using Nmoku.Game.Audio;
using Nmoku.Game.Theme;
using Nmoku.Game;

namespace Nmoku.Game.Shell;

/// <summary>窗口模式（设置页/启动恢复共用；headless 下操作由 Main 跳过）。</summary>
public enum WindowMode
{
    Windowed,
    Maximized,
    Fullscreen,
}

/// <summary>
/// 设置屏（Overlay 浮层，Title/Paused 均可打开）：音量/静音（audio 节）+ 窗口模式/语言（general 节）。
/// 音量与静音经 AudioManager 即时生效并持久化；窗口模式发事件由 Main 操作窗口（设置页不碰 DisplayServer）；
/// 语言切换走 TranslationServer + EventBus（热刷新由壳层重建壳屏，B20 ④）。键位重绑定=大工程，不在本屏。
/// </summary>
public sealed partial class SettingsScreen : Control
{
    public event Action? CloseRequested;
    public event Action<WindowMode>? WindowModeChanged;

    private static readonly string[] Locales = ["zh-CN", "zh-Hant", "en-US", "ja-JP"];
    private static readonly string[] ModeKeys = ["windowed", "maximized", "fullscreen"];

    private Button _muteButton = null!;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop; // dim 背板由 SceneRouter 提供，此处为内容层

        var center = new CenterContainer();
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = Palette.Active.PanelBg,
            BorderColor = Palette.Active.PanelBorder,
            CornerRadiusTopLeft = 8,
            CornerRadiusTopRight = 8,
            CornerRadiusBottomLeft = 8,
            CornerRadiusBottomRight = 8,
            ContentMarginLeft = 24,
            ContentMarginRight = 24,
            ContentMarginTop = 20,
            ContentMarginBottom = 20,
        });
        center.AddChild(panel);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 10);
        box.CustomMinimumSize = new Vector2(420, 0);
        panel.AddChild(box);

        var header = new HBoxContainer();
        box.AddChild(header);
        var title = new Label { Text = I18n.T(Text.Settings.Title), SizeFlagsHorizontal = SizeFlags.Expand };
        title.AddThemeFontSizeOverride("font_size", 24);
        title.AddThemeColorOverride("font_color", Palette.Active.TextPrimary);
        header.AddChild(title);
        header.AddChild(MakeButton(I18n.T(Text.Overlay.Cancel), () => CloseRequested?.Invoke()));

        var audio = AudioManager.Instance;
        float sfx = audio?.SfxVolume ?? 0.6f;
        float bgm = audio?.BgmVolume ?? 0.3f;

        box.AddChild(MakeVolumeRow(I18n.T(Text.Settings.SfxVolume), sfx, v =>
            audio?.SetVolumes(v, audio.BgmVolume)));
        box.AddChild(MakeVolumeRow(I18n.T(Text.Settings.BgmVolume), bgm, v =>
            audio?.SetVolumes(audio.SfxVolume, v)));

        _muteButton = MakeButton(MuteText(), ToggleMute);
        box.AddChild(_muteButton);

        box.AddChild(MakeWindowModeRow());
        box.AddChild(MakeLanguageRow());
    }

    private Control MakeVolumeRow(string label, float value, Action<float> onChanged)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);

        row.AddChild(MakeRowLabel(label));

        var slider = new HSlider
        {
            MinValue = 0,
            MaxValue = 1,
            Step = 0.05,
            Value = value,
            SizeFlagsHorizontal = SizeFlags.Expand,
        };
        var percent = new Label { CustomMinimumSize = new Vector2(48, 0) };
        percent.Text = $"{(int)Mathf.Round(value * 100)}%";
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

    private Control MakeWindowModeRow()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        row.AddChild(MakeRowLabel(I18n.T(Text.Settings.WindowMode)));

        var select = new OptionButton { SizeFlagsHorizontal = SizeFlags.Expand };
        string current = SettingsStore.Get("general", "window_mode", "windowed").AsString();
        foreach (var (key, text) in new[]
                 {
                     (ModeKeys[0], I18n.T(Text.Settings.ModeWindowed)),
                     (ModeKeys[1], I18n.T(Text.Settings.ModeMaximized)),
                     (ModeKeys[2], I18n.T(Text.Settings.ModeFullscreen)),
                 })
        {
            int idx = select.ItemCount;
            select.AddItem(text, idx);
            select.SetItemMetadata(idx, key);
            if (key == current)
            {
                select.Select(idx);
            }
        }
        select.ItemSelected += idx =>
        {
            Variant meta = select.GetItemMetadata((int)idx);
            string key = meta.AsString();
            SettingsStore.Set("general", "window_mode", key);
            WindowModeChanged?.Invoke(key switch
            {
                "maximized" => WindowMode.Maximized,
                "fullscreen" => WindowMode.Fullscreen,
                _ => WindowMode.Windowed,
            });
        };
        row.AddChild(select);
        return row;
    }

    private Control MakeLanguageRow()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        row.AddChild(MakeRowLabel(I18n.T(Text.Settings.Language)));

        var select = new OptionButton { SizeFlagsHorizontal = SizeFlags.Expand };
        // 选项=语言自称（不翻译），当前值来自持久化偏好
        foreach (var (locale, name) in new[]
                 {
                     (Locales[0], "简体中文"),
                     (Locales[1], "繁體中文"),
                     (Locales[2], "English"),
                     (Locales[3], "日本語"),
                 })
        {
            int idx = select.ItemCount;
            select.AddItem(name, idx);
            select.SetItemMetadata(idx, locale);
            if (locale == TranslationServer.GetLocale())
            {
                select.Select(idx);
            }
        }
        select.ItemSelected += idx =>
        {
            string locale = select.GetItemMetadata((int)idx).AsString();
            TranslationServer.SetLocale(locale);
            SettingsStore.Set("general", "lang", locale);
            EventBus.EmitLanguageChanged(locale);
        };
        row.AddChild(select);
        return row;
    }

    private Label MakeRowLabel(string text)
    {
        var label = new Label { Text = text, CustomMinimumSize = new Vector2(110, 0) };
        label.AddThemeColorOverride("font_color", Palette.Active.TextPrimary);
        return label;
    }

    private string MuteText() =>
        AudioManager.Instance?.Muted == true ? I18n.T(Text.Settings.MutedOn) : I18n.T(Text.Settings.MutedOff);

    private void ToggleMute()
    {
        AudioManager.Instance?.ToggleMute();
        _muteButton.Text = MuteText();
    }

    private Button MakeButton(string text, Action onPressed)
    {
        var button = new Button { Text = text };
        button.AddThemeColorOverride("font_color", Palette.Active.TextPrimary);
        button.Pressed += () => onPressed();
        return button;
    }
}
