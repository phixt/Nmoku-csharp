using Godot;
using Nmoku.Core;
using Nmoku.Game.Theme;

namespace Nmoku.Game.Shell;

/// <summary>暂停菜单（GameState.Paused）：继续/重开（带确认）/读档/引导/返回标题（01-core §3.4 PAUSED 行）。</summary>
public sealed partial class PauseMenu : Control
{
    public event Action? ResumeRequested;
    public event Action? RestartRequested;
    public event Action? SaveMenuRequested;
    public event Action? GuideRequested;
    public event Action? TitleRequested;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop; // dim+blur 背板由 SceneRouter 提供，此处为内容层

        var center = new CenterContainer();
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", MakePanelBox());
        center.AddChild(panel);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 10);
        box.CustomMinimumSize = new Vector2(260, 0);
        panel.AddChild(box);

        var label = new Label { Text = Text.Menu.Paused, HorizontalAlignment = HorizontalAlignment.Center };
        label.AddThemeFontSizeOverride("font_size", 28);
        label.AddThemeColorOverride("font_color", Palette.Default.TextPrimary);
        box.AddChild(label);

        box.AddChild(MakeButton(Text.Menu.Resume, () => ResumeRequested?.Invoke()));
        box.AddChild(MakeButton(Text.Menu.Restart, () => RestartRequested?.Invoke()));
        box.AddChild(MakeButton(Text.Title.LoadGame, () => SaveMenuRequested?.Invoke()));
        box.AddChild(MakeButton(Text.Title.Guide, () => GuideRequested?.Invoke()));
        box.AddChild(MakeButton(Text.Menu.BackToTitle, () => TitleRequested?.Invoke()));
    }

    private StyleBoxFlat MakePanelBox() => new()
    {
        BgColor = Palette.Default.PanelBg,
        BorderColor = Palette.Default.PanelBorder,
        CornerRadiusTopLeft = 8,
        CornerRadiusTopRight = 8,
        CornerRadiusBottomLeft = 8,
        CornerRadiusBottomRight = 8,
        ContentMarginLeft = 24,
        ContentMarginRight = 24,
        ContentMarginTop = 20,
        ContentMarginBottom = 20,
    };

    private Button MakeButton(string text, Action onPressed)
    {
        var button = new Button { Text = text };
        button.AddThemeColorOverride("font_color", Palette.Default.TextPrimary);
        button.Pressed += () => onPressed();
        return button;
    }
}
