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
        public const string NoSaveToLoad = "暂无存档";
        public const string ManageTitle = "存档管理";
        public const string DeleteTitle = "删除存档";
        public const string SlotLabel = "槽位 {0}";
        public const string QuickSlotLabel = "槽位 {0}（快存）";
        public const string SlotEmpty = "空";
        public const string SlotInfo = "{0}×{1}×{2} · {3} 手 · {4}";
        public const string SlotFinished = "已结束";
        public const string SlotPlaying = "进行中";
        public const string ImportedToSlot = "已导入到槽位 {0}";
        public const string ImportFailed = "剪贴板中无有效存档";
        public const string ImportNoEmptySlot = "没有空槽位可导入";
        public const string Exported = "已导出到剪贴板";
        public const string ExportedToFile = "已存为存档文件";
        public const string ExportFailed = "存档文件写入失败";
        public const string Import = "导入";
        public const string ImportFile = "导入文件";
        public const string ExportFile = "文件";
        public const string Export = "导出";
        public const string Delete = "删除";
        public const string DeleteConfirm = "确定删除槽位 {0} 的存档？";
        public const string LoadSlotFailed = "该存档无法读取";
    }

    public static class Title
    {
        public const string GameName = "立体五子棋";
        public const string Start = "开始游戏";
        public const string ContinueGame = "继续对局";
        public const string CustomGame = "自定义对局";
        public const string LoadGame = "读档";
        public const string Guide = "引导";
        public const string Quit = "退出";
        public const string Subtitle = "13×13×6 三维连珠 · Nmoku";
        public const string NewGameConfirm = "当前有进行中的对局，开始新局将覆盖保留现场。确定？";
    }

    public static class Menu
    {
        public const string Paused = "已暂停";
        public const string Resume = "继续游戏";
        public const string Restart = "重新开始";
        public const string BackToTitle = "返回标题";
        public const string RestartConfirm = "确定重新开始？当前对局进度将丢失。";
    }

    public static class Replay
    {
        public const string Label = "复盘";
        public const string Exit = "退出复盘";
        public const string StepInfo = "{0} / {1}";
    }

    public static class Guide
    {
        public const string Title = "游戏引导";
        public const string Body = "在三维棋盘的任意层连成 N 子直线即获胜：同层横竖斜、跨层斜线（13 个方向）均可。\n" +
            "A/D 循环切换焦点层，F 镜像，H 开关辅助线，V 色弱模式。\n" +
            "点击右侧面板落子；Ctrl+Z 悔棋；Ctrl+S 快存；L 读最新存档。\n" +
            "SWAP2 规则：黑下首子后，白方可选择交换执黑或继续执白。";
        public const string StartFromGuide = "开始游戏";
        public const string ResumeFromGuide = "回到对局";
        public const string Back = "返回";
    }

    public static class Overlay
    {
        public const string Confirm = "确认";
        public const string Cancel = "取消";
        public const string SwapYes = "交换黑白";
        public const string SwapNo = "继续执白";
    }

    public static class Levels
    {
        public const string Classic = "标准对局";
        public const string Tutorial = "教学·平面";
        public const string Residual = "残局·预置子";
        public const string Cmdline = "命令行参数局";
        public const string Custom = "自定义对局";
        public const string Preset = "预设";
        public const string BoardSize = "棋盘边长（奇数 ≥5）";
        public const string Layers = "层数";
        public const string WinLength = "连珠数（4–9）";
        public const string Rules = "规则";
        public const string Mode = "模式";
        public const string PvE = "人机";
        public const string PvP = "双人";
        public const string AiDifficulty = "AI 难度";
        public const string Start = "开始对局";
        public const string SizeInvalid = "棋盘边长必须为 ≥5 的奇数";
        public const string Gomoku = "无禁手";
        public const string Swap2 = "SWAP2";
        public const string Easy = "简单";
        public const string Medium = "中等";
        public const string Hard = "困难";
    }
}
