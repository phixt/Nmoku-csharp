using Godot;
using Nmoku.Core;
using Nmoku.Game.Theme;

namespace Nmoku.Game.Views;

/// <summary>
/// 2D 右面板（§10.5 R4-1..10）：焦点层正交语义自绘——网格/星位/棋子/悬停 ghost/辅助线/最后手环。
/// F 镜像（默认开，X 轴翻转实现）；颜色全走 Palette；悬停与落子上报 GameManager（紧密联动，本视图不持棋局状态）。
/// </summary>
public sealed partial class Board2DView : Control
{
    private const float GridPadding = 0.5f;   // R4-1 安全边距（格）
    private const float StoneRadius = 0.45f;  // 格单位
    private const float GhostOpacity = 0.35f; // 源 HOVER_PIECE_OPACITY
    private const float StarRadius = 0.12f;
    private const float MarkerRadius = 0.10f;
    private const float LastMoveRingRadius = 0.5f;

    /// <summary>对局状态源（Main 注入）。</summary>
    public GameManager? Game { get; set; }

    /// <summary>调色板（色弱切换时由 Main 换实例）。命名避开 Control.Theme。</summary>
    public Palette Palette { get; set; } = Palette.Dark;

    /// <summary>悬停格变化（界内格或 null）→ GameManager.SetHover。</summary>
    public event Action<Coordinate3D?>? HoverChanged;

    /// <summary>左键点击空格 → GameManager.TryPlace。</summary>
    public event Action<Coordinate3D>? PlaceRequested;

    private bool _mirrored = true; // R4-2：源默认镜像开
    private Vector2 _origin;       // 格 (0,0) 的屏幕中心（含镜像）
    private float _scale = 20f;    // px/格

    // 高光光斑纹理（白色径向渐变到全透明——末档 alpha=0，四角不会出现方形色块）
    private GradientTexture2D? _glowTex;

    private void EnsureGlowTexture()
    {
        if (_glowTex != null)
        {
            return;
        }
        var gradient = new Gradient
        {
            Colors = [new Color(1, 1, 1, 0.9f), new Color(1, 1, 1, 0f)],
            Offsets = [0f, 1f],
        };
        _glowTex = new GradientTexture2D
        {
            Gradient = gradient,
            Fill = GradientTexture2D.FillEnum.Radial,
            FillFrom = new Vector2(0.5f, 0.5f),
            FillTo = new Vector2(1f, 0.5f),
            Width = 64,
            Height = 64,
        };
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        MouseExited += () => HoverChanged?.Invoke(null);
    }

