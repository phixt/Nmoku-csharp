# CHANGELOG

> 维护规则见 `AGENTS.md`：只记"当前场景"相关事实；总量 ≤300 行，旧条目按里程碑压缩合并，但关键教训（踩坑结论）不删。
> 提交级细节看 `git log`，这里只留人读的里程碑叙事。

## M5 修复批 4 + 开始界面重做（2026-11-26）

- **触发**：人类评估开始界面动效不自然/字母不像字母，决定保留核心创意并按新审美重做；同时按推荐口径清掉 10-26 审查报告的剩余遗留 bug（UI 缩放体系重构暂缓）。共 5 个功能提交 + 1 个交接提交。
- **① Core 防御与相机元数据同步**：`Rules.Directions` 改 `ImmutableArray` 防篡改；`Board`/`Rules`/规则引擎补非正尺寸/非正连珠/非法 `CellState` 校验并新增 `Board.Clone()`；`CoordMapper.WorldToLogical` 改四舍五入+防 `spacing=0` 除零；`CameraMath.ComputeDistance` 防非正/NaN 宽高比；`CameraRig.Advance` 同步非插值元数据（修 `ComputeOpacity` 陈旧读）；`AuxScan.Scan` 验悬停界内、`KeycapParser.Parse(null)` 不抛 NRE（+16 例单测）。
- **② AI 行为修复与线程隔离（§10 B28–B30，人类拍板）**：①己方即胜绝对优先于防守（修 B4 战术送死）；②`PatternScore` 按 `WinLength` 4–9 泛化（wl=5 逐值不变）；③Easy 恢复邻近已有子落点档；④`GameManager` 向 AI 线程池传 `Board.Clone()` 独立快照并释放 CTS（+3 例单测）。
- **③ 视图与壳层卫生**：`Board2DView` 改消费 `Main` 推送的同一份 `AuxScanResult` 快照；`Board3DView` 辅助线改单份 `ImmediateMesh` + 标记球节点池并按坐标去重；`Main`/`GameStore`/`AudioManager` 补 `_ExitTree` 退订与停播；`SaveMenu`/`KeymapContent` 补 `RemoveChild` 防同帧残留；`SettingsStore` 增内存缓存+`SetMany` 合并落盘+退出释放；`SceneRouter.Rebuild` 保护 `game` 并支持 `IKeepAliveRefreshable` 原地刷新；`I18nCsvTests` 新增四语言 `{n}` 占位符一致性守卫（+1 例单测）。
- **④ 开始界面重做（`PreTitleScreen.cs`，真实窗口 3 轮截帧调优）**：
  - 封面态：借鉴 `caustic-gem` / `motion-cube` 风格化宝石语言（`PreTitleShaders.cs`：5 阶高保色色板 + 窄带 Bayer 4×4 抖动 + 内部折射虹彩 + 等宽晶体棱边高光 + 硬边分层地面焦散盘），三段斜接 3D 晶体字母 `N` 配黑白星光蓝/月光石双子差速交叉 3D 公转、克制等轴视差几何背景与原生清晰 2D 排版。
  - 流体形变与自中心构图（可双向可逆）：双子自瞬时轨道位置沿弧线落入棋盘格位，晶体 N 三笔画沿纵深分离薄化并融入波浪展开的 5×5×3 棋盘；相机始终以原点 `(0,0,0)` 为自身旋转轴，经 `Camera3D.HOffset` 实现「略偏左」零甩动构图；菜单列 232px/右留白 56px 与 28px 左轴对齐，均衡不挤不空。
  - 菜单态与 7 处历史遗留根治：物理像素 1.5× 超采样 + MSAA 8× 根治 csf 2.5 发糊；左上角标就位；「继续对局」独立管理不被覆盖；`IKeepAliveRefreshable` 原地换肤/换语不重置演示盘；柔和自转生效+拖拽惯性；隐藏时停进程并置视口 `Disabled`；全量 `I18n.T` 零硬编码中文；按钮全走 `Palette.Active` 并配 hover 轻移与强调条。
