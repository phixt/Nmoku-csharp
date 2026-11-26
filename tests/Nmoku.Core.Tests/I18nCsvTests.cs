using System.Reflection;
using System.Text;
using Nmoku.Core;

namespace Nmoku.Core.Tests;

/// <summary>
/// i18n 翻译表完备性（M5 §3.5）：translations.csv 的键=zh-CN 文本本身，必须覆盖全部
/// 用户可见文本——Text 常量全集（反射枚举，含 const 拼接的 Guide.Body 完整值）、
/// KeymapTable 的 Description/Short/Category、KeycapScope 图例名。四语言列键集一致。
/// 测试在 Core.Tests 而测的是 Game 的数据文件：键的权威来源是 Core 的 Text 常量，
/// 文件按相对仓库根定位（.gitattributes 已锁仓库内 LF，键的换行与 C# 常量逐字符一致）。
/// </summary>
public class I18nCsvTests
{
    private static readonly string[] LocaleColumns = ["zh-CN", "zh-Hant", "en-US", "ja-JP"];

    private static string FindCsvPath() =>
        // 数据文件由 csproj 显式声明 content（复制到测试输出目录）→ 不依赖仓库布局，
        // 也不必「向上找 Nmoku.slnx」（审查报告 P1 #33：那种定位离开 checkout 即 302 PASS / 2 FAIL）
        Path.Combine(AppContext.BaseDirectory, "translations.csv");

    /// <summary>单遍 RFC4180 状态机解析（引号内的逗号/换行不拆分；仓库锁 LF，无 \r）。</summary>
    private static List<string[]> ReadCsv()
    {
        string text = File.ReadAllText(FindCsvPath());
        var rows = new List<string[]>();
        var row = new List<string>();
        var field = new StringBuilder();
        bool inQuotes = false;
        foreach (char c in text)
        {
            if (inQuotes)
            {
                if (c == '"')
                {
                    inQuotes = false;
                }
                else
                {
                    field.Append(c);
                }
            }
            else if (c == '"')
            {
                inQuotes = true;
            }
            else if (c == ',')
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if (c == '\n')
            {
                row.Add(field.ToString());
                field.Clear();
                rows.Add([.. row]);
                row.Clear();
            }
            else if (c != '\r')
            {
                field.Append(c);
            }
        }
        Assert.False(inQuotes, "CSV 引号未闭合");
        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add([.. row]);
        }
        Assert.True(rows.Count > 100, $"CSV 行数异常: {rows.Count}");
        return rows;
    }

    private static HashSet<string> ExpectedKeys()
    {
        HashSet<string> keys = [];
        foreach (Type nested in typeof(Text).GetNestedTypes(BindingFlags.Public | BindingFlags.Static))
        {
            foreach (FieldInfo field in nested.GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field.FieldType == typeof(string) && field.GetValue(null) is string value)
                {
                    keys.Add(value);
                }
            }
        }
        foreach (KeymapEntry entry in KeymapTable.Entries)
        {
            keys.Add(entry.Description);
            if (entry.Short != null)
            {
                keys.Add(entry.Short);
            }
            keys.Add(entry.Category);
        }
        foreach (KeycapScope scope in Enum.GetValues<KeycapScope>())
        {
            keys.Add(KeycapScopeMap.DisplayName(scope));
        }
        return keys;
    }

    [Fact]
    public void Csv_CoversAllUserVisibleText_WithAlignedLocaleColumns()
    {
        List<string[]> rows = ReadCsv();
        Assert.Equal("keys", rows[0][0]);
        Assert.Equal(LocaleColumns, rows[0].Skip(1).ToArray());

        HashSet<string> csvKeys = [];
        foreach (string[] row in rows.Skip(1))
        {
            Assert.True(row.Length == 5, $"列数异常: {row[0]}");
            Assert.True(row.All(f => f.Length > 0), $"空值: {row[0]}");
            Assert.True(csvKeys.Add(row[0]), $"重复键: {row[0]}");
        }

        HashSet<string> expected = ExpectedKeys();
        List<string> missing = expected.Where(k => !csvKeys.Contains(k)).ToList();
        Assert.True(missing.Count == 0, $"CSV 缺键: {string.Join(" | ", missing.Take(10))}");
    }

    [Fact]
    public void Csv_Keys_AreZhSourceValues()
    {
        // 键=zh-CN 文本本身（gettext 消息风格）：排除点分小写伪键（如 save.quicksaved）；
        // SWAP2 等纯 ASCII 术语为合法键
        var pseudoKey = new System.Text.RegularExpressions.Regex(@"^[a-z0-9-]+(\.[a-z0-9-]+)+$");
        foreach (string[] row in ReadCsv().Skip(1))
        {
            Assert.False(pseudoKey.IsMatch(row[0]), $"键应为 zh-CN 文本: {row[0]}");
        }
    }

    [Fact]
    public void Csv_FormatPlaceholders_MatchAcrossAllLocales()
    {
        // 防运行期 FormatException：四语言译文的 {0}/{1}/... 占位符索引集合必须与源键逐一对齐
        var placeholder = new System.Text.RegularExpressions.Regex(@"\{(\d+)\}");
        static string Signature(string s, System.Text.RegularExpressions.Regex rx) =>
            string.Join(",", rx.Matches(s).Select(m => m.Groups[1].Value).Distinct().OrderBy(x => x));

        foreach (string[] row in ReadCsv().Skip(1))
        {
            string expected = Signature(row[0], placeholder);
            for (int col = 1; col < row.Length; col++)
            {
                string actual = Signature(row[col], placeholder);
                Assert.True(expected == actual,
                    $"占位符失配 [{LocaleColumns[col - 1]}] 键={row[0]} 期望={{{expected}}} 实际={{{actual}}}");
            }
        }
    }
}
