# Gomoku 613B-E2D → Godot C# 迁移计划

> 源项目：`D:\P_rojects\gomoku-613B-E2D`（TypeScript + Three.js + Vite + Tauri，v0.6.4）
> 目标项目：本仓库（Godot 4.6.x .NET / C#，Windows 桌面 x64）
> 依据文档：源项目 `docs/architecture-00~04`、`audio_var.md`、`test-and-cicd-plan.md`、README
> 状态：计划已评审定稿（2026-09-18），待 M0 环境搭建

---

## 0. 已确认的决策

| # | 决策项 | 结论 |
|---|--------|------|
| 1 | Godot 版本 | **Godot 4.6.2-stable .NET 版**（win64），配套 **.NET 8 SDK** |
| 2 | 目标平台 | **仅 Windows 桌面**，不做 Web 导出 |
| 3 | 视觉还原 | 允许适度重设计；主题/UI 重构为**调色板驱动**系统，首发只做一套调色板 |
| 4 | 存档 | 第一版即兼容读取旧版 **v2.0.0 JSON**（含 v1 迁移）；同时提供 JSON 明文导出 + **自建 zip 容器格式**导出 |
| 5 | 简单项直接做 | 色弱模式、明暗切换按源项目语义直接实现 |
| 6 | 文本 | 先建 `TextConstants` 字符常量文件，后续平滑扩展为 i18n（四语言） |
| 7 | 音频 | 第一版用导入的 WAV/OGG 资产；**保留合成器接口**（`IMusicEngine`），以后可回填程序化合成 |
| 8 | 文档未覆盖部分 | 辅助线系统与 2D 右面板与相机强耦合，**谨慎处理、放独立里程碑**；实现时允许局部读源码（`RightPanel.ts`、`LeftPanel.ts`、`main.ts` 输入部分） |
| 9 | M2 自由相机（2026-09-19） | 在文档八步管道之上**超越源项目**：无级旋转（拖拽连续，非 90° 步进）、视角平移（源因 bug 移除，现恢复）、指数阻尼平滑（替代"设定即所得"，§3.2 原注记作废）；平移限幅防丢棋盘；X 复位回公式态。绑定：方向键/中键拖=平移、左键拖=旋转（InputMap 数据可调）。相机平移旋转与"棋盘平移旋转"互为对偶，实现取相机侧 | 人类指示 2026-09-19 |

---

## 1. 总体策略

源项目不是 Unity 工程，**没有任何引擎资产可搬运**。好在架构文档达到伪代码级完备，因此采取：

1. **逻辑层按文档重写**：`00-domain / 01-core / 02-rules / 04-contracts` 覆盖了全部游戏逻辑，逐条翻译为 C#，用 xUnit 测试锁行为（测试用例清单直接取自 `test-and-cicd-plan.md` §4）。
2. **渲染层按文档重实现**：相机管道（`03-camera`）数学公式全部给出，Three.js → Godot 只是 API 换皮；注意 3 处引擎差异（见 §4.6、风险 R1–R3）。
3. **UI 层适度重设计**：DOM/CSS → Godot `Control` + Theme；一次性还清"调色板系统"的设计债，但首发只填一套色。
4. **纯逻辑与引擎解耦**：核心算法放在**不引用 Godot 的类库**里，测试项目秒级运行、不启动引擎。

### 1.1 解决方案结构（.NET）

```
Nmoku.sln
├── src/Nmoku.Core/          # 纯 C# 类库（net8.0，零 Godot 依赖）
│   ── 棋盘/规则/AI/存档数据模型/事件总线/相机数学/模式收集
├── src/Nmoku.Game/          # Godot C# 项目（net8.0，引用 Nmoku.Core）
│   ── 场景、节点脚本、输入、UI、音频、存储适配
└── tests/Nmoku.Core.Tests/  # xUnit（net8.0，引用 Nmoku.Core）
```

Godot 项目文件（`project.godot`、场景、资产）与 csproj 同置于 `src/Nmoku.Game/`；仓库根再放 `docs/`、`assets-src/`（外部素材与许可）。

### 1.2 源项目 → 目标模块映射总表

