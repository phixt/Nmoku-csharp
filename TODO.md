# TODO

> 维护规则见 `AGENTS.md`：本文件按"当前→近期→远期挂账"三层组织，收工必更新；总量 ≤300 行，已完成的尽快移入 CHANGELOG/git log，不丢当阶段关键教训。

## 当前（M2 开工就绪）

- [ ] M2：CameraRig 纯数学（03-camera §9 八步管道 + §8 缓存脏标记）入 Core + xUnit
- [ ] M2：Board3DView——MultiMesh×4（黑/白×实体/ghost）、线网格 ArrayMesh、`ComputeOpacity` ghost 透明
- [ ] M2：**相机标定对照**（§3.2 / R1 R2）——与源项目 `npm run dev` 并排比 默认盘 / 9×9×1 / 13×13×6 spacing 6.0 三组构图；镜像问题收敛在 CoordMapper 单点
- [ ] M2：Q/E 旋转、W/S+滚轮缩放、Z/C 层间距、X 复位；悬停层高亮占位
- [ ] M2 验收：三组截图目视一致 + FPS ≥ 60 + tag `m2`

## 近期（M3–M4 预备）

- [ ] M3 前：读源 TS `RightPanel.ts`/`LeftPanel.ts`/`main.ts` 输入段，把辅助线/2D 面板行为清单写进 migration-plan R4 节并经人类确认（铁律 2，R4 高风险项）
- [ ] M3：GameManager 接线（回合/合法性/胜负）+ 2D 自绘面板 + 辅助线双渲染器 + 快捷键门控
- [ ] **AI 难度梯度可视化校验**（人类验收工具）：以可理解的方式呈现 Easy/Medium/Hard 的实力差距，
      理想形态为不同难度 AI 互弈的批量对局 + 胜率/评分曲线。可能需要改 Core 的 AI 对局驱动与统计接口，
      底层改动成本随 M2/M3 接线递增，**尽量在 M3 GameManager 接线前定夺接口形状**（本轮仅挂账，不动手）
- [ ] M3 后：复核 B4——AI"堵优先于胜"的体感，必要时人类拍板是否偏离文档式
- [ ] M4：真实浏览器导出 Base64 样本入 Fixtures，闭环 R6（见 Fixtures/README.md 尾注）
- [ ] M4：.nzsave zip 容器 + user:// FileSystemStorageAdapter + 剪贴板互通验证

## 远期挂账（M5 及以后）

- [ ] Godot 导出模板 ~900MB（M5 打包前再下载，不阻塞前期）
- [ ] 音乐补全（源项目遗留 TODO）；SynthMusicEngine 合成器回填（IMusicEngine 接口已挂）
- [ ] i18n：TextConstants → CSV 四语言（zh-CN 为源）+ 语言切换 UI
- [ ] 自定义调色板 JSON 导入导出 UI（Palette 字段已语义化，编辑器不做）
- [ ] Connect6 规则引擎（仅接口位，源项目亦未实装）