- **⑤ 架构拆解与自测补盲**：将 2000 行自测拆入 `Autotest/Main.Autotest.cs`，取消 `Main._Process` 自测早退（四条生产路径正式进入自测覆盖），新增 `m5-pretitle-redesign` 回归锁，补提交 `KeymapAudit.cs.uid`。
- **门禁**：build 0 警告 0 错误 / **382 单测全绿**（362→382，+20 例）/ 无头 `--quit` + `--autotest` ALL PASS（含新回归锁，内存平稳 ~133MB）/ 真实窗口暗亮两套 × 封面·形变·菜单三阶段截帧复验通过。未 push（铁律 9）。

## M5 修复批 3 — 真实缺陷（2026-10-27）

- **触发**：接 2026-10-26 全面代码审查的批次路线图 ③（真实缺陷），7 个条目逐条根治 + 3 项顺带
  （i18n 测试布局耦合、键位展示↔绑定审计、BGM 回归锁去时刻采样化）。**一个 commit 一件事**，
  共 10 个代码提交。
- **① PvE 执子方门控**：落子入口此前只判「界内+空格」，**人类可在 AI 思考窗内替 AI 落子**——
  AI 已算好的应手被 `ApplyAiMove` 的行棋方守卫静默丢弃且无重试，对局就此卡死（双方皆人类在走）。
  现**对齐源**（`main.ts:120/316` 的 `!isPvEMode || currentPlayer === playerColor`），判定抽成
  Core 纯函数 `TurnOwnership`（+4 例 xUnit）+ 自测步 `pve-turn-gate`。
  已知差异（未实装）：源触发 AI 前有 300ms「思考」延迟，C# 侧立即应答。
- **② 相机公式距离重算**：`CameraRig` 把用户缩放存成**绝对距离**且每次 Update 无条件写回 →
  §9 STEP 5 的公式距离成为死代码（视口 16:9→0.5 时公式值应为 57.13，实为 31.69，盘不再贴合画面）。
  改存**倍率**（相对公式距离，§10 B25）：公式距离随视口/层间距真实重算，缩放意图等比保留。
  配套修正 `CameraRigTests` 靠 1 ulp（3.55e-15）浮点毛刺**假绿**的断言（改为精确等于独立复算的公式值）。
- **③ 窗口缩放跟随当前关卡**：`HandleViewportChange` 用命令行默认尺寸 → 非默认关卡后任何缩放都跳回
  13×13×6 构图；改用当前对局盘尺寸（自测 `m5-viewport-level-fit`，含「误用默认尺寸会得到另一数值」的反证）。
- **④ 最小化不写回窗口模式偏好**：最小化被兜底分支写成 `windowed`，把「上次是全屏/最大化」的偏好永久
  抹掉（与代码原注释「最小化不误写」直接矛盾）；最小化现在返回 null = 不写回（自测 `m5-window-mode-pref`）。
- **⑤ 浮层作用域数据驱动**：此前硬编码三个 action 名，`KeymapTable.AllowsWhenOverlayOpen` 守卫在生产
  代码零引用；现作用域由 KeymapTable 决定、本层只提供处理器，并加漂移检测（自测 `m5-overlay-scope`）。
- **⑥ 键位护栏自维护化**：`KeymapTableTests` 的 action 清单是硬编码 32 项且早已漂移（漏 `quick_load`/
  `mirror_2d`）→ 改由 `project.godot` 的 `[input]` 节生成（csproj 显式声明 content，不走「向上找
  `Nmoku.slnx`」的布局耦合），并补反向护栏（表里登记的 action 必须真实存在）。
- **⑦ 标题屏布局锁守卫对齐**：`m5-title-fit` 此前测「内容最小高 vs 416」而布局是硬编码 432 高 →
  真正会出界的类别（菜单列矩形越出逻辑面）根本没测到；守卫改测**实际布局矩形**（菜单列必须落在最小
  逻辑面 320×416 内 + 设计面 768×432 几何不变），菜单列同时改为随逻辑面自适应（设计尺寸以上逐像素不变）。
