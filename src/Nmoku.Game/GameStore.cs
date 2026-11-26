using Godot;
using Nmoku.Core;
using Nmoku.Core.AI;
using Nmoku.Core.Saves;
using FileAccess = Godot.FileAccess;
using Nmoku.Game;

namespace Nmoku.Game;

/// <summary>
/// 游戏壳状态机宿主（M4）：持有 GameState + GameManager（对局内核）+ SaveManager（槽位），
/// 把 01-core §3.4 转移表落成带副作用的动作（重开先确认、读档失败整转移取消）。
/// 快捷键 context 门控见 ShortcutRouter；UI 屏幕由 Main 按状态切换可见性。
/// </summary>
public sealed partial class GameStore : Node
{
    /// <summary>自测隔离存档目录（批11 修正：此前主 store 自测也走 user://saves，清槽步骤会毁真实存档）。</summary>
    public const string AutotestSaveDir = "user://saves_autotest";

    public GameState State { get; private set; } = GameState.Title;
    public GameManager GM { get; } = new();
    public SaveManager Saves { get; }
    public IOverlayManager Overlay { get; set; } = null!;
    public bool IsDarkTheme { get; set; } = true;

    /// <summary>用户最近一次 T 键选择的主题（新对局恢复到它，读档带来的主题不进新局）。</summary>
    private bool _themePreference = true;
    /// <summary>启动默认对局上下文（Main 装配后捕获）：开始新对局恢复用。</summary>
    private LevelConfig? _defaultLevel;
    private bool _defaultPve;
    private AIDifficulty _defaultDifficulty;

    /// <summary>T 键主题切换：EventBus 通知壳层换肤（视图/背板/壳屏重建）；同时记为用户偏好。</summary>
    public void ToggleTheme()
    {
        IsDarkTheme = !IsDarkTheme;
        _themePreference = IsDarkTheme;
        EventBus.EmitThemeToggled(IsDarkTheme);
    }

    /// <summary>
    /// 捕获启动默认对局上下文（Main._Ready 完成初始 Configure 后调用）。
    /// 此后「开始新对局」恒恢复它——源 startGame = initGameContext(STANDARD_LEVEL) 同构：
    /// 新局不继承读档/上一局遗留的维度、规则、模式与 AI（人类反馈 2026-09-25）。
    /// </summary>
    public void CaptureDefaultContext(LevelConfig level, bool pve, AIDifficulty difficulty)
    {
        _defaultLevel = level;
        _defaultPve = pve;
        _defaultDifficulty = difficulty;
    }

    /// <summary>状态变化（壳层切换屏幕可见性）。</summary>
    public event Action<GameState>? StateChanged;
    /// <summary>槽位内容变化（存档菜单刷新）。</summary>
    public event Action? SavesChanged;
    /// <summary>关卡装配完成：对局维度（尺寸/层数）可能变化，壳层需重配视图与相机。</summary>
    public event Action? LevelChanged;

    public GameStore() : this(null)
    {
    }

    public GameStore(IStorageAdapter? adapter)
    {
        Saves = new SaveManager(adapter ?? new FileSystemStorageAdapter());
    }

    private bool _saveEventSubscribed;

    public override void _Ready() => AddChild(GM); // GM 与壳同生命周期，入树后 CallDeferred 可用

    public override void _ExitTree()
    {
        if (_saveEventSubscribed)
        {
            EventBus.SaveUpdated -= OnSaveUpdated;
            _saveEventSubscribed = false;
        }
    }

    private void OnSaveUpdated(IReadOnlyList<SaveData?> _) => SavesChanged?.Invoke();

    /// <summary>启动时装载槽位（小文件同步可接受，保证 Title 界面 L 键即刻可用）。</summary>
    public void InitializeSaves()
    {
        Saves.InitAsync().GetAwaiter().GetResult();
        if (!_saveEventSubscribed)
        {
            EventBus.SaveUpdated += OnSaveUpdated;
            _saveEventSubscribed = true;
        }
    }

    // ---- 转移执行：机器把关拓扑合法性，这里补条件与副作用 ----

