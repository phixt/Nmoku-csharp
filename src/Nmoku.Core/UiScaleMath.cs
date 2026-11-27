namespace Nmoku.Core;

/// <summary>
/// UI 缩放体系纯数学（960×540 基准，零 Godot 依赖）：
/// <para>
/// · 基准逻辑面 = 960×540（16:9 黄金设计基准）；
/// · 公式：<c>csf = clamp((窗口物理高 / 540) × UI倍率, MinCsf, MaxCsf)</c>；
/// · 配合 Godot <c>ContentScaleSize = Zero</c>（expand 语义），窗口化/最大化/全屏/自由拖拽均平滑自适应，
///   高分屏（2K/3K/4K）自动等比放大而不再显小。
/// </para>
/// </summary>
public static class UiScaleMath
{
    public const float BaseLogicalWidth = 960f;
    public const float BaseLogicalHeight = 540f;

    public const float MinMultiplier = 0.75f;
    public const float MaxMultiplier = 1.5f;
    public const float DefaultMultiplier = 1.0f;

    public const float MinContentScaleFactor = 0.75f;
    public const float MaxContentScaleFactor = 6.0f;

    public const int MinWindowWidth = 640;
    public const int MinWindowHeight = 360;

    public const int DefaultWindowWidth = 1920;
    public const int DefaultWindowHeight = 1080;

    /// <summary>设置页可选 UI 缩放倍率档位。</summary>
    public static readonly float[] MultiplierPresets = [0.75f, 1.0f, 1.25f, 1.5f];

    /// <summary>设置页可选窗口化分辨率预设（仍保留窗口边缘自由拖拽）。</summary>
    public static readonly (int Width, int Height)[] ResolutionPresets =
    [
        (960, 540),
        (1280, 720),
        (1600, 900),
        (1920, 1080),
        (2560, 1440),
    ];

    /// <summary>规范化用户偏好的 UI 倍率（非法/NaN 回默认 1.0x，并钳制在允许区间）。</summary>
    public static float NormalizeMultiplier(float raw)
    {
        if (float.IsNaN(raw) || float.IsInfinity(raw) || raw <= 0f)
        {
            return DefaultMultiplier;
        }
        return Math.Clamp(raw, MinMultiplier, MaxMultiplier);
    }

    /// <summary>
    /// 按当前窗口物理高度与 UI 倍率计算 Godot <c>Window.ContentScaleFactor</c>。
    /// 非正高度（如 headless/最小化瞬态）回落至 1080p 基准计算。
    /// </summary>
    public static float ComputeContentScaleFactor(int windowHeightPx, float uiMultiplier)
    {
        float mult = NormalizeMultiplier(uiMultiplier);
        int effectiveH = windowHeightPx > 0 ? windowHeightPx : DefaultWindowHeight;
        float raw = (effectiveH / BaseLogicalHeight) * mult;
        return Math.Clamp(raw, MinContentScaleFactor, MaxContentScaleFactor);
    }

    /// <summary>计算给定物理窗口尺寸与缩放因子下的逻辑画布尺寸。</summary>
    public static (float Width, float Height) ComputeLogicalSize(int windowWidthPx, int windowHeightPx, float csf)
    {
        float safeCsf = Math.Clamp(csf, MinContentScaleFactor, MaxContentScaleFactor);
        return (Math.Max(1, windowWidthPx) / safeCsf, Math.Max(1, windowHeightPx) / safeCsf);
    }

    /// <summary>格式化分辨率键（如 "1920x1080"）。</summary>
    public static string FormatResolutionKey(int width, int height) => $"{width}x{height}";

    /// <summary>解析持久化分辨率键（如 "1920x1080"）；非法或低于最小窗口尺寸返回 null。</summary>
    public static (int Width, int Height)? TryParseResolutionKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }
        string[] parts = key.Trim().Split('x', 'X');
        if (parts.Length != 2
            || !int.TryParse(parts[0], out int w)
            || !int.TryParse(parts[1], out int h))
        {
            return null;
        }
        if (w < MinWindowWidth || h < MinWindowHeight || w > 7680 || h > 4320)
        {
            return null;
        }
        return (w, h);
    }
}
