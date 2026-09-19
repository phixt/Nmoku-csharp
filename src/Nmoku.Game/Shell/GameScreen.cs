using Godot;
using Nmoku.Core;
using Nmoku.Game.Theme;
using Nmoku.Game.Views;

namespace Nmoku.Game.Shell;

/// <summary>
/// 对局基座路由组件：左右分屏（3D 左视图 + 2D 右面板）+ 底部状态栏。
/// 自 M4R 起由 SceneRouter 挂载（KeepAlive：切回标题保留现场），Main 经属性引用接线。
/// </summary>
public sealed partial class GameScreen : Control
{
    public SubViewport Viewport3D { get; private set; } = null!;
    public Camera3D Camera { get; private set; } = null!;
    public Board3DView Board3D { get; private set; } = null!;
    public Board2DView Board2D { get; private set; } = null!;
    public Label StatusLabel { get; private set; } = null!;

    private Godot.Environment _environment = null!;

    private readonly int _boardSize;
    private readonly int _layers;
    private readonly double _spacing;

    public GameScreen(int boardSize, int layers, double spacing)
    {
        _boardSize = boardSize;
        _layers = layers;
        _spacing = spacing;
    }

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore; // 子控件各自接收鼠标，父级不拦截

        var hbox = new HBoxContainer();
        hbox.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(hbox);

        var viewportContainer = new SubViewportContainer
        {
            Stretch = true,
            // ExpandFill：Expand 只分配不填充，子视口会塌缩到最小宽 2px（首轮验收灰屏根因）
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = 0.62f,
        };
        hbox.AddChild(viewportContainer);

        // MSAA 8×：场景极小（≤1014 球体 + 数百线盒），细网格线在远景掠射角下 4× 仍有锯齿（M4R 反馈）
        Viewport3D = new SubViewport { OwnWorld3D = true, Msaa3D = Viewport.Msaa.Msaa8X };
        viewportContainer.AddChild(Viewport3D);

        _environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = Palette.Active.Background,
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = Palette.Active.AmbientLight,
            AmbientLightEnergy = 0.55f,
        };
        Viewport3D.AddChild(new WorldEnvironment { Environment = _environment });
        Viewport3D.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-55, 35, 0) });

        Board3D = new Board3DView { Name = "Board3DView" };
        Viewport3D.AddChild(Board3D);
        Board3D.Configure(_boardSize, _layers, _spacing);

        Camera = new Camera3D
        {
            KeepAspect = Camera3D.KeepAspectEnum.Height, // Godot fov 为垂直向，与 Three.js 语义一致（R1）
            Current = true,
        };
        Viewport3D.AddChild(Camera);

        Board2D = new Board2DView
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = 0.38f,
        };
        hbox.AddChild(Board2D);

        StatusLabel = new Label();
        StatusLabel.SetAnchorsAndOffsetsPreset(LayoutPreset.CenterBottom);
        StatusLabel.HorizontalAlignment = HorizontalAlignment.Center;
        StatusLabel.GrowHorizontal = GrowDirection.Both;
        StatusLabel.OffsetTop = -64;
        AddChild(StatusLabel);
    }

    /// <summary>主题切换（T）：3D 背景与环境光随套更新；棋盘/棋子由 Main.Refresh 经 ApplyPalette 通道刷新。</summary>
    public void ApplyTheme()
    {
        _environment.BackgroundColor = Palette.Active.Background;
        _environment.AmbientLightColor = Palette.Active.AmbientLight;
    }
}