    /// <summary>通用触发（无载荷）。条件类触发（读档/复盘）请走专用方法。</summary>
    public bool Fire(GameTrigger trigger)
    {
        var to = GameStateMachine.Transition(State, trigger);
        if (to == null)
        {
            GD.PrintErr($"GameStore: 非法转移 {State} --{trigger}--> 已拒绝（01-core §3.4）");
            return false;
        }
        if (to == GameState.Playing && trigger == GameTrigger.NewGame)
        {
            GM.Reset(); // 确认已由调用方先行完成（§3.4 "Confirms restart"）
        }
        SetState(to.Value);
        return true;
    }

    public bool StartGame() => Fire(GameTrigger.StartGame);

    /// <summary>保留现场：切回标题后对局（棋盘/相机/视图）KeepAlive 存活时为真。</summary>
    public bool HasLiveGame => GM.History.Count > 0 && !GM.IsOver && !GM.InReplay;

    /// <summary>
    /// 标题/引导的"开始游戏"：新局，恒恢复启动默认上下文（维度/规则/模式/AI/主题偏好）。
    /// 存在进行中的保留局时先确认覆盖（对齐 §3.4 "Confirms restart" 精神）。
    /// </summary>
    public void RequestStartNew()
    {
        void DoStart()
        {
            if (_defaultLevel != null)
            {
                StartLevel(_defaultLevel, _defaultPve, _defaultDifficulty); // Configure 内含 Reset
            }
            else
            {
                GM.Reset();
                Fire(GameTrigger.StartGame);
            }
            if (IsDarkTheme != _themePreference)
            {
                IsDarkTheme = _themePreference; // 读档带来的主题不进新局（T 偏好保留）
                EventBus.EmitThemeToggled(IsDarkTheme);
            }
        }
        if (HasLiveGame)
        {
            Overlay.ShowConfirmDialog(I18n.T(Text.Title.Start), I18n.T(Text.Title.NewGameConfirm), onYes: DoStart);
            return;
        }
        DoStart();
    }

    /// <summary>标题"继续对局"：不动保留现场，直接 TITLE→PLAYING（§3.4 StartGame 行的恢复语义）。</summary>
    public bool ResumeLive() => Fire(GameTrigger.StartGame);

    /// <summary>关卡启动（沙盒面板/预设）：随 LevelConfig 装配对局后 TITLE→PLAYING。</summary>
    public bool StartLevel(LevelConfig level, bool pve, AIDifficulty difficulty)
    {
        GM.Configure(level, pve, difficulty);
        LevelChanged?.Invoke(); // 维度随档变化：视图/相机必须重配（层数>默认时不重配则不渲染——M4 验收反馈）
        return Fire(GameTrigger.StartGame);
    }

    public bool Pause() => Fire(GameTrigger.Pause);

    public bool Resume() => Fire(GameTrigger.Resume);

    /// <summary>重开请求（PAUSED/GAME_OVER）：确认框通过后走 NewGame。</summary>
    public void RequestRestart()
    {
        Overlay.ShowConfirmDialog(I18n.T(Text.Menu.Restart), I18n.T(Text.Menu.RestartConfirm),
            onYes: () => Fire(GameTrigger.NewGame));
    }

    public bool ReturnToTitle() => Fire(GameTrigger.ReturnToTitle);

    public bool OpenGuide() => Fire(GameTrigger.OpenGuide);

    public bool GuideBack() => Fire(GameTrigger.GuideBack);

    /// <summary>读最新档（Title/Paused）：无档 toast 并取消整个转移（§3.4 条件列）。</summary>
    public bool LoadLatest()
    {
        int idx = Saves.GetLatestIndex();
        if (idx < 0)
        {
            Overlay?.ShowToast(I18n.T(Text.Save.NoSaveToLoad), isError: true);
            return false;
        }
        return LoadSlot(idx);
    }

    /// <summary>存档主题随档恢复（SaveData.isDarkTheme）：与当前不同则经 EventBus 触发壳层换肤。</summary>
    private void ApplySavedTheme(SaveData data)
    {
        if (IsDarkTheme != data.IsDarkTheme)
        {
            IsDarkTheme = data.IsDarkTheme;
            EventBus.EmitThemeToggled(IsDarkTheme);
        }
    }

