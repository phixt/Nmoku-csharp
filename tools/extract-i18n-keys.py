# 一次性工具：提取 i18n 键集（Text 常量值 + KeymapTable 文本 + KeycapScope 图例），
# 生成 CSV 草稿（zh-CN 列 = key 本身），翻译列由 agent 填写后入库。
# 用法：python tools/extract-i18n-keys.py [--emit]
import re
import sys

keys = []

src = open('src/Nmoku.Core/TextConstants.cs', encoding='utf-8').read()
pattern = re.compile(r'=\s*"((?:[^"\\]|\\.)*)"')
for m in pattern.finditer(src):
    v = m.group(1).replace('\\n', '\n').replace('\\"', '"')
    keys.append(v)

src = open('src/Nmoku.Core/KeymapTable.cs', encoding='utf-8').read()
for field in ('Description', 'Short', 'Category'):
    for m in re.finditer(field + r'\s*=\s*"([^"]+)"', src):
        keys.append(m.group(1))

src = open('src/Nmoku.Core/KeycapScope.cs', encoding='utf-8').read()
for m in re.finditer(r'=>\s*"([^"]*"?\s*[一-龥][^"]*)"', src):
    keys.append(m.group(1))

# Game 层散点 UI 文本（已确认无 Text 常量承载的）
keys += ['上一步', '下一步', '快捷键说明（F1）', '{0} 层', '层']

seen, out = set(), []
for k in keys:
    if k not in seen:
        seen.add(k)
        out.append(k)

print(f'总键数: {len(out)}')
for k in out:
    print(repr(k))

if '--emit' in sys.argv:
    import csv
    with open('assets/i18n/translations.csv', 'w', encoding='utf-8', newline='') as f:
        w = csv.writer(f)
        w.writerow(['keys', 'zh-Hant', 'en-US', 'ja-JP'])
        for k in out:
            w.writerow([k, '', '', ''])
    print('草稿已写出 assets/i18n/translations.csv')