| 源文件（TS） | 目标（C#） | 方式 | 里程碑 |
|---|---|---|---|
| `core/Board.ts` | `Nmoku.Core/Board.cs` | 按 01-core §1 重写（`byte[]` 扁平存储） | M1 |
| `core/Types.ts` | `Nmoku.Core/Types.cs`（enum/record） | 按 00-domain 重写 | M1 |
| `core/Config.ts` | `Nmoku.Core/GameConfig.cs` | 常量表照搬（§5 附录） | M1 |
| `core/Rules.ts` | `Nmoku.Core/Rules.cs` | 按 02-rules §1/§5 重写（13 方向、Pattern3D） | M1 |
| `core/rules/IRuleEngine.ts` | `Nmoku.Core/IRuleEngine.cs` | 按 04-contracts §2.1 | M1 |
| `core/rules/StandardEngine.ts` | 同上目录 | 02-rules §2.4 | M1 |
| `core/rules/SwapEngine.ts` | 同上目录 | 02-rules §2.4/§3 | M1 |
| `core/SaveManager.ts` | `Nmoku.Core/Saves/SaveData.cs` + `SaveMigrator.cs`（schema/校验/v1→v2 迁移/Base64） | 01-core §6 + 04-contracts §3.3 | M1 |
| `core/StorageAdapter.ts` | `Nmoku.Game/Persistence/FileSystemStorageAdapter.cs`（实现 Core 中接口） | 04-contracts §3.1 | M4 |
| `ai/AIEngine.ts` `ai/IAIEngine.ts` | `Nmoku.Core/AI/` | 02-rules §4 + 04-contracts §2.2 | M1（算法）/M3（接线） |
| `core/EventBus.ts` | `Nmoku.Core/EventBus.cs`（静态 C# event） | 04-contracts §5 | M1 |
| `core/GameStore.ts` | `Nmoku.Game/GameManager.cs`（Node，状态机宿主） | 01-core §3 | M4 |
| `utils/MathUtils.ts` | `Nmoku.Core/Camera/CameraRig.cs` 等 | 03-camera 全量 | M2 |
| `views/LeftPanel.ts` | `Nmoku.Game/Views/Board3DView.tscn` + 脚本 | 03-camera | M2 |
| `views/RightPanel.ts` | `Nmoku.Game/Views/Board2DView.cs`（Control 自绘） | **文档缺口，需读源码** | M3 |
| 辅助线（两面板内） | `Nmoku.Game/Views/HelperLinesService` + 两视图渲染器 | 02-rules §5 + 需读源码 | M3 |
| `ui/OverlayManager.ts` | `Nmoku.Game/Ui/OverlayManager.cs` | 04-contracts §4.1 | M4 |
| 快捷键表 | `project.godot` InputMap + `Nmoku.Game/Input/ShortcutMap.cs` | 04-contracts §4.2 | M3/M4 |
| `core/I18n.ts` + `locales/*.ts` | `Nmoku.Game/Text/TextConstants.cs` →（M5）Translation CSV | 用户决策 #6 | M1/M5 |
| `audio/AudioManager.ts` | `Nmoku.Game/Audio/AudioManager.cs` + `IMusicEngine` 接口 | audio_var.md；资产化路线 | M5 |
| `styles/*.css`、主题色 | `Nmoku.Game/Theme/Palette.cs` + Godot Theme 资源 | 重设计为调色板系统 | M4 |
| `public/fonts/LXGWWenKaiLite-Regular.woff2` | 换用官方 TTF（OFL 许可）导入 | — | M4 |
| Tauri 剪贴板/窗口 | `DisplayServer.ClboardGet/Set`、项目设置 | 平台 API 直换 | M4 |
| Vite/tsc 质量门禁 | xUnit + `dotnet format` + （可选）GitHub Actions | test-and-cicd §5 思路平移 | M1 起 |

---

## 2. 命名与写法约定

- 伪代码函数 → C# 方法一律 PascalCase：`checkWinner→Rules.CheckWinner`、`Get/Set→Board.Get/Set`、`LogicalToWorld→CoordMapper.LogicalToWorld`。
- 值对象用 `record struct`（`Coordinate3D`、`Pattern3D` 列表成员用 `readonly struct`）；`Move` 可变性遵循 00-domain §1.3（Swap 时允许原地换色）。
- 域内不使用 Godot 类型；`Vector3` 只在渲染/相机层出现，Core 用自有 `(int x, int y, int z)`。
- 枚举对齐源常量：`CellState { Empty=0, Black=1, White=2 }`（存档值直接兼容）、`PlayerColor { Black, White }`。
- 每个 Core 公共类型必须有对应测试文件（`XxxTests.cs`），命名 `Method_Scenario_Expectation`。

---

## 3. 关键技术决策

### 3.1 逻辑层（M1，全部在 Nmoku.Core）

- **Board**：`byte[] data`，`Index = z*s² + y*s + x`（01-core §1.3 原样）。越界读返回 0、越界写忽略——这是测试点（01-core §1.3 语义）。
- **规则引擎**：`IRuleEngine` 三方法签名照 04-contracts §2.1。Connect6 只作为扩展点保留接口位置，**第一版不实现**（源项目亦未实装）。
- **Swap2**：完整照 02-rules §2.4/§3，含"预置子跳过 swap"守卫（4.7）。
- **存档模型**：`SaveData` 用 `System.Text.Json`，字段名与 v2.0.0 schema 逐一对齐（camelCase `JsonNamingPolicy`），保证双向可读。`focusZ` 读入即弃。v1 旧档（只有 `moves`）走 `SaveMigrator.MigrateV1` 重建 `boardState`。
- **AI**：评分表/候选集/难度参数全部来自 02-rules §4，无自由发挥。异步契约：`Task<Coordinate3D?> GetBestMoveAsync(board, player, ct)`，内部 `Task.Run` + 协作式取消（04-contracts §2.2.3 要求 100ms 内响应取消）；调用侧回到主线程后再验一次 `IsLegalMove`。
- **EventBus**：静态类 + `event Action<T>`，事件与 payload 按 04-contracts §5 表逐一建 record。Godot 侧不引入 signal 转译层（统一走 EventBus，保持 Core 纯净）。
- **TextConstants**：`public static class Text`，按域分组嵌套类（`Text.Swap.Message` 等）。值为简体中文（源项目默认语言）。M5 时调用点改为 `Text.T(key)` 语义不变的查找。

### 3.2 相机与 3D 视图（M2）

