using Godot;

namespace Nmoku.Game;

/// <summary>
/// i18n 查找包装（§3.5）：以 zh-CN 文本本身为翻译键（gettext 消息风格）——Core 的 Text 常量
/// 值即键、零改动（LevelCatalog.Name / SwapEngine.Message / KeymapTable 等数据层引用处原样流动，
/// 在渲染点包 T）。四语言表 assets/i18n/translations.csv（zh-Hant/en-US/ja-JP；zh-CN 列不注册，
/// 查不到翻译时 TranslationServer 返回消息原文=zh-CN，缺 key 回落语义与源 I18n.t 一致，B20 ④）。
/// {0} 占位与源相同（string.Format）。
/// </summary>
public static class I18n
{
    public static string T(string zh, params object?[] args)
    {
        string text = TranslationServer.Translate(zh);
        return args.Length == 0 ? text : string.Format(text, args);
    }
}
