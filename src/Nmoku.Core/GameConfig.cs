namespace Nmoku.Core;

/// <summary>全局常量表（01-core 附录 B + migration-plan §4，单一事实源）。</summary>
public static class GameConfig
{
    public const int DefaultBoardSize = 13;
    public const int DefaultLayerCount = 6;
    public const int WinLength = 5;
    public const double DefaultLayerSpacing = 3.5;

    public const int SaveSlotCount = 6;
    public const int QuickSaveIndex = 5;

    /// <summary>层间距调节范围（README：Z/C 压缩拉大，X 复位）。</summary>
    public const double MinLayerSpacing = 2.0;
    public const double MaxLayerSpacing = 6.0;

    /// <summary>辅助线检测上限（README 特性节"严格限制 4 格检测范围"）。</summary>
    public const int HelperLineMaxCells = 4;

    /// <summary>沙盒自定义约束（00-domain §2.2/§2.8）。</summary>
    public const int MinBoardSize = 5;   // 且必须为奇数
    public const int MinWinLength = 4;
    public const int MaxWinLength = 9;

    public const string SaveVersion = "2.0.0";

    // ---- 相机（03-camera 附录 A / migration-plan §4，角度均为度）----
    public const double MinFov = 30.0;             // 扁盘
    public const double MaxFov = 60.0;             // 高盘
    public const double MinPitch = 35.0;           // 高盘（见层）
    public const double MaxPitch = 85.0;           // 扁盘（近顶视）
    public const double AspectSensitivity = 2.5;
    public const double SafetyMargin = 1.3;
    public const double ZoomSpeed = 0.15;
    public const double GhostOpacityMin = 0.25;
    public const double MinVisualScale = 0.8;
    public const double MaxVisualScale = 1.2;
    public const int BaseBoardSize = 13;           // 视觉归一化基准盘
    public const int BaseLayers = 6;
    public const double BaseSpacing = 3.5;
    public const double DefaultYaw = 45.0;         // 对角视角（03-camera §10.2）
    public const double MinDistanceFactor = 0.3;   // 距离上下限 base×0.3 / ×3.0（§6.1）
    public const double MaxDistanceFactor = 3.0;
    public const double CameraEpsilon = 1e-6;      // 缓存脏比较（§8.1）

    // ---- 自由相机交互（人类决策 2026-09-19：超越源项目的无级旋转 + 视角平移 + 平滑阻尼）----
    public const double CameraSmoothRate = 12.0;   // Advance 指数阻尼速率（1/秒），越大越"跟手"
    public const double PanRangeFactor = 1.0;      // 平移限幅：目标点偏移 ≤ 此系数 × 包围盒对角线
}
