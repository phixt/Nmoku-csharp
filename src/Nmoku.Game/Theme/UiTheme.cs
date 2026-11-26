using Godot;

namespace Nmoku.Game.Theme;

/// <summary>
/// 全局统一 UI 主题工厂：以霞鹜文楷（LXGWWenKaiLite）为全应用默认字体，
/// 按当前 <see cref="Palette"/> 统一构造 Button / OptionButton / PopupMenu / LineEdit / HSlider / Panel 等样式，
/// 彻底替换 Godot 默认灰暗控件皮肤并穿透所有 CanvasLayer 与弹窗。
/// </summary>
public static class UiTheme
{
    private static FontFile? _defaultFont;
    private static SystemFont? _keycapFont;

    /// <summary>全局默认字体：霞鹜文楷 Lite（与开始界面统一）。</summary>
    public static FontFile DefaultFont =>
        _defaultFont ??= GD.Load<FontFile>("res://assets/fonts/LXGWWenKaiLite-Regular.ttf");

    /// <summary>仅用于物理键帽字符（Esc/Ctrl/F1/WASD）等必要场景的无衬线黑体。</summary>
    public static Font KeycapFont =>
        _keycapFont ??= new SystemFont
        {
            FontNames = ["Segoe UI", "Microsoft YaHei UI", "PingFang SC", "Noto Sans CJK SC", "sans-serif"],
        };