- **顺带**：①i18n 翻译表测试解除仓库布局耦合（审查 P1 #33）②新增**键位「展示 ↔ 绑定」一致性审计**
  （此前只靠人工核对；把 load_latest 展示键改 K 后如期报错）③BGM 交叉淡化回归锁**去「时刻采样」化**
  （实测间歇红 1/8：固定延时+瞬时采样会被进程卡顿整个跳过淡变窗口 → 改逐帧条件等待；把淡入改成硬切
  后守卫如期红在 p3）。
- **门禁**：build 0 警告 0 错误 / **362 测试全绿**（355 → 362，+7 例）/ 无头自测连续多轮 ALL PASS。
- **行为结论**：§10 新增 B25（缩放跨重算语义）、B26（PvE 执子方门控，对齐源）、B27（标题屏菜单列自适应）。
- **未 push**（铁律 9：push 节制；`ci.yml` 只触发 master → 见 AGENTS「分支与 CI 现状」）。

## M5 修复批 2 — 数据完整性（2026-10-27）

- **触发**：接 2026-10-26 全面代码审查的批次路线图 ②（数据完整性），7 个条目逐条根治 + 顺带补齐
  审查报告 P1 表里的写盘可见性/逐槽容错/winLength 通道对称等近邻项。**一个 commit 一件事**，
  共 6 个代码提交（存储层 → 门面与装档路径 → 校验层 → 迁移层 → 容器 → 无头自测）。
- **① 槽位主键改槽位号（P0-1·数据丢失）**：旧契约以「档 Id」寻址 → 复制/导入同一份档到另一槽会让
  **原槽从存档列表消失**（磁盘 blob 仍在、index.json 已无该槽；删任一槽还连带删掉共享 blob）——
  M4 已发布的「复制→填写」即可触发。现索引改 `Dictionary<int, SlotMeta>`、每槽独立 blob
  （`slot{n}.sav`），**导入保真 id 语义不变**（保的是档内容里的 id，存储层不再要求 id 全局唯一），
  同一 Id 出现在两槽互不干扰。**旧盘布局（0.1.0-alpha 的 `{档 Id}.sav`）首次启动自动搬家并删旧件，
  旧存档不丢**（回归锁 `m5-save-legacy-migration`）。
- **② 写盘失败可见**：存储契约改返回 bool；`SaveManager` 先落盘后更新内存（此前先写内存再 await 磁盘
  → 「内存显示已存、磁盘无档」的**假成功**），UI 按结果弹「保存失败 / 删除失败」（新增四语言译文）。
- **③ 逐槽容错**：恢复源 `loadFromStorage` 的逐槽 try/catch 语义——此前 catch-all `Array.Clear`
  使**一个坏档清空全部槽位**（实测好槽被连带清空）；现在只有「索引整体读不出来」才清空。
- **④ 外圈 winLength 范围校验**：index 通道此前零校验（`winLength=0` 即「任意一子判胜」），
  补 4–9 校验，与 manifest 通道同口径（越界视为缺失 → 回默认 5）。
- **⑤ 读档半加载根治**：`rules="renju"`（引擎未实装）此前能通过导入、读档才炸，且**炸在棋盘已被替换
  之后** → 半加载现场 + 异常冒泡到 Godot 主循环。现三层收口：校验层规则白名单（导入即拒）、
  `GameManager.LoadFrom` 改为「规则白名单 → 连珠数范围 → **引擎先建** → 维度核对 → 才动现场」、
  `GameStore.LoadSlot/EnterReplay` 先判状态机转移合法性再装档（此前转移非法时 GM 已被覆盖）。
- **⑥ 垃圾 JSON 不再迁成空档**：v1 迁移分支此前对「缺 moves 数组」的任意 JSON 也照样迁移，产出合法空存档，
  且缺失 timestamp 按源语义补当前时刻 → **该垃圾档成为「读最新」**（用户的真存档反被排后）。
  现要求 moves 为数组（源存档恒有，只是可能为空）；`{"moves":[]}` 仍视为合法空对局。
- **⑦ zip bomb 闸门**：`.nzsave` 解包此前 `ReadToEnd` 无上限 → 几十字节的压缩包可换来 GB 级分配。
  现容器 ≤8MB、单条目解压 ≤4MB（中央目录声明长度 + 实际读取量双闸）、文本输入 ≤8MB；
  条目名查找改大小写不敏感。
