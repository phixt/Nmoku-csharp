# CHANGELOG

> 维护规则见 `AGENTS.md`：只记"当前场景"相关事实；总量 ≤300 行，旧条目按里程碑压缩合并，但关键教训（踩坑结论）不删。
> 提交级细节看 `git log`，这里只留人读的里程碑叙事。

## 当前状态（2026-09-25）

- **里程碑**：M0–M3 ✅（tag `m0`–`m3`）；M4 🚧 验收反馈批恢复（相机异常已定案修复，见下节），
  **待人类真实窗口全流程走查 → tag `m4`**。
- **质量门禁**：`dotnet test` 273 例全绿；build 0 警告；无头自测 ALL PASS（M3 对局 + M4 状态机/存档/复盘 +
  M4R 路由/输入回归 + M4 收尾 + m4c-save-alignment）。
- **架构红线**：`src/Nmoku.Core` 零 Godot 依赖（KeymapTable/GameStateMachine 均在 Core），xUnit 秒级运行。
- **许可证**：项目源码采用 GNU GPL v3.0（GPL-3.0-only），完整协议见根目录 `LICENSE`，README 已加入授权说明。

## M4 验收反馈批9 — 存档菜单定稿与偶数阶销账（2026-09-26）

- **菜单=档案管理**（人类拍板）：不再承担保存当前对局——写局只走 S（首空槽/满则快存）/Ctrl+S/Ctrl+L，
  菜单底部键位提示行补可发现性。头部「导入」（原导入文件）=文件→统一落槽（首空槽，全满覆盖槽 5 并
  确认，快存槽永不被动覆盖）；头部剪贴板导入删除。空槽行 [填写][导入]；非空槽行 [读取][复盘][覆盖
  （剪贴板→该槽，占用先确认）][复制][导出（文件对话框直开）][删除]；「覆盖（写当前局）」「存为快存」
  与字面「文件」按钮（全游戏唯一纯名词无意义标签，人类点名）退役。剪贴板=显式中转站，
  复制↔填写/覆盖搬运闭环；导入原档保真（id/timestamp 随档）。
- **偶数阶销账**：SaveValidator 拒绝偶数 boardSize（`SAVE_INVALID_EVEN_BOARD_SIZE`）；导入链路
  （剪贴板/文件/zip）此前只迁移不校验结构的缺口一并补上（TryParsePackage/TryUnpackPackage 接入
  Validate），全链路封闭。前提（拍板）：存档不与外部游戏互通+无偶数阶入口。migration-plan D1 定案。
- 自测 m4c-save-alignment 重写：S×2/填写/覆盖（合成确认）/文件指定槽/统一落槽/全满覆盖槽5+快存保护。
  教训：导入原档保真使时间戳断言失效 → 改档 Id 断言；headless 剪贴板不可靠 → ExportSlot 返回值 +
  FillSlot(index, text)。
- 门禁：build 0 警告 / 302 测试 / 无头自测 ALL PASS。

## M4 验收反馈批 8 — 浅色方框根除与辅助线减细（2026-09-26）

- **"超界半透明深色方框"根除**：球面暗环用方形径向渐变承载，圆外区域无法归零（各方向距光源
  距离不同，渐变钳制泄露）——浅底原形毕露、深底不可见。改为**逐像素生成**（256×256 一次性
  Image 生成：圆内暗环 + 2% 羽化圆内遮罩、圆外强制透明）。
- **辅助线/红圈减细**：辅助线 2→1.2（保留 AA，斜线需要）、最后手环 3→1.5 + 64 段硬边。
- **任务栏遮挡挂回待办**（人类关键线索：进入游戏→退出游戏之后出现；代码仅 _Ready MoveToCenter，
  需查视口尺寸变化与窗口联动或 Godot 窗口怪癖）。

## M4 验收反馈批 7 — 渲染精度与快捷键页修补（2026-09-26）

- **2D 棋子去玩具感**：四层绘制=体色圆+球面明暗暗环（与高光同心的径向黑晕，远缘变暗模拟
  3D 受光面）+左上高光+硬边细描边（描边 1.5→1.0、AA→硬边、40→64 段——"白色棋子黑边又厚又糊"
  的直接来源）。
