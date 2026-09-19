using System.Text.Json;
using Godot;
using Nmoku.Core;
using Nmoku.Core.Saves;
using FileAccess = Godot.FileAccess;

namespace Nmoku.Game;

/// <summary>
/// IStorageAdapter 的 Godot 文件实现（04-contracts §3.1 + migration-plan §3.4）：
/// user://saves/index.json（SlotMeta[]）+ user://saves/&lt;id&gt;.sav（裸 JSON 文本）。
/// 槽位覆盖换 id 时删除旧 blob（不留孤儿文件）；DeleteSave 幂等；损坏 index 按空库处理。
/// 所有方法内部同步执行（6 个小文件，主线程开销可忽略），以 Task 签名满足 Core 门面。
/// </summary>
public sealed class FileSystemStorageAdapter : IStorageAdapter
{
    private const string Dir = "user://saves";
    private const string IndexFile = "user://saves/index.json";

    private readonly Dictionary<string, SlotMeta> _byId = [];
    private readonly Dictionary<int, string> _slotId = [];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public Task InitAsync()
    {
        _byId.Clear();
        _slotId.Clear();
        DirAccess.MakeDirRecursiveAbsolute(Dir);

        if (!FileAccess.FileExists(IndexFile))
        {
            return Task.CompletedTask;
        }
        string? json = ReadText(IndexFile);
        if (json == null)
        {
            return Task.CompletedTask;
        }
        try
        {
            var metas = JsonSerializer.Deserialize<SlotMeta[]>(json, JsonOptions) ?? [];
            foreach (var meta in metas)
            {
                if (meta.Id.Length == 0 || meta.Index < 0 || meta.Index >= GameConfig.SaveSlotCount)
                {
                    continue;
                }
                _byId[meta.Id] = meta;
                _slotId[meta.Index] = meta.Id;
            }
        }
        catch (JsonException)
        {
            // index 损坏：按空库继续（blob 文件仍在，但槽位列表以 index 为准）
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SlotMeta>> GetIndexAsync() =>
        Task.FromResult<IReadOnlyList<SlotMeta>>([.. _byId.Values]);

    public Task<string?> GetSaveAsync(string id)
    {
        string path = BlobPath(id);
        return Task.FromResult(FileAccess.FileExists(path) ? ReadText(path) : null);
    }

    public Task SetSaveAsync(string id, string data, SlotMeta meta)
    {
        // 同槽位换 id：先删旧 blob，防止反复快存积累孤儿文件
        if (_slotId.TryGetValue(meta.Index, out string? oldId) && oldId != id)
        {
            DeleteBlob(oldId);
            _byId.Remove(oldId);
        }
        WriteText(BlobPath(id), data);
        _byId[id] = meta;
        _slotId[meta.Index] = id;
        WriteIndex();
        return Task.CompletedTask;
    }

    public Task DeleteSaveAsync(string id)
    {
        DeleteBlob(id);
        if (_byId.Remove(id, out var meta))
        {
            if (_slotId.TryGetValue(meta.Index, out string? occupant) && occupant == id)
            {
                _slotId.Remove(meta.Index);
            }
        }
        WriteIndex();
        return Task.CompletedTask;
    }

    private void WriteIndex()
    {
        var metas = _byId.Values.OrderBy(m => m.Index).ToArray();
        WriteText(IndexFile, JsonSerializer.Serialize(metas, JsonOptions));
    }

    private void DeleteBlob(string id)
    {
        string path = BlobPath(id);
        if (FileAccess.FileExists(path))
        {
            DirAccess.RemoveAbsolute(path);
        }
    }

    /// <summary>id 通常为 UUID；仍做白名单净化，拒绝路径分隔符进入文件名。</summary>
    private static string BlobPath(string id)
    {
        var sb = new System.Text.StringBuilder();
        foreach (char c in id)
        {
            sb.Append(char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' ? c : '_');
        }
        return $"{Dir}/{sb}.sav";
    }

    private static string? ReadText(string path)
    {
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        return file == null ? null : file.GetAsText();
    }

    private static void WriteText(string path, string content)
    {
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
        file?.StoreString(content);
    }
}
