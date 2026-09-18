namespace Nmoku.Core;

/// <summary>
/// 文本常量（用户决策 #6）：M1 先集中字符常量，值为简体中文（源项目默认语言）；
/// M5 平滑扩展为 i18n 查找（键语义不变）。
/// </summary>
public static class Text
{
    public static class Swap
    {
        /// <summary>交换阶段 UI 提示（01-core §4.3 "Choose Swap or Pass"）。</summary>
        public const string ChooseSwapOrDefault = "白方选择：交换黑白，还是继续执白？";
    }

    public static class Turn
    {
        public const string BlackTurn = "黑方行棋";
        public const string WhiteTurn = "白方行棋";
    }

    public static class Result
    {
        public const string BlackWin = "黑方获胜";
        public const string WhiteWin = "白方获胜";
        public const string Draw = "平局";
        public const string GameOverSubtitle = "共 {0} 手";
    }

    public static class Save
    {
        public const string QuickSaved = "已快存";
        public const string LoadFailed = "存档无效或已损坏";
    }
}
