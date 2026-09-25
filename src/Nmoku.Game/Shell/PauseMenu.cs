using Godot;
using Nmoku.Core;
using Nmoku.Game.Theme;

/// <summary>
/// 暂停菜单（GameState.Paused）：六项与源项目 esc-menu 对齐，每项旁带键位提示
/// （提示键位取自 KeymapTable 单一数据源，不硬编码）：继续 Esc/重开 R/保存 S/读档（开存档菜单）/说明 G/标题 B。
/// 读档按钮无键位提示——L 键语义是"读最新"（源 esc-load 同款快路径），与打开菜单的按钮不同义。
/// </summary>
public sealed partial class PauseMenu : Control
{
    public event Action? ResumeRequested;
    public event Action? RestartRequested;
    public event Action? SaveProgressRequested;
    public event Action? LoadRequested;
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
        box.CustomMinimumSize = new Vector2(300, 0);
        panel.AddChild(box);

        var label = new Label { Text = Text.Menu.Paused, HorizontalAlignment = HorizontalAlignment.Center };
        label.AddThemeFontSizeOverride("font_size", 28);
        label.AddThemeColorOverride("font_color", Palette.Active.TextPrimary);
        box.AddChild(label);

        box.AddChild(MakeMenuButton(Text.Menu.Resume, "resume", () => ResumeRequested?.Invoke()));
        box.AddChild(MakeMenuButton(Text.Menu.Restart, "restart", () => RestartRequested?.Invoke()));
        box.AddChild(MakeMenuButton(Text.Menu.SaveProgress, "save_first_empty", () => SaveProgressRequested?.Invoke()));
        box.AddChild(MakeMenuButton(Text.Title.LoadGame, action: null, () => LoadRequested?.Invoke()));
        box.AddChild(MakeMenuButton(Text.Title.Guide, "open_guide", () => GuideRequested?.Invoke()));
        box.AddChild(MakeMenuButton(Text.Menu.BackToTitle, "return_to_title", () => TitleRequested?.Invoke()));
    }

    /// <summary>选项文案 + 键位提示（KeymapTable.HintKeyOf；action 为 null 或查不到时只显示文案）。</summary>
    private Button MakeMenuButton(string text, string? action, Action onPressed)
    {
        string? hint = action == null ? null : KeymapTable.HintKeyOf(action);
        var button = new Button { Text = hint == null ? text : $"{text}  [{hint}]" };
        button.AddThemeColorOverride("font_color", Palette.Active.TextPrimary);
        button.Pressed += () => onPressed();
        return button;
    }

    private StyleBoxFlat MakePanelBox() => new()
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
    };
}
