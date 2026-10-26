using Nmoku.Core.Camera;

namespace Nmoku.Core.Tests;

/// <summary>CameraRig：§8.1 增量缓存 + 自由交互（Orbit/Pan/Zoom/ZoomToAnchor）+ 阻尼平滑。</summary>
public class CameraRigTests
{
    private const double Aspect = 16.0 / 9;
    private static readonly double Diagonal = Math.Sqrt(12 * 12 + 12 * 12 + 17.5 * 17.5);
    private static readonly double FormulaDistance = Diagonal * 1.3; // 垂直 fov 限制（横屏）

    private static CameraRig NewRig() => new(13, 6, 3.5, Aspect);

    /// <summary>公式距离（03-camera §9 STEP 5）：独立复算，不抄实现里的中间量。</summary>
    private static double FormulaDistanceOf(int boardSize, int layers, double spacing, double aspect) =>
        CameraMath.ComputeDistance(
            CameraMath.ComputeTargetDiagonal(CameraMath.ComputeDiagonal(boardSize, layers, spacing)),
            CameraMath.ComputeFov(CameraMath.ComputeNormalizedAspect(boardSize, layers, spacing)),
            aspect);

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
        Assert.Equal((6.0, 6.0, 8.75), (rig.Target.Center.X, rig.Target.Center.Y, rig.Target.Center.Z));
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
    public void Update_AspectChange_RecomputesDistanceToFormulaValue()
    {
        var rig = NewRig();
        rig.Update(13, 6, 3.5, 0.5, 45);

        Assert.Equal(2, rig.RecomputeCount);
        // 修复批 ③-2：旧实现把用户缩放存成绝对距离并无条件写回 → 公式距离永不重算
        // （16:9→0.5 应为 57.13 却仍为 31.69）。断言改为**精确等于公式值**：
        // 旧断言 rig.Target.Distance > FormulaDistance 靠 1 ulp（3.55e-15）浮点毛刺假绿。
        Assert.Equal(FormulaDistanceOf(13, 6, 3.5, 0.5), rig.Target.Distance, 9);
        Assert.True(rig.Target.Distance > FormulaDistance * 1.5, "窄视口水平 FOV 限制 → 显著更远");
        Assert.Equal(45, rig.Target.Yaw, 9);
    }

    [Fact]
    public void Update_AspectChange_KeepsUserZoomFactor_AndRefitsViewport()
    {
        // 用户缩放以倍率参与公式距离：视口变窄时倍率不变（构图意图不变），绝对距离按新基准重算
        var rig = NewRig();
        rig.Zoom(5);
        double factor = rig.ZoomFactor;
        Assert.True(factor < 1.0, "缩放拉近 → 倍率 < 1");

        rig.Update(13, 6, 3.5, 0.5, 45);

        Assert.Equal(factor, rig.ZoomFactor, 9);
        Assert.Equal(FormulaDistanceOf(13, 6, 3.5, 0.5) * factor, rig.Target.Distance, 9);
    }

    [Fact]
    public void Update_NoUserZoom_DistanceEqualsFormulaValue()
    {
        var rig = NewRig();
        rig.Update(13, 6, 3.0, 0.5, 45);
        Assert.Equal(1.0, rig.ZoomFactor, 9);
        Assert.Equal(FormulaDistanceOf(13, 6, 3.0, 0.5), rig.Target.Distance, 9);
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
        var offsetBefore = rig.Target.Center - CameraMath.ComputeCenter(13, 6, 3.5);

        rig.Update(13, 6, 3.0, Aspect, 45); // 层间距变化

        // 平移偏移（相对公式中心）原样保留，公式中心随 spacing 移动
        var offsetAfter = rig.Target.Center - CameraMath.ComputeCenter(13, 6, 3.0);
        Assert.Equal(offsetBefore, offsetAfter);
    }

