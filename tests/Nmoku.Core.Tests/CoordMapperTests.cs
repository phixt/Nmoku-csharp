using Nmoku.Core.Camera;

namespace Nmoku.Core.Tests;

/// <summary>03-camera §2：逻辑→世界映射与逆映射（镜像收敛单点）。</summary>
public class CoordMapperTests
{
    [Fact]
    public void LogicalToWorld_MapsAxesPerDoc()
    {
        // x=列→X，y=行→Z，z=层→Y（×spacing）——文档 §2.2 逐字段
        var world = CoordMapper.LogicalToWorld(3, 5, 2, 3.5);
        Assert.Equal(3, world.X);
        Assert.Equal(7.0, world.Y);
        Assert.Equal(5, world.Z);
    }

    [Fact]
    public void LogicalToWorld_ZeroSpacing_PutsAllLayersOnGround()
    {
        var world = CoordMapper.LogicalToWorld(4, 6, 3, 0);
        Assert.Equal(0, world.Y);
        Assert.Equal(6, world.Z);
    }

    [Fact]
    public void WorldToLogical_RoundTrips()
    {
        var world = CoordMapper.LogicalToWorld(4, 7, 3, 3.5);
        var (x, y, z) = CoordMapper.WorldToLogical(world, 3.5);
        Assert.Equal((4, 7, 3), (x, y, z));
    }

    [Fact]
    public void WorldToLogical_RoundsLayerHeightToNearestLayer()
    {
        var (_, _, z) = CoordMapper.WorldToLogical(new WorldPoint(1, 10.4, 2), 3.5);
        Assert.Equal(3, z); // 10.4/3.5 = 2.97 → 3
    }
}
