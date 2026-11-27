# TODO

> 维护规则见 `AGENTS.md`：本文件按"当前→近期→远期挂账"三层组织，收工必更新；总量 ≤300 行，已完成的尽快移入 CHANGELOG/git log，不丢当阶段关键教训。

## 当前（`v0.2.0-beta` / `m5` 已合回 `master` 并发布 2026-11-26）

> 历史背景：M5 音频/设置/i18n/打包 + 2026-10-26 全面代码审查（[`docs/review-2026-10-26.md`](docs/review-2026-10-26.md)）修复批 ①–④ + 开始界面重做 + 全局 `UiTheme` + 亮色立体 N 矢量图标已全部合回 `master`，打 tag `m5` 与 `v0.2.0-beta` 并打包 `build/Nmoku-0.2.0-beta-win64.zip`。
> 当前门禁：**build 0 警告 / 397 单测全绿 / 无头自测 ALL PASS / 正式音频已认领上线（2026-11-27）**。

### 正式音频认领与播放机制（✅ 2026-11-27 已落地，待人类真实窗口试听验收）

- [x] **正式资产替换**：认领 `../programmed-music/out/nmoku_ogg/` 5 轨无缝立体声 BGM（含 `bgm_game_2`）与真音源 `sfx_win`/`sfx_lose`，升级 `AudioBake` 烘焙黑白子玄石/玉石双敲落盘采样与 `-SyncMusic` 同步脚本。
- [x] **播放机制补全（§10 B31）**：Core `AudioPolicy`/`SfxDebounceGate`/`BgmDuckEnvelope`（+15 例单测）；黑白子与 AI/演示盘/复盘分色落子声（同色 65ms 防抖 + ±3% 音高微抖 + 双声部复音）；PvE 胜负音效区分 + 3s 短乐句 BGM 平滑闪避与离开结算止尾；双曲对局池会话续播（修暂停往返切曲）+ 新局亲和/轮换；设置页补「对局音乐」「切屏音乐」四语言下拉。

### 0.2.0-beta 已收口（2026-11-26）

- [x] **分支收口与发布**：`prestart-native` 快进合回 `master`，清理已废弃旧分支 `bgm-engine` / `bgm-engine-mixed-backup`，打 `m5` + `v0.2.0-beta` 并推送 `origin` 触发 CI。
- [x] **应用图标重做（`tools/make-icon.py`）**：参照原 TS 3×3×3 立体盘 + 跨层红柱 + 蓝/黑绿/白红辅助线，改亮色水绿圆角矩形纯平矢量风并由棋盘纵边与空间对角连线勾勒立体 `N`，生成 512/256/多尺寸 ICO。

- [x] **开始界面重做与全局 UI/主题/性能调优（`PreTitleScreen.cs` / `UiTheme.cs`）**：雕刻多面 3D 晶体 `N`（无 Bayer 抖动、无地面焦散）+ 素雅双子差速公转 + 自中心略偏左构图；全局默认字体统一为霞鹜文楷（仅物理键帽用无衬线黑体），全套弹窗/菜单按钮去除默认灰扑扑皮肤；深色黑子回调可见度、浅色字柔化、`FocusGridLine` 拉清深浅棋盘，并根治高分屏过采与隐藏视口空转（3200×2000 稳锁 60fps）。7 处历史遗留清零，真实窗口暗/亮截帧复验通过。
- [x] **AI 行为修复与线程隔离（§10 B28–B30，人类拍板）**：己方即胜绝对优先于防守（修 B4 送死）、`PatternScore` 按 `WinLength` 4–9 泛化、Easy 恢复邻近落点、`GameManager` 传 `Board.Clone()` 快照并释放 CTS。
- [x] **Core 边界防御与相机精度**：`Rules.Directions` 改 `ImmutableArray`；`Board`/`Rules`/规则引擎参数校验；`CoordMapper.WorldToLogical` 四舍五入+防除零；`CameraMath.ComputeDistance` 非法 aspect 防护；`CameraRig.Advance` 同步元数据；`AuxScan`/`KeycapParser` 边界防护。
- [x] **视图与壳层卫生**：2D/3D 共用 `AuxScanResult` 快照；3D 辅助线改 `ImmediateMesh` + 标记球节点池并去重；`Main`/`GameStore`/`AudioManager` 补 `_ExitTree` 退订停播；`SaveMenu`/`KeymapContent` 补 `RemoveChild`；`SettingsStore` 缓存+合并落盘+退出释放；`I18nCsvTests` 新增占位符一致守卫。
- [x] **架构拆解（批次 ④ 第一步）**：`Main.cs` 的 2000 行自测拆入 `Autotest/Main.Autotest.cs`，取消 `_Process` 早退使生产帧循环进入自测覆盖，新增 `m5-pretitle-redesign` 回归锁。