- **顺带（审查报告 P1 近邻项）**：落子坐标/执子方/盘面格值**值域校验**（越界手此前在复盘重放时
  「静默消失」、非法格值经 `(CellState)` 强转成永久封锁的僵尸格使 `IsFull()` 永假）；
  `GetLatestIndex` 不再漏选 timestamp=0 的唯一档。
- **门禁**：build 0 警告 0 错误 / **355 测试全绿**（321 → 355，+34 例回归锁）/ 无头自测 ALL PASS
  （新增 `m5-save-integrity`、`m5-save-legacy-migration` 两步）。未 push（铁律 9：push 节制；
  当前分支 `prestart-native` 不触发 CI，见 AGENTS 进度节）。
- **行为结论**：§10 新增 B22（槽位主键取舍）、B23（写盘失败/坏档/脏档的五条闸门）、
  B24（容器解压上限与条目名）——**与源项目收紧的三处差异均已登记**（铁律 2）。

## M5 修复批 1 — 全面代码审查后的止血修复（2026-10-26）

- **触发**：全仓（Core/Game/Tests，约 15k 行）一次全面只读审查（8 路专项深审 + 独立取证）。
  结论：**质量高于同规模平均**（Core 零 Godot 依赖达成、状态机 7×11 全组合穷举、13 方向参数化覆盖、
  §4 常量表与代码 14/14 对齐、键位三方零漂移），无外部输入直穿的数据损坏路径；风险集中在
  **「看起来绿、实际不绿」的回归锁失效、门禁不可复现、交接窗口失真**三类。
- **① BGM 淡出 ε 停滞挂轨**（`SampleMusicEngine._Process`）：淡出步进落入 0~1e-5 残差时，下一帧
  `IsEqualApprox(Lin,Target)` 判等早退跳过步进，而停播判据要求 `Lin<=0` 严格成立 → 轨位永挂 -80dB
  静默循环。**该缺陷使无头自测间歇性变红**：修复前 8 轮失败 2 次且失败步骤每次不同，CI 门禁随时可能
  随机变红。改为产出残差的同一帧钳到精确 0；修复后 4 轮全绿。
- **② 快捷键门控首行命中即返回**（`KeymapTable.Allows`）：`confirm` 同时登记标题行与引导行，首行命中
  即 return 使引导行永不可达 → **引导屏 Space/Enter 走不通 shortcut 系统**，而 F1 帮助屏仍在广告该
  键位（同文件 `AllowsWhenOverlayOpen` 已用 any 语义，只改了一处）。改为任一登记行允许即允许，未登记
  action 仍放行；补 7 例回归锁。
- **③ 统一读档入口对畸形 JSON 抛异常**：`SaveMigrator.Normalize` 只 catch 了 `JsonDocument.Parse`
  阶段，`Deserialize`/`GetInt64` 的异常逃逸 → 违反 `SaveArchive` 自身契约「无法识别/校验失败一律
  null，不抛异常」，且 `GameStore` 三处调用无 try/catch → **用户粘贴损坏档即异常冒泡到 Godot 主循环**。
  改为全异常兜底 + 尺寸闸门前置到版本分支之前（盘面按声明值分配，越界先判废，防百字节输入换来 GB 级
  分配）；补 17 例回归锁。
- **④ 命令行 `--board/--layers` 未校验**：`Board` 构造不校验 → 0 尺寸盘 `IsFull()` 恒真（首手即判和）、
  负尺寸盘 `InBounds` 恒假（僵尸盘）。改为钳到 `GameConfig` 硬上限。
- **⑤ 构建与发布卫生**：删随 `TitleScreen` 删除遗留的孤儿 `.uid`；修 `package.ps1` 连续两个 UTF-8 BOM；
  修 README 下载文件名（`win-x64.zip` → `win64.zip`，与打包产物一致）。
- **门禁**：build 0 警告 0 错误 / **321 测试全绿**（304 → 321，新增 24 例回归锁）/ 无头自测 ALL PASS。
  未 push（铁律 9：push 节制；当前分支 `prestart-native` 不触发 CI，见 AGENTS 进度节）。

