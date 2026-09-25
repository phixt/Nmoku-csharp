# TODO

> 维护规则见 `AGENTS.md`：本文件按"当前→近期→远期挂账"三层组织，收工必更新；总量 ≤300 行，已完成的尽快移入 CHANGELOG/git log，不丢当阶段关键教训。

## 当前（阻塞：相机异常待查——开工先读 HANDOVER-M4-camera-bug.md）

验收反馈批（存档对齐/窗口自适应/B16 视口）已**整体回退**（2026-09-25）：人类实测出现
"进入游戏视角分 4 次快速放大左上角 → 黑屏 → 灰屏 + 大量报错"，未定位。**保留** toast 死循环修复（B15，
提交 9d75f40，77GB/20GB 事故真凶）。回退后代码与人类实测正常点一致，门禁全绿（270 测试 + 55 步自测）。

- [ ] **阻塞项**：相机异常定位——真实窗口复现（无头不渲染），嫌疑清单与排查顺序见
      `HANDOVER-M4-camera-bug.md` §3；**窗口自适应与 3D 原生视口未分别重验前不得同时上线**
- [ ] 重落被回退成果（`git cherry-pick`，逐批 + 每批人类实测）：窗口自适应 → 存档对齐
      （S 键保存/槽位覆盖/暂停菜单键位提示）→ B16 视口
- [ ] 恢复后的人类验收清单：T 明暗/文楷/自定义对局/文件导入导出/F1/B13 假棋子（SWAP2 显示白方）/
      **连珠数随档**（B14）/路由视觉/连发 toast 不卡
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
