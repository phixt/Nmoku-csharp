namespace Nmoku.Core;

/// <summary>
/// 关卡预设（01-core §7 LevelConfig 数据驱动；00-domain 约束：尺寸奇数≥5、连珠 4–9）。
/// tutorial/residual 为占位重设计（TODO 挂账），先用最小可玩形态；沙盒自定义经
/// GameStore.StartLevel 走同一装配路径，命令行参数也并入该入口（migration-plan M4）。
/// </summary>
public static class LevelCatalog
{
    public static readonly LevelConfig Classic = new()
    {
        Id = "classic",
        Name = Text.Levels.Classic,
        BoardSize = 13,
        Layers = 6,
        WinLength = 5,
        Rules = RuleSet.Gomoku,
    };

    public static readonly LevelConfig Tutorial = new()
    {
        Id = "tutorial",
        Name = Text.Levels.Tutorial,
        BoardSize = 9,
        Layers = 1,
        WinLength = 5,
        Rules = RuleSet.Gomoku,
    };

    public static readonly LevelConfig Residual = new()
    {
        Id = "residual",
        Name = Text.Levels.Residual,
        BoardSize = 13,
        Layers = 6,
        WinLength = 5,
        Rules = RuleSet.Gomoku,
        // 预置子：三手定性局（黑先，白 1 手后黑第 2 手——官方节奏 B1）
        InitialMoves =
        [
            new Move(6, 6, 0, PlayerColor.Black),
            new Move(6, 7, 0, PlayerColor.White),
            new Move(5, 6, 0, PlayerColor.Black),
        ],
    };

    public static IReadOnlyList<LevelConfig> Presets => [Classic, Tutorial, Residual];
}