## M5-pre — 预开始页原生实现（2026-10-01 → 2026-10-05，分支 `prestart-native`）

- **为什么**：HTML 原型三轮反馈后动效在浏览器里调不出满意效果，且 HTML→Godot 迁移成本高；人类指示
  转向原生 Godot 融入设计（`docs/prestart-native-design.md`）。
- **落地方式**：**复用游戏内已验收机制**——演示棋盘直接用 `Board3DView`/`CameraRig`/`AuxScan`
  （M2/M3 人类验收版即"真实机制"），不移植自创版；Core 零改动。
- **R4–R10 迭代**：N 字标从"红棱柱"改**与棋盘同材质的三维线格字**（字即棋盘的种子）；转换动效改
  **真形变**（字干线格直接展开成三层棋盘，顶点/线宽/深度/相机偏航连续插值，零交叉淡化、零闪屏、整体
  可逆）；N+moku 排版改"N 首字下沉"；R6 演示棋盘升级为**可简单游玩的真实实例**（点击落子、悬停辅助线
  走 Core/AuxScan 真实机制）；R10 修真 bug——盘形态棋子把层号误当纵深（悬空错位根因）、文字仿射由投影
  实时导出（弃手调倾角）。
- **补提交**：Godot 4.6 的 C# 脚本 `.uid` 标识文件（4.4+ 引入，漏提交会在重新导入时丢脚本引用）。
- **遗留**（2026-10-26 审查发现，待批 ⑤）：左上角角标 `Position=(-320,18)` 永在屏外；「继续对局」可见性被
  `ShowMenuUi()` 覆盖；`Rebuild("title")`（切主题/语言）使标题页退回字形态并清零演示落子；自转分支被弹簧
  抢占实际不可见；隐藏后仍全速运行；两处硬编码中文未走 `I18n.T`。
  （标题列硬编码 432 高/需 420 宽一项已于**修复批 ③-7** 根治：菜单列随逻辑面自适应。）
  **人类尚未真实窗口走查 → 不建议在此之前打 `m5` tag。**

## 0.1.0-alpha（2026-09-26，M0–M5 收官）

- **当前能力**：13×13×6 立体连珠全流程——纯逻辑层（标准/SWAP2 规则、启发式 AI 三档、存档 v2 兼容）、
  3D 左视图+自由相机、2D 右面板+辅助线、状态机七态壳层、六槽存档+.nzsave 文件互通+复盘、明暗主题+
  色弱、关卡系统、F1 分层分页快捷键、音频（四场景 BGM+落子/胜负 SFX+M 静音）、设置页、四语言界面、
  游戏图标+自包含打包。
- **已知限制**：BGM 为占位音轨（正式版走 Strudel 谱面创作）；最大化过渡为淡入柔化；Connect6/禁手/
  开局库/键位重绑定为远期挂账。
- **质量基线**：build 0 警告、304 单测、无头自测 ALL PASS；打包产物 120MB 自包含 exe，真窗口启动冒烟通过。
  C# 源码 GPL-3.0-only。

## M5 — 音频/i18n/发布（2026-09-26）

- **音频（占位版）**：`AudioManager`（Sfx/Bgm 总线+音量持久化）+ `IMusicEngine` 双实现
  （`SampleMusicEngine` 采样循环 / `SynthMusicEngine` 留壳）；SFX 四件按源参数精确生成、BGM 四场景轨
  （menu/game/pause/guide，复盘不切轨、同轨不重播）；M 静音全局翻转+toast；WASAPI 恢复（M4R 临时
  Dummy 解除）。**正式 BGM=Strudel 谱面路线**（人类指示参照 programmed-music）：`assets-src/music/
  strudel/` 四轨占位谱+渲染管线 README，WAV 暂留空；占位循环由 `tools/AudioBake`（源合成参数离线
  烘焙）+`scripts/bake-audio.ps1` 生成。
- **行为结论 B20**（§10，M5 开工读源）：SFX 全源仅 click/win 两处触发（lose/hover 预制未接线）；
  BGM 四轨场景映射与同轨守卫；默认音量 sfx=0.6/bgm=0.3；**源切语言是热刷新**（injectUITexts 立即
  重注入）——本项目等价=EventBus.LanguageChanged → 重建已挂载壳屏（ApplyTheme 同款通道）。