- `CameraState/CameraRig`（Core 内纯数学）：03-camera §9 的 8 步管道逐行实现，输入 `(boardSize, layers, layerSpacing, aspect, yaw)`，输出 position/fov/near/far/center。缓存结构 `CameraCache` + `NeedsRecompute`（§8.1）保留，按增量脏标记重算。
- **FOV 语义**：Three.js `PerspectiveCamera.fov` 与 Godot `Camera3D.Fov` 均按垂直方向定义，但 Godot 在 `keep_aspect` 覆盖与宽高比 <1 时行为不同——**M2 第一周用 13×13×6 默认棋盘做数值对照标定**（同一组输入打印两边参数），必要时把 §9 STEP 5 的 `fovLimit` 计算保持不变、只核对 `aspect` 传入方向（文档已自带水平 FOV 换算式，风险可控）。
- 相机节点：`Camera3D` 直接设 transform；~~不用 look_at 之外的任何阻尼/平滑~~（**已由决策 #9 取代**：Core `CameraRig.Advance` 指数阻尼，无级旋转 + 视角平移）。
- **坐标映射**：`LogicalToWorld(x, y, z) → new Vector3(x, z * layerSpacing, y)`。Three.js 与 Godot 同为 Y-up 右手系，但**源世界 Z（forward）与 Godot 观察朝向 -Z 的关系需在 M2 用贴边五连的可视化验证**（yaw=45° 对角视角下图象是否镜像）。若镜像，只翻转映射函数一处（集中在此函数，禁止散落）。
- **棋子渲染**：`MultiMeshInstance3D` ×4（黑/白 × 不透明/ghost），几何用 `SphereMesh`（段数按性能调），实例数据每帧按需重建（13×13×6=1014 上限极小，全量重建成本可忽略，**不做增量**）。透明度按 `ComputeOpacity(worldY)`（§8.2，GHOST_OPACITY_MIN=0.25）。
- **透明排序**：文档 §9.4 要求 ghost 按距离降序绘制。Godot 对同一 MultiMesh 内实例不做逐实例排序，方案：ghost 组使用 **`StandardMaterial3D` transparency = alpha_sc mix + `depth_draw` 调整**，接受逐实例混合的非严格排序（球体小、opacity 0.25–1.0，视觉误差极小）；若实测出现明显瑕疵，降级为"每 ghost 层一个 MultiMesh 按层排序"（层数 ≤ 视觉分组数，成本仍低）。**列为验证点，不提前优化。**
- 网格线/层连接线：一次性构建单个 `ArrayMesh`（`PrimitiveType = PRIMITIVE_LINES`），`layerSpacing` 滑杆改变时重建（≤ 2×13×6 条线，重建 <1ms）。
- 悬停定位蓝点（左视图）：M2 先做"当前焦点层半透明高亮面片"占位，精确拾取联动画像在 M3 与辅助线一起处理。

### 3.3 2D 右面板与输入闭环（M3）

- `Board2DView : Control`，`_Draw()` 自绘：网格、焦点层棋子（非焦点层不画或用淡化描边——以源码行为为准）、最后一手标记、悬停预览子、辅助线。坐标为纯数学（无物理/碰撞节点）：`鼠标坐标 ↔ (x,y)` 网格反算，含安全边距（对应 v0.3.2-fix）。
- `F` 键水平镜像、`A/D` 换层、点击落子 → 走 `GameManager.PlaceStone` 统一入口。
- **辅助线（文档最薄弱 + 与视图强耦合，单列子任务）**：
  - 数据端已有：`collectLayerPatterns3D`（02-rules §5）+ "4 格检测范围限制"（README）→ 在 Core 实现 `PatternCollector.Collect(board, layerZ, maxLen=4)`，输出与颜色无关的 `Pattern3D`。
  - 展示端需读 `RightPanel.ts`/`LeftPanel.ts` 确认：己方绿/敌方红（色弱：黄/蓝）、3D 蓝点联动、`H` 开关语义。做**一份数据、两个渲染器**（2D 线段 / 3D `Path3D`-或线段 Mesh），杜绝双份逻辑。
- 输入：全部快捷键进 InputMap action（04-contracts §4.2.2 表逐行建），`ShortcutRouter` 按当前状态机 context 门控（§4.2.3/§4.2.4：匹配即吞掉事件）。

### 3.4 游戏壳与状态机（M4）

- `GameManager : Node` 持有 01-core §3 的 7 态枚举 + 转移表（§3.4 逐行实现，非法转移 `GD.PrintErr` + 拒绝）。UI 场景以 `CanvasLayer` 分：Title / Hud / PauseMenu / GameOverModal / SaveMenu / Guide / ReplayBar。
- `IOverlayManager`（04-contracts §4.1）：`ShowConfirmDialog/ShowInputDialog/ShowToast/CloseAll`，回调契约（模态互斥、Esc 取消、回调恰好一次、toast 不抢焦点）写进验收清单。
- 存档 UI：6 槽位（5=快存）、`L` 读最新、`Ctrl+S` 快存、Base64 导入导出（剪贴板走 `DisplayServer.ClboardGet/Set`）。
- **新自建导出格式 `.nzsave`（zip 容器）**：
  ```
  manifest.json      # { "format": "nzsave/1", "createdAt", "appId": "nmoku.godot" }
  savedata.json      # 内层仍是 v2.0.0 schema（版本演进时加 schema 字段 + 迁移器）
  replay.png?        # 预留：棋盘快照缩略图（可空）
  ```
  读档入口统一嗅探：zip magic `PK` → 容器路径；`{` → JSON 路径；Base64 解码后两者皆可。
- 关卡：`LevelConfig`（01-core §7）+ `tutorial`/`residual` 两内置关 + 沙盒自定义（尺寸奇数≥5、层数、连珠 4–9）设置面板。
- 主题/调色板系统（重设计项）：`Palette : Resource`，字段=语义色（bg/board/grid/line-own/line-opp/ghost/…）+ 明暗两套派生；首发一套"水绿宣纸"light + 现有 dark 映射。`T` 切明暗、`V` 色弱（红绿→黄蓝在 Palette 中是第二组 line 颜色，运行时换组）。JSON 序列化 Palette 为**后续自定义调色板**留门，本版不做编辑器 UI。

### 3.5 音频 / i18n / 发布（M5）

- `AudioManager : Node`：3 个 `AudioStreamPlayer` 总线（BGM/SFX/UI → Godot AudioBus），`PlayBgm(Menu|Game|Pause|Guide)`、`PlaySfx(Click|Place|Win|Lose|...)`、`SetVolume`、`Mute`（语义对齐 audio_var.md §六）。
- `IMusicEngine { Start(track); Stop(); SetIntensity(float); }`：默认实现 `SampleMusicEngine`（导入 OGG 循环轨，占位素材先用 CC0 替代品，**补全音乐**沿用源项目 TODO）；`SynthMusicEngine`（AudioStreamGenerator 复刻和弦合成器）留空实现挂后续版本。
- i18n：`TextConstants` 键 → Godot `CSV` 翻译表（zh-CN 为源语言），`I18n.ts` 的缺 key fallback 语义由 `TranslationServer` + 自测覆盖；语言切换入口放设置面板（四语言：zh-CN/zh-Hant/en-US/ja-JP）。
- 导出：Windows x64，`export_presets.cfg` 进仓库；单 exe + pck 或 one-file 打包为 zip 发布。

