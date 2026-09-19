using Godot;
using Nmoku.Core;
using Nmoku.Game.Theme;

namespace Nmoku.Game.Shell;

/// <summary>复盘条（GameState.Replay）：步进出/入 + 进度条拖动 + 退出（01-core §2.4 游标语义）。</summary>
public sealed partial class ReplayBar : Control
{
    public event Action<int>? StepRequested;      // ±1
    public event Action<int>? SeekRequested;      // 拖动进度条
    public event Action? ExitRequested;

    private Label _position = null!;
    private HSlider _slider = null!;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.CenterBottom);
        GrowHorizontal = GrowDirection.Both;
        OffsetTop = -72;

        var panel = new PanelContainer { MouseFilter = MouseFilterEnum.Stop };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = Palette.Active.PanelBg,
            BorderColor = Palette.Active.PanelBorder,
            CornerRadiusTopLeft = 8,
            CornerRadiusTopRight = 8,
            CornerRadiusBottomLeft = 8,
            CornerRadiusBottomRight = 8,
            ContentMarginLeft = 16,
            ContentMarginRight = 16,
            ContentMarginTop = 8,
            ContentMarginBottom = 8,
        });
        AddChild(panel);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        panel.AddChild(row);

        var title = new Label { Text = Text.Replay.Label };
        title.AddThemeColorOverride("font_color", Palette.Active.TextPrimary);
        row.AddChild(title);

        row.AddChild(MakeButton("上一步", () => StepRequested?.Invoke(-1)));

        _slider = new HSlider
        {
            MinValue = 0,
            MaxValue = 1,
            Step = 1,
            CustomMinimumSize = new Vector2(280, 24),
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
        };
        _slider.ValueChanged += v => SeekRequested?.Invoke((int)v);
        row.AddChild(_slider);

        row.AddChild(MakeButton("下一步", () => StepRequested?.Invoke(1)));

        _position = new Label { Text = "0 / 0" };
        _position.AddThemeColorOverride("font_color", Palette.Active.TextPrimary);
        row.AddChild(_position);

        row.AddChild(MakeButton(Text.Replay.Exit, () => ExitRequested?.Invoke()));
    }

    /// <summary>同步游标显示（GM.StateChanged 驱动）。count=总手数，index=当前可见手数。</summary>
    public void Sync(int index, int count)
    {
        _slider.MaxValue = Math.Max(count, 1);
        _slider.SetValueNoSignal(Math.Clamp(index, 0, count));
        _position.Text = string.Format(Text.Replay.StepInfo, index, count);
    }

    private Button MakeButton(string text, Action onPressed)
    {
        var button = new Button { Text = text };
        button.AddThemeColorOverride("font_color", Palette.Active.TextPrimary);
        button.Pressed += () => onPressed();
        return button;
    }
}
