using Godot;
using Nmoku.Core;
using Nmoku.Core.AI;
using Nmoku.Core.Camera;
using Nmoku.Core.Saves;
using Nmoku.Game;
using Nmoku.Game.Shell;
using Nmoku.Game.Theme;
using Nmoku.Game.Views;

/// <summary>
/// M4 场景根：分屏对局视图（M3）+ 游戏壳——GameStore 持有 7 态状态机（01-core §3.4），
/// OverlayManager 承接模态/toast，六屏（标题/暂停/结算/存档/引导/复盘条）按状态切换可见性。
/// 悬停唯一来源是 2D 面板；相机自由交互保留 M2 方案（决策 #9）。
/// 命令行参数：--board=13 --layers=6 --spacing=3.5 --rules=gomoku|swap2 --mode=pvp|pve --ai=easy|medium|hard
/// </summary>
public partial class Main : Node
{
    private const double OrbitDegPerPixel = 0.25;
    private const double ZoomPerSecond = 3.0;
    private const double SpacingPerSecond = 1.2;
    private const double PanPerSecond = 0.6; // ×当前距离，键盘平移速度

    private SubViewport _viewport3D = null!;
    private Camera3D _camera = null!;
    private Board3DView _board3D = null!;
    private Board2DView _board2D = null!;
    private GameStore _store = null!;
    private GameManager _gm = null!;
    private CameraRig _rig = null!;
    private GameScreen _gameScreen = null!;
    private OverlayManager _overlay = null!;
    private TitleScreen _titleScreen = null!;
    private PauseMenu _pauseMenu = null!;
    private GameOverModal _gameOverModal = null!;
    private SaveMenu _saveMenu = null!;
    private GuideScreen _guideScreen = null!;
    private ReplayBar _replayBar = null!;
    private Label _statusLabel = null!;

    private readonly int _boardSize;
    private readonly int _layers;
    private double _spacing;
    private RuleSet _rules = RuleSet.Gomoku;
    private bool _pve;
    private AIDifficulty _aiDifficulty = AIDifficulty.Medium;
    private Vector2 _lastViewportSize;
    private bool _rotating;
    private bool _panning;

    public Main()
    {
        (_boardSize, _layers, _spacing, _rules, _pve, _aiDifficulty) = ParseDebugArgs();
        if (OS.GetCmdlineUserArgs().Contains("--autotest"))
        {
            _autotestArmed = true;
        }
    }

    public override void _Ready()
    {
        // F1 帮助屏 action 运行时注册（KeymapTable 已登记语义；不进 project.godot 以免 --import 回写风险）
        if (!InputMap.HasAction("toggle_help"))
        {
            InputMap.AddAction("toggle_help");
            InputMap.ActionAddEvent("toggle_help", new InputEventKey { PhysicalKeycode = Key.F1 });
        }

        int custom = 0;
        foreach (string action in InputMap.GetActions())
        {
            if (!action.StartsWith("ui_"))
                custom++;
        }
        GD.Print($"Nmoku M0: engine boot OK (custom input actions={custom}, audio buses={AudioServer.BusCount})");

        BuildLayout();
        BuildShell();

        _store = new GameStore { Name = "GameStore" };
        AddChild(_store); // 触发 GameStore._Ready → GM 入树
        _store.Overlay = _overlay;
        _gm = _store.GM;
        _gm.LayerSpacing = _spacing;
        _gm.Configure(_boardSize, _layers, _rules, _pve, _aiDifficulty);
        _board2D.Game = _gm; // 2D 面板状态源（此前遗漏导致右半屏空白）
        Wire2DPanel();
        WireShell();
        _store.InitializeSaves();

        _lastViewportSize = _viewport3D.GetVisibleRect().Size;
        _rig = new CameraRig(_boardSize, _layers, _spacing, _lastViewportSize.X / _lastViewportSize.Y);
        ApplyCamera();

        ShowState(_store.State);
        Refresh();
        GD.Print($"Nmoku M4: game ready (board={_boardSize}x{_boardSize}x{_layers}, spacing={_spacing:0.0}, " +
                 $"rules={_rules}, mode={(_pve ? "pve" : "pvp")}, distance={_rig.Current.Distance:0.00})");
    }

