# Strudel BGM 谱面（正式 BGM 载体，WAV 暂留空）

M5 按人类指示（2026-09-26）：正式 BGM 走 **Strudel 代码音乐路线**，参照
`D:\P_rojects\programmed-music`（patterns 谱面 → strudel.cc 播放/渲染 → WAV → OGG）。
当前目录是**占位谱骨架**：和声进行对齐源项目 `audio_var.md §二` 的 BGM_PATTERNS，
正式创作在此基础上展开。游戏内现在跑的是 `scripts/bake-audio.ps1` 烘焙的占位循环。

## 目录

```
bgm_menu.strudel    标题屏（源: warm cinematic pad + shimmering arpeggios）
bgm_game.strudel    对局（源: driving, plucky with walking bass）
bgm_guide.strudel   引导页（源: gentle organ swell + sustained pad）
bgm_pause.strudel   暂停（源: held-breath, underwater filtered）
wav/                渲染产物（.wav），当前留空
```

## 渲染管线（正式音频上线步骤）

1. 全选谱面 → 粘贴到 https://strudel.cc/ → Ctrl/Cmd+Enter 试听调整。
2. 满意后导出/录制 WAV（参考 programmed-music 的做法），命名 `bgm_<轨>.wav` 放入 `wav/`。
3. 转 OGG 覆盖游戏占位（q4 单声道与现有一致）：
   `ffmpeg -i wav/bgm_menu.wav -c:a libvorbis -qscale:a 4 ../../../src/Nmoku.Game/assets/audio/bgm_menu.ogg`
4. 无缝循环要求：谱面尾小节动机落回首小节（`@length` 整数轮），或 WAV 首尾都落在包络归零点。

## 约定

- 每轨一个 cycle = 一个和弦（`setcps` 按源 chordDuration 折算，谱内注明）。
- 谱面 UTF-8 无 BOM；`.bat`/`.ps1` 类脚本不要写中文（OEM 码页咬字）。
- 许可：谱面与渲染音频均为本项目自产，随仓库 GPL-3.0 分发。
