using System.Text.Json;
using Godot;
using Nmoku.Core;
using Nmoku.Core.Saves;
using FileAccess = Godot.FileAccess;

namespace Nmoku.Game;

/// <summary>
/// IStorageAdapter 的 Godot 文件实现（04-contracts §3.1 + migration-plan §3.4）：
/// &lt;dir&gt;/index.json（SlotMeta[]）+ &lt;dir&gt;/slot{槽位号}.sav（裸 JSON 文本），默认 user://saves。
/// <para>
/// <b>修复批 ②-1（2026-10-27）· 槽位号为主键</b>：旧实现以「档 Id」为索引主键
/// （<c>Dictionary&lt;string, SlotMeta&gt; _byId</c>），复制/导入同一份档到另一槽会让
/// <b>原槽从索引表消失</b>（磁盘 blob 仍在、index 已无该槽；删除任一槽还会连带删掉共享 blob）。
/// 现改为 <c>Dictionary&lt;int, SlotMeta&gt; _bySlot</c>（槽位号主键）+ 每槽独立 blob
/// （<c>slot{n}.sav</c>），同一 Id 出现在两槽互不干扰；Id 仍随档内容保真（导入语义不变）。
/// </para>
/// <para>
/// <b>旧盘布局迁移</b>：0.1.0-alpha 的 blob 名为 <c>{档 Id}.sav</c>。启动时若某槽的新名
/// blob 不存在而旧名存在，则把旧内容搬到新名并删除旧件（一次性迁移，无孤儿残留）。
/// </para>
/// <para>
/// <b>写盘失败可见（修复批 ②-2）</b>：写入经「临时文件 → 改名」两步，任一步失败即返回 false
/// 且不留半写内容——此前 <c>file?.StoreString</c> 静默吞错，内存索引照常更新、UI 照弹「已快存」
/// （存档假成功）。
/// </para>
/// </summary>
public sealed class FileSystemStorageAdapter : IStorageAdapter
{
    private readonly string _dir;
    private readonly string _indexFile;

    public FileSystemStorageAdapter(string dir = "user://saves")
    {
        _dir = dir;
        _indexFile = $"{dir}/index.json";
    }

    /// <summary>槽位号 → 元数据（主键=槽位号；同槽重复索引项在装载时择一）。</summary>
    private readonly Dictionary<int, SlotMeta> _bySlot = [];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public Task InitAsync()
    {
        _bySlot.Clear();
        DirAccess.MakeDirRecursiveAbsolute(_dir);

        if (!FileAccess.FileExists(_indexFile))
        {
            return Task.CompletedTask;
        }
        string? json = ReadText(_indexFile);
        if (json == null)
        {
            return Task.CompletedTask;
        }
        List<SlotMeta> metas;
        try
        {
            metas = JsonSerializer.Deserialize<SlotMeta[]>(json, JsonOptions)?.ToList() ?? [];
        }
        catch (JsonException)
        {
            // index 损坏：按空库继续（blob 文件仍在，但槽位列表以 index 为准）
            return Task.CompletedTask;
        }

        // 同 Id 可能被多个槽位引用（旧布局允许「复制→填写」产生同 Id 两槽）：
        // 旧件删除前先数清引用数，只删真正无人引用的那个
        var idRefs = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var m in metas)
        {
            if (string.IsNullOrEmpty(m.Id))
            {
                continue;
            }
            idRefs[m.Id] = idRefs.TryGetValue(m.Id, out int n) ? n + 1 : 1;
        }

        bool changed = false;
        foreach (var meta in metas)
        {
            if (string.IsNullOrEmpty(meta.Id) || meta.Index < 0 || meta.Index >= GameConfig.SaveSlotCount)
            {
                changed = true; // 非法索引项被丢弃 → 索引需重写
                continue;
            }
            if (_bySlot.TryGetValue(meta.Index, out var incumbent))
            {
                // 同槽两条：取「blob 真在」者；都在则取 timestamp 新者
                bool candidateHas = SlotBlobExists(meta);
                bool incumbentHas = SlotBlobExists(incumbent);
                bool better = candidateHas && !incumbentHas
                    || (candidateHas && incumbentHas && meta.Timestamp > incumbent.Timestamp);
                if (better)
                {
                    _bySlot[meta.Index] = meta;
                }
                changed = true;
                continue;
            }
            _bySlot[meta.Index] = meta;
            if (MigrateLegacyBlob(meta, idRefs.GetValueOrDefault(meta.Id)))
            {
                changed = true; // 旧布局 blob 已搬到新名 → 索引需重写（顺带清掉旧件）
            }
        }
        if (changed)
        {
            WriteIndex();
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SlotMeta>> GetIndexAsync() =>
        Task.FromResult<IReadOnlyList<SlotMeta>>([.. _bySlot.Values.OrderBy(m => m.Index)]);

    public Task<string?> GetSaveAsync(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= GameConfig.SaveSlotCount)
        {
            return Task.FromResult<string?>(null);
        }
        string path = BlobPath(slotIndex);
        return Task.FromResult(FileAccess.FileExists(path) ? ReadText(path) : null);
    }