    private void BuildLayout()
    {
        // 对局基座组件化（M4R：SceneRouter 挂载入口），字段引用保持不变以最小化接线改动
        _gameScreen = new GameScreen(_boardSize, _layers, _spacing);
        AddChild(_gameScreen);
        _viewport3D = _gameScreen.Viewport3D;
        _camera = _gameScreen.Camera;
        _board3D = _gameScreen.Board3D;
        _board2D = _gameScreen.Board2D;
        _statusLabel = _gameScreen.StatusLabel;
    }

    /// <summary>OverlayManager（layer 20）+ 六个壳屏（layer 10，默认全隐藏，ShowState 按态切换）。</summary>
    private void BuildShell()
    {
        _overlay = new OverlayManager();
        AddChild(_overlay);

        _titleScreen = new TitleScreen { Visible = false };
        _pauseMenu = new PauseMenu { Visible = false };
        _gameOverModal = new GameOverModal { Visible = false };
        _saveMenu = new SaveMenu { Visible = false };
        _guideScreen = new GuideScreen { Visible = false };
        _replayBar = new ReplayBar { Visible = false };

        var uiLayer = new CanvasLayer { Layer = 10 };
        AddChild(uiLayer);
        uiLayer.AddChild(_titleScreen);
        uiLayer.AddChild(_pauseMenu);
        uiLayer.AddChild(_gameOverModal);
        uiLayer.AddChild(_saveMenu);
        uiLayer.AddChild(_guideScreen);
        uiLayer.AddChild(_replayBar);
    }

    private void Wire2DPanel()
    {
        _board2D.HoverChanged += cell => _gm.SetHover(cell);
        _board2D.PlaceRequested += cell => _gm.TryPlace(cell.X, cell.Y, cell.Z);
    }

    private void WireShell()
    {
        // ---- 壳屏意图 → GameStore 动作 ----
        _titleScreen.StartRequested += () => _store.StartGame();
        _titleScreen.LoadRequested += OpenSaveMenu;
        _titleScreen.GuideRequested += () => _store.OpenGuide();
        _titleScreen.QuitRequested += () => GetTree().Quit();

        _pauseMenu.ResumeRequested += () => _store.Resume();
        _pauseMenu.RestartRequested += () => _store.RequestRestart();
        _pauseMenu.SaveMenuRequested += OpenSaveMenu;
        _pauseMenu.GuideRequested += () => _store.OpenGuide();
        _pauseMenu.TitleRequested += () => _store.ReturnToTitle();

        _gameOverModal.RestartRequested += () => _store.RequestRestart();
        _gameOverModal.TitleRequested += () => _store.ReturnToTitle();

        _saveMenu.LoadSlotRequested += i => { CloseSaveMenu(); _store.LoadSlot(i); };
        _saveMenu.ReplaySlotRequested += i => { CloseSaveMenu(); _store.EnterReplay(i); };
        _saveMenu.ExportSlotRequested += i => _store.ExportSlot(i);
        _saveMenu.DeleteSlotRequested += i => _store.DeleteSlot(i);
        _saveMenu.ImportRequested += () => _store.ImportFromClipboard();
        _saveMenu.CloseRequested += CloseSaveMenu;

        _guideScreen.StartRequested += () => _store.StartGame();
        _guideScreen.ResumeRequested += () => _store.Resume();
        _guideScreen.BackRequested += () => _store.GuideBack();

        _replayBar.StepRequested += d => _gm.StepReplay(d);
        _replayBar.SeekRequested += i => _gm.SetReplayIndex(i);
        _replayBar.ExitRequested += () => _store.ExitReplay();

        // ---- 状态机 / 对局事件 → 壳 ----
        _store.StateChanged += ShowState;
        _store.SavesChanged += () =>
        {
            if (_saveMenu.Visible)
            {
                _saveMenu.RefreshSlots(_store.Saves.GetSlots());
            }
        };
        _gm.GameOver += _ => _store.NotifyGameOver();
        _gm.SwapPhaseStarted += ShowSwapDialog;
        _gm.StateChanged += Refresh;
        _gm.FocusChanged += _ => Refresh();
        _gm.HoverChanged += OnHoverChanged;
    }

    public override void _Process(double delta)
    {
        if (_autotestArmed)
        {
            RunAutotest();
            return;
        }
        HandleHeldInput(delta);
        HandleViewportChange();
        _rig.Advance(delta);
        ApplyCamera();
    }

