# TODO

> 维护规则见 `AGENTS.md`：本文件按"当前→近期→远期挂账"三层组织，收工必更新；总量 ≤300 行，已完成的尽快移入 CHANGELOG/git log，不丢当阶段关键教训。

## 当前（M5 代码完结 2026-09-26：音频/设置页/i18n/图标/README/过渡动效；待人类真实窗口走查 → tag `m5`）

### 已落地（待人类实测）

- **音频（占位版）**：AudioManager（Sfx/Bgm 总线）+ `IMusicEngine`/`SampleMusicEngine`（合成器
  `SynthMusicEngine` 留壳）；SFX 四件按源参数精确生成（click 落子/win 任意方获胜——**lose/hover 源即
  未接线**，B20）；BGM 四场景轨分发（menu/game/pause/guide，复盘不切轨、同轨不重播）；M 静音全局
  +toast；音量持久化 `user://settings.cfg`；**WASAPI 已恢复**（真实窗口启动无报错）。
  **正式 BGM 走 Strudel 谱面路线**（人类指示）：谱面骨架 `assets-src/music/strudel/`（四轨占位谱+
  渲染管线 README），WAV 留空；占位循环=`tools/AudioBake` 烘焙（`scripts/bake-audio.ps1` 一键重生成）。
- **设置页**（Overlay 浮层，标题屏/暂停菜单入口）：音效/音乐音量滑条、静音开关（与 M 同步）、
  窗口模式（窗口化/最大化/全屏 + 启动恢复）、语言切换（四语言）；键位重绑定仍挂远期。
- **i18n 四语言**：zh-CN 文本本身作翻译键（gettext 风格，Core 的 Text 常量零改动）；渲染点包
  `I18n.T`（114 处调用点+散点）；CSV `assets/i18n/translations.csv`（zh-Hant/en-US/ja-JP 手译，
  zh-CN 列=原文自映射）；完备性测试（Core 反射键集全覆盖）+ 端到端自测 `m5-i18n`；
  **坑**：zh_CN 查询会按语言族误配 zh_Hant——注册 zh-CN 自映射列后精确命中（自测抓住）。
  切语言热刷新=重建已挂载壳屏（ApplyTheme 同款通道）。
- **图标/发布**：程序化图标（`tools/make-icon.py`：等距三层棋盘+五连，PNG 256 + 多尺寸 ICO）接入
  project.godot + 导出预设；README 发布说明（下载运行/键位表/构建）；package.ps1 全链复验：
  120MB 自包含 exe（pck 内嵌含音频/翻译），真窗口启动冒烟通过。
- **过渡动效（最小方案）**：进对局与最大化逻辑面跳变时 GameScreen Modulate 0.4→1/160ms 快速淡入，
  掩盖 3D 视口首帧黑闪与一帧重排"突兀缩小"；**不碰布局/缩放管线**（B16 回环教训）。
  根治性方案（视口纹理就绪检测等）视人类观感反馈再议。
- **M/T 浮层内生效补齐**：KeymapTable 宣称 AllowWhenOverlay 但浮层分发分支此前只接了 F1/Esc
  （M4 遗留漂移），M5 补接线——M 静音/T 主题在浮层打开时也可用。

### 验收反馈（人类 2026-09-26 实测，待修）

**窗口布局（当前批，根治中）**
- 标题屏「退出」按钮被挤出可视区：窗口化也要拉长窗口才能看见。批11 的 ContentScaleSize=Zero
  本质是"最大化瞬间缩小 UI"的规避而非根治，现窗口化同样溢出——人类指示彻底解决，可回退该规避修复；
  用 computer use 实测默认窗口/最大化/全屏三模式取证后定根治方案。

**M5 设置页 bug（下批修）**
- [ ] 音效滑块滑不动，但数值会变，且方向不符合逻辑（左右相反）
- [ ] 静音开关与静音 toast 不联动（M 键切换时设置页按钮文本不刷新）
- [ ] 切换语言后选项文字不改变，且无法切回简体中文（"改不回"）

### 验收关注点（人类）

1. **音频听感**：占位 BGM/SFX 主观质量（不满意→Strudel 谱面创作替换或 CC0）；M 键/设置页静音/
   音量滑条联动；落子 click 与胜负 win 时机。
2. **WASAPI 实测场景**（TODO 原要求）：窗口切换/设备插拔/系统混音——M4R 曾有设备失效报错，
   本次恢复默认驱动后需真实场景验证。
3. **设置页**：窗口模式三档往返（全屏/最大化/窗口化恢复 1920×1080 居中）、启动恢复上次模式、
   重启后音量/语言保留。
4. **i18n 抽查**：切 English/繁體/日本語 后标题/对局/存档/设置/F1 页文本与观感（译文人工校对：
   agent 手译，术语如"快存/Quick/クイック"）；zh-Hant 用字习惯。
5. **动效观感**：最大化瞬间淡入是否到位（偏弱/偏闪可调 FadeDuration/FadeFromAlpha）。
6. **图标审美**：不满意重跑 `tools/make-icon.py` 调参。
7. **M5 L3 清单**（migration-plan §5 验收栏）：七条 E2E 流程手动过一遍 + 存档导出→删除→导入往返。

## 近期

- [ ] **GitHub Release 上传**（人类执行）：`build/Nmoku-0.1.0-alpha-win64.zip` 已备（M5 打包产物）；
       tag `m5` 待人类走查后打
- [ ] **正式 BGM**：Strudel 谱面创作四轨 → strudel.cc 渲染/录音 → WAV→OGG 替换占位
       （管线与谱面骨架已备，见 assets-src/music/strudel/README.md）
- [ ] **3D 视图加载黑闪根治**（淡入已掩盖观感，根治=视口纹理就绪检测/预渲染，视反馈再议）
- [ ] **教学关小基建**：LevelCatalog.Tutorial.InitialMoves 从硬编码改为可注入+持久化（不建教程系统）
- [ ] **AI 难度梯度可视化校验**：不同难度 AI 互弈批量对局+胜率曲线；Core 接口形状需人类定夺
- [ ] M3 后：复核 B4——AI"堵优先于胜"的体感
- [ ] M4 后：恢复滚轮 zoom-to-anchor——锚点改射线拾取的格/子
- [ ] 高分机器若帧率不足：降 MSAA 档位（超采样已提供 AA，观察项）

## 远期挂账

- [ ] **Connect6**（优先级高于禁手）：动手前先出**接口演进设计**（`PlaceMoves` 或回合内两阶段）经确认再实现
- [ ] Renju 禁手（三三/四四/长连判负）：优先级调低，排 Connect6 之后；`IsLegalMove` 现有签名够用
- [ ] 开局库变体（Swap1/Pro/Long Pro）：前提是与 GameManager/状态机解耦（策略化+数据驱动）
- [ ] **键位重绑定**（大工程；设置页已预留入口位）
- [ ] SynthMusicEngine 合成器回填（IMusicEngine 接口已挂；音频语义参照 audio_var.md）
- [ ] 自定义调色板 JSON 导入导出 UI（Palette 字段已语义化）
- [ ] i18n 扩展：语言列表进设置页下拉的动态化；译文社区化打磨
