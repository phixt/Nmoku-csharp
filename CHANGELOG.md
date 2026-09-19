# CHANGELOG

> 维护规则见 `AGENTS.md`：只记"当前场景"相关事实；总量 ≤300 行，旧条目按里程碑压缩合并，但关键教训（踩坑结论）不删。
> 提交级细节看 `git log`，这里只留人读的里程碑叙事。

## 当前状态（2026-09-19）

- **里程碑**：M0–M3 ✅（tag `m0`–`m3`）；M4 🚧 第一轮壳全通 + **M4R 路由重构**（人类验收反馈返工，见下节）；剩 Palette 明暗/关卡/字体/验收轮。
- **质量门禁**：`dotnet test` 251 例全绿；build 0 警告；无头自测 AUTOTEST ALL PASS（M3 对局 + M4 状态机/存档/复盘 + M4R 路由/输入回归）。
- **架构红线**：`src/Nmoku.Core` 零 Godot 依赖（KeymapTable/GameStateMachine 均在 Core），xUnit 秒级运行。

## M4R — 路由重构（2026-09-19，人类验收反馈返工）

- **人类反馈三问题与根因**：①ESC 无响应——`HandleHeldInput` 顺序求值无 early-return，
  对局中按 ESC 同帧命中 `pause`（切 Paused）后 `resume`（同为 Esc）同帧 `IsActionJustPressed` 仍真 → 切回
  Playing，净效果为零；②R 一键弹暂停菜单+重启确认框——同根因（pause 后同帧 restart）；③标题屏能看见棋盘——
  TitleScreen 背景 ColorRect 漏设 FullRect 实为 0×0，且无任何背板/模糊体系。
- **修复与重构（参照 motion-cube 的 vue-router 模式）**：
  - **首中即吞**（§4.2.4）：单次动作表顺序求值，命中即 return，每帧至多一个——同键跨态双触发根除；
    `SimulateActionPress` 让 autotest 与真实输入共用同一分发路径（m4r-* 回归步）。
  - **`SceneRouter` 三层路由栈**（状态机不动，只做视图投影）：L0 基座/L1 模态/L2 浮层三个 CanvasLayer
    (0/10/20)，对话框层 30；`StackFor(GameState)` 唯一映射点取代散落的可见性硬编码；栈差分挂载/卸载；
    **KeepAlive 基座常驻树内仅切可见性**（game 保留现场=人类拍板；首次 Apply 全部 KeepAlive 入树的坑：
    只挂 target 会导致 game 从未 _Ready、_board2D 为 null，自测暴露）。
  - **背板体系**：每路由声明 None/Dim/Blur/Opaque；Blur 为 screen-texture 九点高斯 shader（单节点
    blur+dim）；各屏内部自带的背板节点全部移除（含 TitleScreen/GuideScreen 的 0×0 隐患）。
  - **保留现场语义**：标题新增"继续对局"（不重置直达 Playing）；"开始游戏"遇进行中保留局先确认覆盖；
    GuideFromTitle 的 Start 同走该确认。
  - **浮层冒泡**：浮层打开只响应 AllowWhenOverlay 键（F1/Esc），Esc 关最顶浮层；引导态 Esc 按 §3.4 两行
    语义（标题入口=返回/对局入口=回对局）。
  - **`KeymapTable` 快捷键单一数据源**（Core）：门控/游戏内 F1 `KeymapScreen`/docs `keymap.md` 三处同源；
    双作用域（状态/浮层）主键冲突检测 + "每个 InputMap action 必须登记"用例兜底漂移。
- **待人类验收**：真实窗口走查 ESC/R 单击单义、每条路由进出视觉（暂停/结算应有模糊背板）、
  标题→对局→标题→继续对局现场保留、F1 帮助屏、存档菜单浮层。

## M4R 修复批 — 人类首轮走查反馈（2026-09-19）

