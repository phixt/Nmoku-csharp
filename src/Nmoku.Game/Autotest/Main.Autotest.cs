using Godot;
using Nmoku.Core;
using Nmoku.Core.AI;
using Nmoku.Core.Audio;
using Nmoku.Core.Camera;
using Nmoku.Core.Saves;
using Nmoku.Game;
using Nmoku.Game.Audio;
using Nmoku.Game.Shell;
using Nmoku.Game.Theme;

/// <summary>无头自测与回归锁（由 Main.cs 拆出，批次 ④；与生产 _Process 并行运行，不再早退跳过生产路径）。</summary>
public partial class Main
{
    // ---- 无头对局自测（-- --autotest）：M3 对局闭环 + M4 状态机/存档/复盘；CI 冒烟依赖 AUTOTEST ALL PASS ----
    private bool _autotestArmed;
    private bool _autotestBuilt;
    private readonly Queue<(string Name, Func<string> Run)> _autotestSteps = new();
    private string _autotestFails = "";
    private int _autotestWait;
    private int _autotestStepWait;
    private string? _autotestStepName;
    private int _autotestTotalFrames;
    /// <summary>单步 wait 帧预算（@120fps ≈ 5s）：headless 不限帧时帧数≠时间，必须有显式上限。</summary>
    private const int AutotestMaxStepWaitFrames = 600;
    /// <summary>全局看门狗（@120fps ≈ 5.5min）：任何挂死形态（无限 wait/异常风暴）都在此强制终止。</summary>
    private const int AutotestMaxTotalFrames = 40000;
    /// <summary>进程内内存上限（P1-2）：提交量超限视为泄漏/死循环，自杀退出防拖垮整机（77GB 事故教训）。</summary>
    private const long AutotestMemoryLimitBytes = 6L * 1024 * 1024 * 1024;

    private int _autotestMemCheckFrame;
    /// <summary>--autotest-only=&lt;子串&gt;：只跑名字匹配的步骤（bisection 用），其余立即出队。</summary>
    private string? _autotestOnly;

    private void RunAutotest()
    {
        if (!_autotestBuilt)
        {
            _autotestBuilt = true;
            _autotestOnly = OS.GetCmdlineUserArgs()
                .FirstOrDefault(a => a.StartsWith("--autotest-only="))?["--autotest-only=".Length..];
            BuildAutotestSteps();
            // headless 不限帧：_Process 每秒数千帧（77GB 提交内存事故的放大器），显式限帧
            Engine.MaxFps = 120;
        }
        // 内存自检每 60 帧（@120fps≈0.5s）一次：超限自杀 + 涨势日志（定位泄漏帧）
        if (++_autotestMemCheckFrame >= 60)
        {
            _autotestMemCheckFrame = 0;
            long commit = System.Diagnostics.Process.GetCurrentProcess().PrivateMemorySize64;
            string stepInfo = _autotestSteps.Count > 0 ? _autotestSteps.Peek().Name : "(done)";
            if (commit > AutotestMemoryLimitBytes)
            {
                GD.Print($"AUTOTEST ABORT: commit {commit / (1024 * 1024)}MB 超上限，卡在 [{stepInfo}] phase={_saveAlignPhase}/{_qsPhase}/{_echoPhase}");
                GetTree().Quit(1);
                return;
            }
            GD.Print($"AUTOTEST mem: {commit / (1024 * 1024)}MB step=[{stepInfo}] saveAlignPhase={_saveAlignPhase} qsPhase={_qsPhase}");
        }
        if (_autotestSteps.Count == 0)
        {
            GD.Print(_autotestFails.Length == 0 ? "AUTOTEST ALL PASS" : $"AUTOTEST FAIL: {_autotestFails}");
            GetTree().Quit();
            return;
        }
        // 全局看门狗：先于步骤执行，异常风暴/无限 wait 都逃不出总预算
        if (++_autotestTotalFrames > AutotestMaxTotalFrames)
        {
            string remaining = string.Join(",", _autotestSteps.Select(s => s.Name));
            GD.Print($"AUTOTEST FAIL: [global-watchdog frame {_autotestTotalFrames}, 未完成: {remaining}] ");
            GetTree().Quit(1);
            return;
        }
        var (name, run) = _autotestSteps.Peek();
        if (_autotestOnly != null && !name.Contains(_autotestOnly, StringComparison.OrdinalIgnoreCase))
        {
            _autotestSteps.Dequeue(); // bisection 过滤：不匹配的步骤直接跳过
            return;
        }
        if (_autotestStepName != name)
        {
            _autotestStepName = name; // 新步骤：重置单步 wait 预算
            _autotestStepWait = 0;
        }
        string result;
        try
        {
            result = run();
        }
        catch (Exception e)
        {
            // 异常视同 FAIL 且照样出队：绝不让同一步骤每帧重试
            // （77GB 提交内存事故根因：异常跳过出队 → 同一坏步骤每帧重建异常对象+堆栈+引擎错误记录）
            GD.PrintErr($"AUTOTEST step {name} threw:\n{e}");
            result = $"EXCEPTION {e.GetType().Name}: {e.Message}";
        }
        if (result == "wait")
        {
            if (++_autotestStepWait <= AutotestMaxStepWaitFrames)
            {
                return;
            }
            result = $"wait 超预算 >{AutotestMaxStepWaitFrames} 帧"; // 超预算落为 FAIL，照常出队
        }
        _autotestSteps.Dequeue();
        if (result != "ok")
        {
            _autotestFails += $"[{name}: {result}] ";
        }
        GD.Print($"AUTOTEST {name}: {result}");
    }

