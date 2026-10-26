# Nmoku（立体五子棋）

6×13×13 **三维五子棋**的 Godot 4 (.NET/C#) 版本，源项目：[gomoku-613B-E2D](https://github.com/phixt/gomoku-613B-E2D)（TypeScript + Three.js + Tauri）。
在三维棋盘的任意层连成 N 子直线即获胜——同层横竖斜、跨层斜线（13 个方向）均可。

- 迁移计划：[docs/migration-plan.md](docs/migration-plan.md)
- 目标平台：Windows 桌面 x64
- 引擎：Godot 4.6.x .NET 版 + .NET 8
- 当前版本：**0.1.0-alpha**（M0–M5，进度见 [AGENTS.md](AGENTS.md)）

## 下载与运行

1. 从 [Releases](../../releases) 下载 `Nmoku-<版本>-win64.zip` 并解压。
2. 双击 `Nmoku.exe` 运行。**自包含发布**：内嵌 .NET 运行时，目标机无需安装 .NET 或 Godot。
3. 系统要求：Windows 10/11 x64；首次启动会创建存档目录（`%APPDATA%\Godot\app_userdata\Nmoku\`）。

## 玩法速览（F1 游戏内有全表）

| 操作 | 键位 |
| --- | --- |
| 落子 | 鼠标点击右侧 2D 面板（悬停显示辅助线） |
| 切换焦点层 | `A` / `D` |
| 旋转视角 | `Q` / `E`，或左键拖动（无级） |
| 缩放 / 平移 | `W` / `S` 或滚轮；`Shift+WASD` 平移 |
| 悔棋 / 快存 / 读快存 | `Ctrl+Z` / `Ctrl+S` / `Ctrl+L` |
| 暂停菜单 | `Esc`（重开 `R`、保存 `S`、读档 `L`、标题 `B`） |
| 明暗主题 / 色弱配色 / 静音 | `T` / `V` / `M` |
| 快捷键帮助 | `F1`（六层分页） |

更多功能：SWAP2 规则关卡、复盘步进（←/→）、六槽存档 + `.nzsave` 文件导入导出（与源浏览器版双向互通）、
四语言界面（设置页切换：简体中文 / 繁體中文 / English / 日本語）、窗口模式与音量设置（标题屏或暂停菜单 → 设置）。

## 从源码构建

```bash
dotnet build                                  # 全解决方案（Core/Game/Tests）
dotnet test tests/Nmoku.Core.Tests            # 单元测试
scripts/package.ps1                           # 一键打包（模板自检→构建→导出→冒烟→zip）
```

## 主要更改（相对源项目）
- 离散视角 → 平滑连续视角（无级旋转 / 视角平移 / 指数阻尼）
- 摄像机焦点不再固定，可以视角平移
- 旋转动作和层间缩放平滑
- 明暗主题系统（`T` 键切换，色弱 `V` 正交组合）
- 存档重构：6 槽位 + `Ctrl+S` 快存 + `S` 保存进度 + 槽位覆盖（确认框）+
  剪贴板 Base64 与 `.nzsave`（zip 容器）文件级导入导出，与源浏览器版双向互通；
  自定义连珠数随档恢复（外圈元数据）
- 关卡系统：预设关卡 + 自定义沙盒（尺寸奇数 ≥5、层数 1–8、连珠 4–9、规则、AI 难度）
- 三层路由壳（标题/对局/暂停/结算/引导/复盘/帮助/存档菜单/设置），F1 快捷键帮助屏与文档同源
- 音频系统：四场景 BGM（标题/对局/引导/暂停）+ 落子/胜负音效，音量与静音设置
- 快捷键全表见 [docs/keymap.md](docs/keymap.md)（游戏内 F1 同源）

## 许可证

本项目源码采用 [GNU General Public License v3.0](LICENSE)（GPL-3.0-only）授权，使用、修改或分发本项目时须遵守 GPL v3.0 的许可条件。项目中的第三方依赖与素材仍受其各自许可证约束（霞鹜文楷字体 OFL，见 `assets-src/fonts/`；BGM 谱面与占位音频为本项目自产）。