    // ---- 无头对局自测（-- --autotest）：M3 对局闭环 + M4 状态机/存档/复盘；CI 冒烟依赖 AUTOTEST ALL PASS ----
    private bool _autotestArmed;
    private bool _autotestBuilt;
    private readonly Queue<(string Name, Func<string> Run)> _autotestSteps = new();
    private string _autotestFails = "";
    private int _autotestWait;

    private void RunAutotest()
    {
        if (!_autotestBuilt)
        {
            _autotestBuilt = true;
            BuildAutotestSteps();
        }
        if (_autotestSteps.Count == 0)
        {
            GD.Print(_autotestFails.Length == 0 ? "AUTOTEST ALL PASS" : $"AUTOTEST FAIL: {_autotestFails}");
            GetTree().Quit();
            return;
        }
        var (name, run) = _autotestSteps.Peek();
        var result = run();
        if (result == "wait")
        {
            return;
        }
        _autotestSteps.Dequeue();
        if (result != "ok")
        {
            _autotestFails += $"[{name}: {result}] ";
        }
        GD.Print($"AUTOTEST {name}: {result}");
    }

    private void BuildAutotestSteps()
    {
        // PvP 五连胜利 + 悔棋
        _gm.Configure(9, 3, RuleSet.Gomoku, false, AIDifficulty.Medium);
        (int x, int y)[] black = [(0, 5), (1, 5), (2, 5), (3, 5), (4, 5)];
        (int x, int y)[] white = [(0, 6), (1, 6), (2, 6), (3, 6)];
        for (int k = 0; k < 4; k++)
        {
            var b = black[k];
            _autotestSteps.Enqueue(("pvp-black" + k, () => _gm.TryPlace(b.x, b.y, 0) ? "ok" : "FAIL"));
            var w = white[k];
            _autotestSteps.Enqueue(("pvp-white" + k, () => _gm.TryPlace(w.x, w.y, 0) ? "ok" : "FAIL"));
        }
        var lastBlack = black[4];
        _autotestSteps.Enqueue(("pvp-black4", () => _gm.TryPlace(lastBlack.x, lastBlack.y, 0) ? "ok" : "FAIL"));
        _autotestSteps.Enqueue(("pvp-win", () =>
            _gm.IsOver && _gm.Winner == 1 ? "ok" : "FAIL"));
        _autotestSteps.Enqueue(("undo", () =>
        {
            _gm.Undo();
            return !_gm.IsOver && _gm.History.Count == 8 && _gm.Current == PlayerColor.Black ? "ok" : "FAIL";
        }));
        _autotestSteps.Enqueue(("reset", () =>
        {
            _gm.Reset();
            return !_gm.IsOver && _gm.History.Count == 0 ? "ok" : "FAIL";
        }));

        // SWAP2 pass 分支：黑首子 → 白决策期 → 继续 → 黑连下第 2 手
        _autotestSteps.Enqueue(("cfg-swap2", () =>
        {
            _gm.Configure(9, 3, RuleSet.Swap2, false, AIDifficulty.Medium);
            return "ok";
        }));
        _autotestSteps.Enqueue(("swap2-first", () => _gm.TryPlace(4, 4, 0) ? "ok" : "FAIL"));
        _autotestSteps.Enqueue(("swap2-pending", () => _gm.SwapPending ? "ok" : "FAIL"));
        _autotestSteps.Enqueue(("swap2-pass", () =>
        {
            _gm.ResolveSwap(false);
            return !_gm.SwapPending && _gm.Current == PlayerColor.Black ? "ok" : "FAIL";
        }));
        _autotestSteps.Enqueue(("swap2-black2", () =>
            _gm.TryPlace(5, 4, 0) && _gm.Current == PlayerColor.White ? "ok" : "FAIL"));

        // SWAP2 swap 分支：决策后全历史换色
        _autotestSteps.Enqueue(("cfg-swap2b", () =>
        {
            _gm.Configure(9, 3, RuleSet.Swap2, false, AIDifficulty.Medium);
            return "ok";
        }));
        _autotestSteps.Enqueue(("swap2b-first", () => _gm.TryPlace(4, 4, 0) ? "ok" : "FAIL"));
        _autotestSteps.Enqueue(("swap2b-swap", () =>
        {
            _gm.ResolveSwap(true);
            return _gm.History[0].Player == PlayerColor.White && _gm.Board.Get(4, 4, 0) == CellState.White
                ? "ok"
                : "FAIL";
        }));
        _autotestSteps.Enqueue(("swap2b-black2", () => _gm.TryPlace(3, 3, 0) ? "ok" : "FAIL"));

        // PvE：人类落子后 AI 应答
        _autotestSteps.Enqueue(("cfg-pve", () =>
        {
            _gm.Configure(9, 3, RuleSet.Gomoku, true, AIDifficulty.Medium);
            return "ok";
        }));
        _autotestSteps.Enqueue(("pve-human", () => _gm.TryPlace(4, 4, 1) ? "ok" : "FAIL"));
        _autotestSteps.Enqueue(("pve-ai-reply", () =>
        {
            _autotestWait++;
            if (_autotestWait > 600)
            {
                return "FAIL timeout";
            }
            return _gm.History.Count == 2 ? "ok" : "wait";
        }));

        // ---- M4：状态机 + 存档往返 + 复盘（隔离目录 user://saves_autotest，不污染真实存档）----
        _autotestSteps.Enqueue(("m4-cfg", () =>
        {
            _gm.Configure(9, 3, RuleSet.Gomoku, false, AIDifficulty.Medium);
            _gm.TryPlace(4, 4, 0);
            _gm.TryPlace(3, 3, 0);
            return _gm.History.Count == 2 ? "ok" : "FAIL";
        }));
        _autotestSteps.Enqueue(("m4-start", () =>
            _store.StartGame() && _store.State == GameState.Playing ? "ok" : "FAIL"));
        _autotestSteps.Enqueue(("m4-pause", () =>
            _store.Pause() && _store.State == GameState.Paused ? "ok" : "FAIL"));
        _autotestSteps.Enqueue(("m4-illegal-reject", () =>
            !_store.Fire(GameTrigger.Pause) && _store.State == GameState.Paused ? "ok" : "FAIL"));
        _autotestSteps.Enqueue(("m4-resume", () =>
            _store.Resume() && _store.State == GameState.Playing ? "ok" : "FAIL"));
        _autotestSteps.Enqueue(("m4-save-load-replay", AutotestSaveLoadReplay));
        _autotestSteps.Enqueue(("m4-win-gameover", () =>
        {
            // 承 m4-cfg（黑 (4,4)，白 (3,3)）：黑在 y=5 凑五连
            (int x, int y)[] b = [(0, 5), (1, 5), (2, 5), (3, 5), (4, 5)];
            (int x, int y)[] w = [(0, 6), (1, 6), (2, 6), (3, 6)];
            for (int k = 0; k < 4; k++)
            {
                if (!_gm.TryPlace(b[k].x, b[k].y, 0) || !_gm.TryPlace(w[k].x, w[k].y, 0))
                {
                    return "FAIL place";
                }
            }
            return _gm.TryPlace(b[4].x, b[4].y, 0)
                && _gm.IsOver && _gm.Winner == 1
                && _store.State == GameState.GameOver ? "ok" : "FAIL";
        }));
        _autotestSteps.Enqueue(("m4-gameover-newgame", () =>
            _store.Fire(GameTrigger.NewGame) && _store.State == GameState.Playing && _gm.History.Count == 0
                ? "ok"
                : "FAIL"));
        _autotestSteps.Enqueue(("m4-back-to-title", () =>
            _store.Pause() && _store.ReturnToTitle() && _store.State == GameState.Title ? "ok" : "FAIL"));
        _autotestSteps.Enqueue(("m4-import", AutotestImportClipboardPath));

        // ---- M4R：首中即吞回归（同键跨态双触发根因：修复前 ESC 同帧 pause+resume 互抵）----
        _autotestSteps.Enqueue(("m4r-start", () =>
        {
            _gm.Configure(9, 3, RuleSet.Gomoku, false, AIDifficulty.Medium);
            return _store.StartGame() && _store.State == GameState.Playing ? "ok" : "FAIL";
        }));
        _autotestSteps.Enqueue(("m4r-pause-stays", () =>
        {
            SimulateActionPress("pause");
            return _store.State == GameState.Paused ? "ok" : "FAIL";
        }));
        _autotestSteps.Enqueue(("m4r-restart-opens-confirm", () =>
        {
            SimulateActionPress("restart"); // R 在 Paused=重开请求：应只弹确认框
            return _overlay.IsModalOpen ? "ok" : "FAIL";
        }));
        _autotestSteps.Enqueue(("m4r-cancel-confirm", () =>
        {
            _overlay.CloseAll();
            return !_overlay.IsModalOpen && _store.State == GameState.Paused ? "ok" : "FAIL";
        }));
        _autotestSteps.Enqueue(("m4r-resume", () =>
        {
            SimulateActionPress("resume");
            return _store.State == GameState.Playing ? "ok" : "FAIL";
        }));
    }