    public bool LoadSlot(int index)
    {
        var data = Saves.Load(index);
        if (data == null)
        {
            Overlay?.ShowToast(I18n.T(Text.Save.LoadSlotFailed), isError: true);
            return false;
        }
        // 转移合法性先判（修复批 ②-5）：此前先 LoadFrom 覆盖 GM 再判转移，转移非法时留下半加载现场
        var to = GameStateMachine.Transition(State, GameTrigger.LoadLatest);
        if (to == null)
        {
            GD.PrintErr($"GameStore: 非法转移 {State} --LoadLatest--> 已拒绝");
            Overlay?.ShowToast(I18n.T(Text.Save.LoadSlotFailed), isError: true);
            return false;
        }
        if (!TryLoadIntoGame(data, index))
        {
            Overlay?.ShowToast(I18n.T(Text.Save.LoadSlotFailed), isError: true);
            return false;
        }
        ApplySavedTheme(data); // 存档主题随档恢复（SaveData.isDarkTheme）
        SetState(to.Value);
        return true;
    }

    /// <summary>
    /// 把档装进对局内核。<b>异常兜底</b>（修复批 ②-5）：装档失败一律 false，
    /// 不让异常冒泡到 Godot 主循环（此前 renju 规则档会在引擎工厂抛 NotSupportedException）。
    /// </summary>
    private bool TryLoadIntoGame(SaveData data, int index)
    {
        try
        {
            return GM.LoadFrom(data, Saves.GetSlotWinLength(index));
        }
        catch (Exception e)
        {
            GD.PrintErr($"GameStore: 装档失败（槽位 {index + 1}）: {e.Message}");
            return false;
        }
    }

    /// <summary>复盘装档（同 <see cref="TryLoadIntoGame"/>：失败即 false，异常不外冒）。</summary>
    private bool TryBeginReplay(SaveData data, int index)
    {
        try
        {
            return GM.BeginReplay(data, Saves.GetSlotWinLength(index));
        }
        catch (Exception e)
        {
            GD.PrintErr($"GameStore: 复盘装档失败（槽位 {index + 1}）: {e.Message}");
            return false;
        }
    }

    /// <summary>进入复盘（存档槽按钮，任意态）：复盘数据装载失败则整体取消。</summary>
    public bool EnterReplay(int slot)
    {
        var data = Saves.Load(slot);
        if (data == null)
        {
            Overlay?.ShowToast(I18n.T(Text.Save.LoadSlotFailed), isError: true);
            return false;
        }
        // 转移合法性先判（修复批 ②-5）：非法转移不得留下半加载现场
        var to = GameStateMachine.Transition(State, GameTrigger.EnterReplay);
        if (to == null)
        {
            return false;
        }
        if (!TryBeginReplay(data, slot))
        {
            Overlay?.ShowToast(I18n.T(Text.Save.LoadSlotFailed), isError: true);
            return false;
        }
        ApplySavedTheme(data);
        SetState(to.Value);
        return true;
    }

    /// <summary>退出复盘：Replay→Title（清理棋盘，§3.4）。</summary>
    public bool ExitReplay()
    {
        bool ok = Fire(GameTrigger.ExitReplay);
        if (ok)
        {
            GM.Reset();
        }
        return ok;
    }

    /// <summary>胜负判定入状态机（GM.GameOver 事件转发，Playing 之外忽略）。</summary>
    public void NotifyGameOver()
    {
        if (State == GameState.Playing)
        {
            Fire(GameTrigger.WinDetected);
        }
    }

    // ---- 存档动作 ----
    // 写局只走键盘：QuickSave（Ctrl+S，快存槽）与 SaveToFirstEmpty（S 键，首空槽/满则快存槽）。
    // 存档菜单是档案管理面板，不再承担保存当前对局（批9 语义定稿 2026-09-26）。