    private void BuildAutotestSteps()
    {
        // PvP 五连胜利 + 悔棋
        _gm.Configure(9, 3, RuleSet.Gomoku, false, AIDifficulty.Medium);
        (int x, int y)[] black = [(0, 5), (1, 5), (2, 5), (3, 5), (4, 5)];
        (int x, int y)[] white = [(0, 6), (1, 6), (2, 6), (3, 6)];
        for (int k = 0; k < 4; k++)
        {
            var b = black[k];
            _autotestSteps.Enqueue(("pvp-black" + k, () => _gm.TryPlace(b.x, b.y, 0) ? "ok" : "FAIL"));
            var w = white[k];
            _autotestSteps.Enqueue(("pvp-white" + k, () => _gm.TryPlace(w.x, w.y, 0) ? "ok" : "FAIL"));
        }
        var lastBlack = black[4];
        _autotestSteps.Enqueue(("pvp-black4", () => _gm.TryPlace(lastBlack.x, lastBlack.y, 0) ? "ok" : "FAIL"));
        _autotestSteps.Enqueue(("pvp-win", () =>
            _gm.IsOver && _gm.Winner == 1 ? "ok" : "FAIL"));
        _autotestSteps.Enqueue(("undo", () =>
        {
            _gm.Undo();
            return !_gm.IsOver && _gm.History.Count == 8 && _gm.Current == PlayerColor.Black ? "ok" : "FAIL";
        }));
        _autotestSteps.Enqueue(("reset", () =>
        {
            _gm.Reset();
            return !_gm.IsOver && _gm.History.Count == 0 ? "ok" : "FAIL";
        }));

        // SWAP2 pass 分支：黑首子 → 白决策期 → 继续 → 黑连下第 2 手
        _autotestSteps.Enqueue(("cfg-swap2", () =>
        {
            _gm.Configure(9, 3, RuleSet.Swap2, false, AIDifficulty.Medium);
            return "ok";
        }));
        _autotestSteps.Enqueue(("swap2-first", () => _gm.TryPlace(4, 4, 0) ? "ok" : "FAIL"));
        _autotestSteps.Enqueue(("swap2-pending", () => _gm.SwapPending ? "ok" : "FAIL"));
        _autotestSteps.Enqueue(("swap2-pass", () =>
        {
            _gm.ResolveSwap(false);
            return !_gm.SwapPending && _gm.Current == PlayerColor.Black ? "ok" : "FAIL";
        }));
        _autotestSteps.Enqueue(("swap2-black2", () =>
            _gm.TryPlace(5, 4, 0) && _gm.Current == PlayerColor.White ? "ok" : "FAIL"));

        // SWAP2 swap 分支：决策后全历史换色
        _autotestSteps.Enqueue(("cfg-swap2b", () =>
        {
            _gm.Configure(9, 3, RuleSet.Swap2, false, AIDifficulty.Medium);
            return "ok";
        }));
        _autotestSteps.Enqueue(("swap2b-first", () => _gm.TryPlace(4, 4, 0) ? "ok" : "FAIL"));
        _autotestSteps.Enqueue(("swap2b-swap", () =>
        {
            _gm.ResolveSwap(true);
            return _gm.History[0].Player == PlayerColor.White && _gm.Board.Get(4, 4, 0) == CellState.White
                ? "ok"
                : "FAIL";
        }));
        _autotestSteps.Enqueue(("swap2b-black2", () => _gm.TryPlace(3, 3, 0) ? "ok" : "FAIL"));
        // SWAP2 回归锁（2026-11-27）：① 换色后悔棋退回第 1 手必须还原首子为黑且重进决策期
        _autotestSteps.Enqueue(("swap2-undo-revert", () =>
        {
            _overlay.CloseAll();
            _gm.Undo();
            bool ok = _gm.History.Count == 1
                      && _gm.History[0].Player == PlayerColor.Black
                      && _gm.Board.Get(4, 4, 0) == CellState.Black
                      && _gm.SwapPending
                      && _gm.Current == PlayerColor.Black
                      && _overlay.IsModalOpen;
            _overlay.CloseAll();
            return ok
                ? "ok"
                : $"FAIL hist={_gm.History.Count} p0={(_gm.History.Count > 0 ? _gm.History[0].Player : 0)} pend={_gm.SwapPending} cur={_gm.Current} modal={_overlay.IsModalOpen}";
        }));
        // ② PvE + SWAP2 首手后 AI 自动继续执白，绝不向人类弹出幽灵确认框
        _autotestSteps.Enqueue(("swap2-pve-no-ghost-modal", () =>
        {
            _overlay.CloseAll();
            _gm.Configure(9, 3, RuleSet.Swap2, pve: true, AIDifficulty.Medium);
            bool p1 = _gm.TryPlace(4, 4, 0);
            bool noModal = !_overlay.IsModalOpen && !_gm.SwapPending && _gm.Current == PlayerColor.Black;
            bool p2 = _gm.TryPlace(5, 4, 0);
            return p1 && noModal && p2
                ? "ok"
                : $"FAIL p1={p1} modal={_overlay.IsModalOpen} pend={_gm.SwapPending} cur={_gm.Current} p2={p2}";
        }));
        // ③ 1 手 SWAP2 读档（未换色弹决策框不死锁 / 已换色白子直接轮黑 / 复盘不弹窗）
        _autotestSteps.Enqueue(("swap2-load-1move-unblocked", () =>
        {
            _overlay.CloseAll();
            _gm.Configure(9, 3, RuleSet.Swap2, pve: false, AIDifficulty.Medium);
            _gm.TryPlace(4, 4, 0);
            var unswappedSave = _gm.BuildSaveData(true);
            _gm.ResolveSwap(true);
            var swappedSave = _gm.BuildSaveData(true);
            _overlay.CloseAll();

            bool load1 = _gm.LoadFrom(unswappedSave);
            bool unswappedOk = load1 && _gm.SwapPending && _overlay.IsModalOpen;
            _overlay.CloseAll();

            bool load2 = _gm.LoadFrom(swappedSave);
            bool swappedOk = load2 && !_gm.SwapPending && !_overlay.IsModalOpen && _gm.Current == PlayerColor.Black;

            bool rep = _gm.BeginReplay(unswappedSave);
            bool replayNoModal = rep && !_overlay.IsModalOpen;
            _overlay.CloseAll();
            return unswappedOk && swappedOk && replayNoModal
                ? "ok"
                : $"FAIL unswapped={unswappedOk} swapped={swappedOk} replay={replayNoModal}";
        }));
        // ④ PvE 存读档 _aiColor 方向校验（修复此前 LoadFrom 将人类色反写为 AI 色导致门控与胜负音效全反）
        _autotestSteps.Enqueue(("pve-save-load-aicolor", () =>
        {
            _gm.Configure(9, 3, RuleSet.Gomoku, pve: true, AIDifficulty.Medium);
            var saveBlackHuman = _gm.BuildSaveData(true); // PlayerColor=1（人黑 AI 白）
            bool loaded1 = _gm.LoadFrom(saveBlackHuman);
            bool blackHumanOk = loaded1
                                && saveBlackHuman.PlayerColor == 1
                                && _gm.AiColor == PlayerColor.White
                                && AudioPolicy.ResolveGameOverSfx(true, 1, _gm.AiColor) == GameOverSfx.Win
                                && AudioPolicy.ResolveGameOverSfx(true, 2, _gm.AiColor) == GameOverSfx.Lose;

            saveBlackHuman.PlayerColor = 2; // 人白 AI 黑
            bool loaded2 = _gm.LoadFrom(saveBlackHuman);
            bool whiteHumanOk = loaded2
                                && _gm.AiColor == PlayerColor.Black
                                && AudioPolicy.ResolveGameOverSfx(true, 2, _gm.AiColor) == GameOverSfx.Win
                                && AudioPolicy.ResolveGameOverSfx(true, 1, _gm.AiColor) == GameOverSfx.Lose;
            return blackHumanOk && whiteHumanOk
                ? "ok"
                : $"FAIL blackHuman={blackHumanOk} whiteHuman={whiteHumanOk} ai={_gm.AiColor}";
        }));

        // PvE：人类落子后 AI 应答
        _autotestSteps.Enqueue(("cfg-pve", () =>
        {
            _gm.Configure(9, 3, RuleSet.Gomoku, true, AIDifficulty.Medium);
            return "ok";
        }));
        _autotestSteps.Enqueue(("pve-human", () => _gm.TryPlace(4, 4, 1) ? "ok" : "FAIL"));
        _autotestSteps.Enqueue(("pve-ai-reply", () =>
        {
            _autotestWait++;
            if (_autotestWait > 600)
            {
                return "FAIL timeout";
            }
            return _gm.History.Count == 2 ? "ok" : "wait";
        }));
        // PvE 执子方门控（修复批 ③-1）：AI 思考窗内人类不得替 AI 落子——
        // AI 的应手经 CallDeferred 回主线程（下一帧才应用），故同帧内行棋方必为 AI 色。
        _autotestSteps.Enqueue(("pve-turn-gate", () =>
        {
            _gm.Configure(9, 3, RuleSet.Gomoku, true, AIDifficulty.Medium);
            bool humanPlaced = _gm.TryPlace(4, 4, 1);
            bool aiTurn = _gm.Current == PlayerColor.White;      // 人执黑、AI 执白（M3 约定）
            bool blocked = !_gm.TryPlace(3, 3, 1);                // 替 AI 落子必须被拒
            bool noExtraMove = _gm.History.Count == 1;           // 被拒的落子不得进历史
            return humanPlaced && aiTurn && blocked && noExtraMove
                ? "ok"
                : $"FAIL placed={humanPlaced} aiTurn={aiTurn} blocked={blocked} hist={_gm.History.Count}";
        }));

        // ---- M4：状态机 + 存档往返 + 复盘（隔离目录 user://saves_autotest，不污染真实存档）----
        _autotestSteps.Enqueue(("m4-cfg", () =>
        {
            _gm.Configure(9, 3, RuleSet.Gomoku, false, AIDifficulty.Medium);
            _gm.TryPlace(4, 4, 0);
            _gm.TryPlace(3, 3, 0);
            return _gm.History.Count == 2 ? "ok" : "FAIL";
        }));
        _autotestSteps.Enqueue(("m4-start", () =>
            _store.StartGame() && _store.State == GameState.Playing ? "ok" : "FAIL"));
        _autotestSteps.Enqueue(("m4-pause", () =>
            _store.Pause() && _store.State == GameState.Paused ? "ok" : "FAIL"));
        _autotestSteps.Enqueue(("m4-illegal-reject", () =>
            !_store.Fire(GameTrigger.Pause) && _store.State == GameState.Paused ? "ok" : "FAIL"));
        _autotestSteps.Enqueue(("m4-resume", () =>
            _store.Resume() && _store.State == GameState.Playing ? "ok" : "FAIL"));
        _autotestSteps.Enqueue(("m4-save-load-replay", AutotestSaveLoadReplay));
        _autotestSteps.Enqueue(("m4-win-gameover", () =>
        {
            // 承 m4-cfg（黑 (4,4)，白 (3,3)）：黑在 y=5 凑五连
            (int x, int y)[] b = [(0, 5), (1, 5), (2, 5), (3, 5), (4, 5)];
            (int x, int y)[] w = [(0, 6), (1, 6), (2, 6), (3, 6)];
            for (int k = 0; k < 4; k++)
            {
                if (!_gm.TryPlace(b[k].x, b[k].y, 0) || !_gm.TryPlace(w[k].x, w[k].y, 0))
                {
                    return "FAIL place";
                }
            }
            return _gm.TryPlace(b[4].x, b[4].y, 0)
                && _gm.IsOver && _gm.Winner == 1
                && _store.State == GameState.GameOver ? "ok" : "FAIL";
        }));
        _autotestSteps.Enqueue(("m4-gameover-newgame", () =>
            _store.Fire(GameTrigger.NewGame) && _store.State == GameState.Playing && _gm.History.Count == 0
                ? "ok"
                : "FAIL"));
        _autotestSteps.Enqueue(("m4-back-to-title", () =>
            _store.Pause() && _store.ReturnToTitle() && _store.State == GameState.Title ? "ok" : "FAIL"));
        _autotestSteps.Enqueue(("m4-import", AutotestImportClipboardPath));

        // ---- M4R：首中即吞回归（同键跨态双触发根因：修复前 ESC 同帧 pause+resume 互抵）----
        _autotestSteps.Enqueue(("m4r-start", () =>
        {
            _gm.Configure(9, 3, RuleSet.Gomoku, false, AIDifficulty.Medium);
            return _store.StartGame() && _store.State == GameState.Playing ? "ok" : "FAIL";
        }));
        _autotestSteps.Enqueue(("m4r-pause-stays", () =>
        {
            SimulateActionPress("pause");
            return _store.State == GameState.Paused ? "ok" : "FAIL";
        }));
        _autotestSteps.Enqueue(("m4r-restart-opens-confirm", () =>
        {
            SimulateActionPress("restart"); // R 在 Paused=重开请求：应只弹确认框
            return _overlay.IsModalOpen ? "ok" : "FAIL";
        }));
        _autotestSteps.Enqueue(("m4r-cancel-confirm", () =>
        {
            _overlay.CloseAll();
            return !_overlay.IsModalOpen && _store.State == GameState.Paused ? "ok" : "FAIL";
        }));
        _autotestSteps.Enqueue(("m4r-resume", () =>
        {
            SimulateActionPress("resume");
            return _store.State == GameState.Playing ? "ok" : "FAIL";
        }));
        _autotestSteps.Enqueue(("m4r-router-keepalive", () =>
        {
            bool ok = _gameScreen.IsInsideTree() && _gameScreen.Visible;
            _store.Pause();
            ok = ok && _store.State == GameState.Paused;
            _store.ReturnToTitle();
            ok = ok && _store.State == GameState.Title
                && _gameScreen.IsInsideTree() && !_gameScreen.Visible; // 保留现场：隐藏不销毁
            ok = ok && _store.ResumeLive() && _store.State == GameState.Playing && _gameScreen.Visible;
            return ok ? "ok" : "FAIL";
        }));
        _autotestSteps.Enqueue(("m4r-help-overlay", () =>
        {
            ToggleHelp();
            bool opened = _router.IsOverlayOpen("help");
            ToggleHelp();
            return opened && !_router.IsOverlayOpen("help") ? "ok" : "FAIL";
        }));
        _autotestSteps.Enqueue(("m4r-newgame-confirm-guard", () =>
        {
            // 保留局存在时"开始游戏"先确认覆盖；取消则现场保留
            _gm.TryPlace(4, 4, 0);
            _store.Pause();
            _store.ReturnToTitle();
            _store.RequestStartNew();
            bool asked = _overlay.IsModalOpen;
            _overlay.CloseAll();
            return asked && _store.State == GameState.Title && _gm.History.Count == 1 ? "ok" : "FAIL";
        }));
        _autotestSteps.Enqueue(("m4r-overlay-unmount-frees", () =>
        {
            // 残留面板回归（"存档菜单取消无响应"根因）：关闭浮层后旧实例必须被释放
            OpenSaveMenu();
            _staleProbe = _saveMenu;
            CloseSaveMenu();
            return _router.IsOverlayOpen("save-menu") ? "FAIL" : "ok";
        }));
        _autotestSteps.Enqueue(("m4r-overlay-unmount-frees-2", () =>
        {
            bool freed = !IsInstanceValid(_staleProbe); // QueueFree 跨帧生效
            return freed ? "ok" : "FAIL";
        }));
        _autotestSteps.Enqueue(("m4r-key-echo-filter", AutotestKeyEchoFilter));

        // ---- M4 第二轮：主题切换 / 连珠可配 / 预置子 ----
        _autotestSteps.Enqueue(("m4b-theme-toggle", () =>
        {
            bool wasDark = _store.IsDarkTheme;
            _store.ToggleTheme();
            bool flipped = _store.IsDarkTheme != wasDark
                && Palette.Active == (wasDark ? Palette.Light : Palette.Dark);
            _store.ToggleTheme();
            return flipped && _store.IsDarkTheme == wasDark ? "ok" : "FAIL";
        }));
        _autotestSteps.Enqueue(("m4b-winlen4-start", () =>
        {
            _store.Pause();
            _store.ReturnToTitle();
            bool ok = _store.StartLevel(new LevelConfig
            {
                Id = "t4",
                BoardSize = 9,
                Layers = 1,
                WinLength = 4,
                Rules = RuleSet.Gomoku,
            }, false, AIDifficulty.Medium) && _store.State == GameState.Playing;
            return ok ? "ok" : "FAIL";
        }));
        _autotestSteps.Enqueue(("m4b-winlen4-win", () =>
        {
            (int x, int y)[] b = [(0, 0), (1, 0), (2, 0), (3, 0)];
            (int x, int y)[] w = [(0, 1), (1, 1), (2, 1)];
            for (int k = 0; k < 3; k++)
            {
                if (!_gm.TryPlace(b[k].x, b[k].y, 0) || !_gm.TryPlace(w[k].x, w[k].y, 0))
                {
                    return "FAIL place";
                }
            }
            return _gm.TryPlace(b[3].x, b[3].y, 0) && _gm.IsOver && _gm.Winner == 1
                && _store.State == GameState.GameOver ? "ok" : "FAIL";
        }));
        _autotestSteps.Enqueue(("m4b-initialmoves", () =>
        {
            _store.Pause();
            _store.ReturnToTitle();
            bool ok = _store.StartLevel(LevelCatalog.Residual, false, AIDifficulty.Medium)
                && _store.State == GameState.Playing
                && _gm.History.Count == 3
                && _gm.Board.MoveCount() == 3
                && _gm.Current == PlayerColor.White; // B W B → 轮白
            return ok ? "ok" : "FAIL";
        }));
        _autotestSteps.Enqueue(("m4b-level-dims-resync", () =>
        {
            // 严重 bug 回归：层数多于默认的关卡必须重配 3D 网格与相机（M4 二轮验收反馈）
            _store.Pause();
            _store.ReturnToTitle();
            bool ok = _store.StartLevel(new LevelConfig
            {
                Id = "deep",
                BoardSize = 11,
                Layers = 8, // > 默认 6：不重配则 3D 网格仍按 6 层构建、多余层不渲染
                WinLength = 5,
                Rules = RuleSet.Gomoku,
            }, false, AIDifficulty.Medium);
            ok = ok && _gm.Board.Layers == 8
                && _board3D.Layers == 8 && _board3D.BoardSize == 11
                && Math.Abs(_rig.Target.Center.Z - 7 * _spacing / 2) < 1e-6; // 相机中心=(layers-1)×spacing/2
            // 深层落子可达（渲染数据端验证）
            ok = ok && _gm.TryPlace(5, 5, 7) && _gm.Board.Get(5, 5, 7) == CellState.Black;
            _store.Pause();
            _store.ReturnToTitle();
            return ok ? "ok" : "FAIL";
        }));
        _autotestSteps.Enqueue(("m4b-nzsave-file-roundtrip", () =>
        {
            // .nzsave 文件往返：导出槽位 → 删除 → 文件导入（zip 容器直解）→ 落首空槽内容一致。
            // 自备目录+预清理——"首空槽=0" 断言不得依赖其他步骤的落槽残留（批11 教训同 m4-import）
            var iso = new GameStore(new FileSystemStorageAdapter("user://saves_autotest_nzsave"));
            iso.InitializeSaves();
            for (int i = 0; i < GameConfig.SaveSlotCount; i++)
            {
                iso.Saves.DeleteAsync(i).GetAwaiter().GetResult();
            }
            var data = _gm.BuildSaveData(false);
            iso.Saves.SaveAsync(1, data).GetAwaiter().GetResult();

            string path = "user://saves_autotest_nzsave/file_roundtrip.nzsave";
            iso.ExportSlotToFile(1, path);
            iso.Saves.DeleteAsync(1).GetAwaiter().GetResult();
            iso.ImportFile(path);
            var reloaded = iso.Saves.Load(0);
            bool ok = reloaded != null && reloaded.Id == data.Id && reloaded.Moves.Count == data.Moves.Count;

            iso.Saves.DeleteAsync(0).GetAwaiter().GetResult();
            DirAccess.RemoveAbsolute(path);
            return ok ? "ok" : "FAIL";
        }));
        _autotestSteps.Enqueue(("m4b-winlen-save-restore", () =>
        {
            // B14 回归：连珠 7 对局快存 → 读档，连珠数随外圈元数据恢复（v2.0.0 schema 无此字段）
            _store.Pause();
            _store.ReturnToTitle();
            bool ok = _store.StartLevel(new LevelConfig
            {
                Id = "wl7",
                BoardSize = 9,
                Layers = 1,
                WinLength = 7,
                Rules = RuleSet.Gomoku,
            }, false, AIDifficulty.Medium) && _gm.WinLength == 7;
            // 连珠 7 生效中：黑连 5 不胜
            (int x, int y)[] b = [(0, 0), (1, 0), (2, 0), (3, 0), (4, 0)];
            (int x, int y)[] w = [(0, 1), (1, 1), (2, 1), (3, 1), (4, 1)];
            for (int k = 0; k < 5; k++)
            {
                ok = ok && _gm.TryPlace(b[k].x, b[k].y, 0) && _gm.TryPlace(w[k].x, w[k].y, 0);
            }
            ok = ok && !_gm.IsOver;
            _store.QuickSave();
            ok = ok && _store.Saves.GetSlotWinLength(GameConfig.QuickSaveIndex) == 7;
            _store.Pause();
            ok = ok && _store.LoadSlot(GameConfig.QuickSaveIndex) && _store.State == GameState.Playing;
            // 恢复后连珠数仍为 7：黑连 6 不胜、连 7 才胜
            ok = ok && _gm.WinLength == 7
                && _gm.TryPlace(5, 0, 0) && !_gm.IsOver
                && _gm.TryPlace(5, 1, 0)
                && _gm.TryPlace(6, 0, 0) && _gm.IsOver && _gm.Winner == 1;
            _store.Pause();
            _store.ReturnToTitle();
            return ok ? "ok" : "FAIL";
        }));

        // ---- M4 收尾轮：04-contracts §6.4/§6.5 清单自动化证据 ----
        _autotestSteps.Enqueue(("m4c-overlay-contract", () =>
        {
            // §6.4 四项：确认回调恰好一次 / 取消不触发 onYes / 输入 trim 提交 / CloseAll 丢弃挂起回调
            int yes = 0, cancel = 0, submit = 0;
            string submitted = "";
            _overlay.ShowConfirmDialog("t", "m", () => yes++, () => cancel++);
            _overlay.ConfirmDialogForTests.EmitSignal("confirmed");
            bool confirmOnce = yes == 1 && cancel == 0 && !_overlay.IsModalOpen;
            _overlay.ShowConfirmDialog("t", "m", () => yes++, () => cancel++);
            _overlay.ConfirmDialogForTests.EmitSignal("canceled");
            bool cancelPath = yes == 1 && cancel == 1;
            _overlay.ShowInputDialog("t", "m", "p", s => { submit++; submitted = s; });
            _overlay.InputFieldForTests.Text = "  ab  ";
            _overlay.InputDialogForTests.EmitSignal("confirmed");
            bool trimOk = submit == 1 && submitted == "ab";
            _overlay.ShowInputDialog("t", "m", "p", s => submit++);
            _overlay.CloseAll();
            _overlay.InputDialogForTests.EmitSignal("confirmed");
            bool discardOk = submit == 1 && !_overlay.IsModalOpen;
            return confirmOnce && cancelPath && trimOk && discardOk
                ? "ok" : $"FAIL y={yes} c={cancel} s={submit} sub={submitted}";
        }));
        _autotestSteps.Enqueue(("m4c-modifier-quicksave", AutotestModifierQuickSave));
        _autotestSteps.Enqueue(("m4c-real-old-restore", AutotestRealOldSaveRestore));
        _autotestSteps.Enqueue(("m4c-save-alignment", AutotestSaveAlignment));
        _autotestSteps.Enqueue(("m4c-newgame-default-context", AutotestNewGameDefaultContext));
        _autotestSteps.Enqueue(("m4c-quick-load", AutotestQuickLoad));
        _autotestSteps.Enqueue(("m4c-replay-entry", AutotestReplayEntry));
        _autotestSteps.Enqueue(("m4c-replay-hud", AutotestReplayHud));
        _autotestSteps.Enqueue(("m4c-window-sequence", AutotestWindowSequence));
        _autotestSteps.Enqueue(("m4c-keymap-paging", AutotestKeymapPaging));
        _autotestSteps.Enqueue(("m5-audio-settings", AutotestAudioSettings));
        _autotestSteps.Enqueue(("m5-bgm-crossfade", AutotestBgmCrossfade));
        _autotestSteps.Enqueue(("m5-i18n", AutotestI18n));
        _autotestSteps.Enqueue(("m5-title-fit", AutotestTitleFit));
        _autotestSteps.Enqueue(("m5-settings-bugs", AutotestSettingsBugs));
        _autotestSteps.Enqueue(("m5-audio-hub", AutotestAudioHub));
        _autotestSteps.Enqueue(("m5-guide-author-note", AutotestGuideAuthorNote));
        _autotestSteps.Enqueue(("m5-save-integrity", AutotestSaveIntegrity));
        _autotestSteps.Enqueue(("m5-save-legacy-migration", AutotestSaveLegacyMigration));
        _autotestSteps.Enqueue(("m5-viewport-level-fit", AutotestViewportLevelFit));
        _autotestSteps.Enqueue(("m5-window-mode-pref", AutotestWindowModePref));
        _autotestSteps.Enqueue(("m5-overlay-scope", AutotestOverlayScope));
        _autotestSteps.Enqueue(("m5-keymap-keys", AutotestKeymapKeys));
        _autotestSteps.Enqueue(("m5-pretitle-redesign", AutotestPreTitleRedesign));
    }

