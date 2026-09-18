namespace Nmoku.Core.Saves;

/// <summary>
/// 持久化后端抽象（04-contracts §3.1）：Core 不知晓背后是文件、注册表还是云。
/// 实现方保证 SetSave 的 data+meta 原子写入；DeleteSave 幂等。
/// </summary>
public interface IStorageAdapter
{
    Task InitAsync();

    /// <summary>返回全部槽位元数据（含空位 null 占位语义由调用方处理，此处仅列出已存在项）。</summary>
    Task<IReadOnlyList<SlotMeta>> GetIndexAsync();

    Task<string?> GetSaveAsync(string id);

    Task SetSaveAsync(string id, string data, SlotMeta meta);

    Task DeleteSaveAsync(string id);
}
