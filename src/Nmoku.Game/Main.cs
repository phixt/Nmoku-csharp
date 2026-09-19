using Godot;
using Nmoku.Core;
using Nmoku.Core.Camera;
using Nmoku.Game.Views;

/// <summary>
/// M2 场景根：3D 左视图 + 自由相机接线（决策 #9）。
/// 游戏状态机/对局接线在 M3/M4（GameManager），当前棋盘为演示棋子（占位）。
/// </summary>
public partial class Main : Node
{
    private const double OrbitDegPerPixel = 0.25;
    private const double ZoomPerSecond = 3.0;
    private const double SpacingPerSecond = 1.2;
    private const double PanPerSecond = 0.6; // ×当前距离，键盘平移速度

    private Camera3D _camera = null!;
    private Board3DView _boardView = null!;
    private CameraRig _rig = null!;
    private Board _demoBoard = null!;
    private readonly int _boardSize;
    private readonly int _layers;
    private double _spacing;
    private Vector2 _lastViewportSize;
    private bool _rotating;
    private bool _panning;

    public Main()
    {
        // M2 标定对照入口：Godot_console.exe --path src/Nmoku.Game -- --board=9 --layers=1 --spacing=3.5
        (_boardSize, _layers, _spacing) = ParseDebugArgs();
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

        _camera = new Camera3D
        {
            KeepAspect = Camera3D.KeepAspectEnum.Height, // Godot fov 为垂直向，与 Three.js 语义一致（R1）
            Current = true,
        };
        AddChild(_camera);

        AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color(0.10f, 0.11f, 0.13f),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = Colors.White,
                AmbientLightEnergy = 0.55f,
            },
        });
        AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-55, 35, 0) });

        _boardView = new Board3DView { Name = "Board3DView" };
        AddChild(_boardView);
        _boardView.Configure(_boardSize, _layers, _spacing);

        _demoBoard = new Board(_boardSize, _layers);
        PlaceDemoStones();

        _lastViewportSize = GetViewport().GetVisibleRect().Size;
        _rig = new CameraRig(_boardSize, _layers, _spacing, _lastViewportSize.X / _lastViewportSize.Y);
        ApplyCamera();

        GD.Print($"Nmoku M2: board3d ready (board={_boardSize}x{_boardSize}x{_layers}, spacing={_spacing:0.0}, " +
                 $"aspect={_lastViewportSize.X / _lastViewportSize.Y:0.00}, distance={_rig.Current.Distance:0.00}, " +
                 $"fov={_rig.Current.Fov:0.0}, pitch={_rig.Current.Pitch:0.0})");
    }

    public override void _Process(double delta)
    {
        HandleHeldInput(delta);
        HandleViewportChange();
        UpdateHover();
        _rig.Advance(delta);
        ApplyCamera();
    }

    public override void _Input(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } button:
                _rotating = button.Pressed;
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Middle } middle:
                _panning = middle.Pressed;
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true }:
                // 径向缩放：与 W/S 同路径，单调无跳变。锚点版有"远锚点大步进 + pitch 钳制瞬移"，
                // 等 M3 有格子拾取后以合法锚点恢复（TODO）
                _rig.Zoom(1);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true }:
                _rig.Zoom(-1);
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

        double before = _spacing;
        if (Input.IsActionPressed("spacing_compress"))
        {
            _spacing -= SpacingPerSecond * delta;
        }
        if (Input.IsActionPressed("spacing_expand"))
        {
            _spacing += SpacingPerSecond * delta;
        }
        _spacing = Math.Clamp(_spacing, GameConfig.MinLayerSpacing, GameConfig.MaxLayerSpacing);
        if (Input.IsActionJustPressed("spacing_reset"))
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
        _boardView.Configure(_boardSize, _layers, _spacing);
        _boardView.Rebuild(_demoBoard);
        _rig.Update(_boardSize, _layers, _spacing, ViewportAspect(), GameConfig.DefaultYaw);
    }

    private void HandleViewportChange()
    {
        // 按尺寸比较而非宽高比：杜绝浮点抖动触发公式重算（距离被拉回初始态的疑似来源之一）
        var size = GetViewport().GetVisibleRect().Size;
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

    /// <summary>悬停高亮占位：鼠标射线与各层平面求交，取最近层贴齐格心（R4 精确联动 M3 做）。</summary>
    private void UpdateHover()
    {
        if (_rotating || _panning)
        {
            _boardView.SetHover(null);
            return;
        }
        var mouse = _camera.GetViewport().GetMousePosition();
        var origin = _camera.ProjectRayOrigin(mouse);
        var dir = _camera.ProjectRayNormal(mouse);

        Coordinate3D? best = null;
        double bestT = double.MaxValue;
        for (int z = 0; z < _layers; z++)
        {
            // B8 朝向：层 = 世界 Z 处的竖直 XY 平面
            double planeZ = z * _spacing;
            if (Math.Abs(dir.Z) < 1e-6)
            {
                continue;
            }
            double t = (planeZ - origin.Z) / dir.Z;
            if (t <= 0 || t >= bestT)
            {
                continue;
            }
            double hitX = origin.X + dir.X * t;
            double hitY = origin.Y + dir.Y * t;
            // 连续范围判定（含格心半格边距）：修"高亮出现在棋盘外任意位置"（人类反馈 2026-09-19）
            if (hitX >= -0.5 && hitX <= _boardSize - 0.5 && hitY >= -0.5 && hitY <= _boardSize - 0.5)
            {
                best = new Coordinate3D((int)Math.Round(hitX), (int)Math.Round(hitY), z);
                bestT = t;
            }
        }
        _boardView.SetHover(best);
    }

    private double WorldPerPixel() =>
        2.0 * Math.Tan(CameraMath.ToRadians(_rig.Current.Fov) / 2.0) * _rig.Current.Distance
        / GetViewport().GetVisibleRect().Size.Y;

    private double ViewportAspect()
    {
        var size = GetViewport().GetVisibleRect().Size;
        return size.X / size.Y;
    }

    /// <summary>M2 演示棋子（占位，M3 接 GameManager 后删除）：中层横五连 + 竖列跨层展示透明度梯度。</summary>
    private void PlaceDemoStones()
    {
        int midX = _boardSize / 2;
        int midY = _boardSize / 2;
        for (int x = midX - 2; x <= midX + 2; x++)
        {
            _demoBoard.Set(x, midY, _layers / 2, CellState.Black);
        }
        for (int i = 0; i < 3; i++)
        {
            _demoBoard.Set(midX - 2 + i, midY + 2, _layers / 2, CellState.White);
        }
        for (int z = 0; z < _layers; z++)
        {
            _demoBoard.Set(_boardSize - 2, 2, z, z % 2 == 0 ? CellState.Black : CellState.White);
        }
        _boardView.Rebuild(_demoBoard);
    }

    /// <summary>M2 标定参数：`-- --board=9 --layers=1 --spacing=3.5`（Godot 用户参数在 `--` 之后）。</summary>
    private static (int Size, int Layers, double Spacing) ParseDebugArgs()
    {
        int size = GameConfig.DefaultBoardSize;
        int layers = GameConfig.DefaultLayerCount;
        double spacing = GameConfig.DefaultLayerSpacing;
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
        }
        return (size, layers, spacing);
    }
}
