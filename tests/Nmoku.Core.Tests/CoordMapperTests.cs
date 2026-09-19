using Nmoku.Core.Camera;

namespace Nmoku.Core.Tests;

/// <summary>B8+B10：逻辑→世界映射（行=Y 竖直、层沿 Z 且层 0 最近摄像机、列绕 Y 翻转）与逆映射。</summary>
public class CoordMapperTests
{
    [Fact]
    public void LogicalToWorld_Rotates180AroundY_PerB10()
    {
        // worldX = size-1-x，worldZ = (layers-1-z)×spacing，worldY = y
        var world = CoordMapper.LogicalToWorld(3, 5, 2, 3.5, boardSize: 13, layers: 6);
        Assert.Equal(9, world.X);      // 13-1-3
        Assert.Equal(5, world.Y);
        Assert.Equal(10.5, world.Z);   // (6-1-2)×3.5 → 层 2 位于中层
    }

    [Fact]
    public void LogicalToWorld_LayerZeroIsNearestFace()
    {
        // 层 0 → 最大 worldZ（最靠近 +Z 侧摄像机）；层 5 → worldZ = 0（最里）
        var first = CoordMapper.LogicalToWorld(0, 0, 0, 3.5, 13, 6);
        var last = CoordMapper.LogicalToWorld(0, 0, 5, 3.5, 13, 6);
        Assert.Equal(17.5, first.Z);
        Assert.Equal(0, last.Z);
    }

    [Fact]
    public void LogicalToWorld_ZeroSpacing_PutsAllLayersOnFrontPlane()
    {
        var world = CoordMapper.LogicalToWorld(4, 6, 3, 0, 13, 6);
        Assert.Equal(0, world.Z);
        Assert.Equal(6, world.Y);
    }

    [Fact]
    public void WorldToLogical_RoundTrips()
    {
        var world = CoordMapper.LogicalToWorld(4, 7, 3, 3.5, 13, 6);
        var (x, y, z) = CoordMapper.WorldToLogical(world, 3.5, 13, 6);
        Assert.Equal((4, 7, 3), (x, y, z));
    }

    [Fact]
    public void WorldToLogical_RoundsLayerDepthToNearestLayer()
    {
        // x 翻转：13-1-1 = 11；层深 10.4/3.5 = 2.97 → 3，翻转后 6-1-3 = 2
        var (x, y, z) = CoordMapper.WorldToLogical(new WorldPoint(1, 2, 10.4), 3.5, 13, 6);
        Assert.Equal((11, 2, 2), (x, y, z));
    }
}