- **格线发糊**：2D 网格线 AA 线改硬边细线（1.5/AA→1.2/硬边，轴对齐线无需 AA）；3D 细网格线
  精度不足 → 视口在原生像素之上再 ×2 超采样（`ViewportSupersample`，宿主缩小绘制=SSAA）。
- **快捷键页修补**：①翻层 NRE——引导屏未挂载时 `_guideScreen.Content` 为 null 被无条件解引用，
  改 null-safe 分宿主 + 回归步 `m4c-keymap-paging`（合成 ← 键翻到末页）；②页码位置固定（两侧
  对称 Expand+层名固定 110px 宽，不再随层名长短左右跳）；③每页无功能键以无色占位（idle 帽）
  显示；④对局层浅色加深 #E8F4F5→#9FCBD0（与白色不分）。全屏任务栏行为人类确认正常，挂账撤销。
- 门禁：build 0 警告 / 298 测试 / 无头自测 ALL PASS。

## M4 验收反馈批 6 — 快捷键页分层分页定稿（2026-09-26）

- **人类澄清"分层"含义**：分层=分页——此前把六层混在一页+底部总览是理解错误。定稿：
  六页（对局/复盘/暂停/Ctrl/Shift/全局），←/→ 或圆圈数字页码点击切换；每页只渲染该层按键
  （尺寸统一、键帽上部 ≤3 字注记）；完整说明统一在页面底部两列（仅当前层）；无总览网格。
- 实现：`KeymapEntry.Short`（≤3 字注记，单一数据源 + 单测锁定 ≤3 字）；`KeymapContent` 分页重构
  （页码指示/键盘/说明按页重建；修复：宿主 Control 不传播子节点最小尺寸导致说明网格叠键盘——
  宿主自身设 CustomMinimumSize）；`Main.TryPageKeymap` 在页打开时拦截 ←/→（复盘态开页不误触步进）；
  KeyboardLayout 加 Esc（Q 排左端）与 Extents 接口（视图禁手算宽度）。
- 另：棋子/格线纹理发糊与全屏任务栏行为两项人类反馈已挂 TODO（本批未修）。
- 门禁：build 0 警告 / 298 测试 / 无头自测 ALL PASS；分页五页截屏自检通过。

## M4 验收反馈批 5 — UI ×2.5 返工与 B16 v4 定稿（2026-09-25）

- **人类验收批 4 判定效果差，四项返工**：①窗口回 **1920×1080**（弃 85% 工作区自适应）；
  ②内容缩放改 **2.5 绝对值**（弃 DPI×2.5=3.54——发糊放大器），等效设计基准 768×432；
  ③"非常糊"根因=**3D 视口纹理是逻辑尺寸被放大显示**（v2 超采样降采样回逻辑纹理，高缩放下白忙）；
  ④帮助页滚动条不该出现（整页须一屏放下）。
- **B16 v4 定稿（视口宿主自绘）**：v3 用 `_GetMinimumSize` 清零堵回环——**实测无效重现回环**
  （"进入游戏几次放大聚焦左上角"），因为 SubViewportContainer 的 C++ `get_minimum_size()` 在
  stretch=false 时直接返回视口尺寸、**不经脚本虚方法**（v1 黑屏事故的深层机理补全）。
  v4 弃用 SubViewportContainer：`GameScreen.ViewportHost`（Control 自绘视口纹理+每帧重绘），
  布局尺寸与视口尺寸**结构性解耦**；视口按宿主逻辑尺寸×内容缩放喂原生像素（1182×1080 恒定，
  逐帧数值验证），纹理与物理像素 1:1。宿主无输入转发需求（本项目输入全在 Main._Input）。
- **键帽阴影主题化**：暗底浅色硬阴影（假环境光，人类拍板"不要土味炫光"）= `Palette.KeycapShadow`；
  亮底深影不变。
- **帮助页单屏紧凑化**：只渲染有功能的键（对齐源）、方向键紧凑排布（KeyboardLayout 四排）、
  图例并入标题行、说明三列网格+大组合合并键帽（[Shift][WASD]）、无滚动条；
  引导屏（标题「快捷键说明」/暂停 G）与 F1 共用同一内容。
- 门禁：build 0 警告 / 297 测试 / 无头自测 ALL PASS；多屏真实窗口截屏自检通过。

## M4 阶段总结（2026-09-26，验收反馈批 1–8 完结，准备 M5）

