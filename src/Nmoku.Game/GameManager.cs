using Godot;
using Nmoku.Core;
using Nmoku.Core.AI;
using Nmoku.Core.Saves;

namespace Nmoku.Game;

/// <summary>
/// 对局管理（M3 接线层，M4 状态机扩展）：回合/合法性/胜负/判和/悔棋/SWAP2 分支/AI，
/// 以及存档快照/恢复与复盘游标（M4）。棋盘-棋子-辅助线紧密联动：视图更新一律由事件驱动。
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
    private AIDifficulty _aiDifficulty = AIDifficulty.Medium;
    private AIEngine? _ai;
    private string? _saveId;          // 由存档载入时保留原 id（同槽覆盖写）

    /// <summary>复盘态（01-core §2.4）：非空时棋盘内容 = moves[0..ReplayIndex]，落子/悔棋被门控。</summary>
    private IReadOnlyList<Move>? _replayMoves;

    public Board Board => _board;
    public PlayerColor Current => _current;
    public int FocusZ => _focusZ;
    public int Winner => _winner;
    public bool IsDraw => _draw;
    public bool IsOver => _winner != 0 || _draw;
    public bool SwapPending => _swapPending;
    public bool Colorblind => _colorblind;
    public bool AuxEnabled => _auxEnabled;
    public bool PvE => _pve;
    public AIDifficulty Difficulty => _aiDifficulty;
    public Coordinate3D? Hover => _hover;
    public IReadOnlyList<Move> History => _history;
    /// <summary>壳层把当前层间距同步进来，存档快照使用（schema 字段 layerSpacing）。</summary>
    public double LayerSpacing { get; set; } = GameConfig.DefaultLayerSpacing;

    /// <summary>当前生效连珠数（存档外圈元数据 B14 的来源；未配置时为默认 5）。</summary>
    public int WinLength => _level?.WinLength ?? GameConfig.WinLength;

    public bool InReplay => _replayMoves != null;
    public int ReplayIndex { get; private set; }
    public int ReplayCount => _replayMoves?.Count ?? 0;

    /// <summary>
    /// 可见最后一手（最后手环标记语义，批10）：对局=整局最后一手；复盘=游标前一手（0 手=无）。
    /// 修复复盘中环冻结在整局最后一手（两视图此前都读 History[^1]，而复盘 History 是全量）。
    /// </summary>
    public Move? LastVisibleMove =>
        InReplay
            ? ReplayIndex > 0 ? _replayMoves![ReplayIndex - 1] : null
            : _history.Count > 0 ? _history[^1] : null;

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

    /// <summary>命令行兼容入口：按裸参数装配（关卡系统的简化路径，WinLength 取默认）。</summary>
    public void Configure(int size, int layers, RuleSet rules, bool pve, AIDifficulty difficulty) =>
        Configure(new LevelConfig
        {
            Id = "sandbox",
            BoardSize = size,
            Layers = layers,
            Rules = rules,
        }, pve, difficulty);

    /// <summary>
    /// 关卡装配（01-core §7）：棋盘/规则引擎/连珠数全部随 LevelConfig；预置子先落盘再由引擎
    /// 重推导回合与决策期（与 LoadFrom 同口径，不信任列表末尾的手数推断）。
    /// </summary>
    public void Configure(LevelConfig level, bool pve, AIDifficulty difficulty)
    {
        _board = new Board(level.BoardSize, level.Layers);
        _level = level;
        _engine = RuleEngineFactory.Create(level.Rules, level.WinLength);
        _pve = pve;
        _aiDifficulty = difficulty;
        _aiColor = PlayerColor.White; // PvE 人执黑，AI 执白（M3 约定）
        _ai = pve ? new AIEngine(difficulty) : null;
        _ai?.Initialize(level.BoardSize, level.Layers, level.WinLength);
        _saveId = null;
        Reset();
        ApplyInitialMoves(level.InitialMoves);
    }

    private void ApplyInitialMoves(IReadOnlyList<Move> initialMoves)
    {
        if (initialMoves.Count == 0)
        {
            return;
        }
        foreach (var move in initialMoves)
        {
            _board.Set(move.X, move.Y, move.Z, move.Player);
            _history.Add(move);
        }
        var ts = _engine.GetNextTurnState(_history, _history[^1].Player, _level);
        _swapPending = ts.IsSwapPhase;
        _current = ts.NextPlayer; // B1：决策期指针不动（=刚行棋方），标准局为对手
        StateChanged?.Invoke();
        if (_swapPending)
        {
            SwapPhaseStarted?.Invoke();
        }
        MaybeTriggerAi(); // PvE 且预置子恰好轮到 AI 则续下
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
        _replayMoves = null;
        ReplayIndex = 0;
        SetHover(null);
        StateChanged?.Invoke();
        FocusChanged?.Invoke(_focusZ);
    }

    /// <summary>
    /// 落子（2D 点击入口）：胜负/判和/决策期/合法性/执子方全部在此门控；复盘态拒绝。
    /// <para>
    /// <b>执子方门控（修复批 ③-1）</b>：PvE 下此前只判「界内+空格」，人类可在 AI 思考窗内
    /// 替 AI 落子 → AI 已算好的应手被 <see cref="ApplyAiMove"/> 的行棋方守卫静默丢弃且无重试，
    /// 对局就此卡死（双方皆人类在走）。门控点唯一即此处（判定抽成 Core 纯函数
    /// <see cref="TurnOwnership"/> 以便 xUnit 锁死）。
    /// </para>
    /// </summary>
    public bool TryPlace(int x, int y, int z)
    {
        if (IsOver || _swapPending || InReplay)
        {
            return false;
        }
        if (!TurnOwnership.CanHumanPlace(_pve, _current, _aiColor))
        {
            return false; // PvE：轮到 AI（AI 思考窗内）→ 人类不得替 AI 落子
        }
        if (!_engine.IsLegalMove(x, y, z, _current, _history, _board))
        {
            return false;
        }
        ApplyMove(x, y, z);
        return true;
    }

    /// <summary>此刻是否轮到人类落子（PvE 下 AI 思考窗内为假；视图/指示器据此显示行棋方）。</summary>
    public bool HumanTurn => TurnOwnership.CanHumanPlace(_pve, _current, _aiColor);

    /// <summary>悔棋（Ctrl+Z）：PvE 下回退到人类可行动为止；SWAP2 决策期回退即撤销首子。复盘态拒绝。</summary>
    public void Undo()
    {
        if (_history.Count == 0 || InReplay)
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

    /// <summary>SWAP2 决策：true=交换黑白（全历史换色），false=继续执白。决策期指针不动（B1）。复盘态拒绝。</summary>
    public void ResolveSwap(bool swap)
    {
        if (!_swapPending || InReplay)
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

    // ---- M4 存档快照/恢复 + 复盘（01-core §2.4/§6）----

    /// <summary>当前局面快照。isDarkTheme 由壳层传入；id 沿用载入时的（同槽覆盖），新对局生成。</summary>
    public SaveData BuildSaveData(bool isDarkTheme)
    {
        return new SaveData
        {
            Id = _saveId ?? Guid.NewGuid().ToString(),
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            BoardState = _board.ToJaggedArray(),
            Moves = [.. _history.Select(m => new SaveMove
            {
                X = m.X,
                Y = m.Y,
                Z = m.Z,
                Player = (int)m.Player,
                Timestamp = m.Timestamp,
                Evaluation = m.Evaluation,
            })],
            Rules = _level.Rules switch
            {
                RuleSet.Swap2 => "swap2",
                RuleSet.Renju => "renju",
                _ => "gomoku",
            },
            GameMode = _pve ? "pve" : "pvp",
            AiDifficulty = _pve ? _aiDifficulty switch
            {
                AIDifficulty.Easy => "easy",
                AIDifficulty.Hard => "hard",
                _ => "medium",
            } : null,
            Status = IsOver ? "finished" : "playing",
            Winner = _winner,
            CurrentPlayer = (int)_current,
            IsDarkTheme = isDarkTheme,
            PlayerColor = 1, // PvE 人执黑（M3 约定，AI 执白）
            BoardSize = _board.BoardSize,
            Layers = _board.Layers,
            LayerSpacing = LayerSpacing,
        };
    }

    /// <summary>
    /// 由存档恢复对局（棋盘尺寸/规则/模式/AI 全部随档重配）。boardState 为局面事实源，
    /// 回合与 SWAP2 决策期由引擎从 history 重新推导（不信任 currentPlayer 字段的中间态）；
    /// focusZ 读入即弃（migration-plan §3.1），复位到层 0。维度非法返回 false。
    /// winLength 为外圈元数据（B14）：v2.0.0 schema 无连珠数字段，null 回默认 5。
    /// <para>
    /// <b>半加载根治（修复批 ②-5）</b>：此前 <c>_board</c> 先被替换、引擎工厂后创建，
    /// 而 renju（引擎未实装）必抛 <c>NotSupportedException</c> → 抛出点在「棋盘已换、引擎未换」之后，
    /// 留下半加载现场且异常冒泡到 Godot 主循环。现改为：<b>规则白名单 → 连珠数范围 → 引擎先建 → 维度核对
    /// → 才动现场</b>，任一步不合即返回 false（不改任何状态）。
    /// </para>
    /// </summary>
    public bool LoadFrom(SaveData d, int? winLength = null)
    {
        if (d == null || d.BoardSize <= 0 || d.Layers <= 0 || d.Moves == null)
        {
            return false;
        }
        // 规则白名单：只受理 gomoku / swap2（renju 引擎未实装，装它必抛）
        RuleSet rules;
        if (string.Equals(d.Rules, "swap2", StringComparison.OrdinalIgnoreCase))
        {
            rules = RuleSet.Swap2;
        }
        else if (string.IsNullOrEmpty(d.Rules)
                 || string.Equals(d.Rules, "gomoku", StringComparison.OrdinalIgnoreCase))
        {
            rules = RuleSet.Gomoku;
        }
        else
        {
            GD.PrintErr($"GameManager: 规则集不受支持（{d.Rules}），拒绝装档");
            return false;
        }
        int effectiveWinLength = winLength ?? GameConfig.WinLength;
        if (effectiveWinLength < GameConfig.MinWinLength || effectiveWinLength > GameConfig.MaxWinLength)
        {
            GD.PrintErr($"GameManager: 连珠数越界（{effectiveWinLength}），拒绝装档");
            return false;
        }
        // 引擎先建：失败即返回，绝不在「棋盘已换、引擎未换」的半加载状态下抛异常
        IRuleEngine engine;
        try
        {
            engine = RuleEngineFactory.Create(rules, effectiveWinLength);
        }
        catch (Exception e)
        {
            GD.PrintErr($"GameManager: 规则引擎创建失败: {e.Message}");
            return false;
        }
        // 盘面维度须与声明一致（否则按维度索引会抛越界，且半加载）
        if (!HasConsistentBoardShape(d))
        {
            return false;
        }
        if (d.Moves.Any(m => m == null))
        {
            return false;
        }

        _generation++;
        _ai?.Cancel();
        _saveId = d.Id;

        _board = new Board(d.BoardSize, d.Layers);
        _level = new LevelConfig { Id = "sandbox", BoardSize = d.BoardSize, Layers = d.Layers, Rules = rules, WinLength = effectiveWinLength };
        _engine = engine;
        _pve = string.Equals(d.GameMode, "pve", StringComparison.OrdinalIgnoreCase);
        _aiColor = d.PlayerColor == 2 ? PlayerColor.White : PlayerColor.Black;
        _aiDifficulty = d.AiDifficulty switch
        {
            "easy" => AIDifficulty.Easy,
            "hard" => AIDifficulty.Hard,
            _ => AIDifficulty.Medium,
        };
        _ai = _pve ? new AIEngine(_aiDifficulty) : null;
        _ai?.Initialize(d.BoardSize, d.Layers, effectiveWinLength);

        _history.Clear();
        _history.AddRange(d.Moves.Select(m => m.ToMove()));
        _board.Reset();
        for (int z = 0; z < _board.Layers; z++)
        {
            for (int y = 0; y < _board.BoardSize; y++)
            {
                for (int x = 0; x < _board.BoardSize; x++)
                {
                    int v = d.BoardState[z][y][x];
                    if (v is 1 or 2)
                    {
                        _board.Set(x, y, z, (CellState)v);
                    }
                }
            }
        }

        _winner = d.Status == "finished" ? Math.Clamp(d.Winner, 0, 2) : 0;
        _draw = d.Status == "finished" && d.Winner == 0;
        _swapPending = false;
        if (_history.Count > 0)
        {
            // 引擎重推导：standard 直接得对手回合；swap2 恰在决策期则恢复 _swapPending（B1 指针不动）
            var ts = _engine.GetNextTurnState(_history, _history[^1].Player, _level);
            _swapPending = ts.IsSwapPhase;
            _current = ts.NextPlayer;
        }
        else
        {
            _current = d.CurrentPlayer is 1 or 2 ? (PlayerColor)d.CurrentPlayer : PlayerColor.Black;
        }

        _focusZ = 0;
        _replayMoves = null;
        ReplayIndex = 0;
        SetHover(null);
        StateChanged?.Invoke();
        FocusChanged?.Invoke(_focusZ);
        MaybeTriggerAi(); // PvE 恢复时恰好轮到 AI 则续下
        return true;
    }

    /// <summary>
    /// 盘面形状与自身声明的 boardSize/layers 是否一致（不一致则按维度索引会抛越界）。
    /// 校验层（<c>SaveValidator</c>）已挡一道，这里是装档前的最后一道闸门。
    /// </summary>
    private static bool HasConsistentBoardShape(SaveData d)
    {
        if (d.BoardState == null || d.BoardState.Length != d.Layers)
        {
            return false;
        }
        foreach (var layer in d.BoardState)
        {
            if (layer == null || layer.Length != d.BoardSize)
            {
                return false;
            }
            foreach (var row in layer)
            {
                if (row == null || row.Length != d.BoardSize)
                {
                    return false;
                }
            }
        }
        return true;
    }

    /// <summary>进入复盘（01-core §2.4）：棋盘重放为 0 手可见，落子/悔棋被门控。</summary>
    public bool BeginReplay(SaveData d, int? winLength = null)
    {
        if (!LoadFrom(d, winLength))
        {
            return false;
        }
        _generation++;
        _ai?.Cancel();
        _replayMoves = [.. _history];
        ReplayIndex = 0;
        ApplyReplayCursor(0);
        SetHover(null);
        StateChanged?.Invoke();
        return true;
    }

    /// <summary>复盘游标：可见手数 0..Count，棋盘整体重放。</summary>
    public void SetReplayIndex(int index)
    {
        if (_replayMoves == null)
        {
            return;
        }
        ReplayIndex = Math.Clamp(index, 0, _replayMoves.Count);
        ApplyReplayCursor(ReplayIndex);
        StateChanged?.Invoke();
    }

    /// <summary>
    /// 游标落盘：棋盘重放 + 行棋方随游标（0 手=黑先）——ghost 颜色、辅助线角色、行棋指示器
    /// 共用此语义；此前复盘 _current 冻结在载入档的终局方（批10 修复"ghost 恒黑"根因）。
    /// </summary>
    private void ApplyReplayCursor(int index)
    {
        ReplayState.ReplayMoves(_board, _replayMoves!, index);
        _current = index == 0 || _replayMoves![index - 1].Player == PlayerColor.White
            ? PlayerColor.Black
            : PlayerColor.White;
    }

    public void StepReplay(int delta) => SetReplayIndex(ReplayIndex + delta);

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

    /// <summary>
    /// 辅助线数据装配（一份数据、两个渲染器）：H 关闭、悬停非空格时返回空。批10 起复盘态不再拦截
    /// ——辅助线随游标棋盘与游标行棋方扫描（人类指示"把辅助线搬过来"，不可落子由门控保证）。
    /// </summary>
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
        if (!_pve || _ai == null || InReplay)
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
        // 传入棋盘独立快照（§10 B30）：避免池线程读取与主线程 Reset/Undo/Load 产生读竞态
        var snapshot = _board.Clone();
        _ai.GetBestMoveAsync(snapshot, _current).ContinueWith(t =>
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
