namespace Nmoku.Core.Camera;

/// <summary>
/// 逻辑格 (x,y,z) → 世界坐标映射。
/// B8 朝向：行=世界Y 竖直、层沿世界Z 排布（源实际，人类确认 2026-09-19）。
/// B10（人类指示 2026-09-19）：整盘绕 Y 轴旋转 180°——层 0 为最靠近摄像机的第一面、
/// 列 0 落在摄像机侧，使 3D 与 2D 面板左右一致、A/D 换层方向符合直觉（第 1 面起由近及远）。
/// 镜像/手性问题收敛在此单点（migration-plan §3.2、R2），禁止散落到渲染代码。
/// </summary>
public static class CoordMapper
{
    /// <summary>x 列与 z 层绕 Y 轴 180° 翻转，y 行不变（竖直）。</summary>
    public static WorldPoint LogicalToWorld(int x, int y, int z, double layerSpacing, int boardSize, int layers) =>
        new(boardSize - 1 - x, y, (layers - 1 - z) * layerSpacing);

    /// <summary>逆映射：层深 worldZ/layerSpacing 四舍五入回层号（同样翻转）。</summary>
    public static (int X, int Y, int Z) WorldToLogical(WorldPoint world, double layerSpacing, int boardSize, int layers) =>
        (boardSize - 1 - (int)world.X, (int)world.Y, layers - 1 - (int)Math.Round(world.Z / layerSpacing));
}
