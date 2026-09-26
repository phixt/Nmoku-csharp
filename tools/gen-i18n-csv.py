# 一次性工具：生成 src/Nmoku.Game/assets/i18n/translations.csv（四语言翻译表）。
# 键 = zh-CN 文本本身（gettext 消息风格，与 Core Text 常量值一致）；zh-CN 列不注册
# （TranslationServer 查不到→返回消息原文=zh-CN，B20 ④）。重跑即全量再生成。
# 用法：python tools/gen-i18n-csv.py
import csv
from pathlib import Path

G = "對局"  # 仅示意

ROWS = [
    # ---- Swap / Turn / Result ----
    ("白方选择：交换黑白，还是继续执白？", "白方選擇：交換黑白，還是繼續執白？",
     "White to choose: swap colors, or keep playing White?", "白の選択：黒白を交換するか、白のまま続けるか？"),
    ("黑方行棋", "黑方行棋", "Black to move", "黒の番"),
    ("白方行棋", "白方行棋", "White to move", "白の番"),
    ("黑方获胜", "黑方獲勝", "Black wins", "黒の勝ち"),
    ("白方获胜", "白方獲勝", "White wins", "白の勝ち"),
    ("平局", "平局", "Draw", "引き分け"),
    ("共 {0} 手", "共 {0} 手", "{0} moves in total", "全 {0} 手"),
    # ---- Save ----
    ("已快存", "已快存", "Quick saved", "クイック保存しました"),
    ("存档无效或已损坏", "存檔無效或已損壞", "Save file is invalid or corrupted",
     "セーブデータが無効または破損しています"),
    ("暂无存档", "暫無存檔", "No saves yet", "セーブデータがありません"),
    ("暂无快存", "暫無快存", "No quick save yet", "クイックセーブがありません"),
    ("存档管理", "存檔管理", "Save Manager", "セーブ管理"),
    ("删除存档", "刪除存檔", "Delete Save", "セーブを削除"),
    ("槽位 {0}", "槽位 {0}", "Slot {0}", "スロット {0}"),
    ("槽位 {0}（快存）", "槽位 {0}（快存）", "Slot {0} (Quick)", "スロット {0}（クイック）"),
    ("空", "空", "Empty", "空"),
    ("{0}×{1}×{2} · {3} 手 · {4}", "{0}×{1}×{2} · {3} 手 · {4}",
     "{0}×{1}×{2} · {3} moves · {4}", "{0}×{1}×{2} · {3} 手 · {4}"),
    ("已结束", "已結束", "Finished", "終了"),
    ("进行中", "進行中", "In progress", "進行中"),
    ("读取", "讀取", "Load", "読み込む"),
    ("填写", "填寫", "Paste", "貼り付け"),
    ("覆盖", "覆蓋", "Overwrite", "上書き"),
    ("覆盖存档", "覆蓋存檔", "Overwrite Save", "セーブを上書き"),
    ("确认用剪贴板中的存档覆盖槽位 {0}？", "確認用剪貼簿中的存檔覆蓋槽位 {0}？",
     "Overwrite slot {0} with the save in the clipboard?",
     "クリップボードのセーブでスロット {0} を上書きしますか？"),
    ("复制", "複製", "Copy", "コピー"),
    ("已复制到剪贴板", "已複製到剪貼簿", "Copied to clipboard", "クリップボードにコピーしました"),
    ("导入", "匯入", "Import", "インポート"),
    ("剪贴板中无有效存档", "剪貼簿中無有效存檔", "No valid save in clipboard",
     "クリップボードに有効なセーブがありません"),
    ("文件中无有效存档", "檔案中無有效存檔", "No valid save in file", "ファイルに有効なセーブがありません"),
    ("已导入到槽位 {0}", "已匯入到槽位 {0}", "Imported to slot {0}", "スロット {0} にインポートしました"),
    ("导出", "匯出", "Export", "エクスポート"),
    ("已存为存档文件", "已存為存檔檔案", "Saved to save file", "セーブファイルに保存しました"),
    ("存档文件写入失败", "存檔檔案寫入失敗", "Failed to write save file", "セーブファイルの書き込みに失敗しました"),
    ("删除", "刪除", "Delete", "削除"),
    ("确定删除槽位 {0} 的存档？", "確定刪除槽位 {0} 的存檔？", "Delete the save in slot {0}?",
     "スロット {0} のセーブを削除しますか？"),
    ("该存档无法读取", "該存檔無法讀取", "This save cannot be loaded", "このセーブは読み込めません"),
    ("已保存到存档 {0}", "已保存到存檔 {0}", "Saved to slot {0}", "スロット {0} に保存しました"),
    ("保存当前对局：Ctrl+S 写快存 · S 存入首个空槽 · Ctrl+L 读快存",
     "儲存目前對局：Ctrl+S 寫快存 · S 存入首個空槽 · Ctrl+L 讀快存",
     "Save current game: Ctrl+S quick save · S first empty slot · Ctrl+L load quick save",
     "対局を保存：Ctrl+S クイック保存 · S 先頭の空きスロット · Ctrl+L クイック読み込み"),
    ("Nmoku 存档文件", "Nmoku 存檔檔案", "Nmoku save file", "Nmoku セーブファイル"),
    # ---- Title ----
    ("立体五子棋", "立體五子棋", "Nmoku 3D Gomoku", "立体五目並べ"),
    ("开始游戏", "開始遊戲", "New Game", "ゲーム開始"),
    ("继续对局", "繼續對局", "Resume Game", "対局を続ける"),
    ("自定义对局", "自訂對局", "Custom Game", "カスタム対局"),
    ("读档", "讀檔", "Load Save", "ロード"),
    ("快捷键说明", "快捷鍵說明", "Key Bindings", "ショートカット一覧"),
    ("退出", "退出", "Quit", "終了"),
    ("13×13×6 三维连珠 · Nmoku", "13×13×6 三維連珠 · Nmoku", "13×13×6 3D Renju · Nmoku",
     "13×13×6 立体連珠 · Nmoku"),
    ("当前有进行中的对局，开始新局将覆盖保留现场。确定？", "目前有進行中的對局，開始新局將覆蓋保留現場。確定？",
     "A game is in progress. Starting a new one will discard it. Continue?",
     "進行中の対局があります。新規開始すると破棄されます。よろしいですか？"),
    # ---- Menu ----
    ("已暂停", "已暫停", "Paused", "一時停止"),
    ("继续游戏", "繼續遊戲", "Resume", "続ける"),
    ("重新开始", "重新開始", "Restart", "再開始"),
    ("保存进度", "儲存進度", "Save Progress", "進行を保存"),
    ("加载进度", "載入進度", "Load Progress", "進行を読み込む"),
    ("返回标题", "返回標題", "Back to Title", "タイトルへ戻る"),
    ("确定重新开始？当前对局进度将丢失。", "確定重新開始？目前對局進度將丟失。",
     "Restart? Current game progress will be lost.", "再開始しますか？現在の進行は失われます。"),
    # ---- Replay ----
    ("复盘", "復盤", "Replay", "復盤"),
    ("退出复盘", "退出復盤", "Exit Replay", "復盤を終了"),
    ("{0} / {1}", "{0} / {1}", "{0} / {1}", "{0} / {1}"),
    ("上一步", "上一步", "Prev", "前へ"),
    ("下一步", "下一步", "Next", "次へ"),
    # ---- Guide ----
    ("游戏引导", "遊戲引導", "Game Guide", "ゲームガイド"),
    ("在三维棋盘的任意层连成 N 子直线即获胜：同层横竖斜、跨层斜线（13 个方向）均可。\n"
     "标题屏可进入自定义对局：选预设关卡或自定尺寸（奇数 ≥5）、层数、连珠数（4–9）、规则与 AI 难度。\n"
     "对局中：A/D 循环换层，F 镜像，H 辅助线，V 色弱，O 网格全亮；Ctrl+Z 悔棋，Ctrl+S 快存，L 读最新档。\n"
     "Q/E 旋转视角（按一下 90°），W/S 或方向键缩放，Shift+WASD 平移，Z/C 调层间距，X 复位。\n"
     "存档管理支持剪贴板与 .nzsave 文件两种导入导出；T 切换明暗主题；按 F1 查看全部快捷键。",
     "在三維棋盤的任意層連成 N 子直線即獲勝：同層橫豎斜、跨層斜線（13 個方向）均可。\n"
     "標題屏可進入自訂對局：選預設關卡或自訂尺寸（奇數 ≥5）、層數、連珠數（4–9）、規則與 AI 難度。\n"
     "對局中：A/D 循環換層，F 鏡像，H 輔助線，V 色弱，O 網格全亮；Ctrl+Z 悔棋，Ctrl+S 快存，L 讀最新檔。\n"
     "Q/E 旋轉視角（按一下 90°），W/S 或方向鍵縮放，Shift+WASD 平移，Z/C 調層間距，X 復位。\n"
     "存檔管理支援剪貼簿與 .nzsave 檔案兩種匯入匯出；T 切換明暗主題；按 F1 查看全部快捷鍵。",
     "Connect N stones in a straight line on any layer of the 3D board to win: rows, columns and diagonals "
     "within a layer, plus cross-layer diagonals (13 directions in total).\n"
     "Custom games are available from the title screen: preset levels or custom size (odd ≥5), layers, "
     "win length (4–9), rules and AI difficulty.\n"
     "In game: A/D cycle layers, F mirror, H helper lines, V colorblind mode, O grid bright; Ctrl+Z undo, "
     "Ctrl+S quick save, L load latest.\n"
     "Q/E rotate view (90° per press), W/S or arrow keys zoom, Shift+WASD pan, Z/C layer spacing, X reset.\n"
     "Save manager supports clipboard and .nzsave file import/export; T toggles light/dark theme; "
     "press F1 for all key bindings.",
     "3D 盤面の任意の層で N 子の直線を繋げば勝利：同層の縦横斜め、層を跨ぐ斜線（13 方向）が有効。\n"
     "タイトル画面からカスタム対局：プリセットまたは自作サイズ（奇数 ≥5）、層数、連珠数（4–9）、"
     "ルールと AI 強度を選択。\n"
     "対局中：A/D 層を循環、F ミラー、H 補助線、V 色覚サポート、O グリッド全表示；Ctrl+Z 待った、"
     "Ctrl+S クイック保存、L 最新を読み込み。\n"
     "Q/E 視点回転（1 回 90°）、W/S または矢印キーでズーム、Shift+WASD パン、Z/C 層間隔、X リセット。\n"
     "セーブ管理はクリップボードと .nzsave ファイルの両方に対応；T でテーマ切替；F1 で全ショートカット表示。"),
    ("回到对局", "回到對局", "Back to Game", "対局に戻る"),
    ("返回", "返回", "Back", "戻る"),
    ("快捷键说明（F1）", "快捷鍵說明（F1）", "Key Bindings (F1)", "ショートカット一覧（F1）"),
    ("{0} 层", "{0} 層", "{0} layer", "{0} レイヤー"),
    # ---- Overlay ----
    ("确认", "確認", "OK", "OK"),
    ("取消", "取消", "Cancel", "キャンセル"),
    ("交换黑白", "交換黑白", "Swap Colors", "黒白を交換"),
    ("继续执白", "繼續執白", "Keep White", "白のまま続ける"),
    # ---- Settings ----
    ("设置", "設定", "Settings", "設定"),
    ("音效音量", "音效音量", "SFX Volume", "効果音の音量"),
    ("音乐音量", "音樂音量", "Music Volume", "音楽の音量"),
    ("静音", "靜音", "Mute", "ミュート"),
    ("静音：开", "靜音：開", "Mute: On", "ミュート：オン"),
    ("静音：关", "靜音：關", "Mute: Off", "ミュート：オフ"),
    ("静音已开启", "靜音已開啟", "Muted", "ミュートしました"),
    ("静音已关闭", "靜音已關閉", "Sound on", "ミュート解除"),
    ("窗口模式", "視窗模式", "Window Mode", "ウィンドウモード"),
    ("窗口化", "視窗化", "Windowed", "ウィンドウ"),
    ("最大化", "最大化", "Maximized", "最大化"),
    ("全屏", "全螢幕", "Fullscreen", "フルスクリーン"),
    ("语言", "語言", "Language", "言語"),
    # ---- Levels ----
    ("标准对局", "標準對局", "Standard Game", "標準対局"),
    ("教学·平面", "教學·平面", "Tutorial · Flat", "チュートリアル·平面"),
    ("残局·预置子", "殘局·預置子", "Endgame · Preset Stones", "終盤·配置済みの石"),
    ("命令行参数局", "命令行參數局", "Command-line Game", "コマンドライン対局"),
    ("预设", "預設", "Preset", "プリセット"),
    ("棋盘边长（奇数 ≥5）", "棋盤邊長（奇數 ≥5）", "Board size (odd ≥5)", "盤面の一辺の長さ（奇数 ≥5）"),
    ("层数", "層數", "Layers", "層数"),
    ("连珠数（4–9）", "連珠數（4–9）", "Win length (4–9)", "連珠数（4–9）"),
    ("规则", "規則", "Rules", "ルール"),
    ("模式", "模式", "Mode", "モード"),
    ("人机", "人機", "vs AI", "対CPU"),
    ("双人", "雙人", "2P Local", "対人"),
    ("AI 难度", "AI 難度", "AI Difficulty", "AI 強度"),
    ("开始对局", "開始對局", "Start Game", "対局開始"),
    ("棋盘边长必须为 ≥5 的奇数", "棋盤邊長必須為 ≥5 的奇數", "Board size must be an odd number ≥5",
     "盤面の一辺は ≥5 の奇数にしてください"),
    ("无禁手", "無禁手", "Free-style", "禁手なし"),
    ("SWAP2", "SWAP2", "SWAP2", "SWAP2"),
    ("简单", "簡單", "Easy", "初級"),
    ("中等", "中等", "Medium", "中級"),
    ("困难", "困難", "Hard", "上級"),
    # ---- KeymapTable：Description ----
    ("快捷键帮助屏", "快捷鍵幫助屏", "Key bindings help screen", "ショートカットヘルプ"),
    ("明暗主题切换", "明暗主題切換", "Light/dark theme toggle", "テーマ（明暗）切替"),
    ("静音开关", "靜音開關", "Mute toggle", "ミュート切替"),
    ("开始新对局（进行中的保留局会先确认）", "開始新對局（進行中的保留局會先確認）",
     "Start a new game (a live game is kept after confirmation)",
     "新しい対局を開始（進行中は確認あり）"),
    ("读最新存档", "讀最新存檔", "Load latest save", "最新のセーブを読み込む"),
    ("开始游戏 / 回到对局（按入口）", "開始遊戲 / 回到對局（按入口）",
     "Start game / back to game (by entry)", "ゲーム開始 / 対局に戻る（入口別）"),
    ("返回标题 / 回到对局（按入口）", "返回標題 / 回到對局（按入口）",
     "Back to title / back to game (by entry)", "タイトルへ / 対局に戻る（入口別）"),
    ("打开暂停菜单", "打開暫停選單", "Open pause menu", "ポーズメニューを開く"),
    ("快存到快存槽（Ctrl+L 读回）", "快存到快存槽（Ctrl+L 讀回）",
     "Quick save to quick slot (Ctrl+L to load back)", "クイックスロットに保存（Ctrl+L で読み戻し）"),
    ("悔棋（PvE 自动回到人类回合）", "悔棋（PvE 自動回到人類回合）",
     "Undo (in PvE returns to the human turn)", "待った（対CPU では人間の番へ戻る）"),
    ("循环切换焦点层", "循環切換焦點層", "Cycle focus layer", "フォーカス層を循環切替"),
    ("2D 面板水平镜像", "2D 面板水平鏡像", "Mirror 2D panel horizontally", "2D パネルを左右反転"),
    ("辅助线开关", "輔助線開關", "Helper lines toggle", "補助線の切替"),
    ("色弱配色", "色弱配色", "Colorblind palette", "色覚サポート配色"),
    ("落子（悬停显示辅助线）", "落子（懸停顯示輔助線）", "Place stone (hover shows helper lines)",
     "着子（ホバーで補助線表示）"),
    ("±90° 旋转", "±90° 旋轉", "Rotate ±90°", "±90° 回転"),
    ("缩放", "縮放", "Zoom", "ズーム"),
    ("无级旋转 / 视角平移", "無級旋轉 / 視角平移", "Free rotation / view panning", "無段回転 / 視点パン"),
    ("径向缩放", "徑向縮放", "Radial zoom", "半径方向ズーム"),
    ("视角平移", "視角平移", "Pan view", "視点パン"),
    ("层间距压缩 / 拉大", "層間距壓縮 / 拉大", "Compress / expand layer spacing", "層間隔を狭める / 広げる"),
    ("层间距复位", "層間距復位", "Reset layer spacing", "層間隔をリセット"),
    ("网格全亮开关", "網格全亮開關", "Grid all-bright toggle", "グリッド全表示切替"),
    ("步进出 / 入", "步進出 / 入", "Step back / forward", "1 手戻る / 進む"),
    ("退出复盘（回标题，棋局清理）", "退出復盤（回標題，棋局清理）",
     "Exit replay (to title, board cleared)", "復盤を終了（タイトルへ、盤面クリア）"),
    ("重新开始（确认框）", "重新開始（確認框）", "Restart (confirmation dialog)", "再開始（確認ダイアログ）"),
    ("保存进度（首个空槽，满则快存槽）", "儲存進度（首個空槽，滿則快存槽）",
     "Save progress (first empty slot, quick slot when full)",
     "進行を保存（先頭の空きスロット、満杯時はクイック）"),
    ("读取快存槽（恢复检查点，对局中 Esc 后可用）", "讀取快存槽（恢復檢查點，對局中 Esc 後可用）",
     "Load quick slot (restore checkpoint; available after Esc in game)",
     "クイックスロットを読み込む（対局中は Esc 後に利用可）"),
    ("返回标题（对局保留现场）", "返回標題（對局保留現場）",
     "Back to title (game state kept)", "タイトルへ戻る（対局は保持）"),
    ("打开引导", "打開引導", "Open guide", "ガイドを開く"),
    ("关闭当前浮层", "關閉當前浮層", "Close current overlay", "現在のオーバーレイを閉じる"),
    ("关闭帮助屏", "關閉幫助屏", "Close help screen", "ヘルプを閉じる"),
    # ---- KeymapTable：Short（键帽注记 ≤3 字）----
    ("帮助", "幫助", "Help", "ヘルプ"),
    ("主题", "主題", "Theme", "テーマ"),
    ("开始", "開始", "Start", "開始"),
    ("暂停", "暫停", "Pause", "ポーズ"),
    ("快存", "快存", "QSave", "即保存"),
    ("悔棋", "悔棋", "Undo", "待った"),
    ("换层", "換層", "Layer", "層切替"),
    ("镜像", "鏡像", "Mirror", "反転"),
    ("辅线", "輔線", "Lines", "補助線"),
    ("色弱", "色弱", "Color", "色覚"),
    ("旋转", "旋轉", "Rotate", "回転"),
    ("平移", "平移", "Pan", "パン"),
    ("间距", "間距", "Spacing", "間隔"),
    ("复位", "復位", "Reset", "リセット"),
    ("全亮", "全亮", "Bright", "全表示"),
    ("步进", "步進", "Step", "ステップ"),
    ("继续", "繼續", "Resume", "続ける"),
    ("重开", "重開", "Restart", "再開始"),
    ("保存", "儲存", "Save", "保存"),
    ("读快存", "讀快存", "QLoad", "即読込"),
    ("标题", "標題", "Title", "タイトル"),
    ("说明", "說明", "Guide", "ガイド"),
    # ---- KeymapTable：Category / KeycapScope 图例 ----
    ("全局", "全域", "Global", "全体"),
    ("引导", "引導", "Guide", "ガイド"),
    ("对局", "對局", "Game", "対局"),
    ("相机与视图", "相機與視圖", "Camera & View", "カメラと視点"),
    ("浮层", "浮層", "Overlay", "オーバーレイ"),
    ("暂停 / 菜单", "暫停 / 選單", "Pause / Menu", "ポーズ / メニュー"),
    ("Ctrl 组合", "Ctrl 組合", "Ctrl combos", "Ctrl 組み合わせ"),
    ("Shift 组合", "Shift 組合", "Shift combos", "Shift 組み合わせ"),
]


def main():
    out = Path('src/Nmoku.Game/assets/i18n/translations.csv')
    out.parent.mkdir(parents=True, exist_ok=True)
    with out.open('w', encoding='utf-8', newline='') as f:
        w = csv.writer(f, quoting=csv.QUOTE_MINIMAL, lineterminator='\n')
        w.writerow(['keys', 'zh-CN', 'zh-Hant', 'en-US', 'ja-JP'])
        seen = set()
        for row in ROWS:
            if row[0] in seen:
                print('重复键（后者覆盖前者将跳过）:', row[0])
                continue
            seen.add(row[0])
            w.writerow([row[0], row[0], *row[1:]])
    print(f'写入 {out}，{len(seen)} 键')


if __name__ == '__main__':
    main()
