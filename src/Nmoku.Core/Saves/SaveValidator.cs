using System.Text.Json;

namespace Nmoku.Core.Saves;

/// <summary>
/// 存档结构校验（01-core §6.2 / SaveManager.validateSaveData）：
/// 合法返回 null，非法返回错误码。纯静态函数，不抛异常。
/// <para>
/// <b>修复批 ②-8（2026-10-27）· 值域与规则白名单</b>：此前只校形状（维度/行宽），
/// 不校<b>取值域</b>——越界手（x/y/z 超出盘面）在复盘重放时「静默消失」，
/// player 越界值经 <c>(PlayerColor)Player</c> 强转成非法棋色，
/// boardState 里的非法格值经 <c>(CellState)v</c> 强转成僵尸格（该格永久封锁、<c>IsFull()</c> 永假 → 判和条件失效）。
/// 另加<b>规则白名单</b>：renju 引擎未实装（<c>RuleEngineFactory</c> 抛 NotSupportedException），
/// 此类档此前能通过导入校验、直到读档才炸（且炸在棋盘已被替换之后 → 半加载）。
/// </para>
/// </summary>
public static class SaveValidator
{
    public const string InvalidStructure = "SAVE_INVALID_STRUCTURE";

    /// <summary>
    /// 偶数阶棋盘档不合法（00-domain 尺寸奇数约束；人类拍板 2026-09-26：存档格式不与外部游戏互通 +
    /// 沙盒/预设无偶数阶入口，全链路封闭偶数阶，中心标注方案随之作废）。
    /// </summary>
    public const string InvalidEvenBoardSize = "SAVE_INVALID_EVEN_BOARD_SIZE";

    /// <summary>落子记录越界或执子方越界（坐标须在盘面内、player 须为 1/2）。</summary>
    public const string InvalidMove = "SAVE_INVALID_MOVE";

    /// <summary>盘面格值越界（只允许 0 空 / 1 黑 / 2 白）。</summary>
    public const string InvalidCellValue = "SAVE_INVALID_CELL_VALUE";

    /// <summary>规则集不受支持（renju 引擎未实装；仅 gomoku / swap2 可载入）。</summary>
    public const string UnsupportedRules = "SAVE_UNSUPPORTED_RULES";

    public static string? Validate(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return InvalidStructure;
        }
        if (!SaveData.HasTruthyString(root, "version")
            || !SaveData.HasArray(root, "boardState")
            || !SaveData.HasArray(root, "moves")
            || !PositiveInt(root, "boardSize")
            || !PositiveInt(root, "layers"))
        {
            return InvalidStructure;
        }

        int boardSize = root.GetProperty("boardSize").GetInt32();
        if (boardSize % 2 == 0)
        {
            return InvalidEvenBoardSize;
        }
        int layers = root.GetProperty("layers").GetInt32();
        var boardState = root.GetProperty("boardState");
        if (boardState.GetArrayLength() != layers)
        {
            return InvalidStructure;
        }

        foreach (var layer in boardState.EnumerateArray())
        {
            if (layer.ValueKind != JsonValueKind.Array || layer.GetArrayLength() != boardSize)
            {
                return InvalidStructure;
            }
            // 行宽必须逐行检查：只查首行会放行 y>0 的错配行
            foreach (var row in layer.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Array || row.GetArrayLength() != boardSize)
                {
                    return InvalidStructure;
                }
                // 格值域：0 空 / 1 黑 / 2 白（越界值经 (CellState) 强转会变成永久封锁的僵尸格）
                foreach (var cell in row.EnumerateArray())
                {
                    if (cell.ValueKind != JsonValueKind.Number || !cell.TryGetInt32(out int v) || v is not (0 or 1 or 2))
                    {
                        return InvalidCellValue;
                    }
                }
            }
        }

        // 规则白名单：renju 引擎未实装，此档永不可载入 → 在校验层就毙掉（不留半加载地雷）
        if (root.TryGetProperty("rules", out var rulesEl))
        {
            if (rulesEl.ValueKind != JsonValueKind.String)
            {
                return InvalidStructure;
            }
            if (!IsSupportedRules(rulesEl.GetString()))
            {
                return UnsupportedRules;
            }
        }

        // 落子记录：坐标须在盘面内、执子方须为 1/2（越界手在复盘重放时会「静默消失」）
        foreach (var move in root.GetProperty("moves").EnumerateArray())
        {
            if (move.ValueKind != JsonValueKind.Object)
            {
                return InvalidMove;
            }
            if (!TryGetInt(move, "x", out int x) || !TryGetInt(move, "y", out int y)
                || !TryGetInt(move, "z", out int z) || !TryGetInt(move, "player", out int player))
            {
                return InvalidMove;
            }
            if (x < 0 || x >= boardSize || y < 0 || y >= boardSize || z < 0 || z >= layers)
            {
                return InvalidMove;
            }
            if (player is not (1 or 2))
            {
                return InvalidMove;
            }
        }

        return null;
    }

    /// <summary>校验已反序列化的对象（维度须与自身声明的 boardSize/layers 一致）。</summary>
    public static string? Validate(SaveData data)
    {
        if (data == null
            || string.IsNullOrEmpty(data.Version)
            || data.BoardState == null || data.Moves == null
            || data.BoardState.Length != data.Layers
            || data.BoardSize <= 0 || data.Layers <= 0)
        {
            return InvalidStructure;
        }
        if (data.BoardSize % 2 == 0)
        {
            return InvalidEvenBoardSize;
        }
        if (!IsSupportedRules(data.Rules))
        {
            return UnsupportedRules;
        }
        foreach (var layer in data.BoardState)
        {
            if (layer == null || layer.Length != data.BoardSize)
            {
                return InvalidStructure;
            }
            foreach (var row in layer)
            {
                if (row == null || row.Length != data.BoardSize)
                {
                    return InvalidStructure;
                }
                foreach (int v in row)
                {
                    if (v is not (0 or 1 or 2))
                    {
                        return InvalidCellValue;
                    }
                }
            }
        }
        foreach (var move in data.Moves)
        {
            if (move == null)
            {
                return InvalidMove;
            }
            if (move.X < 0 || move.X >= data.BoardSize
                || move.Y < 0 || move.Y >= data.BoardSize
                || move.Z < 0 || move.Z >= data.Layers
                || move.Player is not (1 or 2))
            {
                return InvalidMove;
            }
        }
        return null;
    }

    /// <summary>可载入的规则集：gomoku / swap2（空 = 缺省 gomoku）。renju 引擎未实装，不受理。</summary>
    public static bool IsSupportedRules(string? rules) =>
        string.IsNullOrEmpty(rules)
        || rules.Equals("gomoku", StringComparison.OrdinalIgnoreCase)
        || rules.Equals("swap2", StringComparison.OrdinalIgnoreCase);

    private static bool PositiveInt(JsonElement root, string prop) =>
        root.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number
        && v.TryGetInt32(out int n) && n > 0;

    private static bool TryGetInt(JsonElement obj, string prop, out int value)
    {
        if (obj.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number
            && v.TryGetInt32(out value))
        {
            return true;
        }
        value = 0;
        return false;
    }
}
