namespace Nmoku.Core;

/// <summary>
/// 回合归属（PvE 执子方门控）：此刻谁有权落子。
/// <para>
/// <b>修复批 ③-1</b>：此前落子入口只判「界内 + 空格 + 未终局/未决策期/非复盘」，
/// <b>没有判行棋方</b>——PvE 下人类可在 AI 思考窗内替 AI 落子。后果有二：
/// ① AI 已算好的应答被 <c>ApplyAiMove</c> 的「当前行棋方≠AI 色」守卫静默丢弃且**无重试**
/// → 对局卡死（此后双方都是人类在走）；② AI 的应手被人类抢走，局面不可预期。
/// </para>
/// <para>
/// 门控点唯一：落子入口（<c>GameManager.TryPlace</c>）。本类型把判定抽成纯函数以便
/// xUnit 锁死（Game 层无自动化覆盖，见批次路线图 ④）。
/// </para>
/// </summary>
public static class TurnOwnership
{
    /// <summary>
    /// 人类此刻是否有权落子：非 PvE 恒真（双方皆人）；PvE 且当前行棋方 = AI 色时为假
    /// （AI 思考窗内人类不得替 AI 落子）。
    /// </summary>
    public static bool CanHumanPlace(bool pve, PlayerColor current, PlayerColor aiColor) =>
        !pve || current != aiColor;

    /// <summary>此刻是否轮到 AI（PvE 且当前行棋方 = AI 色）。PvP 恒假。</summary>
    public static bool IsAiTurn(bool pve, PlayerColor current, PlayerColor aiColor) =>
        pve && current == aiColor;

    /// <summary>
    /// 由存档 <c>SaveData.playerColor</c>（人类执子色：1=黑、2=白）反推 AI 执子色（互为对手）。
    /// 缺失或非法值默认人类执黑、AI 执白（M3 约定）。
    /// </summary>
    public static PlayerColor AiColorFromHumanColor(int humanPlayerColor) =>
        humanPlayerColor == (int)PlayerColor.White ? PlayerColor.Black : PlayerColor.White;
}
