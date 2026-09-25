# Nmoku (Gomoku 613B-E2D → Godot C#)

6×13×13 三维五子棋的 Godot 4 (.NET/C#) 迁移版，源项目：[gomoku-613B-E2D](https://github.com/phixt/gomoku-613B-E2D)（TypeScript + Three.js + Tauri）。

- 迁移计划：[docs/migration-plan.md](docs/migration-plan.md)
- 目标平台：Windows 桌面 x64
- 引擎：Godot 4.6.x .NET 版 + .NET 8

当前处于 M4阶段，迁移已经完成大半，但尚无可运行工程。

## 主要更改
- 离散视角->平滑连续视角
- 摄像机焦点不再固定，可以视角平移
- 旋转动作和层间缩放平滑
- 主题系统（暂时未实现）
- 存档重构，可以导出实际为zip的特有格式文件。

## 许可证

本项目源码采用 [GNU General Public License v3.0](LICENSE)（GPL-3.0-only）授权，使用、修改或分发本项目时须遵守 GPL v3.0 的许可条件。项目中的第三方依赖与素材仍受其各自许可证约束。