---

## 4. 常量与数据对齐表（单一事实源）

| 常量 | 值 | 出处 |
|---|---|---|
| DEFAULT_BOARD_SIZE / LAYER_COUNT / WIN_LENGTH / LAYER_SPACING | 13 / 6 / 5 / 3.5 | 01-core 附 B |
| SAVE_SLOT_COUNT / QUICK_SAVE_INDEX | 6 / 5 | 01-core 附 B |
| CELL_EMPTY/BLACK/WHITE | 0/1/2 | 01-core 附 B |
| 层间距调节范围 | 2.0–6.0（X 复位到最近非调整值） | README |
| DIRECTIONS_3D | 13 向量 | 02-rules §1.2（顺序也照抄，测试依赖） |
| AI 分数表 | 1e6/1e5/1e4/1e3/100/10，defense ×1.1 | 02-rules §4.3/4.4 |
| AI 难度候选半径 / 思考时间 | Easy 仅堵抢点 200–500ms；Med r=1 100–300ms；Hard r=2 50–200ms | 01-core §8、04-contracts §2.2 |
| MIN/MAX_FOV、MIN/MAX_PITCH | 30/60、35/85 度 | 03-camera |
| ASPECT_SENSITIVITY / SAFETY_MARGIN / ZOOM_SPEED | 2.5 / 1.3 / 0.15 | 03-camera |
| MIN/MAX_VISUAL_SCALE、BASE_* | 0.8/1.2；13/6/3.5 | 03-camera |
| GHOST_OPACITY_MIN | 0.25 | 03-camera |
| 距离上下限 | base×0.3 / base×3.0 | 03-camera §6.1 |
| 辅助线检测上限 | 4 格 | README 特性节 |
| 连珠数范围 / 边长约束 | 4–9；奇数且 ≥5 | 00-domain 2.2/2.8 |

**MoveHistory / SaveData schema 与 v2.0.0 逐字段一致**（04-contracts §3.3），不增删字段；新字段一律走 zip 容器 `manifest.json` 的外围元数据，避免污染兼容层。

---

## 5. 里程碑

> 每个里程碑结束都要求：`dotnet test` 全绿 + 游戏可运行至该阶段验收场景 + git tag（`m0`…`m5`）。

### M0 — 环境与脚手架（半天～1 天）
- [ ] 安装 .NET 8 SDK、Godot 4.6.2 .NET win64（指南见 §7）
- [ ] `Nmoku.sln` + 三项目（Core/Game/Tests），Game 侧跑通空场景 `Main.tscn`
- [ ] xUnit 冒烟测试 + `.gitignore`（Godot 专属见 §6）+ 首次提交流程
- **验收**：Godot 编辑器打开无 cs 加载错误；`dotnet test` 1 个恒真测试通过；F5 从 Godot 运行空窗。

### M1 — 纯逻辑层 + 测试基线（预计最重的"确定性"部分）
- [x] Types/Config/Board/EventBus/Rules(13 方向+Pattern)/Standard+SwapEngine/SaveData+校验+v1 迁移+Base64/AIEngine(Core 版,同步)/PatternCollector(maxLen=4)/TextConstants(zh-CN)
- [x] 测试：把 `test-and-cicd-plan.md` §4.1–4.5 清单逐条翻译为 xUnit（含跨层斜线五连、六连、winLength 可配、Swap2 守卫、AI 合法性、存档往返/损坏容错）
- [x] 04-contracts §6.1/6.2(除时间预算)/6.3(mock adapter) 清单打勾
- **验收**：控制台跑通"AI vs AI 100 手自洽对局"无异常（已并入 xUnit `AIVersusAI_HundredMoves_AllLegalAndNoException`，种子固定可复现）；测试报告覆盖 Core 公共 API（113 passing，2026-09-18）。

### M2 — 3D 左视图 + 连续相机
- [ ] CameraRig(Core) + 标定对照（§3.2）；Board3DView 场景：MultiMesh×4、线 Mesh、ghost 材质与排序方案验证
- [ ] 交互：Q/E 旋转、W/S 与滚轮缩放（zoom-to-anchor）、Z/C 层间距、X 复位；S 键拉远提示语场景可复现
- [ ] 悬停层高亮占位
- **验收**：与源项目并排开（`npm run dev`），同参数（默认盘、9×9×1、13×13×6 spacing 6.0）下相机构图目视一致性记录截图；FPS ≥ 60。

### M3 — 2D 右面板 + 落子闭环 + 辅助线
- [ ] Board2DView 自绘 + 拾取 + 安全边距；A/D 换层、F 镜像、点击落子、最后一手标记、悬停预览
- [ ] GameManager 接线：回合、合法性、胜负、GAME_OVER
- [ ] 辅助线：PatternCollector → 2D/3D 双渲染器，H 开关、V 色弱、蓝点联动；4 格上限与性能约束
- [ ] 快捷键全表进 InputMap + ShortcutRouter context 门控；Ctrl+Z 悔棋
- **验收**：本地双人完整对局（含 SWAP2 分支两条路径）可用；辅助线颜色/开关/镜像行为对齐源项目。

