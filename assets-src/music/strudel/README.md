# 音频资产来源与同步管线（正式 BGM 与胜负音效已上线）

## 当前状态（2026-11-27）

正式音频已从同级项目 `../programmed-music/out/nmoku_ogg/` 认领并覆盖入
`src/Nmoku.Game/assets/audio/`，全面取代 M5 占位合成音轨（详见 `docs/migration-plan.md` §10 B31）：

| 资产 | 规格 | 说明 |
|---|---|---|
| `bgm_menu.ogg` | 44.1kHz 立体声 OGG q4 · 1:32 · 无缝循环 | 主主题 · 标题屏（94 BPM，清透晶体室内乐） |
| `bgm_game.ogg` | 44.1kHz 立体声 OGG q4 · 2:00 · 无缝循环 | 标准对局（96 BPM，Action Loop 晶格悬停，中高频让位） |
| `bgm_game_2.ogg` | 44.1kHz 立体声 OGG q4 · 1:49 · 无缝循环 | 进阶对局（106 BPM，Dorian + 轻巧军鼓/马林巴） |
| `bgm_guide.ogg` | 44.1kHz 立体声 OGG q4 · 1:27 · 无缝循环 | 引导/说明（88 BPM，柔雾毡钢） |
| `bgm_pause.ogg` | 44.1kHz 立体声 OGG q4 · 0:58 · 无缝循环 | 暂停屏息（66 BPM，水下低通柔雾） |
| `sfx_win.wav` | 44.1kHz 16-bit 立体声 WAV · 3.10s | 华丽胜利短乐句（LABS Strings 2 Pizzicato） |
| `sfx_lose.wav` | 44.1kHz 16-bit 立体声 WAV · 3.00s | 沉重失败短乐句（SSO Celli + Splice Ariel） |
| `sfx_stone_black.wav` / `sfx_stone_white.wav` | 44.1kHz 16-bit WAV | 黑子玄石 / 白子玉石双敲落盘采样（由 `tools/AudioBake` 按试听台配方烘焙） |
| `sfx_click.wav` / `sfx_hover.wav` | 44.1kHz 16-bit WAV | 柔和中性点击与微触采样（替代旧方波/正弦占位） |

## 同步与重烘命令

```powershell
# 仅重烘黑白子落盘/点击/悬停采样（不会覆盖正式 BGM 与胜负音效）
powershell -File scripts/bake-audio.ps1

# 从 ../programmed-music/out/nmoku_ogg/ 重新同步正式 BGM/胜负音效 + 重烘落子采样
powershell -File scripts/bake-audio.ps1 -SyncMusic

# 刷新 Godot .import 元数据
D:\Tools\Godot\Godot_console.exe --headless --path src/Nmoku.Game --import
```

## 历史说明

本目录下保留的 4 份 `bgm_*.strudel` 为 M5 早期按源 `audio_var.md §二` 编写的占位谱骨架，留作历史对照；正式编曲工程、REAPER 工程与渲染脚本位于同级仓库 `../programmed-music/`（参见其 `docs/NMOKU-BGM-PLAN.md`）。
