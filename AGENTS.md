# AGENTS.md — Agent 协作规约（唯一协调点）

本仓库由 AI agent 主导开发（qoder / dsh / zcode 等）。人类负责决策与验收，agent 负责实现。
**开工前读本文件 + `CHANGELOG.md` + `TODO.md` + `docs/migration-plan.md`；收工前更新「进度」节与 TODO/CHANGELOG，并独立 commit。**

## 环境（已验证 2026-09-18）

- .NET SDK：8.0.425 与 10.0.401 并存（`C:\Program Files\dotnet`），项目 TFM 统一 `net8.0`
  - 解决方案为 SDK 10 生成的 **`Nmoku.slnx`**（新 XML 格式，VS2026 原生支持）；`dotnet build` 在仓库根自动发现，无需写文件名
- Godot：4.6.2-stable mono win64，✅ 已验证（`4.6.2.stable.mono.official.71f334935`）
  - `D:\Tools\Godot\Godot.exe` — 主程序（编辑器 GUI，人类双击用）
  - `D:\Tools\Godot\Godot_console.exe` — 控制台版，**agent 一律用它**（stdout 可靠）；`GodotSharp/` 目录是其依赖，勿动
  - 用户 PATH 已添加，但**已启动的进程会话可能不继承**；agent 在既有终端里请用全路径 `/d/Tools/Godot/Godot_console.exe`
- 目标平台：仅 Windows 桌面 x64。无 Web/移动端要求。

## 常用命令

```bash
dotnet build                                        # 仓库根执行，全解决方案编译（Core/Game/Tests）
dotnet test tests/Nmoku.Core.Tests                  # 单元测试（秒级，不启动引擎）
/d/Tools/Godot/Godot_console.exe --headless --path src/Nmoku.Game --import    # 资产重导入（改场景/资源后必跑）
/d/Tools/Godot/Godot_console.exe --headless --path src/Nmoku.Game --quit      # 工程无错启动冒烟（应打印 "Nmoku M0: engine boot OK" 或后续主场景日志）
```

## 铁律

1. **`src/Nmoku.Core` 零 Godot 依赖**（不 using Godot.*、不引用 GodotSharp）。所有引擎类型只出现在 `Nmoku.Game`。
2. 游戏逻辑以 `docs/migration-plan.md` §4 常量表与源项目架构文档为唯一事实源；发现文档缺口（辅助线、2D 面板行为）先读源 TS 并在计划文档 R4 节登记行为结论，再写代码。
3. 公共 Core API 必须有 xUnit 测试；测试用例名 `Method_Scenario_Expectation`。
4. 一个 commit 只做一件事，信息格式：`M<里程碑>: <中文短句>`。
5. 不复制源项目 TS 代码进本仓库；仅逻辑按文档重写。素材（字体等）放 `assets-src/` 并附许可。
6. 遇到需要产品决策的分歧（UI 布局、交互语义），停下来问人类，不要自行拍板。
7. 禁止 `git push --force`、`reset --hard` 等破坏性操作。**开发主线在 `master`**（`prestart-native` 已于 2026-11-26 合回 `master` 并发布 `v0.2.0-beta`）；agent 之间靠进度节声明认领。**多会话/多 agent 并行时：一会话一分支（或 worktree），禁止两个 agent 在同一工作区并发改码**——并发改码必然产生冲突与分支污染（2026-10-26 审查发现本仓已有 4 个分支、2 条内容重复但哈希不同的并行实现线，正是此类产物）。
8. `CHANGELOG.md` 与 `TODO.md` 是交接窗口：只记**当前场景**相关事实（里程碑叙事、关键教训、待办分层），各自 **≤300 行**，超限时压缩旧内容但不得丢失当阶段关键教训；收工随进度节一并更新。
9. **push 节制**（2026-09-18 人类指示）：仅在提交**需要 CI 验证**（如改 workflow/构建/引擎相关）时才 `git push`；纯文档/交接类提交留在本地，随下一次需要 CI 的提交一并推送。CI 状态由人类在 Actions 页确认，agent 不轮询。
10. **构建/自测必须串行且守内存**（2026-10-26 补）：`dotnet build -m:1 -nodeReuse:false` 并置 `MSBUILDDISABLENODEREUSE=1`（禁 MSBuild 节点复用累积）；**Godot 无头实例一次只跑一个**，跑后确认进程已退；跑前跑后查可用内存。历史两次内存事故（M4 视口回环 8–14GB 分配 churn、toast 死循环提交 77.4GB，见 §10 B15）都源于**进程失控**，不是引擎 bug——并发跑多个构建/自测实例是最现实的复发路径。
11. **改动范围大于一个 commit 的工作时，拆批做并把路线图写进 TODO/CHANGELOG**（2026-10-26 补）：不要在一个会话里试图一次改完——按"止血 → 数据完整性 → 真实缺陷 → 架构化 → 视口/UI"分批，每批独立可验收、可回滚；跨会话的进度靠 TODO「批次路线图」交接，不靠上下文记忆。

