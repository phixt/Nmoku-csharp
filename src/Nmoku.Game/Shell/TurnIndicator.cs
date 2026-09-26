using Godot;
using Nmoku.Core;
using Nmoku.Game.Theme;
using Nmoku.Game.Views;

namespace Nmoku.Game.Shell;

/// <summary>
/// 行棋指示器（批10 统一，人类拍板 2026-09-26）：虚拟棋子在上、行棋方/结果文字在下（字号略缩），
/// 锚定右下角。对局与复盘两态共用——复盘首次获得该指示（B13 右上角假棋子 + 底部居中状态字退役，
/// 同时消除与复盘条的 CenterBottom 同锚重叠）。纯视图：内容由 Main.UpdateStatus 驱动，不持棋局状态。
/// </summary>
public sealed partial class TurnIndicator : Control
{
    private const float StoneRadius = 10f; // 逻辑 px（13×13 盘面石径 ≈9.7，取整）
    private const int FontSize = 14;       // 原状态字 16 略缩（人类指示）

    private StoneDot _stone = null!;
    private Label _text = null!;

    /// <summary>自测读取。</summary>
    public string TextForTests => _text.Text;
    /// <summary>自测读取。</summary>
    public bool StoneVisible => _stone.Visible;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.BottomRight);
        GrowHorizontal = GrowDirection.Begin; // 向左生长，不出右界
        GrowVertical = GrowDirection.Begin;

        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", 2);
        AddChild(box);

        _stone = new StoneDot();
        box.AddChild(_stone);

        _text = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _text.AddThemeFontSizeOverride("font_size", FontSize);
        box.AddChild(_text);

        ApplyStyle();
    }

    /// <summary>行棋态：虚拟棋子 + 行棋方文字（对局与复盘共用）。</summary>
    public void SetTurn(CellState side, string text)
    {
        _stone.Side = side;
        _stone.Visible = true;
        _text.Text = text;
    }

    /// <summary>非行棋态（结算/SWAP2 决策提示）：只显示文字。</summary>
    public void SetTextOnly(string text)
    {
        _stone.Visible = false;
        _text.Text = text;
    }

    /// <summary>主题切换（T）：文字颜色/描边随套，棋子由 StoneDot._Draw 读 Palette.Active 自愈。</summary>
    public void ApplyStyle()
    {
        _text.AddThemeColorOverride("font_color", Palette.Active.TextPrimary);
        _text.AddThemeColorOverride("font_outline_color", Palette.Active.TextOutline);
        _text.AddThemeConstantOverride("outline_size", 6);
        _stone.QueueRedraw();
    }

    /// <summary>指示棋子（复用盘面四层质感；Empty=不画）。</summary>
    private sealed partial class StoneDot : Control
    {
        public CellState Side { get; set; } = CellState.Black;

        public StoneDot()
        {
            MouseFilter = MouseFilterEnum.Ignore;
            CustomMinimumSize = new Vector2(StoneRadius * 2 + 4, StoneRadius * 2 + 4);
        }

        public override void _Draw()
        {
            if (Side != CellState.Empty)
            {
                StonePainter.Draw(this, Size / 2f, StoneRadius, Side, 0.9f, Palette.Active);
            }
        }
    }
}