    public static Godot.Theme Build(Palette pal)
    {
        bool isDark = pal.Background.Luminance < 0.4f;
        var theme = new Godot.Theme
        {
            DefaultFont = DefaultFont,
            DefaultFontSize = 15,
        };

        // 1) Button / OptionButton：清透有层次的非灰扑扑按钮
        Color btnBorder = isDark
            ? new Color(pal.PanelBorder.R, pal.PanelBorder.G, pal.PanelBorder.B, 0.72f)
            : new Color(pal.PanelBorder.R, pal.PanelBorder.G, pal.PanelBorder.B, 0.85f);
        Color btnDisabledBg = isDark
            ? pal.ButtonBg.Darkened(0.28f)
            : pal.ButtonBg.Darkened(0.05f);
        Color btnDisabledText = new(pal.TextDim.R, pal.TextDim.G, pal.TextDim.B, 0.55f);

        var btnNormal = MakeBox(pal.ButtonBg, btnBorder, radius: 6, border: 1, padH: 12, padV: 5);
        var btnHover = MakeBox(pal.ButtonHover, pal.Accent, radius: 6, border: 1, padH: 12, padV: 5);
        var btnPressed = MakeBox(
            isDark ? pal.ButtonHover.Darkened(0.14f) : pal.ButtonHover.Darkened(0.06f),
            pal.Accent, radius: 6, border: 1, padH: 12, padV: 5);
        var btnDisabled = MakeBox(btnDisabledBg, new Color(btnBorder, 0.35f), radius: 6, border: 1, padH: 12, padV: 5);
        var btnFocus = MakeBox(Colors.Transparent, new Color(pal.Accent, 0.65f), radius: 6, border: 1, padH: 12, padV: 5);

        foreach (string type in new[] { "Button", "OptionButton" })
        {
            theme.SetStylebox("normal", type, btnNormal);
            theme.SetStylebox("hover", type, btnHover);
            theme.SetStylebox("pressed", type, btnPressed);
            theme.SetStylebox("disabled", type, btnDisabled);
            theme.SetStylebox("focus", type, btnFocus);
            theme.SetColor("font_color", type, pal.TextPrimary);
            theme.SetColor("font_hover_color", type, pal.TextPrimary);
            theme.SetColor("font_pressed_color", type, pal.TextPrimary);
            theme.SetColor("font_focus_color", type, pal.TextPrimary);
            theme.SetColor("font_disabled_color", type, btnDisabledText);
            theme.SetFont("font", type, DefaultFont);
        }

        // 2) PopupMenu（OptionButton 下拉列表）
        var popupPanel = MakeBox(pal.PanelBg, pal.PanelBorder, radius: 6, border: 1, padH: 6, padV: 6);
        var popupHover = MakeBox(pal.ButtonHover, pal.Accent, radius: 4, border: 1, padH: 8, padV: 4);
        theme.SetStylebox("panel", "PopupMenu", popupPanel);
        theme.SetStylebox("hover", "PopupMenu", popupHover);
        theme.SetColor("font_color", "PopupMenu", pal.TextPrimary);
        theme.SetColor("font_hover_color", "PopupMenu", pal.TextPrimary);
        theme.SetColor("font_disabled_color", "PopupMenu", btnDisabledText);
        theme.SetFont("font", "PopupMenu", DefaultFont);

        // 3) LineEdit（SpinBox 数值框 / 输入弹窗）
        Color inputBg = isDark ? pal.Background.Lightened(0.06f) : Colors.White;
        var inputNormal = MakeBox(inputBg, btnBorder, radius: 5, border: 1, padH: 8, padV: 4);
        var inputFocus = MakeBox(inputBg, pal.Accent, radius: 5, border: 1, padH: 8, padV: 4);
        theme.SetStylebox("normal", "LineEdit", inputNormal);
        theme.SetStylebox("focus", "LineEdit", inputFocus);
        theme.SetStylebox("read_only", "LineEdit", btnDisabled);
        theme.SetColor("font_color", "LineEdit", pal.TextPrimary);
        theme.SetColor("font_placeholder_color", "LineEdit", pal.TextDim);
        theme.SetColor("caret_color", "LineEdit", pal.Accent);
        theme.SetColor("selection_color", "LineEdit", new Color(pal.Accent, 0.35f));
        theme.SetFont("font", "LineEdit", DefaultFont);

        // 4) HSlider（设置页音量 / 复盘进度条）
        var sliderTrack = MakeBox(
            isDark ? new Color(0.16f, 0.21f, 0.30f) : new Color(0.80f, 0.88f, 0.90f),
            Colors.Transparent, radius: 3, border: 0, padH: 0, padV: 3);
        var sliderFill = MakeBox(pal.Accent, Colors.Transparent, radius: 3, border: 0, padH: 0, padV: 3);
        var sliderFillHl = MakeBox(pal.Accent.Lightened(0.12f), Colors.Transparent, radius: 3, border: 0, padH: 0, padV: 3);
        theme.SetStylebox("slider", "HSlider", sliderTrack);
        theme.SetStylebox("grabber_area", "HSlider", sliderFill);
        theme.SetStylebox("grabber_area_highlight", "HSlider", sliderFillHl);

        // 5) Label / AcceptDialog / ConfirmationDialog
        theme.SetColor("font_color", "Label", pal.TextPrimary);
        theme.SetFont("font", "Label", DefaultFont);
        var dialogPanel = MakeBox(pal.PanelBg, pal.PanelBorder, radius: 8, border: 1, padH: 18, padV: 14);
        theme.SetStylebox("panel", "AcceptDialog", dialogPanel);
        theme.SetStylebox("panel", "ConfirmationDialog", dialogPanel);
        theme.SetColor("title_color", "Window", pal.TextPrimary);
        theme.SetFont("title_font", "Window", DefaultFont);

        return theme;
    }

    private static StyleBoxFlat MakeBox(Color bg, Color borderColor, int radius, int border, int padH, int padV) => new()
    {
        BgColor = bg,
        BorderColor = borderColor,
        BorderWidthLeft = border,
        BorderWidthRight = border,
        BorderWidthTop = border,
        BorderWidthBottom = border,
        CornerRadiusTopLeft = radius,
        CornerRadiusTopRight = radius,
        CornerRadiusBottomLeft = radius,
        CornerRadiusBottomRight = radius,
        ContentMarginLeft = padH,
        ContentMarginRight = padH,
        ContentMarginTop = padV,
        ContentMarginBottom = padV,
    };
}