    /// <summary>
    /// 开始界面重做回归锁（M5-pre 重做）：
    /// ① 切主题 / 切语言走 IKeepAliveRefreshable 原地刷新，不退回封面态且不清空演示盘落子；
    /// ② 「继续对局」可见性独立管理，不被 OnEntered() 覆盖；
    /// ③ 进对局隐藏后子视口自动置 Disabled 停渲染，回标题自动恢复 Always。
    /// </summary>
    private string AutotestPreTitleRedesign()
    {
        if (_store.State == GameState.Playing)
        {
            _store.Pause();
        }
        if (_store.State != GameState.Title)
        {
            _store.ReturnToTitle();
        }
        _titleScreen.SetVisualProgressForCheck(1f);
        bool placed = _titleScreen.PlaceDemoStoneForTests(0, 0, 0);
        int stonesBefore = _titleScreen.DemoStoneCountForTests;

        // 切主题（Dark ↔ Light）+ 切语言（en-US → zh-CN）：原地刷新不得退回 Letter 或清零落子
        _store.ToggleTheme();
        OnLanguageChanged("en-US");
        bool afterThemeLang = _titleScreen.PhaseForTests == 2
            && _titleScreen.DemoStoneCountForTests == stonesBefore;
        OnLanguageChanged("zh-CN");
        _store.ToggleTheme(); // 复原

        // 「继续对局」可见性不被 OnEntered() 覆盖
        _titleScreen.UpdateResumeButton(false);
        _titleScreen.OnEntered();
        bool resumeHiddenOk = !_titleScreen.ResumeVisibleForTests;
        _titleScreen.UpdateResumeButton(true);
        _titleScreen.OnEntered();
        bool resumeShownOk = _titleScreen.ResumeVisibleForTests;
        _titleScreen.UpdateResumeButton(_store.HasLiveGame);

        // 隐藏停渲染、显示恢复渲染（标题视口与对局视口双向互斥停跑）
        bool gamePausedOnTitle = _viewport3D.RenderTargetUpdateMode == SubViewport.UpdateMode.Disabled;
        _store.StartGame();
        bool pausedWhenHidden = _titleScreen.ViewportUpdateModeForTests == SubViewport.UpdateMode.Disabled;
        bool gameRunningInPlay = _viewport3D.RenderTargetUpdateMode == SubViewport.UpdateMode.Always;
        _store.Pause();
        _store.ReturnToTitle();
        bool resumedWhenShown = _titleScreen.ViewportUpdateModeForTests == SubViewport.UpdateMode.Always;

        return placed && afterThemeLang && resumeHiddenOk && resumeShownOk
            && gamePausedOnTitle && pausedWhenHidden && gameRunningInPlay && resumedWhenShown
            ? "ok"
            : $"FAIL placed={placed} keepState={afterThemeLang} resH={resumeHiddenOk} resS={resumeShownOk} " +
              $"gOff={gamePausedOnTitle} vpOff={pausedWhenHidden} gOn={gameRunningInPlay} vpOn={resumedWhenShown}";
    }

    /// <summary>
    /// 键位一致性审计（修复批 ③-6 延伸）：KeymapTable 的**展示键** ↔ InputMap 的**实际绑定**。
    /// 漂移即「帮助屏广告的键按了没反应」——此前这条只靠人工核对（审查报告确认当时零漂移）。
    /// </summary>
    private string AutotestKeymapKeys()
    {
        var issues = KeymapAudit.FindMismatches();
        return issues.Count == 0
            ? "ok"
            : $"FAIL {string.Join(" | ", issues.Take(6))}{(issues.Count > 6 ? $" (+{issues.Count - 6})" : "")}";
    }

    private int _ovScopePhase;
    private bool _ovScopeDarkBefore;

