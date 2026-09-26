using Godot;
using Nmoku.Core;
using Nmoku.Game.Theme;

namespace Nmoku.Game.Shell;

/// <summary>
/// 键帽控件（F1 快捷键页，对齐源 keyboard-sim 视觉）：圆角帽体 + 上浅下深硬阴影（源 0 4px 0）
/// + 作用域单色填充（不叠层，人类拍板 2026-09-25）；同键跨层的少量例外用底部小字分层提示
/// （如 S：主层对局缩放，下层"Ctrl+S · 保存"）。mini 模式用于说明列表键帽小样（无阴影、自定宽）。
/// 浅色帽体加描边定边（源 base 帽 #E8F4F5 与页面底同色的处理方式）。
/// </summary>
public sealed partial class KeyCap : Control
{
    private const float FontSizeFull = 12;
    private const float FontSizeNote = 7;
    private const float FontSizeMini = 9;
    private const int CornerRadius = 6;
    private const float ShadowOffset = 4f;

    private readonly string _label;
    private readonly string? _sub;
    private readonly KeycapScope _scope;
    private readonly bool _idle;
    private readonly bool _mini;

    /// <summary>mini 模式自定宽（修饰键/功能键名比单字符宽）。</summary>
    public KeyCap(string label, KeycapScope scope, bool idle = false, bool mini = false, string? sub = null)
    {
        _label = label;
        _scope = scope;
        _idle = idle;
        _mini = mini;
        _sub = sub;
        MouseFilter = MouseFilterEnum.Ignore;
        if (mini)
        {
            float w = label.Length switch
            {
                1 => 18,
                <= 2 => 26,
                <= 5 => 40,
                _ => 50,
            };
            CustomMinimumSize = new Vector2(w, 16);
            SizeFlagsVertical = SizeFlags.ShrinkCenter; // 网格单元会纵向拉伸 Control，键帽必须定高
        }
    }

    private Color Face => _idle ? Palette.Active.KeycapIdle : FaceOf(_scope);

    /// <summary>作用域帽体色（图例色板等外部共用）。</summary>
    public static Color FaceOf(KeycapScope scope) => scope switch
    {
        KeycapScope.Game => Palette.Active.KeycapGame,
        KeycapScope.Replay => Palette.Active.KeycapReplay,
        KeycapScope.PauseMenu => Palette.Active.KeycapPause,
        KeycapScope.Ctrl => Palette.Active.KeycapCtrl,
        KeycapScope.Shift => Palette.Active.KeycapShift,
        _ => Palette.Active.KeycapGlobal,
    };

    /// <summary>浅色帽体描边用文字反色（色板共用）。</summary>
    public static Color TextOnOf(Color face) => TextOn(face);

    private static Color TextOn(Color face) => Luminance(face) > 0.6f
        ? new Color(0.102f, 0.180f, 0.227f)   // 浅帽深字（源 --text-on-base #1A2E3A）
        : new Color(0.95f, 0.96f, 0.96f);     // 深帽浅字（源 --text-on-dark）

    /// <summary>相对亮度（Rec.709）；GodotSharp 的 Color 未暴露 GetLuminance，此处手算。</summary>
    private static float Luminance(Color c) => 0.2126f * c.R + 0.7152f * c.G + 0.0722f * c.B;

    /// <summary>亮度判定外部共用（色板描边等同款逻辑）。</summary>
    public static float LuminanceOf(Color c) => Luminance(c);

    public override void _Draw()
    {
        var face = Face;
        var textColor = TextOn(face);
        var rect = new Rect2(Vector2.Zero, Size);

        if (!_mini && !_idle)
        {
            // 硬阴影（源 --kbd-shadow-3d 的 0 4px 0 分量）
            var shadow = new StyleBoxFlat
            {
                BgColor = Palette.Active.KeycapShadow,
                CornerRadiusTopLeft = CornerRadius,
                CornerRadiusTopRight = CornerRadius,
                CornerRadiusBottomLeft = CornerRadius,
                CornerRadiusBottomRight = CornerRadius,
            };
            DrawStyleBox(shadow, new Rect2(new Vector2(0, ShadowOffset), Size));
        }

        var faceBox = new StyleBoxFlat
        {
            BgColor = face,
            CornerRadiusTopLeft = CornerRadius,
            CornerRadiusTopRight = CornerRadius,
            CornerRadiusBottomLeft = CornerRadius,
            CornerRadiusBottomRight = CornerRadius,
        };
        if (Luminance(face) > 0.6f)
        {
            // 浅色帽体描边定边（源 base 帽 border #2C4A4E）
            faceBox.BorderColor = new Color(TextOn(face), 0.8f);
            faceBox.BorderWidthBottom = 1;
            faceBox.BorderWidthTop = 1;
            faceBox.BorderWidthLeft = 1;
            faceBox.BorderWidthRight = 1;
        }
        DrawStyleBox(faceBox, rect);

        var font = GetThemeDefaultFont();
        if (_mini)
        {
            DrawString(font, new Vector2(0, Size.Y * 0.5f + FontSizeMini * 0.36f), _label,
                HorizontalAlignment.Center, Size.X, (int)FontSizeMini, textColor);
            return;
        }
        if (_sub == null)
        {
            DrawString(font, new Vector2(0, Size.Y * 0.5f + FontSizeFull * 0.36f), _label,
                HorizontalAlignment.Center, Size.X, (int)FontSizeFull, textColor);
            return;
        }
        // 分层页版式：上部 ≤3 字注记，下部键名
        DrawString(font, new Vector2(0, Size.Y * 0.34f), _sub,
            HorizontalAlignment.Center, Size.X, (int)FontSizeNote, new Color(textColor, 0.9f));
        DrawString(font, new Vector2(0, Size.Y * 0.72f), _label,
            HorizontalAlignment.Center, Size.X, (int)FontSizeFull, textColor);
    }
}
