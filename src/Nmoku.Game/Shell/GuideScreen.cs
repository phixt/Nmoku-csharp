using Godot;
using Nmoku.Core;
using Nmoku.Game.Theme;

namespace Nmoku.Game.Shell;

/// <summary>
/// 引导屏（GUIDE_FROM_TITLE / GUIDE_FROM_GAME）：静态文本版。M4 占位文案，内容深化与关卡化留 M4 后续轮。
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
        MouseFilter = MouseFilterEnum.Stop;

        AddChild(new ColorRect { Color = Palette.Default.Background, MouseFilter = MouseFilterEnum.Ignore });

        var center = new CenterContainer();
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 16);
        center.AddChild(box);

        var title = new Label { Text = Text.Guide.Title, HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 32);
        title.AddThemeColorOverride("font_color", Palette.Default.TextPrimary);
        box.AddChild(title);

        var body = new Label
        {
            Text = Text.Guide.Body,
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(640, 0),
        };
        body.AddThemeColorOverride("font_color", Palette.Default.TextDim);
        box.AddChild(body);

        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        box.AddChild(buttons);

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
        button.AddThemeColorOverride("font_color", Palette.Default.TextPrimary);
        button.Pressed += () => onPressed();
        return button;
    }
}
