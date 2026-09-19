# TODO

> 维护规则见 `AGENTS.md`：本文件按"当前→近期→远期挂账"三层组织，收工必更新；总量 ≤300 行，已完成的尽快移入 CHANGELOG/git log，不丢当阶段关键教训。

## 当前（M2 收尾：人类标定验收）

- [x] M2：CameraRig 纯数学（03-camera §9 八步管道 + §8 缓存 + 决策 #9 自由交互）入 Core + xUnit（174 绿）
- [x] M2：Board3DView（MultiMesh 黑/白实体 + 逐层 ghost 按层透明度、单 ArrayMesh 网格线、悬停蓝点占位）+ Main 接线 + 输入（Q/E ±90°、W/S 持续缩放、滚轮锚点缩放、左键拖旋转、中键拖平移、Z/C 层间距、X 复位）；标定参数 `-- --board=9 --layers=1 --spacing=6.0`
- [ ] M2：**相机标定对照**（§3.2 / R1 R2）——与源项目 `npm run dev` 并排比 默认盘 / 9×9×1 / 13×13×6 spacing 6.0 三组构图；构图差异先读 §10 B7（源相机五处缺陷/简化，以文档为准）；镜像问题收敛在 CoordMapper 单点；拖拽方向/灵敏度手感顺带确认
- [ ] M2 验收：三组截图目视一致 + FPS ≥ 60 + tag `m2`
- [ ] M3 前：读源 TS `RightPanel.ts`/`LeftPanel.ts`/`main.ts` 输入段，把辅助线/2D 面板行为清单写进 migration-plan R4 节并经人类确认（铁律 2，R4 高风险项）
- [ ] M3：GameManager 接线（回合/合法性/胜负/判和）+ 2D 自绘面板 + 辅助线双渲染器 + 快捷键门控
- [ ] **AI 难度梯度可视化校验**（人类验收工具）：以可理解的方式呈现 Easy/Medium/Hard 的实力差距，
      理想形态为不同难度 AI 互弈的批量对局 + 胜率/评分曲线。可能需要改 Core 的 AI 对局驱动与统计接口，
      底层改动成本随 M2/M3 接线递增，**尽量在 M3 GameManager 接线前定夺接口形状**（本轮仅挂账，不动手）
- [ ] M3 后：复核 B4——AI"堵优先于胜"的体感，必要时人类拍板是否偏离文档式
- [ ] M4：真实浏览器导出 Base64 样本入 Fixtures，闭环 R6（见 Fixtures/README.md 尾注）
- [ ] M4：.nzsave zip 容器 + user:// FileSystemStorageAdapter + 剪贴板互通验证

## 远期挂账

- [ ] **Connect6**（2026-09-18 人类拍板进待办，优先级高于禁手）：单子模型 `IsLegalMove`/`CheckWin` 不够用，
      动手前先出**接口演进设计**（`PlaceMoves` 或回合内两阶段）经确认再实现；源项目未实装
- [ ] Renju 禁手（三三/四四/长连判负）：2026-09-18 拍板进待办，**优先级调低**，排 Connect6 之后；`IsLegalMove` 现有签名（player/history）够用，无需接口演进
- [ ] 开局库变体（Swap1/Pro/Long Pro）：之后加，**前提是与 GameManager/状态机解耦**（策略化 + 数据驱动 LevelConfig，加关卡零改码）；解耦成本过高则放弃。
      演示关 tutorial/residual **视为占位符**，M4 关卡系统时重设计，不照搬源项目
- [ ] Godot 导出模板 ~900MB（M5 打包前再下载，不阻塞前期）
- [ ] 音乐补全（源项目遗留 TODO）；SynthMusicEngine 合成器回填（IMusicEngine 接口已挂）
- [ ] i18n：TextConstants → CSV 四语言（zh-CN 为源）+ 语言切换 UI
- [ ] 自定义调色板 JSON 导入导出 UI（Palette 字段已语义化，编辑器不做）
