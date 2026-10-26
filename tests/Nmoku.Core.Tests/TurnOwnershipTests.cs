using Nmoku.Core;

namespace Nmoku.Core.Tests;

/// <summary>
/// 回合归属门控（修复批 ③-1）：PvE 下人类不得在 AI 思考窗内替 AI 落子。
/// 判定抽成 Core 纯函数以便锁死（Game 层无自动化覆盖）。
/// </summary>
public class TurnOwnershipTests
{
    [Fact]
    public void CanHumanPlace_PvP_AlwaysTrue()
    {
        Assert.True(TurnOwnership.CanHumanPlace(pve: false, PlayerColor.Black, PlayerColor.White));
        Assert.True(TurnOwnership.CanHumanPlace(pve: false, PlayerColor.White, PlayerColor.White));
    }

    [Fact]
    public void CanHumanPlace_PvE_HumanTurnAllowed_AiTurnRejected()
    {
        // 人执黑、AI 执白（M3 约定）：黑方回合可落子
        Assert.True(TurnOwnership.CanHumanPlace(pve: true, PlayerColor.Black, PlayerColor.White));
        // 白方（AI）回合：人类不得替 AI 落子
        Assert.False(TurnOwnership.CanHumanPlace(pve: true, PlayerColor.White, PlayerColor.White));
    }

    [Fact]
    public void CanHumanPlace_PvE_HumanIsWhite_AiIsBlack_Mirrored()
    {
        // 读档恢复时行棋方角色可能对调（存档 PlayerColor 决定谁是 AI）
        Assert.True(TurnOwnership.CanHumanPlace(pve: true, PlayerColor.White, PlayerColor.Black));
        Assert.False(TurnOwnership.CanHumanPlace(pve: true, PlayerColor.Black, PlayerColor.Black));
    }

    [Fact]
    public void IsAiTurn_MatchesCanHumanPlace_Complement()
    {
        foreach (bool pve in new[] { false, true })
        {
            foreach (PlayerColor current in Enum.GetValues<PlayerColor>())
            {
                foreach (PlayerColor ai in Enum.GetValues<PlayerColor>())
                {
                    Assert.NotEqual(
                        TurnOwnership.CanHumanPlace(pve, current, ai),
                        TurnOwnership.IsAiTurn(pve, current, ai));
                }
            }
        }
    }
}
