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
}
