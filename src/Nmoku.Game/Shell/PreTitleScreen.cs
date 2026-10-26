using Godot;
using Nmoku.Core;
using Nmoku.Game.Theme;

namespace Nmoku.Game.Shell;

/// <summary>
/// 预开始页（原型 R10 定稿的 Godot 原生实现，作为 Title 态壳屏替代旧 TitleScreen）：
/// 单一 3D 世界——三根字干（压紧实的线格板）即三层棋盘的种子，点击 N 真形变展开成可游玩棋盘。
/// 落子=Core.Board + Core.AuxScan 真实机制（13 方向×正反、首子即停、4 步上限；己深绿/敌红）。
/// 旋转=拖拽阻力跟手 + 松手惯性 + 欠阻尼弹簧回弹（30° 槽位）；自转在用户接管后让位。
/// 手感/构图常量集中在「可调参数」区，在引擎内继续调。与旧 TitleScreen 事件集一致（Main 接线零改动），
/// Core/状态机/路由零改动。
/// </summary>
public sealed partial class PreTitleScreen : Control
{
    public event Action? StartRequested;
    public event Action? ResumeRequested;
    public event Action? CustomGameRequested;
    public event Action? LoadRequested;
    public event Action? GuideRequested;
    public event Action? SettingsRequested;
    public event Action? QuitRequested;

    // ============ 可调参数（引擎内手感调参区） ============
    private const float MorphDuration = 1.5f;      // 形变时长（秒）
    private const float LetterYaw = -0.10f;        // 字形态偏航（rad）
    private const float MenuYaw = 0.30f;           // 盘形态偏航
    private const float CamXLetter = 3.80f;        // 字形态相机 X（锁排整体居中）
    private const float CamXMenu = 3.30f;          // 盘形态相机 X（棋盘偏左）
    private const float CamYLetter = 2.20f;        // 字形态相机 Y（锁排上移）
    private const float CamYMenu = 2.60f;          // 盘形态相机 Y
    private const float OrthoSize = 7.2f;          // 正交视口高（世界单位）
    private const float BarK = 0.18f;              // 字干压缩系数（格线不可辨=实心）
    private const float BarDx = 2.9f;              // 左右干中心距
    private const float LineWLetter = 0.26f;       // 字形态线宽（世界单位，互相覆盖=实心）
    private const float LineWBoard = 0.025f;       // 盘形态线宽
    private const float StoneR = 0.36f;            // 棋子半径（格=1，真实比例）
    private const float BallR = 0.30f;             // 预落子蓝球（比棋子略小）
    private const float MarkerR = 0.13f;           // 辅助线标记球（0.12/0.34 比例）
    private const float DragSense = 0.010f;        // 拖拽灵敏度（rad/px）
    private const float DragFollow = 14f;          // 拖拽跟手阻尼（越大越跟手）
    private const float InertiaFriction = 0.05f;   // 松手惯性摩擦（pow(base, dt)）
    private const float SpringK = 60f;             // 回弹弹簧刚度
    private const float SpringDamp = 7f;           // 回弹阻尼（欠阻尼=可见回弹）
    private const float RestStep = Mathf.Pi / 6f;  // 回弹槽位（30°）
    private const float AutoSpin = 0.66f;          // 接管前自转速度（rad/s）
    // =====================================================

    private enum Phase { Letter, Morph, Menu }
    private Phase _phase = Phase.Letter;
    private float _morphE;                          // 形变进度 0..1
    private int _morphDir = 1;

    private readonly Board _board = new(5, 3);      // 演示实例：5×5×3（规模不可成五，简单游玩）
    private PlayerColor _turn = PlayerColor.Black;

    private SubViewport _vp = null!;
    private Node3D _root = null!;                   // 板根（旋转=偏航）
    private Camera3D _cam = null!;
    private readonly List<MeshInstance3D> _lines = [];
    private readonly List<StoneVisual> _stones = [];
    private MeshInstance3D _ball = null!;
    private MeshInstance3D _auxMesh = null!;
    private readonly List<MeshInstance3D> _auxMarkers = [];
    private readonly List<Label3D> _text3D = [];
    private StandardMaterial3D _lineMat = null!;
    private StandardMaterial3D _auxLineMat = null!;

    // 线段端点：src=字形态（压缩字干）/ dst=盘形态（展开线格），根局部坐标（Z=纵深）
    private sealed record Seg(Vector3 A0, Vector3 B0, Vector3 A1, Vector3 B1, float W0, float W1);
    private readonly List<Seg> _segs = [];