- **浮层残留（严重，"存档菜单取消无响应/Esc 后界面变化/引导文字固定在下层"共同根因）**：
  `SceneRouter.Unmount` 只经 KeepAlive 字典释放内容，销毁型路由（存档/暂停/引导/结算/帮助/复盘条）从不进该
  字典 → 内容节点卸载后**永久残留层内**，继续可见、继续吃点击；后续 PopOverlay 全部空操作。修复：内容统一
  登记 `_content`，Unmount 一律 QueueFree。回归自测：关浮层后断言旧实例跨帧被释放。
  **教训再次坐实：无头不渲染，残留面板类 bug 全靠真实窗口走查暴露。**
- **Q/E 抽搐**：Advance 用最短弧追踪 yaw，而 yaw 无界累积——跨 ±180° 后方向翻转。**行为变更（B12）**：
  Orbit 转角=累积意图，Advance 线性追踪原始差值；最短路径语义仅保留在 Reset（current 搬进 target ±180°
  邻域——归位公式须写 `target+ShortestArc(current−target)`，写反会在等效整圈时归零失效，测试抓住空转四圈）。
  另加 Q/E 0.2s 共享冷却（连按/键回显防抖）。M2 旧测试 `Advance_YawTakesShortestArc` 按新语义重写。
- **2D 高光穿透圆形边框**：高光斑偏移 0.88r + 半径 0.55r = 1.43r 超出棋子。收紧为偏移 0.42r + 半径 0.45r
  = 0.87r，完整落在轮廓内。
- **全屏发糊**：项目从未配置显示参数（Godot 默认 648p 基准）。现设 1920×1080 基准 + `canvas_items` 拉伸 +
  `expand` + `allow_hidpi`——UI 矢量渲染在窗口原生分辨率，高分/全屏清晰。

## M4R 修复批 2 — 二轮走查反馈（2026-09-19）

- **Q/E 单击转 180°**：键回显（长按触发的 echo 事件）会让 `IsActionJustPressed` 在回显帧再次为真，
  0.2s 冷却挡不住 0.25s+ 的系统重复延迟。**单次动作全部改事件驱动**（`_Input` 键事件 + `Echo` 过滤 +
  匹配即吞）：全表与 A/D 一致"按一下动一次"，冷却机制移除；轮询路径只剩按住生效类（zoom/pan/spacing）。
  合成键自测（`Input.ParseInputEvent`）锁定：非 echo 转 90°、echo 不触发。事件化后同键跨态分发
  （R=pause/restart/exit_replay）按 KeymapTable 声明序取本状态合法动作，语义不变。
