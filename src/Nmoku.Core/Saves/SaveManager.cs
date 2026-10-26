namespace Nmoku.Core.Saves;

/// <summary>
/// 槽位存档门面（04-contracts §3.2 + 源 SaveManager 行为）：内存槽位缓存 +
/// IStorageAdapter 读写，越界槽位静默忽略，损坏档跳过不抛异常。
/// <para>
/// <b>修复批 ②（2026-10-27）</b>：槽位号为主键（复制/导入不再抹掉原槽）、
/// 写盘失败可见（返回 false，不假成功）、逐槽容错（一个坏档只丢自己）、
/// 外圈 winLength 范围校验（越界视为缺失 → 回默认 5）。
/// </para>
/// </summary>
public sealed class SaveManager
{
    private readonly IStorageAdapter _storage;
    private readonly SaveData?[] _slots = new SaveData?[GameConfig.SaveSlotCount];
    private readonly string?[] _slotIds = new string?[GameConfig.SaveSlotCount];
    private readonly int?[] _slotWinLengths = new int?[GameConfig.SaveSlotCount];
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

    /// <summary>
    /// 落槽（winLength 为外圈元数据 B14：v2.0.0 schema 无此字段，随 SlotMeta/index.json 旁路保存；
    /// null = 默认连珠数，等价旧档）。
    /// <para>
    /// 顺序纪律（修复批 ②-2）：<b>先落盘、后更新内存</b>——写盘失败时内存不显示「已存」
    /// （此前先写内存再 await 磁盘，写失败即「内存显示已存、磁盘无档」的假成功）。
    /// 落盘前先过 <see cref="SaveValidator"/>（写前校验，脏档不进槽）。
    /// 返回 false = 越界槽位 / 档非法 / 写盘失败（调用方据此提示失败）。
    /// </para>
    /// </summary>
    public async Task<bool> SaveAsync(int index, SaveData data, int? winLength = null)
    {
        if (index < 0 || index >= GameConfig.SaveSlotCount)
        {
            return false; // 越界槽位静默忽略（4.3 测试点）
        }
        if (data == null || SaveValidator.Validate(data) != null)
        {
            return false; // 写前校验：结构非法的档不进槽（此前脏档照落盘）
        }
        int? effectiveWinLength = NormalizeWinLength(winLength);
        var meta = new SlotMeta
        {
            Index = index,
            Id = data.Id,
            Timestamp = data.Timestamp,
            BoardSize = data.BoardSize,
            Layers = data.Layers,
            MovesCount = data.Moves.Count,
            WinLength = effectiveWinLength,
        };
        bool written = await _storage.SetSaveAsync(index, SaveData.Serialize(data), meta);
        if (!written)
        {
            return false; // 写盘失败：不更新内存、不发事件（杜绝假成功）
        }
        _slots[index] = data;
        _slotIds[index] = data.Id;
        _slotWinLengths[index] = effectiveWinLength;
        EventBus.EmitSaveUpdated(_slots);
        return true;
    }

    public SaveData? Load(int index) =>
        index < 0 || index >= GameConfig.SaveSlotCount ? null : _slots[index];

    /// <summary>槽位的外圈连珠数元数据（B14）；空槽/旧档/越界返回 null（即默认 5）。</summary>
    public int? GetSlotWinLength(int index) =>
        index < 0 || index >= GameConfig.SaveSlotCount ? null : _slotWinLengths[index];

    public async Task<bool> DeleteAsync(int index)
    {
        if (index < 0 || index >= GameConfig.SaveSlotCount)
        {
            return false;
        }
        bool deleted = await _storage.DeleteSaveAsync(index);
        if (!deleted)
        {
            return false; // 磁盘删除失败：槽位保留（不制造「界面空、磁盘有」的幽灵档）
        }
        _slots[index] = null;
        _slotIds[index] = null;
        _slotWinLengths[index] = null;
        EventBus.EmitSaveUpdated(_slots);
        return true;
    }

    public Task<bool> QuickSaveAsync(SaveData data, int? winLength = null) =>
        SaveAsync(GameConfig.QuickSaveIndex, data, winLength);

    public SaveData? QuickLoad() => Load(GameConfig.QuickSaveIndex);

    /// <summary>按 timestamp 返回最新存档的槽位下标；无档返回 -1。</summary>
    public int GetLatestIndex()
    {
        int latest = -1;
        long latestTs = long.MinValue;
        for (int i = 0; i < GameConfig.SaveSlotCount; i++)
        {
            // 源实现用 latestTs 初值 0 + 严格 > → 唯一一档 timestamp=0 时返回 -1（漏选）；
            // 此处用「未选中过」判定，timestamp=0 的档也能被选中
            if (_slots[i] is { } d && (latest < 0 || d.Timestamp > latestTs))
            {
                latestTs = d.Timestamp;
                latest = i;
            }
        }
        return latest;
    }

    /// <summary>
    /// 从存储装载槽位。<b>逐槽容错</b>（修复批 ②-3）：源 SaveManager.loadFromStorage 是逐槽
    /// try/catch（一个坏档只丢自己）；本项目此前是 catch-all <c>Array.Clear</c>——
    /// 一个分片读失败即清空<b>全部</b>槽位（实测好槽被连带清空）。仅当索引整体读取失败
    /// （= 无从得知有哪些槽）时才清空。
    /// </summary>
    private async Task LoadFromStorageAsync()
    {
        IReadOnlyList<SlotMeta> index;
        try
        {
            index = await _storage.GetIndexAsync();
        }
        catch
        {
            Array.Clear(_slots);
            Array.Clear(_slotIds);
            Array.Clear(_slotWinLengths);
            return;
        }
        foreach (var meta in index)
        {
            if (meta.Index < 0 || meta.Index >= GameConfig.SaveSlotCount)
            {
                continue;
            }
            if (_slots[meta.Index] != null)
            {
                continue; // 同槽重复索引项：首条为准
            }
            try
            {
                string? raw = await _storage.GetSaveAsync(meta.Index);
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
                // 外圈 winLength 范围校验（修复批 ②-4）：index 通道此前零校验，
                // winLength=0 即「任意一子判胜」；越界视为缺失 → 回默认 5（与 manifest 通道同口径）
                _slotWinLengths[meta.Index] = NormalizeWinLength(meta.WinLength);
            }
            catch
            {
                continue; // 逐槽容错：坏档只丢自己，不牵连其他槽
            }
        }
    }

    /// <summary>外圈 winLength 归一：仅 4–9 有效，越界/缺省 → null（= 默认 5）。</summary>
    private static int? NormalizeWinLength(int? winLength) =>
        winLength is >= GameConfig.MinWinLength and <= GameConfig.MaxWinLength ? winLength : null;
}