## 进度（收工必更新）

| 里程碑 | 状态 | 认领 agent / 备注 |
|---|---|---|
| M0 环境与脚手架 | ✅ 完成（build/test/headless 冒烟全绿，2026-09-18） | qoder |
| M1 纯逻辑层+测试 | ✅ 完成（113 测试全绿，行为结论登记计划文档 §10；review 验收通过，修复批完成（M1.5，121 测试全绿，2026-09-19）） | qoder |
| M2 3D 左视图+相机 | ✅ 完成（人类验收通过 2026-09-19，tag `m2`；自由相机决策 #9 + B7/B8 行为结论） | zcode |
| M3 2D 右面板+输入+辅助线 | ✅ 完成（人类验收通过 2026-09-19，tag `m3`） | zcode |
| M4 状态机/UI/存档 | ✅ 完成（人类确认基本功能稳定 2026-09-26，tag `m4`；批 1–11 全落地：路由/存档定稿/复盘修复/最大化 ContentScaleSize 根治；302 测试+自测 ALL GREEN）。0.1.0-alpha 已备（config/version+发布说明），发布推送由人类执行。最大化过渡动效入待办 | zcode |
| M5 音频/i18n/发布 | 🚧 **代码完结 2026-09-26，待人类真实窗口走查 → tag `m5`**（音频占位版：四场景 BGM+落子/胜负 SFX+M 静音+WASAPI 恢复，正式 BGM 走 Strudel 谱面路线、谱面已入库 WAV 留空；设置页音量/静音/窗口模式/语言；i18n 四语言 CSV+热切换、zh-CN 文本作键 Core 零改动；程序化图标+README 发布说明+打包复验 120MB 自包含；过渡动效最小方案。304 测试+自测 ALL PASS，行为结论 B20 登记 §10）。**验收反馈批 1（2026-09-26）**：标题屏溢出根治（内容压缩 fit 432+窗口 MinSize+模式偏好闭环+m5-title-fit 锁，computer use 三模式实测）。**验收反馈批 2（2026-10-01）**：设置页四 bug 根治（滑条 ExpandFill/静音 MuteChanged 联动/语言 StandardizeLocale 匹配+启动恢复 lang/模式初值=实际态+恢复期防覆盖）+ m5-settings-bugs 回归锁 + 自测浮层清理死代码修复，computer use 复验通过；语言无偏好默认 zh-CN；UI 缩放体系重构设计仍入待办待实施。验收关注点见 TODO「当前」 | zcode |
| M5-pre 预开始页原生实现 | 🚧 **代码完结 2026-10-05，人类尚未真实窗口走查**（分支 `prestart-native`，13 提交；R4–R10 迭代见 CHANGELOG「M5-pre」节）。原生 Godot 方案：复用真实 `Board3DView`/`CameraRig`/`AuxScan`（M2/M3 人类验收版机制），Core 零改动；N 字标改三维线格字、转换动效改真形变（顶点/线宽/深度/相机偏航连续插值，零交叉淡化可逆）、R6 演示棋盘可真实落子。**2026-10-26 审查发现 7 处遗留**（角标永在屏外/继续对局可见性被覆盖/Rebuild 退回字形态/自转不可见/标题列 432 高 vs MinSize 320×416 出界/隐藏后仍全速运行/2 处硬编码中文）→ 排在 TODO 批次路线图 ⑤ 批 | dsh |
| M5 修复批 1 — 全面代码审查止血 | ✅ **完成 2026-10-26**（5 commit：BGM ε 停滞挂轨 / KeymapTable.Allows any 语义 / 读档入口畸形输入加固+尺寸闸门 / 命令行尺寸校验 / 构建发布卫生）。**321 测试全绿**（304→321，+24 例回归锁）+ build 0 警告 + 无头自测 ALL PASS（修复前 8 轮失败 2 次）。§10 补回 B15/B21、修表头 Markdown | dsh |
| M5 修复批 2 — 数据完整性 | ✅ **完成 2026-10-27**（6 commit：槽位主键改槽位号+旧盘布局迁移 / 写盘失败可见+读档半加载根治 / 校验层规则白名单与值域闸门 / 垃圾 JSON 不造空档 / zip bomb 闸门 / 无头自测两步）。P0-1 数据丢失根治（复制→填写不再抹掉原槽；导入保真 id 语义不变）。**355 测试全绿**（321→355，+34 例）+ build 0 警告 + 无头自测 ALL PASS。§10 新增 B22/B23/B24 | dsh |
| M5 修复批 3 — 真实缺陷 | ✅ **完成 2026-10-27**（10 commit：PvE 执子方门控（对齐源 main.ts:120）/ 相机公式距离重算（用户缩放改存倍率）+ 假绿断言修正 / 窗口缩放后相机跟随当前关卡 / 最小化不写回窗口模式偏好 / 浮层作用域改走 KeymapTable 数据源 / 键位护栏自维护化（清单由 project.godot 生成）/ 标题屏布局锁守卫对齐+菜单列自适应；顺带 3 项：i18n 测试解除布局耦合、键位「展示↔绑定」一致性审计、BGM 回归锁去时刻采样化）。**362 测试全绿**（355→362，+7 例）+ build 0 警告 + 无头自测连续多轮 ALL PASS。§10 新增 B25/B26/B27 | dsh |
| M5 修复批 4 + 开始界面重做 + v0.2.0-beta 发布 | ✅ **完成并合回 `master`（2026-11-26，tag `m5` / `v0.2.0-beta`）**（Core 边界防御 / AI B28–B30 泛化与快照隔离 / 2D·3D 共用辅助线与池化 / 壳层卫生与 `Autotest` 拆分 / `PreTitleScreen` 重做为雕刻多面晶体 N + 素雅双子差速公转 + 去除焦散 / 全局 `UiTheme` 统一霞鹜文楷字体与清透按钮皮肤 + 暗底黑子可见度与深浅棋盘明晰度调优 + 高分屏 32× 过采与隐藏视口空转性能根治 + 亮色立体 N 纯平矢量应用图标）。**382 测试全绿** + build 0 警告 + 无头自测 ALL PASS + `Nmoku-0.2.0-beta-win64.zip` 冒烟通过 + 3200×2000 真实窗口暗/亮截帧与 60fps 实测通过。§10 新增 B28/B29/B30；UI 缩放重构暂缓 | antigravity |
| M5 正式音频认领与播放机制升级 | ✅ **代码与资产完结（2026-11-27），待人类真实窗口试听验收**（从同级 `../programmed-music/out/nmoku_ogg/` 认领 5 轨无缝立体声 BGM 含 `bgm_game_2` + 真音源 `sfx_win`/`sfx_lose`；`AudioBake` 升级烘焙黑白子玄石/玉石双敲采样与 `-SyncMusic` 同步；Core 新增 `AudioPolicy`/`SfxDebounceGate`/`BgmDuckEnvelope`；接线人类/AI/演示盘/复盘分色落子声+65ms 同色防抖+±3% 微抖+双声部复音、PvE 胜负区分与 3s 短乐句 BGM 闪避、双曲对局池会话续播与新局亲和/轮换、设置页「对局音乐」「切屏音乐」四语言偏好；顺带修清 SWAP2/PvE 4 处缺陷含读档 `_aiColor` 反转与幽灵弹窗 + 决策轻触音效）。**400 测试全绿**（+18）+ build 0 警告 + `Godot --import`/`--quit`/`--autotest` ALL PASS。§10 新增 B31 | antigravity |
| M5 UI 缩放适配 + 音乐与音效中心 + 作者的话预留 | ✅ **代码完结（2026-11-27），待人类真实窗口走查验收**（Core `UiScaleMath` 实现 960×540 基准与 `csf=(窗口高/540)×UI倍率`，`SettingsScreen` 专注显示/分辨率/UI 倍率/语言；独立浮层 `AudioHubScreen` 合并原设置页音频项，新增音效/音乐独立开关 + 5 轨 BGM 曲目浏览器/实时进度跳转/恢复场景曲 + 音效试听，标题/暂停/设置页均可直达；`GuideScreen` 右上角预留默认折叠「作者的话」卡片）。**410 测试全绿**（+10）+ build 0 警告 + `Godot --import`/`--quit`/`--autotest` ALL PASS（新增 `m5-audio-hub`/`m5-guide-author-note`）。§10 新增 B32 | antigravity |

> **分支与 CI 现状**（2026-11-26）：`prestart-native` 已快进合回 `master` 并推送 `origin`（触发 GitHub Actions CI），已打 `m5` 与 `v0.2.0-beta` 标签；废弃分支 `bgm-engine` 与 `bgm-engine-mixed-backup` 已清理。

> 文档补充：已添加 GNU GPL v3.0 协议与 README 授权说明。

## MCP / 工具说明

本项目**不引入 MCP server**：引擎操作全部 CLI 化（见上）。浏览器类 MCP 仅当需要
对照运行源项目（`npm run dev`，位于 `D:\P_rojects\gomoku-613B-E2D`）做视觉比对时使用，可选。
