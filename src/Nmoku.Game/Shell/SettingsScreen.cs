using Godot;
using Nmoku.Core;
using Nmoku.Game.Theme;

namespace Nmoku.Game.Shell;

/// <summary>窗口模式（设置页/启动恢复共用；headless 下操作由 Main 跳过）。</summary>
public enum WindowMode
{
    Windowed,
    Maximized,
    Fullscreen,
}

/// <summary>
/// 设置屏（Overlay 浮层，Title/Paused 均可打开）：专注显示缩放与语言（general 节）+ 直达「音乐与音效」入口。
/// 原音频设置（音量滑条、独立/全局静音、对局音乐与切屏策略）已合并迁入 <see cref="AudioHubScreen"/>。
/// 窗口模式/分辨率/UI 倍率发事件由 Main 操作窗口（设置页不碰 DisplayServer）；语言切换走 TranslationServer + EventBus。
/// </summary>
public sealed partial class SettingsScreen : Control
{
    public event Action? CloseRequested;
    public event Action? AudioHubRequested;
    public event Action<WindowMode>? WindowModeChanged;
    public event Action<Vector2I>? WindowResolutionChanged;
    public event Action<float>? UiScaleChanged;

    private static readonly string[] Locales = ["zh-CN", "zh-Hant", "en-US", "ja-JP"];
    private static readonly string[] ModeKeys = ["windowed", "maximized", "fullscreen"];

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
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            BorderWidthTop = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 8,
            CornerRadiusTopRight = 8,
            CornerRadiusBottomLeft = 8,
            CornerRadiusBottomRight = 8,
            ContentMarginLeft = 24,
            ContentMarginRight = 24,
            ContentMarginTop = 18,
            ContentMarginBottom = 18,
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

        box.AddChild(MakeWindowModeRow());
        box.AddChild(MakeResolutionRow());
        box.AddChild(MakeUiScaleRow());
        box.AddChild(MakeLanguageRow());

        var audioLink = MakeButton($"{I18n.T(Text.Audio.HubTitle)}  →", () => AudioHubRequested?.Invoke());
        audioLink.Name = "OpenAudioHubButton";
        box.AddChild(audioLink);
    }

    private Control MakeWindowModeRow()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        row.AddChild(MakeRowLabel(I18n.T(Text.Settings.WindowMode)));

        var select = new OptionButton
        {
            Name = "WindowModeSelect",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        // 初值=实际窗口状态（非 cfg）：cfg 只在 SizeChanged 同步点与实际收敛，重启恢复期可能短暂
        // 分叉——下拉作为"当前模式视图"以窗口为准（headless 无窗口退回 cfg，回归锁用）
        string current = DisplayServer.GetName() == "headless"
            ? SettingsStore.Get("general", "window_mode", "windowed").AsString()
            : GetWindow().Mode switch
            {
                Window.ModeEnum.Maximized => "maximized",
                Window.ModeEnum.Fullscreen or Window.ModeEnum.ExclusiveFullscreen => "fullscreen",
                _ => "windowed",
            };
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

    /// <summary>窗口化分辨率挡位（仍保留自由拖拽；选定即切回窗口化并按挡位居中）。</summary>
    private Control MakeResolutionRow()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        row.AddChild(MakeRowLabel(I18n.T(Text.Settings.Resolution)));

        var select = new OptionButton
        {
            Name = "ResolutionSelect",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        string saved = SettingsStore.Get("general", "window_resolution",
            UiScaleMath.FormatResolutionKey(UiScaleMath.DefaultWindowWidth, UiScaleMath.DefaultWindowHeight)).AsString();
        var activeRes = UiScaleMath.TryParseResolutionKey(saved)
                        ?? (UiScaleMath.DefaultWindowWidth, UiScaleMath.DefaultWindowHeight);
        string activeKey = UiScaleMath.FormatResolutionKey(activeRes.Width, activeRes.Height);

        int selectedIdx = -1;
        foreach (var (w, h) in UiScaleMath.ResolutionPresets)
        {
            string key = UiScaleMath.FormatResolutionKey(w, h);
            int idx = select.ItemCount;
            select.AddItem($"{w} × {h}", idx);
            select.SetItemMetadata(idx, key);
            if (key == activeKey)
            {
                selectedIdx = idx;
            }
        }
        if (selectedIdx >= 0)
        {
            select.Select(selectedIdx);
        }
        select.ItemSelected += idx =>
        {
            string key = select.GetItemMetadata((int)idx).AsString();
            if (UiScaleMath.TryParseResolutionKey(key) is { } parsed)
            {
                SettingsStore.Set("general", "window_resolution", key);
                WindowResolutionChanged?.Invoke(new Vector2I(parsed.Width, parsed.Height));
            }
        };
        row.AddChild(select);
        return row;
    }

    /// <summary>UI 缩放倍率（csf = (窗口高/540) × 倍率；0.75x / 1.0x / 1.25x / 1.5x）。</summary>
    private Control MakeUiScaleRow()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        row.AddChild(MakeRowLabel(I18n.T(Text.Settings.UiScale)));

        var select = new OptionButton
        {
            Name = "UiScaleSelect",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        float current = UiScaleMath.NormalizeMultiplier(
            (float)SettingsStore.Get("general", "ui_scale", UiScaleMath.DefaultMultiplier).AsSingle());
        int bestIdx = 1;
        float bestDiff = float.MaxValue;
        foreach (float mult in UiScaleMath.MultiplierPresets)
        {
            int idx = select.ItemCount;
            select.AddItem($"{mult:0.##}x", idx);
            select.SetItemMetadata(idx, mult);
            float diff = MathF.Abs(mult - current);
            if (diff < bestDiff)
            {
                bestDiff = diff;
                bestIdx = idx;
            }
        }
        select.Select(bestIdx);
        select.ItemSelected += idx =>
        {
            float mult = UiScaleMath.NormalizeMultiplier((float)select.GetItemMetadata((int)idx).AsSingle());
            SettingsStore.Set("general", "ui_scale", mult);
            UiScaleChanged?.Invoke(mult);
        };
        row.AddChild(select);
        return row;
    }

    private Control MakeLanguageRow()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        row.AddChild(MakeRowLabel(I18n.T(Text.Settings.Language)));

        var select = new OptionButton
        {
            Name = "LanguageSelect",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        // 选项=语言自称（不翻译），当前值来自 TranslationServer 实时 locale。
        // 匹配必须经 StandardizeLocale：GetLocale 返回下划线形（zh_CN），选项键是横线形（zh-CN）
        string current = TranslationServer.StandardizeLocale(TranslationServer.GetLocale());
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
            if (TranslationServer.StandardizeLocale(locale) == current)
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

    private static Label MakeRowLabel(string text)
    {
        var label = new Label { Text = text, CustomMinimumSize = new Vector2(110, 0) };
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
}
