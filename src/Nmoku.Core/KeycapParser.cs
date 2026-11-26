namespace Nmoku.Core;

/// <summary>
/// Keys 展示文本 → 键帽组的纯解析（F1 页键帽化显示用，2026-09-25）。
/// 分段规则："或" 拆可替换大段 → "/" 拆并列段（Q / E = Q、E 两枚；Shift+WASD / Shift+方向键 =
/// 两组组合）→ "+" 拆组合键（修饰键单独一枚键帽）→ WASD/方向键 等组名展开为逐键。
/// 解析不出任何键帽的段（如 @mouse 文档行的中文描述）整段放弃；全空返回空列表，视图据此跳过。
/// </summary>
public static class KeycapParser
{
    /// <summary>一个并列段：组合时首枚为修饰键（Ctrl+S = [Ctrl][S]），并列双键各自成段。</summary>
    public sealed record CapGroup(string[] Caps)
    {
        public string Modifier => Caps.Length > 1 ? Caps[0] : "";
        public IEnumerable<string> Keys => Caps.Length > 1 ? Caps[1..] : Caps;
    }

    private static readonly Dictionary<string, string[]> GroupExpansion = new()
    {
        ["WASD"] = ["W", "A", "S", "D"],
        ["方向键"] = ["↑", "↓", "←", "→"],
    };

    public static List<CapGroup> Parse(string? keys)
    {
        var groups = new List<CapGroup>();
        if (string.IsNullOrWhiteSpace(keys))
        {
            return groups;
        }
        foreach (var alternative in keys.Split(" 或 ", StringSplitOptions.TrimEntries))
        {
            foreach (var segment in alternative.Split('/', StringSplitOptions.TrimEntries))
            {
                var caps = new List<string>();
                foreach (var token in segment.Split('+', StringSplitOptions.TrimEntries))
                {
                    if (GroupExpansion.TryGetValue(token, out var expanded))
                    {
                        caps.AddRange(expanded);
                    }
                    else if (IsSingleCap(token))
                    {
                        caps.Add(token);
                    }
                    // 其余片段（中文行为描述）不可键帽化：留在段内的已解析部分仍然成立
                }
                if (caps.Count > 0)
                {
                    groups.Add(new CapGroup([.. caps]));
                }
            }
        }
        return groups;
    }

    /// <summary>可作键帽的片段：单字符（字母/箭头）、已知键名（F1/Esc/Space/Enter）、修饰键名。</summary>
    public static bool IsSingleCap(string token) =>
        token.Length == 1
        || token is "F1" or "Esc" or "Space" or "Enter" or "Ctrl" or "Shift" or "Tab";
}
