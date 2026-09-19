using Godot;
using Nmoku.Core;
using Nmoku.Game.Theme;

namespace Nmoku.Game.Shell;

/// <summary>结算模态（GameState.GameOver）：胜负/判和 + 总手数，重开需确认（§3.4 GAME_OVER 行）。</summary>
public sealed partial class GameOverModal : Control
{
    public event Action? RestartRequested;
    public event Action? TitleRequested;

    private Label _result = null!;
    private Label _subtitle = null!;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop; // dim+blur 背板由 SceneRouter 提供，此处为内容层

        var center = new CenterContainer();
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = Palette.Active.PanelBg,
            BorderColor = Palette.Active.Accent,
            CornerRadiusTopLeft = 10,
            CornerRadiusTopRight = 10,
            CornerRadiusBottomLeft = 10,
            CornerRadiusBottomRight = 10,
            ContentMarginLeft = 32,
            ContentMarginRight = 32,
            ContentMarginTop = 24,
            ContentMarginBottom = 24,
        });
        center.AddChild(panel);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 12);
        panel.AddChild(box);

        _result = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _result.AddThemeFontSizeOverride("font_size", 34);
        _result.AddThemeColorOverride("font_color", Palette.Active.TextPrimary);
        box.AddChild(_result);

        _subtitle = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _subtitle.AddThemeColorOverride("font_color", Palette.Active.TextDim);
        box.AddChild(_subtitle);

        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        box.AddChild(buttons);
        buttons.AddChild(MakeButton(Text.Menu.Restart, () => RestartRequested?.Invoke()));
        buttons.AddChild(MakeButton(Text.Menu.BackToTitle, () => TitleRequested?.Invoke()));
    }

    /// <summary>进入 GameOver 态时由壳层填充：winner 1=黑 2=白，0=判和。</summary>
    public void SetResult(int winner, int moveCount)
    {
        _result.Text = winner == 0 ? Text.Result.Draw : winner == 1 ? Text.Result.BlackWin : Text.Result.WhiteWin;
        _subtitle.Text = string.Format(Text.Result.GameOverSubtitle, moveCount);
    }

    private Button MakeButton(string text, Action onPressed)
    {
        var button = new Button { Text = text };
        button.AddThemeColorOverride("font_color", Palette.Active.TextPrimary);
        button.Pressed += () => onPressed();
        return button;
    }
}
