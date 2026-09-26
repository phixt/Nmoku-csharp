using Godot;
using Nmoku.Core;
using Nmoku.Game.Theme;
using Nmoku.Game.Views;

namespace Nmoku.Game.Shell;

/// <summary>
/// 对局基座路由组件：左右分屏（3D 左视图 + 2D 右面板）+ 右下角行棋指示器（批10，对局/复盘共用）。
/// 自 M4R 起由 SceneRouter 挂载（KeepAlive：切回标题保留现场），Main 经属性引用接线。
/// </summary>
public sealed partial class GameScreen : Control
{
    public SubViewport Viewport3D { get; private set; } = null!;
    public ViewportHost Host3D { get; private set; } = null!;
    public Camera3D Camera { get; private set; } = null!;
    public Board3DView Board3D { get; private set; } = null!;
    public Board2DView Board2D { get; private set; } = null!;
    public TurnIndicator TurnIndicator { get; private set; } = null!;

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

        var viewportHost = new ViewportHost
        {
            // B16 v4：自绘视口纹理（见 ViewportHost 注释），布局尺寸与视口尺寸零耦合。
            // ExpandFill：Expand 只分配不填充，子视口会塌缩到最小宽 2px（首轮验收灰屏根因）
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = 0.62f,
        };
        hbox.AddChild(viewportHost);
        Host3D = viewportHost;

        // MSAA 8×：场景极小（≤1014 球体 + 数百线盒），细网格线在远景掠射角下 4× 仍有锯齿（M4R 反馈）
        Viewport3D = new SubViewport { OwnWorld3D = true, Msaa3D = Viewport.Msaa.Msaa8X };
        viewportHost.Viewport = Viewport3D;
        viewportHost.AddChild(Viewport3D);

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

        TurnIndicator = new TurnIndicator();
        AddChild(TurnIndicator);
        ApplyStatusStyle();
    }

    /// <summary>主题切换（T）：3D 背景与环境光随套更新；棋盘/棋子由 Main.Refresh 经 ApplyPalette 通道刷新。</summary>
    public void ApplyTheme()
    {
        _environment.BackgroundColor = Palette.Active.Background;
        _environment.AmbientLightColor = Palette.Active.AmbientLight;
        ApplyStatusStyle();
    }

    /// <summary>
    /// 3D 视口宿主（B16 v4）：自绘 SubViewport 纹理（拉伸铺满自身矩形）。弃用 SubViewportContainer——
    /// 其 stretch=false 时 C++ 侧 get_minimum_size 直接返回视口尺寸、不经脚本虚方法
    /// （_GetMinimumSize 清零无效，B16 v3 实测回环重现："几次放大聚焦左上角"）。
    /// 本宿主最小尺寸恒零（Control 默认），布局尺寸与视口尺寸结构性解耦；每帧重绘跟随纹理。
    /// 视口由 Main.SyncViewport3DResolution 按「宿主逻辑尺寸×内容缩放」喂原生像素，纹理与
    /// 物理像素 1:1（高内容缩放下不再发糊）。
    /// </summary>
    public sealed partial class ViewportHost : Control
    {
        public SubViewport? Viewport { get; set; }

        public override void _Process(double delta) => QueueRedraw();

        public override void _Draw()
        {
            var texture = Viewport?.GetTexture();
            if (texture != null)
            {
                DrawTextureRect(texture, new Rect2(Vector2.Zero, Size), false, Colors.White);
            }
        }
    }

    /// <summary>状态字随主题取色并加描边（M4 二轮：状态字在棋盘底色上不可读）。批10 起宿主为右下角行棋指示器。</summary>
    private void ApplyStatusStyle() => TurnIndicator.ApplyStyle();
}