- **设置页**：Overlay 浮层（标题/暂停入口）——音量滑条×2、静音（M 同步）、窗口模式三档+启动恢复
  （`user://settings.cfg` general 节）、语言切换。`SettingsStore` 统一 cfg 读写（读全文再改防节覆盖）。
- **i18n 四语言**：zh-CN 文本本身作键（gettext 风格，**Core 的 Text 常量零改动**）；翻译表
  `assets/i18n/translations.csv`（zh-Hant/en-US/ja-JP 手译）；Core 完备性测试+端到端自测。
  **关键坑**：①`zh_CN` 查询按语言族误配 `zh_Hant`（Godot 语言前缀匹配）——注册 zh-CN 原文自映射列后
  精确命中，端到端自测 `m5-i18n` 抓住；②批量替换正则误伤 `System.Text.Encoding`——加负向后顾修复后重跑
  （教训：**批量改码先小样本验证再全量**）。
- **图标/发布**：程序化图标 `tools/make-icon.py`（PIL 等距三层棋盘+五连：**paste 是替换语义、透明像素
  会擦底，须 alpha_composite**）；README 发布说明；package.ps1 复验（120MB 自包含、真窗口冒烟）。
  PowerShell 脚本含中文必须 UTF-8 **with BOM**（Windows PowerShell 5 按 ANSI 解析无 BOM 文件）。
- **过渡动效（最小方案）**：进对局/最大化跳变时 GameScreen Modulate 0.4→1/160ms 淡入，掩盖 3D 首帧
  黑闪与一帧重排；不碰布局/缩放管线（B16 回环教训）。
- **M/T 浮层内生效补齐**：KeymapTable 宣称 AllowWhenOverlay 但浮层分发只接了 F1/Esc（M4 遗留漂移），
  M5 补接线（**修复批 ③-5 已把该作用域改为数据驱动**）。门禁：build 0 警告 / 304 测试 / 无头自测 ALL PASS。

## M5 验收反馈批 2 — 设置页四 bug 根治（2026-10-01）

- **①音量滑条"滑不动/方向反"**：HSlider 用 `SizeFlags.Expand`（只分配空间不填充，M3 分屏视口同款坑）
  塌缩到抓手宽度——值→位置映射在 width≈抓手宽时退化。改 `ExpandFill` 铺满行（另两个 OptionButton 同改）。
- **②静音不联动**：M 键翻转无通知通道。`AudioManager` 增 `MuteChanged` 事件（ToggleMute 唯一出口），
  设置页订阅刷新按钮文本、`_ExitTree` 退订（语言热刷新会重建本屏，防悬挂回调）。
- **③语言"选项文字不改变/改不回"**：两叠加根因，热刷新机制本身无恙——(a) 下拉选中匹配用横线形（zh-CN）
  对比 `GetLocale()` 的下划线形（zh_CN）**永不等** → 恒空选；(b) **启动从不读 cfg lang**（设置页只写不读），
  重启丢偏好。修：匹配经 `StandardizeLocale`；`Main._Ready` 增 `RestoreLanguage`——**无偏好时默认 zh-CN**。
- **④模式下拉初值**：改读**实际窗口状态**，cfg 与实际由 SizeChanged 同步点收敛；启动恢复期以
  `_modeRestorePending` 拦截过渡态写回，防待恢复偏好被毁。
- **顺带修自测潜在 bug（被新回归步骤暴露）**：`AutotestAudioSettings` 首帧 `return "ok"` 即被自测框架
  出队，"第二帧关浮层"清理是**死代码**——设置浮层泄漏开放到此后全部步骤。改返回 "wait" 让清理入队。
  **教训：自测框架对非 wait 结果立即出队，多帧步骤首帧必须返回 wait。**
- 门禁：build 0 警告 / 304 测试 / 无头自测 ALL PASS（新增 `m5-settings-bugs` 回归锁：滑条
  ExpandFill+布局后实际宽度、M 键动作路径按钮文本联动、语言 en→zh 全链路 UI 路径含"改不回"、
  模式初值）/ computer use 真实窗口复验（滑条拖动方向跟手、静音联动、启动恢复日语→zh→en→zh
  往返、窗口化→最大化→重开初值=实际→切回窗口化）。