### 人类统一体验验收关注点

1. **新版开始界面**：封面雕塑 N 与 2D 排版观感、流体展开/收起手感和流畅度、演示棋盘自转/拖拽/落子/辅助线手感、暗/亮两套主题与四语言热切换、最小窗口自适应。
2. **对局与 AI 体感**：PvE 下 AI 即胜不再送死（B28）、沙盒 6–9 连珠 AI 表现（B29）、Easy 难度落点自然度（B30）、高分屏/最大化/最小化切换稳定性。
3. **音频 / WASAPI / 设置页 / i18n**：占位 BGM/SFX 听感、窗口切换/设备插拔、设置页滑条/静音/窗口模式/四语言抽查。
4. **M5 L3 清单**（`migration-plan.md` §5）：七条 E2E 流程 + 存档导出/删除/导入往返；体验通过后拍板 **分支合回 `master` + 清理旧分支 `bgm-engine`/`bgm-engine-mixed-backup` + 打 `m5` tag**。

## 批次路线图（剩余项）

### ④ 架构化（后续可选深化）

- [x] `Main.cs` 的 autotest 拆到 `Autotest/Main.Autotest.cs` 并取消 `_Process` 早退（2026-11-26 已落地）
- [ ] 如需继续精简 `Main.cs`（现 1317 行）：依次抽输入分发 → 窗口子系统 → 视图/相机/路由表
- [ ] `Nmoku.Game` 层核心逻辑（`GameStore`/`OverlayManager`）下沉为可 xUnit 化纯逻辑

### ⑤ 预开始页与视口（✅ 2026-11-26 已全部清零）

- [x] 角标标题就位 / 「继续对局」独立管理 / `Rebuild` 原地换肤换语 / 自转修复 / 隐藏停渲染 / 物理像素超采样 / 硬编码中文清零 / 2D 面板共用辅助线快照 / 3D 辅助线池化去重

## 近期

- [ ] **GitHub Release 上传**（人类执行）：上传 `build/Nmoku-0.2.0-beta-win64.zip` 至 `v0.2.0-beta` Release；`FONTS_LICENSE.md` 版权行与项目名修正。
- [ ] **UI 缩放体系重构（人类拍板 2026-11-26 暂缓）**：960×540 基准 + 公式化 `csf = (窗口高/540) × UI倍率` + 窗口化固定分辨率挡位（960×540 / 1920×1080 / 2560×1440，保留自由拖拽）+ UI 倍率下拉（0.5x–2x）。工具 `tools/ui-baseline-preview.html` 已备。
- [ ] **游戏内其余按钮 hover 微交互推广**：预开始页已落地平滑 hover 强调条+轻移，后续可统一推广到暂停/存档/设置等壳屏。
- [x] **正式 BGM 与音效**：已于 2026-11-27 从 `../programmed-music/out/nmoku_ogg/` 认领 5 轨无缝立体声 BGM + 真音源胜负音效，并补全黑白子落盘/AI 落子/胜负闪避/多曲轮换/设置项（待真实窗口试听确认）。
- [ ] **3D 视图加载黑闪根治**（淡入已掩盖观感，视反馈决定是否做视口纹理就绪检测）。
- [ ] **教学关小基建**：`LevelCatalog.Tutorial.InitialMoves` 改可注入+持久化。
- [ ] **AI 难度梯度可视化校验**：不同难度 AI 互弈批量对局 + 胜率曲线。
- [ ] **滚轮 zoom-to-anchor 恢复**：锚点改射线拾取的格/子。
- [ ] **文本键消歧（P2）**：拆开「开始游戏」（标题 vs 引导）与「退出」（应用 vs 复盘）的同值 gettext 键。

## 远期挂账

- [ ] **Connect6**（优先级高于禁手）：动手前先出接口演进设计经确认再实现。
- [ ] **Renju 禁手**（三三/四四/长连判负）：排 Connect6 之后。
- [ ] **开局库变体**（Swap1/Pro/Long Pro）：前提是与 GameManager/状态机解耦。
- [ ] **键位重绑定**（大工程；设置页已预留入口位）。
- [ ] **SynthMusicEngine 合成器回填**（`IMusicEngine` 接口已挂）。
- [ ] **自定义调色板 JSON 导入导出 UI**（`Palette` 字段已语义化）。
- [ ] **i18n 扩展**：语言列表动态化；译文社区化打磨。
