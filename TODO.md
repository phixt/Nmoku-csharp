# TODO

> 维护规则见 `AGENTS.md`：本文件按"当前→近期→远期挂账"三层组织，收工必更新；总量 ≤300 行，已完成的尽快移入 CHANGELOG/git log，不丢当阶段关键教训。

## 当前（相机异常已定案修复；只待人类真实窗口走查 → tag `m4`）

2026-09-25 新会话按 HANDOVER 复现并定案相机异常：**B16 v1（stretch=false 手动喂尺寸）与窗口自适应
（ContentScaleFactor>1）合体触发每帧 ×缩放正反馈回环**（SubViewportContainer 以视口尺寸为最小尺寸），
实测 21 帧内视口 int32 饱和、texture_create 失败刷屏 = 黑屏事故根因；相机无恙。修复=B16 v2 引擎
超采样（不触碰 Viewport.Size），双缩放场景真实窗口验证通过（详见 CHANGELOG 与 migration-plan §10 B16）。

已恢复（各自独立验证）：B15 toast 修复（保留未回退）/ B16 v2（ad73d4b）/ f5cd909 批（8af84bf：
窗口自适应+存档对齐+自测加固）。门禁：build 0 警告 / 273 测试 / 无头自测 ALL PASS。

- [ ] **人类真实窗口走查**（一批一验的合并单）：高分屏 UI 尺寸观感（窗口 85% 居中、字号随 DPI）→
      3D 清晰度（原生像素，对比修复前）→ 进对局相机稳定（无放大/黑屏）→ 存档语义（S 键落首空槽/
      存档菜单覆盖+确认框/快存槽免确认/暂停菜单键位提示）→ T 明暗/文楷/自定义对局/文件导入导出/F1/
      B13 假棋子（SWAP2 显示白方）/连珠数随档（B14）/连发 toast 不卡
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