- **落地全景**：游戏壳七态（标题/对局/暂停/结算/引导/复盘/帮助）+ 三层路由栈（首中即吞、
  KeepAlive、背板体系）；存档六槽+.nzsave 文件容器+剪贴板互通+文件对话框+winLength 外圈元数据
  （B14）；明暗主题（T 偏好模型）+文楷+关卡系统（预设+沙盒）；快捷键体系（KeymapTable 单一
  数据源+F1 分层分页六层页+圆圈页码+键帽注记）；UI 缩放（1920×1080 窗口+内容缩放 2.5）；
  B16 视口四版迭代定稿（v4 ViewportHost 自绘原生像素）；2D 棋子球面受光质感。
- **事故与教训沉淀**：B15 toast 死循环（77GB 事故真凶）；B16 v1 视口回环黑屏（v1→v4 完整机理
  链登记 §10，引擎 get_minimum_size 不走脚本虚方法）；复盘 NRE（主场景 UI 路径自测盲区）；
  无头全绿≠真实窗口正常（贯穿八批的主旋律）。
- **质量基线**：build 0 警告 / 298 单测 / 无头自测 ALL PASS（含 11 个 m4c 回归步）；
  提交全部本地未 push（规约 9），tag `m4` 待人类打。
- **遗留**：任务栏遮挡（两条线索挂账）；tutorial/residual 关卡占位重设计；偶数阶中心标注。

## M4 验收反馈批 4+5 — UI 缩放定稿与快捷键说明入口修复（2026-09-25）

- **修复「没看到快捷键页」**：标题「快捷键说明」/暂停 G 开的是引导占位页（非键盘页）——抽共用
  `KeymapContent`，引导屏与 F1 浮层同内容（与源"说明=键盘说明页"语义一致）。
- **UI 缩放两版迭代（批 4 被批 5 否定）**：批 4 = OS DPI × 2.5 + 窗口 85% 工作区——人类实测
  "效果很差、非常糊、窗口还是 1920×1080 合适"；批 5 定稿 = **窗口固定 1920×1080 +
  ContentScaleFactor=2.5 绝对值**（等效设计基准 768×432，`Main.UiScaleFactor` 唯一调节点）。
  "非常糊"根因=v2 超采样降采样回**逻辑尺寸纹理**再被放大显示——3D 纹理必须原生像素（B16 v4）。
- **引擎事实修正**：headless 下 `ConfirmationDialog`（PopupCentered）**不会**自动确认——
  确认框语义用 `ConfirmDialogForTests.EmitSignal("confirmed")` 合成驱动（此前交接教训 #4 有误）。
- **B16 v4 定稿**（承接批 3 的 v3 无效实测）：弃用 SubViewportContainer，`ViewportHost` 自绘
  视口纹理结构性解耦，视口=宿主逻辑尺寸×内容缩放=原生像素（详见 §10 B16 四版链）。
- 门禁：build 0 警告 / 298 测试 / 无头自测 ALL PASS。

## M4 验收反馈批 3 — 复盘 NRE 修复与 F1 页改版方案（2026-09-25）

- **修复：首次进复盘 NRE 崩溃**（人类实测：存档菜单「复盘」→ `NullReferenceException at
  Main.Refresh`）。根因：`BeginReplay` 先置 `InReplay` 再触发 `GM.StateChanged → Refresh`，此刻
  replay-hud 路由尚未挂载（`_replayBar=null`）；再次进入时旧实例又已随浮层卸载 QueueFree——
  两个窗口都会炸。**无头自测此前只在隔离 store（reborn）上测过 EnterReplay，主场景 UI 路径漏测**。
  修复：`Refresh` 对 `_replayBar` 做 `IsInstanceValid` 守卫 + `OnStateChanged(Replay)` 挂载后补一次
  `Refresh()` 同步。回归步 `m4c-replay-entry`（临时还原守卫验证：精确复现同款 NRE → 修复后 ok）。
  **复盘功能本体（步进/进度条/门控）M4 第一轮已有，此为视图联动时序 bug，非缺失。**
- **F1 快捷键页改版方案（待拍板，全文见 TODO「当前」）**：对齐源 guide 的 keyboard-sim 视觉
  （40px 3D 阴影键帽 / 六作用域配色 / 图例+分组说明），增强①层级切换（作用域页签过滤+键帽联动）
  与②多键组合键帽（Ctrl+S/Shift+WASD/Esc·R）；数据仍由 KeymapTable 单源派生，键位零改动。

