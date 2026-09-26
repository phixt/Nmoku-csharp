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
7. 禁止 `git push --force`、`reset --hard` 等破坏性操作；单分支 `master` 直连开发，agent 之间靠进度节声明认领。
8. `CHANGELOG.md` 与 `TODO.md` 是交接窗口：只记**当前场景**相关事实（里程碑叙事、关键教训、待办分层），各自 **≤300 行**，超限时压缩旧内容但不得丢失当阶段关键教训；收工随进度节一并更新。
9. **push 节制**（2026-09-18 人类指示）：仅在提交**需要 CI 验证**（如改 workflow/构建/引擎相关）时才 `git push`；纯文档/交接类提交留在本地，随下一次需要 CI 的提交一并推送。CI 状态由人类在 Actions 页确认，agent 不轮询。

## 进度（收工必更新）

| 里程碑 | 状态 | 认领 agent / 备注 |
|---|---|---|
| M0 环境与脚手架 | ✅ 完成（build/test/headless 冒烟全绿，2026-09-18） | qoder |
| M1 纯逻辑层+测试 | ✅ 完成（113 测试全绿，行为结论登记计划文档 §10；review 验收通过，修复批完成（M1.5，121 测试全绿，2026-09-19）） | qoder |
| M2 3D 左视图+相机 | ✅ 完成（人类验收通过 2026-09-19，tag `m2`；自由相机决策 #9 + B7/B8 行为结论） | zcode |
| M3 2D 右面板+输入+辅助线 | ✅ 完成（人类验收通过 2026-09-19，tag `m3`） | zcode |
| M4 状态机/UI/存档 | 🚧 验收反馈批 1–11 完结（2026-09-26）：批9 存档菜单定稿、批10 复盘界面修复、批11 任务栏遮挡根治（机制定案=相机拖拽逃逸到标题栏被 Aero-Snap 最大化，修复=拖拽期 Confined 光标约束）均已落地待人类实测，302 测试+无头/真实窗口双全量 ALL GREEN；wintrace 常开+m4c-window-sequence 看守。另修自测污染真实存档缺陷；设置页/比例切换动效/3D 加载黑闪已入待办。tag `m4` 待打；随后进 M5 | zcode |
| M5 音频/i18n/发布 | ⬜ | |

> 文档补充：已添加 GNU GPL v3.0 协议与 README 授权说明。

## MCP / 工具说明

本项目**不引入 MCP server**：引擎操作全部 CLI 化（见上）。浏览器类 MCP 仅当需要
对照运行源项目（`npm run dev`，位于 `D:\P_rojects\gomoku-613B-E2D`）做视觉比对时使用，可选。
