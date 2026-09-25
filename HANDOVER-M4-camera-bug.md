# 交接文档：M4 验收反馈批回退与相机异常待查（2026-09-25）

> **给下一个会话**：本文件是回退现场说明。开工前先按 AGENTS.md 惯例读 TODO/CHANGELOG/migration-plan，
> 再读本文件全文。**在真实窗口复现并定位文末的相机异常之前，不要重落被回退的任何一批改动。**

## 1. 发生了什么（时间线）

1. 收尾轮（B14/R6/契约核对/真实旧档，至 `7f51421`）推送后，人类实测**正常**（能玩，报出三个新需求：
   存档菜单只能读不能存、缺 S 键保存、高分 UI 过小）。
2. agent 一轮内落了三批改动并推送：**存档对齐源项目+窗口自适应+自测加固**（`f5cd909`）、文档（`6535e93`）、
   **3D 子视口正道化 B16**（`4df340f`+`dddc804`）。期间人类报告 `Can't change the size of a SubViewport...`
   警告刷屏（agent 以 B16 修复之）。
3. 人类实测 B16 后：**进入游戏后视角分 4 次左右快速放大左上角 → 黑屏 → 灰屏，大量报错**。
4. 人类决定回退 + 重开会话全面审查（怀疑 agent 幻觉）。agent 执行回退（见 §2），**相机异常未定位**。

## 2. 回退了什么 / 保留了什么

**已回退（`git revert`，历史保留可取回）**：
- `4df340f` B16 代码：GameScreen 容器 `Stretch=true→false` + `Main.SyncViewport3DResolution` 重写
- `f5cd909` 存档对齐（S 键/槽位覆盖/暂停菜单键位提示/KeymapTable.save_first_empty）+ **窗口自适应**
  （`ApplyDisplayScaling`：窗口按工作区 85% 居中 + `ContentScaleFactor`=OS DPI 缩放）+ 自测框架加固
  （异常出队/限帧 120/单步与全局帧预算/**内存自检 6GB 自杀**/`--autotest-only` 步骤过滤）
- `6535e93`、`dddc804` 上述两批的文档（README/keymap/CHANGELOG/B16 登记）

**保留（已独立验证，勿动）**：
- `9d75f40` **toast 溢出裁剪死循环修复（B15）**——77GB/20GB 提交内存事故真凶，
  `RemoveChild` 立即移出再 QueueFree；与渲染链无关，无头自测+单元测试验证
- 收尾轮全部成果：B14 winLength 外圈元数据 / R6 真实样本双向闭环 / §6.3–6.5 契约核对 / 真实旧档端到端恢复

**回退后净状态**：代码与 `7df6439`（人类实测正常点）逐字节一致，仅多 OverlayManager 的 toast 修复。
门禁：build 0 警告 / 270 测试全绿 / 无头自测 ALL PASS（55 步，save-alignment 步随批回退）。
注意：migration-plan §10 的 **B16"定稿"结论随代码一起回退，暂不可信**，重验前当假设。

## 3. 待查异常（唯一阻塞项）

**症状**（人类真实窗口，B16 之后）：进入游戏后视角分 4 次左右**快速放大左上角** → 黑屏 → 灰屏，大量报错。
无头自测全绿——**无头不渲染，此 bug 必须真实窗口复现诊断**（M3 灰屏同款教训）。

**嫌疑清单（按可能性排序，全部未证实）**：
1. **窗口自适应与视口链耦合**：`ApplyDisplayScaling` 在 `_Ready` 首行改窗口尺寸（85% 工作区）+
   设 `ContentScaleFactor`=OS 缩放（人类机器 = 2.0），`GetFinalTransform().Scale` 随之变化——
   启动期窗口/视口尺寸连续变化，`HandleViewportChange` 每次尺寸变化都调
   `_rig.Update(boardSize, layers, spacing, aspect, GameConfig.DefaultYaw)` 重算相机公式。
   **"分 4 次快速放大"与启动期视口尺寸分步收敛的帧数吻合**。
2. **B16 的 SetSize 真正生效后的连锁**：视口被喂到「容器逻辑 × 内容缩放」原生像素
   （人类机器 ≈1688×1530+），MSAA 8× 下若尺寸算错（如 factor 双算 → 3000+ 宽）→ GPU 过载/
   驱动重置 → 黑/灰屏 + 报错。**请索取报错全文**（当时未被捕获）。
3. `_rig.Update` 的 `GameConfig.DefaultYaw` 参数：每次视口尺寸变化都重设 yaw/公式距离——
   对照 M2 决策 #9 的"用户缩放覆盖"语义，检查多次连续 Update 之间 CameraRig 状态是否丢逸
   （CameraRigTests 只覆盖单次 Update）。
4. 存档对齐本体（S 键/覆盖/菜单）与视角无关，嫌疑最低，但同批回退，重落时一并走查。

**建议排查顺序**：真实窗口复现 → 抓 `display:` 行 + 报错全文 + `--verbose` →
最小化验证：只重落窗口自适应（不带 B16）实测；再只落 B16（不带窗口自适应）实测；两者都过才合。
**在两者都重验通过前，"窗口自适应"与"3D 子视口原生像素"不得同时上线**——它们共享
`GetFinalTransform().Scale` 这一条状态链。

## 4. 被回退成果的取回方式

- 取回单批：`git cherry-pick f5cd909`（存档对齐+窗口自适应+自测加固）、`git cherry-pick 4df340f`（B16）。
- 只看内容：`git show f5cd909` / `git show 4df340f`。
- 文档（README 特性刷新 / keymap 存档语义与分辨率节 / CHANGELOG 两节）：在 `6535e93`、`dddc804` 里。
- **建议重落顺序**：窗口自适应（真实窗口验证观感与相机）→ 存档对齐（S 键/覆盖/键位提示，功能走查）→
  B16 视口（高分清晰度复查）→ 各批之间人类实测，一批一验收。

## 5. 本会话沉淀的通用教训（已入 CHANGELOG，新会话别再踩）

1. **Godot 实际运行 `.godot/mono/temp/bin` 的程序集；`dotnet build` 失败时 Godot 静默跑旧构建**——
   门禁命令必须先确认 `0 个错误` 再跑引擎（`| tail -2` 吞掉过 5 个编译错误，白跑三轮）。
2. stdout 重定向有缓冲迷惑性；冻结定位用**直写文件**的追踪，且追踪助手必须内部吞异常 + 绝对路径。
3. `QueueFree` 等延迟生效 API 不得作为循环终止条件（B15，已修）。
4. headless 下 `PopupCentered` 弹窗会瞬间自动确认；涉及确认框的断言用合成信号（overlay-contract 模式）。
5. UI/布局/渲染类 bug 无头全绿不代表没问题——**每批涉及窗口/视口/相机的改动都要人类真实窗口实测**。
