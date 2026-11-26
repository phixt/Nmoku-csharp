using Godot;
using Nmoku.Core;
using Nmoku.Core.AI;
using Nmoku.Game.Theme;
using Nmoku.Game;

namespace Nmoku.Game.Shell;

/// <summary>
/// 沙盒自定义面板（L2 浮层，标题屏"自定义对局"打开）：预设 + 尺寸/层数/连珠/规则/模式/AI 表单。
/// 约束按 00-domain §2.2/§2.8（尺寸奇数 ≥5、连珠 4–9），非法组合开始时 toast 提示。
/// tutorial/residual 为占位重设计项（TODO 挂账），此处先以最小可玩预设提供。
/// </summary>
public sealed partial class SandboxPanel : Control
{
    public event Action<LevelConfig, bool, AIDifficulty>? StartRequested;
    public event Action? CloseRequested;
    /// <summary>校验提示等非模态通知（Main 接到 OverlayManager）。</summary>
    public event Action<string, bool>? ToastRequested;

    private OptionButton _preset = null!;
    private SpinBox _size = null!;
    private SpinBox _layers = null!;
    private SpinBox _winLength = null!;
    private OptionButton _rules = null!;
    private OptionButton _mode = null!;
    private OptionButton _ai = null!;
    private Label _aiLabel = null!;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop; // dim 背板由 SceneRouter 提供

        var center = new CenterContainer();
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", MakePanelBox());
        center.AddChild(panel);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 10);
        panel.AddChild(box);

        var header = new HBoxContainer();
        box.AddChild(header);
        var title = new Label { Text = I18n.T(Text.Levels.Custom), SizeFlagsHorizontal = SizeFlags.Expand };
        title.AddThemeFontSizeOverride("font_size", 24);
        title.AddThemeColorOverride("font_color", Palette.Active.TextPrimary);
        header.AddChild(title);
        header.AddChild(MakeButton(I18n.T(Text.Overlay.Cancel), () => CloseRequested?.Invoke()));

        var form = new GridContainer { Columns = 2 };
        form.AddThemeConstantOverride("h_separation", 10);
        form.AddThemeConstantOverride("v_separation", 10);
        box.AddChild(form);

        _preset = new OptionButton();
        foreach (var preset in LevelCatalog.Presets)
        {
            _preset.AddItem(I18n.T(preset.Name));
        }
        _preset.ItemSelected += i => Prefill((int)i);
        form.AddRow(I18n.T(Text.Levels.Preset), _preset);

        _size = new SpinBox { MinValue = 5, MaxValue = 19, Step = 1, Value = 13 };
        form.AddRow(I18n.T(Text.Levels.BoardSize), _size);

        _layers = new SpinBox { MinValue = 1, MaxValue = 8, Step = 1, Value = 6 };
        form.AddRow(I18n.T(Text.Levels.Layers), _layers);

        _winLength = new SpinBox { MinValue = GameConfig.MinWinLength, MaxValue = GameConfig.MaxWinLength, Step = 1, Value = GameConfig.WinLength };
        form.AddRow(I18n.T(Text.Levels.WinLength), _winLength);

        _rules = new OptionButton();
        _rules.AddItem(I18n.T(Text.Levels.Gomoku));
        _rules.AddItem(I18n.T(Text.Levels.Swap2));
        form.AddRow(I18n.T(Text.Levels.Rules), _rules);

        _mode = new OptionButton();
        _mode.AddItem(I18n.T(Text.Levels.PvP));
        _mode.AddItem(I18n.T(Text.Levels.PvE));
        _mode.ItemSelected += _ => _aiLabel.Modulate = new Color(1, 1, 1, _mode.Selected == 1 ? 1f : 0.4f);
        form.AddRow(I18n.T(Text.Levels.Mode), _mode);

        _aiLabel = new Label { Text = I18n.T(Text.Levels.AiDifficulty) };
        _aiLabel.AddThemeColorOverride("font_color", Palette.Active.TextDim);
        _ai = new OptionButton();
        _ai.AddItem(I18n.T(Text.Levels.Easy));
        _ai.AddItem(I18n.T(Text.Levels.Medium));
        _ai.AddItem(I18n.T(Text.Levels.Hard));
        _ai.Select(1);
        form.AddChild(_aiLabel);
        form.AddChild(_ai);

        var start = new Button { Text = I18n.T(Text.Levels.Start), CustomMinimumSize = new Vector2(0, 40) };
        start.AddThemeColorOverride("font_color", Palette.Active.TextPrimary);
        start.Pressed += OnStart;
        box.AddChild(start);
    }

    private void OnStart()
    {
        int size = (int)_size.Value;
        if (size < GameConfig.MinBoardSize || size % 2 == 0)
        {
            ToastRequested?.Invoke(I18n.T(Text.Levels.SizeInvalid), true);
            return;
        }
        var level = new LevelConfig
        {
            Id = "custom",
            Name = I18n.T(Text.Levels.Custom),
            BoardSize = size,
            Layers = (int)_layers.Value,
            WinLength = (int)_winLength.Value,
            Rules = _rules.Selected == 1 ? RuleSet.Swap2 : RuleSet.Gomoku,
        };
        var difficulty = _ai.Selected switch
        {
            0 => AIDifficulty.Easy,
            2 => AIDifficulty.Hard,
            _ => AIDifficulty.Medium,
        };
        StartRequested?.Invoke(level, _mode.Selected == 1, difficulty);
    }

    private void Prefill(int index)
    {
        var preset = LevelCatalog.Presets[index];
        _size.Value = preset.BoardSize;
        _layers.Value = preset.Layers;
        _winLength.Value = preset.WinLength;
        _rules.Selected = preset.Rules == RuleSet.Swap2 ? 1 : 0;
    }

    private StyleBoxFlat MakePanelBox() => new()
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
        ContentMarginLeft = 24,
        ContentMarginRight = 24,
        ContentMarginTop = 18,
        ContentMarginBottom = 18,
    };

    private Button MakeButton(string text, Action onPressed)
    {
        var button = new Button { Text = text };
        button.AddThemeColorOverride("font_color", Palette.Active.TextPrimary);
        button.Pressed += () => onPressed();
        return button;
    }
}

/// <summary>GridContainer 行辅助：左标签右控件。</summary>
public static class SandboxFormExtensions
{
    public static void AddRow(this GridContainer grid, string labelText, Control editor)
    {
        var label = new Label { Text = labelText, SizeFlagsHorizontal = Control.SizeFlags.Expand };
        label.AddThemeColorOverride("font_color", Palette.Active.TextDim);
        grid.AddChild(label);
        editor.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        grid.AddChild(editor);
    }
}
