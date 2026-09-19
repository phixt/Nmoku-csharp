using System.Text.Json;

namespace Nmoku.Core.Saves;

/// <summary>
/// 存档结构校验（01-core §6.2 / SaveManager.validateSaveData）：
/// 合法返回 null，非法返回错误码。纯静态函数，不抛异常。
/// </summary>
public static class SaveValidator
{
    public const string InvalidStructure = "SAVE_INVALID_STRUCTURE";

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
            }
        }

        return null;
    }

    /// <summary>校验已反序列化的对象（维度须与自身声明的 boardSize/layers 一致）。</summary>
    public static string? Validate(SaveData data)
    {
        if (string.IsNullOrEmpty(data.Version)
            || data.BoardState.Length != data.Layers
            || data.BoardSize <= 0 || data.Layers <= 0)
        {
            return InvalidStructure;
        }
        foreach (var layer in data.BoardState)
        {
            if (layer.Length != data.BoardSize)
            {
                return InvalidStructure;
            }
            foreach (var row in layer)
            {
                if (row.Length != data.BoardSize)
                {
                    return InvalidStructure;
                }
            }
        }
        return null;
    }

    private static bool PositiveInt(JsonElement root, string prop) =>
        root.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number
        && v.TryGetInt32(out int n) && n > 0;
}
