namespace Nmoku.Core.Camera;

/// <summary>
/// 世界坐标点（03-camera §2：X=列、Y=上/层高、Z=前进/行）。
/// Core 侧自有类型（架构红线：Godot.Vector3 只出现在 Game 渲染层）。
/// </summary>
public readonly record struct WorldPoint(double X, double Y, double Z)
{
    public static WorldPoint operator +(WorldPoint a, WorldPoint b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static WorldPoint operator -(WorldPoint a, WorldPoint b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static WorldPoint operator *(WorldPoint a, double s) => new(a.X * s, a.Y * s, a.Z * s);

    public double Length() => Math.Sqrt(X * X + Y * Y + Z * Z);
}
