# TODO

> 维护规则见 `AGENTS.md`：本文件按"当前→近期→远期挂账"三层组织，收工必更新；总量 ≤300 行，已完成的尽快移入 CHANGELOG/git log，不丢当阶段关键教训。

## 当前（验收反馈批 2 已落地：bug1+存档 UX+亮色主题；只待人类真实窗口走查 → tag `m4`）

### 已落地（待人类实测）

- **开始新对局继承读档配置**（已修）：`RequestStartNew` 恒恢复启动默认上下文（源
  `startGame=initGameContext(STANDARD_LEVEL)` 同构）；主题 **T 偏好模型**——新局恢复用户最近
  T 选择（默认暗色），读档带来的主题不进新局。回归步 `m4c-newgame-default-context`。
- **存档 UX（方案 1 P1–P3，已拍板落地）**：暂停「读档」按钮打开存档菜单（L 键保持读最新）；
  **Ctrl+L=读快存槽**（暂停态生效，§3.4 不扩表，空槽 toast）；快存行标注 Ctrl+S/Ctrl+L。
  回归步 `m4c-quick-load`。
- **亮色主题对齐源（方案 2 M1/M2，B17）**：根清屏色随主题（2D 背景暗色根因修复）；
  Light 套按源逐字段重校（底 #E8F4F5/网格 #A0C0C4/文字 #1A2E3A/强调 #2C4A4E 等）。
  门禁：build 0 警告 / 273 测试 / 无头自测 ALL PASS。

- [ ] **人类真实窗口走查（累计清单）**：①上一批（相机/窗口自适应/存档对齐）：进对局相机稳定、
      高分屏 UI 尺寸与 3D 清晰度、S 键/覆盖/暂停菜单键位提示；②bug1：读非默认档（如 994 亮色）→
      返回标题开始新对局 = 默认 13×13×6 + 偏好主题；T 切换后的偏好在新局保留；③存档 UX：暂停
      读档打开菜单可选槽位、Ctrl+S 写快存 → Esc → Ctrl+L 读回检查点、L 仍读最新；④亮色主题：
      T 切换后 2D 面板背景/标题/暂停/存档菜单/结算协调性（本次重校主观验收）、网格/星位/辅助线
      对比度、ghost 透明度；⑤文楷/自定义对局/文件导入导出/F1/B13 假棋子/连珠数随档/连发 toast
- [ ] 高分机器若帧率不足：降 MSAA 档位（超采样已提供 AA，观察项登记 CHANGELOG）
- [ ] M4 挂账：tutorial/residual 关卡占位重设计；偶数阶中心标注（低优先级）

## 近期（M4 之后与 M5 预备）

- [ ] **AI 难度梯度可视化校验**（人类验收工具）：以可理解的方式呈现 Easy/Medium/Hard 的实力差距，
      理想形态为不同难度 AI 互弈的批量对局 + 胜率/评分曲线。Core 的 AI 对局驱动与统计接口形状**需人类定夺**（挂账至今）
- [ ] M3 后：复核 B4——AI"堵优先于胜"的体感，必要时人类拍板是否偏离文档式
- [ ] M4 后：恢复滚轮 zoom-to-anchor——锚点改为射线拾取的格/子（M2 无限地面锚点版有远锚点大步进与
      pitch 钳制瞬移，已回退为径向缩放；B9 首子即停后拾取锚点与辅助线标记可共用）
- [ ] M5：AudioManager + 采样版 BGM（4 轨循环）+ SFX 全集 + M 静音（action 已挂）+ `IMusicEngine`/`SetIntensity` 接口就位。
      **开工先恢复音频驱动**：project.godot 现为 `driver_type="Dummy"`（M4R 临时缓解 WASAPI 设备失效报错与
      系统混音撕裂，见 CHANGELOG），恢复 WASAPI 后必须实测窗口切换/设备切换场景
- [ ] M5：TextConstants → CSV 四语言 + 语言切换；导出配置、Windows 打包 zip、README 收尾

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
