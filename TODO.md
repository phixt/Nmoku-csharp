# TODO

> 维护规则见 `AGENTS.md`：本文件按"当前→近期→远期挂账"三层组织，收工必更新；总量 ≤300 行，已完成的尽快移入 CHANGELOG/git log，不丢当阶段关键教训。

## 当前（M4 开工就绪——建议新对话执行）

M3 已完结（代码 + 无头自测全绿，2026-09-19；**tag `m3` 待人类确认后补打**）。
**M4 开工必读**：本文件全文 + CHANGELOG「M1.5/M2/M3」节 + migration-plan §10.5（R4 行为清单与 D1–D4 决策）+ §10 B7–B9 + §3.4。

- [ ] M4：7 态状态机 + 全转移表（01-core §3/§3.4），非法转移拒绝；现 GameManager 逻辑并入 GameStore
- [ ] M4：OverlayManager（04-contracts §4.1 契约：确认框/输入框/toast/Esc、回调恰好一次）——替换 M3 的 SWAP2 ConfirmationDialog 占位与状态栏 Label
- [ ] M4：FileSystemStorageAdapter(user://) + SaveManager 槽位接线（Core 门面已就绪）+ `.nzsave` zip 容器 + Base64 导入导出（DisplayServer 剪贴板）
- [ ] M4：真实浏览器导出 Base64 样本入 Fixtures，闭环 R6（见 Fixtures/README.md 尾注）
- [ ] M4：复盘 REPLAY（←/→ 步进出/入 + 进度条；InputMap 已有 replay_prev/next/exit_replay）
- [ ] M4：Palette 资源化 + 明暗切换（T 键事件已挂；明暗数值反转 GhostBlack/White 等字段）+ 霞鹜文楷 TTF 导入
- [ ] M4：关卡系统（LevelConfig 数据驱动 + `--rules/--board` 命令行并入关卡选择；tutorial/residual 视为占位符重设计）
- [ ] M4 验收：04-contracts §6.3/6.4/6.5 全勾；真实 v2.0.0 旧档恢复棋局；验收后 tag `m4`
- [ ] 待人类：M3 tag `m3` 确认补打；偶数阶中心标注方案（低优先级）；AI 难度梯度可视化接口形状（M4 期间定夺，见近期）

## 近期（M4 期间与 M5 预备）

- [ ] **AI 难度梯度可视化校验**（人类验收工具）：以可理解的方式呈现 Easy/Medium/Hard 的实力差距，
      理想形态为不同难度 AI 互弈的批量对局 + 胜率/评分曲线。Core 的 AI 对局驱动与统计接口形状**需人类定夺**（挂账至今）
- [ ] M3 后：复核 B4——AI"堵优先于胜"的体感，必要时人类拍板是否偏离文档式
- [ ] M4 后：恢复滚轮 zoom-to-anchor——锚点改为射线拾取的格/子（M2 无限地面锚点版有远锚点大步进与
      pitch 钳制瞬移，已回退为径向缩放；B9 首子即停后拾取锚点与辅助线标记可共用）
- [ ] M5：AudioManager + 采样版 BGM（4 轨循环）+ SFX 全集 + M 静音（action 已挂）+ `IMusicEngine`/`SetIntensity` 接口就位
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
