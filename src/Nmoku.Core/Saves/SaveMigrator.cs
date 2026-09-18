using System.Text.Json;

namespace Nmoku.Core.Saves;

/// <summary>
/// v1 旧档迁移（SaveManager.migrateData）：无 version 或 version &lt; "2.0.0" 时，
/// 由 moves 重建 boardState，缺省字段按旧项目默认值补齐。
/// 另含旧版单体存储（gomoku_saves_v3 数组）的分片拆分。
/// </summary>
public static class SaveMigrator
{
    /// <summary>解析任意版本存档 JSON → v2.0.0 SaveData（必要时迁移）。非法 JSON 返回 null。</summary>
    public static SaveData? Normalize(string json)
    {
        JsonElement root;
        try
        {
            root = JsonDocument.Parse(json).RootElement;
        }
        catch (JsonException)
        {
            return null;
        }
        return root.ValueKind == JsonValueKind.Object ? Normalize(root) : null;
    }

    public static SaveData Normalize(JsonElement root)
    {
        string? version = root.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

        bool needsMigration = string.IsNullOrEmpty(version)
            || string.CompareOrdinal(version, GameConfig.SaveVersion) < 0;

        if (!needsMigration)
        {
            return root.Deserialize<SaveData>(SaveData.JsonOptions)!;
        }

        int boardSize = GetInt(root, "boardSize", GameConfig.DefaultBoardSize);
        int layers = GetInt(root, "layers", GameConfig.DefaultLayerCount);

        var moves = new List<SaveMove>();
        int[][][] boardState = NewBoard(layers, boardSize);
        if (root.TryGetProperty("moves", out var movesEl) && movesEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var m in movesEl.EnumerateArray())
            {
                var move = m.Deserialize<SaveMove>(SaveData.JsonOptions)!;
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
            Timestamp = root.TryGetProperty("timestamp", out var ts) && ts.ValueKind == JsonValueKind.Number
                ? ts.GetInt64()
                : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
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
        JsonElement root;
        try
        {
            root = JsonDocument.Parse(json).RootElement;
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
                result.Add((index, Normalize(item)));
            }
            index++;
        }
        return result;
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