    public void QuickSave()
    {
        if (State != GameState.Playing)
        {
            return;
        }
        // 写盘失败可见（修复批 ②-2）：此前无视落盘结果一律弹「已快存」= 存档假成功
        bool ok = Saves.QuickSaveAsync(GM.BuildSaveData(IsDarkTheme), GM.WinLength)
            .GetAwaiter().GetResult();
        Overlay?.ShowToast(ok ? I18n.T(Text.Save.QuickSaved) : I18n.T(Text.Save.SaveFailed), isError: !ok);
    }

    /// <summary>
    /// 读取快存槽（Ctrl+L，quick-save 的另一半）：暂停态生效（§3.4 读档仅 Title/Paused 合法，
    /// 对局中先 Esc），读回即回对局；空槽 toast 并取消整个转移。
    /// </summary>
    public bool QuickLoad()
    {
        if (State != GameState.Paused)
        {
            return false;
        }
        if (Saves.IsSlotEmpty(GameConfig.QuickSaveIndex))
        {
            Overlay?.ShowToast(I18n.T(Text.Save.NoQuickSave), isError: true);
            return false;
        }
        return LoadSlot(GameConfig.QuickSaveIndex);
    }

    /// <summary>保存进度（源 saveGame 语义，S 键）：落入首个空槽，全满则覆盖快存槽；不弹确认。</summary>
    public void SaveToFirstEmpty()
    {
        int target = -1;
        for (int i = 0; i < GameConfig.SaveSlotCount; i++)
        {
            if (Saves.IsSlotEmpty(i))
            {
                target = i;
                break;
            }
        }
        if (target < 0)
        {
            target = GameConfig.QuickSaveIndex;
        }
        SaveToSlot(target);
    }

    /// <summary>保存当前对局到指定槽位（S 键落槽路径与自测造档用；占用覆盖不确认，源 saveGame 语义）。</summary>
    public void SaveToSlot(int index)
    {
        bool ok = Saves.SaveAsync(index, GM.BuildSaveData(IsDarkTheme), GM.WinLength)
            .GetAwaiter().GetResult();
        Overlay?.ShowToast(ok
            ? I18n.T(Text.Save.SavedToSlot, index + 1)
            : I18n.T(Text.Save.SaveFailed), isError: !ok);
    }

    public void DeleteSlot(int index)
    {
        Overlay.ShowConfirmDialog(I18n.T(Text.Save.DeleteTitle),
            I18n.T(Text.Save.DeleteConfirm, index + 1),
            onYes: () =>
            {
                bool ok = Saves.DeleteAsync(index).GetAwaiter().GetResult();
                if (!ok)
                {
                    Overlay?.ShowToast(I18n.T(Text.Save.DeleteFailed), isError: true);
                }
            });
    }

    /// <summary>复制槽位为 Base64(JSON) 到剪贴板（菜单「复制」；与源浏览器版互通的格式）。</summary>
    public string ExportSlot(int index)
    {
        var data = Saves.Load(index);
        if (data == null)
        {
            Overlay?.ShowToast(I18n.T(Text.Save.LoadSlotFailed), isError: true);
            return "";
        }
        string b64 = SaveData.SerializeToBase64(data);
        DisplayServer.ClipboardSet(b64);
        Overlay?.ShowToast(I18n.T(Text.Save.Copied));
        return b64;
    }

    /// <summary>
    /// 剪贴板 → 指定槽位（菜单空槽「填写」/占用「覆盖」）。与「复制」构成剪贴板搬运闭环：
    /// 导出到剪贴板后可写回任意指定槽位（批9 语义定稿）。
    /// </summary>
    public void FillSlotFromClipboard(int index) => FillSlot(index, DisplayServer.ClipboardGet());

    /// <summary>文本 → 指定槽位（填写/覆盖的解析主体；剪贴板与自测等文本源共用）。解析失败 toast。</summary>
    public void FillSlot(int index, string input)
    {
        var parsed = SaveArchive.TryParsePackage(input);
        if (parsed == null)
        {
            Overlay?.ShowToast(I18n.T(Text.Save.ImportFailed), isError: true);
            return;
        }
        WriteImportToSlot(index, parsed!, confirmIfOccupied: true);
    }

