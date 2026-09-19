using Godot;
using Nmoku.Core;
using Nmoku.Game.Theme;

namespace Nmoku.Game.Shell;

/// <summary>标题屏（GameState.Title 的可见面）：开始/读档/引导/退出。M4 功能版布局，视觉打磨待人类验收。</summary>
public sealed partial class TitleScreen : Control
{
    public event Action? StartRequested;
    public event Action? LoadRequested;
    public event Action? GuideRequested;
    public event Action? QuitRequested;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop; // 挡住底下的棋盘交互

        AddChild(new ColorRect { Color = Palette.Default.Background, MouseFilter = MouseFilterEnum.Ignore });

        var center = new CenterContainer();
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var box = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        box.AddThemeConstantOverride("separation", 12);
        center.AddChild(box);

        var title = new Label { Text = Text.Title.GameName, HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 48);
        title.AddThemeColorOverride("font_color", Palette.Default.TextPrimary);
        box.AddChild(title);

        var subtitle = new Label { Text = Text.Title.Subtitle, HorizontalAlignment = HorizontalAlignment.Center };
        subtitle.AddThemeColorOverride("font_color", Palette.Default.TextDim);
        box.AddChild(subtitle);

        box.AddChild(MakeSpacer(16));
        box.AddChild(MakeButton(Text.Title.Start, () => StartRequested?.Invoke()));
        box.AddChild(MakeButton(Text.Title.LoadGame, () => LoadRequested?.Invoke()));
        box.AddChild(MakeButton(Text.Title.Guide, () => GuideRequested?.Invoke()));
        box.AddChild(MakeSpacer(8));
        box.AddChild(MakeButton(Text.Title.Quit, () => QuitRequested?.Invoke()));
    }

    private Control MakeSpacer(float height) => new()
    {
        CustomMinimumSize = new Vector2(0, height),
        MouseFilter = MouseFilterEnum.Ignore,
    };

    private Button MakeButton(string text, Action onPressed)
    {
        var button = new Button
        {
            Text = text,
            CustomMinimumSize = new Vector2(220, 40),
        };
        button.AddThemeColorOverride("font_color", Palette.Default.TextPrimary);
        button.Pressed += () => onPressed();
        return button;
    }
}
