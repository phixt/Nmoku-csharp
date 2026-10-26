using Godot;
using Nmoku.Core;

namespace Nmoku.Game;

/// <summary>
/// 键位一致性审计（自测用）：KeymapTable 的**展示键** ↔ Godot InputMap 的**实际绑定**。
/// <para>
/// 键位表是 F1 帮助屏 / 菜单键位提示 / docs 的**显示源**，InputMap 是**行为源**——
/// 两者漂移即「帮助屏广告的键按了没反应」（M4 键位漂移类缺陷的根）。此前这条一致性
/// 只靠人工核对（2026-10-26 审查确认当时零漂移），现由无头自测常态守住。
/// </para>
/// <para>
/// 键名解析只认 Godot <see cref="Key"/> 枚举本身（不手抄键码表），展示串里的无法解析项
/// 一律报为不一致——宁可误报也不静默放过「键名写错导致展示与绑定对不上」。
/// </para>
/// </summary>
public static class KeymapAudit
{
    /// <summary>键位组合：物理键 + 修饰键。</summary>
    private readonly record struct Combo(Key Key, bool Ctrl, bool Shift, bool Alt)
    {
        public override string ToString() =>
            (Ctrl ? "Ctrl+" : "") + (Shift ? "Shift+" : "") + (Alt ? "Alt+" : "") + Key;
    }

    /// <summary>不一致项（空 = 键位表与 InputMap 一致）。</summary>
    public static IReadOnlyList<string> FindMismatches()
    {
        var issues = new List<string>();
        foreach (var entry in KeymapTable.Entries)
        {
            if (entry.Actions.All(a => a.StartsWith('@')))
            {
                continue; // 纯文档行（鼠标操作），无 InputMap 绑定可比
            }
            List<Combo> declared = ParseDisplayKeys(entry, issues);
            HashSet<Combo> actual = ActualCombos(entry.Actions);
            string who = string.Join('/', entry.Actions);
            foreach (Combo combo in declared)
            {
                if (!actual.Contains(combo))
                {
                    issues.Add($"{entry.Category}: 展示键 [{combo}] 在 InputMap 无对应绑定（{who}）");
                }
            }
            foreach (Combo combo in actual.Except(declared))
            {
                issues.Add($"{entry.Category}: InputMap 绑定 [{combo}] 未在键位表展示（{who}）");
            }
        }
        return issues;
    }

    /// <summary>展示串 → 键位组合（'/'、'或'、'、' 为分隔符；'Ctrl+'/'Shift+'/'Alt+' 为修饰键前缀）。</summary>
    private static List<Combo> ParseDisplayKeys(KeymapEntry entry, List<string> issues)
    {
        var combos = new List<Combo>();
        foreach (string token in entry.Keys.Split('/', '或', '、'))
        {
            string t = token.Trim();
            if (t.Length == 0)
            {
                continue;
            }
            bool ctrl = false, shift = false, alt = false;
            while (true)
            {
                if (t.StartsWith("Ctrl+", StringComparison.OrdinalIgnoreCase))
                {
                    ctrl = true;
                    t = t[5..];
                }
                else if (t.StartsWith("Shift+", StringComparison.OrdinalIgnoreCase))
                {
                    shift = true;
                    t = t[6..];
                }
                else if (t.StartsWith("Alt+", StringComparison.OrdinalIgnoreCase))
                {
                    alt = true;
                    t = t[4..];
                }
                else
                {
                    break;
                }
            }
            if (!TryResolveKeys(t, out List<Key> keys))
            {
                issues.Add($"{entry.Category}: 键位表里的键名「{t}」无法解析（展示与实际绑定无法比对）");
                continue;
            }
            combos.AddRange(keys.Select(k => new Combo(k, ctrl, shift, alt)));
        }
        return combos;
    }

    /// <summary>展示用键名 → Godot 键（认不出即 false——不猜）。</summary>
    private static bool TryResolveKeys(string token, out List<Key> keys)
    {
        keys = [];
        switch (token)
        {
            case "Esc" or "Escape":
                keys.Add(Key.Escape);
                return true;
            case "Space" or "空格":
                keys.Add(Key.Space);
                return true;
            case "Enter" or "回车":
                keys.Add(Key.Enter);
                return true;
            case "Tab":
                keys.Add(Key.Tab);
                return true;
            case "Backspace":
                keys.Add(Key.Backspace);
                return true;
            case "←" or "Left":
                keys.Add(Key.Left);
                return true;
            case "→" or "Right":
                keys.Add(Key.Right);
                return true;
            case "↑" or "Up":
                keys.Add(Key.Up);
                return true;
            case "↓" or "Down":
                keys.Add(Key.Down);
                return true;
            case "方向键":
                keys.AddRange([Key.Left, Key.Up, Key.Right, Key.Down]);
                return true;
            case "WASD":
                keys.AddRange([Key.W, Key.A, Key.S, Key.D]);
                return true;
        }
        if (token.Length == 1 && char.IsAsciiLetterUpper(token[0]))
        {
            keys.Add((Key)token[0]); // A..Z 的 Godot 键码即 ASCII 码
            return true;
        }
        Key? fKey = token switch
        {
            "F1" => Key.F1,
            "F2" => Key.F2,
            "F3" => Key.F3,
            "F4" => Key.F4,
            "F5" => Key.F5,
            "F6" => Key.F6,
            "F7" => Key.F7,
            "F8" => Key.F8,
            "F9" => Key.F9,
            "F10" => Key.F10,
            "F11" => Key.F11,
            "F12" => Key.F12,
            _ => null,
        };
        if (fKey is { } f)
        {
            keys.Add(f);
            return true;
        }
        return false;
    }

    /// <summary>InputMap 里这些 action 的实际绑定（物理键码优先；内置 action 只登记 keycode）。</summary>
    private static HashSet<Combo> ActualCombos(IEnumerable<string> actions)
    {
        var set = new HashSet<Combo>();
        foreach (string action in actions)
        {
            if (!InputMap.HasAction(action))
            {
                continue;
            }
            foreach (InputEvent ev in InputMap.ActionGetEvents(action))
            {
                if (ev is not InputEventKey key)
                {
                    continue;
                }
                Key physical = key.PhysicalKeycode != Key.None ? key.PhysicalKeycode : key.Keycode;
                if (physical == Key.None)
                {
                    continue;
                }
                set.Add(new Combo(physical, key.CtrlPressed, key.ShiftPressed, key.AltPressed));
            }
        }
        return set;
    }
}
