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
        public const string NoQuickSave = "暂无快存";
        public const string ManageTitle = "存档管理";
        public const string DeleteTitle = "删除存档";
        public const string SlotLabel = "槽位 {0}";
        public const string QuickSlotLabel = "槽位 {0}（快存）";
        public const string SlotEmpty = "空";
        public const string SlotInfo = "{0}×{1}×{2} · {3} 手 · {4}";
        public const string SlotFinished = "已结束";
        public const string SlotPlaying = "进行中";
        public const string Read = "读取";
        public const string Fill = "填写";
        public const string Overwrite = "覆盖";
        public const string OverwriteTitle = "覆盖存档";
        public const string OverwriteConfirm = "确认用剪贴板中的存档覆盖槽位 {0}？";
        public const string Copy = "复制";
        public const string Copied = "已复制到剪贴板";
        public const string Import = "导入";
        public const string ImportFailed = "剪贴板中无有效存档";
        public const string ImportFileFailed = "文件中无有效存档";
        public const string ImportedToSlot = "已导入到槽位 {0}";
        public const string Export = "导出";
        public const string ExportedToFile = "已存为存档文件";
        public const string ExportFailed = "存档文件写入失败";
        public const string Delete = "删除";
        public const string DeleteConfirm = "确定删除槽位 {0} 的存档？";
        public const string LoadSlotFailed = "该存档无法读取";
        public const string SavedToSlot = "已保存到存档 {0}";
        /// <summary>写盘失败（修复批 ②-2：落盘失败不再假报「已快存」）。</summary>
        public const string SaveFailed = "保存失败";
        /// <summary>删除失败（磁盘删除未完成，槽位保留）。</summary>
        public const string DeleteFailed = "删除失败";
        public const string MenuKeyHint = "保存当前对局：Ctrl+S 写快存 · S 存入首个空槽 · Ctrl+L 读快存";
        /// <summary>.nzsave 文件对话框的筛选描述（文件类型通配符不入翻译）。</summary>
        public const string SaveFileFilter = "Nmoku 存档文件";
    }

    public static class Title
    {
        public const string GameName = "立体五子棋";
        public const string Start = "开始游戏";
        public const string ContinueGame = "继续对局";
        public const string CustomGame = "自定义对局";
        public const string LoadGame = "读档";
        public const string Guide = "快捷键说明";
        public const string Quit = "退出";
        public const string Subtitle = "13×13×6 三维连珠 · Nmoku";
        public const string NewGameConfirm = "当前有进行中的对局，开始新局将覆盖保留现场。确定？";
        public const string ClickToEnter = "点击展开棋盘";
        public const string BackToCover = "返回封面";
        public const string ClearDemo = "重置演示";
        public const string DemoHint = "{0} · 点击落子 · 拖拽旋转";
    }

    public static class Menu
    {
        public const string Paused = "已暂停";
        public const string Resume = "继续游戏";
        public const string Restart = "重新开始";
        public const string SaveProgress = "保存进度";
        public const string LoadProgress = "加载进度";
        public const string BackToTitle = "返回标题";
        public const string RestartConfirm = "确定重新开始？当前对局进度将丢失。";
    }

    public static class Replay
    {
        public const string Label = "复盘";
        public const string Exit = "退出复盘";
        public const string StepInfo = "{0} / {1}";
        public const string PrevStep = "上一步";
        public const string NextStep = "下一步";
    }

    public static class Guide
    {
        public const string Title = "游戏引导";
        public const string Body = "在三维棋盘的任意层连成 N 子直线即获胜：同层横竖斜、跨层斜线（13 个方向）均可。\n" +
            "标题屏可进入自定义对局：选预设关卡或自定尺寸（奇数 ≥5）、层数、连珠数（4–9）、规则与 AI 难度。\n" +
            "对局中：A/D 循环换层，F 镜像，H 辅助线，V 色弱，O 网格全亮；Ctrl+Z 悔棋，Ctrl+S 快存，L 读最新档。\n" +
            "Q/E 旋转视角（按一下 90°），W/S 或方向键缩放，Shift+WASD 平移，Z/C 调层间距，X 复位。\n" +
            "存档管理支持剪贴板与 .nzsave 文件两种导入导出；T 切换明暗主题；按 F1 查看全部快捷键。";
        public const string StartFromGuide = "开始游戏";
        public const string ResumeFromGuide = "回到对局";
        public const string Back = "返回";
        public const string KeymapTitle = "快捷键说明（F1）";
        /// <summary>F1 页作用域名拼接模板（en: "{0} layer"）。</summary>
        public const string LayerPageFormat = "{0} 层";
        public const string AuthorNote = "作者的话";
        public const string AuthorNoteBack = "返回按键说明";
        public const string AuthorNotePlaceholder = "此处为「作者的话」预留空间，正式内容撰写中，敬请期待。";
    }

    public static class Overlay
    {
        public const string Confirm = "确认";
        public const string Cancel = "取消";
        public const string SwapYes = "交换黑白";
        public const string SwapNo = "继续执白";
    }

    public static class Settings
    {
        public const string Title = "设置";
        public const string SfxVolume = "音效音量";
        public const string BgmVolume = "音乐音量";
        public const string Mute = "静音";
        public const string MutedOn = "静音：开";
        public const string MutedOff = "静音：关";
        public const string MuteToastOn = "静音已开启";
        public const string MuteToastOff = "静音已关闭";
        public const string WindowMode = "窗口模式";
        public const string ModeWindowed = "窗口化";
        public const string ModeMaximized = "最大化";
        public const string ModeFullscreen = "全屏";
        public const string Resolution = "窗口分辨率";
        public const string UiScale = "界面缩放";
        public const string Language = "语言";
        public const string GameMusic = "对局音乐";
        public const string GameMusicAuto = "自动轮换";
        public const string GameMusicStandard = "标准对局曲";
        public const string GameMusicAdvanced = "进阶对局曲";
        public const string GameMusicOff = "关闭";
        public const string BgmScope = "切屏音乐";
        public const string BgmScopeScene = "随场景切换";
        public const string BgmScopeLock = "锁定当前曲";
    }

    public static class Audio
    {
        public const string HubTitle = "音乐与音效";
        public const string SectionControls = "音量与独立开关";
        public const string SectionLibrary = "背景音乐一览";
        public const string SfxOn = "音效：开";
        public const string SfxOff = "音效：关";
        public const string BgmOn = "音乐：开";
        public const string BgmOff = "音乐：关";
        public const string TestStone = "试听落子";
        public const string TestWin = "试听胜利";
        public const string TestLose = "试听失败";
        public const string NowPlaying = "正在播放：{0}";
        public const string NowStopped = "当前未播放";
        public const string PlayingBadge = "播放中";
        public const string RestoreSceneBgm = "恢复场景曲";
        public const string TrackMenuTitle = "主主题 · 标题屏";
        public const string TrackMenuDesc = "94 BPM · 1:32 · 清透晶体室内乐";
        public const string TrackGameTitle = "标准对局 · 晶格悬停";
        public const string TrackGameDesc = "96 BPM · 2:00 · 轻盈悬停，中高频让位落子声";
        public const string TrackGame2Title = "进阶对局 · 多利亚行进";
        public const string TrackGame2Desc = "106 BPM · 1:49 · Dorian 博弈色彩与轻巧打击乐";
        public const string TrackGuideTitle = "引导与说明 · 柔雾毡钢";
        public const string TrackGuideDesc = "88 BPM · 1:27 · 低干扰阅读光晕";
        public const string TrackPauseTitle = "暂停屏息 · 水下柔雾";
        public const string TrackPauseDesc = "66 BPM · 0:58 · 玻璃罩低通静谧空间";
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
