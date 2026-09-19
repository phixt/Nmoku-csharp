namespace Nmoku.Core.Camera;

/// <summary>
/// 相机装配器：八步管道公式态（§9）+ 自由交互（人类决策 2026-09-19）+ 阻尼平滑。
/// - <see cref="Update"/>：按输入增量重算公式态（§8.1 脏标记；yaw 由 Orbit/Reset 持有，不参与比较）
/// - <see cref="Orbit"/>/<see cref="Pan"/>/<see cref="Zoom"/>/<see cref="ZoomToAnchor"/>：改 Target
/// - <see cref="Advance"/>：指数阻尼把 Current 逼近 Target（帧率无关），Current 供渲染
/// 标量（yaw/pitch/distance/fov/center）是唯一事实源，Position 每次派生；纯数学无 Godot。
/// </summary>
public sealed class CameraRig
{
    private CameraState _target = null!;
    private CameraState _current = null!;
    private WorldPoint _formulaCenter;
    private WorldPoint _panOffset;
    private double? _distanceOverride; // 用户缩放（Z/C 改层间距等重算后保留；X 复位清除）
    private bool _userPitched;
    private bool _initialized;

    /// <summary>目标态（交互立即生效）。</summary>
    public CameraState Target => _target;

    /// <summary>渲染态（平滑逼近 Target；Game 每帧读取并应用到 Camera3D）。</summary>
    public CameraState Current => _current;

    /// <summary>测试可观测：公式态重算次数（§8.1 增量验证）。</summary>
    public int RecomputeCount { get; private set; }

    public CameraRig(int boardSize, int layers, double layerSpacing, double aspect,
        double yawDeg = GameConfig.DefaultYaw)
    {
        Update(boardSize, layers, layerSpacing, aspect, yawDeg);
        _current = _target; // 初始态不做过渡
    }

    /// <summary>§8.1 needsRecompute 原样（含 yaw 维度，供缓存语义测试）。</summary>
    public static bool NeedsRecompute(CameraState cache, int boardSize, int layers,
        double layerSpacing, double aspect, double yaw) =>
        cache.BoardSize != boardSize
        || cache.Layers != layers
        || Math.Abs(cache.LayerSpacing - layerSpacing) > GameConfig.CameraEpsilon
        || Math.Abs(cache.AspectRatio - aspect) > GameConfig.CameraEpsilon
        || Math.Abs(cache.Yaw - yaw) > GameConfig.CameraEpsilon;

