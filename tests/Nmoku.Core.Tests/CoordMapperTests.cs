using Nmoku.Core.Camera;

namespace Nmoku.Core.Tests;

/// <summary>03-camera §2：逻辑→世界映射与逆映射（镜像收敛单点）。</summary>
public class CoordMapperTests
{
    [Fact]
    public void LogicalToWorld_MapsAxesPerSourceOrientation()
    {
        // B8：x=列→X，y=行→Y（竖直），z=层→Z（纵深，×spacing）——与源 LeftPanel 一致
        var world = CoordMapper.LogicalToWorld(3, 5, 2, 3.5);
        Assert.Equal(3, world.X);
        Assert.Equal(5, world.Y);
        Assert.Equal(7.0, world.Z);
    }

    [Fact]
    public void LogicalToWorld_ZeroSpacing_PutsAllLayersOnFrontPlane()
    {
        var world = CoordMapper.LogicalToWorld(4, 6, 3, 0);
        Assert.Equal(0, world.Z);
        Assert.Equal(6, world.Y);
    }

    [Fact]
    public void WorldToLogical_RoundTrips()
    {
        var world = CoordMapper.LogicalToWorld(4, 7, 3, 3.5);
        var (x, y, z) = CoordMapper.WorldToLogical(world, 3.5);
        Assert.Equal((4, 7, 3), (x, y, z));
    }

    [Fact]
    public void WorldToLogical_RoundsLayerDepthToNearestLayer()
    {
        var (_, _, z) = CoordMapper.WorldToLogical(new WorldPoint(1, 2, 10.4), 3.5);
        Assert.Equal(3, z); // 10.4/3.5 = 2.97 → 3
    }
}