    /// <summary>M4 自测：存档落盘 → 模拟重启恢复 → 载入转 Playing → 复盘步进 → 退出回 Title。</summary>
    private string AutotestSaveLoadReplay()
    {
        var sm = new SaveManager(new FileSystemStorageAdapter("user://saves_autotest"));
        sm.InitAsync().GetAwaiter().GetResult();
        var data = _gm.BuildSaveData(true);
        sm.SaveAsync(2, data).GetAwaiter().GetResult();

        // 模拟重启：全新 store 从磁盘恢复槽位
        var reborn = new GameStore(new FileSystemStorageAdapter("user://saves_autotest"));
        reborn.InitializeSaves();
        var loaded = reborn.Saves.Load(2);
        bool saved = loaded != null && loaded.Id == data.Id && loaded.Moves.Count == data.Moves.Count
            && reborn.Saves.GetLatestIndex() == 2;

        // 载入（Title→Playing）→ 复盘步进 → 退出回 Title（§3.4 REPLAY 行）
        bool flow = reborn.LoadSlot(2) && reborn.State == GameState.Playing
            && reborn.GM.History.Count == data.Moves.Count
            && reborn.GM.Board.MoveCount() == data.Moves.Count
            && reborn.EnterReplay(2) && reborn.State == GameState.Replay
            && reborn.GM.Board.MoveCount() == 0;
        reborn.GM.StepReplay(1);
        flow = flow && reborn.GM.ReplayIndex == 1 && reborn.GM.Board.MoveCount() == 1;
        flow = flow && reborn.ExitReplay() && reborn.State == GameState.Title;

        sm.DeleteAsync(2).GetAwaiter().GetResult();
        return saved && flow ? "ok" : "FAIL";
    }

