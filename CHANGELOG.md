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

## M4 批11 — 任务栏遮挡根治调查（2026-09-26，阶段成果：机制未捕获，取证协议就位）

- **几何基线（真实窗口实测）**：窗口 1920×1080 居中 (640,460)，底部 1540；工作区 3200×1904——
  **静态余量 364px**，正常态不可能被遮挡；被遮挡 ⇔ 窗口被外力移动/缩放 ≥364px。
- **自动化排除面**：全部状态转移 + 存档菜单/FileDialog 开关 + 最小化/还原 + 最大化（Godot 最大化
  正确落在工作区 bottom=1904）——全量窗口套件 ALL PASS 全程零几何漂移。机制在自动化触达之外
  （用户拖拽/他应用焦点/DPI 事件/多显示器），**禁止盲修**（自愈类补丁=人类禁的遍历式打补丁）。
- **机制定案+根治（同日人类日志）**：游玩中相机拖拽划出窗口顶部压到标题栏 → Windows 接管拖动 →
  拖至屏幕顶 Aero-Snap 最大化（来回拖=来回翻转；最大化中间态下底部按钮贴任务栏=「遮挡」观感）——
  佐证「开始游戏-退出」不触发、「下几个子」必现。根治=拖拽期间 `Input.MouseMode=Confined`
  （光标裁剪客户区，物理上不可能逃逸到标题栏），松开/失焦还原。
- **取证协议（B16 式）**：`TraceWindowGeometry` 常开——窗口 pos/size/mode/工作区任一变化即刻落
  `src/Nmoku.Game/.trace/window-trace.log`（带状态关联）；人类正常游玩复现后交出该文件即得机制证据，
  再一次修复。回归步 m4c-window-sequence 长期看守（headless 跳过、窗口化断言几何稳定）。
- **顺带修复两个真实缺陷**：①主自测 store 曾跑在真实存档目录（AutotestSaveDir 死常量，清槽步骤会
  毁真实存档）→ 自测已隔离到 saves_autotest；②m4-import / m4b-nzsave-file-roundtrip 的"首空槽=0"
  断言依赖共享目录残留 → 自备目录+预清理。
- 门禁：build 0 警告 / 302 测试 / 无头+真实窗口双全量自测 ALL PASS。

## M4 验收反馈批10 — 复盘界面修复批（2026-09-26）

- **行棋指示器统一**（人类拍板）：新 TurnIndicator 右下角——虚拟棋子在上、文字在下（字号 16→14），
  对局/复盘两态共用（复盘首次获得该指示）；B13 右上角假棋子与底部居中状态字退役（CenterBottom
  同锚重叠根除）。复盘态优先显示游标行棋方（即便载入已结束档）；结算/SWAP2 提示为纯文字态。
- **环随游标**：GM 新 `LastVisibleMove`（对局=末手，复盘=游标前一手，0 手=无）；2D 环与 3D 红环换源，
  修复复盘中环冻结在整局最后一手。行棋方随游标（ApplyReplayCursor，0 手=黑先）——"ghost 恒黑"根因。
- **复盘 ghost/辅助线**：ghost 色随游标行棋方、降透明（0.35→0.21）呈不可落子暗示；ComputeAux 放开
  复盘拦截，辅助线随游标棋盘扫描（3D 同步受益）。棋子四层绘制抽 `StonePainter` 共用（纹理静态缓存）。
- **ReplayBar**：滑条 280→200 修右侧出界；0 手复盘滑条/步进禁用。新回归 `m4c-replay-hud`：几何断言
  （不出界/不与指示器重叠）+ 0 手适配 + 游标环/行棋方断言。
- 门禁：build 0 警告 / 302 测试 / 无头自测 ALL PASS。

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

## M4 验收反馈批 6–8 — 快捷键分页定稿与渲染精度（2026-09-26，已压缩）

- 批6：分层=分页定稿（六页/圆圈页码/键帽注记 ≤3 字 `KeymapEntry.Short` 单源+排版防错锁）。
- 批7：2D 棋子四层球面质感+格线硬边、3D 视口 ×2 超采样、翻层 NRE（引导屏未挂载解引用）修复、
  页码固定、无色占位键。
- 批8：暗环纹理改**逐像素圆内遮罩**（圆外强制透明——浅底"超界方框"根除；方形径向渐变的固有缺陷，
  同 §10 B18 源项目同病教训）、辅助线/红圈减细。
- 各批门禁：build 0 警告 / 298 测试 / 无头自测 ALL PASS。

## M4 验收反馈批 4+5 — UI 缩放定稿与快捷键入口修复（2026-09-25，已压缩；细节见 §10 B16 与 git log）

- 修复「没看到快捷键页」：标题/暂停 G 开的是引导占位页——抽共用 KeymapContent，引导屏与 F1 同内容。
- UI 缩放两版迭代（批 4 被 5 否）：定稿=窗口固定 1920×1080 + ContentScaleFactor=2.5 绝对值
  （等效基准 768×432）；"非常糊"根因=v2 超采样降采样回逻辑纹理再放大——3D 纹理必须原生像素。
- 引擎事实：headless 下 ConfirmationDialog 不会自动确认——确认框语义用 ConfirmDialogForTests 合成驱动。

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

## M4 收尾轮 — 工程项全清（2026-09-19，已压缩；细节见 git log 与 migration-plan §10）

- **B14/R6/契约核对/真实旧档恢复**全落地：winLength 外圈元数据双通道（.nzsave manifest + 槽位
  index）、源项目浏览器双向导入导出闭环（真实样本入库）、m4c-overlay-contract / modifier-quicksave /
  real-old-restore 回归、真实旧档导入端到端成五。
- **教训**：Godot 启动会把进程 CWD 切到项目目录（夹具用 CurrentDirectory 双候选探测）；
  ImplicitUsings 的 FileAccess/Environment 歧义一律写全称；浏览器自动化=合成 mousemove+click
  （悬停门控）、暂停菜单=合成 KeyboardEvent、源项目存档真源=localStorage `gomoku_save_<uuid>`。

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