    private float _yaw = LetterYaw;                 // 当前偏航（拖拽/弹簧修改）
    private float _targetYaw;                       // 拖拽目标（阻力跟手）
    private float _yawVel;                          // 松手角速度
    private bool _dragging, _userTouched, _settling;
    private float _restYaw, _lastX, _downX, _downY;
    private long _lastInteract;
    private long _downTime;
    private (int X, int Y, int Z)? _hoverCell;
    private string _lastHoverKey = "";
    private Vector2 _downPos;
    private Vector2? _hoverScreen;
    private bool _auxDirty = true;

    private Button? _resumeBtn;
    private readonly List<CanvasItem> _menuUi = [];
    private Label _status = null!;
    private Control _cornerTitle = null!;

    private const float Spacing = 2.5f;
    private const float BoardHalf = 2.5f;
    private static float DOf(int z) => z * Spacing - Spacing;
    private static readonly Color AuxSelf = new(0f, 0.55f, 0.25f);        // 浅色主题己方深绿（Palette.LineSelf）
    private static readonly Color AuxEnemy = new(0.816f, 0.008f, 0.106f); // #D0021B 敌方（Palette.LineEnemy）

    private sealed record StoneVisual(MeshInstance3D Node, int X, int Y, int Z);

    // ---- 菜单列布局（修复批 ③-7：此前硬编码 432 高/360 宽，在最小逻辑面 320×416 下出界）----
    /// <summary>设计基准逻辑面宽（1080p 窗口 @ 内容缩放 2.5 → 768 逻辑像素）。</summary>
    private const float DesignFaceWidth = 768f;
    /// <summary>设计菜单列宽（右锚定，右侧留白 <see cref="DesignRightMargin"/>）。</summary>
    private const float DesignColumnWidth = 360f;
    /// <summary>设计菜单列高（垂直居中；按钮按余量均分）。</summary>
    private const float DesignColumnHeight = 432f;
    /// <summary>设计右侧留白。</summary>
    private const float DesignRightMargin = 60f;
    /// <summary>设计按钮间距。</summary>
    private const float DesignSeparation = 12f;
    /// <summary>按钮高下限（再小就不可读）。</summary>
    private const float MinButtonHeight = 24f;
    /// <summary>菜单列按钮数（开始/继续/自定义/读档/引导/设置/退出）。</summary>
    private const int MenuButtonCount = 7;

    private readonly List<Button> _menuButtons = [];
    private VBoxContainer _menuCol = null!;
    private Rect2 _menuRect;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;