## M4 验收反馈批 2 — bug1 修复 + 存档 UX/亮色主题落地（2026-09-25）

- **修复：开始新对局继承读档配置**（读 994 亮色档，重开仍是 994+亮色）。根因：`RequestStartNew`
  只 `GM.Reset()`，不重配 `LoadFrom`/`StartLevel` 改过的维度/规则/模式/AI/主题。修复对齐源
  `startGame = initGameContext(STANDARD_LEVEL)`：`CaptureDefaultContext` 捕获启动默认关卡，
  新局恒 `StartLevel(默认)`；主题引入 **T 偏好模型**（`_themePreference` 随 T 更新，新局恢复偏好
  ——读档带来的主题不进新局，用户 T 选择保留）。回归步 `m4c-newgame-default-context`。
  **引擎事实修正**：headless 下 `ConfirmationDialog`（PopupCentered）**不会**自动确认——确认框
  语义用 `ConfirmDialogForTests.EmitSignal("confirmed")` 合成驱动（此前交接教训 #4 表述有误）。
- **存档 UX 落地（方案 1，人类拍板 P1–P3）**：源核实——槽位管理只嵌标题屏右列；暂停菜单 L=读最新；
  快存=第 6 槽且源有 quick-load 按钮；缺口感 = 只有写快存没有读快存键。落地：P1 暂停「读档」按钮
  打开存档菜单（L 键保持读最新，按钮无键位提示防误导）；P2 新增 **Ctrl+L=读快存槽**（生效态=Paused，
  §3.4 读档仅 Title/Paused 合法，不扩转移表；空槽 toast「暂无快存」）；P3 存档菜单快存行标注
  「快存 · Ctrl+S 写 / Ctrl+L 读」。回归步 `m4c-quick-load`（快存→续弈 2 手→暂停→Ctrl+L→恢复）。
- **亮色主题对齐源（方案 2，B17）**：①根因修复——2D 面板与游戏基座无自有背景、透出根窗口默认
  深灰清屏色（暗色下不可见），现 `SetDefaultClearColor` 随主题（首帧前 + ApplyTheme +
  project.godot 兜底）；②Light 套按源逐字段重校：底 #E8F4F5/网格 #A0C0C4/黑石 #222/白石 #F0E8E0
  /文字 #1A2E3A/强调 #2C4A4E/危险 #8B3A3A（自创"宣纸米色"系弃用）；B9 自方绿/ghost 透明度/
  色弱组校准值保留。**待人类 T 键真实窗口走查**（对比度/协调性主观验收）。
- 门禁：build 0 警告 / 273 测试 / 无头自测 ALL PASS（含 m4c-quick-load）。

## M4 相机异常定案与回退批恢复（2026-09-25）

- **事故根因（真实窗口逐帧复现，HANDOVER-M4-camera-bug.md 三嫌疑之一坐实）**：B16 的
  `Stretch=false` 手动喂视口尺寸存在**每帧 ×内容缩放的正反馈回环**——SubViewportContainer 在
  stretch=false 时以视口尺寸为最小尺寸，容器布局永不压子控件到最小尺寸以下 → 容器被撑到视口大小
  → 下一帧 `desired=容器×1.4167` 再涨。实测（.trace/viewport-trace.log）：游戏屏可见首帧起
  1681→13488→int32 饱和，8–14GB 分配churn，`texture_create` 失败 175 次 = "视角分 4 次快速放大
  左上角→黑屏→灰屏+报错"。相机全程无恙（嫌疑 #3 排除：距离恒 31.7，"放大"是视口纹理 1:1 显示
  逐帧长大的视觉效应）。**回环只在内容缩放 >1 时发展**：窗口自适应批（ContentScaleFactor=OS DPI）
  提供触发条件、B16 提供结构——两批单独安全、合体必炸，与交接文档"不得同时上线"直觉吻合。
- **无头为何全绿**：autotest 的 `_Process` 提前 return，根本不跑 `HandleViewportChange`；
  headless 的 factor≈0.033 又被守卫拦下。教训再次坐实：**窗口/视口/渲染类改动必须真实窗口实测**。
