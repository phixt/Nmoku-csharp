using Godot;
using Nmoku.Core;
using Nmoku.Core.AI;

namespace Nmoku.Game;

/// <summary>
/// 对局管理（M3 接线层，M4 状态机扩展）：回合/合法性/胜负/判和/悔棋/SWAP2 分支/AI。
/// 棋盘-棋子-辅助线紧密联动：视图更新一律由事件驱动（人类指示 2026-09-19）。
/// SWAP2 调用约定见 IRuleEngine XML doc：currentPlayer=刚行棋方，决策期指针不动。
/// </summary>
public sealed partial class GameManager : Node
{
    private Board _board = null!;
    private IRuleEngine _engine = null!;
    private LevelConfig _level = null!;
    private readonly List<Move> _history = [];
    private PlayerColor _current = PlayerColor.Black;
    private int _focusZ;
    private int _winner;              // 0=进行中，1/2=胜者
    private bool _draw;
    private bool _swapPending;
    private bool _colorblind;
    private bool _auxEnabled = true;  // H 键 ALL↔NONE
    private Coordinate3D? _hover;
    private int _generation;          // AI 异步防串代（Reset/Undo 后旧结果作废）

    private bool _pve;
    private PlayerColor _aiColor = PlayerColor.White;
    private AIEngine? _ai;

    public Board Board => _board;
    public PlayerColor Current => _current;
    public int FocusZ => _focusZ;
    public int Winner => _winner;
    public bool IsDraw => _draw;
    public bool IsOver => _winner != 0 || _draw;
    public bool SwapPending => _swapPending;
    public bool Colorblind => _colorblind;
    public bool AuxEnabled => _auxEnabled;
    public Coordinate3D? Hover => _hover;
    public IReadOnlyList<Move> History => _history;

    /// <summary>棋盘/回合/胜负任一变化（2D 重画 + 3D Rebuild 的总信号）。</summary>
    public event Action? StateChanged;
    /// <summary>焦点层变化（A/D）。</summary>
    public event Action<int>? FocusChanged;
    /// <summary>对局结束：winner 1/2，0=判和。</summary>
    public event Action<int>? GameOver;
    /// <summary>进入 SWAP2 决策期（白方选择交换/继续）。</summary>
    public event Action? SwapPhaseStarted;
    /// <summary>悬停格变化（2D→3D 蓝点联动，null=无）。</summary>
    public event Action<Coordinate3D?>? HoverChanged;

    /// <summary>命令行/关卡参数装配（M4 关卡系统接管前入口）。</summary>
    public void Configure(int size, int layers, RuleSet rules, bool pve, AIDifficulty difficulty)
    {
        _board = new Board(size, layers);
        _level = new LevelConfig { Id = "sandbox", BoardSize = size, Layers = layers, Rules = rules };
        _engine = RuleEngineFactory.Create(rules, GameConfig.WinLength);
        _pve = pve;
        _aiColor = PlayerColor.White; // PvE 人执黑，AI 执白（M3 约定）
        _ai = pve ? new AIEngine(difficulty) : null;
        _ai?.Initialize(size, layers, GameConfig.WinLength);
        Reset();
    }

    public void Reset()
    {
        _generation++;
        _ai?.Cancel();
        _history.Clear();
        _board.Reset();
        _current = PlayerColor.Black;
        _winner = 0;
        _draw = false;
        _swapPending = false;
        _focusZ = 0;
        SetHover(null);
        StateChanged?.Invoke();
        FocusChanged?.Invoke(_focusZ);
    }

    /// <summary>落子（2D 点击入口）：胜负/判和/决策期/合法性全部在此门控。</summary>
    public bool TryPlace(int x, int y, int z)
    {
        if (IsOver || _swapPending)
        {
            return false;
        }
        if (!_engine.IsLegalMove(x, y, z, _current, _history, _board))
        {
            return false;
        }
        ApplyMove(x, y, z);
        return true;
    }

    /// <summary>悔棋（Ctrl+Z）：PvE 下回退到人类可行动为止；SWAP2 决策期回退即撤销首子。</summary>
    public void Undo()
    {
        if (_history.Count == 0)
        {
            return;
        }
        _generation++;
        _ai?.Cancel();
        PopMove();
        if (_pve && _current == _aiColor && _history.Count > 0 && !_swapPending)
        {
            PopMove();
        }
        StateChanged?.Invoke();
        if (_swapPending)
        {
            SwapPhaseStarted?.Invoke();
        }
        else
        {
            MaybeTriggerAi();
        }
    }

