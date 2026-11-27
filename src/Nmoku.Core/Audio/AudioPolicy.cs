using Nmoku.Core.AI;

namespace Nmoku.Core.Audio;

/// <summary>BGM 曲目元数据条目（用于 BGM 浏览界面展示与槽位映射，单一事实源）。</summary>
public sealed record BgmTrackMeta(string TrackId, string Slot, string TitleKey, string DescKey);

/// <summary>终局音效类型（PvE 区分胜负，PvP 胜方均播胜利，平局静默不播胜负号角）。</summary>
public enum GameOverSfx
{
    None,
    Win,
    Lose,
}

/// <summary>
/// 音频纯逻辑策略（§10 B31，零 Godot 依赖）：
/// 1) 终局胜负音效判定（PvE 区分人类胜/AI 胜，PvP 胜者播 Win，平局 None）；
/// 2) 对局多曲池选曲策略（会话内保持同曲以支持暂停/引导优雅回归，开新局按难度/规则亲和或轮换）；
/// 3) 同色落子防抖闸门与 BGM 胜负闪避包络。
/// </summary>
public static class AudioPolicy
{
    /// <summary>同色落子防抖窗口（毫秒，对齐 programmed-music 离线试听台 65ms 实测值）。</summary>
    public const double StoneDebounceMs = 65.0;

    public const string StandardGameTrackId = "bgm_game";
    public const string AdvancedGameTrackId = "bgm_game_2";

    /// <summary>已交付的 5 轨正式 BGM 清单（供 BGM 浏览界面渲染与试听）。</summary>
    public static readonly IReadOnlyList<BgmTrackMeta> BgmCatalog =
    [
        new("bgm_menu", "menu", Text.Audio.TrackMenuTitle, Text.Audio.TrackMenuDesc),
        new("bgm_game", "game", Text.Audio.TrackGameTitle, Text.Audio.TrackGameDesc),
        new("bgm_game_2", "game", Text.Audio.TrackGame2Title, Text.Audio.TrackGame2Desc),
        new("bgm_guide", "guide", Text.Audio.TrackGuideTitle, Text.Audio.TrackGuideDesc),
        new("bgm_pause", "pause", Text.Audio.TrackPauseTitle, Text.Audio.TrackPauseDesc),
    ];

    /// <summary>按曲目 Id 查找元数据（未登记的外部扩展曲返回 null）。</summary>
    public static BgmTrackMeta? FindTrackMeta(string? trackId)
    {
        if (string.IsNullOrEmpty(trackId))
        {
            return null;
        }
        for (int i = 0; i < BgmCatalog.Count; i++)
        {
            if (string.Equals(BgmCatalog[i].TrackId, trackId, StringComparison.Ordinal))
            {
                return BgmCatalog[i];
            }
        }
        return null;
    }

    /// <summary>
    /// 终局音效判定：
    /// - winner == 0（平局）或非法值 → None；
    /// - PvE 下 AI 获胜 → Lose，人类获胜 → Win；
    /// - PvP 下任意一方获胜 → Win。
    /// </summary>
    public static GameOverSfx ResolveGameOverSfx(bool pve, int winner, PlayerColor aiColor)
    {
        if (winner is not (1 or 2))
        {
            return GameOverSfx.None;
        }
        if (pve && winner == (int)aiColor)
        {
            return GameOverSfx.Lose;
        }
        return GameOverSfx.Win;
    }

    /// <summary>
    /// 判断当前对局是否倾向进阶对局曲（bgm_game_2，Dorian 轻打击推进）：
    /// 困难 AI / SWAP2 / 残局预设 / 扩展连珠（≥6）或高层数（≥8）优先匹配进阶曲，其余优先标准曲。
    /// </summary>
    public static bool PreferAdvancedGameTrack(LevelConfig? level, bool pve, AIDifficulty aiDifficulty)
    {
        if (pve && aiDifficulty == AIDifficulty.Hard)
        {
            return true;
        }
        if (level == null)
        {
            return false;
        }
        return level.Rules == RuleSet.Swap2
               || string.Equals(level.Id, "residual", StringComparison.OrdinalIgnoreCase)
               || level.WinLength >= 6
               || level.Layers >= 8;
    }

