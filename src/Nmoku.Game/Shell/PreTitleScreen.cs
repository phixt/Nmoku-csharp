using Godot;
using Nmoku.Core;
using Nmoku.Game.Audio;
using Nmoku.Game.Theme;

namespace Nmoku.Game.Shell;

/// <summary>
/// 开始界面（预开始封面 ↔ 流体形变 ↔ 交互菜单一体化）：
/// <para>
/// · 宝石与焦散美学（借鉴 caustic-gem / motion-cube）：雕塑级 3D 晶体字母 N（5 阶高保色色板 +
///   窄带 Bayer 4×4 抖动 + 内部折射虹彩 + 等宽棱边高光）、黑曜星光蓝/冰瓷月光石弧面宝石棋子、
///   底部硬边分层焦散光盘，以及克制的等轴几何视差背景层。
/// · 封面态（Letter）：黑白双子沿交叉倾斜轨道差速绕 N 立体公转（真实前后遮挡），右侧配原生清晰 2D 排版。
/// · 流体形变（Morph，可双向可逆）：双子自当前轨道瞬时位置沿平滑弧线飞入棋盘格位；晶体 N 三笔画沿纵深
///   分离薄化并流体融入三层波浪展开的 5×5×3 棋盘；底部焦散由椭圆渐变为棱镜光斑。
/// · 自中心旋转 + 略偏左构图：相机始终以棋盘/N 自身中心 (0,0,0) 为旋转轴，经 <see cref="Camera3D.HOffset"/>
///   在视锥空间温和偏左构图——任意角度自转/拖拽均原地绕自身中心旋转，零甩动漂移。
/// · 工程卫生：物理像素超采样 + MSAA 8×、隐藏停进程停视口、<see cref="IKeepAliveRefreshable"/> 原地换肤换语。
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
    private const float BoardSpacing = 1.52f;       // 3 层 5×5 演示盘层间距（以原点为几何中心：+s, 0, -s）
    private const float CellStep = 0.82f;           // 单格世界尺寸（总宽 4×0.82 = 3.28）
    private const float StoneRadius = 0.30f;
    private const float HoverBallRadius = 0.24f;
    private const float MarkerRadius = 0.105f;
    private const float GridThickness = 0.027f;
    private const float ConnectorThickness = 0.023f;
    private const float ViewportSupersample = 1.5f;

    // 相机始终绕世界原点 (0,0,0) 旋转，通过 HOffset/VOffset 实现「以自身为中心、略偏左」的稳态构图
    private const float CoverHOffset = 1.38f;       // 封面态：N 与公转双子稳居左侧黄金位，与右侧排版留出干净间距
    private const float MenuHOffset = 1.18f;        // 菜单态：棋盘居中略偏左（约 37% 屏宽），原地自转零甩动
    private const float CoverVOffset = -0.08f;
    private const float MenuVOffset = -0.10f;

    private const float CoverYaw = -0.28f;          // -16°：展现晶体 N 三笔画微错层纵深与切面折光
    private const float CoverPitch = 0.14f;         // +8°
    private const float CoverDist = 10.0f;
    private const float CoverFov = 32f;

    private const float MenuYaw = 0.56f;            // +32°：立体 3/4 展示角
    private const float MenuPitch = 0.35f;          // +20°
    private const float MenuDist = 10.9f;
    private const float MenuFov = 34f;

    // 黑白双子绕 N 差速公转参数（两条交叉倾斜 3D 椭圆轨道，黑子快、白子慢，自然错落追越）
    private const float BlackOrbitSpeed = 0.72f;    // rad/s
    private const float WhiteOrbitSpeed = 0.43f;    // rad/s（差速比 ≈ 1.67）
    private const float BlackOrbitPhase0 = 2.35f;
    private const float WhiteOrbitPhase0 = -0.58f;

    // 拖拽与惯性手感
    private const float DragYawSense = 0.009f;
    private const float DragPitchSense = 0.006f;
    private const float DragFollow = 14f;
    private const float InertiaDecay = 0.04f;
    private const float AutoOrbitSpeed = 0.22f;     // 未接管时的舒缓原地自转（rad/s）
    private const float MinMenuPitch = 0.08f;
    private const float MaxMenuPitch = 0.76f;

    // 菜单列设计基准（768×432 逻辑面：宽 232、右留白 56、间距 9、按钮高 36 —— 不挤不空、平衡舒展）
    private const float DesignFaceWidth = 768f;
    private const float DesignColumnWidth = 232f;
    private const float DesignRightMargin = 56f;
    private const float DesignButtonHeight = 36f;
    private const float DesignSeparation = 9f;
    private const float MinButtonHeight = 24f;
    private const int MaxMenuButtons = 7;

    private enum Phase { Letter, Morph, Menu }

    private Phase _phase = Phase.Letter;
    private float _morphT;
    private int _morphDir = 1;
    private float _clock;
    private float _polishSweep = 0f;    // 首次入场晶体扫光 0 -> 1

    // 演示棋盘（5×5×3，真实 Core 实例，独立于主对局）
    private readonly Board _board = new(5, 3);
    private PlayerColor _turn = PlayerColor.Black;
    private bool _colorblind;
    private bool _hasLiveGame;

    // 背景与 3D 场景节点
    private ColorRect _backdrop = null!;
    private ShaderMaterial _backdropMat = null!;
    private GeoParallaxLayer _geoBg = null!;
    private SubViewport _vp = null!;
    private ViewportHost _vpHost = null!;
    private Node3D _sceneRoot = null!;
    private Camera3D _cam = null!;
    private Godot.Environment _env = null!;

    // 底部硬边分层焦散光盘（借鉴 caustic-gem）
    private MeshInstance3D _causticDisc = null!;
    private ShaderMaterial _causticMat = null!;

    // 雕塑级 3D 晶体字母 N（精确斜接挤出体 + CrystalNShader）
    private Node3D _letterRoot = null!;
    private MeshInstance3D _nLeft = null!;
    private MeshInstance3D _nDiag = null!;
    private MeshInstance3D _nRight = null!;
    private ShaderMaterial _nMatFront = null!;
    private ShaderMaterial _nMatMid = null!;
    private ShaderMaterial _nMatBack = null!;
    private float _letterHoverGlow;
    private bool _letterHovered;

    // 3 层展开棋盘（严格以原点 (0,0,0) 为中心）
    private Node3D _boardRoot = null!;
    private readonly Node3D[] _layerRoots = new Node3D[3];
    private readonly StandardMaterial3D[] _layerGridMats = new StandardMaterial3D[3];
    private readonly List<GridBar> _gridBars = [];
    private readonly List<MeshInstance3D> _connectors = [];
    private StandardMaterial3D _connectorMat = null!;

    // 弧面宝石棋子（前 2 枚封面态差速绕 N 公转，形变时从瞬时轨道位置弧线落位）
    private readonly List<StoneVisual> _stones = [];
    private SphereMesh _stoneMesh = null!;
    private ShaderMaterial _stoneBlackMat = null!;
    private ShaderMaterial _stoneWhiteMat = null!;

    // 辅助线对象池（真实 Core.AuxScan，零每帧分配）
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

    // 2D UI 层
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
        public Vector3 LaunchAnchor { get; set; }
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
        BuildGeoParallaxLayer();
        BuildViewport3D();
        BuildCausticGround();
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

    /// <summary>标题态进入：若已是菜单态则保持菜单态，绝不破坏「继续对局」状态。</summary>
    public void OnEntered()
    {
        SyncResumeVisibility();
        SyncViewportSize();
        if (_phase == Phase.Menu)
        {
            ApplyMorphVisuals(1f);
        }
    }

    /// <summary>继续对局按钮可见性（独立于阶段与 UI 开关）。</summary>
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

    public Rect2 MenuRectForTests => _menuRect;

    public Rect2 ApplyMenuLayoutForTests(Vector2 faceSize)
    {
        ApplyMenuLayout(faceSize);
        return _menuRect;
    }

    public Vector2 MenuContentMinSizeForTests => _menuCol.GetCombinedMinimumSize();
    internal int PhaseForTests => (int)_phase;
    internal int DemoStoneCountForTests => _board.MoveCount();
    internal bool ResumeVisibleForTests => _resumeItem.Slot.Visible;
    internal SubViewport.UpdateMode ViewportUpdateModeForTests => _vp.RenderTargetUpdateMode;

    internal void SetVisualProgressForCheck(float t, Coordinate3D? previewHover = null, float clockOverride = -1f)
    {
        if (clockOverride >= 0f)
        {
            _clock = clockOverride;
        }
        _polishSweep = 1f;
        _morphT = Mathf.Clamp(t, 0f, 1f);
        _phase = _morphT <= 0.001f ? Phase.Letter : _morphT >= 0.999f ? Phase.Menu : Phase.Morph;
        CaptureStoneLaunchAnchors();
        _hoverCell = previewHover;
        _auxDirty = true;
        ApplyMorphVisuals(_morphT);
        if (_phase == Phase.Menu)
        {
            RebuildAux();
        }
    }

    internal bool PlaceDemoStoneForTests(int x, int y, int z)
    {
        if (!_board.InBounds(x, y, z) || !_board.IsEmpty(x, y, z))
        {
            return false;
        }
        PlaceStone(new Coordinate3D(x, y, z), playAudio: false);
        return true;
    }

    // ================= 1. 氛围背景 + 克制等轴视差几何层 =================

    private void BuildBackdrop()
    {
        var shader = new Shader
        {
            Code = """
shader_type canvas_item;
uniform vec4 bg_base : source_color = vec4(0.10, 0.11, 0.13, 1.0);
uniform vec4 bg_glow : source_color = vec4(0.16, 0.21, 0.26, 1.0);
uniform vec4 bg_edge : source_color = vec4(0.06, 0.07, 0.09, 1.0);
uniform vec2 glow_center = vec2(0.38, 0.50);
uniform float aspect = 1.777;

void fragment() {
    vec2 d = UV - glow_center;
    d.x *= aspect;
    float dist = length(d);
    float glow = exp(-dist * dist * 2.25);
    float vig = smoothstep(0.38, 1.48, dist);
    vec3 col = mix(bg_base.rgb, bg_edge.rgb, vig * 0.52);
    col = mix(col, bg_glow.rgb, glow * 0.62);
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

    private void BuildGeoParallaxLayer()
    {
        _geoBg = new GeoParallaxLayer { MouseFilter = MouseFilterEnum.Ignore };
        _geoBg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_geoBg);
    }

    /// <summary>借鉴 motion-cube StartPage 的克制等轴几何与微粒背景（极低对比、随鼠标轻微视差，不喧宾夺主）。</summary>
    private sealed partial class GeoParallaxLayer : Control
    {
        public Color LineColor { get; set; } = new(1f, 1f, 1f, 0.07f);
        public Color FillTintA { get; set; } = new(0.40f, 0.72f, 1f, 0.05f);
        public Color FillTintB { get; set; } = new(0.55f, 0.88f, 0.78f, 0.05f);
        public Vector2 Parallax { get; set; }

        public override void _Draw()
        {
            if (Size.X < 60f || Size.Y < 60f)
            {
                return;
            }
            Vector2 far = Parallax * new Vector2(6f, 4f);
            Vector2 mid = Parallax * new Vector2(14f, 9f);
            Vector2 near = Parallax * new Vector2(22f, 14f);

            // 远景 2 枚克制等轴六边形线框（错开左上品牌角标与主内容，平衡负空间）
            DrawIsoHex(new Vector2(Size.X * 0.57f, Size.Y * 0.13f) + far, 36f, LineColor, withInnerAxes: true);
            DrawIsoHex(new Vector2(Size.X * 0.92f, Size.Y * 0.87f) + far, 34f, LineColor, withInnerAxes: false);

            // 中景 2 枚淡彩等轴小方块（顶部留白 + 左下外沿）
            DrawIsoCubie(new Vector2(Size.X * 0.24f, Size.Y * 0.12f) + mid, 14f, FillTintA, LineColor);
            DrawIsoCubie(new Vector2(Size.X * 0.06f, Size.Y * 0.72f) + mid, 13f, FillTintB, LineColor);

            // 近景少量极细十字 '+' 与小菱点
            Color moteCol = new(LineColor.R, LineColor.G, LineColor.B, LineColor.A * 1.45f);
            DrawPlus(new Vector2(Size.X * 0.24f, Size.Y * 0.15f) + near, 3.5f, moteCol);
            DrawPlus(new Vector2(Size.X * 0.56f, Size.Y * 0.84f) + near, 3.0f, moteCol);
            DrawDiamond(new Vector2(Size.X * 0.86f, Size.Y * 0.14f) + near, 3.2f, moteCol);
            DrawDiamond(new Vector2(Size.X * 0.16f, Size.Y * 0.82f) + near, 2.8f, moteCol);
        }

        private void DrawIsoHex(Vector2 c, float r, Color col, bool withInnerAxes)
        {
            Vector2[] pts = new Vector2[7];
            for (int i = 0; i < 6; i++)
            {
                float a = Mathf.Pi / 6f + i * Mathf.Pi / 3f;
                pts[i] = c + new Vector2(MathF.Cos(a), MathF.Sin(a)) * r;
            }
            pts[6] = pts[0];
            DrawPolyline(pts, col, 1.0f, antialiased: true);
            if (withInnerAxes)
            {
                Color inner = new(col.R, col.G, col.B, col.A * 0.65f);
                DrawLine(c, pts[1], inner, 0.9f, true);
                DrawLine(c, pts[3], inner, 0.9f, true);
                DrawLine(c, pts[5], inner, 0.9f, true);
            }
        }

        private void DrawIsoCubie(Vector2 c, float r, Color fill, Color edge)
        {
            float dx = r * 0.866f;
            float dy = r * 0.5f;
            Vector2 top = c + new Vector2(0, -r);
            Vector2 right = c + new Vector2(dx, -dy);
            Vector2 left = c + new Vector2(-dx, -dy);
            Vector2 bRight = c + new Vector2(dx, dy);
            Vector2 bLeft = c + new Vector2(-dx, dy);
            Vector2 bottom = c + new Vector2(0, r);

            DrawColoredPolygon([top, right, c, left], new Color(fill.R, fill.G, fill.B, fill.A * 1.2f));
            DrawColoredPolygon([left, c, bottom, bLeft], fill);
            DrawColoredPolygon([right, bRight, bottom, c], new Color(fill.R, fill.G, fill.B, fill.A * 0.72f));
            DrawPolyline([top, right, bRight, bottom, bLeft, left, top], edge, 0.9f, true);
        }

        private void DrawPlus(Vector2 c, float arm, Color col)
        {
            DrawLine(c - new Vector2(arm, 0), c + new Vector2(arm, 0), col, 1.0f);
            DrawLine(c - new Vector2(0, arm), c + new Vector2(0, arm), col, 1.0f);
        }

        private void DrawDiamond(Vector2 c, float r, Color col)
        {
            DrawColoredPolygon([
                c + new Vector2(0, -r), c + new Vector2(r, 0),
                c + new Vector2(0, r), c + new Vector2(-r, 0),
            ], col);
        }
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
            AmbientLightEnergy = 0.65f,
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

        _vpHost = new ViewportHost
        {
            Viewport = _vp,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _vpHost.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_vpHost);
        SyncViewportSize();
    }

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

    // ================= 3. 硬边分层地面焦散光盘（caustic-gem） =================

    private void BuildCausticGround()
    {
        _causticMat = new ShaderMaterial { Shader = PreTitleShaders.CreateCausticGroundShader() };
        _causticDisc = new MeshInstance3D
        {
            Name = "CausticGround",
            Mesh = new PlaneMesh { Size = new Vector2(4.4f, 4.4f) },
            MaterialOverride = _causticMat,
            Position = new Vector3(0f, -2.18f, 0f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        _sceneRoot.AddChild(_causticDisc);
    }

    // ================= 4. 雕塑级 3D 晶体字母 N（caustic-gem 切面折光） =================

    private void BuildSculptedN()
    {
        _letterRoot = new Node3D { Name = "SculptedN" };
        _sceneRoot.AddChild(_letterRoot);

        var crystalShader = PreTitleShaders.CreateCrystalNShader();
        _nMatFront = new ShaderMaterial { Shader = crystalShader };
        _nMatMid = new ShaderMaterial { Shader = crystalShader };
        _nMatBack = new ShaderMaterial { Shader = crystalShader };

        const float halfH = 1.60f;
        const float outerX = 1.24f;
        const float innerX = 0.69f;
        const float depth = 0.44f;

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

    /// <summary>
    /// 构造带精确面法线 + 面局部 UV + UV2（物理宽高，供着色器等宽晶体棱边高光使用）的挤出 3D 棱柱。
    /// </summary>
    private static ArrayMesh BuildExtrudedQuad(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float depth)
    {
        float hz = depth * 0.5f;
        var verts = new List<Vector3>(36);
        var norms = new List<Vector3>(36);
        var uvs = new List<Vector2>(36);
        var uv2s = new List<Vector2>(36);

        void AddTri(Vector3 a, Vector3 b, Vector3 c, Vector3 n,
            Vector2 ua, Vector2 ub, Vector2 uc, Vector2 faceSize)
        {
            verts.Add(a); verts.Add(b); verts.Add(c);
            norms.Add(n); norms.Add(n); norms.Add(n);
            uvs.Add(ua); uvs.Add(ub); uvs.Add(uc);
            uv2s.Add(faceSize); uv2s.Add(faceSize); uv2s.Add(faceSize);
        }

        void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, Vector2 faceSize)
        {
            var u0 = new Vector2(0f, 0f);
            var u1 = new Vector2(1f, 0f);
            var u2 = new Vector2(1f, 1f);
            var u3 = new Vector2(0f, 1f);
            AddTri(a, b, c, n, u0, u1, u2, faceSize);
            AddTri(a, c, d, n, u0, u2, u3, faceSize);
        }

        var f0 = new Vector3(p0.X, p0.Y, hz);
        var f1 = new Vector3(p1.X, p1.Y, hz);
        var f2 = new Vector3(p2.X, p2.Y, hz);
        var f3 = new Vector3(p3.X, p3.Y, hz);

        var b0 = new Vector3(p0.X, p0.Y, -hz);
        var b1 = new Vector3(p1.X, p1.Y, -hz);
        var b2 = new Vector3(p2.X, p2.Y, -hz);
        var b3 = new Vector3(p3.X, p3.Y, -hz);

        // 平行四边形的垂直厚度：让斜干的晶体棱边与竖干等宽
        Vector2 eU = p1 - p0;
        Vector2 eV = p3 - p0;
        float lenV = eV.Length();
        float perpW = MathF.Abs(eU.X * eV.Y - eU.Y * eV.X) / MathF.Max(0.001f, lenV);
        var capSize = new Vector2(perpW, lenV);

        AddQuad(f0, f3, f2, f1, Vector3.Back, new Vector2(capSize.Y, capSize.X));
        AddQuad(b0, b1, b2, b3, Vector3.Forward, capSize);

        Vector2[] pts = [p0, p1, p2, p3];
        Vector3[] front = [f0, f1, f2, f3];
        Vector3[] back = [b0, b1, b2, b3];
        for (int i = 0; i < 4; i++)
        {
            int next = (i + 1) % 4;
            Vector2 edge = pts[next] - pts[i];
            float edgeLen = edge.Length();
            Vector3 sideNorm = new Vector3(edge.Y, -edge.X, 0f).Normalized();
            AddQuad(front[i], front[next], back[next], back[i], sideNorm, new Vector2(edgeLen, depth));
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = norms.ToArray();
        arrays[(int)Mesh.ArrayType.TexUV] = uvs.ToArray();
        arrays[(int)Mesh.ArrayType.TexUV2] = uv2s.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    // ================= 5. 3 层 5×5×3 演示棋盘（以原点为旋转轴） =================

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

                var hLine = new MeshInstance3D
                {
                    Mesh = unitBox,
                    MaterialOverride = mat,
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                };
                layerNode.AddChild(hLine);
                _gridBars.Add(new GridBar(hLine, z, true, i, new Vector3(0f, offset, 0f), fullLen, stagger));

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

    // ================= 6. 弧面宝石棋子（封面态差速绕 N 公转） =================

    private void BuildShowcaseStones()
    {
        _stoneMesh = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 28, Rings = 14 };
        var gemStoneShader = PreTitleShaders.CreateGemStoneShader();
        _stoneBlackMat = new ShaderMaterial { Shader = gemStoneShader };
        _stoneWhiteMat = new ShaderMaterial { Shader = gemStoneShader };
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

        // 斜对角无遮挡展示格位：封面态差速绕 N 公转，形变时从瞬时轨道点弧线飞入棋盘
        AddStoneInternal(1, 1, 0, PlayerColor.Black, isShowcase: true);
        AddStoneInternal(3, 3, 1, PlayerColor.White, isShowcase: true);
        CaptureStoneLaunchAnchors();
        _auxDirty = true;
        UpdateDemoStatus();
    }

    private void AddStoneInternal(int x, int y, int z, PlayerColor color, bool isShowcase)
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
            LaunchAnchor = OrbitPositionOf(color, _clock),
            PopProgress = isShowcase ? 1f : 0f,
        });
    }

    /// <summary>黑白双子绕 N 中心的差速交叉 3D 倾斜椭圆轨道（紧贴晶体 N 轮廓前后穿行，不侵占右侧文字区）。</summary>
    private static Vector3 OrbitPositionOf(PlayerColor color, float clock)
    {
        if (color == PlayerColor.Black)
        {
            float a = BlackOrbitPhase0 + clock * BlackOrbitSpeed;
            float rx = 1.66f * MathF.Cos(a);
            float rz = 1.48f * MathF.Sin(a);
            float y = -0.16f + rx * 0.48f + MathF.Sin(a) * 0.24f;
            return new Vector3(rx, y, rz);
        }
        else
        {
            float a = WhiteOrbitPhase0 + clock * WhiteOrbitSpeed;
            float rx = 1.56f * MathF.Cos(a);
            float rz = 1.40f * MathF.Sin(a);
            float y = 0.22f - rx * 0.50f + MathF.Cos(a) * 0.20f;
            return new Vector3(rx, y, rz);
        }
    }

    private void CaptureStoneLaunchAnchors()
    {
        foreach (var s in _stones)
        {
            if (s.IsShowcase)
            {
                s.LaunchAnchor = OrbitPositionOf(s.Color, _clock);
            }
        }
    }

    private void PlaceStone(Coordinate3D cell, bool playAudio)
    {
        AddStoneInternal(cell.X, cell.Y, cell.Z, _turn, isShowcase: false);
        _turn = _turn.Opponent();
        UpdateDemoStatus();
        _auxDirty = true;
        if (playAudio)
        {
            AudioManager.Instance?.PlaySfx(SfxKind.Click);
        }
    }

    // ================= 7. 辅助线对象池（零每帧分配） =================

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

    // ================= 8. 平衡舒展的 2D UI 层 =================

    private void BuildCoverUi()
    {
        _coverUi = new Control { MouseFilter = MouseFilterEnum.Ignore };
        _coverUi.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_coverUi);

        var stack = new VBoxContainer { Name = "CoverStack", MouseFilter = MouseFilterEnum.Ignore };
        stack.AddThemeConstantOverride("separation", 7);
        _coverUi.AddChild(stack);

        _coverBrand = new Label
        {
            Text = "Nmoku",
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _coverBrand.AddThemeFontOverride("font", _fontSerif);
        _coverBrand.AddThemeFontSizeOverride("font_size", 48);
        stack.AddChild(_coverBrand);

        _coverRule = new ColorRect
        {
            CustomMinimumSize = new Vector2(44f, 2f),
            SizeFlagsHorizontal = SizeFlags.ShrinkBegin,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        stack.AddChild(_coverRule);

        _coverCn = new Label { MouseFilter = MouseFilterEnum.Ignore };
        _coverCn.AddThemeFontOverride("font", _fontLxgw);
        _coverCn.AddThemeFontSizeOverride("font_size", 21);
        stack.AddChild(_coverCn);

        _coverSub = new Label { MouseFilter = MouseFilterEnum.Ignore };
        _coverSub.AddThemeFontOverride("font", _fontLxgw);
        _coverSub.AddThemeFontSizeOverride("font_size", 11);
        stack.AddChild(_coverSub);

        var spacer = new Control { CustomMinimumSize = new Vector2(0, 10), MouseFilter = MouseFilterEnum.Ignore };
        stack.AddChild(spacer);

        _coverPromptNormal = CreatePillStyle(Colors.Transparent, Colors.White, 17, padH: 18, padV: 5);
        _coverPromptHover = CreatePillStyle(Colors.Transparent, Colors.White, 17, padH: 18, padV: 5);
        _coverPromptBtn = new Button
        {
            FocusMode = FocusModeEnum.None,
            SizeFlagsHorizontal = SizeFlags.ShrinkBegin,
            CustomMinimumSize = new Vector2(146f, 34f),
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
            Position = new Vector2(28f, 16f),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        ((VBoxContainer)_cornerUi).AddThemeConstantOverride("separation", 0);

        _cornerBrand = new Label
        {
            Text = "Nmoku",
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _cornerBrand.AddThemeFontOverride("font", _fontSerif);
        _cornerBrand.AddThemeFontSizeOverride("font_size", 25);
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

        AddMenuButton(true, () => I18n.T(Text.Title.Start), () => StartRequested?.Invoke());
        _resumeItem = AddMenuButton(false, () => I18n.T(Text.Title.ContinueGame), () => ResumeRequested?.Invoke());
        AddMenuButton(false, () => I18n.T(Text.Title.CustomGame), () => CustomGameRequested?.Invoke());
        AddMenuButton(false, () => I18n.T(Text.Title.LoadGame), () => LoadRequested?.Invoke());
        AddMenuButton(false, () => I18n.T(Text.Title.Guide), () => GuideRequested?.Invoke());
        AddMenuButton(false, () => I18n.T(Text.Settings.Title), () => SettingsRequested?.Invoke());
        AddMenuButton(false, () => I18n.T(Text.Title.Quit), () => QuitRequested?.Invoke());

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
            OffsetLeft = 28f,
            OffsetTop = -42f,
            OffsetRight = 468f,
            OffsetBottom = -16f,
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
        statusWrap.AddThemeConstantOverride("separation", 6);
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
            ? (isDark ? new Color(0.14f, 0.16f, 0.22f) : pal.StoneBlack)
            : pal.StoneWhite;
        _turnDotBox.BorderColor = _turn == PlayerColor.Black && isDark
            ? pal.TextDim
            : pal.PanelBorder;
    }

    // ================= 9. 均衡自适应布局 =================

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
        // 与左侧晶体 N + 公转双子留出舒适呼吸间距（57.5% X 起排，左右留白匀称不挤不空）
        float leftX = Mathf.Clamp(faceSize.X * 0.575f, 16f, faceSize.X - 230f);
        float topY = Mathf.Max(28f, (faceSize.Y - 178f) * 0.47f);
        stack.Position = new Vector2(leftX, topY);
    }

    private void ApplyMenuLayout(Vector2 faceSize)
    {
        float marginX = Mathf.Clamp(faceSize.X * DesignRightMargin / DesignFaceWidth, 12f, DesignRightMargin);
        float colW = Mathf.Clamp(faceSize.X - 2f * marginX, 140f, DesignColumnWidth);

        int count = MaxMenuButtons;
        float maxAvailH = Mathf.Max(160f, faceSize.Y - 36f);
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

    // ================= 10. 主题 / 宝石色板适配（原地刷新） =================

    private void ApplyTheme()
    {
        var pal = Palette.For(_colorblind);
        bool isDark = pal.Background.Luminance < 0.4f;

        // 1) 氛围径向渐变背景 + 等轴视差几何层
        Color glow = isDark
            ? pal.Background.Lightened(0.11f).Lerp(pal.Accent, 0.14f)
            : pal.Background.Lightened(0.06f);
        Color edge = isDark
            ? pal.Background.Darkened(0.32f)
            : pal.Background.Darkened(0.08f);
        _backdropMat.SetShaderParameter("bg_base", pal.Background);
        _backdropMat.SetShaderParameter("bg_glow", glow);
        _backdropMat.SetShaderParameter("bg_edge", edge);

        _geoBg.LineColor = isDark
            ? new Color(0.64f, 0.78f, 0.96f, 0.085f)
            : new Color(0.18f, 0.34f, 0.38f, 0.10f);
        _geoBg.FillTintA = isDark
            ? new Color(0.36f, 0.68f, 1.00f, 0.07f)
            : new Color(0.24f, 0.56f, 0.62f, 0.09f);
        _geoBg.FillTintB = isDark
            ? new Color(0.38f, 0.88f, 0.74f, 0.06f)
            : new Color(0.34f, 0.68f, 0.58f, 0.08f);
        _geoBg.QueueRedraw();

        // 2) 雕塑晶体 N 5 阶宝石色板（暗色：星钻冰蓝青；亮色：青玉祖母绿调——绝不发灰！）
        if (isDark)
        {
            ConfigureCrystalMat(_nMatFront,
                new Color("16264A"), new Color("2C4E8A"), new Color("5C8CE0"), new Color("B6D8FF"), Colors.White,
                new Color("62F0FF"), new Color("B88CFF"), new Color("E8F4FF"));
            ConfigureCrystalMat(_nMatMid,
                new Color("122040"), new Color("25447A"), new Color("4E7CD0"), new Color("A4CCFA"), Colors.White,
                new Color("58E6FF"), new Color("AC80F8"), new Color("DCEBFF"));
            ConfigureCrystalMat(_nMatBack,
                new Color("0E1934"), new Color("1E3868"), new Color("406ABC"), new Color("8EBCEB"), new Color("F0F7FF"),
                new Color("4ED8F2"), new Color("9A72EC"), new Color("CAE0FA"));
        }
        else
        {
            // 亮色水绿宣纸底配翠青碧玉晶体（对齐源水绿 #2C4A4E 色族并提升晶体明澈度）
            ConfigureCrystalMat(_nMatFront,
                new Color("0F2B30"), new Color("1E4D54"), new Color("357B84"), new Color("86CFD2"), new Color("F4FFFF"),
                new Color("5EEAD4"), new Color("7CC4FF"), new Color("D6F8F5"));
            ConfigureCrystalMat(_nMatMid,
                new Color("0C2428"), new Color("184248"), new Color("2C6C75"), new Color("74C0C4"), new Color("EEFFFD"),
                new Color("52E0CC"), new Color("6FB8F5"), new Color("C5F0EC"));
            ConfigureCrystalMat(_nMatBack,
                new Color("091D21"), new Color("13363C"), new Color("245C64"), new Color("62B0B5"), new Color("E2FAFA"),
                new Color("44D4C0"), new Color("60AAE8"), new Color("B2E4E0"));
        }

        // 3) 黑曜星光蓝 / 冰瓷月光石 弧面宝石棋子色板
        ConfigureGemStoneMat(_stoneBlackMat, isWhite: false,
            new Color("080B18"), new Color("121B36"), new Color("243662"), new Color("6EA4EC"), Colors.White,
            isDark ? new Color("5294FF") : new Color("3E8494"));
        ConfigureGemStoneMat(_stoneWhiteMat, isWhite: true,
            new Color("88A0C4"), new Color("B2C6E0"), new Color("DCE8F6"), new Color("F6FAFF"), Colors.White,
            isDark ? new Color("96D4FF") : new Color("7EC8CC"));

        // 4) 硬边分层焦散地面色板（暗色星钻青紫 / 亮色水绿翠金）
        if (isDark)
        {
            _causticMat.SetShaderParameter("shadow_color", new Color("111524"));
            _causticMat.SetShaderParameter("lobe_left_color", new Color("4CB8FF"));
            _causticMat.SetShaderParameter("lobe_right_color", new Color("9E78FF"));
            _causticMat.SetShaderParameter("accent_rim_color", new Color("4CF0C8"));
            _causticMat.SetShaderParameter("inner_ring_color", new Color("B8DCFF"));
            _causticMat.SetShaderParameter("core_color", new Color("F4FAFF"));
            _causticMat.SetShaderParameter("shadow_alpha", 0.34f);
            _causticMat.SetShaderParameter("caustic_alpha", 0.56f);
        }
        else
        {
            _causticMat.SetShaderParameter("shadow_color", new Color("9AB0B6"));
            _causticMat.SetShaderParameter("lobe_left_color", new Color("48C9B8"));
            _causticMat.SetShaderParameter("lobe_right_color", new Color("6BB8F2"));
            _causticMat.SetShaderParameter("accent_rim_color", new Color("72E8B4"));
            _causticMat.SetShaderParameter("inner_ring_color", new Color("C4F2EE"));
            _causticMat.SetShaderParameter("core_color", new Color("FFFFFF"));
            _causticMat.SetShaderParameter("shadow_alpha", 0.24f);
            _causticMat.SetShaderParameter("caustic_alpha", 0.64f);
        }

        // 5) 辅助线 / 悬停球
        _hoverBallMat.AlbedoColor = new Color(pal.HoverHighlight.R, pal.HoverHighlight.G, pal.HoverHighlight.B, 0.78f);
        _auxMarkerSelfMat.AlbedoColor = new Color(pal.LineSelf.R, pal.LineSelf.G, pal.LineSelf.B, 0.90f);
        _auxMarkerEnemyMat.AlbedoColor = new Color(pal.LineEnemy.R, pal.LineEnemy.G, pal.LineEnemy.B, 0.90f);

        // 6) 封面与角标排版
        _coverBrand.AddThemeColorOverride("font_color", pal.TextPrimary);
        _coverRule.Color = pal.Accent;
        _coverCn.AddThemeColorOverride("font_color", pal.TextPrimary);
        _coverSub.AddThemeColorOverride("font_color", pal.TextDim);

        _coverPromptNormal.BgColor = isDark
            ? new Color(pal.ButtonBg.R, pal.ButtonBg.G, pal.ButtonBg.B, 0.55f)
            : new Color(pal.PanelBg.R, pal.PanelBg.G, pal.PanelBg.B, 0.80f);
        _coverPromptNormal.BorderColor = new Color(pal.Accent.R, pal.Accent.G, pal.Accent.B, 0.58f);
        _coverPromptHover.BgColor = pal.Accent;
        _coverPromptHover.BorderColor = pal.Accent;
        _coverPromptBtn.AddThemeColorOverride("font_color", pal.TextPrimary);
        _coverPromptBtn.AddThemeColorOverride("font_hover_color", Colors.White);
        _coverPromptBtn.AddThemeColorOverride("font_pressed_color", Colors.White);

        _cornerBrand.AddThemeColorOverride("font_color", pal.Accent);
        _cornerCn.AddThemeColorOverride("font_color", pal.TextDim);

        // 7) 菜单按钮样式
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
            }
            else
            {
                Color cardBg = isDark
                    ? new Color(pal.PanelBg.R, pal.PanelBg.G, pal.PanelBg.B, 0.78f)
                    : new Color(pal.PanelBg.R, pal.PanelBg.G, pal.PanelBg.B, 0.84f);
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

        // 8) 左下迷你控制条
        Color miniBg = new Color(pal.PanelBg.R, pal.PanelBg.G, pal.PanelBg.B, 0.72f);
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

        ApplyMorphVisuals(_morphT);
        _auxDirty = true;
    }

    private static void ConfigureCrystalMat(ShaderMaterial mat,
        Color dark, Color shadowMid, Color mid, Color bright, Color highlight,
        Color fireA, Color fireB, Color border)
    {
        mat.SetShaderParameter("col_dark", dark);
        mat.SetShaderParameter("col_shadow_mid", shadowMid);
        mat.SetShaderParameter("col_mid", mid);
        mat.SetShaderParameter("col_bright", bright);
        mat.SetShaderParameter("col_highlight", highlight);
        mat.SetShaderParameter("fire_a", fireA);
        mat.SetShaderParameter("fire_b", fireB);
        mat.SetShaderParameter("border_color", border);
    }

    private static void ConfigureGemStoneMat(ShaderMaterial mat, bool isWhite,
        Color dark, Color shadowMid, Color mid, Color bright, Color highlight, Color rimAccent)
    {
        mat.SetShaderParameter("is_white_stone", isWhite ? 1f : 0f);
        mat.SetShaderParameter("col_dark", dark);
        mat.SetShaderParameter("col_shadow_mid", shadowMid);
        mat.SetShaderParameter("col_mid", mid);
        mat.SetShaderParameter("col_bright", bright);
        mat.SetShaderParameter("col_highlight", highlight);
        mat.SetShaderParameter("rim_accent", rimAccent);
    }

    // ================= 11. 流体形变驱动（完全可逆） =================

    private void ApplyMorphVisuals(float t)
    {
        var pal = Palette.For(_colorblind);
        float smooth = SmoothStep(0f, 1f, t);

        _backdropMat.SetShaderParameter("glow_center", new Vector2(
            Mathf.Lerp(0.38f, 0.36f, smooth),
            Mathf.Lerp(0.50f, 0.51f, smooth)));

        // --- Stage 1: 晶体 N 三笔画沿纵深分离薄化并流体融入棋盘 (t: 0.00 -> 0.66) ---
        float nOut = SmoothStep(0.00f, 0.64f, t);
        float nEase = EaseInOutCubic(nOut);
        float nAlpha = 1f - SmoothStep(0.24f, 0.66f, t);
        bool nVisible = nAlpha > 0.01f;
        _letterRoot.Visible = nVisible;
        if (nVisible)
        {
            float breatheY = Mathf.Sin(_clock * 1.25f) * 0.040f * (1f - nOut);
            _letterRoot.Position = new Vector3(0f, breatheY, 0f);
            float hoverLift = 1f + _letterHoverGlow * 0.030f * (1f - nOut);
            _letterRoot.Scale = Vector3.One * hoverLift;

            float thin = Mathf.Lerp(1f, 0.12f, nEase);
            float spreadY = Mathf.Lerp(1f, 0.92f, nEase);
            float spreadX = Mathf.Lerp(1f, 1.18f, nEase);

            _nLeft.Position = new Vector3(
                Mathf.Lerp(0f, 0.88f, nEase),
                0f,
                Mathf.Lerp(0.05f, BoardSpacing, nEase));
            _nLeft.Scale = new Vector3(spreadX, spreadY, thin);

            _nDiag.Position = new Vector3(0f, 0f, 0f);
            _nDiag.Rotation = new Vector3(0f, 0f, Mathf.Lerp(0f, 0.46f, nEase));
            _nDiag.Scale = new Vector3(Mathf.Lerp(1f, 0.72f, nEase), spreadY, thin);

            _nRight.Position = new Vector3(
                Mathf.Lerp(0f, -0.88f, nEase),
                0f,
                Mathf.Lerp(-0.05f, -BoardSpacing, nEase));
            _nRight.Scale = new Vector3(spreadX, spreadY, thin);

            float hoverShader = _letterHoverGlow * (1f - nOut);
            foreach (var mat in new[] { _nMatFront, _nMatMid, _nMatBack })
            {
                mat.SetShaderParameter("alpha_fade", nAlpha);
                mat.SetShaderParameter("hover_glow", hoverShader);
                mat.SetShaderParameter("polish_sweep", _polishSweep);
            }
        }

        // --- Stage 2: 三层 5×5 棋盘波浪涟漪展开 (t: 0.16 -> 0.86) ---
        bool boardVisible = t > 0.14f;
        _boardRoot.Visible = boardVisible || _stones.Count > 0;
        float[] layerBaseAlpha = [0.82f, 0.58f, 0.36f];
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
            float start = 0.16f + bar.Layer * 0.08f + bar.Stagger;
            float end = 0.64f + bar.Layer * 0.08f + bar.Stagger;
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

        float connP = EaseOutQuint(SmoothStep(0.42f, 0.86f, t));
        float connLen = 2f * BoardSpacing * connP;
        _connectorMat.AlbedoColor = new Color(pal.ConnectorLine.R, pal.ConnectorLine.G, pal.ConnectorLine.B, 0.42f * connP);
        foreach (var rail in _connectors)
        {
            rail.Visible = connP > 0.01f;
            rail.Scale = new Vector3(ConnectorThickness, ConnectorThickness, Mathf.Max(0.001f, connLen));
        }

        // --- Stage 3: 黑白宝石棋子——封面差速公转 → 沿流畅弧线落位棋盘 ---
        float stoneFlight = SmoothStep(0.18f, 0.90f, t);
        float stoneEase = EaseInOutCubic(stoneFlight);
        for (int i = 0; i < _stones.Count; i++)
        {
            var s = _stones[i];
            Vector3 dst = CellToBoardLocal(s.X, s.Y, s.Z);
            if (s.IsShowcase)
            {
                Vector3 liveOrbit = OrbitPositionOf(s.Color, _clock);
                if (_phase == Phase.Letter)
                {
                    s.LaunchAnchor = liveOrbit;
                }
                Vector3 src = _phase == Phase.Letter ? liveOrbit : s.LaunchAnchor;
                float arc = Mathf.Sin(stoneEase * Mathf.Pi) * 0.48f * (i == 0 ? 1f : -1f);
                Vector3 pos = src.Lerp(dst, stoneEase) + new Vector3(arc * 0.35f, MathF.Abs(arc) * 0.32f, 0f);
                float r = Mathf.Lerp(0.24f, StoneRadius, stoneEase);
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

        // --- Stage 4: 地面焦散光盘随阶段与旋转联动 ---
        _causticDisc.Position = new Vector3(0f, Mathf.Lerp(-2.06f, -2.28f, smooth), 0f);
        float causticScale = Mathf.Lerp(0.84f, 0.90f, smooth);
        _causticDisc.Scale = new Vector3(causticScale, 1f, causticScale);
        _causticMat.SetShaderParameter("spin_angle", Mathf.Lerp(_clock * 0.35f, _menuYaw, smooth));
        _causticMat.SetShaderParameter("squareness", Mathf.Lerp(0.15f, 0.78f, smooth));
        _causticMat.SetShaderParameter("caustic_intensity", Mathf.Lerp(0.95f, 0.64f, smooth));

        // --- Stage 5: 相机严格绕原点 (0,0,0) 旋转，经 HOffset 实现「略偏左」零甩动构图 ---
        float camE = EaseInOutCubic(t);
        float swirl = Mathf.Sin(camE * Mathf.Pi);
        float yaw = Mathf.Lerp(CoverYaw, _menuYaw, camE) + 0.16f * swirl;
        float pitch = Mathf.Lerp(CoverPitch, _menuPitch, camE) + 0.04f * swirl;
        float dist = Mathf.Lerp(CoverDist, MenuDist, camE) + 0.80f * swirl;
        float fov = Mathf.Lerp(CoverFov, MenuFov, camE);

        float parallaxWeight = 1f - SmoothStep(0f, 0.45f, t);
        yaw += _parallaxCurrent.X * 0.13f * parallaxWeight;
        pitch += _parallaxCurrent.Y * 0.08f * parallaxWeight;

        Vector3 offset = new(
            dist * Mathf.Cos(pitch) * Mathf.Sin(yaw),
            dist * Mathf.Sin(pitch),
            dist * Mathf.Cos(pitch) * Mathf.Cos(yaw));
        _cam.Fov = fov;
        _cam.HOffset = Mathf.Lerp(CoverHOffset, MenuHOffset, camE);
        _cam.VOffset = Mathf.Lerp(CoverVOffset, MenuVOffset, camE);
        _cam.Position = offset;
        _cam.LookAt(Vector3.Zero, Vector3.Up);

        // --- Stage 6: 2D UI 错峰编排 ---
        float coverAlpha = 1f - SmoothStep(0.00f, 0.34f, t);
        _coverUi.Visible = coverAlpha > 0.01f;
        _coverUi.Modulate = new Color(1f, 1f, 1f, coverAlpha);
        _coverUi.Position = new Vector2(-12f * (1f - coverAlpha), -5f * (1f - coverAlpha));
        _coverPromptBtn.MouseFilter = t > 0.20f ? MouseFilterEnum.Ignore : MouseFilterEnum.Stop;

        float cornerP = EaseOutQuint(SmoothStep(0.56f, 0.92f, t));
        _cornerUi.Visible = cornerP > 0.01f;
        _cornerUi.Modulate = new Color(1f, 1f, 1f, cornerP);
        _cornerUi.Position = new Vector2(28f - 12f * (1f - cornerP), 16f);

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

        float barP = EaseOutQuint(SmoothStep(0.66f, 1.00f, t));
        _bottomBar.Visible = barP > 0.01f;
        _bottomBar.Modulate = new Color(1f, 1f, 1f, barP);
        _bottomBar.OffsetTop = -42f + 10f * (1f - barP);
        _bottomBar.OffsetBottom = -16f + 10f * (1f - barP);

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

    // ================= 12. 主循环与交互 =================

    public override void _Process(double delta)
    {
        float dt = (float)Mathf.Min(delta, 0.05);
        _clock += dt;
        if (_polishSweep < 1f)
        {
            _polishSweep = Mathf.Min(1f, _polishSweep + dt / 0.90f);
        }
        SyncViewportSize();

        float pFactor = 1f - MathF.Exp(-8f * dt);
        _parallaxCurrent = _parallaxCurrent.Lerp(_parallaxTarget, pFactor);
        _geoBg.Parallax = _parallaxCurrent;
        _geoBg.QueueRedraw();

        float targetGlow = _phase == Phase.Letter && _letterHovered ? 1f : 0f;
        _letterHoverGlow = Mathf.Lerp(_letterHoverGlow, targetGlow, 1f - MathF.Exp(-10f * dt));
        if (_phase == Phase.Letter)
        {
            float pulse = 0.86f + 0.14f * Mathf.Sin(_clock * 2.3f);
            _coverPromptBtn.Modulate = new Color(1f, 1f, 1f, pulse);
        }

        float hoverFactor = 1f - MathF.Exp(-16f * dt);
        foreach (var item in _menuItems)
        {
            item.HoverAnim = Mathf.Lerp(item.HoverAnim, item.Hovered ? 1f : 0f, hoverFactor);
        }

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
        if (_phase == Phase.Letter)
        {
            CaptureStoneLaunchAnchors();
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
        CaptureStoneLaunchAnchors();
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
