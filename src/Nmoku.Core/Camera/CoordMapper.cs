namespace Nmoku.Core.Camera;

/// <summary>
/// 逻辑格 (x,y,z) → 世界坐标映射（03-camera §2）。镜像/手性问题收敛在此单点（migration-plan §3.2、R2），
/// 若 M2 标定发现镜像只需翻转本类，禁止散落到渲染代码。
/// </summary>
public static class CoordMapper
{
    /// <summary>x=列→X 不变，y=行→Z（前进），z=层→Y（上，乘 layerSpacing）。</summary>
    public static WorldPoint LogicalToWorld(int x, int y, int z, double layerSpacing) =>
        new(x, z * layerSpacing, y);

    /// <summary>逆映射（03-camera §2.2）：层高 worldY/layerSpacing 四舍五入回层号。</summary>
    public static (int X, int Y, int Z) WorldToLogical(WorldPoint world, double layerSpacing) =>
        ((int)world.X, (int)world.Z, (int)Math.Round(world.Y / layerSpacing));
}