    /// <summary>SWAP2 决策：true=交换黑白（全历史换色），false=继续执白。决策期指针不动（B1）。</summary>
    public void ResolveSwap(bool swap)
    {
        if (!_swapPending)
        {
            return;
        }
        _swapPending = false;
        if (swap)
        {
            SwapEngine.ApplySwap(_history, _board);
        }
        StateChanged?.Invoke();
        MaybeTriggerAi();
    }

    /// <summary>A/D 循环换层（§10.5 R4-15：取模 wrap-around）；换层清悬停与覆盖（源 setFocusZ 行为）。</summary>
    public void CycleFocus(int delta)
    {
        int n = _board.Layers;
        _focusZ = ((_focusZ + delta) % n + n) % n;
        SetHover(null);
        FocusChanged?.Invoke(_focusZ);
    }

    public void ToggleAux()
    {
        _auxEnabled = !_auxEnabled;
        StateChanged?.Invoke();
    }

    public void ToggleColorblind()
    {
        _colorblind = !_colorblind;
        StateChanged?.Invoke();
    }

    /// <summary>2D 面板悬停上报（唯一悬停源，3D 蓝点跟随）。</summary>
    public void SetHover(Coordinate3D? cell)
    {
        if (Nullable.Equals(_hover, cell))
        {
            return;
        }
        _hover = cell;
        HoverChanged?.Invoke(cell);
    }

    /// <summary>辅助线数据装配（一份数据、两个渲染器）：H 关闭或悬停非空格时返回空。</summary>
    public AuxScanResult ComputeAux(Coordinate3D? hover)
    {
        if (!_auxEnabled || hover is not { } h || _board.Get(h) != CellState.Empty)
        {
            return AuxScanResult.Empty;
        }
        return AuxScan.Scan(_board, _current, h, includePlanar: true, includeSpace: true);
    }

    private void ApplyMove(int x, int y, int z)
    {
        _board.Set(x, y, z, _current);
        _history.Add(new Move(x, y, z, _current));

        if (_engine.CheckWin(x, y, z, _current, _board))
        {
            _winner = (int)_current;
            GameOver?.Invoke(_winner);
            StateChanged?.Invoke();
            return;
        }
        if (_board.IsFull())
        {
            _draw = true;
            GameOver?.Invoke(0);
            StateChanged?.Invoke();
            return;
        }

        // B1：GetNextTurnState 的 currentPlayer = 刚行棋方
        var ts = _engine.GetNextTurnState(_history, _current, _level);
        if (ts.IsSwapPhase)
        {
            _swapPending = true;
            StateChanged?.Invoke();
            SwapPhaseStarted?.Invoke();
            MaybeTriggerAi(); // PvE：AI 执白时自动"继续执白"
            return;
        }
        _current = ts.NextPlayer;
        StateChanged?.Invoke();
        MaybeTriggerAi();
    }

    private void PopMove()
    {
        var last = _history[^1];
        _history.RemoveAt(_history.Count - 1);
        _board.Set(last.X, last.Y, last.Z, CellState.Empty);
        _winner = 0;
        _draw = false;

        if (_history.Count == 0)
        {
            _current = PlayerColor.Black;
            _swapPending = false;
            return;
        }
        var ts = _engine.GetNextTurnState(_history, _history[^1].Player, _level);
        _swapPending = ts.IsSwapPhase;
        _current = ts.NextPlayer;
    }

    private void MaybeTriggerAi()
    {
        if (!_pve || _ai == null)
        {
            return;
        }
        if (_swapPending)
        {
            // PvE 的 SWAP2 决策：AI 执白时按"继续执白"处理（M3 简化，智能换色留 M4+）
            if (_current.Opponent() == _aiColor)
            {
                ResolveSwap(false);
            }
            return;
        }
        if (IsOver || _current != _aiColor)
        {
            return;
        }

        int gen = _generation;
        _ai.GetBestMoveAsync(_board, _current).ContinueWith(t =>
        {
            if (t.Result is not { } move || gen != _generation || IsOver)
            {
                return;
            }
            // 池线程 → 主线程（R5：调用侧回主线程后再验一次合法性）
            CallDeferred(nameof(ApplyAiMove), move.X, move.Y, move.Z, gen);
        });
    }

    private void ApplyAiMove(int x, int y, int z, int gen)
    {
        if (gen != _generation || IsOver || _current != _aiColor || !_engine.IsLegalMove(x, y, z, _current, _history, _board))
        {
            return;
        }
        ApplyMove(x, y, z);
    }
}
