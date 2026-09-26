# 一次性工具：特殊渲染点 i18n 化（批量脚本覆盖不到的散点硬编码中文）。
import io


def patch(path, pairs):
    src = open(path, encoding='utf-8').read()
    for old, new in pairs:
        if old not in src:
            print(f'MISS {path}: {old[:50]!r}')
            continue
        src = src.replace(old, new)
    open(path, 'w', encoding='utf-8', newline='').write(src)
    print('patched', path)


patch('src/Nmoku.Game/Shell/ReplayBar.cs', [
    ('MakeButton("上一步", ', 'MakeButton(I18n.T(Text.Replay.PrevStep), '),
    ('MakeButton("下一步", ', 'MakeButton(I18n.T(Text.Replay.NextStep), '),
])
patch('src/Nmoku.Game/Shell/KeymapScreen.cs', [
    ('Text = "快捷键说明（F1）"', 'Text = I18n.T(Text.Guide.KeymapTitle)'),
])
patch('src/Nmoku.Game/Shell/KeymapContent.cs', [
    ('Text = KeycapScopeMap.DisplayName(PageOrder[_page]) + "层"',
     'Text = I18n.T(Text.Guide.LayerPageFormat, I18n.T(KeycapScopeMap.DisplayName(PageOrder[_page])))'),
    ('Text = entry.Description,', 'Text = I18n.T(entry.Description),'),
    ('keys.TryAdd(cap, entry.Short ?? "");', 'keys.TryAdd(cap, I18n.T(entry.Short ?? ""));'),
    ('var label = new Label { Text = KeycapScopeMap.DisplayName(scope) };',
     'var label = new Label { Text = I18n.T(KeycapScopeMap.DisplayName(scope)) };'),
])
patch('src/Nmoku.Game/Shell/SandboxPanel.cs', [
    ('_preset.AddItem(preset.Name);', '_preset.AddItem(I18n.T(preset.Name));'),
])
patch('src/Nmoku.Game/Main.cs', [
    ('Filters = ["*.nzsave ; Nmoku 存档文件"],', 'Filters = ["*.nzsave ; " + I18n.T(Text.Save.SaveFileFilter)],'),
])
