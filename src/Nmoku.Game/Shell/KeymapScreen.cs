using Godot;
using Nmoku.Core;
using Nmoku.Game.Theme;

namespace Nmoku.Game.Shell;

/// <summary>
/// 快捷键帮助浮层（F1，L2 浮层，任意状态可用）：标题 + <see cref="KeymapContent"/>（与引导屏共用）。
/// 内容完全由 Core KeymapTable 派生，与 ShortcutRouter 门控同源，杜绝漂移。Esc 或 F1 关闭（逐层冒泡由 Main 处理）。
/// 面板随窗口自适应（全游戏 UI ×2.5 后设计基准约 768×432，固定尺寸会溢出）。
/// </summary>
public sealed partial class KeymapScreen : Control
{
    public event Action? CloseRequested;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop; // 不透明背板由 SceneRouter 提供，此处拦截浮层下方交互

        var margin = new MarginContainer();
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 24);
        margin.AddThemeConstantOverride("margin_right", 24);
        margin.AddThemeConstantOverride("margin_top", 16);
        margin.AddThemeConstantOverride("margin_bottom", 16);
        AddChild(margin);

        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = Palette.Active.PanelBg,
            BorderColor = Palette.Active.PanelBorder,
            CornerRadiusTopLeft = 10,
            CornerRadiusTopRight = 10,
            CornerRadiusBottomLeft = 10,
            CornerRadiusBottomRight = 10,
            ContentMarginLeft = 20,
            ContentMarginRight = 20,
            ContentMarginTop = 12,
            ContentMarginBottom = 12,
        });
        margin.AddChild(panel);

        var outer = new VBoxContainer();
        outer.AddThemeConstantOverride("separation", 8);
        panel.AddChild(outer);

        var header = new HBoxContainer();
        outer.AddChild(header);
        var title = new Label { Text = "快捷键说明（F1）", SizeFlagsHorizontal = SizeFlags.Expand };
        title.AddThemeFontSizeOverride("font_size", 22);
        title.AddThemeColorOverride("font_color", Palette.Active.TextPrimary);
        header.AddChild(title);
        var close = new Button { Text = Text.Overlay.Cancel + " (Esc)" };
        close.Pressed += () => CloseRequested?.Invoke();
        header.AddChild(close);

        outer.AddChild(new KeymapContent());
    }
}
