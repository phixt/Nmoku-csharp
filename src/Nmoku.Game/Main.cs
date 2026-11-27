using Godot;
using Nmoku.Core;
using Nmoku.Core.AI;
using Nmoku.Core.Audio;
using Nmoku.Core.Camera;
using Nmoku.Core.Saves;
using Nmoku.Game;
using Nmoku.Game.Audio;
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
    private AudioManager _audio = null!;
    private PreTitleScreen _titleScreen = null!;
    private PauseMenu _pauseMenu = null!;
    private GameOverModal _gameOverModal = null!;
    private SaveMenu _saveMenu = null!;
    private GuideScreen _guideScreen = null!;
    private SettingsScreen _settingsScreen = null!;
    private KeymapScreen _keymapScreen = null!;
    private ReplayBar _replayBar = null!;
    private SandboxPanel _sandboxPanel = null!;
    private FileDialog? _exportFileDialog;
    private FileDialog? _importFileDialog;
    private int _exportSlot;
    private int _importSlot = -1;
    private TurnIndicator _turnIndicator = null!;

    private readonly int _boardSize;
    private readonly int _layers;
    private double _spacing;
    private RuleSet _rules = RuleSet.Gomoku;
    private bool _pve;
    private AIDifficulty _aiDifficulty = AIDifficulty.Medium;
    private Vector2 _lastViewportSize;

    /// <summary>3D 视口渲染倍率（1:1 原生物理像素 + MSAA 4×；摒弃此前 2× 超采样 + 8× MSAA 的 32× 过采瓶颈）。</summary>
    private const float ViewportSupersample = 1f;

    /// <summary>全局 UI 观感放大系数（人类拍板 2026-09-25）：内容缩放绝对值（×2.5 基准设计），
    /// 窗口固定 1920×1080，等效逻辑视口 768×432；调全局 UI 大小只动这里。</summary>
    private const float UiScaleFactor = 2.5f;
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
        RestoreLanguage(); // 语言偏好先于任何壳屏构建（首个 I18n.T 在标题屏 _Ready）

        // 0) 音频管理器（§3.5）：SFX 池 + BGM 引擎 + 偏好恢复；shell 各屏经 AudioManager.Instance 访问
        _audio = new AudioManager();
        AddChild(_audio);

        // 全局统一 UI 主题：霞鹜文楷 Lite 默认字体 + 主题化非灰暗控件样式
        var uiTheme = UiTheme.Build(Palette.Active);
        GetWindow().Theme = uiTheme;
        RenderingServer.SetDefaultClearColor(Palette.Active.Background); // 首帧前同步（默认暗色）

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
        // 诊断：确认运行的是最新构建（Assembly.Location 在 Godot 4.6 为空，走 temp 输出路径）
        string buildDll = System.IO.Path.Combine(ProjectSettings.GlobalizePath("res://"),
            ".godot/mono/temp/bin/Debug/Nmoku.Game.dll");
        GD.Print($"build: {System.IO.File.GetLastWriteTime(buildDll):yyyy-MM-dd HH:mm:ss}");

        // 1) 对局基座组件（不入树，由路由器首个 ShowState 挂载并 KeepAlive 常驻——保留现场）
        _gameScreen = new GameScreen(_boardSize, _layers, _spacing);

        // 2) 对话框层（z=30，高于路由三层）
        _overlay = new OverlayManager();
        AddChild(_overlay);
        _overlay.ApplyTheme(uiTheme);

        // 3) 路由器与壳屏（工厂内构造+接线，KeepAlive 路由首次挂载后常驻）
        _router = new SceneRouter(BuildRoutes());
        AddChild(_router);
        _router.ApplyGlobalTheme(uiTheme);

        // 4) 壳状态机宿主（自测走隔离目录——AutotestSaveDir 此前是死常量，m4c 清槽步骤会毁真实存档）
        _store = _autotestArmed
            ? new GameStore(new FileSystemStorageAdapter(GameStore.AutotestSaveDir)) { Name = "GameStore" }
            : new GameStore { Name = "GameStore" };
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
        _turnIndicator = _gameScreen.TurnIndicator;
        _board2D.Game = _gm; // 2D 面板状态源（此前遗漏导致右半屏空白）
        _board2D.HoverChanged += cell => _gm.SetHover(cell);
        _board2D.PlaceRequested += cell => _gm.TryPlace(cell.X, cell.Y, cell.Z);
        _gm.MovePlaced += move => _audio.PlayStonePlace(move.Player); // 人类与 AI 落子统一走分色双敲采样（B31）
        _store.StateChanged += OnStateChanged;
        _store.MatchStarted += () =>
            _audio.PrepareNewMatchBgm(AudioPolicy.PreferAdvancedGameTrack(_gm.Level, _gm.PvE, _gm.Difficulty));
        _store.SavesChanged += () =>
        {
            if (_router.IsOverlayOpen("save-menu"))
            {
                _saveMenu.RefreshSlots(_store.Saves.GetSlots());
            }
        };
        _gm.GameOver += winner =>
        {
            // B31：PvE 区分人类胜（Win）与 AI 胜（Lose），PvP 胜方均播 Win，平局静默；播放期间 BGM 自动平滑闪避
            _audio.PlayGameOver(AudioPolicy.ResolveGameOverSfx(_gm.PvE, winner, _gm.AiColor));
            _store.NotifyGameOver();
        };
        _gm.SwapPhaseStarted += ShowSwapDialog;
        _gm.SwapResolved += swapped => _audio.PlaySwapResolved(swapped);
        _gm.StateChanged += Refresh;
        _gm.FocusChanged += _ => Refresh();
        _gm.HoverChanged += OnHoverChanged;
        EventBus.ThemeToggled += ApplyTheme;
        EventBus.LanguageChanged += OnLanguageChanged;
        _store.LevelChanged += OnLevelChanged;
        // 系统标题栏的最大化/还原也要同步偏好（设置页下拉初值=实际状态的前提；headless 无窗口）。
        // Window 无 mode_changed 信号，size_changed 覆盖全部尺寸变化的模式切换（最小化不误写）
        if (DisplayServer.GetName() != "headless")
        {
            GetWindow().SizeChanged += SyncWindowModeToPrefs;
            GetWindow().SizeChanged += OnWindowSizeChangedTrace;
            TraceWindowGeometry(force: true);
        }

        var defaultLevel = new LevelConfig
        {
            Id = "cmdline",
            Name = I18n.T(Text.Levels.Cmdline),
            BoardSize = _boardSize,
            Layers = _layers,
            Rules = _rules,
        };
        _gm.Configure(defaultLevel, _pve, _aiDifficulty);
        _store.CaptureDefaultContext(defaultLevel, _pve, _aiDifficulty); // 开始新对局恒恢复此上下文（读档继承清理）
        _store.InitializeSaves();

        // 7) 相机与首刷
        _lastViewportSize = _viewport3D.GetVisibleRect().Size;
        _rig = new CameraRig(_boardSize, _layers, _spacing, _lastViewportSize.X / _lastViewportSize.Y);
        ApplyCamera();
        Refresh();
        int screen = DisplayServer.WindowGetCurrentScreen();
        GD.Print($"display: window={DisplayServer.WindowGetSize()}, screen={DisplayServer.ScreenGetSize(screen)}, " +
                 $"dpi={DisplayServer.ScreenGetDpi(screen)}, canvasScale={GetWindow().GetFinalTransform().Scale} " +
                 $"(诊断高分屏：window 应为物理像素；3D 视口={_viewport3D.Size})");
        _audio.PlayBgm(BgmTrack.Menu); // 启动即标题轨（源 main.ts:1392）
        GD.Print($"Nmoku M4R: game ready (board={_boardSize}x{_boardSize}x{_layers}, spacing={_spacing:0.0}, " +
                 $"rules={_rules}, mode={(_pve ? "pve" : "pvp")}, distance={_rig.Current.Distance:0.00})");
    }

    public override void _ExitTree()
    {
        EventBus.ThemeToggled -= ApplyTheme;
        EventBus.LanguageChanged -= OnLanguageChanged;
        if (DisplayServer.GetName() != "headless")
        {
            var win = GetWindow();
            if (win != null)
            {
                win.SizeChanged -= SyncWindowModeToPrefs;
                win.SizeChanged -= OnWindowSizeChangedTrace;
            }
        }
        SettingsStore.ReleaseCache();
    }

    /// <summary>
    /// UI 缩放（人类拍板 2026-09-25）：窗口固定项目默认 1920×1080，内容缩放=2.5 绝对值
    /// （等效设计基准 768×432）。不再随 DPI/工作区调整——那是"UI 过小"首版的方案，
    /// 实测观感更差（窗口过大+发糊）后回退。3D 视口由 SyncViewport3DResolution 直接吃
    /// 原生像素保持清晰。
    /// </summary>
    private void ApplyDisplayScaling()
    {
        // headless 的 dummy DisplayServer 无有效窗口参数——跳过（无头自测/CI 必须跳过）
        if (DisplayServer.GetName() == "headless")
        {
            return;
        }
        var window = GetWindow();
        if (window.Mode is Window.ModeEnum.Fullscreen or Window.ModeEnum.ExclusiveFullscreen)
        {
            return;
        }
        // 批11 根治：Godot 4.6 的 ContentScaleSize 默认=启动窗口尺寸（1920×1080），其存在把逻辑面
        // 钉死在 css/csf=768×432，窗口缩放时按 factor×window/css 抬缩放（最大化=×1.67 非整数发糊+
        // 底部内容下压）——显式归零恢复 expand 语义：逻辑面=窗口/因子，元素物理尺寸恒定。
        window.ContentScaleSize = Vector2I.Zero;
        window.ContentScaleFactor = UiScaleFactor;
        window.MoveToCenter();
        // 窗口最小尺寸保护（2026-09-26 根治配套）：逻辑面高 ≥416（1080p 屏可用高），
        // 标题屏内容 ~406 在最小窗口也 fit——阻断"拖矮窗口→底部按钮溢出"
        window.MinSize = new Vector2I(800, (int)(416 * UiScaleFactor));
        RestoreWindowMode(); // 上次会话若是最大化/全屏，启动即恢复（设置页 general 节）
    }

    /// <summary>T 键/存档恢复触发的主题换肤：调色板切套 → 3D 环境 → 已挂载壳屏重建（game 基座除外，其视图走 ApplyPalette 通道）。</summary>
    private void ApplyTheme(bool isDark)
    {
        Palette.Active = isDark ? Palette.Dark : Palette.Light;
        var uiTheme = UiTheme.Build(Palette.Active);
        GetWindow().Theme = uiTheme;
        _overlay.ApplyTheme(uiTheme);
        _router.ApplyGlobalTheme(uiTheme);
        if (_exportFileDialog != null) _exportFileDialog.Theme = uiTheme;
        if (_importFileDialog != null) _importFileDialog.Theme = uiTheme;
        RenderingServer.SetDefaultClearColor(Palette.Active.Background); // 2D 侧无自有背景，透出的根清屏色必须随主题（B17-M1：亮色发暗根因）
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
                var screen = new PreTitleScreen();
                screen.StartRequested += () => _store.RequestStartNew();
                screen.ResumeRequested += () => _store.ResumeLive();
                screen.CustomGameRequested += () => _router.PushOverlay("sandbox");
                screen.LoadRequested += OpenSaveMenu;
                screen.GuideRequested += () => _store.OpenGuide();
                screen.SettingsRequested += OpenSettings;
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
                screen.LoadRequested += OpenSaveMenu; // 暂停读档=打开存档菜单可选槽位（P1）；L 键仍是读最新
                screen.GuideRequested += () => _store.OpenGuide();
                screen.SettingsRequested += OpenSettings;
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
                screen.FillSlotRequested += i => _store.FillSlotFromClipboard(i);
                screen.ExportSlotRequested += i => _store.ExportSlot(i); // 复制：槽→剪贴板
                screen.ExportFileRequested += OpenExportFileDialog;      // 导出：槽→.nzsave 文件
                screen.ImportFileSlotRequested += slot => OpenImportFileDialog(slot); // 空槽行：文件→该槽
                screen.ImportFileRequested += () => OpenImportFileDialog(null);       // 头部：文件→统一落槽
                screen.DeleteSlotRequested += i => _store.DeleteSlot(i);
                screen.CloseRequested += CloseSaveMenu;
                _saveMenu = screen;
                return screen;
            },
        },
        new RouteDef
        {
            Id = "settings",
            Layer = RouteLayer.Overlay,
            Backdrop = BackdropKind.Dim,
            Factory = () =>
            {
                var screen = new SettingsScreen();
                screen.CloseRequested += () => _router.PopOverlay("settings");
                screen.WindowModeChanged += ApplyWindowMode;
                _settingsScreen = screen;
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
                _keymapScreen = screen;
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
                bar.StepRequested += StepReplayWithAudio;
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
            // 批次 ④：移除早退 return，让 AdvanceFade / HandleHeldInput / HandleViewportChange /
            // _rig.Advance / ApplyCamera 在自测期间同样接受覆盖
        }
        AdvanceFade(delta);
        if (!_gameScreen.Visible && !_autotestArmed)
        {
            return; // 标题页期间跳过对局相机与视口轮询，零空转开销
        }
        HandleHeldInput(delta);
        HandleViewportChange();
        _rig.Advance(delta);
        ApplyCamera();
    }

    private void OnWindowSizeChangedTrace() => TraceWindowGeometry();

    // ---- 批11 窗口几何追踪（任务栏遮挡根因调查，B16 式"先机制结论后修复"）：窗口 pos/size/
    // mode/工作区任一变化即落 .trace/window-trace.log（res:// = 游戏项目目录）并 GD.Print，带状态
    // 关联。最小化/最大化不动 pos/size，只有 mode 会变——mode 必须进键。追踪绝不影响主流程。
    private ((int X, int Y) Pos, (int X, int Y) Size, Rect2I Usable, float Scale, int Mode)? _lastWinKey;

    private void TraceWindowGeometry(bool force = false)
    {
        if (DisplayServer.GetName() == "headless")
        {
            return;
        }
        var pos = DisplayServer.WindowGetPosition();
        var size = DisplayServer.WindowGetSize();
        int screen = DisplayServer.WindowGetCurrentScreen();
        var usable = DisplayServer.ScreenGetUsableRect(screen);
        int mode = (int)DisplayServer.WindowGetMode();
        var key = ((pos.X, pos.Y), (size.X, size.Y), usable, GetWindow().ContentScaleFactor, mode);
        if (!force && key.Equals(_lastWinKey))
        {
            return;
        }
        _lastWinKey = key;
        string line = $"[wintrace] t={Time.GetTicksMsec()}ms state={_store?.State.ToString() ?? "boot"} " +
                      $"pos={pos} size={size} mode={(DisplayServer.WindowMode)mode} focus={GetWindow().HasFocus()} " +
                      $"view={GetViewport().GetVisibleRect().Size} scale={GetWindow().GetFinalTransform().Scale} " +
                      $"csf={GetWindow().ContentScaleFactor} css={GetWindow().ContentScaleSize} " +
                      $"screen={screen} usable={usable}";
        GD.Print(line);
        try
        {
            string dir = System.IO.Path.Combine(ProjectSettings.GlobalizePath("res://"), ".trace");
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "window-trace.log"),
                line + System.Environment.NewLine);
        }
        catch
        {
            // 落盘失败只丢追踪不丢功能（只读环境/导出包等）
        }
    }


    public override void _Input(InputEvent @event)
    {
        // 相机鼠标交互仅在 PLAYING/REPLAY 开放（§4.2.3 context 语义）
        bool cameraAllowed = _store != null && _store.State is GameState.Playing or GameState.Replay;
        switch (@event)
        {
            case InputEventKey { Pressed: true, Echo: false } key:
                // 快捷键页打开时 ←/→ 翻层（优先于动作分发：复盘态开页时也不会误触步进）
                if (TryPageKeymap(key))
                {
                    GetViewport().SetInputAsHandled();
                    break;
                }
                // 单次动作事件驱动：echo 直接忽略（按住不重复，与 A/D 同语义）；匹配即吞
                if (TryDispatchKey(key))
                {
                    GetViewport().SetInputAsHandled();
                }
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } button:
                _rotating = button.Pressed && cameraAllowed && !IsOver2DPanel();
                UpdateMouseCapture();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Middle } middle:
                _panning = middle.Pressed && cameraAllowed && !IsOver2DPanel();
                UpdateMouseCapture();
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

    /// <summary>
    /// 相机拖拽期间把光标约束在窗口客户区内（批11 根治，2026-09-26 机制定案）：游玩中快速旋转/平移
    /// 的拖拽一旦划出窗口顶部压到标题栏，Windows 接管为「拖动窗口」，拖至屏幕顶即 Aero-Snap 最大化
    /// ——任务栏遮挡/窗口变巨/来回翻转的根因。Confined=光标裁剪在客户区（物理上不可能再逃逸到
    /// 标题栏），松开或失焦即还原。
    /// </summary>
    private void UpdateMouseCapture()
    {
        if (DisplayServer.GetName() == "headless")
        {
            return;
        }
        Input.MouseMode = _rotating || _panning
            ? Input.MouseModeEnum.Confined
            : Input.MouseModeEnum.Visible;
    }

    public override void _Notification(int what)
    {
        if (what == NotificationApplicationFocusOut && (_rotating || _panning))
        {
            _rotating = _panning = false; // 拖拽中失焦（alt-tab 等）：防光标滞留 Confined 态
            UpdateMouseCapture();
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
        yield return ("toggle_mute", ToggleMute);
        yield return ("rotate_left", () => _rig.Orbit(-90));
        yield return ("rotate_right", () => _rig.Orbit(90));
        yield return ("pause", () => _store.Pause());
        yield return ("resume", () => _store.Resume());
        yield return ("confirm", ConfirmAction);
        yield return ("load_latest", () => _store.LoadLatest());
        yield return ("quick_save", () => _store.QuickSave());
        yield return ("quick_load", () => _store.QuickLoad());
        yield return ("save_first_empty", () => _store.SaveToFirstEmpty());
        yield return ("restart", () => _store.RequestRestart());
        yield return ("return_to_title", () => _store.ReturnToTitle());
        yield return ("open_guide", () => _store.OpenGuide());
        yield return ("replay_prev", () => StepReplayWithAudio(-1));
        yield return ("replay_next", () => StepReplayWithAudio(1));
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
    /// <summary>快捷键页（F1 浮层/引导屏）打开时 ←/→ 翻层；未打开返回 false 走正常分发。
    /// 只操作实际打开的那个宿主——引导屏未挂载过时 _guideScreen.Content 为 null（NRE 事故 2026-09-26）。</summary>
    private bool TryPageKeymap(InputEventKey key)
    {
        if (_router.IsOverlayOpen("help"))
        {
            return StepContent(_keymapScreen.Content, key);
        }
        if (_store.State is GameState.GuideFromTitle or GameState.GuideFromGame)
        {
            return StepContent(_guideScreen.Content, key);
        }
        return false;
    }

    private bool StepContent(KeymapContent? content, InputEventKey key)
    {
        if (content == null)
        {
            return false;
        }
        if (key.PhysicalKeycode == Key.Left)
        {
            content.PageStep(-1);
            return true;
        }
        if (key.PhysicalKeycode == Key.Right)
        {
            content.PageStep(1);
            return true;
        }
        return false;
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
            // L2 浮层作用域：**作用域由 KeymapTable 决定**（AllowWhenOverlay），
            // 本层只提供各 action 的处理器（修复批 ③-5：此前此处硬编码三个 action 名，
            // KeymapTable.AllowsWhenOverlayOpen 守卫在生产代码零引用 → 新增浮层可用键要改两处，
            // M4 的漂移机制未堵死）。
            foreach (string action in OverlayAllowedActions())
            {
                if (!OverlayHandlers().TryGetValue(action, out var run))
                {
                    continue; // 无处理器（如仅文档行）→ 跳过
                }
                if (InputMap.EventIsAction(key, action, exactMatch: true))
                {
                    run();
                    return true;
                }
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
        if (size.X > 0 && size.Y > 0 && _lastViewportSize != size)
        {
            // 逻辑面大幅跳变（最大化 768×432→1280×743 等）：淡入柔化一帧重排的"突兀缩小"观感
            // （M5 最小动效：只调 Modulate，不碰布局/缩放管线——B16 回环教训）
            if (Mathf.Abs(size.X - _lastViewportSize.X) > 200 || Mathf.Abs(size.Y - _lastViewportSize.Y) > 120)
            {
                StartFadeIn();
            }
            _lastViewportSize = size;
            SyncCameraToViewport(size);
        }
        SyncViewport3DResolution();
    }

    /// <summary>
    /// 视口尺寸变化 → 相机按**当前关卡**尺寸重算公式态（修复批 ③-3）。
    /// 此前用命令行默认尺寸（<c>_boardSize/_layers</c>）→ 非默认关卡（沙盒自定义、读档随档）
    /// 之后任何窗口缩放都跳回 13×13×6 构图。
    /// </summary>
    private void SyncCameraToViewport(Vector2 size)
    {
        var board = _gm.Board;
        _rig.Update(board.BoardSize, board.Layers, _spacing, size.X / size.Y, GameConfig.DefaultYaw);
    }

    /// <summary>
    /// 子视口原生分辨率（B16 v4 定稿）：视口 Size=宿主逻辑尺寸×内容缩放=物理像素，自绘纹理
    /// 与屏幕 1:1。历史：v1 SubViewportContainer(stretch=false) 手动喂尺寸 → 容器最小尺寸=视口
    /// 尺寸 → 每帧 ×缩放回环（黑屏事故）；v2 Scaling3DScale 超采样 → 纹理仍是逻辑尺寸被放大
    /// 显示（发糊）；v3 _GetMinimumSize 清零 → C++ 侧 get_minimum_size 不经脚本虚方法，无效；
    /// v4 弃用 SubViewportContainer，自绘宿主（GameScreen.ViewportHost）结构性解耦。
    /// </summary>
    private void SyncViewport3DResolution()
    {
        if (_viewport3D.GetParent() is not Control host)
        {
            return;
        }
        var scale = GetWindow().GetFinalTransform().Scale;
        float factor = MathF.Min(scale.X, scale.Y);
        if (factor <= 0.1f)
        {
            return; // headless dummy 值（≈0.033）：无从换算
        }
        // 原生 1:1 物理像素，高分屏（如 3K/4K）按等比上限 1920 封顶，保证高帧率且零宽高比拉伸
        float physW = host.Size.X * factor * ViewportSupersample;
        float physH = host.Size.Y * factor * ViewportSupersample;
        float cap = MathF.Min(1f, 1920f / MathF.Max(1f, MathF.Max(physW, physH)));
        var desired = new Vector2I(Mathf.Max(64, (int)MathF.Floor(physW * cap)),
            Mathf.Max(64, (int)MathF.Floor(physH * cap)));
        if (_viewport3D.Size != desired)
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

    private void StepReplayWithAudio(int delta)
    {
        int before = _gm.ReplayIndex;
        _gm.StepReplay(delta);
        if (delta > 0 && _gm.ReplayIndex > before && _gm.LastVisibleMove is { } move)
        {
            _audio.PlayStonePlace(move.Player);
        }
    }

    private void OnStateChanged(GameState state)
    {
        if (_lastState == GameState.GameOver && state != GameState.GameOver)
        {
            _audio.StopGameOverSfx(); // 离开结算（重开/回标题）即收掉残余 3s 胜负短乐句，BGM 平滑回升
        }
        _router.ShowState(state);
        SyncBgm(state);
        // 进对局（从标题/引导进入，非暂停/复盘往返）：3D 视口首帧渐显，掩盖纹理就绪前的黑闪
        if (state == GameState.Playing && _lastState is GameState.Title or GameState.GuideFromTitle
            or GameState.GuideFromGame)
        {
            StartFadeIn();
        }
        _lastState = state;
        if (state == GameState.Replay)
        {
            Refresh(); // replay-hud 此刻才挂载：补一次同步（BeginReplay 内的首刷落在挂载前）
        }
        if (state == GameState.GameOver)
        {
            _gameOverModal.SetResult(_gm.Winner, _gm.History.Count);
        }
        if (state == GameState.Title)
        {
            _titleScreen.OnEntered();
            _titleScreen.UpdateResumeButton(_store.HasLiveGame);
        }
        if (state is GameState.GuideFromTitle or GameState.GuideFromGame)
        {
            _guideScreen.FromGame = state == GameState.GuideFromGame;
            _guideScreen.ApplyMode();
        }
    }

    /// <summary>BGM 场景分发（B20 ②）：menu=标题、game=对局、pause=暂停、guide=引导；
    /// 复盘与结算不切轨（源退出复盘 playBGM("game") 撞同轨守卫，全程沿用进入前轨道）。</summary>
    private void SyncBgm(GameState state)
    {
        BgmTrack? track = state switch
        {
            GameState.Title => BgmTrack.Menu,
            GameState.Playing => BgmTrack.Game,
            GameState.Paused => BgmTrack.Pause,
            GameState.GuideFromTitle or GameState.GuideFromGame => BgmTrack.Guide,
            _ => null,
        };
        if (track is { } t)
        {
            _audio.PlayBgm(t);
        }
    }

    // ---- M5 过渡动效（最小安全方案）：内容 Modulate 0.4→1 快速淡入，掩盖逻辑面一帧重排
    //      （最大化跳变）与 3D 视口首帧黑闪；不碰布局/缩放管线（B16 回环教训）----
    private const float FadeDuration = 0.16f;
    private const float FadeFromAlpha = 0.4f;
    private float _fadeRemaining;
    private GameState _lastState;

    private void StartFadeIn()
    {
        _fadeRemaining = FadeDuration;
        _gameScreen.Modulate = new Color(1, 1, 1, FadeFromAlpha);
    }

    private void AdvanceFade(double delta)
    {
        if (_fadeRemaining <= 0)
        {
            return;
        }
        _fadeRemaining -= (float)delta;
        if (_fadeRemaining <= 0)
        {
            _gameScreen.Modulate = Colors.White;
            return;
        }
        float t = 1 - (float)(_fadeRemaining / FadeDuration);
        _gameScreen.Modulate = new Color(1, 1, 1, Mathf.Lerp(FadeFromAlpha, 1f, t));
    }

    private void OpenSaveMenu()
    {
        _router.PushOverlay("save-menu"); // 首次 push 触发工厂：构造+接线并赋值 _saveMenu
        _saveMenu.RefreshSlots(_store.Saves.GetSlots());
    }

    private void CloseSaveMenu() => _router.PopOverlay("save-menu");

    private void OpenSettings() => _router.PushOverlay("settings");

    /// <summary>窗口模式切换（设置页事件）：窗口化恢复项目默认 1920×1080 居中；headless 跳过。</summary>
    private void ApplyWindowMode(WindowMode mode)
    {
        if (DisplayServer.GetName() == "headless")
        {
            return;
        }
        var window = GetWindow();
        switch (mode)
        {
            case WindowMode.Maximized:
                window.Mode = Window.ModeEnum.Maximized;
                break;
            case WindowMode.Fullscreen:
                window.Mode = Window.ModeEnum.Fullscreen;
                break;
            default:
                window.Mode = Window.ModeEnum.Windowed;
                window.Size = new Vector2I(1920, 1080);
                window.MoveToCenter();
                break;
        }
    }

    /// <summary>语言偏好启动恢复（M5 验收反馈批补缺口：设置页写入 cfg 后此前无人读，重启即丢失）。
    /// 无偏好时默认 zh-CN（人类指示 2026-10-01，不随 OS locale 漂移）。cfg 存横线形（zh-CN），
    /// SetLocale 标准化为下划线形——翻译查找两端均按标准形，无匹配问题。</summary>
    private void RestoreLanguage()
    {
        string lang = SettingsStore.Get("general", "lang", "zh-CN").AsString();
        if (string.IsNullOrWhiteSpace(lang))
        {
            lang = "zh-CN";
        }
        TranslationServer.SetLocale(lang);
    }

    /// <summary>窗口模式变化（含系统标题栏操作）写回偏好，保证 cfg 与实际状态一致。</summary>
    private void SyncWindowModeToPrefs()
    {
        if (_modeRestorePending)
        {
            return; // 启动恢复期窗口尚在过渡态（实际仍是 windowed），写回会毁掉待恢复偏好
        }
        WriteWindowModePref(EffectiveWindowMode());
    }

    /// <summary>
    /// 当前有效窗口模式（**最小化优先**：窗口属性与 DisplayServer 任一报 Minimized 即最小化）。
    /// 最小化是窗口态而非模式选择，必须与「最大化/全屏」区分——否则最小化一次就把用户的
    /// 模式偏好抹成 windowed（修复批 ③-4）。
    /// </summary>
    private DisplayServer.WindowMode EffectiveWindowMode()
    {
        var ds = DisplayServer.WindowGetMode();
        if (ds == DisplayServer.WindowMode.Minimized)
        {
            return ds;
        }
        return GetWindow().Mode == Window.ModeEnum.Minimized ? DisplayServer.WindowMode.Minimized : ds;
    }

    /// <summary>
    /// 窗口模式 → 偏好值；**最小化返回 null**（不写回）。
    /// 修复批 ③-4：此前 switch 的兜底分支把最小化当成 windowed 写回 cfg，
    /// 「上次会话是全屏」被最小化一次即抹掉（与本文件原注释「最小化不误写」直接矛盾）。
    /// </summary>
    internal static string? WindowModeToPref(DisplayServer.WindowMode mode) => mode switch
    {
        DisplayServer.WindowMode.Maximized => "maximized",
        DisplayServer.WindowMode.Fullscreen or DisplayServer.WindowMode.ExclusiveFullscreen => "fullscreen",
        DisplayServer.WindowMode.Minimized => null,
        _ => "windowed",
    };

    /// <summary>写回窗口模式偏好（null = 不写：最小化等非模式选择态）。</summary>
    private void WriteWindowModePref(DisplayServer.WindowMode mode)
    {
        string? pref = WindowModeToPref(mode);
        if (pref == null)
        {
            return;
        }
        SettingsStore.Set("general", "window_mode", pref);
    }

    /// <summary>启动恢复进行中：拦截 SyncWindowModeToPrefs 的过渡态写回（见 RestoreWindowMode）。</summary>
    private bool _modeRestorePending;

    /// <summary>启动恢复持久化窗口模式（设置页写入；ApplyDisplayScaling 尾部调用）。
    /// CallDeferred：_Ready 早期窗口尚未完成 DPI/工作区初始化，立即设 Fullscreen 会被
    /// 引擎覆盖回 Maximized（2026-09-26 wintrace 取证 t=1081 mode=Maximized ≠ cfg fullscreen）。</summary>
    private void RestoreWindowMode()
    {
        if (DisplayServer.GetName() == "headless")
        {
            return;
        }
        string mode = SettingsStore.Get("general", "window_mode", "windowed").AsString();
        if (mode != "maximized" && mode != "fullscreen")
        {
            return;
        }
        _modeRestorePending = true;
        CallDeferred(nameof(ApplyRestoredMode), mode);
    }

    private void ApplyRestoredMode(string mode)
    {
        GetWindow().Mode = mode == "fullscreen" ? Window.ModeEnum.Fullscreen : Window.ModeEnum.Maximized;
        _modeRestorePending = false; // 恢复完成后的 SizeChanged 才允许写回
    }

    /// <summary>语言切换热刷新（B20 ④ 源语义：立即重注入文本）——重建已挂载壳屏（game 基座除外，
    /// ApplyTheme 同款通道），游戏基座内动态文本随下次 Refresh 更新。语言持久化在设置页完成。</summary>
    private void OnLanguageChanged(string lang)
    {
        TranslationServer.SetLocale(lang);
        foreach (string id in _router.Mounted.Where(id => id != "game").ToList())
        {
            _router.Rebuild(id, localeChanged: true);
        }
        Refresh();
    }

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

    private void OpenImportFileDialog(int? slot)
    {
        _importSlot = slot ?? -1; // -1 = 头部统一导入（首空槽/满则覆盖槽 5）；≥0 = 空槽行导入到该槽
        _importFileDialog ??= MakeFileDialog(FileDialog.FileModeEnum.OpenFile,
            path =>
            {
                if (_importSlot < 0)
                {
                    _store.ImportFile(path);
                }
                else
                {
                    _store.ImportFileToSlot(path, _importSlot);
                }
            });
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
            Filters = ["*.nzsave ; " + I18n.T(Text.Save.SaveFileFilter)],
            Theme = GetWindow().Theme,
        };
        dialog.FileSelected += path => onSelected(path);
        AddChild(dialog);
        return dialog;
    }

    /// <summary>M 静音（源语义：全局翻转 + toast 提示状态）。</summary>
    private void ToggleMute()
    {
        _audio.ToggleMute();
        _overlay.ShowToast(_audio.Muted ? I18n.T(Text.Settings.MuteToastOn) : I18n.T(Text.Settings.MuteToastOff));
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

    // ---- 浮层作用域（修复批 ③-5）：作用域来自 KeymapTable，本层只提供处理器 ----

    /// <summary>
    /// KeymapTable 声明为「浮层打开时仍可用」的 action（去重、保声明顺序）。
    /// 单一事实源=KeymapTable（04-contracts §4.2.3 的 AllowWhenOverlay 语义）。
    /// </summary>
    internal static IReadOnlyList<string> OverlayAllowedActions() =>
        KeymapTable.Entries
            .Where(e => e.AllowWhenOverlay)
            .SelectMany(e => e.Actions)
            .Where(a => !a.StartsWith('@')) // 纯文档行无 action
            .Distinct()
            .ToList();

    /// <summary>浮层作用域各 action 的处理器（行为归本层；作用域归 KeymapTable）。</summary>
    private Dictionary<string, Action> OverlayHandlers() => new()
    {
        ["toggle_help"] = ToggleHelp,
        ["toggle_theme"] = () => _store.ToggleTheme(),
        ["toggle_mute"] = ToggleMute,
        ["ui_cancel"] = CloseTopOverlay,
    };

    /// <summary>Esc 逐层冒泡：关闭最顶浮层。</summary>
    private void CloseTopOverlay()
    {
        if (_router.TopOverlay is { } top)
        {
            _router.PopOverlay(top);
        }
    }

    /// <summary>
    /// 漂移检测：KeymapTable 声明为浮层可用、但本层没有处理器的 action（空 = 无漂移）。
    /// 出现即意味着「该键在浮层内被 F1 帮助屏广告着却不生效」——修复批 ③-5 的根因类别。
    /// </summary>
    internal IReadOnlyList<string> OverlayActionsWithoutHandler()
    {
        var handlers = OverlayHandlers();
        return OverlayAllowedActions().Where(a => !handlers.ContainsKey(a)).ToList();
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
        _board3D.SetLastMove(_gm.LastVisibleMove?.Coordinate); // 批10：随游标的可见最后一手（0 手=无）
        _board2D.Aux = aux; // 一份数据、两个渲染器（避免 2D 面板重复扫描）
        _board2D.QueueRedraw();
        if (GodotObject.IsInstanceValid(_titleScreen))
        {
            _titleScreen.SetColorblind(_gm.Colorblind);
        }
        UpdateStatus();
        // 复盘条同步须等路由挂载：GM.StateChanged（BeginReplay/SetReplayIndex）先于 _store.SetState 触发，
        // 首次进复盘时 replay-hud 尚未入树、再次进入时旧实例已销毁——两窗口都不得触碰（NRE 崩溃根因）
        if (_gm.InReplay && GodotObject.IsInstanceValid(_replayBar))
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
        _overlay.ShowConfirmDialog("SWAP2", I18n.T(Text.Swap.ChooseSwapOrDefault),
            onYes: () => _gm.ResolveSwap(true),
            onCancel: () => _gm.ResolveSwap(false),
            yesText: I18n.T(Text.Overlay.SwapYes),
            noText: I18n.T(Text.Overlay.SwapNo));
    }

    private void UpdateStatus()
    {
        // 批10：行棋指示器（右下角，游戏/复盘共用）。复盘态优先——显示游标行棋方
        // （Current 已随游标），即便载入的是已结束的档也照常显示行棋
        if (_gm.InReplay)
        {
            _turnIndicator.SetTurn(_gm.Current.ToCell(), TurnText());
            return;
        }
        if (_gm.IsOver)
        {
            _turnIndicator.SetTextOnly(_gm.IsDraw
                ? I18n.T(Text.Result.Draw)
                : _gm.Winner == 1 ? I18n.T(Text.Result.BlackWin) : I18n.T(Text.Result.WhiteWin));
            return;
        }
        if (_gm.SwapPending)
        {
            _turnIndicator.SetTextOnly(I18n.T(Text.Swap.ChooseSwapOrDefault));
            return;
        }
        _turnIndicator.SetTurn(_gm.Current.ToCell(), TurnText());
    }

    private string TurnText() =>
        _gm.Current == PlayerColor.Black ? I18n.T(Text.Turn.BlackTurn) : I18n.T(Text.Turn.WhiteTurn);

    private bool IsOver2DPanel() =>
        _board2D.GetGlobalRect().HasPoint(GetViewport().GetMousePosition());

    private double WorldPerPixel() =>
        2.0 * Math.Tan(CameraMath.ToRadians(_rig.Current.Fov) / 2.0) * _rig.Current.Distance
        / _viewport3D.GetVisibleRect().Size.Y;

    private double ViewportAspect() => _lastViewportSize.X / _lastViewportSize.Y;

    /// <summary>M3 参数：`-- --board=9 --layers=1 --spacing=3.5 --rules=swap2 --mode=pve --ai=hard`（`--` 后为用户参数）。
    /// 尺寸一律钳到 GameConfig 硬上限：Board 构造不校验，0 尺寸盘会让 IsFull() 恒真（首手即判和），
    /// 负尺寸盘会让 InBounds 恒假（落子被静默吞、僵尸盘）。</summary>
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
                size = Math.Clamp(s, GameConfig.MinBoardSize, GameConfig.MaxBoardSize);
            }
            else if (arg.StartsWith("--layers=") && int.TryParse(arg["--layers=".Length..], out int l))
            {
                layers = Math.Clamp(l, 1, GameConfig.MaxLayers);
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
