namespace Nmoku.Core.Camera;

/// <summary>
/// 03-camera §9 八步管道纯数学（无状态、无 Godot 依赖）。常量全部来自 GameConfig（migration-plan §4）。
/// 与源 LeftPanel.ts 的差异（对角线项、水平 FOV、lookAt 中心）见 migration-plan §10 B7：以文档为准。
/// </summary>
public static class CameraMath
{
    /// <summary>STEP 1 包围盒对角线 sqrt(w²+w²+h²)（§2.5，默认盘 ≈24.38）。</summary>
    public static double ComputeDiagonal(int boardSize, int layers, double layerSpacing)
    {
        double w = boardSize - 1;
        double h = (layers - 1) * layerSpacing;
        return Math.Sqrt(w * w + w * w + h * h);
    }

    /// <summary>STEP 1 包围盒几何中心（§2.4），即公式态观察目标。</summary>
    public static WorldPoint ComputeCenter(int boardSize, int layers, double layerSpacing) =>
        new((boardSize - 1) / 2.0, (layers - 1) * layerSpacing / 2.0, (boardSize - 1) / 2.0);

    /// <summary>STEP 2 归一化纵横比（§3.1：高/宽 × 敏感度，clamp 0..1）。扁盘=0，默认盘=1。</summary>
    public static double ComputeNormalizedAspect(int boardSize, int layers, double layerSpacing)
    {
        double w = boardSize - 1;
        double h = (layers - 1) * layerSpacing;
        double raw = h / Math.Max(w, 1);
        return Math.Clamp(raw * GameConfig.AspectSensitivity, 0, 1);
    }

    /// <summary>STEP 3a 视场角（度）lerp(30, 60, n)（§3.2）。</summary>
    public static double ComputeFov(double normalizedAspect) =>
        GameConfig.MinFov + (GameConfig.MaxFov - GameConfig.MinFov) * normalizedAspect;

    /// <summary>STEP 3b 俯仰角（度）lerp(35, 85, 1-n)（§3.3）。扁盘近顶视，高盘见层。</summary>
    public static double ComputePitch(double normalizedAspect) =>
        GameConfig.MinPitch + (GameConfig.MaxPitch - GameConfig.MinPitch) * (1 - normalizedAspect);

    /// <summary>STEP 4 视觉归一化：对角线 clamp 到基准盘 0.8–1.2 倍（§4.1）。</summary>
    public static double ComputeTargetDiagonal(double diagonal)
    {
        double baseDiagonal = ComputeDiagonal(GameConfig.BaseBoardSize, GameConfig.BaseLayers, GameConfig.BaseSpacing);
        return Math.Clamp(diagonal, baseDiagonal * GameConfig.MinVisualScale, baseDiagonal * GameConfig.MaxVisualScale);
    }

    /// <summary>STEP 5 最优距离（§4.2）：受垂直/水平 FOV 较小者限制，×安全边距 1.3。</summary>
    public static double ComputeDistance(double targetDiagonal, double fovDeg, double aspect)
    {
        double fovV = ToRadians(fovDeg);
        double fovH = 2 * Math.Atan(Math.Tan(fovV / 2) * aspect);
        double fovLimit = Math.Min(fovV, fovH);
        return targetDiagonal / 2 / Math.Sin(fovLimit / 2) * GameConfig.SafetyMargin;
    }

    /// <summary>缩放距离上下限（§6.1）：base 距离的 0.3 / 3.0 倍。</summary>
    public static (double Min, double Max) ComputeDistanceLimits(double targetDiagonal, double fovDeg, double aspect)
    {
        double baseDist = ComputeDistance(targetDiagonal, fovDeg, aspect);
        return (baseDist * GameConfig.MinDistanceFactor, baseDist * GameConfig.MaxDistanceFactor);
    }

    /// <summary>STEP 6 球坐标→笛卡尔（Y-up，§5.2）：yaw 绕 Y，pitch 高出 XZ 平面。</summary>
    public static WorldPoint ComputePosition(WorldPoint center, double distance, double pitchDeg, double yawDeg)
    {
        double r = distance * Math.Cos(ToRadians(pitchDeg));
        return new(
            center.X + r * Math.Cos(ToRadians(yawDeg)),
            center.Y + distance * Math.Sin(ToRadians(pitchDeg)),
            center.Z + r * Math.Sin(ToRadians(yawDeg)));
    }