- **高分屏仍是逻辑分辨率（3200×2000 只渲染 1600）**：两层根因——①窗口 override 1600 恰为屏宽一半，
  已去掉（默认=基准 1920）；②**`SubViewportContainer.stretch` 只把子视口设成逻辑像素**，3D 视图渲染在
  低于原生再被拉伸（web 查证为 Godot 已知行为）→ `SyncViewport3DResolution` 手动乘窗口最终变换缩放至
  原生像素。启动日志新增 `display:` 行（window/screen/dpi/canvasScale/3D 视口）用于诊断 DPI 感知是否生效；
  若 window 为逻辑像素，须检查 exe 的 Windows 兼容性 DPI 设置（[Godot 论坛同款问题](https://forum.godotengine.org)）。

## M4R 修复批 3 — 切窗音频报错与远景锯齿（2026-09-19）

- **WASAPI GetBufferSize/invalidated 报错（切窗口时）**：引擎驱动层已知行为——WASAPI 共享模式下默认
  输出设备失效/切换时音频线程关闭重连（[godotengine/godot#18732](https://github.com/godotengine/godot/issues/18732)
  一脉），设备重连抖动共享混音即"其他窗口音频撕裂"。本项目代码零音频（M0 只建总线），无触发点也无修复点。
  **处置**：`driver_type="Dummy"` 临时让引擎不占用音频设备（报错与撕裂即消失）；**M5 恢复 WASAPI 并复测**
  （TODO 已挂账）。若 M5 仍复现，查 `audio/driver/output_latency` 与上游修复版次。
- **3D 远景底端锯齿/发糊**：细网格线盒（0.05 格）+ MSAA 4× 在掠射角下不足。MSAA 4×→**8×**
  （场景极小，开销可忽略）+ 线厚 0.05→**0.06**。若目视仍不满意，下一档是网格线距离淡出（设计项，走 R4 流程）。

## M4 第一轮 — 游戏壳全通（2026-09-19，代码完结，M4 第二轮转新对话）

- **Core**：`GameStateMachine` 7 态 + §3.4 全转移表（非法组合拒绝，穷举测试）；
  `NzSaveContainer`（.nzsave zip 容器：manifest.json + savedata.json，replay.png 预留位）+
  `SaveArchive.TryParse` 统一嗅探（裸 JSON / Base64(JSON) / Base64(zip) / Latin-1 zip 字节四路）；
  `Board.ToJaggedArray()` 存档导出原语；`IOverlayManager` 接口按 §1 依赖方向落 Core（纯 C# 委托）。
- **Game**：`GameStore`（状态机宿主：转移副作用集中在动作方法，读档失败整转移取消）+
  `FileSystemStorageAdapter`（user://saves：index.json + <id>.sav，覆盖槽位清旧 blob）+
  `ShortcutRouter`（§4.2.2 context 门控；相机/视觉键 PLAYING+REPLAY，玩法键 PLAYING）+
  `OverlayManager` Godot 实现（模态互斥拒绝第二调用、回调恰好一次、toast 堆叠 2.5s 不抢焦点；
  扩展 onCancel+按钮文案——SWAP2 的"取消=继续执白"是有效决策）。
- **壳屏六件**：TitleScreen / PauseMenu / GameOverModal / SaveMenu(6 槽+载入/复盘/导出/删除/导入) /
  GuideScreen(静态占位文案) / ReplayBar(步进+进度条)，状态驱动可见性；Ctrl+S 快存、L 读最新、
  剪贴板导出（Base64 v2，与源浏览器互通）/导入（统一嗅探落首个空槽）。
- **GameManager 扩展**：`BuildSaveData`（回合/SWAP2 决策期由引擎从 history 重推导，不信任存档中间态字段；
  focusZ 读入即弃复位层 0）/ `LoadFrom`（棋盘尺寸/规则/模式/AI 随档重配）/ `BeginReplay/SetReplayIndex`（复盘态门控落子/悔棋/AI）。
- **关键教训**：
  - §3.4 的 **Any→REPLAY** 是 7 个态全部可达——首轮实现只给了 Replay 自环，无头自测 `m4-save-load-replay`
    一步暴露（Playing 进不了复盘）。转移表改动必须以文档逐行为准，穷举测试是安全网。
  - Main.cs 重写时漏 `using Nmoku.Core.Saves`，报错表现为 tuple/lambda 的 CS1503 级联噪音，
    真实错误被吞——**块级 lambda 抽成私有方法后错误才浮出**。
  - ImplicitUsings 使 `FileAccess` 歧义（System.IO vs Godot），Game 侧用 `using FileAccess = Godot.FileAccess` 别名收敛。
  - manifest.json 键序按 §3.4 示例走 camelCase（默认序列化是 PascalCase，TestFirst 抓住）。
- **待人类验收**：真实窗口走七态（标题 Space 开始/暂停 Esc/重开确认框/Guide 两入口/结算模态）；
  Ctrl+S→L 往返、存档菜单导出→（源浏览器版）导入互转、复盘 ←/→ 与进度条；视觉与手感反馈。
- **M4 第二轮待办**：见 TODO「当前」节（Palette 明暗/关卡系统/字体/FileDialog/真实样本/§6.3-6.5 核对 + tag `m4`）。

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
