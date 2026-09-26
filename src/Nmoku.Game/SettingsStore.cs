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

    public static Variant Get(string section, string key, Variant @default)
    {
        var cfg = new ConfigFile();
        cfg.Load(SettingsPath);
        return cfg.GetValue(section, key, @default);
    }

    public static void Set(string section, string key, Variant value)
    {
        var cfg = new ConfigFile();
        cfg.Load(SettingsPath);
        cfg.SetValue(section, key, value);
        cfg.Save(SettingsPath);
    }
}
