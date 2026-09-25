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
    private SceneRouter _router = null!;
    private OverlayManager _overlay = null!;
    private TitleScreen _titleScreen = null!;
    private PauseMenu _pauseMenu = null!;
    private GameOverModal _gameOverModal = null!;
    private SaveMenu _saveMenu = null!;
    private GuideScreen _guideScreen = null!;
    private ReplayBar _replayBar = null!;
    private SandboxPanel _sandboxPanel = null!;
    private FileDialog? _exportFileDialog;
    private FileDialog? _importFileDialog;
    private int _exportSlot;
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
        ApplyDisplayScaling();

        // 全局字体：霞鹜文楷 Lite（OFL，assets-src/fonts 本地转换版）——根窗口 Theme 全体控件生效
        var uiTheme = new Theme();
        uiTheme.DefaultFont = GD.Load<FontFile>("res://assets/fonts/LXGWWenKaiLite-Regular.ttf");
        uiTheme.DefaultFontSize = 15;
        GetWindow().Theme = uiTheme;

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

        // 1) 对局基座组件（不入树，由路由器首个 ShowState 挂载并 KeepAlive 常驻——保留现场）
        _gameScreen = new GameScreen(_boardSize, _layers, _spacing);

        // 2) 对话框层（z=30，高于路由三层）
        _overlay = new OverlayManager();
        AddChild(_overlay);

        // 3) 路由器与壳屏（工厂内构造+接线，KeepAlive 路由首次挂载后常驻）
        _router = new SceneRouter(BuildRoutes());
        AddChild(_router);

        // 4) 壳状态机宿主
        _store = new GameStore { Name = "GameStore" };
        AddChild(_store); // 触发 GameStore._Ready → GM 入树
        _store.Overlay = _overlay;

        // 5) 首次路由：挂载 title（可见）+ game（KeepAlive 隐藏）→ GameScreen._Ready
        _router.ShowState(_store.State);

        // 6) 对局内核接线（基座已入树，视图引用可用）
        _gm = _store.GM;
        _gm.LayerSpacing = _spacing;
        _viewport3D = _gameScreen.Viewport3D;
        _camera = _gameScreen.Camera;
        _board3D = _gameScreen.Board3D;
        _board2D = _gameScreen.Board2D;
        _statusLabel = _gameScreen.StatusLabel;
        _board2D.Game = _gm; // 2D 面板状态源（此前遗漏导致右半屏空白）
        _board2D.HoverChanged += cell => _gm.SetHover(cell);
        _board2D.PlaceRequested += cell => _gm.TryPlace(cell.X, cell.Y, cell.Z);
        _store.StateChanged += OnStateChanged;
        _store.SavesChanged += () =>
        {
            if (_router.IsOverlayOpen("save-menu"))
            {
                _saveMenu.RefreshSlots(_store.Saves.GetSlots());
            }
        };
        _gm.GameOver += _ => _store.NotifyGameOver();
        _gm.SwapPhaseStarted += ShowSwapDialog;
        _gm.StateChanged += Refresh;
        _gm.FocusChanged += _ => Refresh();
        _gm.HoverChanged += OnHoverChanged;
        EventBus.ThemeToggled += ApplyTheme;
        _store.LevelChanged += OnLevelChanged;

        _gm.Configure(new LevelConfig
        {
            Id = "cmdline",
            Name = Text.Levels.Cmdline,
            BoardSize = _boardSize,
            Layers = _layers,
            Rules = _rules,
        }, _pve, _aiDifficulty);
        _store.InitializeSaves();

        // 7) 相机与首刷
        _lastViewportSize = _viewport3D.GetVisibleRect().Size;
        _rig = new CameraRig(_boardSize, _layers, _spacing, _lastViewportSize.X / _lastViewportSize.Y);
        ApplyCamera();
        Refresh();
        int screen = DisplayServer.WindowGetCurrentScreen();
        SyncViewport3DResolution(); // 诊断行与相机初值即用最终（原生像素）视口
        GD.Print($"display: window={DisplayServer.WindowGetSize()}, screen={DisplayServer.ScreenGetSize(screen)}, " +
                 $"dpi={DisplayServer.ScreenGetDpi(screen)}, canvasScale={GetWindow().GetFinalTransform().Scale} " +
                 $"(诊断高分屏：window 应为物理像素；3D 视口={_viewport3D.Size})");
        GD.Print($"Nmoku M4R: game ready (board={_boardSize}x{_boardSize}x{_layers}, spacing={_spacing:0.0}, " +
                 $"rules={_rules}, mode={(_pve ? "pve" : "pvp")}, distance={_rig.Current.Distance:0.00})");
    }

    /// <summary>
    /// 高分屏 UI 适配（M4 验收反馈：高分下 UI 过小）：内容缩放取 OS 每-监视器 DPI 缩放
    /// （Windows 200% 缩放 → UI 物理字号与系统应用一致），窗口按工作区 85% 居中。
    /// 拉伸模式 canvas_items+expand 下逻辑视口=物理尺寸/缩放，布局自适应；3D 视口仍由
    /// SyncViewport3DResolution 拉到原生像素。
    /// </summary>
    private void ApplyDisplayScaling()
    {
        // headless 的 dummy DisplayServer 返回 0×0 usable rect——照搬会把窗口设成 0×0
        // （无头自测/CI 必须跳过；这也曾让 guard 包装的进程静默异常）
        if (DisplayServer.GetName() == "headless")
        {
            return;
        }
        var window = GetWindow();
        if (window.Mode is Window.ModeEnum.Fullscreen or Window.ModeEnum.ExclusiveFullscreen)
        {
            return;
        }
        int screen = DisplayServer.WindowGetCurrentScreen();
        window.ContentScaleFactor = Math.Clamp(DisplayServer.ScreenGetScale(screen), 1.0f, 3.0f);
        var usable = DisplayServer.ScreenGetUsableRect(screen);
        if (usable.Size.X <= 0 || usable.Size.Y <= 0)
        {
            return; // 异常环境值兜底：宁可用项目默认尺寸
        }
        window.Size = new Vector2I((int)(usable.Size.X * 0.85f), (int)(usable.Size.Y * 0.85f));
        window.MoveToCenter();
    }

    /// <summary>T 键/存档恢复触发的主题换肤：调色板切套 → 3D 环境 → 已挂载壳屏重建（game 基座除外，其视图走 ApplyPalette 通道）。</summary>
    private void ApplyTheme(bool isDark)
    {
        Palette.Active = isDark ? Palette.Dark : Palette.Light;
        _gameScreen.ApplyTheme();
        foreach (string id in _router.Mounted.Where(id => id != "game").ToList())
        {
            _router.Rebuild(id); // 壳屏按新套重建（工厂重跑读 Active；未挂载的屏幕打开时自动生效）
        }
        Refresh();
    }

    /// <summary>关卡维度变化（尺寸/层数随档）：3D 网格与相机按新维度重配（2D 面板自绘全动态，无需重配）。</summary>
    private void OnLevelChanged()
    {
        var board = _gm.Board;
        _board3D.Configure(board.BoardSize, board.Layers, _spacing);
        _rig.Reset(board.BoardSize, board.Layers, _spacing, ViewportAspect());
        Refresh();
    }

    /// <summary>
    /// 路由表（声明式，migration-plan M4R）：改 KeepAlive/背板字段即改行为，无需动逻辑。
    /// 工厂负责构造+事件接线（KeepAlive 路由仅首次挂载执行一次，无重复订阅）。
    /// </summary>
    private IEnumerable<RouteDef> BuildRoutes() =>
    [
        new RouteDef
        {
            Id = "title",
            Layer = RouteLayer.Base,
            Backdrop = BackdropKind.Opaque,
            KeepAlive = true,
            Factory = () =>
            {
                var screen = new TitleScreen();
                screen.StartRequested += () => _store.RequestStartNew();
                screen.ResumeRequested += () => _store.ResumeLive();
                screen.CustomGameRequested += () => _router.PushOverlay("sandbox");
                screen.LoadRequested += OpenSaveMenu;
                screen.GuideRequested += () => _store.OpenGuide();
                screen.QuitRequested += () => GetTree().Quit();
                _titleScreen = screen;
                return screen;
            },
        },
        new RouteDef
        {
            Id = "game",
            Layer = RouteLayer.Base,
            KeepAlive = true, // 保留现场（人类拍板 2026-09-19）：切回标题仅隐藏
            Factory = () => _gameScreen,
        },
        new RouteDef
        {
            Id = "pause",
            Layer = RouteLayer.Modal,
            Backdrop = BackdropKind.Blur,
            Factory = () =>
            {
                var screen = new PauseMenu();
                screen.ResumeRequested += () => _store.Resume();
                screen.RestartRequested += () => _store.RequestRestart();
                screen.SaveProgressRequested += () => _store.SaveToFirstEmpty();
                screen.LoadLatestRequested += () => _store.LoadLatest();
                screen.GuideRequested += () => _store.OpenGuide();
                screen.TitleRequested += () => _store.ReturnToTitle();
                _pauseMenu = screen;
                return screen;
            },
        },
        new RouteDef
        {
            Id = "game-over",
            Layer = RouteLayer.Modal,
            Backdrop = BackdropKind.Blur,
            Factory = () =>
            {
                var screen = new GameOverModal();
                screen.RestartRequested += () => _store.RequestRestart();
                screen.TitleRequested += () => _store.ReturnToTitle();
                _gameOverModal = screen;
                return screen;
            },
        },
        new RouteDef
        {
            Id = "guide",
            Layer = RouteLayer.Modal,
            Backdrop = BackdropKind.Opaque,
            Factory = () =>
            {
                var screen = new GuideScreen();
                screen.StartRequested += () => _store.RequestStartNew();
                screen.ResumeRequested += () => _store.Resume();
                screen.BackRequested += () => _store.GuideBack();
                _guideScreen = screen;
                return screen;
            },
        },
        new RouteDef
        {
            Id = "save-menu",
            Layer = RouteLayer.Overlay,
            Backdrop = BackdropKind.Dim,
            Factory = () =>
            {
                var screen = new SaveMenu();
                screen.LoadSlotRequested += i => { CloseSaveMenu(); _store.LoadSlot(i); };
                screen.ReplaySlotRequested += i => { CloseSaveMenu(); _store.EnterReplay(i); };
                screen.OverwriteSlotRequested += i => _store.SaveToSlot(i);
                screen.SaveQuickRequested += () => _store.SaveToSlot(GameConfig.QuickSaveIndex, confirmIfOccupied: false);
                screen.ExportSlotRequested += i => _store.ExportSlot(i);
                screen.ExportFileRequested += OpenExportFileDialog;
                screen.DeleteSlotRequested += i => _store.DeleteSlot(i);
                screen.ImportRequested += () => _store.ImportFromClipboard();
                screen.ImportFileRequested += OpenImportFileDialog;
                screen.CloseRequested += CloseSaveMenu;
                _saveMenu = screen;
                return screen;
            },
        },
        new RouteDef
        {
            Id = "help",
            Layer = RouteLayer.Overlay,
            Backdrop = BackdropKind.Opaque,
            Factory = () =>
            {
                var screen = new KeymapScreen();
                screen.CloseRequested += () => _router.PopOverlay("help");
                return screen;
            },
        },
        new RouteDef
        {
            Id = "sandbox",
            Layer = RouteLayer.Overlay,
            Backdrop = BackdropKind.Dim,
            Factory = () =>
            {
                var panel = new SandboxPanel();
                panel.StartRequested += (level, pve, difficulty) =>
                {
                    CloseSandbox();
                    _store.StartLevel(level, pve, difficulty);
                };
                panel.CloseRequested += CloseSandbox;
                panel.ToastRequested += (msg, isError) => _overlay.ShowToast(msg, isError);
                _sandboxPanel = panel;
                return panel;
            },
        },
        new RouteDef
        {
            Id = "replay-hud",
            Layer = RouteLayer.Overlay, // HUD 附件：无背板，不拦截相机鼠标
            Factory = () =>
            {
                var bar = new ReplayBar();
                bar.StepRequested += d => _gm.StepReplay(d);
                bar.SeekRequested += i => _gm.SetReplayIndex(i);
                bar.ExitRequested += () => _store.ExitReplay();
                _replayBar = bar;
                return bar;
            },
        },
    ];

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
    private int _autotestStepWait;
    private string? _autotestStepName;
    private int _autotestTotalFrames;
    /// <summary>单步 wait 帧预算（@120fps ≈ 5s）：headless 不限帧时帧数≠时间，必须有显式上限。</summary>
    private const int AutotestMaxStepWaitFrames = 600;
    /// <summary>全局看门狗（@120fps ≈ 5.5min）：任何挂死形态（无限 wait/异常风暴）都在此强制终止。</summary>
    private const int AutotestMaxTotalFrames = 40000;
    /// <summary>进程内内存上限（P1-2）：提交量超限视为泄漏/死循环，自杀退出防拖垮整机（77GB 事故教训）。</summary>
    private const long AutotestMemoryLimitBytes = 6L * 1024 * 1024 * 1024;

    private int _autotestMemCheckFrame;
    /// <summary>--autotest-only=&lt;子串&gt;：只跑名字匹配的步骤（bisection 用），其余立即出队。</summary>
    private string? _autotestOnly;

    private void RunAutotest()
    {
        if (!_autotestBuilt)
        {
            _autotestBuilt = true;
            _autotestOnly = OS.GetCmdlineUserArgs()
                .FirstOrDefault(a => a.StartsWith("--autotest-only="))?["--autotest-only=".Length..];
            BuildAutotestSteps();
            // headless 不限帧：_Process 每秒数千帧（77GB 提交内存事故的放大器），显式限帧
            Engine.MaxFps = 120;
        }
        // 内存自检每 60 帧（@120fps≈0.5s）一次：超限自杀 + 涨势日志（定位泄漏帧）
        if (++_autotestMemCheckFrame >= 60)
        {
            _autotestMemCheckFrame = 0;
            long commit = System.Diagnostics.Process.GetCurrentProcess().PrivateMemorySize64;
            string stepInfo = _autotestSteps.Count > 0 ? _autotestSteps.Peek().Name : "(done)";
            if (commit > AutotestMemoryLimitBytes)
            {
                GD.Print($"AUTOTEST ABORT: commit {commit / (1024 * 1024)}MB 超上限，卡在 [{stepInfo}] phase={_saveAlignPhase}/{_qsPhase}/{_echoPhase}");
                GetTree().Quit(1);
                return;
            }
            GD.Print($"AUTOTEST mem: {commit / (1024 * 1024)}MB step=[{stepInfo}] saveAlignPhase={_saveAlignPhase} qsPhase={_qsPhase}");
        }
        if (_autotestSteps.Count == 0)
        {
            GD.Print(_autotestFails.Length == 0 ? "AUTOTEST ALL PASS" : $"AUTOTEST FAIL: {_autotestFails}");
            GetTree().Quit();
            return;
        }
        // 全局看门狗：先于步骤执行，异常风暴/无限 wait 都逃不出总预算
        if (++_autotestTotalFrames > AutotestMaxTotalFrames)
        {
            string remaining = string.Join(",", _autotestSteps.Select(s => s.Name));
            GD.Print($"AUTOTEST FAIL: [global-watchdog frame {_autotestTotalFrames}, 未完成: {remaining}] ");
            GetTree().Quit(1);
            return;
        }
        var (name, run) = _autotestSteps.Peek();
        if (_autotestOnly != null && !name.Contains(_autotestOnly, StringComparison.OrdinalIgnoreCase))
        {
            _autotestSteps.Dequeue(); // bisection 过滤：不匹配的步骤直接跳过
            return;
        }
        if (_autotestStepName != name)
        {
            _autotestStepName = name; // 新步骤：重置单步 wait 预算
            _autotestStepWait = 0;
        }
        string result;
        try
        {
            result = run();
        }
        catch (Exception e)
        {
            // 异常视同 FAIL 且照样出队：绝不让同一步骤每帧重试
            // （77GB 提交内存事故根因：异常跳过出队 → 同一坏步骤每帧重建异常对象+堆栈+引擎错误记录）
            GD.PrintErr($"AUTOTEST step {name} threw:\n{e}");
            result = $"EXCEPTION {e.GetType().Name}: {e.Message}";
        }
        if (result == "wait")
        {
            if (++_autotestStepWait <= AutotestMaxStepWaitFrames)
            {
                return;
            }
            result = $"wait 超预算 >{AutotestMaxStepWaitFrames} 帧"; // 超预算落为 FAIL，照常出队
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
        _autotestSteps.Enqueue(("m4r-router-keepalive", () =>
        {
            bool ok = _gameScreen.IsInsideTree() && _gameScreen.Visible;
            _store.Pause();
            ok = ok && _store.State == GameState.Paused;
            _store.ReturnToTitle();
            ok = ok && _store.State == GameState.Title
                && _gameScreen.IsInsideTree() && !_gameScreen.Visible; // 保留现场：隐藏不销毁
            ok = ok && _store.ResumeLive() && _store.State == GameState.Playing && _gameScreen.Visible;
            return ok ? "ok" : "FAIL";
        }));
        _autotestSteps.Enqueue(("m4r-help-overlay", () =>
        {
            ToggleHelp();
            bool opened = _router.IsOverlayOpen("help");
            ToggleHelp();
            return opened && !_router.IsOverlayOpen("help") ? "ok" : "FAIL";
        }));
        _autotestSteps.Enqueue(("m4r-newgame-confirm-guard", () =>
        {
            // 保留局存在时"开始游戏"先确认覆盖；取消则现场保留
            _gm.TryPlace(4, 4, 0);
            _store.Pause();
            _store.ReturnToTitle();
            _store.RequestStartNew();
            bool asked = _overlay.IsModalOpen;
            _overlay.CloseAll();
            return asked && _store.State == GameState.Title && _gm.History.Count == 1 ? "ok" : "FAIL";
        }));
        _autotestSteps.Enqueue(("m4r-overlay-unmount-frees", () =>
        {
            // 残留面板回归（"存档菜单取消无响应"根因）：关闭浮层后旧实例必须被释放
            OpenSaveMenu();
            _staleProbe = _saveMenu;
            CloseSaveMenu();
            return _router.IsOverlayOpen("save-menu") ? "FAIL" : "ok";
        }));
        _autotestSteps.Enqueue(("m4r-overlay-unmount-frees-2", () =>
        {
            bool freed = !IsInstanceValid(_staleProbe); // QueueFree 跨帧生效
            return freed ? "ok" : "FAIL";
        }));
        _autotestSteps.Enqueue(("m4r-key-echo-filter", AutotestKeyEchoFilter));

        // ---- M4 第二轮：主题切换 / 连珠可配 / 预置子 ----
        _autotestSteps.Enqueue(("m4b-theme-toggle", () =>
        {
            bool wasDark = _store.IsDarkTheme;
            _store.ToggleTheme();
            bool flipped = _store.IsDarkTheme != wasDark
                && Palette.Active == (wasDark ? Palette.Light : Palette.Dark);
            _store.ToggleTheme();
            return flipped && _store.IsDarkTheme == wasDark ? "ok" : "FAIL";
        }));
        _autotestSteps.Enqueue(("m4b-winlen4-start", () =>
        {
            _store.Pause();
            _store.ReturnToTitle();
            bool ok = _store.StartLevel(new LevelConfig
            {
                Id = "t4",
                BoardSize = 9,
                Layers = 1,
                WinLength = 4,
                Rules = RuleSet.Gomoku,
            }, false, AIDifficulty.Medium) && _store.State == GameState.Playing;
            return ok ? "ok" : "FAIL";
        }));
        _autotestSteps.Enqueue(("m4b-winlen4-win", () =>
        {
            (int x, int y)[] b = [(0, 0), (1, 0), (2, 0), (3, 0)];
            (int x, int y)[] w = [(0, 1), (1, 1), (2, 1)];
            for (int k = 0; k < 3; k++)
            {
                if (!_gm.TryPlace(b[k].x, b[k].y, 0) || !_gm.TryPlace(w[k].x, w[k].y, 0))
                {
                    return "FAIL place";
                }
            }
            return _gm.TryPlace(b[3].x, b[3].y, 0) && _gm.IsOver && _gm.Winner == 1
                && _store.State == GameState.GameOver ? "ok" : "FAIL";
        }));
        _autotestSteps.Enqueue(("m4b-initialmoves", () =>
        {
            _store.Pause();
            _store.ReturnToTitle();
            bool ok = _store.StartLevel(LevelCatalog.Residual, false, AIDifficulty.Medium)
                && _store.State == GameState.Playing
                && _gm.History.Count == 3
                && _gm.Board.MoveCount() == 3
                && _gm.Current == PlayerColor.White; // B W B → 轮白
            return ok ? "ok" : "FAIL";
        }));
        _autotestSteps.Enqueue(("m4b-level-dims-resync", () =>
        {
            // 严重 bug 回归：层数多于默认的关卡必须重配 3D 网格与相机（M4 二轮验收反馈）
            _store.Pause();
            _store.ReturnToTitle();
            bool ok = _store.StartLevel(new LevelConfig
            {
                Id = "deep",
                BoardSize = 11,
                Layers = 8, // > 默认 6：不重配则 3D 网格仍按 6 层构建、多余层不渲染
                WinLength = 5,
                Rules = RuleSet.Gomoku,
            }, false, AIDifficulty.Medium);
            ok = ok && _gm.Board.Layers == 8
                && _board3D.Layers == 8 && _board3D.BoardSize == 11
                && Math.Abs(_rig.Target.Center.Z - 7 * _spacing / 2) < 1e-6; // 相机中心=(layers-1)×spacing/2
            // 深层落子可达（渲染数据端验证）
            ok = ok && _gm.TryPlace(5, 5, 7) && _gm.Board.Get(5, 5, 7) == CellState.Black;
            _store.Pause();
            _store.ReturnToTitle();
            return ok ? "ok" : "FAIL";
        }));
        _autotestSteps.Enqueue(("m4b-nzsave-file-roundtrip", () =>
        {
            // .nzsave 文件往返：导出槽位 → 删除 → 文件导入（zip 容器直解）→ 落首空槽内容一致
            var iso = new GameStore(new FileSystemStorageAdapter("user://saves_autotest"));
            iso.InitializeSaves();
            var data = _gm.BuildSaveData(false);
            iso.Saves.SaveAsync(1, data).GetAwaiter().GetResult();

            string path = "user://saves_autotest/file_roundtrip.nzsave";
            iso.ExportSlotToFile(1, path);
            iso.Saves.DeleteAsync(1).GetAwaiter().GetResult();
            iso.ImportFile(path);
            var reloaded = iso.Saves.Load(0);
            bool ok = reloaded != null && reloaded.Id == data.Id && reloaded.Moves.Count == data.Moves.Count;

            iso.Saves.DeleteAsync(0).GetAwaiter().GetResult();
            DirAccess.RemoveAbsolute(path);
            return ok ? "ok" : "FAIL";
        }));
        _autotestSteps.Enqueue(("m4b-winlen-save-restore", () =>
        {
            // B14 回归：连珠 7 对局快存 → 读档，连珠数随外圈元数据恢复（v2.0.0 schema 无此字段）
            _store.Pause();
            _store.ReturnToTitle();
            bool ok = _store.StartLevel(new LevelConfig
            {
                Id = "wl7",
                BoardSize = 9,
                Layers = 1,
                WinLength = 7,
                Rules = RuleSet.Gomoku,
            }, false, AIDifficulty.Medium) && _gm.WinLength == 7;
            // 连珠 7 生效中：黑连 5 不胜
            (int x, int y)[] b = [(0, 0), (1, 0), (2, 0), (3, 0), (4, 0)];
            (int x, int y)[] w = [(0, 1), (1, 1), (2, 1), (3, 1), (4, 1)];
            for (int k = 0; k < 5; k++)
            {
                ok = ok && _gm.TryPlace(b[k].x, b[k].y, 0) && _gm.TryPlace(w[k].x, w[k].y, 0);
            }
            ok = ok && !_gm.IsOver;
            _store.QuickSave();
            ok = ok && _store.Saves.GetSlotWinLength(GameConfig.QuickSaveIndex) == 7;
            _store.Pause();
            ok = ok && _store.LoadSlot(GameConfig.QuickSaveIndex) && _store.State == GameState.Playing;
            // 恢复后连珠数仍为 7：黑连 6 不胜、连 7 才胜
            ok = ok && _gm.WinLength == 7
                && _gm.TryPlace(5, 0, 0) && !_gm.IsOver
                && _gm.TryPlace(5, 1, 0)
                && _gm.TryPlace(6, 0, 0) && _gm.IsOver && _gm.Winner == 1;
            _store.Pause();
            _store.ReturnToTitle();
            return ok ? "ok" : "FAIL";
        }));

        // ---- M4 收尾轮：04-contracts §6.4/§6.5 清单自动化证据 ----
        _autotestSteps.Enqueue(("m4c-overlay-contract", () =>
        {
            // §6.4 四项：确认回调恰好一次 / 取消不触发 onYes / 输入 trim 提交 / CloseAll 丢弃挂起回调
            int yes = 0, cancel = 0, submit = 0;
            string submitted = "";
            _overlay.ShowConfirmDialog("t", "m", () => yes++, () => cancel++);
            _overlay.ConfirmDialogForTests.EmitSignal("confirmed");
            bool confirmOnce = yes == 1 && cancel == 0 && !_overlay.IsModalOpen;
            _overlay.ShowConfirmDialog("t", "m", () => yes++, () => cancel++);
            _overlay.ConfirmDialogForTests.EmitSignal("canceled");
            bool cancelPath = yes == 1 && cancel == 1;
            _overlay.ShowInputDialog("t", "m", "p", s => { submit++; submitted = s; });
            _overlay.InputFieldForTests.Text = "  ab  ";
            _overlay.InputDialogForTests.EmitSignal("confirmed");
            bool trimOk = submit == 1 && submitted == "ab";
            _overlay.ShowInputDialog("t", "m", "p", s => submit++);
            _overlay.CloseAll();
            _overlay.InputDialogForTests.EmitSignal("confirmed");
            bool discardOk = submit == 1 && !_overlay.IsModalOpen;
            return confirmOnce && cancelPath && trimOk && discardOk
                ? "ok" : $"FAIL y={yes} c={cancel} s={submit} sub={submitted}";
        }));
        _autotestSteps.Enqueue(("m4c-modifier-quicksave", AutotestModifierQuickSave));
        _autotestSteps.Enqueue(("m4c-real-old-restore", AutotestRealOldSaveRestore));
        _autotestSteps.Enqueue(("m4c-save-alignment", AutotestSaveAlignment));
    }

    private int _saveAlignPhase;
    private int _saveAlignClearIdx;
    private long _slotTsBefore;

    /// <summary>源项目存档对齐回归：S 键落首空槽 ×2、覆盖带确认框（信号合成确认）、快存槽免确认由单测覆盖。</summary>
    private string AutotestSaveAlignment()
    {
        switch (_saveAlignPhase)
        {
            case 0:
                // 跨帧逐槽清除（P2-1：不在 _Process 里同步等整串异步链）；清完再进对局
                if (_saveAlignClearIdx < GameConfig.SaveSlotCount)
                {
                    _store.Saves.DeleteAsync(_saveAlignClearIdx).GetAwaiter().GetResult();
                    _saveAlignClearIdx++;
                    return "wait";
                }
                _gm.Configure(9, 3, RuleSet.Gomoku, false, AIDifficulty.Medium);
                if (!_store.StartGame() || !_gm.TryPlace(4, 4, 0) || !_gm.TryPlace(4, 5, 0) || !_store.Pause())
                {
                    _saveAlignPhase = 0;
                    return "FAIL start";
                }
                Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = Key.S, Pressed = true, Echo = false });
                _saveAlignPhase = 1;
                return "wait";
            case 1:
                Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = Key.S, Pressed = false });
                var first = _store.Saves.Load(0);
                bool landed0 = first != null && first.Moves.Count == 2 && _store.Saves.IsSlotEmpty(1);
                Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = Key.S, Pressed = true, Echo = false });
                _saveAlignPhase = 2;
                return landed0 ? "wait" : "FAIL first empty";
            case 2:
                Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = Key.S, Pressed = false });
                if (_store.Saves.IsSlotEmpty(1))
                {
                    _saveAlignPhase = 0;
                    return "FAIL second empty";
                }
                var current0 = _store.Saves.Load(0);
                if (current0 == null)
                {
                    _saveAlignPhase = 0;
                    return "FAIL slot0 empty"; // 不可达假设显式化：防 NRE 每帧重试放大成事故
                }
                // 覆盖写入断言走免确认分支——headless 下 PopupCentered 弹窗会瞬间自动确认
                // （引擎怪癖，登记 CHANGELOG），确认框语义由 m4c-overlay-contract + 人类真实窗口走查覆盖
                _slotTsBefore = current0.Timestamp;
                _store.SaveToSlot(0, confirmIfOccupied: false);
                var saved = _store.Saves.Load(0);
                bool overwritten = saved != null && saved.Timestamp > _slotTsBefore;
                _store.Pause();
                _store.ReturnToTitle();
                _saveAlignPhase = 0;
                _saveAlignClearIdx = 0;
                return overwritten ? "ok" : "FAIL overwrite";
            default:
                _saveAlignPhase = 0;
                return "FAIL";
        }
    }

    /// <summary>
    /// R6/验收轮：真实 v2.0.0 旧档（源浏览器导出，见 tests Fixtures browser_real_export_*）
    /// 走完整导入→读档→续弈链路。相对路径在仓库根执行（本地与 CI 一致）。
    /// </summary>
    private string AutotestRealOldSaveRestore()
    {
        // Godot 启动会把进程 CWD 切到项目目录 → 同时探测仓库根（CI/本地）与项目目录两种基点
        string rel = "tests/Nmoku.Core.Tests/Fixtures/browser_real_export_v2_standard.b64.txt";
        string? fixturePath = new[]
        {
            System.IO.Path.Combine(System.Environment.CurrentDirectory, rel),
            System.IO.Path.GetFullPath(System.IO.Path.Combine(System.Environment.CurrentDirectory, "..", "..", rel)),
        }.FirstOrDefault(System.IO.File.Exists);
        using var file = fixturePath == null
            ? null
            : Godot.FileAccess.Open(fixturePath, Godot.FileAccess.ModeFlags.Read);
        if (file == null)
        {
            return "FAIL fixture not found (run from repo root)";
        }
        string b64 = file.GetAsText().Trim();

        // 完整导入路径（嗅探+校验+落首空槽）；先前自测步可能已占用槽 0 → 按 id 定位
        _store.ImportText(b64);
        int slot = -1;
        for (int i = 0; i < GameConfig.SaveSlotCount; i++)
        {
            if (_store.Saves.Load(i)?.Id == "47dd93b4-cb80-48cb-b2ba-2419ea829461")
            {
                slot = i;
                break;
            }
        }
        if (slot < 0)
        {
            return "FAIL import";
        }
        if (!_store.LoadSlot(slot) || _store.State != GameState.Playing || _gm.History.Count != 8)
        {
            return "FAIL restore";
        }
        // 续弈：样本黑已有 (4..7,4) 四连，恢复后轮黑——第 9 手 (8,4) 成五（胜负判定随档生效）
        if (!_gm.TryPlace(8, 4, 0) || !_gm.IsOver || _gm.Winner != 1 || _store.State != GameState.GameOver)
        {
            return "FAIL continue";
        }
        _store.ReturnToTitle();
        _store.Saves.DeleteAsync(slot).GetAwaiter().GetResult();
        return "ok";
    }

    private int _qsPhase;

    /// <summary>§6.5 修饰键检测：Ctrl+S 触发快存、裸 S 不触发（exact match + 直通不误吞）。</summary>
    private string AutotestModifierQuickSave()
    {
        switch (_qsPhase)
        {
            case 0:
                _gm.Configure(9, 3, RuleSet.Gomoku, false, AIDifficulty.Medium);
                if (!_store.StartGame() || _store.State != GameState.Playing)
                {
                    return "FAIL start";
                }
                Input.ParseInputEvent(new InputEventKey
                {
                    PhysicalKeycode = Key.S, CtrlPressed = true, Pressed = true, Echo = false,
                });
                _qsPhase = 1;
                return "wait";
            case 1:
                Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = Key.S, Pressed = false });
                bool saved = !_store.Saves.IsSlotEmpty(GameConfig.QuickSaveIndex);
                _store.Saves.DeleteAsync(GameConfig.QuickSaveIndex).GetAwaiter().GetResult();
                // 裸 S：无快捷键匹配（exact match 下 ctrl_pressed 不符）→ 只落 zoom_out 持续键，快存不得触发
                Input.ParseInputEvent(new InputEventKey
                {
                    PhysicalKeycode = Key.S, Pressed = true, Echo = false,
                });
                _qsPhase = 2;
                return saved ? "wait" : "FAIL ctrl+s no save";
            case 2:
                Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = Key.S, Pressed = false });
                bool untouched = _store.Saves.IsSlotEmpty(GameConfig.QuickSaveIndex);
                _store.Pause();
                _store.ReturnToTitle();
                _qsPhase = 0;
                return untouched ? "ok" : "FAIL plain s saved";
            default:
                return "FAIL";
        }
    }

    /// <summary>M4R-2 自测：合成真实按键事件验证 echo 过滤——非 echo 按键转 90°，echo 按键不再触发。</summary>
    private int _echoPhase;
    private double _yawAtStart, _yawAfterPress, _yawAfterEcho;

    private string AutotestKeyEchoFilter()
    {
        switch (_echoPhase)
        {
            case 0:
                _gm.Configure(9, 3, RuleSet.Gomoku, false, AIDifficulty.Medium);
                _store.StartGame();
                _yawAtStart = _rig.Target.Yaw;
                Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = Key.Q, Pressed = true, Echo = false });
                _echoPhase = 1;
                return "wait"; // 事件经输入管线下一帧 flush → _Input
            case 1:
                _yawAfterPress = _rig.Target.Yaw;
                Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = Key.Q, Pressed = true, Echo = true });
                _echoPhase = 2;
                return "wait";
            case 2:
                _yawAfterEcho = _rig.Target.Yaw;
                Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = Key.Q, Pressed = false });
                _echoPhase = 0;
                bool pressed = Math.Abs(_yawAfterPress - _yawAtStart + 90) < 1e-6; // Q = -90°
                bool echoIgnored = Math.Abs(_yawAfterEcho - _yawAfterPress) < 1e-6;
                return pressed && echoIgnored ? "ok" : $"FAIL pressΔ={_yawAfterPress - _yawAtStart:0.0} echoΔ={_yawAfterEcho - _yawAfterPress:0.0}";
            default:
                return "FAIL";
        }
    }

    private Control? _staleProbe;

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
        var imported = filled == 0 ? isoStore.Saves.Load(0) : null;
        bool ok = imported != null && imported.Id == data.Id;
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
            case InputEventKey { Pressed: true, Echo: false } key:
                // 单次动作事件驱动：echo 直接忽略（按住不重复，与 A/D 同语义）；匹配即吞
                if (TryDispatchKey(key))
                {
                    GetViewport().SetInputAsHandled();
                }
                break;
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

    /// <summary>context 门控后的持续按住（相机/间距类，§4.2.2 按住生效）。</summary>
    private bool Held(string action) =>
        ShortcutRouter.Allows(action, _store.State) && Input.IsActionPressed(action, true);

    /// <summary>
    /// 有序单次动作表（顺序即优先级）。HandleHeldInput 首中即吞；autotest 经
    /// SimulateActionPress 复用同一分发逻辑，保证测试与真实输入同路径。
    /// </summary>
    private IEnumerable<(string Action, Action Run)> SingleActions()
    {
        yield return ("toggle_help", ToggleHelp);
        yield return ("toggle_theme", () => _store.ToggleTheme());
        yield return ("rotate_left", () => _rig.Orbit(-90));
        yield return ("rotate_right", () => _rig.Orbit(90));
        yield return ("pause", () => _store.Pause());
        yield return ("resume", () => _store.Resume());
        yield return ("confirm", ConfirmAction);
        yield return ("load_latest", () => _store.LoadLatest());
        yield return ("quick_save", () => _store.QuickSave());
        yield return ("save_first_empty", () => _store.SaveToFirstEmpty());
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
        yield return ("spacing_reset", () =>
        {
            _spacing = GameConfig.DefaultLayerSpacing;
            _rig.Reset(_gm.Board.BoardSize, _gm.Board.Layers, _spacing, ViewportAspect());
        });
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

    /// <summary>
    /// 键盘单次动作的事件驱动分发（M4R-2：echo 过滤=按一下动一下，键回显不再重复触发，
    /// Q/E 与 A/D 同为单击语义）。返回 true 表示事件已消费（§4.2.4 匹配即吞）。
    /// </summary>
    private bool TryDispatchKey(InputEventKey key)
    {
        if (_overlay.IsModalOpen)
        {
            return false; // 模态框优先：按键交给对话框自身处理（不吞）
        }
        if (_router.HasOverlay)
        {
            // L2 浮层作用域：只响应 AllowWhenOverlay 键（KeymapTable）
            if (InputMap.EventIsAction(key, "toggle_help", exactMatch: true))
            {
                ToggleHelp();
                return true;
            }
            if (InputMap.EventIsAction(key, "ui_cancel", exactMatch: true) && _router.TopOverlay is { } top)
            {
                _router.PopOverlay(top); // Esc 逐层冒泡：先关最顶浮层
                return true;
            }
            return false;
        }
        if (_store.State is GameState.GuideFromTitle or GameState.GuideFromGame
            && InputMap.EventIsAction(key, "ui_cancel", exactMatch: true))
        {
            // 引导态 Esc（§3.4 两行的 Esc 语义）：标题入口=返回，对局入口=回对局
            if (_store.State == GameState.GuideFromGame)
            {
                _store.Resume();
            }
            else
            {
                _store.GuideBack();
            }
            return true;
        }
        foreach (var (action, run) in SingleActions())
        {
            // 同键跨态（如 R=pause/restart）：被门控的匹配跳过，继续找本状态下的合法动作
            if (InputMap.EventIsAction(key, action, exactMatch: true)
                && ShortcutRouter.Allows(action, _store.State))
            {
                run();
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 持续按住类键（zoom/pan/spacing，§4.2.2 按住生效）。单次动作已改 _Input 事件驱动
    /// （echo 过滤），此处不再轮询 JustPressed——按住不重复的键不会从这里触发。
    /// </summary>
    private void HandleHeldInput(double delta)
    {
        if (_overlay.IsModalOpen || _router.HasOverlay)
        {
            return; // 模态框/浮层吞掉一切持续键（单次键在 _Input 按作用域分发）
        }

        // exact match 防 Shift 平移串线（M2 决策 #9）
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
        _board3D.Configure(_gm.Board.BoardSize, _gm.Board.Layers, _spacing);
        _rig.Update(_gm.Board.BoardSize, _gm.Board.Layers, _spacing, ViewportAspect(), GameConfig.DefaultYaw);
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
        SyncViewport3DResolution();
    }

    /// <summary>
    /// 子视口原生分辨率（M4R-2 引入，B16 定稿）：容器 Stretch=false 后视口尺寸完全由本方法接管——
    /// 容器逻辑尺寸 × 窗口最终变换缩放（含 stretch/DPI）= 原生像素，3D 与 2D 同清晰度。
    /// stretch=true 时引擎每帧接管尺寸、手动 SetSize 被拒绝（viewport.cpp 警告刷屏且从未生效），
    /// 而 stretch_shrink 是 int 除数，0.5 类半像素到不了——故定稿 stretch=false 方案。
    /// 均匀缩放不改纵横比，相机无需重配。
    /// </summary>
    private void SyncViewport3DResolution()
    {
        if (_viewport3D.GetParent() is not SubViewportContainer container)
        {
            return;
        }
        var scale = GetWindow().GetFinalTransform().Scale;
        float factor = MathF.Min(scale.X, scale.Y);
        if (factor <= 0.1f)
        {
            return; // headless dummy 值（≈0.033）：无从换算
        }
        var desired = (Vector2I)(((Vector2)container.Size * factor).Floor());
        if (desired.X > 0 && desired.Y > 0 && _viewport3D.Size != desired)
        {
            _viewport3D.Size = desired;
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

    // ---- 状态机 → 路由（可见性由 SceneRouter 按栈差分管理，这里只做各屏的数据灌入）----

    private void OnStateChanged(GameState state)
    {
        _router.ShowState(state);
        if (state == GameState.GameOver)
        {
            _gameOverModal.SetResult(_gm.Winner, _gm.History.Count);
        }
        if (state == GameState.Title)
        {
            _titleScreen.UpdateResumeButton(_store.HasLiveGame);
        }
        if (state is GameState.GuideFromTitle or GameState.GuideFromGame)
        {
            _guideScreen.FromGame = state == GameState.GuideFromGame;
            _guideScreen.ApplyMode();
        }
    }

    private void OpenSaveMenu()
    {
        _router.PushOverlay("save-menu"); // 首次 push 触发工厂：构造+接线并赋值 _saveMenu
        _saveMenu.RefreshSlots(_store.Saves.GetSlots());
    }

    private void CloseSaveMenu() => _router.PopOverlay("save-menu");

    private void CloseSandbox() => _router.PopOverlay("sandbox");

    // ---- .nzsave 文件级导入导出（FileDialog；容器/嗅探在 Core，Game 只管路径选择与字节读写）----

    private void OpenExportFileDialog(int slot)
    {
        if (_store.Saves.IsSlotEmpty(slot))
        {
            return;
        }
        _exportSlot = slot;
        _exportFileDialog ??= MakeFileDialog(FileDialog.FileModeEnum.SaveFile,
            path => _store.ExportSlotToFile(_exportSlot, path));
        _exportFileDialog.CurrentFile = $"nmoku-{DateTime.Now:yyyyMMdd-HHmmss}.nzsave";
        _exportFileDialog.PopupCentered();
    }

    private void OpenImportFileDialog()
    {
        _importFileDialog ??= MakeFileDialog(FileDialog.FileModeEnum.OpenFile,
            path => _store.ImportFile(path));
        _importFileDialog.PopupCentered();
    }

    private FileDialog MakeFileDialog(FileDialog.FileModeEnum mode, Action<string> onSelected)
    {
        var dialog = new FileDialog
        {
            Access = FileDialog.AccessEnum.Filesystem,
            FileMode = mode,
            Exclusive = true,
            Unresizable = false,
            MinSize = new Vector2I(720, 480),
            Filters = ["*.nzsave ; Nmoku 存档文件"],
        };
        dialog.FileSelected += path => onSelected(path);
        AddChild(dialog);
        return dialog;
    }

    private void ToggleHelp()
    {
        if (_router.IsOverlayOpen("help"))
        {
            _router.PopOverlay("help");
        }
        else
        {
            _router.PushOverlay("help");
        }
    }

    // ---- GameManager 事件 → 视图联动 ----

    /// <summary>统一刷新：调色板 → 棋子重建（含辅助线标记桶）→ 网格显隐 → 辅助线 → 状态栏。</summary>
    private void Refresh()
    {
        var palette = Palette.For(_gm.Colorblind);
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