- **修复（B16 v2，ad73d4b）**：容器保持 `Stretch=true`（引擎托管逻辑像素），`SyncViewport3DResolution`
  改设引擎内建 3D 缓冲超采样 `Scaling3DMode=Bilinear + Scaling3DScale=窗口最终变换缩放`——
  3D 按原生像素渲染后引擎降采样，不触碰 Viewport.Size。双验（factor=1 与 1.4167）240 帧视口零增长、
  内存平稳、0 报错 0 警告（连 v0 的 "Can't change the size" 刷屏一并消失）。
- **回退批恢复**：f5cd909 cherry-pick 回落（8af84bf：存档对齐 S 键/槽位覆盖/暂停菜单键位提示 +
  窗口自适应 ApplyDisplayScaling + 自测框架加固）；B16 v1（4df340f）不再回落（v2 取代）。
- **遗留观察项（人类走查时留意）**：超采样后 3D 缓冲为逻辑尺寸×缩放²，MSAA 8× 仍在；若高分机器
  帧率不足，降 MSAA 档位即可（超采样本身已提供 AA）。

## M4 收尾轮 — 工程项全清（2026-09-19）

- **B14 winLength 外圈元数据双通道**（登记 migration-plan §10）：v2.0.0 内层不增删字段——
  ① `.nzsave` manifest 可选 `winLength`（null 省键、越界视为缺失不毙档）；② 槽位 index.json 的
  SlotMeta 可选 `winLength`（§3.1.2 超集兼容，旧 index 无键→默认 5）。`LoadFrom(data, winLength)`
  恢复引擎/AI/关卡；快存/读档/复盘/导入导出全链路接线；无头自测连珠 7 快存→读档→连 6 不胜/连 7 胜。
- **R6 双向闭环**：跑源项目 `npm run dev` + 浏览器 UI 真实落子 8 手 → Esc 保存 → localStorage 抓取，
  样本入库 `Fixtures/browser_real_export_v2_standard.{json,b64.txt}`（= 源剪贴板字节）；反向把本项目
  Base64 经源「填写」对话框导入成功（toast 已保存到存档 2）。行为级回归 `SaveFixtureTests.BrowserRealExport_*`。
- **§6.3–6.5 契约核对**（结果登记 migration-plan §10.6）：逐条核对并补自动化证据——
  `m4c-overlay-contract`（确认恰好一次/取消不触发 onYes/输入 trim/CloseAll 丢回调，合成信号断言）、
  `m4c-modifier-quicksave`（合成 Ctrl+S 真触发快存、裸 S 不触发）、`Save_MultipleSlots_RoundTripIndependently`。
  **登记偏差**：适配器 GetIndex 只返回非空槽 meta，固定 6 槽投影在 SaveManager 层（无消费方依赖字面形状）。
- **真实旧档端到端恢复**：`m4c-real-old-restore` 读仓库内真实样本 → ImportText 完整导入路径 →
  读档恢复 8 手 → 续弈第 9 手成五 → GameOver。自测夹具定位：**Godot 启动会把进程 CWD 切到项目目录**，
  用 `CurrentDirectory` 与其上跳两级双候选探测（CI 从仓库根跑、本地同布局）。
- **教训**：ImplicitUsings 的 `FileAccess`/`Environment` 歧义在 Main.cs 再次各踩一次（全限定名收敛）——
  Godot 项目里 System 类名一律写全称。浏览器自动化经验：源项目落子=合成 mousemove+click（悬停门控）、
  暂停菜单=合成 KeyboardEvent（cua.keypress 在 IAB 挂起）、存档真源=localStorage `gomoku_save_<uuid>`。

## M4R — 路由重构（2026-09-19，人类验收反馈返工）

- 人类反馈三问题：①ESC 无响应（顺序求值无 early-return，同帧 pause+resume 互抵）②R 一键双触发
  （同根因）③标题屏可见棋盘（背板 0×0）。
- 修复：**首中即吞**（单次动作命中即 return，`SimulateActionPress` 共用分发）；**SceneRouter 三层
  路由栈**（L0 基座/L1 模态/L2 浮层+对话框 30，`StackFor` 唯一映射，KeepAlive 基座切可见性——
  坑：只挂 target 会让 game 从未 _Ready）；背板体系（None/Dim/Blur/Opaque，screen-texture 高斯）；
  保留现场语义（标题"继续对局"，覆盖先确认）；浮层冒泡（F1/Esc，Esc 关最顶）；**KeymapTable 单一
  数据源**（门控/F1/文档三处同源，冲突检测兜底漂移）。

