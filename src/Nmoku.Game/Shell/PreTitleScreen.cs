using Godot;
using Nmoku.Core;
using Nmoku.Game.Audio;
using Nmoku.Game.Theme;

namespace Nmoku.Game.Shell;

/// <summary>
/// 开始界面（预开始封面 ↔ 流体形变 ↔ 交互菜单一体化）：
/// <para>
/// · 封面态（Letter）：三段精密斜接的雕塑级 3D 字母 N（微错层纵深 + 悬浮黑白双子 + 鼠标微视差），
///   配右侧原生清晰 2D 标题排版（Nmoku / 立体五子棋 / 点击展开）。
/// · 流体形变（Morph，可双向可逆）：雕塑 N 三笔画沿纵深分离、舒展并渐隐，同时三层 5×5×3 线格
///   从中心波浪式逐层涟漪展开，悬浮双子沿弧线落入棋盘格位，透视相机沿曲线滑入 3/4 展示构图，
///   左上品牌角标与右侧菜单列按错峰级联入场。
/// · 菜单态（Menu）：左侧 5×5×3 真实 Core.Board + Core.AuxScan 可玩演示盘（柔和自转、拖拽惯性、
///   点击落子弹跳、13 向辅助线跟随主题与色弱），右侧调色板驱动菜单列（含 hover 强调条与轻移反馈）。
/// · 工程卫生：物理像素超采样 + MSAA 8×、隐藏时停渲染停进程、实现 <see cref="IKeepAliveRefreshable"/>
///   原地换肤/换语言不重置阶段与演示棋子、全文本走 <see cref="I18n.T"/>。
/// </para>
/// </summary>
public sealed partial class PreTitleScreen : Control, IKeepAliveRefreshable
{
    public event Action? StartRequested;
    public event Action? ResumeRequested;
    public event Action? CustomGameRequested;
    public event Action? LoadRequested;
    public event Action? GuideRequested;
    public event Action? SettingsRequested;
    public event Action? QuitRequested;

    // ============ 视觉与动效参数 ============
    private const float MorphDuration = 1.15f;
    private const float BoardSpacing = 1.55f;       // 3 层 5×5 演示盘层间距（中心对称：+s, 0, -s）
    private const float CellStep = 0.84f;           // 单格世界尺寸（总宽 4×0.84 = 3.36，紧凑精致）
    private const float StoneRadius = 0.31f;
    private const float HoverBallRadius = 0.25f;
    private const float MarkerRadius = 0.105f;
    private const float GridThickness = 0.028f;
    private const float ConnectorThickness = 0.024f;
    private const float ViewportSupersample = 1.5f;

    // 透视相机起止构图（Cover：N 偏左居中留白舒展；Menu：棋盘稳居左 56%，与右侧菜单列零碰撞）
    private static readonly Vector3 CoverTarget = new(1.12f, -0.02f, 0f);
    private static readonly Vector3 MenuTarget = new(1.72f, -0.10f, 0f);
    private const float CoverYaw = -0.30f;          // -17.2°：展现 N 三笔画的微错层纵深
    private const float CoverPitch = 0.13f;         // +7.4°
    private const float CoverDist = 9.4f;
    private const float CoverFov = 32f;

    private const float MenuYaw = 0.56f;            // +32.1°：经典立体 3/4 展示角
    private const float MenuPitch = 0.35f;          // +20.1°
    private const float MenuDist = 11.0f;
    private const float MenuFov = 34f;

    // 拖拽与惯性手感
    private const float DragYawSense = 0.009f;
    private const float DragPitchSense = 0.006f;
    private const float DragFollow = 14f;
    private const float InertiaDecay = 0.04f;
    private const float AutoOrbitSpeed = 0.22f;     // 未接管时的舒缓自转速度（rad/s）
    private const float MinMenuPitch = 0.08f;
    private const float MaxMenuPitch = 0.78f;

    // 菜单列设计基准（768×432 逻辑面：宽 248、右留白 44、上下余量充足；最小逻辑面 320×416 自适应不越界）
    private const float DesignFaceWidth = 768f;
    private const float DesignColumnWidth = 248f;
    private const float DesignRightMargin = 44f;
    private const float DesignButtonHeight = 36f;
    private const float DesignSeparation = 8f;
    private const float MinButtonHeight = 24f;
    private const int MaxMenuButtons = 7;

    private enum Phase { Letter, Morph, Menu }

    private Phase _phase = Phase.Letter;
    private float _morphT;              // 0 = 封面态，1 = 菜单态
    private int _morphDir = 1;
    private float _clock;               // 呼吸 / 微浮 / 提示脉动时钟

    // 演示棋盘（5×5×3，真实 Core 实例，独立于主对局）
    private readonly Board _board = new(5, 3);
    private PlayerColor _turn = PlayerColor.Black;
    private bool _colorblind;
    private bool _hasLiveGame;

    // 3D 场景节点
    private ColorRect _backdrop = null!;
    private ShaderMaterial _backdropMat = null!;
    private SubViewport _vp = null!;
    private ViewportHost _vpHost = null!;
    private Node3D _sceneRoot = null!;
    private Camera3D _cam = null!;
    private DirectionalLight3D _keyLight = null!;
    private DirectionalLight3D _fillLight = null!;
    private Godot.Environment _env = null!;

    // 雕塑级 N 字标（三段精确斜接挤出体）
    private Node3D _letterRoot = null!;
    private MeshInstance3D _nLeft = null!;
    private MeshInstance3D _nDiag = null!;
    private MeshInstance3D _nRight = null!;
    private StandardMaterial3D _nMatFront = null!;
    private StandardMaterial3D _nMatMid = null!;
    private StandardMaterial3D _nMatBack = null!;
    private float _letterHoverGlow;
    private bool _letterHovered;

    // 3 层展开棋盘（每层独立容器 + 网格线 + 5 根层连接立柱）
    private Node3D _boardRoot = null!;
    private readonly Node3D[] _layerRoots = new Node3D[3];
    private readonly StandardMaterial3D[] _layerGridMats = new StandardMaterial3D[3];
    private readonly List<GridBar> _gridBars = [];
    private readonly List<MeshInstance3D> _connectors = [];
    private StandardMaterial3D _connectorMat = null!;

    // 棋子（前 2 枚为封面悬浮双子，形变时沿弧线落入展示格位；后续为用户演示落子）
    private readonly List<StoneVisual> _stones = [];
    private SphereMesh _stoneMesh = null!;
    private StandardMaterial3D _stoneBlackMat = null!;
    private StandardMaterial3D _stoneWhiteMat = null!;

    // 辅助线（真实 Core.AuxScan + 节点池复用，零每帧分配）
    private MeshInstance3D _hoverBall = null!;
    private StandardMaterial3D _hoverBallMat = null!;
    private MeshInstance3D _auxLinesNode = null!;
    private ImmediateMesh _auxLinesMesh = null!;
    private StandardMaterial3D _auxLineMat = null!;
    private SphereMesh _auxMarkerMesh = null!;
    private StandardMaterial3D _auxMarkerSelfMat = null!;
    private StandardMaterial3D _auxMarkerEnemyMat = null!;
    private readonly List<MeshInstance3D> _auxMarkerPool = [];
    private Coordinate3D? _hoverCell;
    private Coordinate3D? _renderedHoverCell;
    private Vector2? _hoverScreen;
    private bool _auxDirty = true;

    // 相机 / 交互态
    private float _menuYaw = MenuYaw;
    private float _menuPitch = MenuPitch;
    private float _targetYaw = MenuYaw;
    private float _targetPitch = MenuPitch;
    private float _yawVel;
    private float _pitchVel;
    private bool _dragging;
    private bool _userTouched;
    private Vector2 _downPos;
    private ulong _downTimeMs;
    private Vector2 _parallaxTarget;
    private Vector2 _parallaxCurrent;

    // 2D UI 节点（封面排版 / 左上品牌角标 / 右侧菜单列 / 左下迷你工具条）
    private FontFile _fontLxgw = null!;
    private SystemFont _fontSerif = null!;

    private Control _coverUi = null!;
    private Label _coverBrand = null!;
    private ColorRect _coverRule = null!;
    private Label _coverCn = null!;
    private Label _coverSub = null!;
    private Button _coverPromptBtn = null!;
    private StyleBoxFlat _coverPromptNormal = null!;
    private StyleBoxFlat _coverPromptHover = null!;

    private Control _cornerUi = null!;
    private Label _cornerBrand = null!;
    private Label _cornerCn = null!;

    private VBoxContainer _menuCol = null!;
    private readonly List<MenuButtonItem> _menuItems = [];
    private MenuButtonItem _resumeItem = null!;
    private MenuButtonItem _startItem = null!;
    private MenuButtonItem _customItem = null!;
    private MenuButtonItem _loadItem = null!;
    private MenuButtonItem _guideItem = null!;
    private MenuButtonItem _settingsItem = null!;
    private MenuButtonItem _quitItem = null!;
    private Rect2 _menuRect;

    private HBoxContainer _bottomBar = null!;
    private Button _backCoverBtn = null!;
    private Button _clearDemoBtn = null!;
    private Panel _turnDot = null!;
    private StyleBoxFlat _turnDotBox = null!;
    private Label _demoStatusLabel = null!;

    private sealed record GridBar(
        MeshInstance3D Node, int Layer, bool Horizontal, int Index,
        Vector3 BasePos, float FullLength, float Stagger);

