namespace Nmoku.Core;

/// <summary>
/// 正式键盘物理排布数据（F1 页键盘模拟区唯一布局来源，2026-09-25 人类指示"参考正式键盘排布、
/// 不手动补位"）：字母三排标准错位（Q 排 0u / A 排 0.25u / Z 排 0.75u，ANSI 字母区近似）+
/// 空格条 + 方向键簇。视图按此数据生成键帽坐标，禁止在视图里手摆位置；布局防错由单测锁定
/// （KeymapTable 所有单键主键必须能在布局中找到）。
/// </summary>
public static class KeyboardLayout
{
    public sealed record PhysicalKey(string Name, double OffsetUnits, double WidthUnits = 1);

    public const double RowAdvance = 1.0; // 排距（单位：键帽宽）

    /// <summary>字母区三排（标准 QWERTY 顺序与错位；Esc 在 Q 排左端——对局/复盘/暂停三层共用）。</summary>
    public static readonly IReadOnlyList<IReadOnlyList<PhysicalKey>> LetterRows =
    [
        new PhysicalKey[] { new("Esc", -1.5) }
            .Concat("QWERTYUIOP".Select((c, i) => new PhysicalKey(c.ToString(), i)))
            .ToArray(),
        "ASDFGHJKL".Select((c, i) => new PhysicalKey(c.ToString(), 0.25 + i)).ToArray(),
        "ZXCVBNM".Select((c, i) => new PhysicalKey(c.ToString(), 0.75 + i)).ToArray(),
    ];

    /// <summary>布局水平范围（单元）：视图据此定宽，禁止手算。</summary>
    public static (double MinOffset, double MaxEnd) Extents =>
    (
        LetterRows.SelectMany(r => r).Min(k => k.OffsetUnits),
        LetterRows.SelectMany(r => r).Max(k => k.OffsetUnits + k.WidthUnits)
    );

    /// <summary>空格条 + 方向键（紧凑排布：倒 T 靠右缘，↑ 在上一排右端、←↓→ 与空格同排）。</summary>
    public static readonly PhysicalKey Space = new("Space", 2.5, 5);

    public static readonly IReadOnlyList<PhysicalKey> ArrowRow =
    [
        new("←", 7.75), new("↓", 8.75), new("→", 9.75),
    ];
    public static readonly PhysicalKey ArrowUp = new("↑", 8.75);

    /// <summary>整幅布局总行数（字母三排 + 空格/方向键排）。</summary>
    public const int TotalRows = 4;

    /// <summary>按键名查找物理位置；找不到返回 null（调用方决定是否渲染）。</summary>
    public static (int Row, PhysicalKey Key)? Find(string name)
    {
        for (int row = 0; row < LetterRows.Count; row++)
        {
            foreach (var key in LetterRows[row])
            {
                if (key.Name == name)
                {
                    return (row, key);
                }
            }
        }
        if (name == Space.Name)
        {
            return (3, Space);
        }
        if (name == ArrowUp.Name)
        {
            return (2, ArrowUp);
        }
        foreach (var key in ArrowRow)
        {
            if (key.Name == name)
            {
                return (3, key);
            }
        }
        return null;
    }

    /// <summary>全部已登记键名（防重与遍历用）。</summary>
    public static IEnumerable<string> AllNames =>
        LetterRows.SelectMany(r => r).Select(k => k.Name)
            .Append(Space.Name).Append(ArrowUp.Name)
            .Concat(ArrowRow.Select(k => k.Name));
}
