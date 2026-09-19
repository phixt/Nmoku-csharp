using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace Nmoku.Core.Saves;

/// <summary>
/// 自建导出格式 `.nzsave` zip 容器（migration-plan §3.4）：manifest.json + savedata.json，
/// replay.png 为预留位（棋盘快照缩略图，本版不写入）。外圈元数据只进 manifest，不污染 v2.0.0 兼容层。
/// </summary>
public static class NzSaveContainer
{
    public const string Format = "nzsave/1";
    public const string AppId = "nmoku.godot";

    public const string ManifestEntry = "manifest.json";
    public const string SaveDataEntry = "savedata.json";

    /// <summary>打包为 zip 容器字节流（压缩级别对存档这种小 JSON 不敏感，取默认）。</summary>
    public static byte[] Pack(SaveData data, long createdAtUnixMs)
    {
        var manifest = new Manifest
        {
            Format = Format,
            CreatedAt = createdAtUnixMs,
            AppId = AppId,
        };
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(zip, ManifestEntry, JsonSerializer.Serialize(manifest, JsonOptions));
            WriteEntry(zip, SaveDataEntry, SaveData.Serialize(data));
        }
        return buffer.ToArray();
    }

    /// <summary>
    /// 解包 zip 容器；缺 savedata.json、缺 manifest、损坏一律返回 null。
    /// manifest 只做形状检查（容器版本演进时在此分流），字段值暂不参与校验。
    /// </summary>
    public static SaveData? TryUnpack(byte[] raw)
    {
        if (!IsZipMagic(raw))
        {
            return null;
        }
        try
        {
            using var zip = new ZipArchive(new MemoryStream(raw), ZipArchiveMode.Read);
            var manifestEntry = zip.GetEntry(ManifestEntry);
            var dataEntry = zip.GetEntry(SaveDataEntry);
            if (manifestEntry == null || dataEntry == null)
            {
                return null;
            }
            using var manifestDoc = JsonDocument.Parse(ReadEntry(manifestEntry));
            if (manifestDoc.RootElement.ValueKind != JsonValueKind.Object ||
                !manifestDoc.RootElement.TryGetProperty("format", out var fmt) ||
                fmt.GetString() is not { Length: > 0 })
            {
                return null;
            }
            return SaveData.Deserialize(ReadEntry(dataEntry));
        }
        catch (InvalidDataException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>zip magic：PK\x03\x04（本地头）或 PK\x05\x06（空档）。</summary>
    public static bool IsZipMagic(byte[] raw) =>
        raw.Length >= 4 && raw[0] == (byte)'P' && raw[1] == (byte)'K'
        && ((raw[2] == 3 && raw[3] == 4) || (raw[2] == 5 && raw[3] == 6));

    private static void WriteEntry(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name);
        using var stream = entry.Open();
        stream.Write(Encoding.UTF8.GetBytes(content));
    }

    private static string ReadEntry(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>manifest.json 键为 camelCase（migration-plan §3.4 示例：{ "format", "createdAt", "appId" }）。</summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private sealed class Manifest
    {
        public string Format { get; set; } = "";
        public long CreatedAt { get; set; }
        public string AppId { get; set; } = "";
    }
}

/// <summary>
/// 统一读档入口（migration-plan §3.4 嗅探规则）：输入可为裸 JSON（`{` 开头）、
/// `.nzsave` 容器字节（PK magic）或两者的 Base64 包装。返回通过 Normalize 的 v2 档；
/// 无法识别/校验失败一律 null，不抛异常。
/// </summary>
public static class SaveArchive
{
    public static SaveData? TryParse(string input)
    {
        string? json = TryExtractJson(input);
        return json == null ? null : SaveMigrator.Normalize(json);
    }

    /// <summary>嗅探出内层 savedata JSON 文本（不解档、不迁移），供调用方走既有迁移/校验管道。</summary>
    public static string? TryExtractJson(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }
        string trimmed = input.Trim();

        if (trimmed.StartsWith('{'))
        {
            return trimmed;
        }

        byte[] raw = DecodeBase64(trimmed) ?? DecodeLatin1Bytes(trimmed);
        if (raw == null)
        {
            return null;
        }
        if (NzSaveContainer.IsZipMagic(raw))
        {
            var inner = NzSaveContainer.TryUnpack(raw);
            return inner == null ? null : SaveData.Serialize(inner);
        }
        // Base64 解出直接是 JSON 文本（源项目剪贴板格式）
        string text = Encoding.UTF8.GetString(raw);
        return text.TrimStart().StartsWith('{') ? text : null;
    }

    private static byte[]? DecodeBase64(string s)
    {
        try
        {
            return Convert.FromBase64String(s);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>兼容把原始 zip 字节当 Latin-1 字符串粘贴的场景（剪贴板二进制保真路径）。</summary>
    private static byte[]? DecodeLatin1Bytes(string s) =>
        s.Any(c => c > 0xFF) ? null : s.Select(c => (byte)c).ToArray();
}
