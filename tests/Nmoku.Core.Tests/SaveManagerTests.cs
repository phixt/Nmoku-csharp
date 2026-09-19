using Nmoku.Core.Saves;

namespace Nmoku.Core.Tests;

/// <summary>内存版 IStorageAdapter（04-contracts §6.3 mock adapter 清单）。</summary>
public sealed class MemoryStorageAdapter : IStorageAdapter
{
    public Dictionary<string, string> Saves { get; } = [];
    public List<SlotMeta> Index { get; } = [];
    public bool InitCalled { get; private set; }
    public bool ThrowOnInit { get; set; }

    public int SetSaveCalls { get; private set; }
    public int DeleteCalls { get; private set; }

    public Task InitAsync()
    {
        if (ThrowOnInit)
        {
            throw new IOException("backend unavailable");
        }
        InitCalled = true;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SlotMeta>> GetIndexAsync() =>
        Task.FromResult<IReadOnlyList<SlotMeta>>(Index);

    public Task<string?> GetSaveAsync(string id) =>
        Task.FromResult(Saves.TryGetValue(id, out var s) ? s : null);

    public Task SetSaveAsync(string id, string data, SlotMeta meta)
    {
        SetSaveCalls++;
        Saves[id] = data; // 原子性：字典两步间无 await
        var existing = Index.FindIndex(m => m.Id == id);
        if (existing >= 0)
        {
            Index[existing] = meta;
        }
        else
        {
            Index.Add(meta);
        }
        return Task.CompletedTask;
    }

    public Task DeleteSaveAsync(string id)
    {
        DeleteCalls++;
        Saves.Remove(id);
        Index.RemoveAll(m => m.Id == id);
        return Task.CompletedTask;
    }

    /// <summary>注入损坏项：索引存在但内容不是 JSON。</summary>
    public void Poison(string id, string corruptContent, int slotIndex)
    {
        Saves[id] = corruptContent;
        Index.Add(new SlotMeta { Index = slotIndex, Id = id, Timestamp = 1, BoardSize = 9, Layers = 3, MovesCount = 0 });
    }
}

public class SaveManagerTests : IDisposable
{
    private static SaveData NewSave(long timestamp, int boardSize = 9, int layers = 3)
    {
        var boardState = Enumerable.Range(0, layers)
            .Select(_ => Enumerable.Range(0, boardSize).Select(_ => new int[boardSize]).ToArray())
            .ToArray();
        return new SaveData
        {
            Id = Guid.NewGuid().ToString(),
            Version = "2.0.0",
            Timestamp = timestamp,
            BoardState = boardState,
            BoardSize = boardSize,
            Layers = layers,
        };
    }

    public void Dispose() => EventBus.ClearAll();

    [Fact]
    public async Task SaveThenLoad_RoundTripsThroughStorage()
    {
        var storage = new MemoryStorageAdapter();
        var manager = new SaveManager(storage);
        await manager.InitAsync();
        Assert.True(storage.InitCalled);

        var data = NewSave(1);
        data.Moves.Add(new SaveMove { X = 2, Y = 3, Z = 1, Player = 2 });
        await manager.SaveAsync(0, data);

        Assert.True(manager.Ready);
        var loaded = manager.Load(0)!;
        Assert.Equal(data.Id, loaded.Id);
        Assert.Equal(2, loaded.Moves[0].X);
        Assert.Equal(1, storage.SetSaveCalls);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(6)]
    public async Task Save_OutOfRangeSlot_SilentlyIgnored(int slot)
    {
        var storage = new MemoryStorageAdapter();
        var manager = new SaveManager(storage);
        await manager.InitAsync();

        await manager.SaveAsync(slot, NewSave(1));
        Assert.Empty(storage.Saves);
    }

    [Fact]
    public async Task QuickSave_LandsInReservedSlot()
    {
        var manager = new SaveManager(new MemoryStorageAdapter());
        await manager.InitAsync();

        await manager.QuickSaveAsync(NewSave(5));

        Assert.NotNull(manager.QuickLoad());
        Assert.False(manager.IsSlotEmpty(GameConfig.QuickSaveIndex));
        Assert.True(manager.IsSlotEmpty(4));
    }

    [Fact]
    public async Task GetLatestIndex_PicksMaxTimestamp()
    {
        var manager = new SaveManager(new MemoryStorageAdapter());
        await manager.InitAsync();
        Assert.Equal(-1, manager.GetLatestIndex());

        await manager.SaveAsync(0, NewSave(100));
        await manager.SaveAsync(3, NewSave(200));
        await manager.SaveAsync(5, NewSave(150));

        Assert.Equal(3, manager.GetLatestIndex());
    }

    [Fact]
    public async Task Delete_ClearsSlotAndStorage()
    {
        var storage = new MemoryStorageAdapter();
        var manager = new SaveManager(storage);
        await manager.InitAsync();
        var data = NewSave(1);
        await manager.SaveAsync(2, data);

        await manager.DeleteAsync(2);

        Assert.True(manager.IsSlotEmpty(2));
        Assert.Null(await storage.GetSaveAsync(data.Id));
        Assert.Equal(1, storage.DeleteCalls);
    }

    [Fact]
    public async Task Save_EmitsSaveUpdatedEvent()
    {
        var manager = new SaveManager(new MemoryStorageAdapter());
        await manager.InitAsync();
        int fired = 0;
        EventBus.SaveUpdated += slots => { fired++; Assert.Equal(6, slots.Count); };

        await manager.SaveAsync(1, NewSave(1));
        await manager.DeleteAsync(1);

        Assert.Equal(2, fired);
    }

    [Fact]
    public async Task Init_CorruptSlotContent_SkippedWithoutThrow()
    {
        var storage = new MemoryStorageAdapter();
        storage.Poison("bad-id", "{ not json !!", 1);

        var manager = new SaveManager(storage);
        await manager.InitAsync(); // 不应抛出

        Assert.True(manager.IsSlotEmpty(1));
        Assert.True(manager.Ready);
    }

    [Fact]
    public async Task Init_DimensionMismatchedV2Save_SkippedWithoutThrow()
    {
        // v2 直通档维度错配（boardState 1 层 vs layers 3）：旧实现不过 Validate 会穿透进槽位
        const string json = """{"id":"bad-dims","version":"2.0.0","boardSize":9,"layers":3,"moves":[],"boardState":[[[0,0,0]]]}""";
        var storage = new MemoryStorageAdapter();
        storage.Poison("bad-dims", json, 2);

        var manager = new SaveManager(storage);
        await manager.InitAsync();

        Assert.True(manager.IsSlotEmpty(2));
        Assert.True(manager.Ready);
    }

    [Fact]
    public async Task Init_StorageUnavailable_SlotsStayEmpty()
    {
        var storage = new MemoryStorageAdapter { ThrowOnInit = true };
        var manager = new SaveManager(storage);
        await Assert.ThrowsAsync<IOException>(() => manager.InitAsync());
        Assert.False(manager.Ready);
    }

    [Fact]
    public async Task Reload_IndexPlusShards_RestoresSlots()
    {
        var storage = new MemoryStorageAdapter();
        var first = new SaveManager(storage);
        await first.InitAsync();
        var data = NewSave(42);
        await first.SaveAsync(4, data);

        // 新实例模拟"重启进程"
        var second = new SaveManager(storage);
        await second.InitAsync();

        Assert.Equal(data.Id, second.Load(4)!.Id);
        Assert.Equal(42, second.Load(4)!.Timestamp);
    }
}