    /// <summary>
    /// 槽位选曲决策：
    /// 1) 覆盖为 "off" → null；覆盖为具体曲目 Id 且在池内 → 锁定该曲；
    /// 2) 会话内（!rotate）且 currentSessionId 仍在池内 → 保持原曲（修复多曲池下暂停/引导往返误切曲）；
    /// 3) 新局首次无历史（lastId == null）或新局亲和曲与上一局不同 → 优先取 preferredId；
    /// 4) 同类新局轮换 → 按 playMode（"sequence" 顺序 / 默认 "random" 不与上一首重复）切下一首。
    /// </summary>
    public static string? SelectNextTrackId(
        IReadOnlyList<string> poolIds,
        string? currentSessionId,
        string? lastId,
        string? slotOverride,
        string? playMode,
        bool rotate,
        string? preferredId = null,
        Func<int, int>? nextRandom = null)
    {
        if (poolIds.Count == 0 || string.Equals(slotOverride, "off", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        if (!string.IsNullOrEmpty(slotOverride)
            && !string.Equals(slotOverride, "auto", StringComparison.OrdinalIgnoreCase))
        {
            for (int i = 0; i < poolIds.Count; i++)
            {
                if (string.Equals(poolIds[i], slotOverride, StringComparison.Ordinal))
                {
                    return poolIds[i];
                }
            }
        }
        if (poolIds.Count == 1)
        {
            return poolIds[0];
        }
        if (!rotate && currentSessionId != null && ContainsId(poolIds, currentSessionId))
        {
            return currentSessionId;
        }
        if (preferredId != null
            && ContainsId(poolIds, preferredId)
            && (lastId == null || !string.Equals(preferredId, lastId, StringComparison.Ordinal)))
        {
            return preferredId;
        }
        if (string.Equals(playMode, "sequence", StringComparison.OrdinalIgnoreCase))
        {
            int idx = IndexOfId(poolIds, lastId);
            return poolIds[(idx + 1) % poolIds.Count];
        }

        // random（不与 lastId 重复）：先筛候选集，避免无界重试
        var candidates = new List<string>(poolIds.Count);
        for (int i = 0; i < poolIds.Count; i++)
        {
            if (!string.Equals(poolIds[i], lastId, StringComparison.Ordinal))
            {
                candidates.Add(poolIds[i]);
            }
        }
        if (candidates.Count == 0)
        {
            return poolIds[0];
        }
        int pickIdx = nextRandom != null
            ? Math.Abs(nextRandom(candidates.Count)) % candidates.Count
            : 0;
        return candidates[pickIdx];
    }

    private static bool ContainsId(IReadOnlyList<string> list, string id) => IndexOfId(list, id) >= 0;

    private static int IndexOfId(IReadOnlyList<string> list, string? id)
    {
        if (id == null)
        {
            return -1;
        }
        for (int i = 0; i < list.Count; i++)
        {
            if (string.Equals(list[i], id, StringComparison.Ordinal))
            {
                return i;
            }
        }
        return -1;
    }
}

/// <summary>
/// 按声道键隔离的音效防抖闸门（默认 65ms）：
/// 同色连点在窗口内去抖防叠爆；异色（例如人类黑子后紧跟 AI 白子）彼此独立、不互相吞音。
/// </summary>
public sealed class SfxDebounceGate
{
    private readonly double _minIntervalMs;
    private readonly Dictionary<int, double> _lastMsByChannel = [];

    public SfxDebounceGate(double minIntervalMs = AudioPolicy.StoneDebounceMs)
    {
        _minIntervalMs = Math.Max(0.0, minIntervalMs);
    }

    public bool TryAcquire(int channelKey, double nowMs)
    {
        if (_lastMsByChannel.TryGetValue(channelKey, out double lastMs)
            && nowMs >= lastMs
            && nowMs - lastMs < _minIntervalMs)
        {
            return false;
        }
        _lastMsByChannel[channelKey] = nowMs;
        return true;
    }

    public void Reset() => _lastMsByChannel.Clear();
}

/// <summary>
/// BGM 闪避包络（用于 3s 胜负管弦短乐句播放期间柔化 BGM，避免和声打架）：
/// 触发时快速压低至 <see cref="DuckLevel"/>，短乐句结束或切屏后平滑回升至 1.0。
/// </summary>
public sealed class BgmDuckEnvelope
{
    public const float DefaultDuckLevel = 0.22f;
    public const float DefaultAttackSec = 0.12f;
    public const float DefaultReleaseSec = 1.0f;

    private readonly float _duckLevel;
    private readonly float _attackSec;
    private readonly float _releaseSec;

    public float Current { get; private set; } = 1f;

    public BgmDuckEnvelope(
        float duckLevel = DefaultDuckLevel,
        float attackSec = DefaultAttackSec,
        float releaseSec = DefaultReleaseSec)
    {
        _duckLevel = Math.Clamp(duckLevel, 0f, 1f);
        _attackSec = Math.Max(0.001f, attackSec);
        _releaseSec = Math.Max(0.001f, releaseSec);
    }

    public float Step(float deltaSec, bool holdDucked)
    {
        if (deltaSec <= 0f)
        {
            return Current;
        }
        float target = holdDucked ? _duckLevel : 1f;
        if (MathF.Abs(Current - target) < 0.0005f)
        {
            Current = target;
            return Current;
        }
        float span = MathF.Max(0.001f, 1f - _duckLevel);
        if (target < Current)
        {
            Current = MathF.Max(target, Current - (span / _attackSec) * deltaSec);
        }
        else
        {
            Current = MathF.Min(target, Current + (span / _releaseSec) * deltaSec);
        }
        return Current;
    }

    public void Reset() => Current = 1f;
}
