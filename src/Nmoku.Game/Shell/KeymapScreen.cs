using Godot;
using Nmoku.Core;
using Nmoku.Game.Theme;

namespace Nmoku.Game.Shell;

/// <summary>
/// 快捷键帮助屏（F1，L2 浮层，任意状态可用）：内容完全由 Core KeymapTable 派生，
/// 与 ShortcutRouter 门控、docs 对照表同源，杜绝漂移。Esc 或 F1 关闭（逐层冒泡由 Main 处理）。
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
        margin.AddThemeConstantOverride("margin_left", 48);
        margin.AddThemeConstantOverride("margin_right", 48);
        margin.AddThemeConstantOverride("margin_top", 24);
        margin.AddThemeConstantOverride("margin_bottom", 24);
        AddChild(margin);

        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = Palette.Default.PanelBg,
            BorderColor = Palette.Default.PanelBorder,
            CornerRadiusTopLeft = 10,
            CornerRadiusTopRight = 10,
            CornerRadiusBottomLeft = 10,
            CornerRadiusBottomRight = 10,
            ContentMarginLeft = 28,
            ContentMarginRight = 28,
            ContentMarginTop = 18,
            ContentMarginBottom = 18,
        });
        margin.AddChild(panel);

        var outer = new VBoxContainer();
        outer.AddThemeConstantOverride("separation", 10);
        panel.AddChild(outer);

        var header = new HBoxContainer();
        outer.AddChild(header);
        var title = new Label { Text = "快捷键说明（F1）", SizeFlagsHorizontal = SizeFlags.Expand };
        title.AddThemeFontSizeOverride("font_size", 26);
        title.AddThemeColorOverride("font_color", Palette.Default.TextPrimary);
        header.AddChild(title);
        var close = new Button { Text = Text.Overlay.Cancel + " (Esc)" };
        close.Pressed += () => CloseRequested?.Invoke();
        header.AddChild(close);

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        outer.AddChild(scroll);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 6);
        box.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(box);

        foreach (var (category, rows) in KeymapTable.ByCategory())
        {
            var head = new Label { Text = category };
            head.AddThemeFontSizeOverride("font_size", 18);
            head.AddThemeColorOverride("font_color", Palette.Default.Accent);
            box.AddChild(head);

            foreach (var row in rows)
            {
                var line = new HBoxContainer();
                line.AddThemeConstantOverride("separation", 16);
                var keys = new Label
                {
                    Text = row.Keys,
                    CustomMinimumSize = new Vector2(240, 0),
                    SizeFlagsHorizontal = SizeFlags.ShrinkBegin,
                };
                keys.AddThemeColorOverride("font_color", Palette.Default.TextPrimary);
                var desc = new Label
                {
                    Text = row.Description,
                    SizeFlagsHorizontal = SizeFlags.ExpandFill,
                    AutowrapMode = TextServer.AutowrapMode.WordSmart,
                };
                desc.AddThemeColorOverride("font_color", Palette.Default.TextDim);
                line.AddChild(keys);
                line.AddChild(desc);
                box.AddChild(line);
            }
        }
    }
}
