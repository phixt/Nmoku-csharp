using Godot;

namespace Nmoku.Game;

/// <summary>
/// 应用级偏好存储（user://settings.cfg）：audio 节（音量/静音，AudioManager）与
/// general 节（窗口模式/语言，设置页）共享同一文件；写走「读全文再改」避免节间覆盖。
/// 与对局存档无关——这是设备/偏好配置（源项目 localStorage 的 app_lang 等同位物）。
/// </summary>
public static class SettingsStore
{
    private const string SettingsPath = "user://settings.cfg";
    private static ConfigFile? _cache;

    private static ConfigFile LoadOrGet()
    {
        if (_cache != null)
        {
            return _cache;
        }
        var cfg = new ConfigFile();
        cfg.Load(SettingsPath);
        _cache = cfg;
        return cfg;
    }

    public static Variant Get(string section, string key, Variant @default) =>
        LoadOrGet().GetValue(section, key, @default);

    public static void Set(string section, string key, Variant value)
    {
        var cfg = LoadOrGet();
        cfg.SetValue(section, key, value);
        cfg.Save(SettingsPath);
    }

    /// <summary>同节多键合并单次落盘（滑条拖动等场景避免 3 次读 + 3 次写）。</summary>
    public static void SetMany(string section, params (string Key, Variant Value)[] entries)
    {
        var cfg = LoadOrGet();
        foreach (var (key, value) in entries)
        {
            cfg.SetValue(section, key, value);
        }
        cfg.Save(SettingsPath);
    }
}
