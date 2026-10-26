namespace Nmoku.Core.Tests;

/// <summary>
/// Godot 工程配置读取（键位护栏的单一事实源）。
/// <para>
/// 定位方式：csproj **显式声明 content**（复制到测试输出目录），测试从
/// <see cref="AppContext.BaseDirectory"/> 读取——不用「向上找 Nmoku.slnx」的仓库布局耦合
/// （审查报告 P1 #33：那种定位让测试离开 checkout 即 302 PASS / 2 FAIL）。
/// </para>
/// </summary>
internal static class ProjectGodot
{
    /// <summary>测试输出目录中的 project.godot（csproj 声明的 content 副本）。</summary>
    public static string FilePath => Path.Combine(AppContext.BaseDirectory, "project.godot");

    public static string ReadText() => File.ReadAllText(FilePath);

    /// <summary>
    /// [input] 节登记的全部 action 名（InputMap 的物理事实源）。
    /// 解析规则：节内形如 <c>name={</c> 的行即一个 action（Godot 配置文件的 action 块语法）。
    /// </summary>
    public static HashSet<string> InputMapActions()
    {
        var actions = new HashSet<string>(StringComparer.Ordinal);
        bool inInput = false;
        foreach (string raw in ReadText().Split('\n'))
        {
            string line = raw.Trim();
            if (line.StartsWith('['))
            {
                inInput = line == "[input]";
                continue;
            }
            if (!inInput || !line.EndsWith('{'))
            {
                continue;
            }
            int eq = line.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }
            string name = line[..eq].Trim();
            if (name.Length > 0)
            {
                actions.Add(name);
            }
        }
        return actions;
    }
}
