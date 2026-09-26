using Godot;
using Nmoku.Core;
using Nmoku.Game.Theme;
using Nmoku.Game;

namespace Nmoku.Game.Shell;

/// <summary>
/// 标题屏（GameState.Title 的可见面）：开始/继续对局/读档/引导/退出。
/// 不透明背板由 SceneRouter 提供；"继续对局"仅在存在保留现场的对局时显示（KeepAlive 语义）。
/// </summary>
public sealed partial class TitleScreen : Control
{
    public event Action? StartRequested;
    public event Action? ResumeRequested;
    public event Action? CustomGameRequested;
    public event Action? LoadRequested;
    public event Action? GuideRequested;
    public event Action? SettingsRequested;
    public event Action? QuitRequested;

    private Button _resumeButton = null!;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop; // 挡住底下的棋盘交互

        var center = new CenterContainer();
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var box = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        // 布局容量硬约束（2026-09-26 根治）：逻辑面高 = 1080/2.5 = 432，M5 加"设置"按钮后内容
        // 自然高 ~470 溢出（CenterContainer 居中溢出语义=上下两端同时裁切 →「退出」不可见）。
        // 压缩到 ~375 留余量；UiScaleFactor 2.5 与按钮物理尺寸是人类拍板值不动。
        box.AddThemeConstantOverride("separation", 8);
        center.AddChild(box);

        var title = new Label { Text = I18n.T(Text.Title.GameName), HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 42);
        title.AddThemeColorOverride("font_color", Palette.Active.TextPrimary);
        box.AddChild(title);

        var subtitle = new Label { Text = I18n.T(Text.Title.Subtitle), HorizontalAlignment = HorizontalAlignment.Center };
        subtitle.AddThemeFontSizeOverride("font_size", 13);
        subtitle.AddThemeColorOverride("font_color", Palette.Active.TextDim);
        box.AddChild(subtitle);

        box.AddChild(MakeSpacer(10));
        box.AddChild(MakeButton(I18n.T(Text.Title.Start), () => StartRequested?.Invoke()));
        _resumeButton = MakeButton(I18n.T(Text.Title.ContinueGame), () => ResumeRequested?.Invoke());
        _resumeButton.Visible = false;
        box.AddChild(_resumeButton);
        box.AddChild(MakeButton(I18n.T(Text.Title.CustomGame), () => CustomGameRequested?.Invoke()));
        box.AddChild(MakeButton(I18n.T(Text.Title.LoadGame), () => LoadRequested?.Invoke()));
        box.AddChild(MakeButton(I18n.T(Text.Title.Guide), () => GuideRequested?.Invoke()));
        box.AddChild(MakeButton(I18n.T(Text.Settings.Title), () => SettingsRequested?.Invoke()));
        box.AddChild(MakeSpacer(6));
        box.AddChild(MakeButton(I18n.T(Text.Title.Quit), () => QuitRequested?.Invoke()));
    }

    /// <summary>进入标题态时由壳层更新：是否存在保留现场的对局。</summary>
    public void UpdateResumeButton(bool hasLiveGame) => _resumeButton.Visible = hasLiveGame;

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
            // 34：容纳「继续对局」出现的 7 按钮布局仍 fit 逻辑面高 432（40 时 448 溢出）
            CustomMinimumSize = new Vector2(220, 34),
        };
        button.AddThemeColorOverride("font_color", Palette.Active.TextPrimary);
        button.Pressed += () => onPressed();
        return button;
    }
}
