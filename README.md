# Nmoku (Gomoku 613B-E2D → Godot C#)

6×13×13 三维五子棋的 Godot 4 (.NET/C#) 迁移版，源项目：[gomoku-613B-E2D](https://github.com/phixt/gomoku-613B-E2D)（TypeScript + Three.js + Tauri）。

- 迁移计划：[docs/migration-plan.md](docs/migration-plan.md)
- 目标平台：Windows 桌面 x64
- 引擎：Godot 4.6.x .NET 版 + .NET 8

当前处于 **M4 验收阶段**：游戏可运行，状态机/UI/存档/路由/主题/关卡系统均已落地，
剩全流程人类走查后打 tag `m4`（进度见 [AGENTS.md](AGENTS.md) 与 [TODO.md](TODO.md)）。

## 主要更改（相对源项目）
- 离散视角 → 平滑连续视角（无级旋转 / 视角平移 / 指数阻尼）
- 摄像机焦点不再固定，可以视角平移
- 旋转动作和层间缩放平滑
- 明暗主题系统（`T` 键切换，水绿宣纸 light / 深色 dark，色弱 `V` 正交组合）
- 存档重构：6 槽位 + `Ctrl+S` 快存 + `S` 保存进度 + 槽位覆盖（确认框）+
  剪贴板 Base64 与 `.nzsave`（zip 容器）文件级导入导出，与源浏览器版双向互通；
  自定义连珠数随档恢复（外圈元数据）
- 关卡系统：预设关卡 + 自定义沙盒（尺寸奇数 ≥5、层数 1–8、连珠 4–9、规则、AI 难度）
- 三层路由壳（标题/对局/暂停/结算/引导/复盘/帮助/存档菜单），F1 快捷键帮助屏与文档同源
- 高分屏适配：UI 随系统 DPI 缩放，窗口按工作区 85% 居中；3D 视口按原生像素渲染
- 快捷键全表见 [docs/keymap.md](docs/keymap.md)（游戏内 F1 同源）

## 许可证

本项目源码采用 [GNU General Public License v3.0](LICENSE)（GPL-3.0-only）授权，使用、修改或分发本项目时须遵守 GPL v3.0 的许可条件。项目中的第三方依赖与素材仍受其各自许可证约束。
