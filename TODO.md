# TODO

> 维护规则见 `AGENTS.md`：本文件按"当前→近期→远期挂账"三层组织，收工必更新；总量 ≤300 行，已完成的尽快移入 CHANGELOG/git log，不丢当阶段关键教训。

## 当前（M5 代码完结 + 全面审查修复批 1–4 & 开始界面重做已落地 2026-11-26；待人类真实窗口统一体验 → 分支合回 `master` → tag `m5`）

> 历史背景：2026-10-26 全面代码审查（[`docs/review-2026-10-26.md`](docs/review-2026-10-26.md)）后，
> 修复批 ①止血 / ②数据完整性 / ③真实缺陷 已于 10-26/10-27 落地；2026-11-26 按人类指示落地
> **修复批 ④（Core/AI/视图/壳层遗留 bug + 开始界面彻底重做 + autotest 拆分与 `_Process` 覆盖）**。
> 当前门禁：**build 0 警告 / 382 单测全绿 / 无头自测 ALL PASS**（含 `m5-pretitle-redesign`）。

### 本轮已完成（2026-11-26，待人类统一体验）

- [x] **开始界面重做与实机调优（`PreTitleScreen.cs` / `PreTitleShaders.cs`）**：雕刻多面 3D 晶体 `N`（无 Bayer 像素抖动/背景格纹）+ 素雅瓷质双子差速公转 + 封面柔和焦散（展开棋盘后彻底消隐）+ 自中心略偏左构图 + 均衡 2D UI；同步深化全局暗/亮主题、新增 `FocusGridLine` 提升深浅背景棋盘明晰度，并根治高分屏 32× 过采与隐藏视口空转性能瓶颈（3200×2000 核显稳锁 60fps）。7 处历史遗留清零，真实窗口暗亮两套 × 开始页/对局页截帧复验通过。
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

- [ ] **分支收口与 CI 门禁（待人类拍板）**：验收通过后 `prestart-native` 合回 `master`、删除已并入价值的 `bgm-engine` / `bgm-engine-mixed-backup`、评估 CI 触发条件是否放宽到开发分支；`FONTS_LICENSE.md` 版权行与项目名修正。
- [ ] **UI 缩放体系重构（人类拍板 2026-11-26 暂缓）**：960×540 基准 + 公式化 `csf = (窗口高/540) × UI倍率` + 窗口化固定分辨率挡位（960×540 / 1920×1080 / 2560×1440，保留自由拖拽）+ UI 倍率下拉（0.5x–2x）。工具 `tools/ui-baseline-preview.html` 已备。
- [ ] **游戏内其余按钮 hover 微交互推广**：预开始页已落地平滑 hover 强调条+轻移，后续可统一推广到暂停/存档/设置等壳屏。
- [ ] **GitHub Release 上传**（人类执行）：`build/Nmoku-0.1.0-alpha-win64.zip` 已备；tag `m5` 待人类走查后打。
- [ ] **正式 BGM**：Strudel 谱面创作四轨 → strudel.cc 渲染/录音 → WAV→OGG 替换占位（见 `assets-src/music/strudel/README.md`）。
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