## M4 第二轮 — 明暗主题/字体/关卡/文件存档（2026-09-19）

- Palette 明暗双套+WithColorblind（V 与 T 正交）+Ghost 反转；T→EventBus→换肤（SaveData.isDarkTheme
  随档恢复）。霞鹜文楷全局字体。关卡系统：LevelCatalog 三预设+沙盒表单+命令行并入同一装配路径
  （Configure 随档装配 WinLength 与预置子）。.nzsave 文件级导入导出+FileDialog。
- **限制（后由 B14 解决）**：v2.0.0 schema 无 winLength 字段，自定义连珠数随档丢失→外圈元数据。

## M4 第一轮 — 游戏壳全通（2026-09-19）

- Core：GameStateMachine 七态+§3.4 转移表穷举；NzSaveContainer（zip 容器）+SaveArchive 四路嗅探；
  Board.ToJaggedArray；IOverlayManager 落 Core。
- Game：GameStore（转移副作用集中，读档失败整转移取消）+FileSystemStorageAdapter+ShortcutRouter+
  OverlayManager（模态互斥/回调恰好一次/toast 堆叠）；壳屏六件；Ctrl+S 快存/L 读最新/剪贴板导出导入；
  GameManager BuildSaveData/LoadFrom/复盘门控。
- **教训**：§3.4 Any→REPLAY 七态全可达（穷举测试抓漏）；缺 using 报错被 lambda 级联噪音吞
  （块级 lambda 抽方法后浮出）；ImplicitUsings 的 FileAccess/Environment 歧义一律写全称；
  manifest 键序 camelCase（TestFirst 抓住）。

## M3 — 2D 右面板 + 落子闭环 + 辅助线（2026-09-19，✅ 验收通过 tag `m3`）

- **R4 流程**（铁律 2）：通读源 RightPanel/LeftPanel/main 输入段，行为清单 + D1–D4 决策落计划 §10.5
  （星位边星公式、F 镜像默认开、A/D 循环换层、辅助线扫描细节），人类确认后实现。
- **Core**：`AuxScan` 悬停扫描（跳空/连走/敌停/落点与截断标记/角色与配色解耦，9 用例）；`Board` 增
  Coordinate3D 重载。PatternCollector 与 AuxScan 并存（前者整层模式，后者悬停战术线，对应源两套机制）。
- **Game**：`GameManager`（回合/合法性/胜负/判和/悔棋/SWAP2 双分支/AI 异步应答+防串代，事件驱动视图）；
  `Board2DView` 自绘（星位/悬停 ghost/最后手环/镜像/辅助线，己方先画敌方压上）；`Board3DView` 焦点层
  强制实体（D2）+ 3D 蓝点跟随 2D 悬停（R4-12）+ 辅助线渲染；分屏 SubViewport（OwnWorld3D + MSAA）。
- **键位**：A/D 循环换层、F 镜像、H 辅助线 ALL↔NONE、V 色弱（Palette.Colorblind）、Ctrl+Z 悔棋
  （PvE 自动回退到人类回合）；滚轮/缩放/平移保持 M2 决策 #9 方案。
- **质量**：无头对局自测（PvP 五连胜/悔棋/SWAP2 双分支/AI 应答）ALL PASS 并挂入 CI 冒烟；测试 186 例全绿。
- **待人类**：双人全流程 + `-- --rules=swap2` 两分支 + `-- --mode=pve` 对照验收，tag `m3`。
- **灰屏修复（首轮验收反馈）**：分屏子视口塌缩到 2px——`SizeFlags.Expand` 只分配空间不填充，
  须用 `ExpandFill`（真实窗口诊断定位：vpSize=(2,648)、相机按 2px 宽算出 2665 距离）；2D 面板补
  `Game` 状态源注入（右半屏空白根因）。教训：**无头冒烟不渲染，布局类 bug 必须真实窗口跑诊断**。
