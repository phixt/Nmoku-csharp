using System.Text.Json;

namespace Nmoku.Core.Saves;

/// <summary>
/// v1 旧档迁移（SaveManager.migrateData）：无 version 或 version &lt; "2.0.0" 时，
/// 由 moves 重建 boardState，缺省字段按旧项目默认值补齐。
/// 另含旧版单体存储（gomoku_saves_v3 数组）的分片拆分。
/// </summary>
public static class SaveMigrator
{
    /// <summary>解析任意版本存档 JSON → v2.0.0 SaveData（必要时迁移）。
    /// 非法 JSON、结构畸形（字段类型不符）、尺寸越界一律返回 null，**不抛异常**——
    /// 本方法是统一读档入口（剪贴板 / 文件 / 槽位）的唯一闸门，契约见 SaveArchive。</summary>
    public static SaveData? Normalize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }
        try
        {
            using var doc = JsonDocument.Parse(json);
            return Normalize(doc.RootElement);
        }
        catch (JsonException)
        {
            return null; // 非法 JSON 文本
        }
        catch (FormatException)
        {
            return null; // 数值字段非整数 / 超范围
        }
        catch (InvalidOperationException)
        {
            return null; // JsonElement 类型与预期不符
        }
        catch (ArgumentException)
        {
            return null; // JsonDocument.Parse 参数非法
        }
    }

    /// <summary>解析 v1/v2 存档 JSON 元素 → v2.0.0 SaveData（必要时迁移）。
    /// 结构畸形（字段类型不符、moves 元素非对象、尺寸越界）一律返回 null，**不抛异常**。</summary>
    public static SaveData? Normalize(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        // 尺寸闸门前置到版本分支之前：v1 迁移按声明值分配盘面（NewBoard）、v2 直通档下游按
        // BoardSize/Layers 索引，两条路径同一份不可信输入 → 越界值先判废，
        // 否则百字节输入即可换来 GB 级分配
        int boardSize = GetInt(root, "boardSize", GameConfig.DefaultBoardSize);
        int layers = GetInt(root, "layers", GameConfig.DefaultLayerCount);
        if (boardSize < 1 || boardSize > GameConfig.MaxBoardSize
            || layers < 1 || layers > GameConfig.MaxLayers)
        {
            return null;
        }

        string? version = root.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

        bool needsMigration = string.IsNullOrEmpty(version)
            || string.CompareOrdinal(version, GameConfig.SaveVersion) < 0;

        if (!needsMigration)
        {
            try
            {
                return root.Deserialize<SaveData>(SaveData.JsonOptions);
            }
            catch (JsonException)
            {
                return null; // v2 直通档结构畸形（字段类型不符）
            }
        }

        long? timestamp = TryReadTimestamp(root);
        if (timestamp == null)
        {
            return null; // timestamp 数值非整数 / 超范围 → 结构畸形
        }

        var moves = new List<SaveMove>();
        int[][][] boardState = NewBoard(layers, boardSize);
        if (root.TryGetProperty("moves", out var movesEl) && movesEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var m in movesEl.EnumerateArray())
            {
                SaveMove? move;
                try
                {
                    move = m.Deserialize<SaveMove>(SaveData.JsonOptions);
                }
                catch (JsonException)
                {
                    return null; // 元素结构畸形：意图无法确定 → 整档判废（不静默丢手）
                }
                if (move == null)
                {
                    return null;
                }
                moves.Add(move);
                if (move.X >= 0 && move.X < boardSize &&
                    move.Y >= 0 && move.Y < boardSize &&
                    move.Z >= 0 && move.Z < layers)
                {
                    boardState[move.Z][move.Y][move.X] = move.Player;
                }
            }
        }

        return new SaveData
        {
            Id = GetStringValue(root, "id") ?? Guid.NewGuid().ToString(),
            Version = GameConfig.SaveVersion,
            Timestamp = timestamp.Value,
            BoardState = boardState,
            Moves = moves,
            Rules = GetStringValue(root, "rules") ?? "gomoku",
            GameMode = GetStringValue(root, "gameMode") ?? "pvp",
            AiDifficulty = GetStringValue(root, "aiDifficulty"),
            Status = GetStringValue(root, "status") ?? "playing",
            Winner = GetInt(root, "winner", 0),
            CurrentPlayer = GetInt(root, "currentPlayer", 1),
            FocusZ = GetInt(root, "focusZ", 0),
            IsDarkTheme = root.TryGetProperty("isDarkTheme", out var dk) && dk.ValueKind == JsonValueKind.True,
            PlayerColor = GetInt(root, "playerColor", 1),
            BoardSize = boardSize,
            Layers = layers,
            LayerSpacing = root.TryGetProperty("layerSpacing", out var sp) && sp.ValueKind == JsonValueKind.Number
                ? sp.GetDouble()
                : GameConfig.DefaultLayerSpacing,
        };
    }

    /// <summary>
    /// 拆分旧单体存储数组（LocalStorageAdapter.tryMigrate）：null/缺 id 的条目跳过，
    /// 其余按数组下标作为槽位索引。
    /// </summary>
    public static IReadOnlyList<(int Index, SaveData Save)> SplitMonolith(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }
        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(json);
            root = doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return [];
        }
        if (root.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<(int, SaveData)>();
        int index = 0;
        foreach (var item in root.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object && SaveData.HasTruthyString(item, "id"))
            {
                var normalized = Normalize(item);
                if (normalized != null)
                {
                    result.Add((index, normalized));
                }
            }
            index++;
        }
        return result;
    }

    /// <summary>timestamp：缺省/非数值 → 补当前时刻（v1 旧档兼容）；
    /// 数值但非整数或超范围 → null（结构畸形，由调用方整档判废）。</summary>
    private static long? TryReadTimestamp(JsonElement root)
    {
        if (!root.TryGetProperty("timestamp", out var ts) || ts.ValueKind != JsonValueKind.Number)
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }
        return ts.TryGetInt64(out long v) ? v : null;
    }

    private static int[][][] NewBoard(int layers, int boardSize)
    {
        var board = new int[layers][][];
        for (int z = 0; z < layers; z++)
        {
            board[z] = new int[boardSize][];
            for (int y = 0; y < boardSize; y++)
            {
                board[z][y] = new int[boardSize];
            }
        }
        return board;
    }

    private static int GetInt(JsonElement root, string prop, int fallback) =>
        root.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int n)
            ? n
            : fallback;

    private static string? GetStringValue(JsonElement root, string prop) =>
        root.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
