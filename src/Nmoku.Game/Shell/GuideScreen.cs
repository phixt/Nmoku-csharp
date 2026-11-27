using Godot;
using Nmoku.Core;
using Nmoku.Game.Theme;

namespace Nmoku.Game.Shell;

/// <summary>
/// 引导屏（GUIDE_FROM_TITLE / GUIDE_FROM_GAME）：标题「快捷键说明」/暂停 G 的目标页——
/// 默认仅展示快捷键键盘页（<see cref="KeymapContent"/>）；右上角预留「作者的话」折叠切换按钮
/// （平时不出现，点击展开独立阅读卡片，配 guide BGM；F1 快捷浮层保持纯粹按键表）。
/// </summary>
public sealed partial class GuideScreen : Control
{
    /// <summary>true=从对局进入（显示"回到对局"），false=从标题进入（显示"开始游戏"/"返回"）。</summary>
    public bool FromGame { get; set; }

    /// <summary>当前是否正展开「作者的话」预留卡片（默认 false，平时不出现）。</summary>
    public bool IsAuthorNoteOpen => _authorNoteOpen;

    public event Action? StartRequested;
    public event Action? ResumeRequested;
    public event Action? BackRequested;

    public KeymapContent Content { get; private set; } = null!;

    private Control _legend = null!;
    private Control _authorNoteHost = null!;
    private Button _authorToggle = null!;
    private Button _start = null!;
    private Button _resume = null!;
    private Button _back = null!;
    private bool _authorNoteOpen;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop; // 不透明背板由 SceneRouter 提供，此处为内容层

        var margin = new MarginContainer();
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 10);
        margin.AddThemeConstantOverride("margin_right", 10);
        margin.AddThemeConstantOverride("margin_top", 8);
        margin.AddThemeConstantOverride("margin_bottom", 8);
        AddChild(margin);

        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = Palette.Active.PanelBg,
            BorderColor = Palette.Active.PanelBorder,
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            BorderWidthTop = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 10,
            CornerRadiusTopRight = 10,
            CornerRadiusBottomLeft = 10,
            CornerRadiusBottomRight = 10,
            ContentMarginLeft = 12,
            ContentMarginRight = 12,
            ContentMarginTop = 8,
            ContentMarginBottom = 8,
        });
        margin.AddChild(panel);

        var outer = new VBoxContainer();
        outer.AddThemeConstantOverride("separation", 4);
        panel.AddChild(outer);

        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 6);
        outer.AddChild(header);
        var title = new Label { Text = I18n.T(Text.Guide.Title), SizeFlagsHorizontal = SizeFlags.Expand };
        title.AddThemeFontSizeOverride("font_size", 14);
        title.AddThemeColorOverride("font_color", Palette.Active.TextPrimary);
        header.AddChild(title);

        _legend = KeymapContent.BuildLegend();
        header.AddChild(_legend);

        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        buttons.AddThemeConstantOverride("separation", 6);
        header.AddChild(buttons);

        // 「作者的话」默认折叠：平时仅作为一个克制的角标按钮存在，不占用正文空间
        _authorToggle = MakeButton(I18n.T(Text.Guide.AuthorNote), ToggleAuthorNote);
        _authorToggle.Name = "AuthorNoteToggle";
        _authorToggle.AddThemeColorOverride("font_color", Palette.Active.TextDim);
        buttons.AddChild(_authorToggle);

        buttons.AddChild(_start = MakeButton(I18n.T(Text.Guide.StartFromGuide), () => StartRequested?.Invoke()));
        buttons.AddChild(_resume = MakeButton(I18n.T(Text.Guide.ResumeFromGuide), () => ResumeRequested?.Invoke()));
        buttons.AddChild(_back = MakeButton(I18n.T(Text.Guide.Back), () => BackRequested?.Invoke()));

        Content = new KeymapContent();
        outer.AddChild(Content);

        _authorNoteHost = BuildAuthorNotePanel();
        _authorNoteHost.Name = "AuthorNotePanel";
        _authorNoteHost.Visible = false;
        outer.AddChild(_authorNoteHost);
    }

    /// <summary>按入口来源切换按钮可见性（_Ready 之后由壳层调用；每次进入默认收起「作者的话」）。</summary>
    public void ApplyMode()
    {
        _start.Visible = !FromGame;
        _resume.Visible = FromGame;
        _back.Visible = !FromGame;
        SetAuthorNoteOpen(false);
    }

    /// <summary>展开/收起「作者的话」预留卡片。</summary>
    public void SetAuthorNoteOpen(bool open)
    {
        _authorNoteOpen = open;
        Content.Visible = !open;
        _legend.Visible = !open;
        _authorNoteHost.Visible = open;
        _authorToggle.Text = open
            ? I18n.T(Text.Guide.AuthorNoteBack)
            : I18n.T(Text.Guide.AuthorNote);
        _authorToggle.AddThemeColorOverride("font_color",
            open ? Palette.Active.Accent : Palette.Active.TextDim);
    }

    private void ToggleAuthorNote() => SetAuthorNoteOpen(!_authorNoteOpen);

    /// <summary>
    /// 「作者的话」预留阅读容器（默认隐藏；支持长文滚动，后续直接替换文本即可）。
    /// </summary>
    private static Control BuildAuthorNotePanel()
    {
        var scroll = new ScrollContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };

        var center = new CenterContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        scroll.AddChild(center);

        var card = new PanelContainer
        {
            CustomMinimumSize = new Vector2(560, 0),
        };
        card.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = Palette.Active.Background.Lerp(Palette.Active.PanelBg, 0.55f),
            BorderColor = Palette.Active.PanelBorder,
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            BorderWidthTop = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 8,
            CornerRadiusTopRight = 8,
            CornerRadiusBottomLeft = 8,
            CornerRadiusBottomRight = 8,
            ContentMarginLeft = 26,
            ContentMarginRight = 26,
            ContentMarginTop = 22,
            ContentMarginBottom = 22,
        });
        center.AddChild(card);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 12);
        card.AddChild(box);

        var heading = new Label
        {
            Text = I18n.T(Text.Guide.AuthorNote),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        heading.AddThemeFontSizeOverride("font_size", 20);
        heading.AddThemeColorOverride("font_color", Palette.Active.Accent);
        box.AddChild(heading);

        var body = new Label
        {
            Name = "AuthorNoteBody",
            Text = I18n.T(Text.Guide.AuthorNotePlaceholder),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            HorizontalAlignment = HorizontalAlignment.Center,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        body.AddThemeFontSizeOverride("font_size", 14);
        body.AddThemeColorOverride("font_color", Palette.Active.TextDim);
        box.AddChild(body);

        return scroll;
    }

    private static Button MakeButton(string text, Action onPressed)
    {
        var button = new Button { Text = text };
        button.AddThemeColorOverride("font_color", Palette.Active.TextPrimary);
        button.Pressed += () => onPressed();
        return button;
    }
}