## M5 验收反馈批 1 — 标题屏溢出根治（2026-09-26）

- **根因（computer use 三模式实测取证）**：标题屏内容自然高 ~470 逻辑像素 > 窗口化逻辑面高 432
  （1080/2.5），CenterContainer 居中溢出=上下两端同时裁切 →「退出」不可见；M4 按钮少一个临界
  通过，M5 加「设置」按钮破临界。最大化/全屏 743~800 高从来够放。
- **修复**：标题屏压缩 fit（42/13 号字+间距 8+按钮 34，7 按钮最坏 406）+ 窗口 MinSize 1040 阻断
  拖矮溢出 + SizeChanged 同步窗口模式偏好（cfg 与实际状态闭环）+ CallDeferred 修 fullscreen
  恢复落成 Maximized 偏差；回归锁 m5-title-fit。**批11 ContentScaleSize=Zero 保留不回退**（防发糊
  机制正确；「显小」观感由已入待办的 UI 缩放体系重构解决——csf 跟随窗口）。
- 实测顺带取证设置页四 bug（滑条塌缩=SizeFlags.Expand 同款坑/静音联动/语言切换/模式下拉初值）
  已持久化 TODO，与 UI 缩放重构同碰设置页、建议合并实施。
- 门禁：build 0 警告 / 304 测试 / 无头自测 ALL PASS / computer use 三模式复验。

## 0.1.0-alpha（2026-09-26，M4 收官节选）

- **M4 能力**：状态机/UI/存档/路由/主题/关卡/F1 帮助（细节见 git log 与 §10）；批 1–11 验收反馈全落地。
- **关键教训（M4 精选）**：B16 v4 视口原生像素四版迭代（回环只在内容缩放>1 时发展）；
  `ContentScaleSize` 在 Godot 4.6 默认=启动窗口尺寸（B19）；headless 下 ConfirmationDialog 不自动确认
  （合成 confirmed 驱动）；**窗口/视口/渲染类改动必须真实窗口实测**；自动化无法复现时先向人类逐动作澄清。

## M4 验收反馈批 2–11（2026-09-25/26，已压缩；细节见 git log 与 §10）

- 批11 最大化 UI 异常根治（B19）；批10 复盘界面修复；批9 存档菜单定稿=档案管理+偶数阶销账；批6 快捷键
  六层分页；批5 UI 缩放 2.5 绝对值定稿+ViewportHost 自绘（B16 v4）；批4 F1 入口错位；批3 复盘 NRE 修复；
  批2 默认上下文（T 偏好模型）/存档 UX P1–P3/亮色主题 B17 对齐源。
- 教训：确认框语义用合成 confirmed 驱动；主场景 UI 路径漏测=复盘 NRE 根因（隔离 store 全绿≠主场景无恙）；
  导入原档保真使时间戳断言失效（改档 Id 断言）；headless 剪贴板不可靠（ExportSlot+FillSlot）。

## M4 相机异常定案与回退批恢复（2026-09-25）

- **事故根因（真实窗口逐帧复现）**：B16 的 `Stretch=false` 手动喂视口尺寸存在**每帧 ×内容缩放的正反馈
  回环**——SubViewportContainer 在 stretch=false 时以视口尺寸为最小尺寸 → 容器被撑大 → 下一帧再涨；
  实测 1681→13488→int32 饱和，8–14GB 分配 churn = "视角快速放大→黑屏"。相机全程无恙。
  **回环只在内容缩放 >1 时发展**——两批单独安全、合体必炸。
- **无头为何全绿**：autotest 的 `_Process` 提前 return，根本不跑 `HandleViewportChange`。
  教训坐实：**窗口/视口/渲染类改动必须真实窗口实测**。
- **修复（B16 v2）**：容器保持 `Stretch=true`，`SyncViewport3DResolution` 改设引擎内建 3D 缓冲超采样；
  双验 240 帧视口零增长、内存平稳、0 报错。**回退批恢复**：cherry-pick 回落（存档对齐/窗口自适应/自测加固）。

