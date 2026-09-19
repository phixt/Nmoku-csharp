using Godot;
using Nmoku.Core;
using Nmoku.Core.AI;
using Nmoku.Core.Camera;
using Nmoku.Game;
using Nmoku.Game.Theme;
using Nmoku.Game.Views;

/// <summary>
/// M3 场景根：左右分屏（3D 左视图 + 2D 右面板）+ GameManager 接线。
/// 悬停唯一来源是 2D 面板，3D 蓝点/辅助线跟随（紧密联动）；相机自由交互保留 M2 方案（决策 #9）。
/// 状态机/菜单 UI 在 M4；命令行参数为 M3 关卡入口：--board=13 --layers=6 --spacing=3.5
/// --rules=gomoku|swap2 --mode=pvp|pve --ai=easy|medium|hard。
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
    private GameManager _gm = null!;
    private CameraRig _rig = null!;
    private ConfirmationDialog _swapDialog = null!;
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
        int custom = 0;
        foreach (string action in InputMap.GetActions())
        {
            if (!action.StartsWith("ui_"))
                custom++;
        }
        GD.Print($"Nmoku M0: engine boot OK (custom input actions={custom}, audio buses={AudioServer.BusCount})");

        BuildLayout();
        BuildDialogs();

        _gm = new GameManager { Name = "GameManager" };
        AddChild(_gm);
        _gm.Configure(_boardSize, _layers, _rules, _pve, _aiDifficulty);
        Wire2DPanel();

        _gm.StateChanged += Refresh;
        _gm.FocusChanged += _ => Refresh();
        _gm.HoverChanged += OnHoverChanged;
        _gm.SwapPhaseStarted += ShowSwapDialog;

        _lastViewportSize = _viewport3D.GetVisibleRect().Size;
        _rig = new CameraRig(_boardSize, _layers, _spacing, _lastViewportSize.X / _lastViewportSize.Y);
        ApplyCamera();

        Refresh();
        GD.Print($"Nmoku M3: game ready (board={_boardSize}x{_boardSize}x{_layers}, spacing={_spacing:0.0}, " +
                 $"rules={_rules}, mode={(_pve ? "pve" : "pvp")}, distance={_rig.Current.Distance:0.00})");
    }

    private void BuildLayout()
    {
        var hbox = new HBoxContainer();
        hbox.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(hbox);

        var viewportContainer = new SubViewportContainer
        {
            Stretch = true,
            SizeFlagsHorizontal = Control.SizeFlags.Expand,
            SizeFlagsStretchRatio = 0.62f,
        };
        hbox.AddChild(viewportContainer);

        _viewport3D = new SubViewport { OwnWorld3D = true, Msaa3D = Viewport.Msaa.Msaa4X };
        viewportContainer.AddChild(_viewport3D);

        _viewport3D.AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = Palette.Default.Background,
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = Palette.Default.AmbientLight,
                AmbientLightEnergy = 0.55f,
            },
        });
        _viewport3D.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-55, 35, 0) });

        _board3D = new Board3DView { Name = "Board3DView" };
        _viewport3D.AddChild(_board3D);
        _board3D.Configure(_boardSize, _layers, _spacing);

        _camera = new Camera3D
        {
            KeepAspect = Camera3D.KeepAspectEnum.Height, // Godot fov 为垂直向，与 Three.js 语义一致（R1）
            Current = true,
        };
        _viewport3D.AddChild(_camera);

        _board2D = new Board2DView
        {
            SizeFlagsHorizontal = Control.SizeFlags.Expand,
            SizeFlagsStretchRatio = 0.38f,
        };
        hbox.AddChild(_board2D);

        _statusLabel = new Label();
        _statusLabel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterBottom);
        _statusLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _statusLabel.GrowHorizontal = Control.GrowDirection.Both;
        _statusLabel.OffsetTop = -64;
        AddChild(_statusLabel);
    }

    private void BuildDialogs()
    {
        // SWAP2 决策（M3 占位 UI，M4 换 OverlayManager）：确定=交换、取消/Esc=继续执白
        _swapDialog = new ConfirmationDialog
        {
            Title = "SWAP2",
            DialogText = Text.Swap.ChooseSwapOrDefault,
            OkButtonText = "交换黑白",
            CancelButtonText = "继续执白",
            Exclusive = true,
        };
        _swapDialog.Confirmed += () => _gm.ResolveSwap(true);
        _swapDialog.Canceled += () => _gm.ResolveSwap(false);
        AddChild(_swapDialog);
    }

    private void Wire2DPanel()
    {
        _board2D.HoverChanged += cell => _gm.SetHover(cell);
        _board2D.PlaceRequested += cell => _gm.TryPlace(cell.X, cell.Y, cell.Z);
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

    // ---- 无头对局自测（-- --autotest）：胜负/判和/悔棋/SWAP2 双分支/AI 落子；CI 冒烟依赖 AUTOTEST ALL PASS ----
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
    }

    public override void _Input(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } button:
                _rotating = button.Pressed && !IsOver2DPanel();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Middle } middle:
                _panning = middle.Pressed && !IsOver2DPanel();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true }:
                // 径向缩放：与 W/S 同路径，单调无跳变（zoom-to-anchor 待 M3 拾取恢复）
                if (!IsOver2DPanel()) _rig.Zoom(1);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true }:
                if (!IsOver2DPanel()) _rig.Zoom(-1);
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

    private void HandleHeldInput(double delta)
    {
        // exact match：Shift+WASD/方向键是平移，不得与无修饰键的 zoom_in/out 串线
        if (Input.IsActionJustPressed("rotate_left", true))
        {
            _rig.Orbit(-90);
        }
        if (Input.IsActionJustPressed("rotate_right", true))
        {
            _rig.Orbit(90);
        }
        if (Input.IsActionPressed("zoom_in", true))
        {
            _rig.Zoom(ZoomPerSecond * delta);
        }
        if (Input.IsActionPressed("zoom_out", true))
        {
            _rig.Zoom(-ZoomPerSecond * delta);
        }

        double panStep = PanPerSecond * delta * _rig.Current.Distance;
        if (Input.IsActionPressed("pan_left", true))
        {
            _rig.Pan(-panStep, 0);
        }
        if (Input.IsActionPressed("pan_right", true))
        {
            _rig.Pan(panStep, 0);
        }
        if (Input.IsActionPressed("pan_up", true))
        {
            _rig.Pan(0, panStep);
        }
        if (Input.IsActionPressed("pan_down", true))
        {
            _rig.Pan(0, -panStep);
        }

        // ---- M3 游戏键位（§10.5 R4-15..18；Z/C/X 相机语义保留 M2 方案）----
        if (Input.IsActionJustPressed("layer_prev", true))
        {
            _gm.CycleFocus(-1);
        }
        if (Input.IsActionJustPressed("layer_next", true))
        {
            _gm.CycleFocus(1);
        }
        if (Input.IsActionJustPressed("mirror_2d", true))
        {
            _board2D.ToggleMirror();
        }
        if (Input.IsActionJustPressed("toggle_helper_lines", true))
        {
            _gm.ToggleAux();
        }
        if (Input.IsActionJustPressed("toggle_colorblind", true))
        {
            _gm.ToggleColorblind();
        }
        if (Input.IsActionJustPressed("undo", true))
        {
            _gm.Undo();
        }

        double before = _spacing;
        if (Input.IsActionPressed("spacing_compress", true))
        {
            _spacing -= SpacingPerSecond * delta;
        }
        if (Input.IsActionPressed("spacing_expand", true))
        {
            _spacing += SpacingPerSecond * delta;
        }
        _spacing = Math.Clamp(_spacing, GameConfig.MinLayerSpacing, GameConfig.MaxLayerSpacing);
        if (Input.IsActionJustPressed("spacing_reset", true))
        {
            _spacing = GameConfig.DefaultLayerSpacing;
            _rig.Reset(_boardSize, _layers, _spacing, ViewportAspect());
        }
        if (Math.Abs(_spacing - before) > 1e-9)
        {
            OnSpacingChanged();
        }
    }

    private void OnSpacingChanged()
    {
        _board3D.Configure(_boardSize, _layers, _spacing);
        _board3D.Rebuild(_gm.Board, _gm.FocusZ);
        _rig.Update(_boardSize, _layers, _spacing, ViewportAspect(), GameConfig.DefaultYaw);
        RecomputeAux();
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

    // ---- GameManager 事件 → 视图联动 ----

    private void Refresh()
    {
        var palette = _gm.Colorblind ? Palette.Colorblind : Palette.Default;
        _board3D.ApplyPalette(palette);
        _board2D.Palette = palette;
        _board3D.Rebuild(_gm.Board, _gm.FocusZ);
        _board2D.QueueRedraw();
        RecomputeAux();
        UpdateStatus();
        if (!_gm.SwapPending)
        {
            _swapDialog.Hide();
        }
    }

    private void OnHoverChanged(Coordinate3D? cell)
    {
        _board3D.SetHover(cell); // 3D 蓝点跟随 2D 悬停（R4-12）
        RecomputeAux();
        _board2D.QueueRedraw();
    }

    private void RecomputeAux()
    {
        _board3D.SetAuxData(_gm.ComputeAux(_gm.Hover));
    }

    private void ShowSwapDialog()
    {
        _swapDialog.PopupCentered();
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