    private sealed class StoneVisual
    {
        public required MeshInstance3D Node { get; init; }
        public required int X { get; init; }
        public required int Y { get; init; }
        public required int Z { get; init; }
        public required PlayerColor Color { get; init; }
        public bool IsShowcase { get; init; }
        public Vector3 CoverAnchor { get; init; }
        public float PopProgress { get; set; } = 1f;
    }

    private sealed class MenuButtonItem
    {
        public required Control Slot { get; init; }
        public required Button Button { get; init; }
        public required ColorRect AccentBar { get; init; }
        public required bool IsPrimary { get; init; }
        public required Func<string> LabelProvider { get; init; }
        public float HoverAnim { get; set; }
        public bool Hovered { get; set; }
    }

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;

        _fontLxgw = GD.Load<FontFile>("res://assets/fonts/LXGWWenKaiLite-Regular.ttf");
        _fontSerif = new SystemFont { FontNames = ["Georgia", "Times New Roman", "serif"] };

        BuildBackdrop();
        BuildViewport3D();
        BuildSculptedN();
        BuildDemoBoard3D();
        BuildShowcaseStones();
        BuildAuxPool();
        BuildCoverUi();
        BuildCornerUi();
        BuildMenuUi();
        BuildBottomBar();

        ApplyTheme();
        RefreshLocale();
        ApplyMorphVisuals(0f);
        SyncLayout(Size);