## M4 收尾轮 — 工程项全清（2026-09-19，已压缩；细节见 git log 与 migration-plan §10）

- **B14/R6/契约核对/真实旧档恢复**全落地：winLength 外圈元数据双通道（.nzsave manifest + 槽位 index）、
  源项目浏览器双向导入导出闭环、m4c-overlay-contract/modifier-quicksave/real-old-restore 回归。
- **教训**：Godot 启动会把进程 CWD 切到项目目录；ImplicitUsings 的 FileAccess/Environment 歧义写全称；
  浏览器自动化=合成 mousemove+click；源项目存档真源=localStorage `gomoku_save_<uuid>`。

## M4R — 路由重构（2026-09-19，人类验收反馈返工）

- 人类反馈三问题：①ESC 无响应（顺序求值无 early-return，同帧 pause+resume 互抵）②R 一键双触发 ③标题屏可见棋盘。
- 修复：**首中即吞**（单次动作命中即 return）；**SceneRouter 三层路由栈**（L0 基座/L1 模态/L2 浮层，
  `StackFor` 唯一映射，KeepAlive 基座切可见性）；背板体系（None/Dim/Blur/Opaque）；保留现场语义；
  浮层冒泡（Esc 关最顶）；**KeymapTable 单一数据源**（门控/F1/文档三处同源）。

## M4 第二轮 — 明暗主题/字体/关卡/文件存档（2026-09-19）

- Palette 明暗双套+WithColorblind（V 与 T 正交）+Ghost 反转；T→EventBus→换肤（随档恢复）；
  霞鹜文楷全局字体；关卡系统三预设+沙盒表单+命令行并入同一装配路径；.nzsave 文件级导入导出+FileDialog。
- **限制（后由 B14 解决）**：v2.0.0 schema 无 winLength 字段 → 外圈元数据。

## M4 第一轮 — 游戏壳全通（2026-09-19）

- Core：GameStateMachine 七态+§3.4 转移表穷举；NzSaveContainer+SaveArchive 四路嗅探；IOverlayManager 落 Core。
- Game：GameStore（转移副作用集中）+FileSystemStorageAdapter+ShortcutRouter+OverlayManager（模态互斥/
  回调恰好一次/toast 堆叠）；壳屏六件；Ctrl+S 快存/L 读最新/剪贴板导出导入；GameManager 快照/恢复/复盘门控。
- **教训**：§3.4 Any→REPLAY 七态全可达（穷举测试抓漏）；缺 using 报错被 lambda 级联噪音吞；
  manifest 键序 camelCase（TestFirst 抓住）。

## M0–M3 基线与关键教训（2026-09-18/19，已压缩，详情见 git log 与 §10）

- **M0 脚手架**：Core/Game/Tests 三项目 + `Nmoku.slnx`（SDK 10 新格式，TFM 仍 net8.0）+ InputMap + 霞鹜文楷 TTF + CI。教训：`--headless --import` 会回写 `project.godot`；Godot 控制台壳须与主 exe 成对。
- **M1/M1.5 纯逻辑层**（121 测试全绿）：Board/Rules(13向)/Standard+Swap2/存档 v2/AI/EventBus/PatternCollector。教训（§10）：B1 SWAP2 决策期指针不动；B3 旧单体档空条目下标照常递增；B5 辅助线 4 格上限在渲染端。
- **M2 3D 视图+相机**（tag `m2`）：`CoordMapper` 单点收敛 + `CameraMath`/`CameraRig` 八步管道 + 决策 #9 自由相机 + `Palette` 语义调色板；B8 世界朝向（行=Y、层=Z）。
- **M3 2D 面板+辅助线**（tag `m3`）：`Board2DView` 自绘 + `AuxScan` 悬停扫描 + `GameManager`。教训：B9 辅助线首子即停 + 网格反比例雾化 `1/(1+d)` + O 键全亮；B10 整盘绕 Y 转 180°；`SizeFlags.Expand` 只分配不填充（须 `ExpandFill`）。

## 更早

- 计划与决策：见 `docs/migration-plan.md` §0（9 项人类决策）与 §5（M0–M5 里程碑）。