    [Fact]
    public void Zoom_PersistsAcrossSpacingRecompute_AsRelativeFactor()
    {
        // 人类反馈修复：Z/C 改层间距触发公式重算时，用户缩放不得被拉回初始态。
        // 修复批 ③-2 后语义细化为「**倍率**保留」：绝对距离随公式基准（盘面包围盒）一起缩放，
        // 构图比例不变——绝对距离不变会让缩小的盘看起来被拉远。
        var rig = NewRig();
        rig.Zoom(5);
        double factor = rig.ZoomFactor;
        double zoomed = rig.Target.Distance;
        Assert.True(zoomed < FormulaDistance);

        rig.Update(13, 6, 3.0, Aspect, 45); // 层间距变化

        Assert.Equal(factor, rig.ZoomFactor, 9);                            // 倍率保留（未被公式覆盖）
        Assert.Equal(FormulaDistanceOf(13, 6, 3.0, Aspect) * factor, rig.Target.Distance, 9);
        Assert.Equal(7.5, rig.Target.Center.Z, 9);                          // 层轴中心确实随 spacing 更新
    }

    [Fact]
    public void Reset_ClearsZoomFactor_BackToFormula()
    {
        var rig = NewRig();
        rig.Zoom(5);
        rig.Reset(13, 6, 3.5, Aspect);
        Assert.Equal(1.0, rig.ZoomFactor, 9);
        Assert.Equal(FormulaDistanceOf(13, 6, 3.5, Aspect), rig.Target.Distance, 9);
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
    public void Advance_YawChasesAccumulatedIntent_NotShortestArc()
    {
        // M4R 行为变更：Orbit 的转角是累积意图，Advance 线性追踪原始差值。
        // 旧"最短弧"语义在连续 ±90° 跨 ±180° 时方向翻转（Q/E 抽搐根因），弃用；
        // 最短路径仅保留在 Reset（就近归位），见 Reset_YawReferenceFrameSnapsToNearestAngle。
        var rig = NewRig();
        rig.Orbit(305); // 目标 350：按字面意图 +305° 逼近（递增），不回绕抄近路
        var current = rig.Advance(0.01);
        Assert.True(current.Yaw > 45, "累积意图语义：应沿 +305° 方向（递增）逼近");
    }

    [Fact]
    public void Reset_YawReferenceFrameSnapsToNearestAngle()
    {
        var rig = NewRig();
        for (int i = 0; i < 20; i++)
        {
            rig.Orbit(-90);
            rig.Advance(1.0 / 60);
        }
        Assert.True(Math.Abs(rig.Current.Yaw) > 360, "前置：参考系已远离初始角");
        rig.Reset(13, 6, 3.5, Aspect);
        Assert.InRange(rig.Current.Yaw - rig.Target.Yaw, -180, 180); // 就近归位，回家走最短路径
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
        Assert.Equal((6.0, 6.0, 8.75), (rig.Target.Center.X, rig.Target.Center.Y, rig.Target.Center.Z));
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

    // ---- yaw 连续累积追踪（M4R：Q/E 跨 ±180° 抽搐根因——Advance 不得用最短弧）----

    [Fact]
    public void Advance_YawAccumulationAcrossBoundary_ChaseNeverReverses()
    {
        var rig = NewRig();
        for (int i = 0; i < 60; i++)
        {
            if (i % 3 == 0)
            {
                rig.Orbit(-90); // 目标持续越过 ±180°（45 → -45 → … → -1665）
            }
            double before = rig.Current.Yaw;
            rig.Advance(1.0 / 60);
            Assert.True(rig.Current.Yaw <= before + 1e-9,
                $"第 {i} 帧 yaw 反向：{before:0.00} → {rig.Current.Yaw:0.00}（最短弧翻转抽搐）");
        }
        for (int i = 0; i < 300; i++)
        {
            rig.Advance(1.0 / 60); // 充分收敛
        }
        Assert.Equal(rig.Target.Yaw, rig.Current.Yaw, 6);
    }
}
