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

    private Camera3D _camera = null!;
    private Board3DView _boardView = null!;
    private CameraRig _rig = null!;
    private Board _demoBoard = null!;
    private readonly int _boardSize;
    private readonly int _layers;
    private double _spacing;
    private double _lastAspect;
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

        _lastAspect = ViewportAspect();
        _rig = new CameraRig(_boardSize, _layers, _spacing, _lastAspect);
        ApplyCamera();

        GD.Print($"Nmoku M2: board3d ready (board={_boardSize}x{_boardSize}x{_layers}, spacing={_spacing:0.0}, " +
                 $"aspect={_lastAspect:0.00}, distance={_rig.Current.Distance:0.00}, " +
                 $"fov={_rig.Current.Fov:0.0}, pitch={_rig.Current.Pitch:0.0})");
    }

    public override void _Process(double delta)
    {
        HandleHeldInput(delta);
        HandleAspectChange();
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
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true } wheelUp:
                _rig.ZoomToAnchor(GroundAnchor(wheelUp.Position) ?? _rig.Target.Center, 1);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true } wheelDown:
                _rig.ZoomToAnchor(GroundAnchor(wheelDown.Position) ?? _rig.Target.Center, -1);
                break;
            case InputEventMouseMotion motion:
            {
                if (_rotating)
                {
                    // 手感方向待 M2 标定确认（决策 #9）
                    _rig.Orbit(motion.Relative.X * OrbitDegPerPixel, -motion.Relative.Y * OrbitDegPerPixel);
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
        if (Input.IsActionJustPressed("rotate_left"))
        {
            _rig.Orbit(-90);
        }
        if (Input.IsActionJustPressed("rotate_right"))
        {
            _rig.Orbit(90);
        }
        if (Input.IsActionPressed("zoom_in"))
        {
            _rig.Zoom(ZoomPerSecond * delta);
        }
        if (Input.IsActionPressed("zoom_out"))
        {
            _rig.Zoom(-ZoomPerSecond * delta);
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

    private void HandleAspectChange()
    {
        double aspect = ViewportAspect();
        if (Math.Abs(aspect - _lastAspect) > GameConfig.CameraEpsilon)
        {
            _lastAspect = aspect;
            _rig.Update(_boardSize, _layers, _spacing, aspect, GameConfig.DefaultYaw);
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
            double planeY = z * _spacing;
            if (Math.Abs(dir.Y) < 1e-6)
            {
                continue;
            }
            double t = (planeY - origin.Y) / dir.Y;
            if (t <= 0 || t >= bestT)
            {
                continue;
            }
            int x = (int)Math.Round(origin.X + dir.X * t);
            int y = (int)Math.Round(origin.Z + dir.Z * t);
            if (x >= 0 && x < _boardSize && y >= 0 && y < _boardSize)
            {
                best = new Coordinate3D(x, y, z);
                bestT = t;
            }
        }
        _boardView.SetHover(best);
    }

    /// <summary>滚轮缩放锚点：鼠标射线与地面 y=0 的交点；无交点（望向地平线）由调用方回退盘心。</summary>
    private WorldPoint? GroundAnchor(Vector2 mouse)
    {
        var origin = _camera.ProjectRayOrigin(mouse);
        var dir = _camera.ProjectRayNormal(mouse);
        if (Math.Abs(dir.Y) < 1e-6)
        {
            return null;
        }
        double t = -origin.Y / dir.Y;
        return t <= 0 ? null : new WorldPoint(origin.X + dir.X * t, 0, origin.Z + dir.Z * t);
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