    /// <summary>
    /// 浮层作用域数据驱动回归（修复批 ③-5）：①KeymapTable 声明为浮层可用的 action 必须都有处理器
    /// （漂移检测：此前作用域硬编码三个 action 名，守卫 API 生产代码零引用）；
    /// ②浮层打开时 T 键（AllowWhenOverlay）应生效（主题翻转）；③Esc 逐层冒泡关闭浮层。
    /// </summary>
    private string AutotestOverlayScope()
    {
        switch (_ovScopePhase)
        {
            case 0:
            {
                var missing = OverlayActionsWithoutHandler();
                if (missing.Count > 0)
                {
                    return $"FAIL unhandled={string.Join(",", missing)}";
                }
                ToggleHelp();
                if (!_router.IsOverlayOpen("help"))
                {
                    return "FAIL help-not-open";
                }
                _ovScopeDarkBefore = _store.IsDarkTheme;
                _ovScopePhase = 1;
                return "wait";
            }
            case 1:
                Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = Key.T, Pressed = true, Echo = false });
                _ovScopePhase = 2;
                return "wait";
            case 2:
            {
                Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = Key.T, Pressed = false });
                if (_store.IsDarkTheme == _ovScopeDarkBefore)
                {
                    _ovScopePhase = 0;
                    _router.PopOverlay("help");
                    return "FAIL theme-not-toggled-in-overlay";
                }
                _store.ToggleTheme(); // 复原主题（不走输入路径，避免二次翻转）
                // Esc：ui_cancel 是引擎内置 action（只登记 keycode）→ 合成事件须同时给 keycode，
                // 否则 exactMatch 匹配不上（真实按键事件两者都有）
                Input.ParseInputEvent(new InputEventKey
                {
                    Keycode = Key.Escape,
                    PhysicalKeycode = Key.Escape,
                    Pressed = true,
                    Echo = false,
                });
                _ovScopePhase = 3;
                return "wait";
            }
            case 3:
            {
                Input.ParseInputEvent(new InputEventKey
                {
                    Keycode = Key.Escape,
                    PhysicalKeycode = Key.Escape,
                    Pressed = false,
                });
                bool closed = !_router.IsOverlayOpen("help");
                _ovScopePhase = 0;
                return closed ? "ok" : "FAIL esc-did-not-close-overlay";
            }
            default:
                _ovScopePhase = 0;
                return "FAIL";
        }
    }

    /// <summary>
    /// 最小化不得改写窗口模式偏好（修复批 ③-4）：最小化是窗口态不是模式选择，
    /// 此前兜底分支把它写成 windowed → 用户「上次会话是全屏」的偏好被一次最小化抹掉。
    /// headless 无窗口管理器，故直接验「模式→偏好值」映射与写回函数本身；测毕复原真实偏好。
    /// </summary>
    private string AutotestWindowModePref()
    {
        bool mappingOk =
            WindowModeToPref(DisplayServer.WindowMode.Windowed) == "windowed"
            && WindowModeToPref(DisplayServer.WindowMode.Minimized) == null
            && WindowModeToPref(DisplayServer.WindowMode.Maximized) == "maximized"
            && WindowModeToPref(DisplayServer.WindowMode.Fullscreen) == "fullscreen"
            && WindowModeToPref(DisplayServer.WindowMode.ExclusiveFullscreen) == "fullscreen";

        string before = SettingsStore.Get("general", "window_mode", "windowed").AsString();
        SettingsStore.Set("general", "window_mode", "fullscreen");
        WriteWindowModePref(DisplayServer.WindowMode.Minimized);
        bool kept = SettingsStore.Get("general", "window_mode", "").AsString() == "fullscreen";
        WriteWindowModePref(DisplayServer.WindowMode.Maximized);
        bool wrote = SettingsStore.Get("general", "window_mode", "").AsString() == "maximized";
        SettingsStore.Set("general", "window_mode", before); // 复原真实偏好（不污染用户设置）

        return mappingOk && kept && wrote
            ? "ok"
            : $"FAIL mapping={mappingOk} kept={kept} wrote={wrote}";
    }

    private int _vpFitPhase;

    /// <summary>
    /// 视口变化跟随**当前关卡**（修复批 ③-3）：此前 <c>HandleViewportChange</c> 用命令行默认尺寸
    /// （13×6）重算相机 → 非默认关卡后任何窗口缩放都跳回默认构图。
    /// 断言三件事：①相机的盘尺寸=当前关卡尺寸；②距离=该关卡在窄视口下的**公式值**；
    /// ③反证——若误用命令行默认尺寸，距离会是另一个值（否则此锁测不出回归）。
    /// </summary>
    private string AutotestViewportLevelFit()
    {
        switch (_vpFitPhase)
        {
            case 0:
                if (_store.State != GameState.Title)
                {
                    _store.Pause();
                    _store.ReturnToTitle();
                    return "wait";
                }
                _spacing = 3.0; // 非默认层间距（与命令行默认 3.5 区分开）
                _store.StartLevel(new LevelConfig
                {
                    Id = "vpfit",
                    BoardSize = 9,
                    Layers = 3,
                    Rules = RuleSet.Gomoku,
                }, false, AIDifficulty.Medium);
                _vpFitPhase = 1;
                return "wait";
            case 1:
            {
                SyncCameraToViewport(new Vector2(400f, 800f)); // 窄高视口（宽高比 0.5）
                double expected = CameraMath.ComputeDistance(
                    CameraMath.ComputeTargetDiagonal(CameraMath.ComputeDiagonal(9, 3, _spacing)),
                    CameraMath.ComputeFov(CameraMath.ComputeNormalizedAspect(9, 3, _spacing)),
                    0.5);
                double cmdlineDefault = CameraMath.ComputeDistance(
                    CameraMath.ComputeTargetDiagonal(CameraMath.ComputeDiagonal(13, 6, _spacing)),
                    CameraMath.ComputeFov(CameraMath.ComputeNormalizedAspect(13, 6, _spacing)),
                    0.5);
                bool sizeOk = _rig.Target.BoardSize == 9 && _rig.Target.Layers == 3;
                bool distOk = Math.Abs(_rig.Target.Distance - expected) < 1e-9;
                bool distinguishable = Math.Abs(expected - cmdlineDefault) > 1e-6;
                _vpFitPhase = 2;
                return sizeOk && distOk && distinguishable
                    ? "ok"
                    : $"FAIL sizeOk={sizeOk} distOk={distOk} dist={_rig.Target.Distance:0.000} " +
                      $"exp={expected:0.000} cmdline={cmdlineDefault:0.000}";
            }
            default:
                // 复原：默认关卡 + 默认层间距 + 回标题（后续步骤与人工复验都依赖默认上下文）
                _spacing = GameConfig.DefaultLayerSpacing;
                _store.StartLevel(new LevelConfig
                {
                    Id = "cmdline",
                    BoardSize = GameConfig.DefaultBoardSize,
                    Layers = GameConfig.DefaultLayerCount,
                    Rules = RuleSet.Gomoku,
                }, false, AIDifficulty.Medium);
                _store.Pause();
                _store.ReturnToTitle();
                _vpFitPhase = 0;
                return "ok";
        }
    }

    /// <summary>
    /// 标题屏布局容量锁（修复批 ③-7：守卫对象错位——旧锁测「内容最小高 vs 416」，
    /// 而布局是硬编码 432 高，真正会出界的类别（菜单列矩形越出逻辑面）根本没测到）。
    /// <para>
    /// 现改为测<b>实际布局矩形</b>：①内容最小高 ≤ 最小逻辑面高（原断言保留）；
    /// ②菜单列在最小逻辑面（320×416 = 窗口 MinSize 800×1040 ÷ 内容缩放 2.5）内完整可见；
    /// ③设计逻辑面（768×432）下保持设计几何（观感零变化）。
    /// </para>
    /// </summary>
    private string AutotestTitleFit()
    {
        _titleScreen.UpdateResumeButton(true);
        float minH = _titleScreen.GetCombinedMinimumSize().Y;
        _titleScreen.UpdateResumeButton(_store.HasLiveGame);
        if (minH > 416f)
        {
            return $"FAIL title-min-height={minH:0.0} > 416";
        }

        var minFace = new Vector2(320f, 360f);
        Rect2 minRect = _titleScreen.ApplyMenuLayoutForTests(minFace);
        bool minInside = new Rect2(Vector2.Zero, minFace).Encloses(minRect);

        var designFace = new Vector2(960f, 540f);
        Rect2 designRect = _titleScreen.ApplyMenuLayoutForTests(designFace);
        bool designInside = new Rect2(Vector2.Zero, designFace).Encloses(designRect);
        bool designOk = designInside
            && Mathf.IsEqualApprox(designRect.Size.X, 248f)
            && designRect.Size.Y > 200f && designRect.Size.Y <= 380f;

        var scaledFace = new Vector2(768f, 432f);
        Rect2 scaledRect = _titleScreen.ApplyMenuLayoutForTests(scaledFace);
        bool scaledInside = new Rect2(Vector2.Zero, scaledFace).Encloses(scaledRect);

        _titleScreen.ApplyMenuLayoutForTests(_titleScreen.Size); // 复原真实布局
        return minInside && designOk && scaledInside
            ? "ok"
            : $"FAIL min-rect={minRect} inside={minInside} design-rect={designRect} designOk={designOk} scaled={scaledRect}";
    }

    /// <summary>
    /// 存档数据完整性回归（修复批 ②，2026-10-27）：
    /// ①<b>槽位号主键</b>——「复制→填写」后原槽不得从索引/列表消失（旧契约以档 Id 为主键，原槽直接消失）；
    /// ②<b>规则白名单</b>——renju 档（引擎未实装）导入必须被拒，不进槽位；
    /// ③<b>垃圾 JSON</b>——无 version 的垃圾不得被迁成「最新空档」。
    /// 走隔离目录（与真实存档隔离），并直接核对磁盘 index.json（内存态断言会掩盖索引塌缩）。
    /// </summary>
    private string AutotestSaveIntegrity()
    {
        const string dir = "user://saves_autotest_integrity";
        var iso = new GameStore(new FileSystemStorageAdapter(dir));
        iso.InitializeSaves();
        for (int i = 0; i < GameConfig.SaveSlotCount; i++)
        {
            iso.Saves.DeleteAsync(i).GetAwaiter().GetResult();
        }

        // ① 复制→填写：槽 0 的档写入槽 1（原槽必须仍在）
        var data = _gm.BuildSaveData(false);
        iso.Saves.SaveAsync(0, data).GetAwaiter().GetResult();
        string b64 = iso.ExportSlot(0);
        iso.FillSlot(1, b64);
        bool bothSlots = iso.Saves.Load(0) != null && iso.Saves.Load(1) != null;
        bool sameId = iso.Saves.Load(0)?.Id == iso.Saves.Load(1)?.Id && iso.Saves.Load(0)?.Id == data.Id;

        // 索引核对：两条槽位记录（磁盘层才算数——内存态断言掩盖索引塌缩）
        int indexEntries = 0;
        using (var file = Godot.FileAccess.Open($"{dir}/index.json", Godot.FileAccess.ModeFlags.Read))
        {
            if (file != null)
            {
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(file.GetAsText());
                    indexEntries = doc.RootElement.GetArrayLength();
                }
                catch (System.Text.Json.JsonException)
                {
                    indexEntries = -1;
                }
            }
        }

        // ② renju 档（引擎未实装）导入必须被拒
        const string renjuJson = """
            {"id":"renju-probe","version":"2.0.0","timestamp":1,"boardSize":5,"layers":1,
             "rules":"renju","gameMode":"pvp","status":"playing","winner":0,"currentPlayer":1,
             "isDarkTheme":false,"playerColor":1,"layerSpacing":3.5,"moves":[],
             "boardState":[[[0,0,0,0,0],[0,0,0,0,0],[0,0,0,0,0],[0,0,0,0,0],[0,0,0,0,0]]]}
            """;
        iso.FillSlot(2, renjuJson);
        bool renjuRejected = iso.Saves.Load(2) == null;

        // ③ 垃圾 JSON 不得迁成空档
        iso.FillSlot(3, """{"foo":"bar"}""");
        bool garbageRejected = iso.Saves.Load(3) == null;

        for (int i = 0; i < GameConfig.SaveSlotCount; i++)
        {
            iso.Saves.DeleteAsync(i).GetAwaiter().GetResult();
        }
        return bothSlots && sameId && indexEntries == 2 && renjuRejected && garbageRejected
            ? "ok"
            : $"FAIL slots={bothSlots} id={sameId} index={indexEntries} renju={renjuRejected} garbage={garbageRejected}";
    }

    /// <summary>
    /// 旧盘布局迁移回归（修复批 ②-1，2026-10-27）：0.1.0-alpha 的存档布局是
    /// <c>index.json</c> + <c>{档 Id}.sav</c>；新布局改为槽位号主键的 <c>slot{n}.sav</c>。
    /// 升级后首次启动必须把旧 blob 搬到新名并删旧件，<b>旧存档不得丢</b>。
    /// </summary>
    private string AutotestSaveLegacyMigration()
    {
        const string dir = "user://saves_autotest_legacy";
        const string legacyId = "legacy-id-1";
        foreach (string name in new[] { "index.json", $"{legacyId}.sav", "slot0.sav" })
        {
            DirAccess.RemoveAbsolute($"{dir}/{name}");
        }
        DirAccess.MakeDirRecursiveAbsolute(dir);

        // 造一份旧布局：index.json + {档 Id}.sav
        var legacy = _gm.BuildSaveData(false);
        legacy.Id = legacyId;
        using (var file = Godot.FileAccess.Open($"{dir}/{legacyId}.sav", Godot.FileAccess.ModeFlags.Write))
        {
            file?.StoreString(SaveData.Serialize(legacy));
        }
        using (var file = Godot.FileAccess.Open($"{dir}/index.json", Godot.FileAccess.ModeFlags.Write))
        {
            file?.StoreString(
                $$"""[{"index":0,"id":"{{legacyId}}","timestamp":{{legacy.Timestamp}},"boardSize":{{legacy.BoardSize}},"layers":{{legacy.Layers}},"movesCount":{{legacy.Moves.Count}}}]""");
        }

        var iso = new GameStore(new FileSystemStorageAdapter(dir));
        iso.InitializeSaves();
        var loaded = iso.Saves.Load(0);
        bool migrated = loaded != null && loaded.Id == legacyId;
        bool newBlob = Godot.FileAccess.FileExists($"{dir}/slot0.sav");
        bool legacyGone = !Godot.FileAccess.FileExists($"{dir}/{legacyId}.sav");

        iso.Saves.DeleteAsync(0).GetAwaiter().GetResult();
        DirAccess.RemoveAbsolute($"{dir}/index.json");
        DirAccess.RemoveAbsolute($"{dir}/slot0.sav");
        return migrated && newBlob && legacyGone
            ? "ok"
            : $"FAIL migrated={migrated} newBlob={newBlob} legacyGone={legacyGone}";
    }

    /// <summary>
    /// M5 i18n 端到端（2026-09-26）：CSV→Translation→I18n.T 全链路——切 en_US 后
    /// Text 常量（zh 键）应查出英文译文、带参模板正确格式化、切回 zh-CN 回落原文。幂等无副作用。</summary>
    private string AutotestI18n()
    {
        TranslationServer.SetLocale("en_US");
        string start = I18n.T(Text.Title.Start);
        string slot = I18n.T(Text.Save.SlotLabel, 3);
        string fallback = I18n.T("不在翻译表中的键");
        TranslationServer.SetLocale("zh_CN");
        string restored = I18n.T(Text.Title.Start);
        return start == "New Game" && slot == "Slot 3" && fallback == "不在翻译表中的键"
            && restored == "开始游戏"
            ? "ok"
            : $"FAIL start={start} slot={slot} fallback={fallback} restored={restored}";
    }

    private int _setBugPhase;
    private string _setBugLangBefore = "";
    private string _setBugLocaleBefore = "";
    private float _setBugScaleBefore = 1f;

    private OptionButton? SettingsLanguageSelect() =>
        _settingsScreen.FindChild("LanguageSelect", true, false) as OptionButton;

    private OptionButton? SettingsModeSelect() =>
        _settingsScreen.FindChild("WindowModeSelect", true, false) as OptionButton;

    private OptionButton? SettingsResolutionSelect() =>
        _settingsScreen.FindChild("ResolutionSelect", true, false) as OptionButton;

    private OptionButton? SettingsScaleSelect() =>
        _settingsScreen.FindChild("UiScaleSelect", true, false) as OptionButton;

    private Label? SettingsTitleLabel() =>
        _settingsScreen.FindChildren("*", "Label", true, false).OfType<Label>()
            .FirstOrDefault(l => l.Text == I18n.T(Text.Settings.Title));

    /// <summary>M5 设置屏回归锁（显示/分辨率/UI 倍率/语言热刷新 + 音频项已迁至 AudioHubScreen）：
    /// ① 窗口模式/分辨率/UI 倍率下拉均 ExpandFill 且可写回偏好；② 语言切换 en↔zh 全链路含"改不回"锁；
    /// ③ 直达「音乐与音效」入口按钮存在。测试前后快照/恢复 general.lang 与 general.ui_scale。</summary>
    private string AutotestSettingsBugs()
    {
        switch (_setBugPhase)
        {
            case 0:
                _setBugLangBefore = SettingsStore.Get("general", "lang", "").AsString();
                _setBugLocaleBefore = TranslationServer.GetLocale();
                _setBugScaleBefore = (float)SettingsStore.Get("general", "ui_scale", 1f).AsSingle();
                OpenSettings();
                _setBugPhase = 1;
                return "wait";
            case 1:
                if (_settingsScreen == null || !IsInstanceValid(_settingsScreen))
                {
                    return "FAIL no-settings";
                }
                OptionButton? res = SettingsResolutionSelect();
                OptionButton? scale = SettingsScaleSelect();
                Button? audioLink = _settingsScreen.FindChild("OpenAudioHubButton", true, false) as Button;
                if (res == null || res.ItemCount != UiScaleMath.ResolutionPresets.Length
                    || scale == null || scale.ItemCount != UiScaleMath.MultiplierPresets.Length
                    || audioLink == null)
                {
                    return $"FAIL display-controls res={res?.ItemCount} scale={scale?.ItemCount} audioLink={audioLink != null}";
                }
                scale.EmitSignal("item_selected", 2); // 1.25x
                if (MathF.Abs((float)SettingsStore.Get("general", "ui_scale", 0f).AsSingle() - 1.25f) > 0.01f)
                {
                    return "FAIL ui-scale-not-persisted";
                }
                SettingsStore.Set("general", "ui_scale", _setBugScaleBefore);
                ApplyUiScale(_setBugScaleBefore);

                OptionButton? lang = SettingsLanguageSelect();
                if (lang == null || lang.GetSelected() < 0)
                {
                    return "FAIL language-initial-selection";
                }
                lang.EmitSignal("item_selected", 2); // en-US
                _setBugPhase = 2;
                return "wait";
            case 2:
                if (TranslationServer.GetLocale() != "en_US")
                {
                    return $"FAIL en-locale={TranslationServer.GetLocale()}";
                }
                OptionButton? langEn = SettingsLanguageSelect();
                Label? titleEn = SettingsTitleLabel();
                if (langEn == null || langEn.GetSelected() != 2 || langEn.Text != "English")
                {
                    return $"FAIL en-select sel={langEn?.GetSelected()} face={langEn?.Text}";
                }
                if (titleEn == null || titleEn.Text != "Settings")
                {
                    return $"FAIL en-title={titleEn?.Text ?? "null"}";
                }
                langEn.EmitSignal("item_selected", 0); // zh-CN——"改不回"锁
                _setBugPhase = 3;
                return "wait";
            case 3:
                if (TranslationServer.GetLocale() != "zh_CN")
                {
                    return $"FAIL zh-locale={TranslationServer.GetLocale()}";
                }
                OptionButton? langZh = SettingsLanguageSelect();
                Label? titleZh = SettingsTitleLabel();
                if (langZh == null || langZh.GetSelected() != 0 || langZh.Text != "简体中文")
                {
                    return $"FAIL zh-select sel={langZh?.GetSelected()} face={langZh?.Text}";
                }
                if (titleZh == null || titleZh.Text != "设置")
                {
                    return $"FAIL zh-title={titleZh?.Text ?? "null"}";
                }
                OptionButton? mode = SettingsModeSelect();
                string modeCfg = SettingsStore.Get("general", "window_mode", "windowed").AsString();
                int modeIdx = Array.IndexOf(new[] { "windowed", "maximized", "fullscreen" }, modeCfg);
                if (mode == null || mode.GetSelected() != modeIdx)
                {
                    return $"FAIL mode-initial sel={mode?.GetSelected()} cfg={modeCfg}";
                }
                SettingsStore.Set("general", "lang", _setBugLangBefore);
                _router.PopOverlay("settings");
                _setBugPhase = 4;
                return "wait";
            case 4:
                if (_router.IsOverlayOpen("settings"))
                {
                    return "wait";
                }
                TranslationServer.SetLocale(_setBugLocaleBefore);
                _setBugPhase = 0;
                return "ok";
            default:
                _setBugPhase = 0;
                return "FAIL";
        }
    }

    private int _hubPhase;
    private (bool Master, bool Sfx, bool Bgm) _hubMuteBefore;

    /// <summary>音乐与音效中心（AudioHubScreen）回归锁：
    /// ① 合并后的两个音量滑条 ExpandFill 不塌缩；② 音效/音乐独立开关正交且与 M 键全局静音联动；
    /// ③ 5 轨 BGM 曲目卡齐全、点击直选试听切换成功且 Seek 不抛。</summary>
    private string AutotestAudioHub()
    {
        switch (_hubPhase)
        {
            case 0:
                _hubMuteBefore = (_audio.Muted, _audio.SfxMuted, _audio.BgmMuted);
                OpenAudioHub();
                _hubPhase = 1;
                return "wait";
            case 1:
                if (_audioHubScreen == null || !IsInstanceValid(_audioHubScreen))
                {
                    return "FAIL no-audio-hub";
                }
                var sfxSlider = _audioHubScreen.FindChild("SfxVolumeSlider", true, false) as HSlider;
                var bgmSlider = _audioHubScreen.FindChild("BgmVolumeSlider", true, false) as HSlider;
                if (sfxSlider == null || bgmSlider == null
                    || sfxSlider.SizeFlagsHorizontal != Godot.Control.SizeFlags.ExpandFill
                    || bgmSlider.SizeFlagsHorizontal != Godot.Control.SizeFlags.ExpandFill
                    || sfxSlider.Size.X < 60f || bgmSlider.Size.X < 60f)
                {
                    return $"FAIL sliders sfx={sfxSlider?.Size.X:0} bgm={bgmSlider?.Size.X:0}";
                }
                var sfxBtn = _audioHubScreen.FindChild("SfxMuteButton", true, false) as Button;
                var bgmBtn = _audioHubScreen.FindChild("BgmMuteButton", true, false) as Button;
                var masterBtn = _audioHubScreen.FindChild("MasterMuteButton", true, false) as Button;
                if (sfxBtn == null || bgmBtn == null || masterBtn == null)
                {
                    return "FAIL mute-buttons-missing";
                }
                // 独立开关正交验证
                _audio.ToggleSfxMute();
                bool sfxOnly = _audio.SfxMuted != _hubMuteBefore.Sfx
                               && _audio.BgmMuted == _hubMuteBefore.Bgm
                               && _audio.Muted == _hubMuteBefore.Master
                               && sfxBtn.Text == (_audio.SfxMuted ? I18n.T(Text.Audio.SfxOff) : I18n.T(Text.Audio.SfxOn));
                _audio.SetSfxMuted(_hubMuteBefore.Sfx);

                _audio.ToggleBgmMute();
                bool bgmOnly = _audio.BgmMuted != _hubMuteBefore.Bgm
                               && _audio.SfxMuted == _hubMuteBefore.Sfx
                               && _audio.Muted == _hubMuteBefore.Master
                               && bgmBtn.Text == (_audio.BgmMuted ? I18n.T(Text.Audio.BgmOff) : I18n.T(Text.Audio.BgmOn));
                _audio.SetBgmMuted(_hubMuteBefore.Bgm);

                // M 键全局静音联动 MasterMuteButton
                string masterBefore = masterBtn.Text;
                SimulateActionPress("toggle_mute");
                bool masterSynced = masterBtn.Text != masterBefore && _audio.Muted != _hubMuteBefore.Master;
                if (_audio.Muted != _hubMuteBefore.Master)
                {
                    _audio.ToggleMute();
                }

                // 5 轨曲目卡存在 + 点击直选 bgm_game_2 试听
                foreach (var meta in AudioPolicy.BgmCatalog)
                {
                    if (_audioHubScreen.FindChild("TrackCard_" + meta.TrackId, true, false) is not Button)
                    {
                        return $"FAIL missing-card-{meta.TrackId}";
                    }
                }
                var card2 = (Button)_audioHubScreen.FindChild("TrackCard_bgm_game_2", true, false);
                card2.EmitSignal("pressed");
                _audio.SeekBgm(5.0);
                var status = _audio.GetBgmStatus();
                bool previewOk = status.IsPlaying && status.TrackId == "bgm_game_2";

                _router.PopOverlay("audio-hub");
                _hubPhase = 2;
                return sfxOnly && bgmOnly && masterSynced && previewOk
                    ? "wait"
                    : $"FAIL sfxOnly={sfxOnly} bgmOnly={bgmOnly} master={masterSynced} preview={previewOk} track={status.TrackId}";
            case 2:
                if (_router.IsOverlayOpen("audio-hub"))
                {
                    return "wait";
                }
                _audio.StopBgm();
                _hubPhase = 0;
                return "ok";
            default:
                _hubPhase = 0;
                return "FAIL";
        }
    }

    private int _authorPhase;

    /// <summary>引导屏「作者的话」默认折叠回归锁：进入引导默认关闭不出现，点击按钮展开后切至阅读卡片，再点返回按键表。</summary>
    private string AutotestGuideAuthorNote()
    {
        switch (_authorPhase)
        {
            case 0:
                if (_store.State != GameState.Title)
                {
                    _store.Pause();
                    _store.ReturnToTitle();
                    return "wait";
                }
                _store.OpenGuide();
                _authorPhase = 1;
                return "wait";
            case 1:
                if (_guideScreen == null || !IsInstanceValid(_guideScreen))
                {
                    return "FAIL no-guide";
                }
                var toggle = _guideScreen.FindChild("AuthorNoteToggle", true, false) as Button;
                var notePanel = _guideScreen.FindChild("AuthorNotePanel", true, false) as Control;
                if (toggle == null || notePanel == null)
                {
                    return "FAIL missing-author-controls";
                }
                bool defaultCollapsed = !_guideScreen.IsAuthorNoteOpen && _guideScreen.Content.Visible && !notePanel.Visible;
                toggle.EmitSignal("pressed");
                bool opened = _guideScreen.IsAuthorNoteOpen && !_guideScreen.Content.Visible && notePanel.Visible;
                toggle.EmitSignal("pressed");
                bool closedAgain = !_guideScreen.IsAuthorNoteOpen && _guideScreen.Content.Visible && !notePanel.Visible;
                _store.GuideBack();
                _authorPhase = 0;
                return defaultCollapsed && opened && closedAgain
                    ? "ok"
                    : $"FAIL default={defaultCollapsed} opened={opened} closed={closedAgain}";
            default:
                _authorPhase = 0;
                return "FAIL";
        }
    }

    private int _kmPhase;

    /// <summary>快捷键页 ←/→ 翻层回归（NRE 事故 2026-09-26：引导屏未挂载时 Content 解引用）：
    /// 开页合成 ← 键，须翻到末页（循环）且无异常；引导屏路径同函数。</summary>
    private string AutotestKeymapPaging()
    {
        switch (_kmPhase)
        {
            case 0:
                if (_store.State != GameState.Title)
                {
                    _store.Pause();
                    _store.ReturnToTitle();
                    return "wait";
                }
                ToggleHelp();
                Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = Key.Left, Pressed = true, Echo = false });
                _kmPhase = 1;
                return "wait";
            case 1:
                Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = Key.Left, Pressed = false });
                // PageStep(-1) 自第 1 页循环到第 6 页（全局层）；页状态由 KeymapContent 自持
                bool ok = _keymapScreen.Content != null && _router.IsOverlayOpen("help");
                ToggleHelp();
                _kmPhase = 0;
                return ok ? "ok" : "FAIL help-not-open";
            default:
                _kmPhase = 0;
                return "FAIL";
        }
    }

    private bool _audioPhase;
    private (float Sfx, float Bgm, bool Muted) _audioSnapshot;

    /// <summary>M5 音频+设置屏回归（2026-09-26）： AudioManager 装配、四轨 BGM/四件 SFX 播放调用
    /// （headless Dummy 驱动不抛）、音量钳制与静音翻转（M 键动作路径）、设置浮层开关。测试前后
    /// 快照/恢复 settings.cfg 的 audio 节——不污染真实偏好。</summary>
    private string AutotestAudioSettings()
    {
        if (!_audioPhase)
        {
            _audioPhase = true;
            _audioSnapshot = (_audio.SfxVolume, _audio.BgmVolume, _audio.Muted);
            if (AudioManager.Instance == null)
            {
                return "FAIL no-audio-manager";
            }
            // 播放调用冒烟：占位资产加载失败会抛（GD.Load null → player.Play 空转但不抛；资产缺失本身在 _Ready 抛）
            foreach (BgmTrack track in Enum.GetValues<BgmTrack>())
            {
                _audio.PlayBgm(track);
            }
            foreach (SfxKind kind in Enum.GetValues<SfxKind>())
            {
                _audio.PlaySfx(kind);
            }
            _audio.SetVolumes(1.5f, -1f); // 钳制语义（源 setVolume 0–1）
            if (_audio.SfxVolume != 1f || _audio.BgmVolume != 0f)
            {
                return "FAIL clamp";
            }
            _audio.SetVolumes(_audioSnapshot.Sfx, _audioSnapshot.Bgm);
            _audio.ToggleMute();
            if (!_audio.Muted)
            {
                return "FAIL mute-on";
            }
            _audio.ToggleMute();
            if (_audio.Muted != _audioSnapshot.Muted)
            {
                return "FAIL mute-restore";
            }
            // M 键动作路径（SingleActions → ToggleMute → toast）与设置浮层
            SimulateActionPress("toggle_mute");
            if (_audio.Muted == _audioSnapshot.Muted)
            {
                return "FAIL mute-action";
            }
            _audio.ToggleMute(); // 恢复
            OpenSettings();
            // 须返回 wait 让第二帧清理入队：自测框架对非 wait 结果立即出队——
            // 此前返回 ok 使"关浮层"分支成为死代码，设置浮层泄漏开放到后续全部步骤
            // （2026-10-01 被 m5-settings-bugs 暴露：旧实例不重建，新步骤拿到陈旧文本）
            return _router.IsOverlayOpen("settings") ? "wait" : "FAIL no-settings";
        }
        // 第二帧：关浮层 + 最终恢复
        _router.PopOverlay("settings");
        if (_router.IsOverlayOpen("settings"))
        {
            return "wait";
        }
        _audio.SetVolumes(_audioSnapshot.Sfx, _audioSnapshot.Bgm);
        if (_audio.Muted != _audioSnapshot.Muted)
        {
            _audio.ToggleMute();
        }
        _audioPhase = false;
        return "ok";
    }

    private int _bgmPhase;
    private ulong _bgmT0;
    private bool _bgmTimedOut;

    private bool BgmWaited(double ms) => Time.GetTicksMsec() - _bgmT0 >= (ulong)ms;

    /// <summary>
    /// 条件等待（修复批 ③ 顺带：交叉淡化回归锁去「时刻采样」化）——条件成立即 true；
    /// 未成立则按墙钟上限区分「继续等」与「超时失败」（<see cref="_bgmTimedOut"/>）。
    /// <para>
    /// 此前各检查点是「固定延时 → 瞬时采样淡化中段」：进程卡顿（GC / 杀软扫描新构建产物）
    /// 会让采样点整个跳过淡变窗口 → 间歇性红（本次实测 8 轮 1 次红在 p4：
    /// 淡出已走到 0.13 而断言要求 ≥0.15）。改为**逐帧条件等待**后，卡顿只影响「走到哪一步」，
    /// 不再影响「确实在渐变」这一语义断言。
    /// </para>
    /// </summary>
    private bool BgmWait(Func<bool> cond, double timeoutMs = 2500)
    {
        if (cond())
        {
            return true;
        }
        _bgmTimedOut = BgmWaited(timeoutMs);
        return false;
    }

    private static string BgmSnap(SampleMusicEngine.LaneSnapshot[] s) =>
        string.Join("|", s.Select(l => $"{l.Slot}:{l.LinVolume:0.00}->{l.TargetVolume:0.00}{(l.Playing ? "" : "停")}"));

    /// <summary>M5 BGM 平滑切换回归锁（2026-10-02，headless 可跑）：双播放器交叉淡化（0.5s 出/0.6s 入，
    /// 源 playBGM 语义 B20 ②/B21）——①换轨=新轨淡入旧轨淡出并行、旧轨淡完即停 ②快速 A→B→A 走
    /// 优雅回归（切回仍在淡出的轨=音量从中间值回升，不重启）③同槽在播守卫 no-op（B20 ②）
    /// ④缺文件槽位容忍（pretitle 预留槽不炸不打断）+ 曲库按槽归池 + 槽位覆盖 off 淡出收尾
    /// （cfg 快照/恢复）。只断言轨位音量轨迹，不依赖真实音频输出（headless Dummy 驱动可用）。</summary>
    private string AutotestBgmCrossfade()
    {
        var eng = _audio.MusicForTests;
        if (eng == null)
        {
            return "FAIL no-engine";
        }
        switch (_bgmPhase)
        {
            case 0:
                _bgmT0 = Time.GetTicksMsec();
                _audio.PlayBgm(BgmTrack.Game);
                _bgmPhase = 1;
                return "wait";
            case 1: // game 独占在播且已升满（条件等待：升满且只剩一轨即走，不取固定时刻）
                if (!BgmWait(() =>
                {
                    var s = eng.LanesForTests;
                    var g = s.FirstOrDefault(l => l.Slot == "game");
                    return g is { Playing: true } && g.LinVolume >= 0.9f && s.Count(l => l.Playing) == 1;
                }))
                {
                    return _bgmTimedOut ? $"FAIL p1 {BgmSnap(eng.LanesForTests)}" : "wait";
                }
                _bgmT0 = Time.GetTicksMsec();
                _audio.PlayBgm(BgmTrack.Menu);
                _bgmPhase = 2;
                return "wait";
            case 2: // menu 升满在播，game 淡完已停
                if (!BgmWait(() =>
                {
                    var m = eng.LanesForTests.FirstOrDefault(l => l.Slot == "menu");
                    return m is { Playing: true } && m.LinVolume >= 0.9f
                        && !eng.LanesForTests.Any(l => l.Slot == "game" && l.Playing);
                }))
                {
                    return _bgmTimedOut ? $"FAIL p2 {BgmSnap(eng.LanesForTests)}" : "wait";
                }
                _bgmT0 = Time.GetTicksMsec();
                _audio.PlayBgm(BgmTrack.Game);
                _bgmPhase = 3;
                return "wait";
            case 3: // game 淡入**中段**（逐帧观察：必须经过中间音量 → 渐变而非硬切）
                if (!BgmWait(() =>
                {
                    var g = eng.LanesForTests.FirstOrDefault(l => l.Slot == "game");
                    return g is { Playing: true } && g.LinVolume is > 0.05f and < 0.95f;
                }))
                {
                    return _bgmTimedOut ? $"FAIL p3 {BgmSnap(eng.LanesForTests)}" : "wait";
                }
                _bgmT0 = Time.GetTicksMsec();
                _audio.PlayBgm(BgmTrack.Menu);
                _bgmPhase = 4;
                return "wait";
            case 4: // game 淡出**已启动且仍在播**（优雅回归前置态：切回时它还在淡出中）
                if (!BgmWait(() =>
                {
                    var g = eng.LanesForTests.FirstOrDefault(l => l.Slot == "game");
                    return g is { Playing: true, TargetVolume: 0f } && g.LinVolume < 0.95f;
                }))
                {
                    return _bgmTimedOut ? $"FAIL p4 {BgmSnap(eng.LanesForTests)}" : "wait";
                }
                _bgmT0 = Time.GetTicksMsec();
                _audio.PlayBgm(BgmTrack.Game); // A→B→A 快速切回
                _bgmPhase = 5;
                return "wait";
            case 5: // game 优雅回归升满（原轨位继续），menu 淡完；随后守卫 no-op 校验
                if (!BgmWait(() =>
                {
                    var g = eng.LanesForTests.FirstOrDefault(l => l.Slot == "game");
                    return g is { Playing: true } && g.LinVolume >= 0.9f
                        && !eng.LanesForTests.Any(l => l.Slot == "menu" && l.Playing);
                }))
                {
                    return _bgmTimedOut ? $"FAIL p5 {BgmSnap(eng.LanesForTests)}" : "wait";
                }
                var s5 = eng.LanesForTests;
                _bgmT0 = Time.GetTicksMsec();
                var before = s5.Select(l => (l.Slot, l.TargetVolume)).ToArray();
                _audio.PlayBgm(BgmTrack.Game);
                var after = eng.LanesForTests.Select(l => (l.Slot, l.TargetVolume)).ToArray();
                if (eng.CurrentSlotForTests != "game" || !before.SequenceEqual(after))
                {
                    return $"FAIL p5-guard {BgmSnap(eng.LanesForTests)}";
                }
                _bgmPhase = 6;
                return "wait";
            case 6: // 守卫后仍在播；随后 P2 扩展：缺槽容忍 + 曲库池 + 槽位覆盖 off
                if (!BgmWait(() =>
                {
                    var g = eng.LanesForTests.FirstOrDefault(l => l.Slot == "game");
                    return g is { Playing: true } && g.LinVolume >= 0.9f;
                }))
                {
                    return _bgmTimedOut ? $"FAIL p6 {BgmSnap(eng.LanesForTests)}" : "wait";
                }
                // ① 缺文件槽位：调用不炸、不打断当前播放
                var pre6 = eng.LanesForTests.Select(l => (l.Slot, l.TrackId, l.TargetVolume)).ToArray();
                _audio.PlayBgm("pretitle"); // 预开始页预留槽位，文件未交付
                var post6 = eng.LanesForTests.Select(l => (l.Slot, l.TrackId, l.TargetVolume)).ToArray();
                if (!pre6.SequenceEqual(post6))
                {
                    return $"FAIL p6-missing-slot {BgmSnap(eng.LanesForTests)}";
                }
                // ② 曲库池：正式交付 5 轨（game 槽双曲 bgm_game + bgm_game_2，其余三槽各 1 首，pretitle 空池）
                var lib = eng.LibraryForTests;
                if (lib.Pool("game").Count != 2
                    || lib.Pool("game")[0].Id != "bgm_game"
                    || lib.Pool("game")[1].Id != "bgm_game_2"
                    || lib.Pool("menu").Count != 1
                    || lib.Pool("guide").Count != 1
                    || lib.Pool("pause").Count != 1
                    || lib.Pool("pretitle").Count != 0)
                {
                    return $"FAIL p6-pool game={lib.Pool("game").Count} pretitle={lib.Pool("pretitle").Count}";
                }
                // ②b 新局轮换（PrepareNewMatchBgm）：同槽双曲在开启新局时应平滑切至另一曲
                _bgmTrackBeforeRotate = eng.LanesForTests.FirstOrDefault(l => l.Slot == "game" && l.TargetVolume >= 1f)?.TrackId ?? "";
                _bgmSlotOvrBefore = SettingsStore.Get("music", "slot_game", "<absent>").AsString();
                SettingsStore.Set("music", "slot_game", "auto");
                _audio.PrepareNewMatchBgm(preferAdvanced: _bgmTrackBeforeRotate == "bgm_game");
                _bgmT0 = Time.GetTicksMsec();
                _bgmPhase = 7;
                return "wait";
            case 7: // 轮换后切到另一曲目，且黑白落子 + 胜负短乐句触发 BGM 闪避
                if (!BgmWait(() =>
                {
                    var g = eng.LanesForTests.FirstOrDefault(l => l.Slot == "game" && l.TargetVolume >= 1f);
                    return g is { Playing: true } && g.TrackId != _bgmTrackBeforeRotate && g.LinVolume >= 0.5f;
                }))
                {
                    return _bgmTimedOut ? $"FAIL p7-rotate before={_bgmTrackBeforeRotate} {BgmSnap(eng.LanesForTests)}" : "wait";
                }
                _audio.PlayStonePlace(PlayerColor.Black);
                _audio.PlayStonePlace(PlayerColor.White);
                _audio.PlayGameOver(GameOverSfx.Win);
                _bgmT0 = Time.GetTicksMsec();
                _bgmPhase = 8;
                return "wait";
            case 8: // 胜负短乐句期间 BGM 闪避压低 → StopGameOverSfx 释放 → 测 slot_game=off 淡出收尾
                if (!BgmWait(() => _audio.BgmDuckForTests < 0.75f))
                {
                    return _bgmTimedOut ? $"FAIL p8-duck duck={_audio.BgmDuckForTests:0.00} playing={_audio.IsGameOverStingerPlayingForTests}" : "wait";
                }
                _audio.StopGameOverSfx();
                SettingsStore.Set("music", "slot_game", "off");
                _audio.PlayBgm("game");
                _bgmT0 = Time.GetTicksMsec();
                _bgmPhase = 9;
                return "wait";
            case 9: // off 覆盖淡出完成 + BGM 闪避回升 → 恢复偏好收尾
                if (!BgmWait(() => !eng.LanesForTests.Any(l => l.Playing) && _audio.BgmDuckForTests >= 0.9f))
                {
                    return _bgmTimedOut ? $"FAIL p9-off duck={_audio.BgmDuckForTests:0.00} {BgmSnap(eng.LanesForTests)}" : "wait";
                }
                SettingsStore.Set("music", "slot_game", _bgmSlotOvrBefore == "<absent>" ? "auto" : _bgmSlotOvrBefore);
                _audio.StopBgm();
                _bgmPhase = 0;
                return "ok";
        }
        return "FAIL phase";
    }

    private string _bgmSlotOvrBefore = "";
    private string _bgmTrackBeforeRotate = "";

    private int _saveAlignPhase;
    private int _saveAlignClearIdx;
    private string _saveAlignB64 = "";
    private string _saveAlignB64Alt = "";
    private string _saveAlignIdBefore = "";
    private string _saveAlignAltId = "";
    private string _saveAlignCopyId = "";
    private string _saveAlignQuickId = "";

    /// <summary>批9 存档语义定稿回归（2026-09-26）：S 键落首空槽；复制→填写（空槽免确认）→覆盖
    /// （占用先确认，合成 confirmed）；文件导入指定槽与统一落槽（首空槽）；全满时统一导入覆盖槽 5
    /// 且快存槽受保护。剪贴板本体 headless 不可靠，走 ExportSlot 返回值 + FillSlot(index, text)。
    /// 导入原档保真（id/timestamp 随档），覆盖发生与否用档 Id 断言（BuildSaveData 每次新 id）。</summary>
    private string AutotestSaveAlignment()
    {
        switch (_saveAlignPhase)
        {
            case 0:
                // 跨帧逐槽清除（P2-1：不在 _Process 里同步等整串异步链）；清完再进对局
                if (_saveAlignClearIdx < GameConfig.SaveSlotCount)
                {
                    _store.Saves.DeleteAsync(_saveAlignClearIdx).GetAwaiter().GetResult();
                    _saveAlignClearIdx++;
                    return "wait";
                }
                _gm.Configure(9, 3, RuleSet.Gomoku, false, AIDifficulty.Medium);
                if (!_store.StartGame() || !_gm.TryPlace(4, 4, 0) || !_gm.TryPlace(4, 5, 0) || !_store.Pause())
                {
                    _saveAlignPhase = 0;
                    return "FAIL start";
                }
                Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = Key.S, Pressed = true, Echo = false });
                _saveAlignPhase = 1;
                return "wait";
            case 1:
                Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = Key.S, Pressed = false });
                var first = _store.Saves.Load(0);
                bool landed0 = first != null && first.Moves.Count == 2 && _store.Saves.IsSlotEmpty(1);
                Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = Key.S, Pressed = true, Echo = false });
                _saveAlignPhase = 2;
                return landed0 ? "wait" : "FAIL first empty";
            case 2:
                Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = Key.S, Pressed = false });
                if (_store.Saves.IsSlotEmpty(1))
                {
                    _saveAlignPhase = 0;
                    return "FAIL second empty";
                }
                // 复制（槽→剪贴板格式文本）→ 填写（空槽 2，免确认直写）
                _saveAlignB64 = _store.ExportSlot(0);
                _store.FillSlot(2, _saveAlignB64);
                _saveAlignB64Alt = _store.ExportSlot(1);
                _saveAlignAltId = _store.Saves.Load(1)!.Id;
                _saveAlignIdBefore = _store.Saves.Load(0)!.Id; // 即剪贴板那份档（b64_0）的 Id
                _saveAlignPhase = 3;
                return "wait";
            case 3:
                var filled = _store.Saves.Load(2);
                if (filled == null || filled.Moves.Count != 2 || filled.Id != _saveAlignIdBefore)
                {
                    _saveAlignPhase = 0;
                    return "FAIL fill empty slot";
                }
                // 覆盖（占用槽 0 ← 槽 1 的档）：确认框语义合成驱动（headless 弹窗不自动确认）
                _store.FillSlot(0, _saveAlignB64Alt);
                _overlay.ConfirmDialogForTests.EmitSignal("confirmed");
                _saveAlignPhase = 4;
                return "wait";
            case 4:
                var afterOverwrite = _store.Saves.Load(0);
                bool overwriteOk = afterOverwrite != null && afterOverwrite.Id == _saveAlignAltId;
                // 文件路径：槽 0 → .nzsave → 空槽行导入到槽 3
                _store.ExportSlotToFile(0, "user://m4c-roundtrip.nzsave");
                _store.ImportFileToSlot("user://m4c-roundtrip.nzsave", 3);
                _saveAlignPhase = 5;
                return overwriteOk ? "wait" : "FAIL overwrite occupied";
            case 5:
                var fileSlot = _store.Saves.Load(3);
                if (fileSlot == null || fileSlot.Moves.Count != 2)
                {
                    _saveAlignPhase = 0;
                    return "FAIL file-to-slot";
                }
                _store.ImportFile("user://m4c-roundtrip.nzsave"); // 统一导入 → 首空槽 = 槽位 5（下标 4）
                _saveAlignPhase = 6;
                return "wait";
            case 6:
                var unified = _store.Saves.Load(4);
                if (unified == null || unified.Moves.Count != 2)
                {
                    _saveAlignPhase = 0;
                    return "FAIL unified first empty";
                }
                // 全满铺setup：下标 4 与快存槽各换一版新档（新 Id）→ 统一导入（槽 0 的档，Id=槽 2 现档）
                // 须覆盖最后一个非快存槽（下标 4），快存槽（下标 5）受保护
                _saveAlignCopyId = _store.Saves.Load(2)!.Id; // 剪贴板里那份档的 Id
                _store.Saves.SaveAsync(4, _gm.BuildSaveData(_store.IsDarkTheme)).GetAwaiter().GetResult();
                _store.Saves.SaveAsync(GameConfig.QuickSaveIndex, _gm.BuildSaveData(_store.IsDarkTheme))
                    .GetAwaiter().GetResult();
                var quickBefore = _store.Saves.Load(GameConfig.QuickSaveIndex);
                if (quickBefore == null)
                {
                    _saveAlignPhase = 0;
                    return "FAIL full setup";
                }
                _saveAlignQuickId = quickBefore.Id;
                _store.ImportText(_saveAlignB64);
                _overlay.ConfirmDialogForTests.EmitSignal("confirmed");
                _saveAlignPhase = 7;
                return "wait";
            case 7:
                var after4 = _store.Saves.Load(4);
                var quickAfter = _store.Saves.Load(GameConfig.QuickSaveIndex);
                bool protectedOk = after4 != null && after4.Id == _saveAlignCopyId
                    && quickAfter != null && quickAfter.Id == _saveAlignQuickId;
                _store.ReturnToTitle();
                _saveAlignPhase = 0;
                _saveAlignClearIdx = 0;
                return protectedOk ? "ok" : "FAIL quick-slot protection";
            default:
                _saveAlignPhase = 0;
                return "FAIL";
        }
    }

    private int _ngPhase;

    /// <summary>
    /// 新对局默认上下文回归（人类反馈 2026-09-25）：非默认对局（9×3）+ 读档主题泄漏后，
    /// 标题「开始游戏」必须回到启动默认 13×13×6 与偏好主题。StartLevel 与 LoadFrom 改配置的
    /// 效果等价（Configure 路径），故用 StartLevel 造现场；主题直接置属性模拟 ApplySavedTheme 泄漏。
    /// </summary>
    private string AutotestNewGameDefaultContext()
    {
        switch (_ngPhase)
        {
            case 0:
                if (!_store.StartLevel(new LevelConfig
                {
                    Id = "ng-default",
                    Name = "ng-default",
                    BoardSize = 9,
                    Layers = 3,
                    Rules = RuleSet.Gomoku,
                }, true, AIDifficulty.Hard) || !_gm.TryPlace(4, 4, 0))
                {
                    _ngPhase = 0;
                    return "FAIL start custom";
                }
                _store.IsDarkTheme = false; // 模拟读档亮色主题泄漏（不经过 T，不改偏好）
                _store.Pause();
                _store.ReturnToTitle();
                _ngPhase = 1;
                return "wait";
            case 1:
                if (_store.State != GameState.Title)
                {
                    return "wait";
                }
                _store.RequestStartNew(); // 有保留局：确认框弹出（headless 不自动确认，同 m4r-confirm-guard）
                bool asked = _overlay.IsModalOpen;
                _overlay.ConfirmDialogForTests.EmitSignal("confirmed"); // 合成确认 → DoStart
                _ngPhase = 2;
                return asked ? "wait" : "FAIL no-confirm-dialog";
            case 2:
                if (_store.State != GameState.Playing)
                {
                    return "wait"; // 单步帧预算兜底（超预算自动 FAIL 出队）
                }
                bool dimsOk = _gm.Board.BoardSize == 13 && _gm.Board.Layers == 6;
                bool modeOk = !_gm.PvE && _gm.Difficulty == AIDifficulty.Medium && _gm.History.Count == 0;
                bool themeOk = _store.IsDarkTheme; // 偏好未被 T 改过 = 默认暗色
                _ngPhase = 0;
                return dimsOk && modeOk && themeOk
                    ? "ok"
                    : $"FAIL dims={dimsOk} mode={modeOk} theme={themeOk}";
            default:
                _ngPhase = 0;
                return "FAIL";
        }
    }

    /// <summary>
    /// R6/验收轮：真实 v2.0.0 旧档（源浏览器导出，见 tests Fixtures browser_real_export_*）
    /// 走完整导入→读档→续弈链路。相对路径在仓库根执行（本地与 CI 一致）。
    /// </summary>
    private string AutotestRealOldSaveRestore()
    {
        // Godot 启动会把进程 CWD 切到项目目录 → 同时探测仓库根（CI/本地）与项目目录两种基点
        string rel = "tests/Nmoku.Core.Tests/Fixtures/browser_real_export_v2_standard.b64.txt";
        string? fixturePath = new[]
        {
            System.IO.Path.Combine(System.Environment.CurrentDirectory, rel),
            System.IO.Path.GetFullPath(System.IO.Path.Combine(System.Environment.CurrentDirectory, "..", "..", rel)),
        }.FirstOrDefault(System.IO.File.Exists);
        using var file = fixturePath == null
            ? null
            : Godot.FileAccess.Open(fixturePath, Godot.FileAccess.ModeFlags.Read);
        if (file == null)
        {
            return "FAIL fixture not found (run from repo root)";
        }
        string b64 = file.GetAsText().Trim();

        // 完整导入路径（嗅探+校验+落首空槽）；先前自测步可能已占用槽 0 → 按 id 定位
        _store.ImportText(b64);
        int slot = -1;
        for (int i = 0; i < GameConfig.SaveSlotCount; i++)
        {
            if (_store.Saves.Load(i)?.Id == "47dd93b4-cb80-48cb-b2ba-2419ea829461")
            {
                slot = i;
                break;
            }
        }
        if (slot < 0)
        {
            return "FAIL import";
        }
        if (!_store.LoadSlot(slot) || _store.State != GameState.Playing || _gm.History.Count != 8)
        {
            return "FAIL restore";
        }
        // 续弈：样本黑已有 (4..7,4) 四连，恢复后轮黑——第 9 手 (8,4) 成五（胜负判定随档生效）
        if (!_gm.TryPlace(8, 4, 0) || !_gm.IsOver || _gm.Winner != 1 || _store.State != GameState.GameOver)
        {
            return "FAIL continue";
        }
        _store.ReturnToTitle();
        _store.Saves.DeleteAsync(slot).GetAwaiter().GetResult();
        return "ok";
    }

    private int _qsPhase;

    /// <summary>§6.5 修饰键检测：Ctrl+S 触发快存、裸 S 不触发（exact match + 直通不误吞）。</summary>
    private string AutotestModifierQuickSave()
    {
        switch (_qsPhase)
        {
            case 0:
                _gm.Configure(9, 3, RuleSet.Gomoku, false, AIDifficulty.Medium);
                if (!_store.StartGame() || _store.State != GameState.Playing)
                {
                    return "FAIL start";
                }
                Input.ParseInputEvent(new InputEventKey
                {
                    PhysicalKeycode = Key.S, CtrlPressed = true, Pressed = true, Echo = false,
                });
                _qsPhase = 1;
                return "wait";
            case 1:
                Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = Key.S, Pressed = false });
                bool saved = !_store.Saves.IsSlotEmpty(GameConfig.QuickSaveIndex);
                _store.Saves.DeleteAsync(GameConfig.QuickSaveIndex).GetAwaiter().GetResult();
                // 裸 S：无快捷键匹配（exact match 下 ctrl_pressed 不符）→ 只落 zoom_out 持续键，快存不得触发
                Input.ParseInputEvent(new InputEventKey
                {
                    PhysicalKeycode = Key.S, Pressed = true, Echo = false,
                });
                _qsPhase = 2;
                return saved ? "wait" : "FAIL ctrl+s no save";
            case 2:
                Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = Key.S, Pressed = false });
                bool untouched = _store.Saves.IsSlotEmpty(GameConfig.QuickSaveIndex);
                _store.Pause();
                _store.ReturnToTitle();
                _qsPhase = 0;
                return untouched ? "ok" : "FAIL plain s saved";
            default:
                return "FAIL";
        }
    }

    private int _qlPhase;
    private int _qlMovesBefore;
    /// <summary>Ctrl+L 读快存回归（P2）：快存后续弈 2 手，暂停中 Ctrl+L 恢复到快存点并回对局。
    /// 生效态=Paused（§3.4 读档仅 Title/Paused 合法，Playing 不扩表）。</summary>
    private string AutotestQuickLoad()
    {
        switch (_qlPhase)
        {
            case 0:
                _store.Pause();
                _store.ReturnToTitle(); // 上一步（newgame-default-context）结束于 Playing，先回标题
                _gm.Configure(9, 3, RuleSet.Gomoku, false, AIDifficulty.Medium);
                if (!_store.StartGame() || _store.State != GameState.Playing)
                {
                    _qlPhase = 0;
                    return "FAIL start";
                }
                Input.ParseInputEvent(new InputEventKey
                {
                    PhysicalKeycode = Key.S, CtrlPressed = true, Pressed = true, Echo = false,
                });
                _qlPhase = 1;
                return "wait";
            case 1:
                Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = Key.S, Pressed = false });
                if (_store.Saves.IsSlotEmpty(GameConfig.QuickSaveIndex))
                {
                    _qlPhase = 0;
                    return "FAIL quicksave missing";
                }
                _qlMovesBefore = _gm.History.Count;
                _gm.TryPlace(0, 0, 0);
                _gm.TryPlace(1, 1, 0);
                if (!_store.Pause())
                {
                    _qlPhase = 0;
                    return "FAIL pause";
                }
                Input.ParseInputEvent(new InputEventKey
                {
                    PhysicalKeycode = Key.L, CtrlPressed = true, Pressed = true, Echo = false,
                });
                _qlPhase = 2;
                return "wait";
            case 2:
                Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = Key.L, Pressed = false });
                bool restored = _store.State == GameState.Playing && _gm.History.Count == _qlMovesBefore;
                _store.Pause();
                _store.ReturnToTitle();
                _store.Saves.DeleteAsync(GameConfig.QuickSaveIndex).GetAwaiter().GetResult();
                _qlPhase = 0;
                return restored ? "ok" : $"FAIL restored={restored} state={_store.State} moves={_gm.History.Count}/{_qlMovesBefore}";
            default:
                _qlPhase = 0;
                return "FAIL";
        }
    }

    private int _rpPhase;

    /// <summary>主场景复盘进入回归（真实窗口 NRE 崩溃根因）：首次 EnterReplay 时 replay-hud 尚未挂载、
    /// Refresh 会触发 _replayBar.Sync——修复前必炸（无头自测此前只在隔离 store 上测过复盘）。
    /// 槽位 0 无档则自造一档（自足），从标题进入复盘断言状态与手数，再退出清理。</summary>
    private string AutotestReplayEntry()
    {
        switch (_rpPhase)
        {
            case 0:
                if (_store.State != GameState.Title)
                {
                    _store.Pause();
                    _store.ReturnToTitle();
                    return "wait";
                }
                if (_store.Saves.IsSlotEmpty(0))
                {
                    _gm.Configure(9, 3, RuleSet.Gomoku, false, AIDifficulty.Medium);
                    if (!_store.StartGame() || !_gm.TryPlace(4, 4, 0) || !_gm.TryPlace(4, 5, 0))
                    {
                        _rpPhase = 0;
                        return "FAIL setup";
                    }
                    _store.SaveToSlot(0);
                    _store.Pause();
                    _store.ReturnToTitle();
                    return "wait";
                }
                _rpPhase = 1;
                return "wait";
            case 1:
                if (!_store.EnterReplay(0))
                {
                    _rpPhase = 0;
                    return "FAIL enter";
                }
                _rpPhase = 2;
                return "wait";
            case 2:
                bool ok = _store.State == GameState.Replay && _gm.InReplay && _gm.ReplayCount > 0;
                _store.ExitReplay();
                _rpPhase = 0;
                return ok ? "ok" : $"FAIL state={_store.State} inReplay={_gm.InReplay} count={_gm.ReplayCount}";
            default:
                _rpPhase = 0;
                return "FAIL";
        }
    }

    private int _rhPhase;

    /// <summary>批10 复盘 HUD 回归（2026-09-26 人类实测反馈）：0 手复盘滑条/步进禁用；环与行棋方
    /// 随游标（LastVisibleMove/Current，游标 0 无环且黑先）；ReplayBar 几何断言——不出右界、
    /// 不与右下角行棋指示器重叠。</summary>
    private string AutotestReplayHud()
    {
        switch (_rhPhase)
        {
            case 0:
                if (_store.State != GameState.Title)
                {
                    _store.Pause();
                    _store.ReturnToTitle();
                    return "wait";
                }
                // 造双档：槽 1 = 0 手（空局复盘适配用）、槽 0 = 2 手（游标断言用），无条件重建防串场
                _gm.Configure(9, 3, RuleSet.Gomoku, false, AIDifficulty.Medium);
                if (!_store.StartGame())
                {
                    _rhPhase = 0;
                    return "FAIL start";
                }
                _store.SaveToSlot(1);
                if (!_gm.TryPlace(4, 4, 0) || !_gm.TryPlace(4, 5, 0))
                {
                    _rhPhase = 0;
                    return "FAIL place";
                }
                _store.SaveToSlot(0);
                _store.Pause();
                _store.ReturnToTitle();
                _rhPhase = 1;
                return "wait";
            case 1:
                if (!_store.EnterReplay(1))
                {
                    _rhPhase = 0;
                    return "FAIL enter-empty";
                }
                _rhPhase = 2;
                return "wait";
            case 2:
                if (_store.State != GameState.Replay || _replayBar == null
                    || !GodotObject.IsInstanceValid(_replayBar))
                {
                    return "wait";
                }
                if (!_replayBar.ZeroMoveMode || _gm.ReplayCount != 0 || _gm.LastVisibleMove != null
                    || _gm.Current != PlayerColor.Black
                    || _turnIndicator.TextForTests != I18n.T(Text.Turn.BlackTurn) || !_turnIndicator.StoneVisible)
                {
                    _rhPhase = 0;
                    return "FAIL zero-move";
                }
                _store.ExitReplay();
                _rhPhase = 3;
                return "wait";
            case 3:
                if (_store.State != GameState.Title)
                {
                    return "wait";
                }
                if (!_store.EnterReplay(0))
                {
                    _rhPhase = 0;
                    return "FAIL enter-2moves";
                }
                _gm.StepReplay(1); // 游标 1：环=第 1 手、行棋方=白
                _rhPhase = 4;
                return "wait";
            case 4:
                if (_store.State != GameState.Replay || _gm.ReplayIndex != 1 || _replayBar == null
                    || !GodotObject.IsInstanceValid(_replayBar))
                {
                    return "wait";
                }
                if (_gm.LastVisibleMove is not { } last || !last.Equals(_gm.History[0])
                    || _gm.Current != PlayerColor.White
                    || _turnIndicator.TextForTests != I18n.T(Text.Turn.WhiteTurn))
                {
                    _rhPhase = 0;
                    return "FAIL cursor-follow";
                }
                // 几何断言（768 逻辑宽基准）：ReplayBar 不出界、不与指示器重叠
                var bar = _replayBar.GetGlobalRect();
                float viewWidth = GetViewport().GetVisibleRect().Size.X;
                var ind = _turnIndicator.GetGlobalRect();
                if (bar.Position.X < -0.5 || bar.End.X > viewWidth + 0.5 || bar.Intersects(ind))
                {
                    _rhPhase = 0;
                    return $"FAIL geom bar=[{bar.Position.X:F0},{bar.End.X:F0}] vp={viewWidth:F0} indX={ind.Position.X:F0}";
                }
                _gm.SetReplayIndex(0);
                _rhPhase = 5;
                return "wait";
            case 5:
                if (_gm.ReplayIndex != 0)
                {
                    return "wait";
                }
                bool ok = _gm.LastVisibleMove == null && _gm.Current == PlayerColor.Black;
                _store.ExitReplay();
                _rhPhase = 0;
                return ok ? "ok" : "FAIL cursor-0";
            default:
                _rhPhase = 0;
                return "FAIL";
        }
    }

    private int _wsPhase;
    private Vector2I _wsPos0;
    private Vector2I _wsSize0;
    private string _maxOcclusion = "";

    /// <summary>批11 任务栏遮挡（人类指示"不要遍历，彻底解决"）：自动驱动人类复现序列——
    /// 游戏→退出→读取→退出 + 存档菜单开→关 + 导出文件对话框开→关，全程窗口几何由
    /// TraceWindowGeometry 落盘关联；结束断言 pos/size 未被改动，被改动即几何证据。
    /// headless 无窗口管理器，恒 ok。</summary>
    private string AutotestWindowSequence()
    {
        if (DisplayServer.GetName() == "headless")
        {
            return "ok";
        }
        switch (_wsPhase)
        {
            case 0:
                if (_store.State != GameState.Title)
                {
                    _store.Pause();
                    _store.ReturnToTitle();
                    return "wait";
                }
                if (_store.Saves.GetLatestIndex() < 0)
                {
                    _gm.Configure(9, 3, RuleSet.Gomoku, false, AIDifficulty.Medium);
                    if (!_store.StartGame() || !_gm.TryPlace(4, 4, 0))
                    {
                        _wsPhase = 0;
                        return "FAIL setup";
                    }
                    _store.SaveToSlot(0);
                    _store.Pause();
                    _store.ReturnToTitle();
                    return "wait";
                }
                _wsPos0 = DisplayServer.WindowGetPosition();
                _wsSize0 = DisplayServer.WindowGetSize();
                if (!_store.StartGame()) // 进游戏（有保留局则为"继续对局"，语义等价）
                {
                    _wsPhase = 0;
                    return "FAIL start";
                }
                _wsPhase = 1;
                return "wait";
            case 1: // 游戏 → 退出到标题
                _store.Pause();
                if (!_store.ReturnToTitle())
                {
                    _wsPhase = 0;
                    return "FAIL exit1";
                }
                _wsPhase = 2;
                return "wait";
            case 2: // 读取（L/读最新同语义）
                if (_store.State != GameState.Title)
                {
                    return "wait";
                }
                if (!_store.LoadLatest())
                {
                    _wsPhase = 0;
                    return "FAIL load";
                }
                _wsPhase = 3;
                return "wait";
            case 3: // 退出
                _store.Pause();
                if (!_store.ReturnToTitle())
                {
                    _wsPhase = 0;
                    return "FAIL exit2";
                }
                _wsPhase = 4;
                return "wait";
            case 4: // 存档菜单开
                if (_store.State != GameState.Title)
                {
                    return "wait";
                }
                _router.PushOverlay("save-menu");
                _wsPhase = 5;
                return "wait";
            case 5: // 存档菜单关
                CloseSaveMenu();
                _wsPhase = 6;
                return "wait";
            case 6: // 导出文件对话框开（人类另一线索：FileDialog 页面出现时）
                OpenExportFileDialog(_store.Saves.GetLatestIndex());
                _wsPhase = 7;
                return "wait";
            case 7: // 对话框关（不落盘）
                _exportFileDialog?.Hide();
                _wsPhase = 8;
                return "wait";
            case 8: // 最小化→还原：人类实际使用中最高频的窗口态变化（alt-tab/最小化偶现特征的头号嫌疑）
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.Minimized);
                _wsPhase = 9;
                return "wait";
            case 9:
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
                _wsPhase = 10;
                return "wait";
            case 10: // 最大化：Godot-on-Windows 若把最大化窗口画进整屏（越过工作区），任务栏即遮挡——可自动化取证
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.Maximized);
                _wsPhase = 11;
                return "wait";
            case 11:
            {
                var maxedPos = DisplayServer.WindowGetPosition();
                var maxed = DisplayServer.WindowGetSize(); // (宽, 高)
                var usable = DisplayServer.ScreenGetUsableRect(DisplayServer.WindowGetCurrentScreen());
                bool occluded = maxedPos.Y + maxed.Y > usable.Position.Y + usable.Size.Y;
                _maxOcclusion = occluded
                    ? $"maximized pos={maxedPos} size={maxed} usable={usable}"
                    : "";
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
                _wsPhase = 12;
                return "wait";
            }
            case 12: // 终判：窗口几何未被动过，且最大化未越过工作区
                var pos = DisplayServer.WindowGetPosition();
                var size = DisplayServer.WindowGetSize();
                bool stable = pos == _wsPos0 && size == _wsSize0;
                _wsPhase = 0;
                if (!stable)
                {
                    return $"FAIL moved pos {_wsPos0}→{pos} size {_wsSize0}→{size}";
                }
                return _maxOcclusion.Length == 0
                    ? "ok"
                    : $"FAIL maximize-over-workarea {_maxOcclusion}";
            case 13: // 人类真实路径补测：带保留局开始游戏 → RequestStartNew 弹确认框（子窗口开关！）
                if (_store.State != GameState.Title || !_store.HasLiveGame)
                {
                    return "wait";
                }
                _store.RequestStartNew();
                _wsPhase = 14;
                return "wait";
            case 14:
                if (!_overlay.IsModalOpen)
                {
                    return "wait";
                }
                _overlay.ConfirmDialogForTests.EmitSignal("confirmed");
                _wsPhase = 15;
                return "wait";
            case 15:
                if (_store.State != GameState.Playing)
                {
                    return "wait";
                }
                _store.Pause();
                _store.ReturnToTitle();
                _wsPhase = 16;
                return "wait";
            case 16: // 确认框（子窗口）开→关后窗口模式不得被翻动
                var postMode = DisplayServer.WindowGetMode();
                _wsPhase = 0;
                return postMode == DisplayServer.WindowMode.Windowed
                    ? "ok"
                    : $"FAIL dialog-flipped-mode pos={DisplayServer.WindowGetPosition()} " +
                      $"size={DisplayServer.WindowGetSize()} mode={postMode}";
            default:
                _wsPhase = 0;
                return "FAIL";
        }
    }

    /// <summary>M4R-2 自测：合成真实按键事件验证 echo 过滤——非 echo 按键转 90°，echo 按键不再触发。</summary>
    private int _echoPhase;
    private double _yawAtStart, _yawAfterPress, _yawAfterEcho;

    private string AutotestKeyEchoFilter()
    {
        switch (_echoPhase)
        {
            case 0:
                _gm.Configure(9, 3, RuleSet.Gomoku, false, AIDifficulty.Medium);
                _store.StartGame();
                _yawAtStart = _rig.Target.Yaw;
                Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = Key.Q, Pressed = true, Echo = false });
                _echoPhase = 1;
                return "wait"; // 事件经输入管线下一帧 flush → _Input
            case 1:
                _yawAfterPress = _rig.Target.Yaw;
                Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = Key.Q, Pressed = true, Echo = true });
                _echoPhase = 2;
                return "wait";
            case 2:
                _yawAfterEcho = _rig.Target.Yaw;
                Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = Key.Q, Pressed = false });
                _echoPhase = 0;
                bool pressed = Math.Abs(_yawAfterPress - _yawAtStart + 90) < 1e-6; // Q = -90°
                bool echoIgnored = Math.Abs(_yawAfterEcho - _yawAfterPress) < 1e-6;
                return pressed && echoIgnored ? "ok" : $"FAIL pressΔ={_yawAfterPress - _yawAtStart:0.0} echoΔ={_yawAfterEcho - _yawAfterPress:0.0}";
            default:
                return "FAIL";
        }
    }

    private Control? _staleProbe;

    /// <summary>M4 自测：存档落盘 → 模拟重启恢复 → 载入转 Playing → 复盘步进 → 退出回 Title。</summary>
    private string AutotestSaveLoadReplay()
    {
        var sm = new SaveManager(new FileSystemStorageAdapter("user://saves_autotest"));
        sm.InitAsync().GetAwaiter().GetResult();
        var data = _gm.BuildSaveData(true);
        sm.SaveAsync(2, data).GetAwaiter().GetResult();

        // 模拟重启：全新 store 从磁盘恢复槽位
        var reborn = new GameStore(new FileSystemStorageAdapter("user://saves_autotest"));
        reborn.InitializeSaves();
        var loaded = reborn.Saves.Load(2);
        bool saved = loaded != null && loaded.Id == data.Id && loaded.Moves.Count == data.Moves.Count
            && reborn.Saves.GetLatestIndex() == 2;

        // 载入（Title→Playing）→ 复盘步进 → 退出回 Title（§3.4 REPLAY 行）
        bool flow = reborn.LoadSlot(2) && reborn.State == GameState.Playing
            && reborn.GM.History.Count == data.Moves.Count
            && reborn.GM.Board.MoveCount() == data.Moves.Count
            && reborn.EnterReplay(2) && reborn.State == GameState.Replay
            && reborn.GM.Board.MoveCount() == 0;
        reborn.GM.StepReplay(1);
        flow = flow && reborn.GM.ReplayIndex == 1 && reborn.GM.Board.MoveCount() == 1;
        flow = flow && reborn.ExitReplay() && reborn.State == GameState.Title;

        sm.DeleteAsync(2).GetAwaiter().GetResult();
        return saved && flow ? "ok" : "FAIL";
    }

    /// <summary>M4 自测：Base64 导入路径（统一嗅探入口）落入首个空槽。自备目录+预清理——
    /// "首空槽=0" 断言不得依赖其他步骤的落槽残留（批11 主 store 隔离后曾与其共享目录而互相污染）。</summary>
    private string AutotestImportClipboardPath()
    {
        var data = _gm.BuildSaveData(false);
        var isoStore = new GameStore(new FileSystemStorageAdapter("user://saves_autotest_import"));
        isoStore.InitializeSaves();
        for (int i = 0; i < GameConfig.SaveSlotCount; i++)
        {
            isoStore.Saves.DeleteAsync(i).GetAwaiter().GetResult();
        }
        isoStore.ImportText(SaveData.SerializeToBase64(data));
        int filled = -1;
        for (int i = 0; i < GameConfig.SaveSlotCount; i++)
        {
            if (!isoStore.Saves.IsSlotEmpty(i))
            {
                filled = i;
                break;
            }
        }
        var imported = filled == 0 ? isoStore.Saves.Load(0) : null;
        bool ok = imported != null && imported.Id == data.Id;
        if (filled >= 0)
        {
            isoStore.Saves.DeleteAsync(filled).GetAwaiter().GetResult();
        }
        return ok ? "ok" : "FAIL";
    }
}