### M4 — 游戏壳：状态机、UI、存档
- [ ] 7 态状态机 + 全转移表 + 暂停/标题/结算/引导(静态文本版) UI
- [ ] FileSystemStorageAdapter(user://) + SaveManager 槽位/快存/L/Ctrl+S + zip 容器 .nzsave + 导入导出（剪贴板 `DisplayServer.ClipboardGet/Set`）
- [ ] 关卡（tutorial/residual/沙盒自定义面板：尺寸/层数/连珠 4–9）
- [ ] 复盘 REPLAY（步进出/入、进度条）；OverlayManager 契约；Palette 系统 + 明暗 + 首发水绿宣纸单套色；霞鹜文楷 TTF 导入
- **验收**：04-contracts §6.3/6.4/6.5 全勾；**读源项目真实 v2.0.0 旧档恢复棋局**（从浏览器版导 Base64 互转）；复盘与源版本行为一致。

### M5 — 音频、i18n、发布
- [ ] AudioManager + 采样版 BGM（4 轨循环）+ SFX 全集 + M 静音 + `IMusicEngine`/`SetIntensity` 接口就位（合成器留壳）
- [ ] TextConstants → CSV 四语言 + 语言切换
- [ ] 导出配置、Windows 打包 zip、README/CHANGELOG
- **验收**：安装包冒烟清单（test-and-cicd §L3 的 7 条 E2E 流程手动过一遍）；导出存档→删除→导入往返。

---

## 6. 仓库约定

- `.gitignore`：`.godot/`、`bin/`、`obj/`、`*.glb.import` 不需要——**import 元数据（`*.import`）要提交**；`.vs/`、`.idea/`、`export/`（导出产物）、`*.exe`。
- 提交信息：中文短句 + 前缀（`M1: 移植 Rules 13 方向判定`）。
- 源项目参照只读：不把 TS 代码复制进本仓库（许可 MIT 允许，但保持清洁室姿态，仅逻辑按文档重写；`assets-src/` 收纳字体/图标等**素材**并记录许可）。
- ✅ GitHub Actions CI 已就位（`.github/workflows/ci.yml`：dotnet build/test 门禁 + Godot headless 冒烟），待仓库推送远程后生效。
- ✅ 存档测试夹具已入库（`tests/Nmoku.Core.Tests/Fixtures/`，R6 前置完成；M4 时再补真实浏览器导出样本闭环）。

## 7. 环境搭建（M0 操作步骤，对话中会逐步陪同）

> 多 agent 协作规约（命令、铁律、进度表）见仓库根 `AGENTS.md`；本项目不引入 MCP，全部 CLI 化。

1. **.NET 8 SDK**：✅ 已装（8.0.425，与 10.0.401 并存，TFM 统一 net8.0）
2. **Godot 4.6.2 mono win64**：✅ 已装于 `D:\Tools\Godot\`（`Godot.exe` GUI 版 + `Godot_console.exe` 控制台版，agent 用后者；`GodotSharp/` 为 C# 依赖勿动）。PATH 已加入用户变量。
3. **VS2026**：确认勾选了「.NET 桌面开发」工作负载；它只作为代码编辑器/调试器使用，**F5 运行游戏走 Godot 编辑器**。Godot 编辑器首次创建 C# 项目会生成 csproj/sln；VS 打开 sln 后按 F5 会"启动 Godot 并调试"（需 NuGet 装 `GodotSharp` 相关引用，由 Godot 自动写入）。
4. 验证链：`Godot 编辑器打开项目 → 输出面板无 .NET 错误 → dotnet test 可跑`。

## 8. 风险清单

| ID | 风险 | 等级 | 缓解 |
|----|------|------|------|
| R1 | FOV/aspect 在 Godot 与 Three.js 语义差异导致构图不一致 | 中 | M2 数值对照标定；文档公式自带 fovH 换算，`aspect` 传入方向单点验证 |
| R2 | 世界 Z 轴朝向/镜像差异（lookAt 手性） | 中 | 翻转收敛在 `CoordMapper` 一处；贴边五连截图对照 |
| R3 | ghost 逐实例透明排序不严格 | 低 | 已定降级方案（按层分桶）；目视验证驱动，不预防性优化 |
| R4 | 辅助线/悬停联动文档缺口，行为理解偏差 | **高** | M3 独立子任务；先读源 TS（3 文件）提炼行为清单并经你确认再写 |
| R5 | AI 线程与 Godot 主线程交互（取消/结果回投） | 低 | Core 纯函数 + Game 层单点接线；测试覆盖取消路径 |
| R6 | 旧档兼容细节（字段缺失、v1 形状、非法 Base64） | 中 | ✅ 已闭环（2026-09-19）：源浏览器版**真实导出样本**入 `tests/…/Fixtures/browser_real_export_v2_standard.{json,b64.txt}`（8 手 13×6 进行中局，UI 落子→Esc 保存→localStorage 抓取），双向互通实测（本项目 Base64 导入源浏览器成功）；行为级回归见 `SaveFixtureTests.BrowserRealExport_*` |
| R7 | woff2 字体 Godot 不识别 | 低 | 取霞鹜文楷官方 TTF；OFL 许可随仓库附声明 |
| R8 | 调色板抽象过度设计拖慢 M4 | 中 | 首发仅一套 light+dark+色弱三组色，Palette 字段先语义化、不做编辑器 |
| R9 | Godot 4.6 相对 4.5 的 API 变动踩坑 | 低 | 均为标准稳定 API（MultiMesh/Camera3D/Control._draw），遇阻再查 release notes |

## 9. 最终验收（对齐 04-contracts §6 全表 + L3 手动清单）

见各里程碑验收栏；M5 后追加：七条用户流程（README 操作指南表全键位）+ 四语言抽查 + 双端存档互转演示。

## 10. 行为结论登记（文档缺口 → 读源 TS 核实，R4 同款流程）| # | 议题 | 结论 | 依据 |
|---|---|---|---|
| B1 | Swap2 `getNextTurnState` 的 `currentPlayer` 是谁 | **刚行棋方**。swap 分支返回 `nextPlayer=刚行棋方(黑)`，shell 在决策期不改写 currentPlayer → 黑连下第 1、2 手（换色决策弹窗插在两子之间），第 3 手起白方行棋；pass 与 swap 的唯一区别是棋子归属换不换 | `main.ts:341-366`、`SwapEngine.ts:18-34` |
| B2 | 预置子跳过 swap 的守卫 | 源 TS 引擎**没有** levelConfig 参数，属隐式行为（有预置子时点击后 history 恒 ≥2，永远撞不到 `length=1` 分支）。C# 按 04-contracts §2.1.3 的显式签名实现，守卫结果与源等价 | `SwapEngine.ts` vs 02-rules §2.4 |
| B3 | 旧单体档拆分（`gomoku_saves_v3`） | 条目为 null **或缺 `id`** 都被跳过，但数组下标照常递增（下标即槽位号） | `LocalStorageAdapter.tryMigrate` |
| B4 | AI `attack + 1.1*defense` 的次序副作用 | 严格按文档表实现：当 AI 的成五点与对方的成五点**不在同一格**时，AI 会优先堵而不自己胜（defense 1.1e6 > attack 1e6）。源项目同式，保持忠实，M3 接线后如体感过弱再议 | 02-rules §4.4 |
| B5 | PatternCollector 的 maxLen=4 | 4 格是**展示端**限制（README 性能约束），不是数据端裁剪。Core `Collect(board, layerZ)` 输出完整模式；渲染器用 `GameConfig.HelperLineMaxCells` 截断，保证"一份数据、两个渲染器" | README 特性节 + 02-rules §5.2 |
| B6 | AI 等分候选的平局策略 | C# 在最高分等分池中按种子随机取一（可复现）；TS 取首个最高分（先到先得，`AIEngine.ts:104-107`）。两实现的候选枚举顺序本就不同，逐位对齐无意义，登记差异备查 | `AIEngine.ts` vs `AIEngine.GetScoredMove` |
| B7 | 相机：文档 §9 管道 vs 源 `LeftPanel.ts` 实测差异 | **以文档为准**。源实测：① 对角线只算 w²+h²（缺第二个水平轴项，默认盘 targetDiag 21.22 vs 文档 24.38，距离差 ~15%）；② 距离只用垂直 FOV（无 min(fovV,fovH)，横屏 aspect≥1 时两者等价）；③ `rotateY`/`zoom` 的 lookAt 中心 Y/Z 互换（源 bug，文档 §2.4 为正确中心）；④ 缩放兜底路径直接调 fov（15–60 clamp）且锚点路径 minDist 硬编码 5.0、无上限；⑤ 棋子为 focusZ 离散透明双材质，文档 §8.2 连续 opacity 是重设计目标。M2 标定对照时按此解释构图差异 | `LeftPanel.ts:305-437` |
| B8 | 世界朝向：文档 §2.2（层=世界Y 上）vs 源几何（`LeftPanel.ts:57-101` 层坐标在第三元） | **行=世界Y 竖直、层=世界Z 纵深**（源实际；人类确认 2026-09-19：源中"竖向格线"即行线，采用源朝向做标定对照）。相机中心用正确包围盒中心 `(w/2, w/2, h/2)`，不沿用源的 Y/Z 互换（B7③）；§8.2 透明度沿层轴（worldZ）衰减；CoordMapper 为唯一收敛点 | `LeftPanel.ts:57-101`、人类反馈 2026-09-19 |
| B9 | 辅助线语义简化 + 焦点显隐模型（**弃源逻辑**，人类指示 2026-09-19） | ① 每方向只到**首个非空子**即停：一条线（悬停→该子，可越空格）+ 一个标记（角色=子色）；源的跳空续走/己方连走/截断标记会使跨层斜线视觉穿透中间层（"己方只剩最远标记/敌方己敌混杂"），不再保留。② 3D 显隐：网格逐层 焦点1.0/相邻0.35/远层0；棋子六桶=焦点实体/被辅助线标记强化/其余大幅减弱，**黑白分色**（Palette.Ghost*，暗背景黑子需更高透明度，浅色主题反转） | 人类反馈 2026-09-19 |
| B10 | 初始朝向：层 0 应为最靠近摄像机的第一面，双面板左右/上下需一致（人类反馈 2026-09-19） | **整盘绕 Y 轴旋转 180°**（CoordMapper 单点）：worldX=size-1-x、worldZ=(layers-1-z)×spacing——层 0 最近、A/D 由近及远符合直觉、双面板左右一致；2D 面板再纵向镜像（行 0 在底部，对齐 3D 行=Y 向上） | 人类反馈 2026-09-19 |
| B13 | 行棋方指示"假棋子"（人类反馈 2026-09-19；源 RightPanel.ts:577-596 的 ghost 为悬停预览子，随 currentPlayer 着色、HOVER_PIECE_OPACITY=0.35，无常驻指示） | 2D 面板右上角**常驻**半透明指示子（透明度 0.55，略高于悬停语义保可读）：显示当前行棋方；纯 _Draw 图元——**不入棋盘数据/子数/辅助线/3D MultiMesh**；SWAP2 决策期指针不动（B1），指示子显示**决策方白方**；复盘/终局不显示 | 人类反馈 2026-09-19 + RightPanel.ts 源读 |
| B14 | winLength 存档缺口（v2.0.0 schema §3.3 无连珠数字段，自定义 4–9 连珠随档丢失） | **外圈元数据双通道**，内层 v2.0.0 不增删字段（§4 铁则）：① `.nzsave` manifest.json 增可选 `winLength`（camelCase，null 时省键，越界视为缺失不毙档）；② 槽位 index.json 的 SlotMeta 增可选 `winLength`（本项目自建存储元数据，04-contracts §3.1.2 字段为超集兼容，旧 index 无此键 → null 回默认 5）。两通道都只做**随档携带**，读档方 `LoadFrom(data, winLength)` 恢复引擎/AI/关卡配置 | migration-plan §4 外圈许可 + 04-contracts §3.3/§3.1.2 对照（2026-09-19） |
| B16 | 高分屏 3D 子视口原生像素（2026-09-25 v4 定案） | 引擎行为链（四版迭代）：v1 `SubViewportContainer(stretch=false)` 手动喂尺寸——C++ `get_minimum_size()` 在 stretch=false 时**直接返回视口尺寸**且容器布局永不压子控件到最小尺寸以下 → 容器被撑大 → 下一帧 desired 再乘缩放 → **每帧 ×缩放正反馈回环**（真实窗口实测 int32 饱和、texture_create 失败刷屏=黑屏事故）；v2 `Scaling3DScale` 超采样——3D 缓冲降采样回**逻辑尺寸纹理**再被画布放大，高内容缩放下必然发糊；v3 C# `_GetMinimumSize` 清零——**无效**（C++ 覆写不经脚本虚方法，v3 实测回环重现）；**v4 定稿：弃用 SubViewportContainer**，`GameScreen.ViewportHost`（Control）自绘视口纹理+每帧重绘，最小尺寸恒零结构性解耦，视口 Size=宿主逻辑尺寸×内容缩放=物理像素，纹理 1:1（逐帧数值验证恒定）。UI 缩放：窗口固定 1920×1080 + `ContentScaleFactor=2.5` 绝对值（等效基准 768×432；DPI×2.5 与 85% 工作区方案实测观感差被否） | 真实窗口逐帧追踪+截屏（.trace/，2026-09-25）+ GodotSharp API 文档 |
| B17 | 亮色主题配色（源 light 体系对照 + 本项目缺口；**迁移方向待人类确认**） | 源 light 三处来源：① `Types.ts` Theme——画布底 `#E8F4F5`、网格 `#A0C0C4`、焦点网格/强调 `#2C4A4E`、ghost `#AAAAAA`、辅助线 `#D0021B`；② `RightPanel.ts` 石子渐变——黑 `#444→#222→#000` 描边 `#000`、白 `#F0E8E0→#D0C8C0→#A09A95` 描边 `#555`；③ CSS——body `#F2F7F2`、标题屏渐变 `#F4F9F4→#E8F0E6`、主按钮 `#2C4A4E` 配字 `#F4F9F4`、hover `#C8E0E4`、危险 `#8B3A3A`。**本项目缺口**：2D 面板与游戏基座无自有背景（Board2DView 只画网格/棋子），透出根窗口默认深灰清屏色——暗色下不可见，亮色下右侧发暗（人类反馈"亮色的 2D 其他背景还是暗色"根因）；且现行 Light 套是自创"水绿宣纸"方向，底色偏米色，偏离源浅水绿系。迁移要点：补 2D 背景层（取 Palette.Background 或随主题 SetDefaultClearColor）→ Light 套按上表逐字段重校（Background→#E8F4F5、GridLine→#A0C0C4、ButtonBg/Accent→#2C4A4E 系、2D 石子渐变端色→源值）→ T 明暗全屏走查 | `Types.ts:54-70`、`RightPanel.ts:38-53`、`base.css:35-39`、`layout.css:33-41`、`components.css:16-88`（2026-09-25） |

## 10.5 R4 行为清单（2026-09-19 通读源 TS：RightPanel.ts / LeftPanel.ts / main.ts 输入段；**待人类确认后才写 M3 代码**）

### 2D 右面板（正交相机，一次只看焦点层）
| # | 行为 | 源实现 |
|---|---|---|
| R4-1 | 相机 | 正交；视口宽 = (size-1) + 2×0.5 padding，中心 (size-1)/2，宽高比适配（aspect>1 定高、否则定宽）；层切换=相机沿深度移动到 focusZ×spacing（`RightPanel.ts:714-754/664-672`） |
| R4-2 | **镜像 F** | **默认 `isMirrored=true`**（水平翻转，靠相机 left/right 互换实现）；toggleMirror 切换 |
| R4-3 | 星位 | 5 点 = 中心 (size-1)/2 + 四**边星** offset=max(1, floor(size/5))（13 路即 (2,2) 等，**非正四角**）；红圆 r=0.12；色弱换蓝。与 3D 立柱（正四角+中心）是两套锚点 |
| R4-4 | 悬停 | NDC 反投影 → round 取格；**界内且焦点层该格为空**才有效；显示当前玩家 ghost 子（透明度 0.35）；无效→隐藏+清覆盖；落子后合成"面板中心"悬停一次 |
| R4-5 | 辅助线扫描（4 格上限） | 逐方向：先**跳过连续空格**找首个非空；随后——己方连子→画线续走；敌方→画线+标记点后停；空格→在 prev 放己方色标记点后停；因 4 格截断→在最后非空处放己方色标记。敌方线画层上 0.03、己方 0.02（敌方压上层） |
| R4-6 | 辅助线颜色 | 己方绿 `0x00FF00` / 敌方红 `0xFF0000`；**色弱 V**：己方蓝 `0x0088FF` / 敌方黄 `0xFFCC00`；标记圆 r=0.10、透明度 0.8 |
| R4-7 | 辅助线开关 H | `auxMode` 位标志（bit1=2D 战术线、bit0=3D 跨层线），但 cycle 只有 **ALL↔NONE** 两态，双面板同步 |
| R4-8 | 最后手标记 | 红环 0.45–0.55，仅当 lastMove 在焦点层；色弱换黄 `0xFFCC00` |
| R4-9 | 焦点层渲染 | 2D 只画焦点层棋子（实体），其余层不画；换层=事件 LAYER_CHANGED → 相机移动+重画+清覆盖 |
| R4-10 | 落子门控 | `isGameActive && isMyTurn` 双回调；占用+规则引擎合法性检查；胜利走 onGameWon（规则引擎优先，无引擎退回 checkWinner） |

### 3D 左面板联动
| # | 行为 | 源实现 |
|---|---|---|
| R4-11 | 3D 辅助线 | `on3DAuxDataChanged` → 线（顶点色，opacity 0.9，polygonOffset -1）+ 标记球（r=0.12，opacity 0.8），逻辑坐标×spacing 转世界（`LeftPanel.ts:460-509`） |
| R4-12 | 3D 蓝点 | `onHoverChanged` → **固定蓝球 r=0.2（0x0088FF）**在悬停格（+0.1 抬升）；无效清除——即"悬停定位蓝点" |
| R4-13 | 3D 棋子显隐 | 焦点层 focus 材质（1.0）、其余层 ghost **0.25**（离散焦点模型，B7⑤；与 M2 的 §8.2 连续透明度需合并，见 D2） |
| R4-14 | 3D 网格 | 焦点层 focusGridColor 实线、其余层 ghostGridColor ×0.4（两组材质）；Z/C/X 改间距时网格/立柱/辅助轨全量重建 |

### 键位与状态机（main.ts:1213-1366）
| # | 行为 | 源实现 |
|---|---|---|
| R4-15 | A/D 换层 | **循环取模** `(focusZ±1+N)%N`（非钳制） |
| R4-16 | Z/C/X | 步进 ∓0.5 / X 复位（M2 已改连续按住+X 总复位，经人类验收，**保持 M2 方案**，登记差异） |
| R4-17 | 滚轮 | 源=rotateY 旋转；M2 已改缩放（决策 #9 验收过），**保持 M2** |
| R4-18 | 其余 | Q/E ±90°、F 镜像、H/V 同上、R/Esc 暂停、M 静音/T 主题全局、回放 ←/→ + Esc/R 且相机键穿透 |

### 待人类拍板的设计决策（M3 写码前）
| # | 议题 | 建议 |
|---|---|---|
| D1 | 2D 星位公式 | 按源 max(1, floor(size/5)) 边星+中心（与 3D 立柱的两套锚点并存）；偶数阶方案维持挂账 |
| D2 | 3D 焦点层显隐模型 | 焦点层强制实体 1.0，非焦点层用 §8.2 连续透明度（比源全 0.25 更有层次；是否忠实源 0.25 请定夺） |
| D3 | 蓝点配色 | 源固定 0x0088FF 不随色弱变；建议接入 Palette.HoverHighlight（同为蓝），色弱时可选换色 |
| D4 | 辅助线镜像 | F 镜像默认开（isMirrored=true）并按源实现于相机层；镜像对辅助线 2D 扫描无影响（只翻转呈现） |

## 10.6 契约核对：04-contracts §6.3/6.4/6.5（M4 收尾轮，2026-09-19）

**§6.3 IStorageAdapter（FileSystemStorageAdapter + SaveManager）**
- ✅ Initialize 先于其他方法：SaveManager.InitAsync 首步 await storage.InitAsync，唯一消费方 GameStore.InitializeSaves 先 await 再开放读档 UI（结构保证）。
- ✅ SetSave+GetSave 往返一致：`SaveThenLoad_RoundTripsThroughStorage`。
- ✅ DeleteSave 后 GetSave 为 null：`Delete_ClearsSlotAndStorage`。
- ✅ GetIndex 槽位数：**登记偏差**——适配器只返回非空槽 meta（`IReadOnlyList<SlotMeta>` 非 nullable 装不下空位），
  固定 6 槽投影在 SaveManager 层完成（`GetSlots()`/`IsSlotEmpty`），无消费方依赖"长度恰为 6 含 null"的字面形状（B3 同源行为）。
- ✅ 多槽写入互不破坏：适配器方法内部同步、无 await 交错点；`Save_MultipleSlots_RoundTripIndependently`。

**§6.4 IOverlayManager（Godot 实现）**
- ✅ 确认回调只在确认触发、取消不触发 onYes：`m4c-overlay-contract`（合成 confirmed/canceled 信号断言回调计数）。
- ✅ ShowInputDialog 回调收到 trim 后输入：同上自测（"  ab  " → "ab"，§4.1.2）。
- ✅ ShowToast 自动消退不挡输入：2.5s 定时自清 + MouseFilter Ignore（代码审计 + 人类走查多轮）。
- ✅ CloseAll 丢弃挂起回调（不调用不转移）：同上自测（CloseAll 后补发 confirmed 无回调）。

**§6.5 KeyboardShortcutMap（KeymapTable + ShortcutRouter）**
- ✅ 同 context 无冲突绑定：Core `KeymapTable.FindConflicts` + 19 例测试（双作用域主键冲突检测）。
- ✅ 未匹配按键直通：分发为"首中即吞"，未匹配返回 false 落回持续键轮询/引擎默认；`m4c-modifier-quicksave`
  证明裸 S 不触发 Ctrl+S 快存（直通给 zoom_out）。
- ✅ 修饰键组合正确检测：InputMap `EventIsAction(exactMatch:true)`；`m4c-modifier-quicksave`
  合成 Ctrl+S（物理键 S+ctrl）真实触发快存、裸 S 不触发——修饰键既不漏检也不误吞。

## 11. M1 交付清单（2026-09-18）

`src/Nmoku.Core`：Types / GameConfig / Board / Rules / PatternCollector / RuleEngines(IRuleEngine,StandardEngine,SwapEngine) / Saves(SaveData,SaveValidator,SaveMigrator,IStorageAdapter,SaveManager) / EventBus / TextConstants(Text) / AI(AIEngine)。测试 113 例全绿（test-and-cicd §4.1–4.5 + contracts §6.1/6.2/6.3 全覆盖，AI vs AI 100 手为固定种子可复现用例）。