    /// <summary>从 .nzsave 文件导入到指定槽位（空槽行「导入」）。</summary>
    public void ImportFileToSlot(string path, int index)
    {
        var parsed = ParseSaveFile(path);
        if (parsed == null)
        {
            Overlay?.ShowToast(I18n.T(Text.Save.ImportFileFailed), isError: true);
            return;
        }
        WriteImportToSlot(index, parsed!, confirmIfOccupied: true);
    }

    /// <summary>
    /// 统一文件导入（头部「导入」）：落入第一个空槽；全满则覆盖最后一个非快存槽（槽 5）并确认
    /// ——快存槽永不被动覆盖（语义定稿 2026-09-26）。
    /// </summary>
    public void ImportFile(string path)
    {
        var parsed = ParseSaveFile(path);
        if (parsed == null)
        {
            Overlay?.ShowToast(I18n.T(Text.Save.ImportFileFailed), isError: true);
            return;
        }
        ImportData(parsed!);
    }

    /// <summary>文本导入（Base64/JSON 嗅探；旧档恢复与自测路径）。落位同统一导入。</summary>
    public void ImportText(string input)
    {
        var parsed = SaveArchive.TryParsePackage(input);
        if (parsed == null)
        {
            Overlay?.ShowToast(I18n.T(Text.Save.ImportFailed), isError: true);
            return;
        }
        ImportData(parsed!);
    }

    private NzSavePackage? ParseSaveFile(string path)
    {
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (file == null)
        {
            return null;
        }
        byte[] raw = file.GetBuffer((long)file.GetLength());
        return NzSaveContainer.TryUnpackPackage(raw)
               ?? SaveArchive.TryParsePackage(System.Text.Encoding.UTF8.GetString(raw));
    }

    /// <summary>统一导入落位：第一个空槽；全满则覆盖最后一个非快存槽（槽 5）并确认（快存槽保护）。</summary>
    private void ImportData(NzSavePackage package)
    {
        int target = -1;
        for (int i = 0; i < GameConfig.SaveSlotCount; i++)
        {
            if (Saves.IsSlotEmpty(i))
            {
                target = i;
                break;
            }
        }
        if (target < 0)
        {
            target = GameConfig.SaveSlotCount - 2; // 最后一个非快存槽位
        }
        WriteImportToSlot(target, package, confirmIfOccupied: true);
    }

    /// <summary>导入写入指定槽位：空槽直写；占用先确认（headless 语义由 m4c-overlay-contract 覆盖）。</summary>
    private void WriteImportToSlot(int index, NzSavePackage package, bool confirmIfOccupied)
    {
        void DoWrite()
        {
            bool ok = Saves.SaveAsync(index, package.Data, package.WinLength).GetAwaiter().GetResult();
            Overlay?.ShowToast(ok
                ? I18n.T(Text.Save.ImportedToSlot, index + 1)
                : I18n.T(Text.Save.SaveFailed), isError: !ok);
        }
        if (!confirmIfOccupied || Saves.IsSlotEmpty(index))
        {
            DoWrite();
            return;
        }
        Overlay.ShowConfirmDialog(I18n.T(Text.Save.OverwriteTitle),
            I18n.T(Text.Save.OverwriteConfirm, index + 1), onYes: DoWrite);
    }

    /// <summary>导出槽位为 .nzsave 文件（manifest+savedata zip 容器，manifest 随带外圈 winLength）。</summary>
    public void ExportSlotToFile(int index, string path)
    {
        var data = Saves.Load(index);
        if (data == null)
        {
            Overlay?.ShowToast(I18n.T(Text.Save.LoadSlotFailed), isError: true);
            return;
        }
        byte[] zip = NzSaveContainer.Pack(data, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Saves.GetSlotWinLength(index));
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
        if (file == null)
        {
            Overlay?.ShowToast(I18n.T(Text.Save.ExportFailed), isError: true);
            return;
        }
        file.StoreBuffer(zip);
        Overlay?.ShowToast(I18n.T(Text.Save.ExportedToFile));
    }

    private void SetState(GameState state)
    {
        if (State == state)
        {
            return;
        }
        State = state;
        GD.Print($"[state] → {state}"); // 批11 诊断：与 wintrace 窗口几何变化做时间关联
        StateChanged?.Invoke(state);
    }
}