    /// <summary>
    /// 重算公式态。yawDeg 仅首次构造/Reset 时作为种子（之后朝向由 <see cref="Orbit"/> 累积持有）。
    /// 平移偏移跨输入变化保留（clamp 到新对角线）；pitch 在用户 Orbit 过后不再被 aspect 覆盖。
    /// </summary>
    public void Update(int boardSize, int layers, double layerSpacing, double aspect, double yawDeg)
    {
        if (_initialized
            && _target.BoardSize == boardSize
            && _target.Layers == layers
            && Math.Abs(_target.LayerSpacing - layerSpacing) <= GameConfig.CameraEpsilon
            && Math.Abs(_target.AspectRatio - aspect) <= GameConfig.CameraEpsilon)
        {
            return; // §8.1：输入未变不重算
        }

        // 走到这里必有输入变化：整体按传入值重算（各阶段本就是输入到输出的纯函数，
        // §8.1 的分阶段增量在此粒度下与全量重算等价）
        _formulaCenter = CameraMath.ComputeCenter(boardSize, layers, layerSpacing);
        double diagonal = CameraMath.ComputeDiagonal(boardSize, layers, layerSpacing);
        _panOffset = ClampPan(_panOffset, diagonal);
        double targetDiagonal = CameraMath.ComputeTargetDiagonal(diagonal);
        double normalizedAspect = CameraMath.ComputeNormalizedAspect(boardSize, layers, layerSpacing);
        double fov = CameraMath.ComputeFov(normalizedAspect);
        double pitch = _initialized && _userPitched ? _target.Pitch : CameraMath.ComputePitch(normalizedAspect);
        var (minDist, maxDist) = CameraMath.ComputeDistanceLimits(targetDiagonal, fov, aspect);
        // 用户缩放优先于公式距离（修复：Z/C 改层间距触发重算时缩放被拉回初始态）
        double distance = _distanceOverride is { } overridden
            ? Math.Clamp(overridden, minDist, maxDist)
            : CameraMath.ComputeDistance(targetDiagonal, fov, aspect);
        _distanceOverride = distance;
        var (near, far) = CameraMath.ComputeClipPlanes(distance);
        double yaw = _initialized ? _target.Yaw : yawDeg;
        var center = _formulaCenter + _panOffset;

        _target = new CameraState
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
            Position = CameraMath.ComputePosition(center, distance, pitch, yaw),
            NearPlane = near,
            FarPlane = far,
            Yaw = yaw,
        };
        _initialized = true;
        RecomputeCount++;
    }

    /// <summary>无级旋转（度）：yaw 累加无上限；pitch clamp [MinPitch, MaxPitch]。</summary>
    public void Orbit(double deltaYawDeg, double deltaPitchDeg = 0)
    {
        double pitch = Math.Clamp(_target.Pitch + deltaPitchDeg, GameConfig.MinPitch, GameConfig.MaxPitch);
        _userPitched |= Math.Abs(deltaPitchDeg) > GameConfig.CameraEpsilon;
        ApplyTarget(_target.Yaw + deltaYawDeg, pitch, _target.Distance, _target.Center);
    }

    /// <summary>视角平移：沿屏幕对齐地面轴移动目标点，相机随动（限幅 PanRangeFactor×对角线）。</summary>
    public void Pan(double rightAmount, double forwardAmount)
    {
        var (right, forward) = CameraMath.ComputePanAxes(_target.Yaw);
        _panOffset = ClampPan(_panOffset + right * rightAmount + forward * forwardAmount, _target.Diagonal);
        ApplyTarget(_target.Yaw, _target.Pitch, _target.Distance, _formulaCenter + _panOffset);
    }

    /// <summary>径向缩放（§6.1）：delta&gt;0 拉近；范围 [base×0.3, base×3.0]。缩放跨公式重算保留。</summary>
    public void Zoom(double delta)
    {
        var (min, max) = CameraMath.ComputeDistanceLimits(_target.TargetDiagonal, _target.Fov, _target.AspectRatio);
        double distance = Math.Clamp(_target.Distance - delta * GameConfig.ZoomSpeed * _target.Distance, min, max);
        _distanceOverride = distance;
        ApplyTarget(_target.Yaw, _target.Pitch, distance, _target.Center);
    }

    /// <summary>
    /// 锚点缩放（§6.3，滚轮用）：位置向 anchor 推近/拉远，随后反解球坐标；
    /// 轨道半径仍 clamp 在 [min, max]，pitch 不越界。
    /// </summary>
    public void ZoomToAnchor(WorldPoint anchor, double delta)
    {
        var dir = anchor - _target.Position;
        double anchorDist = dir.Length();
        if (anchorDist < GameConfig.CameraEpsilon)
        {
            return;
        }
        double ratio = delta * GameConfig.ZoomSpeed; // >0 = 向锚点推近（§6.3 ratio 语义）
        var position = _target.Position + dir * ratio;
        var (pitch, yaw, radius) = CameraMath.DecomposePosition(position, _target.Center);
        var (min, max) = CameraMath.ComputeDistanceLimits(_target.TargetDiagonal, _target.Fov, _target.AspectRatio);
        radius = Math.Clamp(radius, min, max);
        _distanceOverride = radius;
        ApplyTarget(yaw, Math.Clamp(pitch, GameConfig.MinPitch, GameConfig.MaxPitch), radius, _target.Center);
    }

    /// <summary>复位（X 键 / 读档 §10.2）：清平移与缩放覆盖、回默认 yaw 与公式 pitch/distance；渲染态平滑归位。</summary>
    public void Reset(int boardSize, int layers, double layerSpacing, double aspect)
    {
        _panOffset = default;
        _distanceOverride = null;
        _userPitched = false;
        _initialized = false;
        Update(boardSize, layers, layerSpacing, aspect, GameConfig.DefaultYaw);
        // yaw 参考系就近归位：把 current 搬进 target 的 ±180° 邻域（等效角差整圈时归零，
        // 避免 Reset 后线性追踪空转 N 整圈）；最短路径回家，之后 Advance 线性追踪（累积意图语义，M4R）
        _current = _current with { Yaw = _target.Yaw + ShortestArc(_current.Yaw - _target.Yaw) };
    }

    /// <summary>指数阻尼逼近（帧率无关）：Game 每帧调用，返回平滑后的渲染态。</summary>
    public CameraState Advance(double dtSeconds)
    {
        double t = 1 - Math.Exp(-GameConfig.CameraSmoothRate * Math.Max(dtSeconds, 0));
        // yaw 线性追踪原始差值：Target 由 Orbit 无界累积，Current 连续跟随同一参考系，
        // 差值恒等于"本次意图转角"。不可用最短弧——跨 ±180° 后方向翻转（Q/E 连续旋转抽搐根因）。
        double yaw = Step(_current.Yaw, _target.Yaw, (_target.Yaw - _current.Yaw) * t);
        double pitch = Step(_current.Pitch, _target.Pitch, (_target.Pitch - _current.Pitch) * t);
        double distance = Step(_current.Distance, _target.Distance, (_target.Distance - _current.Distance) * t);
        double fov = Step(_current.Fov, _target.Fov, (_target.Fov - _current.Fov) * t);
        var center = _current.Center + (_target.Center - _current.Center) * t;
        if ((_target.Center - center).Length() < GameConfig.CameraEpsilon)
        {
            center = _target.Center;
        }

        var position = CameraMath.ComputePosition(center, distance, pitch, yaw);
        var (near, far) = CameraMath.ComputeClipPlanes(distance);
        _current = _current with
        {
            Yaw = yaw,
            Pitch = pitch,
            Distance = distance,
            Fov = fov,
            Center = center,
            Position = position,
            NearPlane = near,
            FarPlane = far,
        };
        return _current;
    }

    /// <summary>连续透明度（§8.2），按当前态的中心与层高。</summary>
    public double ComputeOpacity(double worldY) =>
        CameraMath.ComputeOpacity(worldY, _current.Layers, _current.LayerSpacing);

    private void ApplyTarget(double yaw, double pitch, double distance, WorldPoint center)
    {
        var (near, far) = CameraMath.ComputeClipPlanes(distance);
        _target = _target with
        {
            Yaw = yaw,
            Pitch = pitch,
            Distance = distance,
            Center = center,
            Position = CameraMath.ComputePosition(center, distance, pitch, yaw),
            NearPlane = near,
            FarPlane = far,
        };
    }

    private static double Step(double from, double to, double delta)
    {
        // 收敛后精确贴齐，避免指数逼近的浮点尾差
        return Math.Abs(to - from) <= Math.Abs(delta) + GameConfig.CameraEpsilon ? to : from + delta;
    }

    /// <summary>仅 Reset 参考系归位使用（把 current 搬到 target 的 ±180° 邻域）；Advance 不做回绕。</summary>
    private static double ShortestArc(double deltaDeg)
    {
        deltaDeg %= 360;
        if (deltaDeg > 180)
        {
            deltaDeg -= 360;
        }
        if (deltaDeg < -180)
        {
            deltaDeg += 360;
        }
        return deltaDeg;
    }

    private static WorldPoint ClampPan(WorldPoint offset, double diagonal)
    {
        double max = GameConfig.PanRangeFactor * diagonal;
        double len = offset.Length();
        return len > max ? offset * (max / len) : offset;
    }
}