- **第二轮验收反馈（2026-09-19，B9）**：①辅助线改**首子即停**语义（每方向一条线+一个标记，弃源的
  跳空续走/己方连走/截断标记——跨层斜线视觉穿透中间层的根因）；②棋盘逐层显隐（焦点 1.0/相邻 0.35/远层 0）；
  ③棋子六桶显隐：焦点实体/被辅助线标记强化/其余大幅减弱，黑白分色透明度入 Palette（暗背景黑子需更高）。
  ④网格材质补 Alpha 混合（不透明材质忽略 alpha 通道，逐层显隐失效）；显隐曲线改**反比例雾化**
  `alpha=1/(1+d)`（焦点 1.0/相邻 0.5/远层渐近留雾），新增 **O 键**切换全亮 1.0。
- **第三轮收尾（2026-09-19）**：3D 最后手标记（公告板红环套棋子中心、NoDepthTest 压顶、色弱换黄，
  与蓝球/辅助线点形状区分）；2D 棋子径向渐变纹理（GradientTexture2D，色值从 Palette 派生 + 换主题重生成，
  ghost 用 alpha 调制）。**M3 代码完结，M4 转新对话执行**（开工指引见 TODO「当前」节）。
- **第三轮验收反馈修复（2026-09-19，B10）**：整盘绕 Y 轴旋转 180°（CoordMapper 单点）——层 0 为最靠近
  摄像机的第一面、A/D 由近及远符合直觉、双面板左右一致；2D 面板纵向镜像（行 0 在底部对齐 3D）。
  2D 棋子"方形色块"根因：径向渐变纹理铺满方形、偏心高光时边缘色涂满四角——改三层绘制
  （体色圆+左上高光光斑+描边）。**教训：Core 文件手写 using 时警惕误引 Godot（红线），构建即暴露。**
  追加：网格逐层深度同步 B10 旋转（原 BuildGrid 未翻转导致格线显隐与棋子错位、高亮停在远端）。
  **M3 验收通过，tag `m3` 已打并移至修复后提交（打标几分钟后发现格线错位，人类指示完成即打，已披露）。**

## M2 — 3D 左视图 + 相机（2026-09-19，✅ 验收通过 tag `m2`）

- **Core 相机层**：`CoordMapper`（逻辑→世界，镜像收敛单点）+ `CameraMath` 八步管道纯函数 + `CameraRig`
  （§8.1 增量缓存 + 决策 #9 自由交互：无级旋转 Orbit、视角平移 Pan（限幅防丢盘）、Zoom/ZoomToAnchor、
  指数阻尼 `Advance`（帧率无关，yaw 走最短角）、Reset）。174 测试全绿。
- **决策 #9**（人类 2026-09-19）：相机超越源项目——无级旋转/视角平移/平滑阻尼（源因 bug 移除平移）；
  §10 登记 B7：源 `LeftPanel.ts` 相机五处缺陷/简化（对角线缺项、无水平 FOV 限制、lookAt 中心互换等），实现以文档为准。
- **Game 侧**：`Board3DView`（MultiMesh 黑/白实体 + 逐层 ghost 按层连续透明度、单 ArrayMesh 网格线、悬停蓝点占位）
  + `Main` 接线：Q/E ±90°、W/S 持续缩放、滚轮锚点缩放、左键拖无级旋转、中键拖平移（方向键已被缩放/复盘占用，
  平移键位待人类定夺）、Z/C 层间距、X 复位；演示棋子占位（M3 删）；标定参数 `-- --board=9 --layers=1 --spacing=6.0`。
- 三组标定配置无头数值自检通过（distance/fov/pitch 与文档锚点一致）；**待人类**：与源项目并排目视对照 + 手感确认 + FPS ≥ 60 + tag `m2`。
- **首轮验收反馈修复（2026-09-19）**：①滚轮回退径向缩放——无限地面锚点版有"远锚点大步进 + pitch 钳制瞬移"
  （W/S 本身经无头自动测试证实单调无跳变，跳变体感来自滚轮路径），zoom-to-anchor 挂 M3 拾取恢复；
  ②悬停改比棋子略小的蓝色小球 + 射线交点连续边界判定（不再出现在棋盘外）；③拖拽上下方向翻转（左右确认无误）；
  ④新增 Shift+WASD/方向键平移，动作判定改 exact match 防与缩放串线；⑤网格线改薄盒 MultiMesh（可加粗）+ MSAA 4×。
