using Nmoku.Core.Camera;

namespace Nmoku.Core.Tests;

/// <summary>CameraRig：§8.1 增量缓存 + 自由交互（Orbit/Pan/Zoom/ZoomToAnchor）+ 阻尼平滑。</summary>
public class CameraRigTests
{
    private const double Aspect = 16.0 / 9;
    private static readonly double Diagonal = Math.Sqrt(12 * 12 + 12 * 12 + 17.5 * 17.5);
    private static readonly double FormulaDistance = Diagonal * 1.3; // 垂直 fov 限制（横屏）

    private static CameraRig NewRig() => new(13, 6, 3.5, Aspect);

    // ---- 构造与增量缓存 ----

    [Fact]
    public void Constructor_InitializesFormulaStateWithDefaultYaw()
    {
        var rig = NewRig();
        Assert.Equal(1, rig.RecomputeCount);
        Assert.Equal(60, rig.Target.Fov, 9);
        Assert.Equal(35, rig.Target.Pitch, 9);
        Assert.Equal(FormulaDistance, rig.Target.Distance, 9);
        Assert.Equal(45, rig.Target.Yaw, 9);
        Assert.Equal((6.0, 8.75, 6.0), (rig.Target.Center.X, rig.Target.Center.Y, rig.Target.Center.Z));
        Assert.Equal(rig.Target.Position, rig.Current.Position);
    }

    [Fact]
    public void Update_SameInputs_DoesNotRecompute()
    {
        var rig = NewRig();
        rig.Update(13, 6, 3.5, Aspect, 45);
        Assert.Equal(1, rig.RecomputeCount);
    }

    [Fact]
    public void Update_AspectChange_RecomputesDistanceAndKeepsYaw()
    {
        var rig = NewRig();
        rig.Update(13, 6, 3.5, 0.5, 45);

        Assert.Equal(2, rig.RecomputeCount);
        Assert.True(rig.Target.Distance > FormulaDistance, "窄视口水平 FOV 限制 → 更远");
        Assert.Equal(45, rig.Target.Yaw, 9);
    }

    [Fact]
    public void NeedsRecompute_UsesEpsilonForReals_ExactForInts()
    {
        var cache = NewRig().Target;
        Assert.False(CameraRig.NeedsRecompute(cache, 13, 6, 3.5, Aspect, 45));
        Assert.False(CameraRig.NeedsRecompute(cache, 13, 6, 3.5 + 1e-9, Aspect, 45));
        Assert.False(CameraRig.NeedsRecompute(cache, 13, 6, 3.5, Aspect, 45 + 1e-9));
        Assert.True(CameraRig.NeedsRecompute(cache, 13, 6, 3.5 + 1e-3, Aspect, 45));
        Assert.True(CameraRig.NeedsRecompute(cache, 13, 6, 3.5, Aspect, 45.5));
        Assert.True(CameraRig.NeedsRecompute(cache, 9, 6, 3.5, Aspect, 45));
        Assert.True(CameraRig.NeedsRecompute(cache, 13, 2, 3.5, Aspect, 45));
    }

    // ---- 无级旋转 ----

    [Fact]
    public void Orbit_YawAccumulatesWithoutLimit_AndDerivesPosition()
    {
        var rig = NewRig();
        rig.Orbit(90);
        Assert.Equal(135, rig.Target.Yaw, 9);
        // yaw=135：X 偏移为负、Z 偏移为正
        Assert.True(rig.Target.Position.X < rig.Target.Center.X);
        Assert.True(rig.Target.Position.Z > rig.Target.Center.Z);

        rig.Orbit(370);
        Assert.Equal(505, rig.Target.Yaw, 9);
    }

    [Fact]
    public void Orbit_PitchClampedToDocRange()
    {
        var rig = NewRig();
        rig.Orbit(0, 200);
        Assert.Equal(GameConfig.MaxPitch, rig.Target.Pitch, 9);

        rig.Orbit(0, -400);
        Assert.Equal(GameConfig.MinPitch, rig.Target.Pitch, 9);
    }

    [Fact]
    public void Orbit_UserPitch_SurvivesAspectRecompute()
    {
        var rig = NewRig();
        rig.Orbit(0, 10);
        rig.Update(13, 6, 3.5, 0.5, 45);
        Assert.Equal(45, rig.Target.Pitch, 9); // 35+10，未被公式覆盖
    }

    // ---- 视角平移 ----

    [Fact]
    public void Pan_MovesCenterAndPositionRigidly()
    {
        var rig = NewRig();
        var centerBefore = rig.Target.Center;
        var positionBefore = rig.Target.Position;

        rig.Pan(1, 0); // 屏幕右 1 单位

        var (right, _) = CameraMath.ComputePanAxes(45);
        var delta = rig.Target.Center - centerBefore;
        Assert.Equal(right.X, delta.X, 9);
        Assert.Equal(right.Z, delta.Z, 9);
        Assert.Equal((rig.Target.Position - positionBefore).Length(), delta.Length(), 9);
    }

    [Fact]
    public void Pan_ClampsToDiagonalRange()
    {
        var rig = NewRig();
        rig.Pan(1e9, 0);
        double offset = (rig.Target.Center - CameraMath.ComputeCenter(13, 6, 3.5)).Length();
        Assert.Equal(GameConfig.PanRangeFactor * Diagonal, offset, 6);
    }

