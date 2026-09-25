using Godot;
using Nmoku.Core;
using Nmoku.Game.Theme;

namespace Nmoku.Game.Shell;

/// <summary>
/// 引导屏（GUIDE_FROM_TITLE / GUIDE_FROM_GAME）：标题「快捷键说明」/暂停 G 的目标页——
/// 内容即快捷键键盘页（<see cref="KeymapContent"/>，与 F1 浮层共用，2026-09-25 修复入口错位：
/// 此前本屏是占位文案，人类点「快捷键说明」看不到键盘页）。底部按钮按入口切换（§3.4 两行语义）。
/// </summary>
public sealed partial class GuideScreen : Control
{
    /// <summary>true=从对局进入（显示"回到对局"），false=从标题进入（显示"开始游戏"/"返回"）。</summary>
    public bool FromGame { get; set; }

    public event Action? StartRequested;
    public event Action? ResumeRequested;
    public event Action? BackRequested;

    private Button _start = null!;
    private Button _resume = null!;
    private Button _back = null!;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop; // 不透明背板由 SceneRouter 提供，此处为内容层

        var margin = new MarginContainer();
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 24);
        margin.AddThemeConstantOverride("margin_right", 24);
        margin.AddThemeConstantOverride("margin_top", 16);
        margin.AddThemeConstantOverride("margin_bottom", 16);
        AddChild(margin);

        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = Palette.Active.PanelBg,
            BorderColor = Palette.Active.PanelBorder,
            CornerRadiusTopLeft = 10,
            CornerRadiusTopRight = 10,
            CornerRadiusBottomLeft = 10,
            CornerRadiusBottomRight = 10,
            ContentMarginLeft = 20,
            ContentMarginRight = 20,
            ContentMarginTop = 12,
            ContentMarginBottom = 12,
        });
        margin.AddChild(panel);

        var outer = new VBoxContainer();
        outer.AddThemeConstantOverride("separation", 8);
        panel.AddChild(outer);

        var title = new Label { Text = Text.Guide.Title };
        title.AddThemeFontSizeOverride("font_size", 22);
        title.AddThemeColorOverride("font_color", Palette.Active.TextPrimary);
        outer.AddChild(title);

        outer.AddChild(new KeymapContent());

        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        outer.AddChild(buttons);

        _start = MakeButton(Text.Guide.StartFromGuide, () => StartRequested?.Invoke());
        _resume = MakeButton(Text.Guide.ResumeFromGuide, () => ResumeRequested?.Invoke());
        _back = MakeButton(Text.Guide.Back, () => BackRequested?.Invoke());
        buttons.AddChild(_start);
        buttons.AddChild(_resume);
        buttons.AddChild(_back);
    }

    /// <summary>按入口来源切换按钮可见性（_Ready 之后由壳层调用）。</summary>
    public void ApplyMode()
    {
        _start.Visible = !FromGame;
        _resume.Visible = FromGame;
        _back.Visible = !FromGame;
    }

    private Button MakeButton(string text, Action onPressed)
    {
        var button = new Button { Text = text };
        button.AddThemeColorOverride("font_color", Palette.Active.TextPrimary);
        button.Pressed += () => onPressed();
        return button;
    }
}