    /// <summary>M4 自测：Base64 导入路径（统一嗅探入口）落入首个空槽。</summary>
    private string AutotestImportClipboardPath()
    {
        var data = _gm.BuildSaveData(false);
        var isoStore = new GameStore(new FileSystemStorageAdapter("user://saves_autotest"));
        isoStore.InitializeSaves();
        isoStore.ImportText(SaveData.SerializeToBase64(data));
        int filled = -1;
        for (int i = 0; i < GameConfig.SaveSlotCount; i++)
        {
            if (!isoStore.Saves.IsSlotEmpty(i))
            {
                filled = i;
                break;
            }
        }
        bool ok = filled == 0 && isoStore.Saves.Load(0)!.Id == data.Id;
        if (filled >= 0)
        {
            isoStore.Saves.DeleteAsync(filled).GetAwaiter().GetResult();
        }
        return ok ? "ok" : "FAIL";
    }

    public override void _Input(InputEvent @event)
    {
        // 相机鼠标交互仅在 PLAYING/REPLAY 开放（§4.2.3 context 语义）
        bool cameraAllowed = _store != null && _store.State is GameState.Playing or GameState.Replay;
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } button:
                _rotating = button.Pressed && cameraAllowed && !IsOver2DPanel();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Middle } middle:
                _panning = middle.Pressed && cameraAllowed && !IsOver2DPanel();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true }:
                // 径向缩放：与 W/S 同路径，单调无跳变（zoom-to-anchor 待拾取恢复）
                if (cameraAllowed && !IsOver2DPanel()) _rig.Zoom(1);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true }:
                if (cameraAllowed && !IsOver2DPanel()) _rig.Zoom(-1);
                break;
            case InputEventMouseMotion motion:
            {
                if (_rotating)
                {
                    // 左右方向人类已确认；上下已按反馈翻转（2026-09-19）
                    _rig.Orbit(motion.Relative.X * OrbitDegPerPixel, motion.Relative.Y * OrbitDegPerPixel);
                }
                else if (_panning)
                {
                    double worldPerPixel = WorldPerPixel();
                    _rig.Pan(-motion.Relative.X * worldPerPixel, -motion.Relative.Y * worldPerPixel);
                }
                break;
            }
        }
    }

    /// <summary>context 门控后的单次触发（ShortcutRouter 拒绝则不产生效果）。</summary>
    private bool Want(string action) =>
        ShortcutRouter.Allows(action, _store.State) && Input.IsActionJustPressed(action, true);

    /// <summary>context 门控后的持续按住（相机/间距类）。</summary>
    private bool Held(string action) =>
        ShortcutRouter.Allows(action, _store.State) && Input.IsActionPressed(action, true);

    /// <summary>
    /// 有序单次动作表（顺序即优先级）。HandleHeldInput 首中即吞；autotest 经
    /// SimulateActionPress 复用同一分发逻辑，保证测试与真实输入同路径。
    /// </summary>
    private IEnumerable<(string Action, Action Run)> SingleActions()
    {
        yield return ("pause", () => _store.Pause());
        yield return ("resume", () => _store.Resume());
        yield return ("confirm", ConfirmAction);
        yield return ("load_latest", () => _store.LoadLatest());
        yield return ("quick_save", () => _store.QuickSave());
        yield return ("restart", () => _store.RequestRestart());
        yield return ("return_to_title", () => _store.ReturnToTitle());
        yield return ("open_guide", () => _store.OpenGuide());
        yield return ("replay_prev", () => _gm.StepReplay(-1));
        yield return ("replay_next", () => _gm.StepReplay(1));
        yield return ("exit_replay", () => _store.ExitReplay());
        yield return ("layer_prev", () => _gm.CycleFocus(-1));
        yield return ("layer_next", () => _gm.CycleFocus(1));
        yield return ("mirror_2d", () => _board2D.ToggleMirror());
        yield return ("toggle_helper_lines", () => _gm.ToggleAux());
        yield return ("toggle_colorblind", () => _gm.ToggleColorblind());
        yield return ("toggle_grid_all", () => _board3D.ToggleGridAll());
        yield return ("undo", () => _gm.Undo());
    }

    private void ConfirmAction()
    {
        switch (_store.State)
        {
            case GameState.Title:
            case GameState.GuideFromTitle:
                _store.StartGame();
                break;
            case GameState.GuideFromGame:
                _store.Resume();
                break;
        }
    }

    private void HandleHeldInput(double delta)
    {
        if (_overlay.IsModalOpen)
        {
            return; // 模态框吞掉一切游戏快捷键（Esc/Enter 由对话框自身处理，§4.1）
        }
        if (_saveMenu.Visible)
        {
            if (Input.IsActionJustPressed("ui_cancel"))
            {
                CloseSaveMenu();
            }
            return;
        }

        // 引导态 Esc（§3.4 两行的 Esc 语义）：标题入口=返回，对局入口=回对局
        if (_store.State is GameState.GuideFromTitle or GameState.GuideFromGame
            && Input.IsActionJustPressed("ui_cancel"))
        {
            if (_store.State == GameState.GuideFromGame)
            {
                _store.Resume();
            }
            else
            {
                _store.GuideBack();
            }
            return;
        }

        // ---- 单次动作：首中即吞（04-contracts §4.2.4"匹配即吞掉事件"）----
        // 每帧至多执行一个：状态在同帧变化后不再求值后续动作，
        // 根除 ESC(pause+resume 同帧互抵) 与 R(pause+restart 同帧双弹) 的双触发。
        foreach (var (action, run) in SingleActions())
        {
            if (Want(action))
            {
                run();
                return;
            }
        }

        // ---- 相机/间距：持续按住类，不参与首中互斥（exact match 防 Shift 平移串线，M2 决策 #9）----
        if (Held("rotate_left"))
        {
            _rig.Orbit(-90);
        }
        if (Held("rotate_right"))
        {
            _rig.Orbit(90);
        }
        if (Held("zoom_in"))
        {
            _rig.Zoom(ZoomPerSecond * delta);
        }
        if (Held("zoom_out"))
        {
            _rig.Zoom(-ZoomPerSecond * delta);
        }

        double panStep = PanPerSecond * delta * _rig.Current.Distance;
        if (Held("pan_left"))
        {
            _rig.Pan(-panStep, 0);
        }
        if (Held("pan_right"))
        {
            _rig.Pan(panStep, 0);
        }
        if (Held("pan_up"))
        {
            _rig.Pan(0, panStep);
        }
        if (Held("pan_down"))
        {
            _rig.Pan(0, -panStep);
        }

        double before = _spacing;
        if (Held("spacing_compress"))
        {
            _spacing -= SpacingPerSecond * delta;
        }
        if (Held("spacing_expand"))
        {
            _spacing += SpacingPerSecond * delta;
        }
        _spacing = Math.Clamp(_spacing, GameConfig.MinLayerSpacing, GameConfig.MaxLayerSpacing);
        if (Want("spacing_reset"))
        {
            _spacing = GameConfig.DefaultLayerSpacing;
            _rig.Reset(_boardSize, _layers, _spacing, ViewportAspect());
        }
        if (Math.Abs(_spacing - before) > 1e-9)
        {
            OnSpacingChanged();
        }
    }

    /// <summary>autotest 专用：绕过 Input 单例直接驱动单次动作分发（与真实路径共用有序表与门控）。</summary>
    private void SimulateActionPress(string action)
    {
        foreach (var (candidate, run) in SingleActions())
        {
            if (candidate == action)
            {
                if (ShortcutRouter.Allows(action, _store.State))
                {
                    run();
                }
                return;
            }
        }
    }

    private void OnSpacingChanged()
    {
        _board3D.Configure(_boardSize, _layers, _spacing);
        _rig.Update(_boardSize, _layers, _spacing, ViewportAspect(), GameConfig.DefaultYaw);
        _gm.LayerSpacing = _spacing; // 存档快照字段随动
        Refresh();
    }

    private void HandleViewportChange()
    {
        // 按尺寸比较而非宽高比：杜绝浮点抖动触发公式重算（距离被拉回初始态的疑似来源之一）
        var size = _viewport3D.GetVisibleRect().Size;
        if (_lastViewportSize != size)
        {
            _lastViewportSize = size;
            _rig.Update(_boardSize, _layers, _spacing, size.X / size.Y, GameConfig.DefaultYaw);
        }
    }

    private void ApplyCamera()
    {
        var s = _rig.Current;
        _camera.Position = new Vector3((float)s.Position.X, (float)s.Position.Y, (float)s.Position.Z);
        _camera.LookAt(new Vector3((float)s.Center.X, (float)s.Center.Y, (float)s.Center.Z), Vector3.Up);
        _camera.Fov = (float)s.Fov;
        _camera.Near = (float)s.NearPlane;
        _camera.Far = (float)s.FarPlane;
    }

    // ---- 状态机 → 屏幕可见性 ----

    private void ShowState(GameState state)
    {
        _titleScreen.Visible = state == GameState.Title;
        _pauseMenu.Visible = state == GameState.Paused;
        _gameOverModal.Visible = state == GameState.GameOver;
        if (state == GameState.GameOver)
        {
            _gameOverModal.SetResult(_gm.Winner, _gm.History.Count);
        }
        _guideScreen.Visible = state is GameState.GuideFromTitle or GameState.GuideFromGame;
        if (_guideScreen.Visible)
        {
            _guideScreen.FromGame = state == GameState.GuideFromGame;
            _guideScreen.ApplyMode();
        }
        _replayBar.Visible = state == GameState.Replay;
        _statusLabel.Visible = state is GameState.Playing or GameState.Paused;
        if (state is not (GameState.Title or GameState.Paused))
        {
            CloseSaveMenu(); // 换态时安全收起存档菜单（正常路径已在按钮回调先行关闭）
        }
    }

    private void OpenSaveMenu()
    {
        _saveMenu.RefreshSlots(_store.Saves.GetSlots());
        _saveMenu.Visible = true;
    }

    private void CloseSaveMenu() => _saveMenu.Visible = false;

    // ---- GameManager 事件 → 视图联动 ----

    /// <summary>统一刷新：调色板 → 棋子重建（含辅助线标记桶）→ 网格显隐 → 辅助线 → 状态栏。</summary>
    private void Refresh()
    {
        var palette = _gm.Colorblind ? Palette.Colorblind : Palette.Default;
        _board3D.ApplyPalette(palette);
        _board2D.Palette = palette;
        var aux = _gm.ComputeAux(_gm.Hover);
        _board3D.Rebuild(_gm.Board, _gm.FocusZ, aux);
        _board3D.SetGridFocus(_gm.FocusZ);
        _board3D.SetAuxData(aux);
        _board3D.SetLastMove(_gm.History.Count > 0 ? _gm.History[^1].Coordinate : null);
        _board2D.QueueRedraw();
        UpdateStatus();
        if (_gm.InReplay)
        {
            _replayBar.Sync(_gm.ReplayIndex, _gm.ReplayCount);
        }
    }

    private void OnHoverChanged(Coordinate3D? cell)
    {
        _board3D.SetHover(cell); // 3D 蓝点跟随 2D 悬停（R4-12）
        Refresh();               // 标记桶随辅助线变化重建
    }

    /// <summary>SWAP2 决策（§4.1 扩展：取消=继续执白，是有效决策而非放弃）。</summary>
    private void ShowSwapDialog()
    {
        _overlay.ShowConfirmDialog("SWAP2", Text.Swap.ChooseSwapOrDefault,
            onYes: () => _gm.ResolveSwap(true),
            onCancel: () => _gm.ResolveSwap(false),
            yesText: Text.Overlay.SwapYes,
            noText: Text.Overlay.SwapNo);
    }

    private void UpdateStatus()
    {
        if (_gm.IsOver)
        {
            _statusLabel.Text = _gm.IsDraw
                ? Text.Result.Draw
                : _gm.Winner == 1 ? Text.Result.BlackWin : Text.Result.WhiteWin;
        }
        else if (_gm.SwapPending)
        {
            _statusLabel.Text = Text.Swap.ChooseSwapOrDefault;
        }
        else
        {
            _statusLabel.Text = _gm.Current == PlayerColor.Black ? Text.Turn.BlackTurn : Text.Turn.WhiteTurn;
        }
    }

    private bool IsOver2DPanel() =>
        _board2D.GetGlobalRect().HasPoint(GetViewport().GetMousePosition());

    private double WorldPerPixel() =>
        2.0 * Math.Tan(CameraMath.ToRadians(_rig.Current.Fov) / 2.0) * _rig.Current.Distance
        / _viewport3D.GetVisibleRect().Size.Y;

    private double ViewportAspect() => _lastViewportSize.X / _lastViewportSize.Y;

    /// <summary>M3 参数：`-- --board=9 --layers=1 --spacing=3.5 --rules=swap2 --mode=pve --ai=hard`（`--` 后为用户参数）。</summary>
    private static (int Size, int Layers, double Spacing, RuleSet Rules, bool PvE, AIDifficulty Ai) ParseDebugArgs()
    {
        int size = GameConfig.DefaultBoardSize;
        int layers = GameConfig.DefaultLayerCount;
        double spacing = GameConfig.DefaultLayerSpacing;
        var rules = RuleSet.Gomoku;
        bool pve = false;
        var ai = AIDifficulty.Medium;

        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--board=") && int.TryParse(arg["--board=".Length..], out int s))
            {
                size = s;
            }
            else if (arg.StartsWith("--layers=") && int.TryParse(arg["--layers=".Length..], out int l))
            {
                layers = l;
            }
            else if (arg.StartsWith("--spacing=")
                     && double.TryParse(arg["--spacing=".Length..],
                         System.Globalization.CultureInfo.InvariantCulture, out double sp))
            {
                spacing = sp;
            }
            else if (arg.StartsWith("--rules="))
            {
                rules = arg["--rules=".Length..].Equals("swap2", StringComparison.OrdinalIgnoreCase)
                    ? RuleSet.Swap2
                    : RuleSet.Gomoku;
            }
            else if (arg.StartsWith("--mode="))
            {
                pve = arg["--mode=".Length..].Equals("pve", StringComparison.OrdinalIgnoreCase);
            }
            else if (arg.StartsWith("--ai="))
            {
                ai = arg["--ai=".Length..].ToLowerInvariant() switch
                {
                    "easy" => AIDifficulty.Easy,
                    "hard" => AIDifficulty.Hard,
                    _ => AIDifficulty.Medium,
                };
            }
        }
        return (size, layers, spacing, rules, pve, ai);
    }
}
