namespace Nmoku.Core.Camera;

/// <summary>
/// 逻辑格 (x,y,z) → 世界坐标映射（B8：源实际朝向——行=世界Y 竖直、层=世界Z 纵深，人类确认 2026-09-19）。
/// 镜像/手性问题收敛在此单点（migration-plan §3.2、R2），若标定发现镜像只需翻转本类，禁止散落到渲染代码。
/// </summary>
public static class CoordMapper
{
    /// <summary>x=列→X 不变，y=行→Y（竖直），z=层→Z（纵深，乘 layerSpacing）。</summary>
    public static WorldPoint LogicalToWorld(int x, int y, int z, double layerSpacing) =>
        new(x, y, z * layerSpacing);

    /// <summary>逆映射：层深 worldZ/layerSpacing 四舍五入回层号。</summary>
    public static (int X, int Y, int Z) WorldToLogical(WorldPoint world, double layerSpacing) =>
        ((int)world.X, (int)world.Y, (int)Math.Round(world.Z / layerSpacing));
}
