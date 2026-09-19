namespace Nmoku.Core.Saves;

/// <summary>
/// 槽位存档门面（04-contracts §3.2 + 源 SaveManager 行为）：内存槽位缓存 +
/// IStorageAdapter 读写，越界槽位静默忽略，损坏档跳过不抛异常。
/// </summary>
public sealed class SaveManager
{
    private readonly IStorageAdapter _storage;
    private readonly SaveData?[] _slots = new SaveData?[GameConfig.SaveSlotCount];
    private readonly string?[] _slotIds = new string?[GameConfig.SaveSlotCount];
    private bool _ready;

    public SaveManager(IStorageAdapter storage) => _storage = storage;

    public bool Ready => _ready;

    public async Task InitAsync()
    {
        await _storage.InitAsync();
        await LoadFromStorageAsync();
        _ready = true;
    }

    public IReadOnlyList<SaveData?> GetSlots() => _slots;

    public bool IsSlotEmpty(int index) =>
        index < 0 || index >= GameConfig.SaveSlotCount || _slots[index] == null;

    public async Task SaveAsync(int index, SaveData data)
    {
        if (index < 0 || index >= GameConfig.SaveSlotCount)
        {
            return; // 越界槽位静默忽略（4.3 测试点）
        }
        _slots[index] = data;
        _slotIds[index] = data.Id;
        var meta = new SlotMeta
        {
            Index = index,
            Id = data.Id,
            Timestamp = data.Timestamp,
            BoardSize = data.BoardSize,
            Layers = data.Layers,
            MovesCount = data.Moves.Count,
        };
        await _storage.SetSaveAsync(data.Id, SaveData.Serialize(data), meta);
        EventBus.EmitSaveUpdated(_slots);
    }

    public SaveData? Load(int index) =>
        index < 0 || index >= GameConfig.SaveSlotCount ? null : _slots[index];

    public async Task DeleteAsync(int index)
    {
        if (index < 0 || index >= GameConfig.SaveSlotCount)
        {
            return;
        }
        string? id = _slotIds[index];
        _slots[index] = null;
        _slotIds[index] = null;
        if (id != null)
        {
            await _storage.DeleteSaveAsync(id);
        }
        EventBus.EmitSaveUpdated(_slots);
    }

    public Task QuickSaveAsync(SaveData data) => SaveAsync(GameConfig.QuickSaveIndex, data);

    public SaveData? QuickLoad() => Load(GameConfig.QuickSaveIndex);

    /// <summary>按 timestamp 返回最新存档的槽位下标；无档返回 -1。</summary>
    public int GetLatestIndex()
    {
        int latest = -1;
        long latestTs = 0;
        for (int i = 0; i < GameConfig.SaveSlotCount; i++)
        {
            if (_slots[i] is { } d && d.Timestamp > latestTs)
            {
                latestTs = d.Timestamp;
                latest = i;
            }
        }
        return latest;
    }

    private async Task LoadFromStorageAsync()
    {
        try
        {
            var index = await _storage.GetIndexAsync();
            foreach (var meta in index)
            {
                if (meta.Index < 0 || meta.Index >= GameConfig.SaveSlotCount)
                {
                    continue;
                }
                string? raw = await _storage.GetSaveAsync(meta.Id);
                if (raw == null)
                {
                    continue;
                }
                var parsed = SaveMigrator.Normalize(raw); // 内部吞 JSON 异常时返回 null
                if (parsed == null || SaveValidator.Validate(parsed) != null)
                {
                    continue; // 损坏存档（含 v2 直通档的维度错配）：跳过、槽位保持 null
                }
                _slots[meta.Index] = parsed;
                _slotIds[meta.Index] = parsed.Id;
            }
        }
        catch
        {
            Array.Clear(_slots);
            Array.Clear(_slotIds);
        }
    }
}