    /// <summary>球坐标反解（自由交互后从位置恢复 scalars，保持"标量→位置"单一派生方向）。</summary>
    public static (double PitchDeg, double YawDeg, double Distance) DecomposePosition(
        WorldPoint position, WorldPoint center)
    {
        var offset = position - center;
        double distance = offset.Length();
        double pitch = Math.Asin(Math.Clamp(distance < GameConfig.CameraEpsilon ? 0 : offset.Y / distance, -1, 1));
        double yaw = Math.Atan2(offset.Z, offset.X);
        return (FromRadians(pitch), FromRadians(yaw), distance);
    }

    /// <summary>STEP 7 裁剪面（§7）：near 不低于 0.1，far 为距离 10 倍。</summary>
    public static (double Near, double Far) ComputeClipPlanes(double distance) =>
        (Math.Max(0.1, distance * 0.01), distance * 10.0);

    /// <summary>连续透明度（§8.2）：距盘心 Y 越远越透明，最低 GHOST_OPACITY_MIN；单层盘恒 1。</summary>
    public static double ComputeOpacity(double worldY, double centerY, double maxDelta)
    {
        if (maxDelta < GameConfig.CameraEpsilon)
        {
            return 1.0;
        }
        double normalized = Math.Abs(worldY - centerY) / maxDelta;
        return GameConfig.GhostOpacityMin + (1.0 - GameConfig.GhostOpacityMin) * (1 - normalized);
    }

    /// <summary>透明度便捷重载：按层数/层间距自行求中心与最大偏移。</summary>
    public static double ComputeOpacity(double worldY, int layers, double layerSpacing)
    {
        double center = (layers - 1) * layerSpacing / 2.0;
        return ComputeOpacity(worldY, center, center);
    }

    /// <summary>平移轴（屏幕对齐的地面系）：right 与 groundForward 由 yaw 决定（拖拽平移用）。</summary>
    public static (WorldPoint Right, WorldPoint GroundForward) ComputePanAxes(double yawDeg)
    {
        double yawRad = ToRadians(yawDeg);
        // 相机在地面的注视方向 = -(cos yaw, 0, sin yaw)；right = forward × up
        var groundForward = new WorldPoint(-Math.Cos(yawRad), 0, -Math.Sin(yawRad));
        var right = new WorldPoint(Math.Sin(yawRad), 0, -Math.Cos(yawRad));
        return (right, groundForward);
    }

    /// <summary>STEP 1–7 全量：一次算出公式态（STEP 8 应用在 Game 侧）。</summary>
    public static CameraState ComputeState(int boardSize, int layers, double layerSpacing,
        double aspect, double yawDeg)
    {
        var center = ComputeCenter(boardSize, layers, layerSpacing);
        double diagonal = ComputeDiagonal(boardSize, layers, layerSpacing);
        double targetDiagonal = ComputeTargetDiagonal(diagonal);
        double normalizedAspect = ComputeNormalizedAspect(boardSize, layers, layerSpacing);
        double fov = ComputeFov(normalizedAspect);
        double pitch = ComputePitch(normalizedAspect);
        double distance = ComputeDistance(targetDiagonal, fov, aspect);
        var (near, far) = ComputeClipPlanes(distance);
        var position = ComputePosition(center, distance, pitch, yawDeg);
        return new CameraState
        {
            BoardSize = boardSize,
            Layers = layers,
            LayerSpacing = layerSpacing,
            AspectRatio = aspect,
            Center = center,
            Diagonal = diagonal,
            TargetDiagonal = targetDiagonal,
            NormalizedAspect = normalizedAspect,
            Fov = fov,
            Pitch = pitch,
            Distance = distance,
            Position = position,
            NearPlane = near,
            FarPlane = far,
            Yaw = yawDeg,
        };
    }

    public static double ToRadians(double degrees) => degrees * Math.PI / 180;
    public static double FromRadians(double radians) => radians * 180 / Math.PI;
}
