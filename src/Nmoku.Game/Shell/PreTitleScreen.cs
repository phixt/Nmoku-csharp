using Godot;
using Nmoku.Core;
using Nmoku.Game.Audio;
using Nmoku.Game.Theme;

namespace Nmoku.Game.Shell;

/// <summary>
/// 开始界面（预开始封面 ↔ 流体形变 ↔ 交互菜单一体化）：
/// <para>
/// · 雕刻多面晶体 N：三笔画各自雕琢多组斜切棱面与中脊，随缓动与视角平滑流转光影（无像素抖动/网格噪点）；
///   封面态底部配柔和折射焦散光斑，展开棋盘后焦散彻底消隐。
/// · 温润棋子与差速公转：采用与对局一致的素雅棋子质感（非夸张宝石），黑白双子沿交叉倾斜轨道差速绕 N 公转，
///   形变时自瞬时轨道位置沿弧线落位至三层 5×5×3 交互演示棋盘。
/// · 自中心旋转 + 略偏左构图：相机严格以 (0,0,0) 为轴心，经 <see cref="Camera3D.HOffset"/> 略偏左构图，
///   原地自转/拖拽零甩动。
/// · 性能与工程卫生：1:1 原生像素 + MSAA 4×、仅在状态变化时更新网格与射线拾取、隐藏自动停进程与视口、
///   <see cref="IKeepAliveRefreshable"/> 原地换肤换语。
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
    private const float MorphDuration = 1.12f;
    private const float BoardSpacing = 1.52f;       // 3 层 5×5 演示盘层间距（以原点为几何中心：+s, 0, -s）
    private const float CellStep = 0.82f;           // 单格世界尺寸（总宽 4×0.82 = 3.28）
    private const float StoneRadius = 0.30f;
    private const float HoverBallRadius = 0.24f;
    private const float MarkerRadius = 0.105f;
    private const float GridThickness = 0.042f;     // 明晰锐利的演示盘线宽（深浅背景皆清晰）
    private const float ConnectorThickness = 0.030f;
    private const float ViewportSupersample = 1.0f; // 原生 1:1 物理像素，杜绝过度超采拖慢帧率

    // 相机始终绕世界原点 (0,0,0) 旋转，通过 HOffset/VOffset 实现「以自身为中心、略偏左」稳态构图
    private const float CoverHOffset = 1.36f;
    private const float MenuHOffset = 1.16f;
    private const float CoverVOffset = -0.06f;
    private const float MenuVOffset = -0.08f;

    private const float CoverYaw = -0.26f;          // -15°：展现雕刻多面晶体 N 的立体切面与纵深
    private const float CoverPitch = 0.14f;         // +8°
    private const float CoverDist = 9.9f;
    private const float CoverFov = 32f;

    private const float MenuYaw = 0.56f;            // +32°：立体 3/4 展示角
    private const float MenuPitch = 0.35f;          // +20°
    private const float MenuDist = 10.8f;
    private const float MenuFov = 34f;

    // 黑白双子绕 N 差速公转参数（两条交叉倾斜 3D 椭圆轨道，黑子快、白子慢）
    private const float BlackOrbitSpeed = 0.68f;    // rad/s
    private const float WhiteOrbitSpeed = 0.41f;    // rad/s（差速比 ≈ 1.66）
    private const float BlackOrbitPhase0 = 2.35f;
    private const float WhiteOrbitPhase0 = -0.58f;

    // 拖拽与惯性手感
    private const float DragYawSense = 0.009f;
    private const float DragPitchSense = 0.006f;
    private const float DragFollow = 14f;
    private const float InertiaDecay = 0.04f;
    private const float AutoOrbitSpeed = 0.22f;
    private const float MinMenuPitch = 0.08f;
    private const float MaxMenuPitch = 0.76f;

    // 菜单列设计基准（768×432 逻辑面：宽 232、右留白 56、间距 9、按钮高 36）
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
    private float _lastAppliedMorphT = -1f;
    private int _morphDir = 1;
    private float _clock;
    private float _polishSweep = 0f;

    // 演示棋盘（5×5×3，真实 Core 实例，独立于主对局）
    private readonly Board _board = new(5, 3);
    private PlayerColor _turn = PlayerColor.Black;
    private bool _colorblind;
    private bool _hasLiveGame;

    // 背景与 3D 场景节点
    private ColorRect _backdrop = null!;
    private ShaderMaterial _backdropMat = null!;
    private SubViewport _vp = null!;
    private ViewportHost _vpHost = null!;
    private Node3D _sceneRoot = null!;
    private Camera3D _cam = null!;
    private Godot.Environment _env = null!;

    // 雕刻多面晶体 N
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

    // 素雅瓷质棋子（与对局一致，拒绝夸张宝石质感）
    private readonly List<StoneVisual> _stones = [];
    private SphereMesh _stoneMesh = null!;
    private StandardMaterial3D _stoneBlackMat = null!;
    private StandardMaterial3D _stoneWhiteMat = null!;

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
    private bool _pickDirty = true;
    private bool _morphDirty = true;

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

        _fontLxgw = UiTheme.DefaultFont;
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
        _morphDirty = true;
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
            _vpHost?.SetProcess(visible);
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
            _morphDirty = true;
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
    internal Vector2I ViewportSizeForTests => _vp.Size;

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
        _morphDirty = true;
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

    // ================= 1. 干净通透的氛围渐变背景 =================

    private void BuildBackdrop()
    {
        var shader = new Shader
        {
            Code = """
shader_type canvas_item;
uniform vec4 bg_base : source_color = vec4(0.039, 0.051, 0.075, 1.0);
uniform vec4 bg_glow : source_color = vec4(0.085, 0.125, 0.185, 1.0);
uniform vec4 bg_edge : source_color = vec4(0.020, 0.028, 0.044, 1.0);
uniform vec2 glow_center = vec2(0.38, 0.50);
uniform float aspect = 1.777;

void fragment() {
    vec2 d = UV - glow_center;
    d.x *= aspect;
    float dist = length(d);
    float glow = exp(-dist * dist * 2.15);
    float vig = smoothstep(0.40, 1.50, dist);
    vec3 col = mix(bg_base.rgb, bg_edge.rgb, vig * 0.45);
    col = mix(col, bg_glow.rgb, glow * 0.55);
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

    // ================= 2. 高效 1:1 原生像素 3D 视口 =================

    private void BuildViewport3D()
    {
        _vp = new SubViewport
        {
            OwnWorld3D = true,
            TransparentBg = true,
            Msaa3D = Viewport.Msaa.Msaa4X,
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

        // 柔和双定向光（无阴影开销）：给素雅棋子提供清晰的球面体积感与轮廓回光
        _vp.AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-48f, 34f, 0f),
            LightEnergy = 0.95f,
            ShadowEnabled = false,
        });
        _vp.AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(24f, -142f, 0f),
            LightEnergy = 0.42f,
            LightColor = new Color(0.84f, 0.92f, 1.00f),
            ShadowEnabled = false,
        });

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
        // 等比封顶长边 ≤1920：1080p 保持 1:1，3K/4K 高分屏自动降渲染像素省 GPU 且严格保宽高比
        float physW = Size.X * scale;
        float physH = Size.Y * scale;
        float cap = MathF.Min(1f, 1920f / MathF.Max(1f, MathF.Max(physW, physH)));
        int w = Mathf.Max(64, (int)MathF.Floor(physW * cap));
        int h = Mathf.Max(64, (int)MathF.Floor(physH * cap));
        if (_vp.Size != new Vector2I(w, h))
        {
            _vp.Size = new Vector2I(w, h);
        }
        _backdropMat.SetShaderParameter("aspect", Size.X / MathF.Max(1f, Size.Y));
    }

    // ================= 3. 雕刻多面晶体字母 N =================

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
        const float depth = 0.50f;

        _nLeft = new MeshInstance3D
        {
            Name = "NLeft",
            Mesh = BuildFacetedCrystalBar(
                new Vector2(-outerX, -halfH),
                new Vector2(-innerX, -halfH),
                new Vector2(-innerX, halfH),
                new Vector2(-outerX, halfH),
                depth,
                ridgeBias: +0.08f,
                midBreakLeft: 0.44f,
                midBreakRight: 0.58f),
            MaterialOverride = _nMatFront,
        };

        _nDiag = new MeshInstance3D
        {
            Name = "NDiag",
            Mesh = BuildFacetedCrystalBar(
                new Vector2(innerX, -halfH),
                new Vector2(outerX, -halfH),
                new Vector2(-innerX, halfH),
                new Vector2(-outerX, halfH),
                depth * 0.94f,
                ridgeBias: -0.06f,
                midBreakLeft: 0.54f,
                midBreakRight: 0.42f),
            MaterialOverride = _nMatMid,
        };

        _nRight = new MeshInstance3D
        {
            Name = "NRight",
            Mesh = BuildFacetedCrystalBar(
                new Vector2(innerX, -halfH),
                new Vector2(outerX, -halfH),
                new Vector2(outerX, halfH),
                new Vector2(innerX, halfH),
                depth,
                ridgeBias: -0.08f,
                midBreakLeft: 0.58f,
                midBreakRight: 0.44f),
            MaterialOverride = _nMatBack,
        };

        _letterRoot.AddChild(_nLeft);
        _letterRoot.AddChild(_nDiag);
        _letterRoot.AddChild(_nRight);
    }

    /// <summary>
    /// 构造带真实雕刻斜切棱面与中脊的 3D 晶体笔画（硬法线平面切面，外轮廓严格保持字母 N 锐利剪影）：
    /// · 正面与背面各雕琢 10 组角度互异的三角晶体切面（上下端斜角 + 左右纵脊斜面 + 中段折面）；
    /// · 四周保留清晰腰棱侧壁；转动或鼠标微动时每个雕刻面各自独立受光、呈现自然明暗变化。
    /// </summary>
    private static ArrayMesh BuildFacetedCrystalBar(
        Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3,
        float depth, float ridgeBias, float midBreakLeft, float midBreakRight)
    {
        float wallHz = depth * 0.24f;
        float crownHz = depth * 0.58f;

        var verts = new List<Vector3>(108);
        var norms = new List<Vector3>(108);

        void AddFacetTri(Vector3 a, Vector3 b, Vector3 c, Vector3 outwardHint)
        {
            // Godot CW 正面约定：外法线 = (c - a) × (b - a)
            Vector3 n = (c - a).Cross(b - a);
            if (n.LengthSquared() < 1e-10f)
            {
                return;
            }
            n = n.Normalized();
            if (n.Dot(outwardHint) < 0f)
            {
                (b, c) = (c, b);
                n = -n;
            }
            verts.Add(a); verts.Add(b); verts.Add(c);
            norms.Add(n); norms.Add(n); norms.Add(n);
        }

        // 外轮廓上增加左右中段折点（严格落在原始外边上，零破坏 N 剪影）
        Vector2 pRightMid = p1.Lerp(p2, midBreakRight);
        Vector2 pLeftMid = p0.Lerp(p3, midBreakLeft);

        // 雕刻中脊三控制点（沿笔画方向微偏置，形成柔和而不对称的宝石多切面）
        Vector2 Bilinear(float u, float v) => p0.Lerp(p1, u).Lerp(p3.Lerp(p2, u), v);
        Vector2 rBot2 = Bilinear(0.50f + ridgeBias * 0.6f, 0.16f);
        Vector2 rMid2 = Bilinear(0.50f - ridgeBias, 0.50f);
        Vector2 rTop2 = Bilinear(0.50f + ridgeBias * 0.8f, 0.84f);

        void BuildCrown(float zSign)
        {
            float wz = wallHz * zSign;
            float cz = crownHz * zSign;
            Vector3 hint = new(0f, 0f, zSign);

            var v0 = new Vector3(p0.X, p0.Y, wz);
            var v1 = new Vector3(p1.X, p1.Y, wz);
            var v12 = new Vector3(pRightMid.X, pRightMid.Y, wz);
            var v2 = new Vector3(p2.X, p2.Y, wz);
            var v3 = new Vector3(p3.X, p3.Y, wz);
            var v30 = new Vector3(pLeftMid.X, pLeftMid.Y, wz);

            var rb = new Vector3(rBot2.X, rBot2.Y, cz * 0.92f);
            var rm = new Vector3(rMid2.X, rMid2.Y, cz * 1.08f);
            var rt = new Vector3(rTop2.X, rTop2.Y, cz * 0.94f);

            // 底/顶两块三角端斜切面
            AddFacetTri(v0, rb, v1, hint);
            AddFacetTri(v2, rt, v3, hint);

            // 右侧 4 块交错折切面
            AddFacetTri(v1, rb, v12, hint);
            AddFacetTri(rb, rm, v12, hint);
            AddFacetTri(v12, rm, rt, hint);
            AddFacetTri(v12, rt, v2, hint);

            // 左侧 4 块交错折切面
            AddFacetTri(v3, rt, v30, hint);
            AddFacetTri(rt, rm, v30, hint);
            AddFacetTri(v30, rm, rb, hint);
            AddFacetTri(v30, rb, v0, hint);
        }

        BuildCrown(+1f);
        BuildCrown(-1f);

        // 四周清晰腰棱侧壁（6 段闭合，与中段折点严丝合缝）
        Vector2[] ring = [p0, p1, pRightMid, p2, p3, pLeftMid];
        for (int i = 0; i < ring.Length; i++)
        {
            int next = (i + 1) % ring.Length;
            Vector2 e = ring[next] - ring[i];
            Vector3 sideHint = new Vector3(e.Y, -e.X, 0f).Normalized();
            var fa = new Vector3(ring[i].X, ring[i].Y, wallHz);
            var fb = new Vector3(ring[next].X, ring[next].Y, wallHz);
            var ba = new Vector3(ring[i].X, ring[i].Y, -wallHz);
            var bb = new Vector3(ring[next].X, ring[next].Y, -wallHz);
            AddFacetTri(fa, fb, bb, sideHint);
            AddFacetTri(fa, bb, ba, sideHint);
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = norms.ToArray();
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

    // ================= 6. 素雅棋子（封面态差速绕 N 公转） =================

    private void BuildShowcaseStones()
    {
        _stoneMesh = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 24, Rings = 12 };
        _stoneBlackMat = new StandardMaterial3D
        {
            Roughness = 0.28f,
            Metallic = 0.04f,
            MetallicSpecular = 0.45f,
        };
        _stoneWhiteMat = new StandardMaterial3D
        {
            Roughness = 0.32f,
            Metallic = 0.0f,
            MetallicSpecular = 0.35f,
        };
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

        AddStoneInternal(1, 1, 0, PlayerColor.Black, isShowcase: true);
        AddStoneInternal(3, 3, 1, PlayerColor.White, isShowcase: true);
        CaptureStoneLaunchAnchors();
        _auxDirty = true;
        _morphDirty = true;
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
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
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

    /// <summary>黑白双子绕 N 中心的差速交叉 3D 倾斜椭圆轨道。</summary>
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

        _auxMarkerMesh = new SphereMesh { Radius = MarkerRadius, Height = MarkerRadius * 2f, RadialSegments = 10, Rings = 6 };
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
            Mesh = new SphereMesh { Radius = HoverBallRadius, Height = HoverBallRadius * 2f, RadialSegments = 16, Rings = 8 },
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
            ? pal.StoneBlack
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

    // ================= 10. 主题配色适配（原地刷新） =================

    private void ApplyTheme()
    {
        var pal = Palette.For(_colorblind);
        bool isDark = pal.Background.Luminance < 0.4f;

        // 1) 深邃/通透氛围渐变背景
        Color glow = isDark
            ? pal.Background.Lightened(0.08f).Lerp(pal.Accent, 0.12f)
            : pal.Background.Lightened(0.05f);
        Color edge = isDark
            ? pal.Background.Darkened(0.42f)
            : pal.Background.Darkened(0.06f);
        _backdropMat.SetShaderParameter("bg_base", pal.Background);
        _backdropMat.SetShaderParameter("bg_glow", glow);
        _backdropMat.SetShaderParameter("bg_edge", edge);

        // 2) 雕刻多面晶体 N 高保色渐变色板（暗色：星蓝冰晶；亮色：青碧翡翠）
        if (isDark)
        {
            ConfigureCrystalMat(_nMatFront,
                new Color("112244"), new Color("244A86"), new Color("4E82D6"), new Color("A8D0FF"), Colors.White,
                new Color("58E8FF"), new Color("9E82FF"), new Color("D6ECFF"));
            ConfigureCrystalMat(_nMatMid,
                new Color("0E1D3B"), new Color("1F4076"), new Color("4474C4"), new Color("96C2F5"), new Color("F5FAFF"),
                new Color("4EDCF5"), new Color("9076F2"), new Color("C8E2FA"));
            ConfigureCrystalMat(_nMatBack,
                new Color("0B1730"), new Color("1A3666"), new Color("3A64B0"), new Color("84B2E8"), new Color("EAF4FF"),
                new Color("44CEE8"), new Color("846AE6"), new Color("B8D6F5"));
        }
        else
        {
            ConfigureCrystalMat(_nMatFront,
                new Color("14383F"), new Color("245C66"), new Color("3D8B94"), new Color("8CD2D4"), new Color("F4FFFF"),
                new Color("5EEAD4"), new Color("74C0FA"), new Color("D8F8F5"));
            ConfigureCrystalMat(_nMatMid,
                new Color("103036"), new Color("1E5059"), new Color("347A84"), new Color("7BC4C7"), new Color("EEFFFD"),
                new Color("50DFCA"), new Color("68B4F0"), new Color("C8F0EC"));
            ConfigureCrystalMat(_nMatBack,
                new Color("0D282E"), new Color("18444C"), new Color("2B6973"), new Color("6AB4B8"), new Color("E4FAFA"),
                new Color("42D2BC"), new Color("5CA6E4"), new Color("B8E6E2"));
        }

        // 3) 素雅瓷质棋子（深色背景启用柔和轮廓回光，确保黑子清晰可见）
        _stoneBlackMat.AlbedoColor = pal.StoneBlack;
        _stoneBlackMat.Roughness = isDark ? 0.25f : 0.30f;
        _stoneBlackMat.RimEnabled = isDark;
        _stoneBlackMat.Rim = 0.42f;
        _stoneBlackMat.RimTint = 0.30f;
        _stoneWhiteMat.AlbedoColor = pal.StoneWhite;

        // 4) 辅助线 / 悬停球
        _hoverBallMat.AlbedoColor = new Color(pal.HoverHighlight.R, pal.HoverHighlight.G, pal.HoverHighlight.B, 0.78f);
        _auxMarkerSelfMat.AlbedoColor = new Color(pal.LineSelf.R, pal.LineSelf.G, pal.LineSelf.B, 0.90f);
        _auxMarkerEnemyMat.AlbedoColor = new Color(pal.LineEnemy.R, pal.LineEnemy.G, pal.LineEnemy.B, 0.90f);

        // 6) 封面与角标排版（浅色背景文字温雅青墨不过度死黑）
        _coverBrand.AddThemeColorOverride("font_color", pal.TextPrimary);
        _coverRule.Color = pal.Accent;
        _coverCn.AddThemeColorOverride("font_color", pal.TextPrimary);
        _coverSub.AddThemeColorOverride("font_color", pal.TextDim);

        _coverPromptNormal.BgColor = isDark
            ? new Color(pal.ButtonBg.R, pal.ButtonBg.G, pal.ButtonBg.B, 0.84f)
            : new Color(pal.ButtonBg.R, pal.ButtonBg.G, pal.ButtonBg.B, 0.94f);
        _coverPromptNormal.BorderColor = new Color(pal.Accent.R, pal.Accent.G, pal.Accent.B, 0.72f);
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
                    ? new Color(pal.ButtonBg.R, pal.ButtonBg.G, pal.ButtonBg.B, 0.88f)
                    : new Color(pal.ButtonBg.R, pal.ButtonBg.G, pal.ButtonBg.B, 0.96f);
                Color cardHover = isDark
                    ? new Color(pal.ButtonHover.R, pal.ButtonHover.G, pal.ButtonHover.B, 0.96f)
                    : new Color(pal.ButtonHover.R, pal.ButtonHover.G, pal.ButtonHover.B, 0.98f);
                Color cardBorder = new Color(pal.PanelBorder.R, pal.PanelBorder.G, pal.PanelBorder.B, isDark ? 0.62f : 0.78f);

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
        Color miniBg = new Color(pal.ButtonBg.R, pal.ButtonBg.G, pal.ButtonBg.B, isDark ? 0.86f : 0.94f);
        Color miniBorder = new Color(pal.PanelBorder.R, pal.PanelBorder.G, pal.PanelBorder.B, isDark ? 0.62f : 0.78f);
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

        _morphDirty = true;
        ApplyMorphVisuals(_morphT);
        _auxDirty = true;
    }

    private static void ConfigureCrystalMat(ShaderMaterial mat,
        Color dark, Color shadowMid, Color mid, Color bright, Color highlight,
        Color fireA, Color fireB, Color rimTint)
    {
        mat.SetShaderParameter("col_dark", dark);
        mat.SetShaderParameter("col_shadow_mid", shadowMid);
        mat.SetShaderParameter("col_mid", mid);
        mat.SetShaderParameter("col_bright", bright);
        mat.SetShaderParameter("col_highlight", highlight);
        mat.SetShaderParameter("fire_a", fireA);
        mat.SetShaderParameter("fire_b", fireB);
        mat.SetShaderParameter("rim_tint", rimTint);
    }

    // ================= 11. 流体形变驱动（完全可逆 + 脏标记节流） =================

    private void ApplyMorphVisuals(float t)
    {
        bool structuralDirty = _morphDirty || MathF.Abs(t - _lastAppliedMorphT) > 1e-5f;
        _lastAppliedMorphT = t;
        _morphDirty = false;

        var pal = Palette.For(_colorblind);
        float smooth = SmoothStep(0f, 1f, t);

        if (structuralDirty)
        {
            _backdropMat.SetShaderParameter("glow_center", new Vector2(
                Mathf.Lerp(0.38f, 0.36f, smooth),
                Mathf.Lerp(0.50f, 0.51f, smooth)));
        }

        // --- Stage 1: 雕刻多面晶体 N（封面态缓动微摆展露切面光流，形变时分离薄化融入棋盘） ---
        float nOut = SmoothStep(0.00f, 0.64f, t);
        float nEase = EaseInOutCubic(nOut);
        float nAlpha = 1f - SmoothStep(0.24f, 0.66f, t);
        bool nVisible = nAlpha > 0.01f;
        _letterRoot.Visible = nVisible;
        if (nVisible)
        {
            float idleWeight = 1f - nOut;
            float breatheY = Mathf.Sin(_clock * 1.20f) * 0.038f * idleWeight;
            float swayYaw = Mathf.Sin(_clock * 0.58f) * 0.14f * idleWeight;
            float swayPitch = Mathf.Cos(_clock * 0.42f) * 0.045f * idleWeight;
            _letterRoot.Position = new Vector3(0f, breatheY, 0f);
            _letterRoot.Rotation = new Vector3(swayPitch, swayYaw, 0f);
            float hoverLift = 1f + _letterHoverGlow * 0.028f * idleWeight;
            _letterRoot.Scale = Vector3.One * hoverLift;

            if (structuralDirty)
            {
                float thin = Mathf.Lerp(1f, 0.12f, nEase);
                float spreadY = Mathf.Lerp(1f, 0.92f, nEase);
                float spreadX = Mathf.Lerp(1f, 1.18f, nEase);

                _nLeft.Position = new Vector3(
                    Mathf.Lerp(0f, 0.88f, nEase),
                    0f,
                    Mathf.Lerp(0.05f, BoardSpacing, nEase));
                _nLeft.Scale = new Vector3(spreadX, spreadY, thin);

                _nDiag.Position = Vector3.Zero;
                _nDiag.Rotation = new Vector3(0f, 0f, Mathf.Lerp(0f, 0.46f, nEase));
                _nDiag.Scale = new Vector3(Mathf.Lerp(1f, 0.72f, nEase), spreadY, thin);

                _nRight.Position = new Vector3(
                    Mathf.Lerp(0f, -0.88f, nEase),
                    0f,
                    Mathf.Lerp(-0.05f, -BoardSpacing, nEase));
                _nRight.Scale = new Vector3(spreadX, spreadY, thin);
            }

            float hoverShader = _letterHoverGlow * idleWeight;
            SetCrystalFrameUniforms(_nMatFront, nAlpha, hoverShader, _polishSweep);
            SetCrystalFrameUniforms(_nMatMid, nAlpha, hoverShader, _polishSweep);
            SetCrystalFrameUniforms(_nMatBack, nAlpha, hoverShader, _polishSweep);
        }

        // --- Stage 2: 三层 5×5 演示棋盘（仅 t/主题变化时重算 30 格线与 5 立柱，零稳态空转） ---
        if (structuralDirty)
        {
            bool boardVisible = t > 0.14f;
            _boardRoot.Visible = boardVisible || _stones.Count > 0;
            for (int z = 0; z < 3; z++)
            {
                float lz = SmoothStep(0.16f + z * 0.08f, 0.66f + z * 0.08f, t);
                float zPos = Mathf.Lerp((1 - z) * 0.32f, (1 - z) * BoardSpacing, EaseOutQuint(lz));
                _layerRoots[z].Position = new Vector3(0f, 0f, zPos);
                _layerRoots[z].Visible = lz > 0.005f;

                // 首层用 FocusGridLine、后两层用 GridLine，深浅背景下层次清晰
                Color layerColor = z == 0 ? pal.FocusGridLine : pal.GridLine;
                float layerAlpha = z == 0 ? 0.96f : z == 1 ? 0.72f : 0.48f;
                _layerGridMats[z].AlbedoColor = new Color(layerColor.R, layerColor.G, layerColor.B, layerAlpha * lz);
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
                float thick = GridThickness * Mathf.Lerp(0.40f, 1f, p);
                bar.Node.Scale = bar.Horizontal
                    ? new Vector3(len, thick, thick)
                    : new Vector3(thick, len, thick);
            }

            float connP = EaseOutQuint(SmoothStep(0.42f, 0.86f, t));
            float connLen = 2f * BoardSpacing * connP;
            _connectorMat.AlbedoColor = new Color(pal.ConnectorLine.R, pal.ConnectorLine.G, pal.ConnectorLine.B, 0.56f * connP);
            foreach (var rail in _connectors)
            {
                rail.Visible = connP > 0.01f;
                rail.Scale = new Vector3(ConnectorThickness, ConnectorThickness, Mathf.Max(0.001f, connLen));
            }
        }

        // --- Stage 3: 黑白双子（封面差速公转 → 形变弧线落位 → 菜单稳态仅驱动新落子弹出） ---
        if (_phase != Phase.Menu || structuralDirty || HasAnimatingStones())
        {
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
        }

        // --- Stage 4: 相机严格绕原点 (0,0,0) 旋转，经 HOffset 实现「略偏左」零甩动构图 ---
        float camE = EaseInOutCubic(t);
        float swirl = Mathf.Sin(camE * Mathf.Pi);
        float yaw = Mathf.Lerp(CoverYaw, _menuYaw, camE) + 0.16f * swirl;
        float pitch = Mathf.Lerp(CoverPitch, _menuPitch, camE) + 0.04f * swirl;
        float dist = Mathf.Lerp(CoverDist, MenuDist, camE) + 0.78f * swirl;
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
        if (structuralDirty)
        {
            float coverAlpha = 1f - SmoothStep(0.00f, 0.34f, t);
            _coverUi.Visible = coverAlpha > 0.01f;
            _coverUi.Modulate = new Color(1f, 1f, 1f, coverAlpha);
            _coverUi.Position = new Vector2(-12f * (1f - coverAlpha), -5f * (1f - coverAlpha));
            _coverPromptBtn.MouseFilter = t > 0.20f ? MouseFilterEnum.Ignore : MouseFilterEnum.Stop;

            float cornerP = EaseOutQuint(SmoothStep(0.56f, 0.92f, t));
            _cornerUi.Visible = cornerP > 0.01f;
            _cornerUi.Modulate = new Color(1f, 1f, 1f, cornerP);
            _cornerUi.Position = new Vector2(28f - 12f * (1f - cornerP), 16f);

            float barP = EaseOutQuint(SmoothStep(0.66f, 1.00f, t));
            _bottomBar.Visible = barP > 0.01f;
            _bottomBar.Modulate = new Color(1f, 1f, 1f, barP);
            _bottomBar.OffsetTop = -42f + 10f * (1f - barP);
            _bottomBar.OffsetBottom = -16f + 10f * (1f - barP);
        }

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

    private static void SetCrystalFrameUniforms(ShaderMaterial mat, float alphaFade, float hoverGlow, float polishSweep)
    {
        mat.SetShaderParameter("alpha_fade", alphaFade);
        mat.SetShaderParameter("hover_glow", hoverGlow);
        mat.SetShaderParameter("polish_sweep", polishSweep);
    }

    private bool HasAnimatingStones()
    {
        foreach (var s in _stones)
        {
            if (s.PopProgress < 1f)
            {
                return true;
            }
        }
        return false;
    }

    // ================= 12. 主循环与交互 =================

    public override void _Process(double delta)
    {
        float dt = (float)Mathf.Min(delta, 0.05);
        _clock += dt;
        if (_polishSweep < 1f)
        {
            _polishSweep = Mathf.Min(1f, _polishSweep + dt / 0.85f);
        }

        float pFactor = 1f - MathF.Exp(-8f * dt);
        _parallaxCurrent = _parallaxCurrent.Lerp(_parallaxTarget, pFactor);

        float targetGlow = _phase == Phase.Letter && _letterHovered ? 1f : 0f;
        _letterHoverGlow = Mathf.Lerp(_letterHoverGlow, targetGlow, 1f - MathF.Exp(-10f * dt));
        if (_phase == Phase.Letter)
        {
            float pulse = 0.86f + 0.14f * Mathf.Sin(_clock * 2.2f);
            _coverPromptBtn.Modulate = new Color(1f, 1f, 1f, pulse);
        }

        float hoverFactor = 1f - MathF.Exp(-16f * dt);
        foreach (var item in _menuItems)
        {
            float target = item.Hovered ? 1f : 0f;
            if (MathF.Abs(item.HoverAnim - target) > 0.002f)
            {
                item.HoverAnim = Mathf.Lerp(item.HoverAnim, target, hoverFactor);
            }
            else
            {
                item.HoverAnim = target;
            }
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
                _pickDirty = true;
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
            float prevYaw = _menuYaw;
            float prevPitch = _menuPitch;
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

            if (MathF.Abs(_menuYaw - prevYaw) > 1e-4f || MathF.Abs(_menuPitch - prevPitch) > 1e-4f)
            {
                _pickDirty = true;
            }

            if (_pickDirty && _hoverScreen.HasValue && !_dragging)
            {
                _hoverCell = PickCell(_hoverScreen.Value);
                _pickDirty = false;
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
                _pickDirty = true;
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
                _pickDirty = true;
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
            _pickDirty = true;
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