    /// <summary>F 键镜像切换。</summary>
    public void ToggleMirror()
    {
        _mirrored = !_mirrored;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (Game == null)
        {
            return;
        }
        ComputeLayout();
        var board = Game.Board;
        int size = board.BoardSize;
        int focus = Game.FocusZ;

        // 网格
        for (int y = 0; y < size; y++)
        {
            DrawLine(CellToScreen(0, y), CellToScreen(size - 1, y), Palette.GridLine, 1.5f, true);
        }
        for (int x = 0; x < size; x++)
        {
            DrawLine(CellToScreen(x, 0), CellToScreen(x, size - 1), Palette.GridLine, 1.5f, true);
        }

        // 星位（R4-3/D1：边星 max(1, floor(size/5)) + 中心，由 size 推导禁止硬编码）
        int offset = Math.Max(1, size / 5);
        float center = (size - 1) / 2f;
        foreach (var (sx, sy) in new[]
                 {
                     (center, center), (offset, offset), (offset, size - 1 - offset),
                     (size - 1 - offset, offset), (size - 1 - offset, size - 1 - offset),
                 })
        {
            DrawCircle(CellToScreen(sx, sy), StarRadius * _scale, Palette.StarPoint,
                filled: true, width: -1f, antialiased: true);
        }

        // 焦点层棋子（R4-9：只画焦点层，实体）
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                var state = board.Get(x, y, focus);
                if (state != CellState.Empty)
                {
                    DrawStone(CellToScreen(x, y), state, 1f);
                }
            }
        }

        // 最后手标记环（R4-8：仅焦点层显示）
        if (Game.History.Count > 0)
        {
            var last = Game.History[^1];
            if (last.Z == focus)
            {
                DrawArc(CellToScreen(last.X, last.Y), LastMoveRingRadius * _scale,
                    0, MathF.Tau, 40, Palette.LastMoveRing, 3f, true);
            }
        }

        // 悬停 ghost（R4-4：界内且空格）
        if (Game.Hover is { } h && board.Get(h) == CellState.Empty)
        {
            DrawStone(CellToScreen(h.X, h.Y), Game.Current.ToCell(), GhostOpacity);
        }

        // 辅助线（己方先画、敌方后画 → 敌方压上层，R4-5）
        var aux = Game.ComputeAux(Game.Hover);
        DrawAuxLines(aux.PlanarLines, AuxRole.Self);
        DrawAuxLines(aux.PlanarLines, AuxRole.Enemy);
        foreach (var m in aux.PlanarMarkers)
        {
            var color = RoleColor(m.Role, 0.8f);
            DrawCircle(CellToScreen(m.Position.X, m.Position.Y), MarkerRadius * _scale, color,
                filled: true, width: -1f, antialiased: true);
        }
    }

    private void DrawAuxLines(IReadOnlyList<AuxLine> lines, AuxRole role)
    {
        foreach (var line in lines)
        {
            if (line.Role != role)
            {
                continue;
            }
            DrawLine(CellToScreen(line.Start.X, line.Start.Y), CellToScreen(line.End.X, line.End.Y),
                RoleColor(role, 0.9f), 2f, true);
        }
    }

    private void DrawStone(Vector2 pos, CellState state, float alpha)
    {
        // 三层绘制（源 canvas 质感）：体色圆 + 左上高光光斑 + 描边。
        // 不用整张径向纹理直接铺方形——高光偏心时渐变边缘色会涂满四角（"奇怪方框"根因）。
        bool isBlack = state == CellState.Black;
        var baseColor = isBlack ? Palette.StoneBlack : Palette.StoneWhite;
        var body = isBlack ? baseColor.Darkened(0.55f) : baseColor.Darkened(0.25f);
        DrawCircle(pos, StoneRadius * _scale, new Color(body.R, body.G, body.B, alpha),
            filled: true, width: -1f, antialiased: true);

        EnsureGlowTexture();
        var glowStrength = (isBlack ? 0.35f : 0.55f) * alpha;
        // 高光必须完整落在轮廓内：偏移幅 0.30√2·r ≈ 0.42r + 光斑半径 0.45r = 0.87r < r。
        // 旧参数（0.28 格偏移 + 0.55r 光斑 = 1.43r）会穿透圆形边框（M4R 验收反馈）。
        float glowDiameter = StoneRadius * 0.9f * _scale;
        var glowCenter = pos + new Vector2(-0.30f, -0.30f) * StoneRadius * _scale;
        var glowRect = new Rect2(glowCenter - new Vector2(glowDiameter / 2f, glowDiameter / 2f),
            new Vector2(glowDiameter, glowDiameter));
        DrawTextureRect(_glowTex, glowRect, tile: false, modulate: new Color(1, 1, 1, glowStrength));

        var outline = isBlack
            ? new Color(0.07f, 0.07f, 0.08f, alpha)
            : new Color(baseColor.R, baseColor.G, baseColor.B, MathF.Min(1f, alpha + 0.2f));
        DrawArc(pos, StoneRadius * _scale, 0, MathF.Tau, 40, outline, 1.5f, true);
    }

    private Color RoleColor(AuxRole role, float alpha)
    {
        var @base = role == AuxRole.Self ? Palette.LineSelf : Palette.LineEnemy;
        return new Color(@base.R, @base.G, @base.B, alpha);
    }

    private void ComputeLayout()
    {
        int size = Game?.Board.BoardSize ?? GameConfig.DefaultBoardSize;
        float total = (size - 1) + GridPadding * 2;
        _scale = MathF.Min(Size.X, Size.Y) / total;
        float half = (size - 1) / 2f;
        // 纵向镜像（B10）：行 0 在底部，与 3D 竖直平面（行=世界Y 向上）一致
        _origin = new Vector2(
            Size.X / 2f + (_mirrored ? half : -half) * _scale,
            Size.Y / 2f + half * _scale);
    }

    private Vector2 CellToScreen(float x, float y) =>
        new(_origin.X + x * _scale * (_mirrored ? -1 : 1), _origin.Y - y * _scale);

    private Coordinate3D? ScreenToCell(Vector2 pos)
    {
        if (Game == null)
        {
            return null;
        }
        ComputeLayout();
        float x = (pos.X - _origin.X) * (_mirrored ? -1 : 1) / _scale;
        float y = (_origin.Y - pos.Y) / _scale;
        int gx = (int)MathF.Round(x);
        int gy = (int)MathF.Round(y);
        if (gx < 0 || gx >= Game.Board.BoardSize || gy < 0 || gy >= Game.Board.BoardSize)
        {
            return null;
        }
        return new Coordinate3D(gx, gy, Game.FocusZ);
    }

    public override void _GuiInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseMotion motion:
                HoverChanged?.Invoke(ScreenToCell(motion.Position));
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } click:
                if (ScreenToCell(click.Position) is { } cell)
                {
                    PlaceRequested?.Invoke(cell);
                }
                break;
        }
    }
}
