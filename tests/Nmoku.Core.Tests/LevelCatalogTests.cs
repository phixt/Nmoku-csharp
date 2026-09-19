using Nmoku.Core;
using Xunit;

namespace Nmoku.Core.Tests;

/// <summary>关卡预设（01-core §7 + 00-domain §2.2/§2.8 约束）。</summary>
public class LevelCatalogTests
{
    [Fact]
    public void Presets_SatisfyDomainConstraints()
    {
        Assert.All(LevelCatalog.Presets, level =>
        {
            Assert.True(level.BoardSize >= GameConfig.MinBoardSize, $"{level.Id}: 边长 <5");
            Assert.Equal(1, level.BoardSize % 2); // 奇数
            Assert.InRange(level.WinLength, GameConfig.MinWinLength, GameConfig.MaxWinLength);
            Assert.True(level.Layers >= 1, $"{level.Id}: 层数 <1");
        });
    }

    [Fact]
    public void Residual_InitialMoves_InBoundsUniqueAndSequential()
    {
        var level = LevelCatalog.Residual;
        Assert.NotEmpty(level.InitialMoves);

        var seen = new HashSet<(int, int, int)>();
        foreach (var move in level.InitialMoves)
        {
            Assert.InRange(move.X, 0, level.BoardSize - 1);
            Assert.InRange(move.Y, 0, level.BoardSize - 1);
            Assert.InRange(move.Z, 0, level.Layers - 1);
            Assert.True(seen.Add((move.X, move.Y, move.Z)), "预置子坐标重复");
        }
        // 手顺黑白交替（黑先）
        for (int i = 0; i < level.InitialMoves.Count; i++)
        {
            var expected = i % 2 == 0 ? PlayerColor.Black : PlayerColor.White;
            Assert.Equal(expected, level.InitialMoves[i].Player);
        }
    }

    [Fact]
    public void Presets_HaveUniqueIdsAndNames()
    {
        var ids = LevelCatalog.Presets.Select(p => p.Id).ToHashSet();
        Assert.Equal(LevelCatalog.Presets.Count, ids.Count);
        Assert.All(LevelCatalog.Presets, p => Assert.False(string.IsNullOrWhiteSpace(p.Name)));
    }
}
