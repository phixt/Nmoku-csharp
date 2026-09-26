# 一次性工具（两步）：
#   apply  —— Game 层 Text.* 渲染点批量包 I18n.T：
#             string.Format(Text.X.Y, → I18n.T(Text.X.Y, ；其余 → I18n.T(Text.X.Y)。
#             排除 System.Text.*（Encoding/Json 等）与注释行、已包行。
#   fixusings —— 给所有含 I18n. 的文件补 using Nmoku.Game;（Shell/* 多数缺）。
# 替换后必须 dotnet build + 逐文件目检 diff。
import re
import sys
from pathlib import Path

pattern = re.compile(r'string\.Format\((?<!System\.)((?:Text)\.\w+\.\w+),|(?<!System\.)\b(Text\.\w+\.\w+)\b')

def repl(m):
    if m.group(1):
        return f'I18n.T({m.group(1)},'
    return f'I18n.T({m.group(2)})'

def apply():
    changed = 0
    for path in Path('src/Nmoku.Game').rglob('*.cs'):
        if 'obj' in path.parts:
            continue
        out = []
        dirty = False
        for line in path.read_text(encoding='utf-8').splitlines():
            stripped = line.strip()
            if stripped.startswith('///') or stripped.startswith('//') or 'I18n.T(Text.' in line or 'Text.' not in line:
                out.append(line)
                continue
            code, sep, comment = line.partition(' //')
            new_code = pattern.sub(repl, code)
            out.append(new_code + sep + comment)
            if new_code != code:
                changed += 1
                dirty = True
        if dirty:
            path.write_text('\n'.join(out) + '\n', encoding='utf-8', newline='')
    print('lines changed:', changed)

def fix_usings():
    for path in Path('src/Nmoku.Game').rglob('*.cs'):
        if 'obj' in path.parts:
            continue
        src = path.read_text(encoding='utf-8')
        if 'I18n.T(' not in src or 'using Nmoku.Game;' in src:
            continue
        lines = src.splitlines()
        insert_at = next((i for i, l in enumerate(lines) if l.startswith('using ')), 0)
        # 插到 using 块尾（按字母序 Nmoku.Game 在 Nmoku.Game.* 之前）
        while insert_at < len(lines) and lines[insert_at].startswith('using '):
            insert_at += 1
        lines.insert(insert_at, 'using Nmoku.Game;')
        path.write_text('\n'.join(lines) + '\n', encoding='utf-8', newline='')
        print('using added:', path)

if __name__ == '__main__':
    {'apply': apply, 'fixusings': fix_usings}[sys.argv[1]]()
