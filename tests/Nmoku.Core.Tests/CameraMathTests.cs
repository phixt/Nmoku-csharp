using Nmoku.Core.Camera;

namespace Nmoku.Core.Tests;

/// <summary>03-camera §2–§8 纯函数；文档自带数值（§2.5 默认盘对角线 ≈24.38）作锚点。</summary>
public class CameraMathTests
{
    // sqrt(12²+12²+17.5²) = sqrt(594.25)，由分量独立复算而非抄实现的返回值
    private static readonly double DefaultDiagonal = Math.Sqrt(12 * 12 + 12 * 12 + 17.5 * 17.5);

    // ---- STEP 1 几何 ----

    [Fact]
    public void ComputeDiagonal_DefaultBoard_MatchesDocValue()
    {
        Assert.Equal(DefaultDiagonal, CameraMath.ComputeDiagonal(13, 6, 3.5), 9);
    }

    [Fact]
    public void ComputeDiagonal_FlatSingleLayer_OmitsHeight()
    {
        Assert.Equal(Math.Sqrt(128), CameraMath.ComputeDiagonal(9, 1, 3.5), 9);
    }

    [Fact]
    public void ComputeCenter_IsBoundingBoxMidpoint()
    {
        var center = CameraMath.ComputeCenter(13, 6, 3.5);
        Assert.Equal((6.0, 8.75, 6.0), (center.X, center.Y, center.Z));
    }

    // ---- STEP 2 归一化纵横比 ----

    [Fact]
    public void ComputeNormalizedAspect_FlatBoard_ReturnsZero()
    {
        Assert.Equal(0, CameraMath.ComputeNormalizedAspect(13, 1, 3.5));
    }

    [Fact]
    public void ComputeNormalizedAspect_TallDefaultBoard_ClampsToOne()
    {
        // §3.1 示例：17.5/12×2.5=3.65 → clamp 1
        Assert.Equal(1, CameraMath.ComputeNormalizedAspect(13, 6, 3.5));
    }

    [Fact]
    public void ComputeNormalizedAspect_TwoLayerBoard_ReturnsRawRatio()
    {
        Assert.Equal(3.5 / 12 * 2.5, CameraMath.ComputeNormalizedAspect(13, 2, 3.5), 12);
    }

    // ---- STEP 3 FOV / Pitch ----

    [Theory]
    [InlineData(0.0, 30.0)]
    [InlineData(0.5, 45.0)]
    [InlineData(1.0, 60.0)]
    public void ComputeFov_LerpsMinToMax(double n, double expected)
    {
        Assert.Equal(expected, CameraMath.ComputeFov(n), 9);
    }

    [Theory]
    [InlineData(0.0, 85.0)]   // 扁盘近顶视
    [InlineData(0.5, 60.0)]
    [InlineData(1.0, 35.0)]   // 高盘压低看层
    public void ComputePitch_InvertsFovDirection(double n, double expected)
    {
        Assert.Equal(expected, CameraMath.ComputePitch(n), 9);
    }

    // ---- STEP 4 视觉归一化 ----

    [Fact]
    public void ComputeTargetDiagonal_SmallBoard_ClampsToBaseMinimum()
    {
        // 9×9×1 对角线 sqrt(128)≈11.31 < 基准×0.8 ≈19.50 → 抬到下限
        Assert.Equal(DefaultDiagonal * 0.8, CameraMath.ComputeTargetDiagonal(Math.Sqrt(128)), 9);
    }

    [Fact]
    public void ComputeTargetDiagonal_HugeBoard_ClampsToBaseMaximum()
    {
        Assert.Equal(DefaultDiagonal * 1.2, CameraMath.ComputeTargetDiagonal(DefaultDiagonal * 5), 9);
    }

    [Fact]
    public void ComputeTargetDiagonal_DefaultBoard_Unchanged()
    {
        Assert.Equal(DefaultDiagonal, CameraMath.ComputeTargetDiagonal(DefaultDiagonal), 9);
    }

    // ---- STEP 5 距离 ----

    [Fact]
    public void ComputeDistance_Landscape_LimitedByVerticalFov()
    {
        // aspect 16:9 > 1 时水平 FOV 恒更宽 → 限制者是垂直 fov：dist = targetDiag × 1.3
        double distance = CameraMath.ComputeDistance(DefaultDiagonal, 60, 16.0 / 9);
        Assert.Equal(DefaultDiagonal * 1.3, distance, 9);
    }

    [Fact]
    public void ComputeDistance_Portrait_LimitedByHorizontalFov()
    {
        double landscape = CameraMath.ComputeDistance(DefaultDiagonal, 60, 16.0 / 9);
        double portrait = CameraMath.ComputeDistance(DefaultDiagonal, 60, 0.5);
        Assert.True(portrait > landscape, "窄视口应由水平 FOV 限制，距离更远");

        // 独立复算：fovH = 2·atan(tan(30°)·0.5)
        double fovH = 2 * Math.Atan(Math.Tan(Math.PI / 6) * 0.5);
        double expected = DefaultDiagonal / 2 / Math.Sin(fovH / 2) * 1.3;
        Assert.Equal(expected, portrait, 9);
    }

