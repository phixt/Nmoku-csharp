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
    private const float ReplayGhostOpacity = 0.21f; // 批10：复盘 ghost 呈"不可落子"暗示（点击本被门控）
    private const float StarRadius = 0.12f;
    private const float MarkerRadius = 0.10f;
    private const float LastMoveRingRadius = 0.5f;

    /// <summary>对局状态源（Main 注入）。</summary>
    public GameManager? Game { get; set; }

    /// <summary>调色板（色弱切换时由 Main 换实例）。命名避开 Control.Theme。</summary>
    public Palette Palette { get; set; } = Palette.Dark;

    /// <summary>由 Main 推送的辅助线快照（与 3D 视图共用一份数据，避免重复扫描）。</summary>
    public AuxScanResult Aux { get; set; } = AuxScanResult.Empty;

    /// <summary>悬停格变化（界内格或 null）→ GameManager.SetHover。</summary>
    public event Action<Coordinate3D?>? HoverChanged;

    /// <summary>左键点击空格 → GameManager.TryPlace。</summary>
    public event Action<Coordinate3D>? PlaceRequested;

    private bool _mirrored = true; // R4-2：源默认镜像开
    private Vector2 _origin;       // 格 (0,0) 的屏幕中心（含镜像）
    private float _scale = 20f;    // px/格

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

        // 网格（人类反馈 2026-09-26：AA 线在高缩放下发糊——轴对齐线用硬边细线）
        for (int y = 0; y < size; y++)
        {
            DrawLine(CellToScreen(0, y), CellToScreen(size - 1, y), Palette.GridLine, 1.2f, false);
        }
        for (int x = 0; x < size; x++)
        {
            DrawLine(CellToScreen(x, 0), CellToScreen(x, size - 1), Palette.GridLine, 1.2f, false);
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

        // 最后手标记环（R4-8：仅焦点层显示）。批10：随"可见最后一手"——对局=整局末手，
        // 复盘=游标前一手（0 手无环）；修复复盘中环冻结在整局最后一手
        if (Game.LastVisibleMove is { } last && last.Z == focus)
        {
            DrawArc(CellToScreen(last.X, last.Y), LastMoveRingRadius * _scale,
                0, MathF.Tau, 64, Palette.LastMoveRing, 1.5f, false);
        }

        // 悬停 ghost（R4-4：界内且空格）。批10 复盘态：色随游标行棋方（GM.Current 已随游标），
        // 降透明度呈"不可落子"暗示；辅助线照常显示（ComputeAux 已放开复盘）
        if (Game.Hover is { } h && board.Get(h) == CellState.Empty)
        {
            float ghostAlpha = Game.InReplay ? ReplayGhostOpacity : GhostOpacity;
            StonePainter.DrawOnBoard(this, CellToScreen(h.X, h.Y), _scale, Game.Current.ToCell(), ghostAlpha, Palette);
        }

        // 辅助线（己方先画、敌方后画 → 敌方压上层，R4-5；复用 Main 推送的同一份快照）
        var aux = Aux;
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
                RoleColor(role, 0.9f), 1.2f, true);
        }
    }

    private void DrawStone(Vector2 pos, CellState state, float alpha) =>
        StonePainter.DrawOnBoard(this, pos, _scale, state, alpha, Palette);

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
