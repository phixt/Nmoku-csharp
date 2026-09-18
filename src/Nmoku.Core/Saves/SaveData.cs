using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nmoku.Core.Saves;

/// <summary>
/// v2.0.0 存档 schema（04-contracts §3.3 + 01-core §6.1），字段与旧项目逐一对齐，
/// 保证双向可读。字符串字段保持 string 以宽松容纳未知值（损坏档不得抛异常）。
/// </summary>
public sealed class SaveData
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("version")] public string Version { get; set; } = GameConfig.SaveVersion;
    [JsonPropertyName("timestamp")] public long Timestamp { get; set; }
    [JsonPropertyName("boardState")] public int[][][] BoardState { get; set; } = [];
    [JsonPropertyName("moves")] public List<SaveMove> Moves { get; set; } = [];
    /// <summary>"gomoku" | "renju" | "swap2"。</summary>
    [JsonPropertyName("rules")] public string Rules { get; set; } = "gomoku";
    /// <summary>"pvp" | "pve"。</summary>
    [JsonPropertyName("gameMode")] public string GameMode { get; set; } = "pvp";
    /// <summary>"easy" | "medium" | "hard"，可缺失。</summary>
    [JsonPropertyName("aiDifficulty")] public string? AiDifficulty { get; set; }
    /// <summary>"playing" | "finished" | "paused"。</summary>
    [JsonPropertyName("status")] public string Status { get; set; } = "playing";
    /// <summary>0=未分，1=黑，2=白。</summary>
    [JsonPropertyName("winner")] public int Winner { get; set; }
    [JsonPropertyName("currentPlayer")] public int CurrentPlayer { get; set; } = 1;
    /// <summary>已废弃：读入即弃，相机由 boardSize/layers/layerSpacing 重算（04 §3.3 兼容注）。</summary>
    [JsonPropertyName("focusZ")] public int FocusZ { get; set; }
    [JsonPropertyName("isDarkTheme")] public bool IsDarkTheme { get; set; }
    [JsonPropertyName("playerColor")] public int PlayerColor { get; set; } = 1;
    [JsonPropertyName("boardSize")] public int BoardSize { get; set; } = GameConfig.DefaultBoardSize;
    [JsonPropertyName("layers")] public int Layers { get; set; } = GameConfig.DefaultLayerCount;
    [JsonPropertyName("layerSpacing")] public double LayerSpacing { get; set; } = GameConfig.DefaultLayerSpacing;

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Serialize(SaveData data) =>
        JsonSerializer.Serialize(data, JsonOptions);

    public static SaveData? Deserialize(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<SaveData>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Base64(JSON)，与旧版 btoa(JSON.stringify) 的剪贴板导出互通（UTF-8 超集路径）。</summary>
    public static string SerializeToBase64(SaveData data) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(Serialize(data)));

    /// <summary>解析剪贴板 Base64：非法 base64 / 非 JSON / 缺关键字段一律返回 null（SaveManager.tryParseSaveData）。</summary>
    public static SaveData? TryParseFromBase64(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }
        byte[] raw;
        try
        {
            raw = Convert.FromBase64String(input.Trim());
        }
        catch (FormatException)
        {
            return null;
        }

        JsonElement root;
        try
        {
            root = JsonDocument.Parse(Encoding.UTF8.GetString(raw)).RootElement;
        }
        catch (JsonException)
        {
            return null;
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        if (!HasTruthyString(root, "version") ||
            !HasArray(root, "moves") ||
            !HasPositiveNumber(root, "boardSize") ||
            !HasPositiveNumber(root, "layers"))
        {
            return null;
        }

        return Deserialize(Encoding.UTF8.GetString(raw));
    }

    internal static bool HasTruthyString(JsonElement root, string prop) =>
        root.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
        && v.GetString() is { Length: > 0 };

    internal static bool HasArray(JsonElement root, string prop) =>
        root.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Array;

    internal static bool HasPositiveNumber(JsonElement root, string prop) =>
        root.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number
        && v.TryGetInt32(out int n) && n > 0;
}

/// <summary>存档内的落子记录（04-contracts §3.3 MoveRecord）。</summary>
public sealed class SaveMove
{
    [JsonPropertyName("x")] public int X { get; set; }
    [JsonPropertyName("y")] public int Y { get; set; }
    [JsonPropertyName("z")] public int Z { get; set; }
    [JsonPropertyName("player")] public int Player { get; set; }
    [JsonPropertyName("timestamp")] public long? Timestamp { get; set; }
    [JsonPropertyName("evaluation")] public double? Evaluation { get; set; }

    public Move ToMove() => new(X, Y, Z, (PlayerColor)Player, Timestamp, Evaluation);
}