    [Fact]
    public void ComputeDistanceLimits_AreFractionOfBaseDistance()
    {
        var (min, max) = CameraMath.ComputeDistanceLimits(DefaultDiagonal, 60, 16.0 / 9);
        double baseDist = DefaultDiagonal * 1.3;
        Assert.Equal(baseDist * 0.3, min, 9);
        Assert.Equal(baseDist * 3.0, max, 9);
    }

    // ---- STEP 6 位置 ----

    [Fact]
    public void ComputePosition_Pitch90_SitsStraightAbove()
    {
        var pos = CameraMath.ComputePosition(new WorldPoint(6, 8.75, 6), 10, 90, 45);
        Assert.Equal((6.0, 18.75, 6.0), (pos.X, pos.Y, pos.Z));
    }

    [Fact]
    public void ComputePosition_Yaw45_HasEqualXZOffsets()
    {
        var center = new WorldPoint(6, 8.75, 6);
        var pos = CameraMath.ComputePosition(center, 10, 0, 45);
        Assert.Equal(10 * Math.Cos(Math.PI / 4), pos.X - center.X, 9);
        Assert.Equal(pos.X - center.X, pos.Z - center.Z, 9);
    }

    [Fact]
    public void DecomposePosition_RoundTripsSpherical()
    {
        var center = new WorldPoint(6, 8.75, 6);
        var pos = CameraMath.ComputePosition(center, 25, 42, 137);
        var (pitch, yaw, distance) = CameraMath.DecomposePosition(pos, center);
        Assert.Equal(42, pitch, 6);
        Assert.Equal(137, yaw, 6);
        Assert.Equal(25, distance, 6);
    }

    // ---- STEP 7 裁剪面 ----

    [Fact]
    public void ComputeClipPlanes_ScaleWithDistance_AndFloorNearAt01()
    {
        var (near, far) = CameraMath.ComputeClipPlanes(DefaultDiagonal * 1.3);
        Assert.Equal(DefaultDiagonal * 1.3 * 0.01, near, 9);
        Assert.Equal(DefaultDiagonal * 1.3 * 10, far, 9);

        var (nearTiny, _) = CameraMath.ComputeClipPlanes(5);
        Assert.Equal(0.1, nearTiny);
    }

    // ---- §8.2 透明度 ----

    [Theory]
    [InlineData(8.75, 1.0)]     // 盘心层全实
    [InlineData(0.0, 0.25)]     // 底层最透
    [InlineData(17.5, 0.25)]    // 顶层最透
    [InlineData(4.375, 0.625)]  // 中点线性
    public void ComputeOpacity_FallsOffWithHeight(double worldY, double expected)
    {
        Assert.Equal(expected, CameraMath.ComputeOpacity(worldY, 6, 3.5), 9);
    }

    [Fact]
    public void ComputeOpacity_SingleLayer_AlwaysOpaque()
    {
        Assert.Equal(1.0, CameraMath.ComputeOpacity(0, 1, 3.5));
    }

    // ---- 平移轴 ----

    [Fact]
    public void ComputePanAxes_YawZero_LooksAlongMinusX()
    {
        var (right, forward) = CameraMath.ComputePanAxes(0);
        Assert.Equal((0.0, 0.0, -1.0), (right.X, right.Y, right.Z));       // 屏幕右 = -Z
        Assert.Equal((-1.0, 0.0, 0.0), (forward.X, forward.Y, forward.Z)); // 地面注视 = -X
    }

    [Fact]
    public void ComputePanAxes_Yaw45_AxesAreDiagonalUnitVectors()
    {
        var (right, forward) = CameraMath.ComputePanAxes(45);
        double s = Math.Sin(Math.PI / 4);
        Assert.Equal((s, 0.0, -s), (right.X, right.Y, right.Z));
        Assert.Equal((-s, 0.0, -s), (forward.X, forward.Y, forward.Z));
        Assert.Equal(1.0, right.Length(), 12);
        Assert.Equal(1.0, forward.Length(), 12);
    }

    // ---- 全量管道 ----

    [Fact]
    public void ComputeState_DefaultBoard_MatchesDocAnchors()
    {
        var state = CameraMath.ComputeState(13, 6, 3.5, 16.0 / 9, GameConfig.DefaultYaw);
        Assert.Equal(DefaultDiagonal, state.Diagonal, 9);
        Assert.Equal(60, state.Fov, 9);
        Assert.Equal(35, state.Pitch, 9);
        Assert.Equal(DefaultDiagonal * 1.3, state.Distance, 9);
        Assert.Equal((6.0, 8.75, 6.0), (state.Center.X, state.Center.Y, state.Center.Z));
        // yaw=45、pitch=35：X/Z 偏移相等且为正（对角视角）
        Assert.True(state.Position.X > state.Center.X);
        Assert.Equal(state.Position.X - state.Center.X, state.Position.Z - state.Center.Z, 9);
    }
}
