using Godot;
using Nmoku.Core;
using Nmoku.Game.Theme;
using Nmoku.Game;

namespace Nmoku.Game.Shell;

/// <summary>复盘条（GameState.Replay）：步进出/入 + 进度条拖动 + 退出（01-core §2.4 游标语义）。</summary>
public sealed partial class ReplayBar : Control
{
    public event Action<int>? StepRequested;      // ±1
    public event Action<int>? SeekRequested;      // 拖动进度条
    public event Action? ExitRequested;

    private Label _position = null!;
    private HSlider _slider = null!;
    private Button _prev = null!;
    private Button _next = null!;

    /// <summary>空局复盘（0 手）：步进与拖动全部禁用（批10 适配；自测读取）。</summary>
    public bool ZeroMoveMode { get; private set; }

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
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            BorderWidthTop = 1,
            BorderWidthBottom = 1,
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

        var title = new Label { Text = I18n.T(Text.Replay.Label) };
        title.AddThemeColorOverride("font_color", Palette.Active.TextPrimary);
        row.AddChild(title);

        _prev = MakeButton(I18n.T(Text.Replay.PrevStep), () => StepRequested?.Invoke(-1));
        row.AddChild(_prev);

        // 滑条 280→200：批10 修右侧出界（CenterBottom+Grow.Both 下面板最小宽不得超过 768 逻辑宽，
        // 需给右下角行棋指示器留位）；m4c-replay-hud 以几何断言锁"不出界、不与指示器重叠"
        _slider = new HSlider
        {
            MinValue = 0,
            MaxValue = 1,
            Step = 1,
            CustomMinimumSize = new Vector2(200, 24),
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
        };
        _slider.ValueChanged += v => SeekRequested?.Invoke((int)v);
        row.AddChild(_slider);

        _next = MakeButton(I18n.T(Text.Replay.NextStep), () => StepRequested?.Invoke(1));
        row.AddChild(_next);

        _position = new Label { Text = "0 / 0" };
        _position.AddThemeColorOverride("font_color", Palette.Active.TextPrimary);
        row.AddChild(_position);

        row.AddChild(MakeButton(I18n.T(Text.Replay.Exit), () => ExitRequested?.Invoke()));
    }

    /// <summary>同步游标显示（GM.StateChanged 驱动）。count=总手数，index=当前可见手数。</summary>
    public void Sync(int index, int count)
    {
        // 空局复盘（0 手）：无步可进——滑条与步进钮全部禁用（批10 适配）
        ZeroMoveMode = count == 0;
        _slider.Editable = !ZeroMoveMode; // HSlider 用 Editable 反向语义
        _prev.Disabled = ZeroMoveMode;
        _next.Disabled = ZeroMoveMode;
        _slider.MaxValue = Math.Max(count, 1);
        _slider.SetValueNoSignal(Math.Clamp(index, 0, count));
        _position.Text = I18n.T(Text.Replay.StepInfo, index, count);
    }

    private Button MakeButton(string text, Action onPressed)
    {
        var button = new Button { Text = text };
        button.AddThemeColorOverride("font_color", Palette.Active.TextPrimary);
        button.Pressed += () => onPressed();
        return button;
    }
}