        Resized += () =>
        {
            SyncViewportSize();
            SyncLayout(Size);
        };
    }

    public override void _Notification(int what)
    {
        if (what == NotificationVisibilityChanged && _vp != null)
        {
            bool visible = IsVisibleInTree();
            SetProcess(visible);
            _vp.RenderTargetUpdateMode = visible
                ? SubViewport.UpdateMode.Always
                : SubViewport.UpdateMode.Disabled;
            if (visible)
            {
                SyncViewportSize();
            }
        }
        base._Notification(what);
    }

    /// <summary>标题态进入（Main 状态钩子调用）：若已是菜单态则保持菜单态，绝不破坏「继续对局」状态。</summary>
    public void OnEntered()
    {
        SyncResumeVisibility();
        SyncViewportSize();
        if (_phase == Phase.Menu)
        {
            ApplyMorphVisuals(1f);
        }
    }

    /// <summary>继续对局按钮可见性（独立于阶段与 UI 显示开关，不再被 ShowMenuUi 覆盖）。</summary>
    public void UpdateResumeButton(bool visible)
    {
        _hasLiveGame = visible;
        SyncResumeVisibility();
        SyncLayout(Size);
    }

    /// <summary>色弱开关同步（演示盘辅助线实时响应）。</summary>
    public void SetColorblind(bool colorblind)
    {
        if (_colorblind == colorblind)
        {
            return;
        }
        _colorblind = colorblind;
        ApplyTheme();
    }

    // ---- IKeepAliveRefreshable：切主题 / 切语言原地刷新，保留当前阶段与演示落子 ----

    public void RefreshTheme() => ApplyTheme();

    public void RefreshLocale()
    {
        _coverCn.Text = I18n.T(Text.Title.GameName);
        _coverSub.Text = I18n.T(Text.Title.Subtitle);
        _coverPromptBtn.Text = I18n.T(Text.Title.ClickToEnter);
        _cornerCn.Text = I18n.T(Text.Title.GameName);
        foreach (var item in _menuItems)
        {
            item.Button.Text = item.LabelProvider();
        }
        _backCoverBtn.Text = $"↺ {I18n.T(Text.Title.BackToCover)}";
        _clearDemoBtn.Text = I18n.T(Text.Title.ClearDemo);
        UpdateDemoStatus();
    }

    // ---- 自测与可视化检查钩子 ----

    /// <summary>自测读取：菜单列实际布局矩形（相对本屏左上角）。</summary>
    public Rect2 MenuRectForTests => _menuRect;

    /// <summary>自测：按给定逻辑面重排菜单列并返回实际矩形。</summary>
    public Rect2 ApplyMenuLayoutForTests(Vector2 faceSize)
    {
        ApplyMenuLayout(faceSize);
        return _menuRect;
    }

    /// <summary>自测读取：菜单列内容最小尺寸。</summary>
    public Vector2 MenuContentMinSizeForTests => _menuCol.GetCombinedMinimumSize();

    /// <summary>自测读取：当前阶段（0=Letter, 1=Morph, 2=Menu）。</summary>
    internal int PhaseForTests => (int)_phase;

    /// <summary>自测读取：演示盘已落子数（含 2 枚初始展示子）。</summary>
    internal int DemoStoneCountForTests => _board.MoveCount();

    /// <summary>自测读取：继续对局按钮当前是否可见。</summary>
    internal bool ResumeVisibleForTests => _resumeItem.Slot.Visible;

    /// <summary>自测读取：视口渲染更新模式（隐藏时必须为 Disabled）。</summary>
    internal SubViewport.UpdateMode ViewportUpdateModeForTests => _vp.RenderTargetUpdateMode;

    /// <summary>自测 / 视觉抓帧：直接定位到指定形变进度（0=封面，0..1=形变中，1=菜单）。</summary>
    internal void SetVisualProgressForCheck(float t, Coordinate3D? previewHover = null)
    {
        _morphT = Mathf.Clamp(t, 0f, 1f);
        _phase = _morphT <= 0.001f ? Phase.Letter : _morphT >= 0.999f ? Phase.Menu : Phase.Morph;
        if (previewHover.HasValue)
        {
            _hoverCell = previewHover;
            _auxDirty = true;
        }
        else
        {
            _hoverCell = null;
            _auxDirty = true;
        }
        ApplyMorphVisuals(_morphT);
        if (_phase == Phase.Menu)
        {
            RebuildAux();
        }
    }

    /// <summary>自测：在演示盘上落一子（验证切主题/切语言后演示盘不被清零）。</summary>
    internal bool PlaceDemoStoneForTests(int x, int y, int z)
    {
        if (!_board.InBounds(x, y, z) || !_board.IsEmpty(x, y, z))
        {
            return false;
        }
        PlaceStone(new Coordinate3D(x, y, z), playAudio: false);
        return true;
    }

    // ================= 1. 氛围径向渐变背景 =================

    private void BuildBackdrop()
    {
        var shader = new Shader
        {
            Code = """
shader_type canvas_item;
uniform vec4 bg_base : source_color = vec4(0.10, 0.11, 0.13, 1.0);
uniform vec4 bg_glow : source_color = vec4(0.16, 0.21, 0.26, 1.0);
uniform vec4 bg_edge : source_color = vec4(0.06, 0.07, 0.09, 1.0);
uniform vec2 glow_center = vec2(0.35, 0.50);
uniform float aspect = 1.777;

void fragment() {
    vec2 d = UV - glow_center;
    d.x *= aspect;
    float dist = length(d);
    float glow = exp(-dist * dist * 2.15);
    float vig = smoothstep(0.35, 1.45, dist);
    vec3 col = mix(bg_base.rgb, bg_edge.rgb, vig * 0.55);
    col = mix(col, bg_glow.rgb, glow * 0.65);
    COLOR = vec4(col, 1.0);
}
""",
        };
        _backdropMat = new ShaderMaterial { Shader = shader };
        _backdrop = new ColorRect
        {
            Material = _backdropMat,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_backdrop);
    }

    // ================= 2. 物理像素超采样 3D 视口 =================

    private void BuildViewport3D()
    {
        _vp = new SubViewport
        {
            OwnWorld3D = true,
            TransparentBg = true,
            Msaa3D = Viewport.Msaa.Msaa8X,
            Size = new Vector2I(1280, 720),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        _env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.ClearColor,
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = Colors.White,
            AmbientLightEnergy = 0.62f,
        };
        _vp.World3D = new World3D { Environment = _env };
        AddChild(_vp);

        _sceneRoot = new Node3D { Name = "PreTitleScene" };
        _vp.AddChild(_sceneRoot);

        _cam = new Camera3D
        {
            Projection = Camera3D.ProjectionType.Perspective,
            KeepAspect = Camera3D.KeepAspectEnum.Height,
            Fov = CoverFov,
            Near = 0.2f,
            Far = 80f,
            Current = true,
        };
        _vp.AddChild(_cam);

        // 主光 + 柔和补光：让雕塑 N 与棋子具备温润的明暗体积感
        _keyLight = new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-42f, 34f, 0f),
            LightEnergy = 0.88f,
        };
        _vp.AddChild(_keyLight);

        _fillLight = new DirectionalLight3D
        {
            RotationDegrees = new Vector3(28f, -138f, 0f),
            LightEnergy = 0.36f,
        };
        _vp.AddChild(_fillLight);

        _vpHost = new ViewportHost
        {
            Viewport = _vp,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _vpHost.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_vpHost);
        SyncViewportSize();
    }

    /// <summary>SubViewport 纹理宿主（B16 v4 同款：最小尺寸恒零，与布局结构性解耦）。</summary>
    private sealed partial class ViewportHost : Control
    {
        public SubViewport? Viewport { get; set; }
        public override void _Process(double delta)
        {
            if (IsVisibleInTree())
            {
                QueueRedraw();
            }
        }
        public override void _Draw()
        {
            var tex = Viewport?.GetTexture();
            if (tex != null)
            {
                DrawTextureRect(tex, new Rect2(Vector2.Zero, Size), false, Colors.White);
            }
        }
    }

    /// <summary>按宿主逻辑尺寸 × 窗口缩放 × 超采样率同步视口物理像素（根治 csf 2.5 发糊）。</summary>
    private void SyncViewportSize()
    {
        if (_vp == null || Size.X < 4f || Size.Y < 4f)
        {
            return;
        }
        float scale = 1f;
        var win = GetWindow();
        if (win != null && DisplayServer.GetName() != "headless")
        {
            var s = win.GetFinalTransform().Scale;
            float f = MathF.Min(s.X, s.Y);
            if (f > 0.1f)
            {
                scale = f * ViewportSupersample;
            }
        }
        int w = Mathf.Clamp((int)MathF.Floor(Size.X * scale), 64, 4096);
        int h = Mathf.Clamp((int)MathF.Floor(Size.Y * scale), 64, 4096);
        if (_vp.Size != new Vector2I(w, h))
        {
            _vp.Size = new Vector2I(w, h);
        }
        _backdropMat.SetShaderParameter("aspect", Size.X / MathF.Max(1f, Size.Y));
    }

    // ================= 3. 雕塑级 3D 字母 N（精确斜接挤出体） =================

    private void BuildSculptedN()
    {
        _letterRoot = new Node3D { Name = "SculptedN" };
        _sceneRoot.AddChild(_letterRoot);

        _nMatFront = CreateSculptMaterial();
        _nMatMid = CreateSculptMaterial();
        _nMatBack = CreateSculptMaterial();

        const float halfH = 1.66f;
        const float outerX = 1.28f;
        const float innerX = 0.72f;
        const float depth = 0.42f;

        // 左竖干：[-outerX, -innerX] × [-halfH, +halfH]
        _nLeft = new MeshInstance3D
        {
            Name = "NLeft",
            Mesh = BuildExtrudedQuad(
                new Vector2(-outerX, -halfH),
                new Vector2(-innerX, -halfH),
                new Vector2(-innerX, halfH),
                new Vector2(-outerX, halfH),
                depth),
            MaterialOverride = _nMatFront,
        };

        // 斜干：上下水平边与左右竖干顶/底完全齐平（精确瑞士字体斜接，零突角！）
        _nDiag = new MeshInstance3D
        {
            Name = "NDiag",
            Mesh = BuildExtrudedQuad(
                new Vector2(innerX, -halfH),
                new Vector2(outerX, -halfH),
                new Vector2(-innerX, halfH),
                new Vector2(-outerX, halfH),
                depth * 0.92f),
            MaterialOverride = _nMatMid,
        };

        // 右竖干：[+innerX, +outerX] × [-halfH, +halfH]
        _nRight = new MeshInstance3D
        {
            Name = "NRight",
            Mesh = BuildExtrudedQuad(
                new Vector2(innerX, -halfH),
                new Vector2(outerX, -halfH),
                new Vector2(outerX, halfH),
                new Vector2(innerX, halfH),
                depth),
            MaterialOverride = _nMatBack,
        };

        _letterRoot.AddChild(_nLeft);
        _letterRoot.AddChild(_nDiag);
        _letterRoot.AddChild(_nRight);
    }

    private static StandardMaterial3D CreateSculptMaterial() => new()
    {
        Roughness = 0.34f,
        Metallic = 0.08f,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
    };

    /// <summary>由逆时针 4 顶点构造带精确平面法线的挤出 3D 棱柱（前/后/四侧 6 面共 12 三角形）。</summary>
    private static ArrayMesh BuildExtrudedQuad(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float depth)
    {
        float hz = depth * 0.5f;
        var verts = new List<Vector3>(36);
        var norms = new List<Vector3>(36);

        void AddTri(Vector3 a, Vector3 b, Vector3 c, Vector3 n)
        {
            verts.Add(a); verts.Add(b); verts.Add(c);
            norms.Add(n); norms.Add(n); norms.Add(n);
        }

        void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n)
        {
            AddTri(a, b, c, n);
            AddTri(a, c, d, n);
        }

        var f0 = new Vector3(p0.X, p0.Y, hz);
        var f1 = new Vector3(p1.X, p1.Y, hz);
        var f2 = new Vector3(p2.X, p2.Y, hz);
        var f3 = new Vector3(p3.X, p3.Y, hz);

        var b0 = new Vector3(p0.X, p0.Y, -hz);
        var b1 = new Vector3(p1.X, p1.Y, -hz);
        var b2 = new Vector3(p2.X, p2.Y, -hz);
        var b3 = new Vector3(p3.X, p3.Y, -hz);

        // 前面 (+Z) & 背面 (-Z)（Godot 正面顺时针/右手绕序：外法向由 (c-a)×(b-a) 决定，用前后双向稳妥构造）
        AddQuad(f0, f3, f2, f1, Vector3.Back);
        AddQuad(b0, b1, b2, b3, Vector3.Forward);

        // 四侧面
        Vector2[] pts = [p0, p1, p2, p3];
        Vector3[] front = [f0, f1, f2, f3];
        Vector3[] back = [b0, b1, b2, b3];
        for (int i = 0; i < 4; i++)
        {
            int next = (i + 1) % 4;
            Vector2 edge = pts[next] - pts[i];
            Vector3 sideNorm = new Vector3(edge.Y, -edge.X, 0f).Normalized();
            AddQuad(front[i], front[next], back[next], back[i], sideNorm);
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = norms.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    // ================= 4. 3 层 5×5×3 演示棋盘 =================

    private void BuildDemoBoard3D()
    {
        _boardRoot = new Node3D { Name = "DemoBoard" };
        _sceneRoot.AddChild(_boardRoot);

        float fullLen = 4f * CellStep;
        var unitBox = new BoxMesh { Size = Vector3.One };

        for (int z = 0; z < 3; z++)
        {
            var layerNode = new Node3D { Name = $"Layer_{z}" };
            _boardRoot.AddChild(layerNode);
            _layerRoots[z] = layerNode;

            var mat = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            };
            _layerGridMats[z] = mat;

            for (int i = 0; i < 5; i++)
            {
                float offset = (i - 2f) * CellStep;
                float stagger = MathF.Abs(i - 2f) * 0.045f;

                // 水平线（沿 X）
                var hLine = new MeshInstance3D
                {
                    Mesh = unitBox,
                    MaterialOverride = mat,
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                };
                layerNode.AddChild(hLine);
                _gridBars.Add(new GridBar(hLine, z, true, i, new Vector3(0f, offset, 0f), fullLen, stagger));

                // 竖直线（沿 Y）
                var vLine = new MeshInstance3D
                {
                    Mesh = unitBox,
                    MaterialOverride = mat,
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                };
                layerNode.AddChild(vLine);
                _gridBars.Add(new GridBar(vLine, z, false, i, new Vector3(offset, 0f, 0f), fullLen, stagger));
            }
        }

        // 5 根立体连接立柱（四角 + 天元，呼应正式对局 Board3DView 锚点）
        _connectorMat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };
        float halfSpan = 2f * CellStep;
        (float rx, float ry)[] rails =
        [
            (-halfSpan, -halfSpan), (halfSpan, -halfSpan),
            (-halfSpan, halfSpan), (halfSpan, halfSpan),
            (0f, 0f),
        ];
        foreach (var (rx, ry) in rails)
        {
            var rail = new MeshInstance3D
            {
                Mesh = unitBox,
                MaterialOverride = _connectorMat,
                Position = new Vector3(rx, ry, 0f),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            _boardRoot.AddChild(rail);
            _connectors.Add(rail);
        }
    }

    private static Vector3 CellToBoardLocal(int x, int y, int z) =>
        new((x - 2f) * CellStep, (y - 2f) * CellStep, (1 - z) * BoardSpacing);

    // ================= 5. 展示棋子与演示落子 =================

    private void BuildShowcaseStones()
    {
        _stoneMesh = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 28, Rings = 14 };
        _stoneBlackMat = new StandardMaterial3D { Roughness = 0.28f, Metallic = 0.10f };
        _stoneWhiteMat = new StandardMaterial3D { Roughness = 0.32f, Metallic = 0.04f };
        SeedShowcaseStones();
    }

    private void SeedShowcaseStones()
    {
        foreach (var s in _stones)
        {
            _boardRoot.RemoveChild(s.Node);
            s.Node.QueueFree();
        }
        _stones.Clear();
        _board.Reset();
        _turn = PlayerColor.Black;

        // 2 枚经典展示子：在封面态轻悬于 N 字标负空间，形变时沿流畅弧线落入斜对角互不遮挡格位
        AddStoneInternal(1, 1, 0, PlayerColor.Black, isShowcase: true, new Vector3(-0.34f, -0.78f, 0.36f));
        AddStoneInternal(3, 3, 1, PlayerColor.White, isShowcase: true, new Vector3(0.36f, 0.82f, -0.28f));
        _auxDirty = true;
        UpdateDemoStatus();
    }

    private void AddStoneInternal(int x, int y, int z, PlayerColor color, bool isShowcase, Vector3 coverAnchor)
    {
        _board.Set(x, y, z, color);
        var mi = new MeshInstance3D
        {
            Mesh = _stoneMesh,
            MaterialOverride = color == PlayerColor.Black ? _stoneBlackMat : _stoneWhiteMat,
            Position = CellToBoardLocal(x, y, z),
            Scale = Vector3.One * (isShowcase ? StoneRadius : 0.001f),
        };
        _boardRoot.AddChild(mi);
        _stones.Add(new StoneVisual
        {
            Node = mi,
            X = x,
            Y = y,
            Z = z,
            Color = color,
            IsShowcase = isShowcase,
            CoverAnchor = coverAnchor,
            PopProgress = isShowcase ? 1f : 0f,
        });
    }

    private void PlaceStone(Coordinate3D cell, bool playAudio)
    {
        AddStoneInternal(cell.X, cell.Y, cell.Z, _turn, isShowcase: false, Vector3.Zero);
        _turn = _turn.Opponent();
        UpdateDemoStatus();
        _auxDirty = true;
        if (playAudio)
        {
            AudioManager.Instance?.PlaySfx(SfxKind.Click);
        }
    }

    // ================= 6. 辅助线对象池（零每帧分配） =================

    private void BuildAuxPool()
    {
        _auxLineMat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            VertexColorUseAsAlbedo = true,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };
        _auxLinesMesh = new ImmediateMesh();
        _auxLinesNode = new MeshInstance3D
        {
            Name = "AuxLines",
            Mesh = _auxLinesMesh,
            MaterialOverride = _auxLineMat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        _boardRoot.AddChild(_auxLinesNode);

        _auxMarkerMesh = new SphereMesh { Radius = MarkerRadius, Height = MarkerRadius * 2f, RadialSegments = 12, Rings = 6 };
        _auxMarkerSelfMat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            NoDepthTest = true,
            RenderPriority = 10,
        };
        _auxMarkerEnemyMat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            NoDepthTest = true,
            RenderPriority = 10,
        };

        _hoverBallMat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };
        _hoverBall = new MeshInstance3D
        {
            Name = "HoverBall",
            Mesh = new SphereMesh { Radius = HoverBallRadius, Height = HoverBallRadius * 2f, RadialSegments = 20, Rings = 10 },
            MaterialOverride = _hoverBallMat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        _boardRoot.AddChild(_hoverBall);
    }

    private void RebuildAux()
    {
        var pal = Palette.For(_colorblind);
        bool show = _phase == Phase.Menu && _hoverCell is { } h && _board.InBounds(h) && _board.IsEmpty(h.X, h.Y, h.Z);
        _auxLinesMesh.ClearSurfaces();
        _auxLinesNode.Visible = false;
        _hoverBall.Visible = show;
        foreach (var m in _auxMarkerPool)
        {
            m.Visible = false;
        }
        if (!show || _hoverCell is not { } cell)
        {
            _renderedHoverCell = null;
            return;
        }

        _renderedHoverCell = cell;
        _hoverBall.Position = CellToBoardLocal(cell.X, cell.Y, cell.Z);

        var res = AuxScan.Scan(_board, _turn, cell, includePlanar: false, includeSpace: true);
        if (res.SpaceLines.Count > 0)
        {
            _auxLinesMesh.SurfaceBegin(Mesh.PrimitiveType.Lines, _auxLineMat);
            foreach (var line in res.SpaceLines)
            {
                var c = line.Role == AuxRole.Self ? pal.LineSelf : pal.LineEnemy;
                var vc = new Color(c.R, c.G, c.B, 0.92f);
                _auxLinesMesh.SurfaceSetColor(vc);
                _auxLinesMesh.SurfaceAddVertex(CellToBoardLocal(line.Start.X, line.Start.Y, line.Start.Z));
                _auxLinesMesh.SurfaceSetColor(vc);
                _auxLinesMesh.SurfaceAddVertex(CellToBoardLocal(line.End.X, line.End.Y, line.End.Z));
            }
            _auxLinesMesh.SurfaceEnd();
            _auxLinesNode.Visible = true;
        }

        int used = 0;
        foreach (var m in res.SpaceMarkers)
        {
            if (used >= _auxMarkerPool.Count)
            {
                var node = new MeshInstance3D
                {
                    Mesh = _auxMarkerMesh,
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                };
                _boardRoot.AddChild(node);
                _auxMarkerPool.Add(node);
            }
            var mi = _auxMarkerPool[used++];
            mi.Position = CellToBoardLocal(m.Position.X, m.Position.Y, m.Position.Z);
            mi.MaterialOverride = m.Role == AuxRole.Self ? _auxMarkerSelfMat : _auxMarkerEnemyMat;
            mi.Visible = true;
        }
    }

    // ================= 7. 原生清晰 2D UI 层 =================

    private void BuildCoverUi()
    {
        _coverUi = new Control { MouseFilter = MouseFilterEnum.Ignore };
        _coverUi.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_coverUi);

        var stack = new VBoxContainer { Name = "CoverStack", MouseFilter = MouseFilterEnum.Ignore };
        stack.AddThemeConstantOverride("separation", 8);
        _coverUi.AddChild(stack);

        _coverBrand = new Label
        {
            Text = "Nmoku",
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _coverBrand.AddThemeFontOverride("font", _fontSerif);
        _coverBrand.AddThemeFontSizeOverride("font_size", 50);
        stack.AddChild(_coverBrand);

        _coverRule = new ColorRect
        {
            CustomMinimumSize = new Vector2(40f, 2f),
            SizeFlagsHorizontal = SizeFlags.ShrinkBegin,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        stack.AddChild(_coverRule);

        _coverCn = new Label { MouseFilter = MouseFilterEnum.Ignore };
        _coverCn.AddThemeFontOverride("font", _fontLxgw);
        _coverCn.AddThemeFontSizeOverride("font_size", 22);
        stack.AddChild(_coverCn);

        _coverSub = new Label { MouseFilter = MouseFilterEnum.Ignore };
        _coverSub.AddThemeFontOverride("font", _fontLxgw);
        _coverSub.AddThemeFontSizeOverride("font_size", 11);
        stack.AddChild(_coverSub);

        var spacer = new Control { CustomMinimumSize = new Vector2(0, 10), MouseFilter = MouseFilterEnum.Ignore };
        stack.AddChild(spacer);

        _coverPromptNormal = CreatePillStyle(Colors.Transparent, Colors.White, 16);
        _coverPromptHover = CreatePillStyle(Colors.Transparent, Colors.White, 16);
        _coverPromptBtn = new Button
        {
            FocusMode = FocusModeEnum.None,
            SizeFlagsHorizontal = SizeFlags.ShrinkBegin,
            CustomMinimumSize = new Vector2(138f, 32f),
            MouseDefaultCursorShape = CursorShape.PointingHand,
        };
        _coverPromptBtn.AddThemeFontOverride("font", _fontLxgw);
        _coverPromptBtn.AddThemeFontSizeOverride("font_size", 13);
        _coverPromptBtn.AddThemeStyleboxOverride("normal", _coverPromptNormal);
        _coverPromptBtn.AddThemeStyleboxOverride("hover", _coverPromptHover);
        _coverPromptBtn.AddThemeStyleboxOverride("pressed", _coverPromptHover);
        _coverPromptBtn.Pressed += StartMorphForward;
        stack.AddChild(_coverPromptBtn);
    }

    private void BuildCornerUi()
    {
        _cornerUi = new VBoxContainer
        {
            Position = new Vector2(22f, 14f),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        ((VBoxContainer)_cornerUi).AddThemeConstantOverride("separation", 0);

        _cornerBrand = new Label
        {
            Text = "Nmoku",
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _cornerBrand.AddThemeFontOverride("font", _fontSerif);
        _cornerBrand.AddThemeFontSizeOverride("font_size", 26);
        _cornerUi.AddChild(_cornerBrand);

        _cornerCn = new Label { MouseFilter = MouseFilterEnum.Ignore };
        _cornerCn.AddThemeFontOverride("font", _fontLxgw);
        _cornerCn.AddThemeFontSizeOverride("font_size", 12);
        _cornerUi.AddChild(_cornerCn);

        AddChild(_cornerUi);
    }

    private void BuildMenuUi()
    {
        _menuCol = new VBoxContainer
        {
            AnchorLeft = 1f,
            AnchorRight = 1f,
            AnchorTop = 0.5f,
            AnchorBottom = 0.5f,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _menuCol.AddThemeConstantOverride("separation", (int)DesignSeparation);
        AddChild(_menuCol);

        _startItem = AddMenuButton(true, () => I18n.T(Text.Title.Start), () => StartRequested?.Invoke());
        _resumeItem = AddMenuButton(false, () => I18n.T(Text.Title.ContinueGame), () => ResumeRequested?.Invoke());
        _customItem = AddMenuButton(false, () => I18n.T(Text.Title.CustomGame), () => CustomGameRequested?.Invoke());
        _loadItem = AddMenuButton(false, () => I18n.T(Text.Title.LoadGame), () => LoadRequested?.Invoke());
        _guideItem = AddMenuButton(false, () => I18n.T(Text.Title.Guide), () => GuideRequested?.Invoke());
        _settingsItem = AddMenuButton(false, () => I18n.T(Text.Settings.Title), () => SettingsRequested?.Invoke());
        _quitItem = AddMenuButton(false, () => I18n.T(Text.Title.Quit), () => QuitRequested?.Invoke());

        SyncResumeVisibility();
    }

    private MenuButtonItem AddMenuButton(bool isPrimary, Func<string> labelProvider, Action onPressed)
    {
        var slot = new Control
        {
            CustomMinimumSize = new Vector2(0, DesignButtonHeight),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Ignore,
        };

        var btn = new Button
        {
            FocusMode = FocusModeEnum.None,
            Alignment = HorizontalAlignment.Left,
            MouseDefaultCursorShape = CursorShape.PointingHand,
        };
        btn.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        btn.AddThemeFontOverride("font", _fontLxgw);
        btn.AddThemeFontSizeOverride("font_size", 14);
        btn.Pressed += onPressed;
        slot.AddChild(btn);

        var accentBar = new ColorRect
        {
            MouseFilter = MouseFilterEnum.Ignore,
            AnchorLeft = 0f,
            AnchorRight = 0f,
            AnchorTop = 0.24f,
            AnchorBottom = 0.76f,
            OffsetLeft = 5f,
            OffsetRight = 8f,
            Visible = !isPrimary,
        };
        btn.AddChild(accentBar);

        var item = new MenuButtonItem
        {
            Slot = slot,
            Button = btn,
            AccentBar = accentBar,
            IsPrimary = isPrimary,
            LabelProvider = labelProvider,
        };
        btn.MouseEntered += () => item.Hovered = true;
        btn.MouseExited += () => item.Hovered = false;

        _menuCol.AddChild(slot);
        _menuItems.Add(item);
        return item;
    }

    private void BuildBottomBar()
    {
        _bottomBar = new HBoxContainer
        {
            AnchorLeft = 0f,
            AnchorRight = 0f,
            AnchorTop = 1f,
            AnchorBottom = 1f,
            OffsetLeft = 20f,
            OffsetTop = -40f,
            OffsetRight = 460f,
            OffsetBottom = -14f,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _bottomBar.AddThemeConstantOverride("separation", 8);
        AddChild(_bottomBar);

        _backCoverBtn = CreateMiniButton();
        _backCoverBtn.Pressed += StartMorphBack;
        _bottomBar.AddChild(_backCoverBtn);

        _clearDemoBtn = CreateMiniButton();
        _clearDemoBtn.Pressed += SeedShowcaseStones;
        _bottomBar.AddChild(_clearDemoBtn);

        var statusWrap = new HBoxContainer
        {
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        statusWrap.AddThemeConstantOverride("separation", 5);
        _bottomBar.AddChild(statusWrap);

        _turnDotBox = new StyleBoxFlat
        {
            CornerRadiusTopLeft = 5,
            CornerRadiusTopRight = 5,
            CornerRadiusBottomLeft = 5,
            CornerRadiusBottomRight = 5,
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            BorderWidthTop = 1,
            BorderWidthBottom = 1,
        };
        _turnDot = new Panel
        {
            CustomMinimumSize = new Vector2(9f, 9f),
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _turnDot.AddThemeStyleboxOverride("panel", _turnDotBox);
        statusWrap.AddChild(_turnDot);

        _demoStatusLabel = new Label
        {
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _demoStatusLabel.AddThemeFontOverride("font", _fontLxgw);
        _demoStatusLabel.AddThemeFontSizeOverride("font_size", 11);
        statusWrap.AddChild(_demoStatusLabel);
    }

    private Button CreateMiniButton()
    {
        var b = new Button
        {
            FocusMode = FocusModeEnum.None,
            CustomMinimumSize = new Vector2(0, 24f),
            MouseDefaultCursorShape = CursorShape.PointingHand,
        };
        b.AddThemeFontOverride("font", _fontLxgw);
        b.AddThemeFontSizeOverride("font_size", 11);
        return b;
    }

    private static StyleBoxFlat CreatePillStyle(Color bg, Color border, int radius, int padH = 14, int padV = 4) => new()
    {
        BgColor = bg,
        BorderColor = border,
        BorderWidthLeft = 1,
        BorderWidthRight = 1,
        BorderWidthTop = 1,
        BorderWidthBottom = 1,
        CornerRadiusTopLeft = radius,
        CornerRadiusTopRight = radius,
        CornerRadiusBottomLeft = radius,
        CornerRadiusBottomRight = radius,
        ContentMarginLeft = padH,
        ContentMarginRight = padH,
        ContentMarginTop = padV,
        ContentMarginBottom = padV,
    };

    private void SyncResumeVisibility()
    {
        if (_resumeItem != null)
        {
            _resumeItem.Slot.Visible = _hasLiveGame;
        }
    }

    private void UpdateDemoStatus()
    {
        if (_demoStatusLabel == null)
        {
            return;
        }
        string turnName = _turn == PlayerColor.Black
            ? I18n.T(Text.Turn.BlackTurn)
            : I18n.T(Text.Turn.WhiteTurn);
        _demoStatusLabel.Text = I18n.T(Text.Title.DemoHint, turnName);

        var pal = Palette.Active;
        bool isDark = pal.Background.Luminance < 0.4f;
        _turnDotBox.BgColor = _turn == PlayerColor.Black
            ? (isDark ? new Color(0.15f, 0.16f, 0.19f) : pal.StoneBlack)
            : pal.StoneWhite;
        _turnDotBox.BorderColor = _turn == PlayerColor.Black && isDark
            ? pal.TextDim
            : pal.PanelBorder;
    }

    // ================= 8. 自适应布局 =================

    private void SyncLayout(Vector2 faceSize)
    {
        if (faceSize.X < 10f || faceSize.Y < 10f)
        {
            return;
        }
        ApplyCoverLayout(faceSize);
        ApplyMenuLayout(faceSize);
    }

    private void ApplyCoverLayout(Vector2 faceSize)
    {
        if (_coverUi.GetNodeOrNull<Control>("CoverStack") is not { } stack)
        {
            return;
        }
        // 右侧黄金区域居中排版：与左侧雕塑 N 形成舒展平衡的非对称封面构图
        float leftX = Mathf.Clamp(faceSize.X * 0.54f, 16f, faceSize.X - 240f);
        float topY = Mathf.Max(28f, (faceSize.Y - 175f) * 0.48f);
        stack.Position = new Vector2(leftX, topY);
    }

    /// <summary>菜单列随逻辑面自适应：设计面（768×432）以上保持舒展比例，最小窗口（320×416）完整落界内。</summary>
    private void ApplyMenuLayout(Vector2 faceSize)
    {
        float marginX = Mathf.Clamp(faceSize.X * DesignRightMargin / DesignFaceWidth, 12f, DesignRightMargin);
        float colW = Mathf.Clamp(faceSize.X - 2f * marginX, 140f, DesignColumnWidth);

        int count = MaxMenuButtons;
        float maxAvailH = Mathf.Max(160f, faceSize.Y - 32f);
        float designTotalH = count * DesignButtonHeight + (count - 1) * DesignSeparation;
        float targetH = Mathf.Min(designTotalH, maxAvailH);
        int sep = (int)MathF.Round(Mathf.Clamp(DesignSeparation * (targetH / designTotalH), 4f, DesignSeparation));
        float buttonH = Mathf.Max(MinButtonHeight, (targetH - (count - 1) * sep) / count);

        int visibleCount = _menuItems.Count(m => m.Slot.Visible);
        if (visibleCount == 0)
        {
            visibleCount = count;
        }
        float visibleTotalH = visibleCount * buttonH + (visibleCount - 1) * sep;

        _menuCol.OffsetLeft = -(colW + marginX);
        _menuCol.OffsetRight = -marginX;
        _menuCol.OffsetTop = -visibleTotalH * 0.5f;
        _menuCol.OffsetBottom = visibleTotalH * 0.5f;
        _menuCol.AddThemeConstantOverride("separation", sep);

        int fontSize = buttonH < 30f ? 12 : 14;
        foreach (var item in _menuItems)
        {
            item.Slot.CustomMinimumSize = new Vector2(0f, buttonH);
            item.Button.AddThemeFontSizeOverride("font_size", fontSize);
        }

        _menuRect = new Rect2(
            faceSize.X - marginX - colW,
            (faceSize.Y - visibleTotalH) * 0.5f,
            colW,
            visibleTotalH);
    }

    // ================= 9. 全量主题 / 色弱适配（原地刷新） =================

    private void ApplyTheme()
    {
        var pal = Palette.For(_colorblind);
        bool isDark = pal.Background.Luminance < 0.4f;

        // 1) 氛围径向背景
        Color glow = isDark
            ? pal.Background.Lightened(0.11f).Lerp(pal.Accent, 0.14f)
            : pal.Background.Lightened(0.06f);
        Color edge = isDark
            ? pal.Background.Darkened(0.32f)
            : pal.Background.Darkened(0.08f);
        _backdropMat.SetShaderParameter("bg_base", pal.Background);
        _backdropMat.SetShaderParameter("bg_glow", glow);
        _backdropMat.SetShaderParameter("bg_edge", edge);

        // 2) 3D 灯光与雕塑 N 材质（微明暗层次凸显三笔画纵深）
        _env.AmbientLightColor = pal.AmbientLight;
        _env.AmbientLightEnergy = isDark ? 0.55f : 0.72f;
        _keyLight.LightEnergy = isDark ? 0.92f : 0.78f;
        _fillLight.LightEnergy = isDark ? 0.34f : 0.42f;

        Color nCore = isDark
            ? pal.TextPrimary.Lerp(pal.Accent, 0.24f)
            : pal.Accent;
        _nMatFront.AlbedoColor = new Color(nCore.Lightened(isDark ? 0.08f : 0.06f), _nMatFront.AlbedoColor.A);
        _nMatMid.AlbedoColor = new Color(nCore, _nMatMid.AlbedoColor.A);
        _nMatBack.AlbedoColor = new Color(nCore.Darkened(isDark ? 0.16f : 0.14f), _nMatBack.AlbedoColor.A);

        // 3) 棋子 / 辅助线 / 悬停球
        _stoneBlackMat.AlbedoColor = pal.StoneBlack;
        _stoneWhiteMat.AlbedoColor = pal.StoneWhite;
        _hoverBallMat.AlbedoColor = new Color(pal.HoverHighlight.R, pal.HoverHighlight.G, pal.HoverHighlight.B, 0.78f);
        _auxMarkerSelfMat.AlbedoColor = new Color(pal.LineSelf.R, pal.LineSelf.G, pal.LineSelf.B, 0.90f);
        _auxMarkerEnemyMat.AlbedoColor = new Color(pal.LineEnemy.R, pal.LineEnemy.G, pal.LineEnemy.B, 0.90f);

        // 4) 封面 & 角标排版
        _coverBrand.AddThemeColorOverride("font_color", pal.TextPrimary);
        _coverRule.Color = pal.Accent;
        _coverCn.AddThemeColorOverride("font_color", pal.TextPrimary);
        _coverSub.AddThemeColorOverride("font_color", pal.TextDim);

        _coverPromptNormal.BgColor = isDark
            ? new Color(pal.ButtonBg.R, pal.ButtonBg.G, pal.ButtonBg.B, 0.55f)
            : new Color(pal.PanelBg.R, pal.PanelBg.G, pal.PanelBg.B, 0.78f);
        _coverPromptNormal.BorderColor = new Color(pal.Accent.R, pal.Accent.G, pal.Accent.B, 0.55f);
        _coverPromptHover.BgColor = pal.Accent;
        _coverPromptHover.BorderColor = pal.Accent;
        _coverPromptBtn.AddThemeColorOverride("font_color", pal.TextPrimary);
        _coverPromptBtn.AddThemeColorOverride("font_hover_color", Colors.White);
        _coverPromptBtn.AddThemeColorOverride("font_pressed_color", Colors.White);

        _cornerBrand.AddThemeColorOverride("font_color", pal.Accent);
        _cornerCn.AddThemeColorOverride("font_color", pal.TextDim);

        // 5) 菜单按钮样式（主按钮醒目强调，次按钮温润通透卡片）
        foreach (var item in _menuItems)
        {
            StyleBoxFlat normal;
            StyleBoxFlat hover;
            StyleBoxFlat pressed;

            if (item.IsPrimary)
            {
                normal = CreatePillStyle(pal.Accent, pal.Accent.Lightened(0.12f), 7, padH: 16, padV: 4);
                hover = CreatePillStyle(pal.Accent.Lightened(0.08f), pal.Accent.Lightened(0.22f), 7, padH: 16, padV: 4);
                pressed = CreatePillStyle(pal.Accent.Darkened(0.10f), pal.Accent, 7, padH: 16, padV: 4);
                item.Button.AddThemeColorOverride("font_color", Colors.White);
                item.Button.AddThemeColorOverride("font_hover_color", Colors.White);
                item.Button.AddThemeColorOverride("font_pressed_color", Colors.White);
                item.AccentBar.Color = new Color(1f, 1f, 1f, 0.85f);
            }
            else
            {
                Color cardBg = isDark
                    ? new Color(pal.PanelBg.R, pal.PanelBg.G, pal.PanelBg.B, 0.78f)
                    : new Color(pal.PanelBg.R, pal.PanelBg.G, pal.PanelBg.B, 0.82f);
                Color cardHover = isDark
                    ? new Color(pal.ButtonHover.R, pal.ButtonHover.G, pal.ButtonHover.B, 0.94f)
                    : new Color(pal.ButtonHover.R, pal.ButtonHover.G, pal.ButtonHover.B, 0.92f);
                Color cardBorder = new Color(pal.PanelBorder.R, pal.PanelBorder.G, pal.PanelBorder.B, isDark ? 0.36f : 0.55f);

                normal = CreatePillStyle(cardBg, cardBorder, 7, padH: 16, padV: 4);
                hover = CreatePillStyle(cardHover, pal.Accent, 7, padH: 16, padV: 4);
                pressed = CreatePillStyle(cardHover.Darkened(0.08f), pal.Accent, 7, padH: 16, padV: 4);
                item.Button.AddThemeColorOverride("font_color", pal.TextPrimary);
                item.Button.AddThemeColorOverride("font_hover_color", pal.TextPrimary);
                item.Button.AddThemeColorOverride("font_pressed_color", pal.TextPrimary);
                item.AccentBar.Color = pal.Accent;
            }

            item.Button.AddThemeStyleboxOverride("normal", normal);
            item.Button.AddThemeStyleboxOverride("hover", hover);
            item.Button.AddThemeStyleboxOverride("pressed", pressed);
        }

        // 6) 底部工具条
        Color miniBg = new Color(pal.PanelBg.R, pal.PanelBg.G, pal.PanelBg.B, 0.70f);
        Color miniBorder = new Color(pal.PanelBorder.R, pal.PanelBorder.G, pal.PanelBorder.B, 0.45f);
        var miniNormal = CreatePillStyle(miniBg, miniBorder, 6, padH: 10, padV: 2);
        var miniHover = CreatePillStyle(pal.ButtonHover, pal.Accent, 6, padH: 10, padV: 2);
        foreach (var b in new[] { _backCoverBtn, _clearDemoBtn })
        {
            b.AddThemeStyleboxOverride("normal", miniNormal);
            b.AddThemeStyleboxOverride("hover", miniHover);
            b.AddThemeStyleboxOverride("pressed", miniHover);
            b.AddThemeColorOverride("font_color", pal.TextDim);
            b.AddThemeColorOverride("font_hover_color", pal.TextPrimary);
            b.AddThemeColorOverride("font_pressed_color", pal.TextPrimary);
        }
        _demoStatusLabel.AddThemeColorOverride("font_color", pal.TextDim);
        UpdateDemoStatus();

        // 重刷当前阶段网格与辅助线颜色
        ApplyMorphVisuals(_morphT);
        _auxDirty = true;
    }

    // ================= 10. 流体形变驱动（完全可逆） =================

    private void ApplyMorphVisuals(float t)
    {
        var pal = Palette.For(_colorblind);
        float smooth = SmoothStep(0f, 1f, t);

        // 背景光晕中心随构图轻柔漂移
        _backdropMat.SetShaderParameter("glow_center", new Vector2(
            Mathf.Lerp(0.35f, 0.30f, smooth),
            Mathf.Lerp(0.50f, 0.52f, smooth)));

        // --- Stage 1: 雕塑 N 三笔画沿纵深分离成三层薄片并流体融入棋盘 (t: 0.00 -> 0.68) ---
        float nOut = SmoothStep(0.00f, 0.64f, t);
        float nEase = EaseInOutCubic(nOut);
        float nAlpha = 1f - SmoothStep(0.24f, 0.66f, t);
        bool nVisible = nAlpha > 0.01f;
        _letterRoot.Visible = nVisible;
        if (nVisible)
        {
            float breatheY = Mathf.Sin(_clock * 1.25f) * 0.042f * (1f - nOut);
            _letterRoot.Position = new Vector3(0f, breatheY, 0f);
            float hoverLift = 1f + _letterHoverGlow * 0.030f * (1f - nOut);
            _letterRoot.Scale = Vector3.One * hoverLift;

            // 微浮雕错层（+0.05/0/-0.05）保持封面斜接精密利落，形变时流体展开至前/中/后三层纵深并薄化融散
            float thin = Mathf.Lerp(1f, 0.12f, nEase);
            float spreadY = Mathf.Lerp(1f, 0.92f, nEase);
            float spreadX = Mathf.Lerp(1f, 1.18f, nEase);

            _nLeft.Position = new Vector3(
                Mathf.Lerp(0f, 0.92f, nEase),
                0f,
                Mathf.Lerp(0.05f, BoardSpacing, nEase));
            _nLeft.Scale = new Vector3(spreadX, spreadY, thin);

            _nDiag.Position = new Vector3(0f, 0f, 0f);
            _nDiag.Rotation = new Vector3(0f, 0f, Mathf.Lerp(0f, 0.46f, nEase));
            _nDiag.Scale = new Vector3(Mathf.Lerp(1f, 0.72f, nEase), spreadY, thin);

            _nRight.Position = new Vector3(
                Mathf.Lerp(0f, -0.92f, nEase),
                0f,
                Mathf.Lerp(-0.05f, -BoardSpacing, nEase));
            _nRight.Scale = new Vector3(spreadX, spreadY, thin);

            ApplySculptAlpha(_nMatFront, nAlpha, _letterHoverGlow * (1f - nOut));
            ApplySculptAlpha(_nMatMid, nAlpha, _letterHoverGlow * 0.85f * (1f - nOut));
            ApplySculptAlpha(_nMatBack, nAlpha, _letterHoverGlow * 0.7f * (1f - nOut));
        }

        // --- Stage 2: 三层 5×5 棋盘波浪涟漪展开 (t: 0.16 -> 0.86) ---
        bool boardVisible = t > 0.14f;
        _boardRoot.Visible = boardVisible || _stones.Count > 0;
        float[] layerBaseAlpha = [0.82f, 0.58f, 0.36f]; // 前层最清晰、后层柔和通透
        for (int z = 0; z < 3; z++)
        {
            float lz = SmoothStep(0.16f + z * 0.08f, 0.66f + z * 0.08f, t);
            float zPos = Mathf.Lerp((1 - z) * 0.32f, (1 - z) * BoardSpacing, EaseOutQuint(lz));
            _layerRoots[z].Position = new Vector3(0f, 0f, zPos);
            _layerRoots[z].Visible = lz > 0.005f;
            _layerGridMats[z].AlbedoColor = new Color(pal.GridLine.R, pal.GridLine.G, pal.GridLine.B, layerBaseAlpha[z] * lz);
        }

        foreach (var bar in _gridBars)
        {
            float start = 0.14f + bar.Layer * 0.08f + bar.Stagger;
            float end = 0.62f + bar.Layer * 0.08f + bar.Stagger;
            float p = EaseOutQuint(SmoothStep(start, end, t));
            if (p <= 0.005f)
            {
                bar.Node.Visible = false;
                continue;
            }
            bar.Node.Visible = true;
            bar.Node.Position = bar.BasePos;
            float len = bar.FullLength * p;
            float thick = GridThickness * Mathf.Lerp(0.35f, 1f, p);
            bar.Node.Scale = bar.Horizontal
                ? new Vector3(len, thick, thick)
                : new Vector3(thick, len, thick);
        }

        // 5 根层连接立柱柔和伸展 (t: 0.42 -> 0.86)
        float connP = EaseOutQuint(SmoothStep(0.42f, 0.86f, t));
        float connLen = 2f * BoardSpacing * connP;
        _connectorMat.AlbedoColor = new Color(pal.ConnectorLine.R, pal.ConnectorLine.G, pal.ConnectorLine.B, 0.42f * connP);
        foreach (var rail in _connectors)
        {
            rail.Visible = connP > 0.01f;
            rail.Scale = new Vector3(ConnectorThickness, ConnectorThickness, Mathf.Max(0.001f, connLen));
        }

        // --- Stage 3: 棋子沿流畅弧线落位 / 弹性盛放 ---
        float stoneFlight = SmoothStep(0.30f, 0.90f, t);
        float stoneEase = EaseInOutCubic(stoneFlight);
        for (int i = 0; i < _stones.Count; i++)
        {
            var s = _stones[i];
            Vector3 dst = CellToBoardLocal(s.X, s.Y, s.Z);
            if (s.IsShowcase)
            {
                // 封面态轻盈悬浮于 N 负空间，形变时沿温和弧线滑入棋盘格位
                float floatY = Mathf.Sin(_clock * 1.6f + i * 2.4f) * 0.07f * (1f - stoneFlight);
                Vector3 src = s.CoverAnchor + new Vector3(0f, floatY, 0f);
                float arc = Mathf.Sin(stoneEase * Mathf.Pi) * 0.45f * (i == 0 ? 1f : -1f);
                Vector3 pos = src.Lerp(dst, stoneEase) + new Vector3(arc * 0.35f, MathF.Abs(arc) * 0.28f, 0f);
                float r = Mathf.Lerp(0.22f, StoneRadius, stoneEase);
                s.Node.Visible = true;
                s.Node.Position = pos;
                s.Node.Scale = Vector3.One * r;
            }
            else
            {
                float p = EaseOutBack(SmoothStep(0.56f, 0.94f, t)) * EaseOutBack(s.PopProgress);
                s.Node.Visible = p > 0.01f;
                s.Node.Position = dst;
                s.Node.Scale = Vector3.One * (StoneRadius * Mathf.Max(0.001f, p));
            }
        }

        // --- Stage 4: 透视相机曲线滑行 + 微视差 ---
        float camE = EaseInOutCubic(t);
        float swirl = Mathf.Sin(camE * Mathf.Pi);
        float yaw = Mathf.Lerp(CoverYaw, _menuYaw, camE) + 0.16f * swirl;
        float pitch = Mathf.Lerp(CoverPitch, _menuPitch, camE) + 0.04f * swirl;
        float dist = Mathf.Lerp(CoverDist, MenuDist, camE) + 0.85f * swirl;
        float fov = Mathf.Lerp(CoverFov, MenuFov, camE);
        Vector3 target = CoverTarget.Lerp(MenuTarget, camE);

        // 封面态叠加鼠标柔和视差，菜单态自动淡出避免干扰落子瞄准
        float parallaxWeight = 1f - SmoothStep(0f, 0.45f, t);
        yaw += _parallaxCurrent.X * 0.14f * parallaxWeight;
        pitch += _parallaxCurrent.Y * 0.09f * parallaxWeight;

        Vector3 offset = new(
            dist * Mathf.Cos(pitch) * Mathf.Sin(yaw),
            dist * Mathf.Sin(pitch),
            dist * Mathf.Cos(pitch) * Mathf.Cos(yaw));
        _cam.Fov = fov;
        _cam.Position = target + offset;
        _cam.LookAt(target, Vector3.Up);

        // --- Stage 5: 2D UI 错峰编排 ---
        // 封面排版：0.00 -> 0.34 轻柔上滑淡出
        float coverAlpha = 1f - SmoothStep(0.00f, 0.34f, t);
        _coverUi.Visible = coverAlpha > 0.01f;
        _coverUi.Modulate = new Color(1f, 1f, 1f, coverAlpha);
        _coverUi.Position = new Vector2(-14f * (1f - coverAlpha), -6f * (1f - coverAlpha));
        _coverPromptBtn.Disabled = t > 0.20f;

        // 左上品牌角标：0.56 -> 0.92 平滑滑入（真正位于 (22, 14) 清晰可见！）
        float cornerP = EaseOutQuint(SmoothStep(0.56f, 0.92f, t));
        _cornerUi.Visible = cornerP > 0.01f;
        _cornerUi.Modulate = new Color(1f, 1f, 1f, cornerP);
        _cornerUi.Position = new Vector2(22f - 14f * (1f - cornerP), 14f);

        // 右侧菜单列：0.48 -> 1.00 逐按钮错峰滑入（用 MouseFilter 控交互，避免 Disabled 变灰破坏级联观感）
        bool menuActive = t > 0.46f;
        _menuCol.Visible = menuActive;
        if (menuActive)
        {
            int visIdx = 0;
            foreach (var item in _menuItems)
            {
                if (!item.Slot.Visible)
                {
                    continue;
                }
                float bStart = 0.48f + visIdx * 0.045f;
                float bEnd = MathF.Min(1f, bStart + 0.24f);
                float bp = EaseOutQuint(SmoothStep(bStart, bEnd, t));
                item.Button.Modulate = new Color(1f, 1f, 1f, bp);
                item.Button.MouseFilter = t < 0.82f ? MouseFilterEnum.Ignore : MouseFilterEnum.Stop;
                float slideX = 16f * (1f - bp) - item.HoverAnim * 4f;
                item.Button.OffsetLeft = slideX;
                item.Button.OffsetRight = slideX;
                if (!item.IsPrimary)
                {
                    item.AccentBar.Modulate = new Color(1f, 1f, 1f, item.HoverAnim);
                }
                visIdx++;
            }
        }

        // 左下演示控制条：0.66 -> 1.00 柔和上浮显现
        float barP = EaseOutQuint(SmoothStep(0.66f, 1.00f, t));
        _bottomBar.Visible = barP > 0.01f;
        _bottomBar.Modulate = new Color(1f, 1f, 1f, barP);
        _bottomBar.OffsetTop = -40f + 10f * (1f - barP);
        _bottomBar.OffsetBottom = -14f + 10f * (1f - barP);

        if (_phase != Phase.Menu)
        {
            _hoverBall.Visible = false;
            _auxLinesNode.Visible = false;
            foreach (var m in _auxMarkerPool)
            {
                m.Visible = false;
            }
        }
    }

    private void ApplySculptAlpha(StandardMaterial3D mat, float alpha, float glow)
    {
        var c = mat.AlbedoColor;
        mat.AlbedoColor = new Color(c.R, c.G, c.B, alpha);
        mat.EmissionEnabled = glow > 0.01f;
        if (mat.EmissionEnabled)
        {
            mat.Emission = Palette.Active.Accent;
            mat.EmissionEnergyMultiplier = glow * 0.35f;
        }
    }

    // ================= 11. 主循环与交互 =================

    public override void _Process(double delta)
    {
        float dt = (float)Mathf.Min(delta, 0.05);
        _clock += dt;
        SyncViewportSize();

        // 鼠标微视差阻尼
        float pFactor = 1f - MathF.Exp(-8f * dt);
        _parallaxCurrent = _parallaxCurrent.Lerp(_parallaxTarget, pFactor);

        // 封面态 N 悬停柔光与提示呼吸
        float targetGlow = _phase == Phase.Letter && _letterHovered ? 1f : 0f;
        _letterHoverGlow = Mathf.Lerp(_letterHoverGlow, targetGlow, 1f - MathF.Exp(-10f * dt));
        if (_phase == Phase.Letter)
        {
            float pulse = 0.84f + 0.16f * Mathf.Sin(_clock * 2.4f);
            _coverPromptBtn.Modulate = new Color(1f, 1f, 1f, pulse);
        }

        // 按钮 hover 微交互阻尼（左移 4px + 强调竖条显现）
        float hoverFactor = 1f - MathF.Exp(-16f * dt);
        foreach (var item in _menuItems)
        {
            float targetH = item.Hovered ? 1f : 0f;
            item.HoverAnim = Mathf.Lerp(item.HoverAnim, targetH, hoverFactor);
        }

        // 新落演示子弹性缩放推进
        foreach (var s in _stones)
        {
            if (s.PopProgress < 1f)
            {
                s.PopProgress = Mathf.Min(1f, s.PopProgress + dt / 0.28f);
            }
        }

        if (_phase == Phase.Morph)
        {
            _morphT += dt / MorphDuration * _morphDir;
            if (_morphT >= 1f)
            {
                _morphT = 1f;
                _phase = Phase.Menu;
                _auxDirty = true;
            }
            else if (_morphT <= 0f)
            {
                _morphT = 0f;
                _phase = Phase.Letter;
                _dragging = false;
                _hoverCell = null;
            }
        }
        else if (_phase == Phase.Menu)
        {
            if (_dragging)
            {
                float follow = 1f - MathF.Exp(-DragFollow * dt);
                float nextYaw = Mathf.Lerp(_menuYaw, _targetYaw, follow);
                float nextPitch = Mathf.Lerp(_menuPitch, _targetPitch, follow);
                _yawVel = (nextYaw - _menuYaw) / MathF.Max(0.001f, dt) * 0.45f;
                _pitchVel = (nextPitch - _menuPitch) / MathF.Max(0.001f, dt) * 0.45f;
                _menuYaw = nextYaw;
                _menuPitch = nextPitch;
            }
            else if (MathF.Abs(_yawVel) > 0.015f || MathF.Abs(_pitchVel) > 0.015f)
            {
                _menuYaw += _yawVel * dt;
                _menuPitch = Mathf.Clamp(_menuPitch + _pitchVel * dt, MinMenuPitch, MaxMenuPitch);
                float damp = MathF.Pow(InertiaDecay, dt);
                _yawVel *= damp;
                _pitchVel *= damp;
            }
            else if (!_userTouched)
            {
                // 修复原自转死代码：未拖拽接管前柔和连续自转
                _menuYaw += AutoOrbitSpeed * dt;
            }

            if (_hoverScreen.HasValue && !_dragging)
            {
                _hoverCell = PickCell(_hoverScreen.Value);
            }
            if (_auxDirty || !Nullable.Equals(_hoverCell, _renderedHoverCell))
            {
                RebuildAux();
                _auxDirty = false;
            }
        }

        ApplyMorphVisuals(_morphT);
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseMotion mmAny && Size.X > 1f && Size.Y > 1f)
        {
            _parallaxTarget = new Vector2(
                Mathf.Clamp((mmAny.Position.X / Size.X - 0.5f) * 2f, -1f, 1f),
                Mathf.Clamp((mmAny.Position.Y / Size.Y - 0.5f) * 2f, -1f, 1f));
        }

        if (_phase == Phase.Letter)
        {
            if (@event is InputEventMouseMotion mm)
            {
                _letterHovered = InLetterZone(mm.Position);
                MouseDefaultCursorShape = _letterHovered ? CursorShape.PointingHand : CursorShape.Arrow;
            }
            else if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } mb
                     && InLetterZone(mb.Position))
            {
                StartMorphForward();
            }
            return;
        }

        if (_phase != Phase.Menu)
        {
            return;
        }

        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left } mb2)
        {
            if (mb2.Pressed)
            {
                // 避免在右侧菜单区触发拖拽
                if (_menuRect.Grow(8f).HasPoint(mb2.Position))
                {
                    return;
                }
                _dragging = true;
                _userTouched = true;
                _downPos = mb2.Position;
                _downTimeMs = Time.GetTicksMsec();
                _targetYaw = _menuYaw;
                _targetPitch = _menuPitch;
                _yawVel = 0f;
                _pitchVel = 0f;
            }
            else if (_dragging)
            {
                _dragging = false;
                float moved = (mb2.Position - _downPos).Length();
                if (moved < 6f && Time.GetTicksMsec() - _downTimeMs < 450)
                {
                    var picked = PickCell(mb2.Position);
                    if (picked is { } cell && _board.InBounds(cell) && _board.IsEmpty(cell.X, cell.Y, cell.Z))
                    {
                        PlaceStone(cell, playAudio: true);
                    }
                }
                else
                {
                    _yawVel = Mathf.Clamp(_yawVel, -2.6f, 2.6f);
                    _pitchVel = Mathf.Clamp(_pitchVel, -1.6f, 1.6f);
                }
            }
        }
        else if (@event is InputEventMouseMotion mm2)
        {
            if (_dragging)
            {
                _targetYaw -= mm2.Relative.X * DragYawSense;
                _targetPitch = Mathf.Clamp(_targetPitch + mm2.Relative.Y * DragPitchSense, MinMenuPitch, MaxMenuPitch);
            }
            else
            {
                _hoverScreen = mm2.Position;
                var picked = PickCell(mm2.Position);
                MouseDefaultCursorShape = picked is { } c && _board.IsEmpty(c.X, c.Y, c.Z)
                    ? CursorShape.PointingHand
                    : CursorShape.Arrow;
            }
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseMotion mm && _phase == Phase.Menu && !_dragging)
        {
            _hoverScreen = mm.Position;
        }
    }

    private bool InLetterZone(Vector2 screenPos)
    {
        if (Size.X < 10f || Size.Y < 10f)
        {
            return false;
        }
        // 以雕塑 N 包围盒投影到屏幕并留适当交互边距
        Vector3[] corners =
        [
            new(-1.4f, -1.8f, -0.4f), new(1.4f, -1.8f, -0.4f),
            new(-1.4f, 1.8f, 0.4f), new(1.4f, 1.8f, 0.4f),
        ];
        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;
        Vector2 vpSize = _vp.Size;
        foreach (var c in corners)
        {
            Vector2 p = _cam.UnprojectPosition(_letterRoot.ToGlobal(c)) / vpSize * Size;
            minX = MathF.Min(minX, p.X);
            maxX = MathF.Max(maxX, p.X);
            minY = MathF.Min(minY, p.Y);
            maxY = MathF.Max(maxY, p.Y);
        }
        return screenPos.X >= minX - 24f && screenPos.X <= maxX + 24f
            && screenPos.Y >= minY - 24f && screenPos.Y <= maxY + 24f;
    }

    private Coordinate3D? PickCell(Vector2 screenPos)
    {
        if (Size.X < 4f || Size.Y < 4f || _menuRect.Grow(4f).HasPoint(screenPos))
        {
            return null;
        }
        Vector2 vpPos = screenPos / Size * (Vector2)_vp.Size;
        Vector3 origin = _cam.ProjectRayOrigin(vpPos);
        Vector3 dir = _cam.ProjectRayNormal(vpPos);

        Coordinate3D? best = null;
        float bestDist = float.MaxValue;
        Vector3 normal = _boardRoot.GlobalTransform.Basis.Z;

        for (int z = 0; z < 3; z++)
        {
            float layerZ = (1 - z) * BoardSpacing;
            var plane = new Plane(normal, _boardRoot.ToGlobal(new Vector3(0f, 0f, layerZ)));
            var hit = plane.IntersectsRay(origin, dir);
            if (hit == null)
            {
                continue;
            }
            Vector3 local = _boardRoot.ToLocal(hit.Value);
            float fx = local.X / CellStep + 2f;
            float fy = local.Y / CellStep + 2f;
            int gx = (int)MathF.Round(fx);
            int gy = (int)MathF.Round(fy);
            if (gx < 0 || gx >= 5 || gy < 0 || gy >= 5)
            {
                continue;
            }
            if (MathF.Abs(fx - gx) > 0.44f || MathF.Abs(fy - gy) > 0.44f)
            {
                continue;
            }
            float d = (hit.Value - origin).Length();
            if (d < bestDist)
            {
                bestDist = d;
                best = new Coordinate3D(gx, gy, z);
            }
        }
        return best;
    }

    private void StartMorphForward()
    {
        if (_phase == Phase.Menu)
        {
            return;
        }
        _phase = Phase.Morph;
        _morphDir = 1;
        MouseDefaultCursorShape = CursorShape.Arrow;
    }

    private void StartMorphBack()
    {
        if (_phase == Phase.Letter)
        {
            return;
        }
        _phase = Phase.Morph;
        _morphDir = -1;
        _dragging = false;
        _hoverCell = null;
        MouseDefaultCursorShape = CursorShape.Arrow;
    }

    // ================= 缓动函数 =================

    private static float SmoothStep(float edge0, float edge1, float x)
    {
        float t = Mathf.Clamp((x - edge0) / MathF.Max(1e-5f, edge1 - edge0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private static float EaseInOutCubic(float t) =>
        t < 0.5f ? 4f * t * t * t : 1f - MathF.Pow(-2f * t + 2f, 3f) * 0.5f;

    private static float EaseOutQuint(float t) =>
        1f - MathF.Pow(1f - Mathf.Clamp(t, 0f, 1f), 5f);

    private static float EaseOutBack(float t)
    {
        float x = Mathf.Clamp(t, 0f, 1f);
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        return 1f + c3 * MathF.Pow(x - 1f, 3f) + c1 * MathF.Pow(x - 1f, 2f);
    }
}
