namespace Nmoku.Core.Saves;

/// <summary>
/// 持久化后端抽象（04-contracts §3.1）：Core 不知晓背后是文件、注册表还是云。
/// 实现方保证 SetSave 的 data+meta 原子写入；DeleteSave 幂等。
/// <para>
/// <b>槽位号为主键</b>（修复批 ②-1，2026-10-27）：旧契约以「档 Id」寻址，
/// 复制/导入同一份档到另一槽会让<b>原槽从索引表消失</b>（磁盘 blob 仍在、index 已无该槽，
/// 删除任一槽还会连带删掉共享 blob）——已发布功能「复制→填写」即可触发，属数据丢失。
/// 现契约一律<b>按槽位号寻址</b>：Id 只作为存档内容里的身份标识（导入保真语义不变），
/// 实现方负责让「同一 Id 出现在两个槽」互不干扰（各自独立 blob）。
/// </para>
/// </summary>
public interface IStorageAdapter
{
    Task InitAsync();

    /// <summary>返回全部已占用槽位的元数据（一槽一条；空槽不在此列）。</summary>
    Task<IReadOnlyList<SlotMeta>> GetIndexAsync();

    /// <summary>读取槽位内容；槽位为空 / 读取失败 → null（不抛异常）。</summary>
    Task<string?> GetSaveAsync(int slotIndex);

    /// <summary>
    /// 写入槽位；<paramref name="meta"/> 须与 <paramref name="slotIndex"/> 一致（实现方为准入闸门）。
    /// <b>写盘失败返回 false</b>（不抛异常、不留半写内容）——调用方据此报「保存失败」，
    /// 杜绝「内存显示已存、磁盘无档」的假成功。
    /// </summary>
    Task<bool> SetSaveAsync(int slotIndex, string data, SlotMeta meta);

    /// <summary>删除槽位；幂等（槽位本就不存在 → true）；删除失败返回 false。</summary>
    Task<bool> DeleteSaveAsync(int slotIndex);
}