        BuildViewport();
        BuildGeometry();
        BuildStones();
        BuildAux();
        BuildText3D();
        BuildUi();
        ApplyTheme();
        UpdateLines(0f);
        UpdateStatus();
    }

    /// <summary>标题态进入（Main 状态钩子调用）：重进直接菜单形态，不重播形变。</summary>
    public void OnEntered()
    {
        if (_phase == Phase.Menu)
        {
            ShowMenuUi(true);
        }
    }

    /// <summary>继续对局按钮可见性（与旧 TitleScreen 同接口，Main 状态钩子调用）。</summary>
    public void UpdateResumeButton(bool visible)
    {
        if (_resumeBtn != null)
        {
            _resumeBtn.Visible = visible;
        }
    }

    /// <summary>
    /// 菜单列按当前逻辑面自适应（修复批 ③-7）。
    /// <para>
    /// 此前列的尺寸是硬编码的 432 高 × 360 宽（右锚定、垂直居中），而窗口最小尺寸
    /// （800×1040 物理 @ 内容缩放 2.5）推出的**最小逻辑面只有 320×416**——
    /// 最小窗口下菜单列左侧出界 100px、上下各出界 8px（M5「标题屏溢出」根治被新页重写推翻）。
    /// 现按可用空间收缩：设计尺寸以上保持原样（观感零变化），以下按比例收缩。
    /// </para>
    /// </summary>
    private void ApplyMenuLayout(Vector2 faceSize)
    {
        float marginX = Mathf.Clamp(faceSize.X * DesignRightMargin / DesignFaceWidth, 8f, DesignRightMargin);
        float colW = Mathf.Clamp(faceSize.X - 2 * marginX, 120f, DesignColumnWidth);
        // 列高：设计高 432（垂直居中，与原布局逐像素一致）；逻辑面更矮时收缩到面高
        float boxH = Mathf.Min(DesignColumnHeight, Mathf.Max(120f, faceSize.Y));
        int sep = (int)MathF.Round(Mathf.Clamp(DesignSeparation * (boxH / DesignColumnHeight), 4f, DesignSeparation));
        float buttonH = Mathf.Max(MinButtonHeight, (boxH - 6 * sep) / MenuButtonCount);
        float totalH = MenuButtonCount * buttonH + (MenuButtonCount - 1) * sep;

        _menuCol.OffsetLeft = -(colW + marginX);
        _menuCol.OffsetRight = -marginX;
        _menuCol.OffsetTop = -totalH / 2f;
        _menuCol.OffsetBottom = totalH / 2f;
        _menuCol.AddThemeConstantOverride("separation", sep);
        foreach (Button b in _menuButtons)
        {
            b.CustomMinimumSize = new Vector2(0, buttonH);
        }
        _menuRect = new Rect2(faceSize.X - marginX - colW, (faceSize.Y - totalH) / 2f, colW, totalH);
    }

    /// <summary>自测读取：菜单列的实际布局矩形（相对本屏左上角）。</summary>
    public Rect2 MenuRectForTests => _menuRect;

    /// <summary>自测：按给定逻辑面尺寸重排菜单列（模拟不同窗口尺寸），返回重排后的矩形。</summary>
    public Rect2 ApplyMenuLayoutForTests(Vector2 faceSize)
    {
        ApplyMenuLayout(faceSize);
        return _menuRect;
    }

    /// <summary>自测读取：菜单列内容的最小尺寸（按钮是否装得下列内）。</summary>
    public Vector2 MenuContentMinSizeForTests => _menuCol.GetCombinedMinimumSize();

    // ================= 3D 场景 =================

    private void BuildViewport()
    {
        _vp = new SubViewport
        {
            OwnWorld3D = true,
            Size = new Vector2I(1280, 960),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        _vp.World3D = new World3D
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = Palette.Active.Background,
            },
        };

        AddChild(_vp);                               // 视口必须入树才会渲染
        _root = new Node3D { Rotation = new Vector3(0, _yaw, 0) };
        _vp.AddChild(_root);

        _cam = new Camera3D
        {
            Projection = Camera3D.ProjectionType.Orthogonal,
            Size = OrthoSize,
            Position = new Vector3(CamXLetter, CamYLetter, 12f),
        };
        _vp.AddChild(_cam);

        var light = new DirectionalLight3D { Rotation = new Vector3(-0.9f, 0.6f, 0f), LightEnergy = 0.6f };
        _vp.AddChild(light);

        var host = new ViewportHost { Viewport = _vp };
        host.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        host.Resized += SyncViewportSize;
        AddChild(host);
        Resized += SyncViewportSize;
        SyncViewportSize();
    }

    /// <summary>SubViewport 纹理宿主（B16 v4 同款：自绘拉伸，最小尺寸恒零，与布局解耦）。</summary>
    private sealed partial class ViewportHost : Control
    {
        public SubViewport? Viewport { get; set; }
        public override void _Process(double delta) => QueueRedraw();
        public override void _Draw()
        {
            var tex = Viewport?.GetTexture();
            if (tex != null)
            {
                DrawTextureRect(tex, new Rect2(Vector2.Zero, Size), false, Colors.White);
            }
        }
    }

    /// <summary>视口尺寸跟随宿主像素（防拉伸变形；窗口/dpr 变化每帧自愈）。</summary>
    private void SyncViewportSize()
    {
        var w = Mathf.Max(64, (int)(Size.X * devicePixelRatio));
        var h = Mathf.Max(64, (int)(Size.Y * devicePixelRatio));
        if (_vp.Size != new Vector2I(w, h))
        {
            _vp.Size = new Vector2I(w, h);
        }
    }

    private float devicePixelRatio => 1f; // 宿主像素即最终像素（窗口内容缩放由根视口承担）

    private void BuildGeometry()
    {
        _lineMat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = new Color(0.353f, 0.478f, 0.494f, 0.85f), // 源次级字色（青灰），叠压成实心字干
        };

        var barRot = Mathf.Atan2(BarDx - BarK, 5f);   // 对角干摆正：左右干内缘连线的真实方位角
        var bars = new (float Bx, float Rot, int Z)[]
        {
            (2.5f - BarDx / 2, 0f, 0),
            (2.5f, barRot, 1),
            (2.5f + BarDx / 2, 0f, 2),
        };
        foreach (var (bx, rot, z) in bars)
        {
            var d = DOf(z);
            var cs = Mathf.Cos(rot);
            var sn = Mathf.Sin(rot);
            for (var i = 0; i <= 5; i++)
            {
                var du = (i - 2.5f) * BarK;
                // 列线：字形态=实心笔画（宽线互相覆盖），盘形态=该层竖线
                _segs.Add(new Seg(
                    new Vector3(bx + du * cs + 2.5f * sn, 2.5f + du * sn - 2.5f * cs, Spacing),
                    new Vector3(bx + du * cs - 2.5f * sn, 2.5f + du * sn + 2.5f * cs, Spacing),
                    new Vector3(i, 0, d), new Vector3(i, 5, d),
                    LineWLetter, LineWBoard));
                // 行线：字形态宽 0（几何消失），盘形态=该层横线
                var vs = 2.5f + (i - 2.5f) * 0.04f;
                _segs.Add(new Seg(
                    new Vector3(bx - 2.5f * BarK * cs - (vs - 2.5f) * sn, 2.5f - 2.5f * BarK * sn + (vs - 2.5f) * cs, Spacing),
                    new Vector3(bx + 2.5f * BarK * cs - (vs - 2.5f) * sn, 2.5f + 2.5f * BarK * sn + (vs - 2.5f) * cs, Spacing),
                    new Vector3(0, i, d), new Vector3(5, i, d),
                    0f, LineWBoard));
            }
        }
        foreach (var s in _segs)
        {
            var mi = new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = Vector3.One },
                MaterialOverride = _lineMat,
            };
            _lines.Add(mi);
            _root.AddChild(mi);
        }
    }

    private void BuildStones()
    {
        var stoneMesh = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 24, Rings = 12 };
        foreach (var (x, y, z, kind) in new (int, int, int, PlayerColor)[]
                 {
                     (2, 0, 1, PlayerColor.Black),   // 原图标黑子
                     (2, 1, 0, PlayerColor.White),   // 原图标白子
                 })
        {
            _board.Set(x, y, z, kind);
            var mi = new MeshInstance3D
            {
                Mesh = stoneMesh,
                Position = new Vector3(x + .5f, y + .5f, DOf(z)),
                Scale = Vector3.Zero,   // 字形态隐藏；形变中滑入格位并长成
            };
            ApplyStoneMaterial(mi, kind);
            _root.AddChild(mi);
            _stones.Add(new StoneVisual(mi, x, y, z));
        }
    }

    private void BuildAux()
    {
        _auxLineMat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            VertexColorUseAsAlbedo = true,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };
        _auxMesh = new MeshInstance3D { Visible = false };
        _auxMesh.MaterialOverride = _auxLineMat;
        _root.AddChild(_auxMesh);

        for (var i = 0; i < 26; i++)
        {
            var mat = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                NoDepthTest = true,   // 标记画在棋子之上（原图标「子心彩点」来源）
                RenderPriority = 10,
            };
            var m = new MeshInstance3D
            {
                Mesh = new SphereMesh { Radius = MarkerR, Height = MarkerR * 2, RadialSegments = 10, Rings = 6 },
                MaterialOverride = mat,
                Visible = false,
            };
            _auxMarkers.Add(m);
            _root.AddChild(m);
        }

        _ball = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = BallR, Height = BallR * 2, RadialSegments = 20, Rings = 10 },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.184f, 0.525f, 0.91f),
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            },
            Visible = false,
        };
        _root.AddChild(_ball);
    }

    private void BuildText3D()
    {
        var lxgw = GD.Load<FontFile>("res://assets/fonts/LXGWWenKaiLite-Regular.ttf");
        var times = new SystemFont { FontNames = new[] { "Times New Roman" } };
        var z = Spacing + 0.15f;
        _text3D.Add(new Label3D
        {
            Text = "moku", Font = times, FontSize = 230, PixelSize = 0.005f, Rotation = new Vector3(0, 0, -0.42f),
            Modulate = Palette.Active.TextPrimary, HorizontalAlignment = HorizontalAlignment.Left,
            Position = new Vector3(4.95f, 4.30f, z),
        });
        _text3D.Add(new Label3D
        {
            Text = I18n.T(Text.Title.GameName), Font = lxgw, FontSize = 140, PixelSize = 0.005f, Rotation = new Vector3(0, 0, -0.42f),
            Modulate = Palette.Active.TextPrimary, HorizontalAlignment = HorizontalAlignment.Left,
            Position = new Vector3(4.95f, 3.50f, z),
        });
        _text3D.Add(new Label3D
        {
            Text = I18n.T(Text.Title.Subtitle), Font = lxgw, FontSize = 52, PixelSize = 0.005f, Rotation = new Vector3(0, 0, -0.42f),
            Modulate = Palette.Active.TextDim, HorizontalAlignment = HorizontalAlignment.Left,
            Position = new Vector3(4.95f, 2.80f, z),
        });
        foreach (var l in _text3D)
        {
            _root.AddChild(l);
        }
    }

    private void BuildUi()
    {
        // 菜单按钮列（右，设计尺寸 360×432；尺寸随逻辑面自适应，见 ApplyMenuLayout）
        var lxgw = GD.Load<FontFile>("res://assets/fonts/LXGWWenKaiLite-Regular.ttf");
        var times = new SystemFont { FontNames = new[] { "Times New Roman" } };
        var col = new VBoxContainer
        {
            AnchorLeft = 1f, AnchorRight = 1f, AnchorTop = 0.5f, AnchorBottom = 0.5f,
            OffsetLeft = -420, OffsetRight = -60, OffsetTop = -216, OffsetBottom = 216,
        };
        col.AddThemeConstantOverride("separation", (int)DesignSeparation);
        _menuCol = col;
        _menuUi.Add(col);
        AddChild(col);

        Button MakeButton(string text, bool primary)
        {
            var b = new Button
            {
                Text = text,
                CustomMinimumSize = new Vector2(0, 46),
                SizeFlagsVertical = SizeFlags.ExpandFill,
                FocusMode = FocusModeEnum.None,
            };
            b.AddThemeFontSizeOverride("font_size", 25);
            b.AddThemeStyleboxOverride("normal", MakeBox(
                primary ? new Color(0.44f, 0.55f, 0.57f, 0.35f) : new Color(1, 1, 1, 0.72f),
                new Color(0.086f, 0.29f, 0.31f, primary ? 0.4f : 0.20f)));
            b.AddThemeStyleboxOverride("hover", MakeBox(new Color(1, 1, 1, 0.98f), Palette.Active.Accent));
            b.AddThemeStyleboxOverride("pressed", MakeBox(new Color(0.86f, 0.93f, 0.93f, 1f), Palette.Active.Accent));
            _menuButtons.Add(b);
            return b;
        }

        var start = MakeButton(I18n.T(Text.Title.Start), true);
        start.Pressed += () => StartRequested?.Invoke();
        col.AddChild(start);
        _menuUi.Add(start);

        _resumeBtn = MakeButton(I18n.T(Text.Title.ContinueGame), false);
        _resumeBtn.Pressed += () => ResumeRequested?.Invoke();
        _resumeBtn.Visible = false;
        col.AddChild(_resumeBtn);
        _menuUi.Add(_resumeBtn);

        foreach (var (text, ev) in new (string, Action?)[]
                 {
                     (I18n.T(Text.Title.CustomGame), CustomGameRequested),
                     (I18n.T(Text.Title.LoadGame), LoadRequested),
                     (I18n.T(Text.Title.Guide), GuideRequested),
                     (I18n.T(Text.Settings.Title), SettingsRequested),
                     (I18n.T(Text.Title.Quit), QuitRequested),
                 })
        {
            var b = MakeButton(text, false);
            if (ev != null)
            {
                b.Pressed += ev;
            }
            col.AddChild(b);
            _menuUi.Add(b);
        }

        // 左上角标题（菜单形态；从屏左滑入接续 Label3D 锁排）
        var corner = new VBoxContainer { Position = new Vector2(-320, 18), MouseFilter = MouseFilterEnum.Ignore };
        var nmLabel = new Label { Text = "Nmoku" };
        nmLabel.AddThemeFontOverride("font", times);
        nmLabel.AddThemeFontSizeOverride("font_size", 46);
        nmLabel.AddThemeColorOverride("font_color", Palette.Active.Accent);
        var cnLabel = new Label { Text = I18n.T(Text.Title.GameName) };
        cnLabel.AddThemeFontOverride("font", lxgw);
        cnLabel.AddThemeFontSizeOverride("font_size", 26);
        corner.AddChild(nmLabel);
        corner.AddChild(cnLabel);
        _cornerTitle = corner;
        _menuUi.Add(corner);
        AddChild(corner);

        // 左下重置
        var reset = new Button
        {
            Text = "↺ 返回字形",
            AnchorTop = 1f, AnchorBottom = 1f,
            OffsetLeft = 16, OffsetTop = -52, OffsetRight = 130, OffsetBottom = -18,
            FocusMode = FocusModeEnum.None,
        };
        reset.AddThemeFontSizeOverride("font_size", 14);
        reset.Pressed += StartMorphBack;
        _menuUi.Add(reset);
        AddChild(reset);

        // 状态行（底部，右移避开重置按钮）
        _status = new Label
        {
            AnchorTop = 1f, AnchorBottom = 1f,
            OffsetLeft = 175, OffsetTop = -44,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _status.AddThemeColorOverride("font_color", Palette.Active.TextDim);
        _status.AddThemeFontSizeOverride("font_size", 15);
        _menuUi.Add(_status);
        AddChild(_status);

        // 菜单列随逻辑面自适应（修复批 ③-7）：窗口尺寸变化即重排，
        // 保证最小窗口（逻辑面 320×416）下菜单列完整可见。
        Resized += () => ApplyMenuLayout(Size);
        ApplyMenuLayout(Size);

        ShowMenuUi(false);
    }

    private static StyleBoxFlat MakeBox(Color bg, Color border) => new()
    {
        BgColor = bg,
        BorderColor = border,
        BorderWidthBottom = 1, BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1,
        CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8, CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8,
    };

    private void ShowMenuUi(bool on)
    {
        foreach (var c in _menuUi)
        {
            c.Visible = on;
        }
        if (on)
        {
            _cornerTitle.Position = new Vector2(-320, 18);   // 滑入起点（视觉连续）
        }
    }

    // ================= 形变与线格 =================

    /// <summary>逐线段端点/宽度插值 → 盒体变换（字形态压紧实=实心字干；盘形态=展开线格）。</summary>
    private void UpdateLines(float e)
    {
        foreach (var (seg, mi) in _segs.Zip(_lines, (s, m) => (s, m)))
        {
            var a = seg.A0.Lerp(seg.A1, e);
            var b = seg.B0.Lerp(seg.B1, e);
            var w = Mathf.Lerp(seg.W0, seg.W1, e);
            var dir = b - a;
            var len = dir.Length();
            if (len < 0.01f || w < 0.005f)
            {
                mi.Scale = Vector3.Zero;
                continue;
            }
            mi.Position = (a + b) / 2f;
            mi.Scale = new Vector3(w, len, w);
            mi.Rotation = new Vector3(0, 0, Mathf.Atan2(dir.Y, dir.X) - Mathf.Pi / 2f);
        }
    }

    // ================= 辅助线（真实 AuxScan） =================

    private void RebuildAux()
    {
        var show = _hoverCell is { } h && _board.IsEmpty(h.X, h.Y, h.Z);
        _auxMesh.Visible = show;
        foreach (var m in _auxMarkers)
        {
            m.Visible = false;
        }
        _ball.Visible = show;
        if (!show || _hoverCell is not { } cell)
        {
            return;
        }

        var res = AuxScan.Scan(_board, _turn, new Coordinate3D(cell.X, cell.Y, cell.Z), false, true);
        var im = new ImmediateMesh();
        im.SurfaceBegin(Mesh.PrimitiveType.Lines, _auxLineMat);
        foreach (var line in res.SpaceLines)
        {
            var c = line.Role == AuxRole.Self ? AuxSelf : AuxEnemy;
            im.SurfaceSetColor(c);
            im.SurfaceAddVertex(new Vector3(line.Start.X + .5f, line.Start.Y + .5f, DOf(line.Start.Z)));
            im.SurfaceSetColor(c);
            im.SurfaceAddVertex(new Vector3(line.End.X + .5f, line.End.Y + .5f, DOf(line.End.Z)));
        }
        im.SurfaceEnd();
        _auxMesh.Mesh = im;

        var idx = 0;
        foreach (var m in res.SpaceMarkers)
        {
            if (idx >= _auxMarkers.Count)
            {
                break;
            }
            var mi = _auxMarkers[idx++];
            mi.Position = new Vector3(m.Position.X + .5f, m.Position.Y + .5f, DOf(m.Position.Z));
            ((StandardMaterial3D)mi.MaterialOverride).AlbedoColor =
                m.Role == AuxRole.Self ? AuxSelf : AuxEnemy;
            mi.Visible = true;
        }

        _ball.Position = new Vector3(cell.X + .5f, cell.Y + .5f, DOf(cell.Z));
    }

    // ================= 交互 =================

    public override void _GuiInput(InputEvent @event)
    {
        if (_phase == Phase.Letter)
        {
            if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left
                && InLetterZone(mb.Position))
            {
                StartMorph();
            }
            return;
        }
        if (_phase != Phase.Menu)
        {
            return;
        }
        if (@event is InputEventMouseButton mb2 && mb2.ButtonIndex == MouseButton.Left)
        {
            if (mb2.Pressed)
            {
                _dragging = true;
                _settling = false;
                _lastX = mb2.Position.X;
                _downPos = mb2.Position;
                _downTime = (long)Time.GetTicksMsec();
                _targetYaw = _yaw;
                _yawVel = 0;
                _userTouched = true;
            }
            else if (_dragging)
            {
                _dragging = false;
                var moved = (mb2.Position - _downPos).Length();
                if (moved < 6f && Time.GetTicksMsec() - (ulong)_downTime < 500 &&
                    _hoverCell is { } cell && _board.IsEmpty(cell.X, cell.Y, cell.Z))
                {
                    PlaceStone(cell);
                }
                else
                {
                    _yawVel = Mathf.Clamp(_yawVel, -2.5f, 2.5f);   // 松手惯性
                }
                _lastInteract = (long)Time.GetTicksMsec();
            }
        }
        else if (@event is InputEventMouseMotion mm)
        {
            if (_dragging)
            {
                _targetYaw += mm.Relative.X * DragSense;
            }
            _hoverScreen = mm.Position;
            _lastInteract = (long)Time.GetTicksMsec();
        }
    }

    private bool InLetterZone(Vector2 pos)
    {
        var minX = float.MaxValue; var maxX = float.MinValue;
        var minY = float.MaxValue; var maxY = float.MinValue;
        foreach (var s in _segs)
        {
            foreach (var p in new[] { s.A0, s.B0 })
            {
                var q = ToScreen(_root.ToGlobal(p));
                minX = Mathf.Min(minX, q.X); maxX = Mathf.Max(maxX, q.X);
                minY = Mathf.Min(minY, q.Y); maxY = Mathf.Max(maxY, q.Y);
            }
        }
        return pos.X > minX - 30 && pos.X < maxX + 30 && pos.Y > minY - 30 && pos.Y < maxY + 30;
    }

    /// <summary>世界 → 屏幕像素（正交相机线性换算）。</summary>
    private Vector2 ToScreen(Vector3 globalPos)
    {
        var vpSize = (Vector2)_vp.Size;
        var local = _cam.ToLocal(globalPos);
        var halfH = OrthoSize / 2f;
        var halfW = halfH * vpSize.X / vpSize.Y;
        var x = (local.X + halfW) / (halfW * 2f) * vpSize.X;
        var y = (halfH - local.Y) / (halfH * 2f) * vpSize.Y;
        return new Vector2(x, y) / vpSize * Size;
    }

    private (int X, int Y, int Z)? PickCell(Vector2 screenPos)
    {
        var vpPos = screenPos / Size * (Vector2)_vp.Size;
        var from = _cam.ProjectRayOrigin(vpPos);
        var dir = _cam.ProjectRayNormal(vpPos);
        var normal = _root.GlobalTransform.Basis.Z;
        (int, int, int)? best = null;
        var bestT = float.MaxValue;
        for (var z = 0; z < 3; z++)
        {
            var plane = new Plane(normal, _root.ToGlobal(new Vector3(2.5f, 2.5f, DOf(z))));
            var hit = plane.IntersectsRay(from, dir);
            if (hit == null)
            {
                continue;
            }
            var local = _root.ToLocal(hit.Value);
            var cx = (int)Mathf.Floor(local.X);
            var cy = (int)Mathf.Floor(local.Y);
            if (cx < 0 || cx >= 5 || cy < 0 || cy >= 5)
            {
                continue;
            }
            var t = (hit.Value - from).Length();
            if (t < bestT)
            {
                bestT = t;
                best = (cx, cy, z);
            }
        }
        return best;
    }

    private void PlaceStone((int X, int Y, int Z) cell)
    {
        _board.Set(cell.X, cell.Y, cell.Z, _turn);
        var mi = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 24, Rings = 12 },
            Position = new Vector3(cell.X + .5f, cell.Y + .5f, DOf(cell.Z)),
            Scale = Vector3.Zero,
        };
        ApplyStoneMaterial(mi, _turn);
        _root.AddChild(mi);
        _stones.Add(new StoneVisual(mi, cell.X, cell.Y, cell.Z));
        var tw = CreateTween();
        tw.TweenProperty(mi, "scale", Vector3.One * StoneR, 0.26).SetEase(Tween.EaseType.Out);
        _turn = _turn == PlayerColor.Black ? PlayerColor.White : PlayerColor.Black;
        UpdateStatus();
        _auxDirty = true;
    }

    private void ApplyStoneMaterial(MeshInstance3D mi, PlayerColor kind)
    {
        mi.MaterialOverride = new StandardMaterial3D
        {
            AlbedoColor = kind == PlayerColor.Black ? new Color(0.13f, 0.13f, 0.15f) : new Color(0.94f, 0.92f, 0.88f),
            Roughness = 0.55f,
            Metallic = 0.1f,
        };
    }

    private void UpdateStatus()
    {
        _status.Text = $"回合：{(_turn == PlayerColor.Black ? "黑" : "白")} · 点击空格落子 · 拖拽旋转";
    }

    // ================= 主循环 =================

    public override void _Process(double delta)
    {
        var dt = (float)Mathf.Min(delta, 0.05);
        SyncViewportSize();

        if (_phase == Phase.Morph)
        {
            _morphE += dt / MorphDuration * _morphDir;
            if (_morphE >= 1)
            {
                _morphE = 1;
                _phase = Phase.Menu;
                ShowMenuUi(true);
            }
            if (_morphE <= 0)
            {
                _morphE = 0;
                _phase = Phase.Letter;
                ShowMenuUi(false);
            }
            var e = EaseInOut(Mathf.Clamp(_morphE, 0, 1));
            _yaw = Mathf.Lerp(LetterYaw, MenuYaw, e);
            _root.Rotation = new Vector3(0, _yaw, 0);
            _cam.Position = new Vector3(Mathf.Lerp(CamXLetter, CamXMenu, e),
                Mathf.Lerp(CamYLetter, CamYMenu, e), 12f);
            DriveText3D(e);
            UpdateLines(e);
            UpdateMorphStones(e);
        }
        else if (_phase == Phase.Letter)
        {
            _yaw = LetterYaw;
            _root.Rotation = new Vector3(0, _yaw, 0);
            _cam.Position = new Vector3(CamXLetter, CamYLetter, 12f);
            DriveText3D(0f);
            UpdateLines(0f);
            UpdateMorphStones(0f);
        }
        else
        {
            // 菜单：拖拽（阻力跟手）→ 惯性 → 弹簧回弹（30° 槽位）；自转让位
            if (_dragging)
            {
                _yaw += (_targetYaw - _yaw) * Math.Min(1f, dt * DragFollow);
                _yawVel = (_targetYaw - _yaw) * DragFollow * 0.35f;
            }
            else if (Mathf.Abs(_yawVel) > 0.02f)
            {
                _yaw += _yawVel * dt;
                _yawVel *= Mathf.Pow(InertiaFriction, dt);
            }
            else if (Time.GetTicksMsec() - (ulong)_lastInteract > 350)
            {
                if (!_settling)
                {
                    _settling = true;
                    _restYaw = Mathf.Round(_yaw / RestStep) * RestStep;
                }
                var acc = -SpringK * (_yaw - _restYaw) - SpringDamp * _yawVel;
                _yawVel += acc * dt;
                _yaw += _yawVel * dt;
                if (Mathf.Abs(_yaw - _restYaw) < 0.002f && Math.Abs(_yawVel) < 0.02f)
                {
                    _yaw = _restYaw;
                    _yawVel = 0;
                }
            }
            else if (!_userTouched)
            {
                _yaw += AutoSpin * dt;   // 未接管前缓慢自转
            }
            _root.Rotation = new Vector3(0, _yaw, 0);

            if (_hoverScreen.HasValue)
            {
                _hoverCell = PickCell(_hoverScreen.Value);
            }
            if (_auxDirty || _hoverCell.HasValue)
            {
                RebuildAux();
                _auxDirty = false;
            }
        }
    }


    private void UpdateMorphStones(float e)
    {
        var me = EaseInOut(Mathf.Clamp(e * 1.3f - 0.3f, 0, 1));
        foreach (var s in _stones)
        {
            var src = s.Z == 1
                ? new Vector3(2.5f - BarDx / 2, 5.4f, Spacing)
                : new Vector3(2.5f + BarDx / 2, -0.4f, Spacing);
            var dst = new Vector3(s.X + .5f, s.Y + .5f, DOf(s.Z));
            s.Node.Position = src.Lerp(dst, me);
            s.Node.Scale = Vector3.One * (StoneR * me);
        }
    }

    private void DriveText3D(float e)
    {
        // 文字锁排：随形变从近层平面滑向左上（3D 连续），菜单后由 2D 角标标题接续
        var me = EaseInOut(Mathf.Clamp(e, 0, 1));
        var tx = Mathf.Lerp(3.75f, 1.15f, me);
        var tyTop = Mathf.Lerp(4.35f, 4.05f, me);
        var z = Spacing + 0.15f - 0.5f * me;
        var sc = Mathf.Lerp(1f, 0.45f, me);
        _text3D[0].Position = new Vector3(tx, tyTop, z);
        _text3D[1].Position = new Vector3(tx, tyTop - 1.05f * (1 - 0.55f * me), z);
        _text3D[2].Position = new Vector3(tx, tyTop - 1.85f * (1 - 0.6f * me), z);
        foreach (var l in _text3D)
        {
            l.Scale = new Vector3(sc, sc, 1f);
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // 悬停拾取走 _GuiInput；这里兜底鼠标移出控件区的情况
        if (@event is InputEventMouseMotion mm && _phase == Phase.Menu && !_dragging)
        {
            _hoverScreen = mm.Position;
        }
    }

    private void StartMorph()
    {
        if (_phase != Phase.Letter)
        {
            return;
        }
        _phase = Phase.Morph;
        _morphDir = 1;
        _morphE = 0;
        ShowMenuUi(false);
    }

    private void StartMorphBack()
    {
        if (_phase != Phase.Menu)
        {
            return;
        }
        _phase = Phase.Morph;
        _morphDir = -1;
        ShowMenuUi(false);
    }

    private void ApplyTheme()
    {
        var pal = Palette.Active;
        if (_vp?.World3D?.Environment is Godot.Environment env)
        {
            env.BackgroundColor = pal.Background;
        }
        foreach (var l in _text3D)
        {
            l.Modulate = ReferenceEquals(l, _text3D[2]) ? pal.TextDim : pal.TextPrimary;
        }
        _status.AddThemeColorOverride("font_color", pal.TextDim);
    }

    private static float EaseInOut(float t) => t < 0.5f ? 4 * t * t * t : 1 - Mathf.Pow(-2 * t + 2, 3) / 2;
}