- **第二轮验收反馈修复（2026-09-19）**：①"Z/C 导致视角初始化"——CameraRig 增加用户缩放覆盖（Zoom 记录距离，
  公式重算后按新限幅钳制保留；X 复位清除）；②世界朝向对齐源项目（**B8**）：行=世界Y 竖直、层=世界Z 纵深
  （源里"竖向格线"即行线；文档 §2.2 的水平楼层朝向弃用），相机中心改用正确包围盒中心 (w/2, w/2, h/2)，
  网格竖线族归位 + 补层连接立柱（透明度 0.35 同源），悬停拾取改在竖直层平面（worldZ）求交。
- **奠基与收尾（2026-09-19）**：`Game/Theme/Palette.cs` 语义调色板（视图零颜色字面量，为色弱/明暗/主题奠基）；
  天元/地元锚点公式化 `(size-1)/2` + 三界点（禁硬编码）；偶数阶中心标注方案挂账（低优先级）。
  **M2 验收通过（人类 2026-09-19），tag `m2`**。两轮反馈八项 + 朝向 B8 全部落账。

## M1.5 — review 修复批（2026-09-19）

- 契约文档化：`IRuleEngine.GetNextTurnState`/`TurnState` XML doc 写死"currentPlayer=刚行棋方、swap 决策期指针不动、决策者=Message 受众白方"；修 SwapEngine 与测试注释口径。
- 存档健壮性：`SaveValidator` 行宽逐行校验（原只查每层首行，y>0 错配放行）；`SaveManager` 载入路径对 v2 直通档补 `Validate`，维度错配不再穿透进槽位缓存（+3 回归用例）。
- 规则层小收敛：`RuleEngineFactory.Create(RuleSet, winLength)`（Renju 未实装即抛 NotSupportedException）、`Board.IsFull()` 判和原语（+5 用例）。
- 测试清理：死字段/恒等三元/名不副实用例改名；计划文档 §10 修 B1 措辞（黑连下第 1、2 手，第 3 手起白方）、登记 B6（AI 等分策略 C# 种子随机池 vs TS 先到先得）。
- 验收：build 0 警告、测试 121 例全绿；按铁律 9 与交接文档一并 push 走 CI 门禁。

## M1 — 纯逻辑层（2026-09-18）

- 落地 `Nmoku.Core`：Board（扁平 byte[]，越界读 0/写忽略）、Rules（13 方向、winLength 4–9 可配）、Standard/SWAP2 规则引擎、存档 v2.0.0 兼容层（校验/v1 迁移/Base64/槽位门面+mock 适配器测试）、启发式 AI（评分表 + 半径候选 + 可取消异步）、EventBus、TextConstants(zh-CN)、PatternCollector。
- 验收方式：AI vs AI 100 手并入 xUnit（固定种子可复现）。
- **关键教训（读源 TS 核实，全文见 migration-plan §10）**：
  - B1 SWAP2 的 `getNextTurnState` 收到的 `currentPlayer` 是**刚行棋方**；决策期 shell 不改指针 → 黑连下 2、3 手才是官方节奏。
  - B3 旧单体档里 null 和缺 `id` 的条目都跳过，但**数组下标照常递增**（下标即槽位号）。
  - B4 `attack+1.1*defense` 忠实实现后，AI 在"成五点不同格"时会先堵不胜——源项目同式，M3 后如体感弱再议。
  - B5 辅助线"4 格上限"是渲染端裁剪，不是数据端；`GameConfig.HelperLineMaxCells`。

## M0 — 环境与脚手架（2026-09-18）

- 三项目解决方案（Core/Game/Tests）+ InputMap 26 动作 + 4 音频总线 + `.gitattributes`/`Directory.Build.props` + 霞鹜文楷 Lite TTF（源 woff2 本地无损转换，OFL）+ 存档夹具 10 件 + CI workflow。
- **关键教训（环境）**：
  - Godot 控制台壳按"去掉 `_console` 的同名 exe"定位主程序，重命名两边要成对。
  - SDK 10 的 `dotnet new sln` 生成 **`Nmoku.slnx`**（XML 新格式），SDK 8 解析不了 → CI `setup-dotnet` 钉 10.0.x，TFM 仍 net8.0。
  - 用户 PATH 改动不会进入已启动的 shell 会话；agent 会话用全路径 `/d/Tools/Godot/Godot_console.exe`。
  - `--headless --import` 会回写 `project.godot`，改前先 read 再合并。

## 更早

- 计划与决策：见 `docs/migration-plan.md` §0（8 项人类决策）与 §5（M0–M5 里程碑）。