    public Task<bool> SetSaveAsync(int slotIndex, string data, SlotMeta meta)
    {
        if (slotIndex < 0 || slotIndex >= GameConfig.SaveSlotCount)
        {
            return Task.FromResult(false);
        }
        // 临时文件 → 改名：任一步失败都不留半写内容（旧 blob 保持原样）
        if (!WriteBlob(slotIndex, data))
        {
            return Task.FromResult(false);
        }
        _bySlot[slotIndex] = meta with { Index = slotIndex };
        if (!WriteIndex())
        {
            return Task.FromResult(false); // 索引写失败 → 报失败（不假成功）
        }
        return Task.FromResult(true);
    }

    public Task<bool> DeleteSaveAsync(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= GameConfig.SaveSlotCount)
        {
            return Task.FromResult(false);
        }
        string path = BlobPath(slotIndex);
        if (FileAccess.FileExists(path))
        {
            DirAccess.RemoveAbsolute(path); // 残留即孤儿（下次写同槽覆盖），不阻塞删除
        }
        _bySlot.Remove(slotIndex);
        // 索引是槽位存在与否的权威：索引写失败即删除未完成
        return Task.FromResult(WriteIndex());
    }

    /// <summary>写索引：临时文件 → 改名；失败返回 false（索引文件保持旧内容，不会半写）。</summary>
    private bool WriteIndex()
    {
        var metas = _bySlot.Values.OrderBy(m => m.Index).ToArray();
        string tmp = $"{_indexFile}.tmp";
        if (!WriteText(tmp, JsonSerializer.Serialize(metas, JsonOptions)))
        {
            DeleteFile(tmp);
            return false;
        }
        if (!ReplaceFile(tmp, _indexFile))
        {
            DeleteFile(tmp);
            return false;
        }
        return true;
    }

    /// <summary>写槽位 blob：临时文件 → 改名；失败返回 false。</summary>
    private bool WriteBlob(int slotIndex, string content)
    {
        string path = BlobPath(slotIndex);
        string tmp = $"{path}.tmp";
        if (!WriteText(tmp, content))
        {
            DeleteFile(tmp);
            return false;
        }
        if (!ReplaceFile(tmp, path))
        {
            DeleteFile(tmp);
            return false;
        }
        return true;
    }

    /// <summary>旧布局 blob（{档 Id}.sav）→ 新布局（slot{n}.sav）；返回是否发生了迁移。</summary>
    private bool MigrateLegacyBlob(SlotMeta meta, int idRefs)
    {
        string path = BlobPath(meta.Index);
        if (FileAccess.FileExists(path))
        {
            return false; // 已有新件，无需迁移
        }
        string legacy = LegacyBlobPath(meta.Id);
        if (!FileAccess.FileExists(legacy))
        {
            return false; // 旧件也不在：该槽的 blob 缺失（索引有项、内容丢失）→ 装载时跳过
        }
        string? text = ReadText(legacy);
        if (text == null)
        {
            return false;
        }
        if (!WriteBlob(meta.Index, text))
        {
            return false;
        }
        if (idRefs <= 1)
        {
            DeleteFile(legacy); // 无其他槽引用该 Id → 旧件删除（不留孤儿）
        }
        return true;
    }

    /// <summary>该槽是否有可用 blob（新名或旧名均可——旧名会在装载时迁移）。</summary>
    private bool SlotBlobExists(SlotMeta meta) =>
        FileAccess.FileExists(BlobPath(meta.Index))
        || FileAccess.FileExists(LegacyBlobPath(meta.Id));

    /// <summary>
    /// 临时文件改名覆盖目标。改名优先（POSIX 上原子）；Windows 的 rename 不覆盖既有目标，
    /// 此时先删旧件再改名，仍失败则退回复制覆盖。
    /// </summary>
    private static bool ReplaceFile(string tmp, string target)
    {
        if (DirAccess.RenameAbsolute(tmp, target) == Error.Ok)
        {
            return true;
        }
        if (FileAccess.FileExists(target))
        {
            DirAccess.RemoveAbsolute(target);
            if (DirAccess.RenameAbsolute(tmp, target) == Error.Ok)
            {
                return true;
            }
        }
        // 兜底：复制覆盖（跨卷 / 文件系统不支持改名）
        if (DirAccess.CopyAbsolute(tmp, target) == Error.Ok)
        {
            DeleteFile(tmp);
            return true;
        }
        return false;
    }

    private static void DeleteFile(string path)
    {
        if (FileAccess.FileExists(path))
        {
            DirAccess.RemoveAbsolute(path);
        }
    }

    /// <summary>新布局 blob 路径：槽位号为主键（每槽一件，互不共享）。</summary>
    private string BlobPath(int slotIndex) => $"{_dir}/slot{slotIndex}.sav";

    /// <summary>旧布局 blob 路径（0.1.0-alpha：{档 Id}.sav），仅用于一次性迁移。</summary>
    private string LegacyBlobPath(string id)
    {
        var sb = new System.Text.StringBuilder();
        foreach (char c in id)
        {
            sb.Append(char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' ? c : '_');
        }
        return $"{_dir}/{sb}.sav";
    }

    private static string? ReadText(string path)
    {
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        return file == null ? null : file.GetAsText();
    }

    /// <summary>写文本；返回是否成功（打开失败 / 写入报错 → false，不再静默吞错）。</summary>
    private static bool WriteText(string path, string content)
    {
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
        if (file == null)
        {
            return false;
        }
        file.StoreString(content);
        return file.GetError() == Error.Ok;
    }
}