    [Fact]
    public void Pan_PersistsAcrossInputRecompute()
    {
        var rig = NewRig();
        rig.Pan(1, 0);
        var pannedCenter = rig.Target.Center;

        rig.Update(13, 6, 3.0, Aspect, 45); // 层间距变化

        Assert.Equal(pannedCenter.X, rig.Target.Center.X, 9); // 平移偏移保留
        Assert.True(Math.Abs(rig.Target.Center.Y - 7.5) < 1e-9); // 公式中心 Y 随 spacing 更新
    }

    // ---- 缩放 ----

    [Fact]
    public void Zoom_ProportionalStepWithoutClamp()
    {
        var rig = NewRig();
        rig.Zoom(1);
        Assert.Equal(FormulaDistance * (1 - GameConfig.ZoomSpeed), rig.Target.Distance, 9);
    }

    [Fact]
    public void Zoom_ClampsAtDocLimits()
    {
        var rig = NewRig();
        rig.Zoom(1e9);
        Assert.Equal(FormulaDistance * 0.3, rig.Target.Distance, 6);

        var rig2 = NewRig();
        rig2.Zoom(-1e9);
        Assert.Equal(FormulaDistance * 3.0, rig2.Target.Distance, 6);
    }

    [Fact]
    public void ZoomToAnchor_ApproachesAnchor_KeepsScalarConsistency()
    {
        var rig = NewRig();
        var anchor = rig.Target.Position + new WorldPoint(0, 0, -10);
        double anchorDistBefore = (anchor - rig.Target.Position).Length();

        rig.ZoomToAnchor(anchor, 1);

        double anchorDistAfter = (anchor - rig.Target.Position).Length();
        Assert.Equal(anchorDistBefore * (1 - GameConfig.ZoomSpeed), anchorDistAfter, 9);

        // 标量 ↔ 位置 保持单一派生关系
        var (pitch, yaw, radius) = CameraMath.DecomposePosition(rig.Target.Position, rig.Target.Center);
        Assert.Equal(rig.Target.Pitch, pitch, 6);
        Assert.Equal(rig.Target.Yaw, yaw, 6);
        Assert.Equal(rig.Target.Distance, radius, 6);
    }

    // ---- 阻尼平滑 ----

    [Fact]
    public void Advance_LargeDt_ConvergesToTarget()
    {
        var rig = NewRig();
        rig.Orbit(90);
        rig.Zoom(2);
        rig.Pan(1, 1);

        var current = rig.Advance(10);

        Assert.Equal(rig.Target.Yaw, current.Yaw, 9);
        Assert.Equal(rig.Target.Pitch, current.Pitch, 9);
        Assert.Equal(rig.Target.Distance, current.Distance, 9);
        Assert.Equal(rig.Target.Center, current.Center);
        Assert.Equal(rig.Target.Position, current.Position);
    }

    [Fact]
    public void Advance_PartialDt_ApproachesMonotonically()
    {
        var rig = NewRig();
        rig.Orbit(90);

        var current = rig.Advance(0.01);
        Assert.InRange(current.Yaw, 45, 135);
        Assert.True(current.Yaw > 45 + 1e-3, "小步长应部分逼近");
        Assert.True(current.Yaw < 135 - 1e-3, "单帧不应到达");
    }

    [Fact]
    public void Advance_YawTakesShortestArc()
    {
        var rig = NewRig();
        rig.Orbit(305); // 目标 350°：最短路径是 -55°（经 0° 回绕）
        var current = rig.Advance(0.01);
        Assert.True(current.Yaw < 45, "应向 350° 反方向（递减）逼近，而不是 +305°");
    }

    [Fact]
    public void Advance_ZeroDt_LeavesStateUnchanged()
    {
        var rig = NewRig();
        var before = rig.Current;
        var after = rig.Advance(0);
        Assert.Equal(before, after);
    }

    // ---- 复位 ----

    [Fact]
    public void Reset_RestoresFormulaState_AndCurrentGlides()
    {
        var rig = NewRig();
        rig.Orbit(120, 40);
        rig.Pan(5, 5);
        rig.Zoom(-5);
        rig.Advance(10); // 渲染态先跟上交互态

        rig.Reset(13, 6, 3.5, Aspect);

        Assert.Equal(45, rig.Target.Yaw, 9);
        Assert.Equal(35, rig.Target.Pitch, 9);
        Assert.Equal(FormulaDistance, rig.Target.Distance, 9);
        Assert.Equal((6.0, 8.75, 6.0), (rig.Target.Center.X, rig.Target.Center.Y, rig.Target.Center.Z));
        Assert.NotEqual(rig.Target.Position, rig.Current.Position); // 渲染态尚未追上，平滑归位中
    }

    // ---- 透明度 ----

    [Fact]
    public void ComputeOpacity_MatchesMathOverload()
    {
        var rig = NewRig();
        Assert.Equal(CameraMath.ComputeOpacity(0, 6, 3.5), rig.ComputeOpacity(0), 9);
        Assert.Equal(CameraMath.ComputeOpacity(8.75, 6, 3.5), rig.ComputeOpacity(8.75), 9);
    }
}
