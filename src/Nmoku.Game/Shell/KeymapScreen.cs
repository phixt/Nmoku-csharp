using Godot;
using Nmoku.Core;
using Nmoku.Game.Theme;
using Nmoku.Game;

namespace Nmoku.Game.Shell;

/// <summary>
/// 快捷键帮助浮层（F1，L2 浮层，任意状态可用）：标题 + <see cref="KeymapContent"/>（与引导屏共用）。
/// 内容完全由 Core KeymapTable 派生，与 ShortcutRouter 门控同源，杜绝漂移。Esc 或 F1 关闭（逐层冒泡由 Main 处理）。
/// 面板随窗口自适应（全游戏 UI ×2.5 后设计基准约 768×432，固定尺寸会溢出）。
/// </summary>
public sealed partial class KeymapScreen : Control
{
    public event Action? CloseRequested;
    public KeymapContent Content { get; private set; } = null!;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop; // 不透明背板由 SceneRouter 提供，此处拦截浮层下方交互

        var margin = new MarginContainer();
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 10);
        margin.AddThemeConstantOverride("margin_right", 10);
        margin.AddThemeConstantOverride("margin_top", 8);
        margin.AddThemeConstantOverride("margin_bottom", 8);
        AddChild(margin);

        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = Palette.Active.PanelBg,
            BorderColor = Palette.Active.PanelBorder,
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            BorderWidthTop = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 10,
            CornerRadiusTopRight = 10,
            CornerRadiusBottomLeft = 10,
            CornerRadiusBottomRight = 10,
            ContentMarginLeft = 12,
            ContentMarginRight = 12,
            ContentMarginTop = 8,
            ContentMarginBottom = 8,
        });
        margin.AddChild(panel);

        var outer = new VBoxContainer();
        outer.AddThemeConstantOverride("separation", 4);
        panel.AddChild(outer);

        var header = new HBoxContainer();
        outer.AddChild(header);
        var title = new Label { Text = I18n.T(Text.Guide.KeymapTitle), SizeFlagsHorizontal = SizeFlags.Expand };
        title.AddThemeFontSizeOverride("font_size", 14);
        title.AddThemeColorOverride("font_color", Palette.Active.TextPrimary);
        header.AddChild(title);
        header.AddChild(KeymapContent.BuildLegend());
        var close = new Button { Text = I18n.T(Text.Overlay.Cancel) + " (Esc)" };
        close.Pressed += () => CloseRequested?.Invoke();
        header.AddChild(close);

        Content = new KeymapContent();
        outer.AddChild(Content);
    }
}
