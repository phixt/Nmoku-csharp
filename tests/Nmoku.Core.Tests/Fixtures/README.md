# 存档测试夹具（B5 / 风险 R6）

> 目的：M1 实现 `SaveData` 反序列化、M4 实现读取迁移/校验/Base64 剪贴板导入时，
> 以本目录为**先行固化的行为样本**。构造依据为源项目
> `src/core/SaveManager.ts`（schema、`migrateData`、`validateSaveData`、
> `serializeToBase64`/`tryParseSaveData`）与 `src/core/LocalStorageAdapter.ts`（存储键）。

## 源存储键（浏览器 localStorage / Godot user:// 对应关系见迁移计划 §3.1）

| 键 | 内容 |
|---|---|
| `gomoku_save_index` | `SlotMeta[]`（见 `slot_index_example.json`） |
| `gomoku_save_<uuid>` | 单个 SaveData 的 JSON 字符串 |
| `gomoku_saves_v3`（遗留） | 单文件时代全部存档数组，init 时分片迁移后删除 |

## 样本清单与预期行为

| 文件 | 形状 | C# 侧预期 |
|---|---|---|
| `v2_standard_inprogress.json` | v2.0.0，9×9×3，5 手，含 `timestamp`/`evaluation` 可选字段 | 直接反序列化；`boardState[z][y][x]` 与 moves 一致 |
| `v2_swap2_pve.json` | v2.0.0，swap2+PvE，`aiDifficulty=medium`，dark | 枚举解析（rules/gameMode/difficulty） |
| `v2_finished_black_win.json` | v2.0.0，默认 13×13×6，黑横向五连终局 | `status=finished,winner=1`；喂给 `Rules.CheckWinner` 应判黑胜 |
| `v1_legacy_no_version.json` | 无 `version`/`boardState` | 迁移器重建 9×9×3 boardState（3 手）、补默认字段、version→"2.0.0" |
| `v1_legacy_minimal.json` | 只有 `moves` | 迁移全走默认：13/6/gomoku/pvp/player=BLACK、`id` 新生成 |
| `legacy_monolith_v3.json` | 数组 `[v1档, null, v1档]` | 适配器迁移：跳过 null，逐条分片，索引 movesCount 正确 |
| `corrupt_wrong_dims.json` | `boardState.length(2) != layers(3)` | `ValidateSaveData` 返回结构错误码 |
| `corrupt_not_json.txt` | 非法 JSON | JSON.parse 失败路径：槽位置 null，不抛异常 |
| `clipboard_v2_standard.b64.txt` | Base64(JSON)，等价 standard 样本 | `TryParseSaveData` 往返一致；非法 base64→null |
| `slot_index_example.json` | `SlotMeta[]`（槽 0 与快存槽 5） | `GetLatestIndex` 按 timestamp 取 5 |

## 约定

- 所有 JSON 为紧凑格式（`JSON.stringify` 默认分隔符），与源实现字节级风格一致。
- 棋子格值：`0 空 / 1 黑 / 2 白`；`boardState` 访问序 **[z][y][x]**。
- 这些是**手工按 schema 构造**的样本；M4 验收时需补一份从运行中的源项目（浏览器版导出 Base64）
  抓取的**真实存档**作为最终回归样本（R6 的完整闭环）。
