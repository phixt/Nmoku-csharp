namespace Nmoku.Core.Camera;

/// <summary>相机全量参数（03-camera §9 管道输出；§8.1 缓存的数据面）。角度字段均为度。</summary>
public sealed record CameraState
{
    public required int BoardSize { get; init; }
    public required int Layers { get; init; }
    public required double LayerSpacing { get; init; }
    public required double AspectRatio { get; init; }

    public required WorldPoint Center { get; init; }        // 观察目标（含平移偏移）
    public required double Diagonal { get; init; }
    public required double TargetDiagonal { get; init; }
    public required double NormalizedAspect { get; init; }
    public required double Fov { get; init; }
    public required double Pitch { get; init; }
    public required double Distance { get; init; }
    public required WorldPoint Position { get; init; }      // 由 Center/Distance/Pitch/Yaw 派生
    public required double NearPlane { get; init; }
    public required double FarPlane { get; init; }
    public required double Yaw { get; init; }
}
