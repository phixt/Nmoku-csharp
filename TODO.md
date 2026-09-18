# TODO

> 维护规则见 `AGENTS.md`：本文件按"当前→近期→远期挂账"三层组织，收工必更新；总量 ≤300 行，已完成的尽快移入 CHANGELOG/git log，不丢当阶段关键教训。

## 当前（M1.5 review 修复批，2026-09-19 执行）

M1 review（2026-09-18）验收通过；以下修复项已经人类确认，除注明外零行为变化：

- [ ] 契约文档化：`IRuleEngine`/`TurnState` XML doc 写死调用约定——`GetNextTurnState` 的 `currentPlayer` 是**刚行棋方**，swap 决策期 nextPlayer/指针不动（决策者=Message 受众白方，弹窗决策）；同步修 `SwapEngine.cs:31` 注释口径
- [ ] 存档健壮性：`SaveValidator` 行宽改**逐行**校验（现只查每层第一行，y>0 错配放行）；`SaveManager` 载入路径 v2 直通分支（Normalize 不过 Validate）补校验，维度错配不得穿透进槽位缓存
- [ ] 规则层小收敛：`RuleEngineFactory.Create(RuleSet, winLength)`（M3 接线前消灭 Game 侧 if-else 隐患）；盘满判和 API（00-domain §4.9，现仅 Board.MoveCount 注释提及）
- [ ] 清理：`RuleEngineTests.AllEngines` 死字段、`RulesTests.cs:54` 恒等三元、`AIEngineTests` "HardWithOwnFour" 名不副实改名
- [ ] 计划文档 §10：修 B1 措辞——源实际节奏为黑下第 1、2 手（决策插在中间），白下第 3 手；补 B6：AI 等分平局策略 C#（种子随机池）vs TS（先到先得），两实现候选顺序本就不同，无保真风险
- [ ] 批次验收：本地 build 0 警告 + 测试全绿后**一次性** push 走 CI 门禁（铁律 9，不逐 commit 推）

## 近期（M2 为主，M3–M4 预备）

- [ ] M2：CameraRig 纯数学（03-camera §9 八步管道 + §8 缓存脏标记）入 Core + xUnit
- [ ] M2：Board3DView——MultiMesh×4（黑/白×实体/ghost）、线网格 ArrayMesh、`ComputeOpacity` ghost 透明
- [ ] M2：**相机标定对照**（§3.2 / R1 R2）——与源项目 `npm run dev` 并排比 默认盘 / 9×9×1 / 13×13×6 spacing 6.0 三组构图；镜像问题收敛在 CoordMapper 单点
- [ ] M2：Q/E 旋转、W/S+滚轮缩放、Z/C 层间距、X 复位；悬停层高亮占位
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
