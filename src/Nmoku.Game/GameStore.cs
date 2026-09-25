using Godot;
using Nmoku.Core;
using Nmoku.Core.AI;
using Nmoku.Core.Saves;
using FileAccess = Godot.FileAccess;

namespace Nmoku.Game;

/// <summary>
/// 游戏壳状态机宿主（M4）：持有 GameState + GameManager（对局内核）+ SaveManager（槽位），
/// 把 01-core §3.4 转移表落成带副作用的动作（重开先确认、读档失败整转移取消）。
/// 快捷键 context 门控见 ShortcutRouter；UI 屏幕由 Main 按状态切换可见性。
/// </summary>
public sealed partial class GameStore : Node
{
    private const string AutotestSaveDir = "user://saves_autotest";

    public GameState State { get; private set; } = GameState.Title;
    public GameManager GM { get; } = new();
    public SaveManager Saves { get; }
    public IOverlayManager Overlay { get; set; } = null!;
    public bool IsDarkTheme { get; set; } = true;

    /// <summary>T 键主题切换：EventBus 通知壳层换肤（视图/背板/壳屏重建）。</summary>
    public void ToggleTheme()
    {
        IsDarkTheme = !IsDarkTheme;
        EventBus.EmitThemeToggled(IsDarkTheme);
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

    public override void _Ready() => AddChild(GM); // GM 与壳同生命周期，入树后 CallDeferred 可用

    /// <summary>启动时装载槽位（小文件同步可接受，保证 Title 界面 L 键即刻可用）。</summary>
    public void InitializeSaves()
    {
        Saves.InitAsync().GetAwaiter().GetResult();
        EventBus.SaveUpdated += _ => SavesChanged?.Invoke();
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
    /// 标题/引导的"开始游戏"：新局。存在进行中的保留局时先确认覆盖
    /// （对齐 §3.4 "Confirms restart" 精神），确认后重置棋盘再 TITLE→PLAYING。
    /// </summary>
    public void RequestStartNew()
    {
        if (HasLiveGame)
        {
            Overlay.ShowConfirmDialog(Text.Title.Start, Text.Title.NewGameConfirm,
                onYes: () =>
                {
                    GM.Reset();
                    Fire(GameTrigger.StartGame);
                });
            return;
        }
        GM.Reset();
        Fire(GameTrigger.StartGame);
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
        Overlay.ShowConfirmDialog(Text.Menu.Restart, Text.Menu.RestartConfirm,
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
            Overlay?.ShowToast(Text.Save.NoSaveToLoad, isError: true);
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
        if (data == null || !GM.LoadFrom(data, Saves.GetSlotWinLength(index)))
        {
            Overlay?.ShowToast(Text.Save.LoadSlotFailed, isError: true);
            return false;
        }
        ApplySavedTheme(data); // 存档主题随档恢复（SaveData.isDarkTheme）
        var to = GameStateMachine.Transition(State, GameTrigger.LoadLatest);
        if (to == null)
        {
            GD.PrintErr($"GameStore: 非法转移 {State} --LoadLatest--> 已拒绝");
            return false;
        }
        SetState(to.Value);
        return true;
    }

    /// <summary>进入复盘（存档槽按钮，任意态）：复盘数据装载失败则整体取消。</summary>
    public bool EnterReplay(int slot)
    {
        var data = Saves.Load(slot);
        if (data == null || !GM.BeginReplay(data, Saves.GetSlotWinLength(slot)))
        {
            Overlay?.ShowToast(Text.Save.LoadSlotFailed, isError: true);
            return false;
        }
        ApplySavedTheme(data);
        var to = GameStateMachine.Transition(State, GameTrigger.EnterReplay);
        if (to == null)
        {
            return false;
        }
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

    public void QuickSave()
    {
        if (State != GameState.Playing)
        {
            return;
        }
        Saves.QuickSaveAsync(GM.BuildSaveData(IsDarkTheme), GM.WinLength).GetAwaiter().GetResult();
        Overlay?.ShowToast(Text.Save.QuickSaved);
    }

    public void DeleteSlot(int index)
    {
        Overlay.ShowConfirmDialog(Text.Save.DeleteTitle,
            string.Format(Text.Save.DeleteConfirm, index + 1),
            onYes: () => Saves.DeleteAsync(index).GetAwaiter().GetResult());
    }

    /// <summary>导出槽位为 Base64(JSON) 到剪贴板（与源浏览器版互通的格式）。</summary>
    public string ExportSlot(int index)
    {
        var data = Saves.Load(index);
        if (data == null)
        {
            Overlay?.ShowToast(Text.Save.LoadSlotFailed, isError: true);
            return "";
        }
        string b64 = SaveData.SerializeToBase64(data);
        DisplayServer.ClipboardSet(b64);
        Overlay?.ShowToast(Text.Save.Exported);
        return b64;
    }

    /// <summary>从剪贴板导入（统一嗅探：裸 JSON / Base64(JSON) / .nzsave 容器）。落入第一个空槽。</summary>
    public void ImportFromClipboard() => ImportText(DisplayServer.ClipboardGet());

    public void ImportText(string input)
    {
        var parsed = SaveArchive.TryParsePackage(input);
        if (parsed == null)
        {
            Overlay?.ShowToast(Text.Save.ImportFailed, isError: true);
            return;
        }
        ImportData(parsed.Data, parsed.WinLength);
    }

    /// <summary>从 .nzsave 文件导入（优先 zip 容器直解，回退文本嗅探）。落入第一个空槽。</summary>
    public void ImportFile(string path)
    {
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (file == null)
        {
            Overlay?.ShowToast(Text.Save.ImportFailed, isError: true);
            return;
        }
        byte[] raw = file.GetBuffer((long)file.GetLength());
        var parsed = NzSaveContainer.TryUnpackPackage(raw)
                     ?? SaveArchive.TryParsePackage(System.Text.Encoding.UTF8.GetString(raw));
        if (parsed == null)
        {
            Overlay?.ShowToast(Text.Save.ImportFailed, isError: true);
            return;
        }
        ImportData(parsed.Data, parsed.WinLength);
    }

    /// <summary>导出槽位为 .nzsave 文件（manifest+savedata zip 容器，manifest 随带外圈 winLength）。</summary>
    public void ExportSlotToFile(int index, string path)
    {
        var data = Saves.Load(index);
        if (data == null)
        {
            Overlay?.ShowToast(Text.Save.LoadSlotFailed, isError: true);
            return;
        }
        byte[] zip = NzSaveContainer.Pack(data, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Saves.GetSlotWinLength(index));
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
        if (file == null)
        {
            Overlay?.ShowToast(Text.Save.ExportFailed, isError: true);
            return;
        }
        file.StoreBuffer(zip);
        Overlay?.ShowToast(Text.Save.ExportedToFile);
    }

    /// <summary>导入落位共用路径：第一个空槽；无空槽 toast。winLength 随槽位外圈元数据保存（B14）。</summary>
    private void ImportData(SaveData parsed, int? winLength)
    {
        for (int i = 0; i < GameConfig.SaveSlotCount; i++)
        {
            if (!Saves.IsSlotEmpty(i))
            {
                continue;
            }
            Saves.SaveAsync(i, parsed, winLength).GetAwaiter().GetResult();
            Overlay?.ShowToast(string.Format(Text.Save.ImportedToSlot, i + 1));
            return;
        }
        Overlay?.ShowToast(Text.Save.ImportNoEmptySlot, isError: true);
    }

    private void SetState(GameState state)
    {
        if (State == state)
        {
            return;
        }
        State = state;
        StateChanged?.Invoke(state);
    }
}
