# CHANGELOG

> 维护规则见 `AGENTS.md`：只记"当前场景"相关事实；总量 ≤300 行，旧条目按里程碑压缩合并，但关键教训（踩坑结论）不删。
> 提交级细节看 `git log`，这里只留人读的里程碑叙事。

## 当前状态（2026-09-18）

- **里程碑**：M0 ✅ / M1 ✅（tag `m0`、`m1`）；下一站 M2（3D 左视图 + 相机）。
- **质量门禁**：`dotnet test` 113 例全绿；全解决方案 build 0 警告；Godot 4.6.2 无头冒烟 OK。
  Actions 双 job 全绿（`dotnet` + `godot-smoke`）。`godot-smoke` 两次踩坑已修：
  ① 跨 job 不共享构建产物，引擎启动前须 `dotnet build src/Nmoku.Game`（缺程序集时 C# 类找不到）；
  ② 冒烟断言误用数组 `-notmatch`——它返回"未命中元素集合"而非布尔，引擎横幅行必未命中，集合非空恒为真；
  断言须先 `-join` 成单串再匹配（本地 pwsh 复现：旧写法 exit 1 / 新写法 exit 0，输出逐行同 CI 日志）。
- **M1 review（2026-09-18）**：验收通过——口径属实（0 警告/113 绿）、红线全过、Swap2 与 AI 逐项对照源 TS 无保真偏差。
  修复批已完成（见「M1.5」节）。新规则拍板：Connect6 进待办（须先出接口演进设计）且优先级高于 Renju 禁手（调低）；
  开局库后置、须与状态机解耦（难则放弃）；演示关视为占位符。
- **架构红线**：`src/Nmoku.Core` 零 Godot 依赖，纯 C#（net8.0），xUnit 秒级运行。

## M3 — 2D 右面板 + 落子闭环 + 辅助线（2026-09-19，待人类验收）

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
