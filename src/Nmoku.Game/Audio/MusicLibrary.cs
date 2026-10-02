using Godot;

namespace Nmoku.Game.Audio;

/// <summary>
/// BGM 曲库（发现式加载，2026-10-02）：槽位=场景键字符串；文件约定
/// <c>res://assets/audio/bgm_&lt;slot&gt;.ogg</c>（基准）+ <c>bgm_&lt;slot&gt;_2..9.ogg</c>
/// （变体，一槽多曲自动成池）。ResourceLoader.Exists 探测在编辑器与导出包内一致
/// （.import 重映射透明）；池缓存含空结果，缺文件槽位只探测一次。
/// 交付对接（assets-src/music/strudel/README.md）：Strudel 渲染 OGG 按此命名覆盖/新增即生效。
/// </summary>
public sealed class MusicLibrary
{
    public sealed record Track(string Id, AudioStream Stream);

    private const string Dir = "res://assets/audio";
    private const int MaxVariants = 9;

    private readonly Dictionary<string, IReadOnlyList<Track>> _pools = [];

    /// <summary>取槽位曲池（懒加载+缓存）；空池=该槽位无文件。</summary>
    public IReadOnlyList<Track> Pool(string slot)
    {
        if (_pools.TryGetValue(slot, out var cached))
        {
            return cached;
        }
        var list = new List<Track>
        {
            Capacity = 1 + MaxVariants - 1,
        };
        Append(list, $"{Dir}/bgm_{slot}.ogg");
        for (int n = 2; n <= MaxVariants; n++)
        {
            Append(list, $"{Dir}/bgm_{slot}_{n}.ogg");
        }
        _pools[slot] = list;
        return list;
    }

    private static void Append(List<Track> list, string path)
    {
        if (!ResourceLoader.Exists(path) || ResourceLoader.Load<AudioStream>(path) is not { } stream)
        {
            return;
        }
        if (stream is AudioStreamOggVorbis ogg)
        {
            ogg.Loop = true; // 循环在运行时声明，避免依赖导入元数据
        }
        list.Add(new Track(System.IO.Path.GetFileNameWithoutExtension(path), stream));
    }
}
