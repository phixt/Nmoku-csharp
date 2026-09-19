using System.Text;
using Nmoku.Core.Saves;
using Xunit;

namespace Nmoku.Core.Tests;

/// <summary>.nzsave zip 容器与统一嗅探入口（migration-plan §3.4）。</summary>
public class NzSaveContainerTests
{
    private static SaveData Sample() => new()
    {
        Id = "aabbccdd-0000-0000-0000-000000000001",
        Timestamp = 1758240000000,
        BoardSize = 5,
        Layers = 2,
        Rules = "gomoku",
        GameMode = "pvp",
        CurrentPlayer = 2,
        Moves =
        [
            new SaveMove { X = 2, Y = 2, Z = 0, Player = 1 },
            new SaveMove { X = 2, Y = 3, Z = 0, Player = 2 },
        ],
    };

    [Fact]
    public void Pack_ProducesZipWithManifestAndSaveData()
    {
        byte[] raw = NzSaveContainer.Pack(Sample(), createdAtUnixMs: 1758240001234);

        Assert.True(NzSaveContainer.IsZipMagic(raw));
        using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(raw));
        var manifest = System.Text.Json.JsonDocument.Parse(ReadEntry(zip, NzSaveContainer.ManifestEntry)).RootElement;
        Assert.Equal("nzsave/1", manifest.GetProperty("format").GetString());
        Assert.Equal("nmoku.godot", manifest.GetProperty("appId").GetString());
        Assert.Equal(1758240001234, manifest.GetProperty("createdAt").GetInt64());
        var inner = SaveData.Deserialize(ReadEntry(zip, NzSaveContainer.SaveDataEntry));
        Assert.NotNull(inner);
        Assert.Equal(Sample().Id, inner!.Id);
    }

    [Fact]
    public void TryUnpack_RoundTrip_PreservesSaveData()
    {
        var original = Sample();
        var restored = NzSaveContainer.TryUnpack(NzSaveContainer.Pack(original, 1));
        Assert.NotNull(restored);
        Assert.Equal(original.Id, restored!.Id);
        Assert.Equal(original.BoardSize, restored.BoardSize);
        Assert.Equal(original.Layers, restored.Layers);
        Assert.Equal(original.Moves.Count, restored.Moves.Count);
        Assert.Equal(original.Moves[1].Player, restored.Moves[1].Player);
    }

    // ---- manifest 外圈元数据 winLength（B14）----

    [Fact]
    public void Pack_WithWinLength_TryUnpackPackageSurfacesIt()
    {
        var package = NzSaveContainer.TryUnpackPackage(NzSaveContainer.Pack(Sample(), 1, winLength: 7));

        Assert.NotNull(package);
        Assert.Equal(7, package!.WinLength);
        Assert.Equal(Sample().Id, package.Data.Id);
    }

    [Fact]
    public void Pack_WithoutWinLength_ManifestOmitsField()
    {
        byte[] raw = NzSaveContainer.Pack(Sample(), 1);
        using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(raw));
        var manifest = System.Text.Json.JsonDocument.Parse(ReadEntry(zip, NzSaveContainer.ManifestEntry)).RootElement;

        Assert.False(manifest.TryGetProperty("winLength", out _));
        Assert.Null(NzSaveContainer.TryUnpackPackage(raw)!.WinLength);
    }

    [Fact]
    public void TryUnpackPackage_WinLengthOutOfRange_TreatedAsMissing()
    {
        // 外圈脏数据不毙档：越界连珠数忽略、内层档照常解出
        var package = NzSaveContainer.TryUnpackPackage(NzSaveContainer.Pack(Sample(), 1, winLength: 99));

        Assert.NotNull(package);
        Assert.Null(package!.WinLength);
        Assert.Equal(Sample().Id, package.Data.Id);
    }

    [Fact]
    public void TryParsePackage_PlainJson_WinLengthNull()
    {
        var package = SaveArchive.TryParsePackage(SaveData.Serialize(Sample()));

        Assert.NotNull(package);
        Assert.Null(package!.WinLength);
    }

    [Fact]
    public void TryParsePackage_Base64WrappedZip_CarriesManifestWinLength()
    {
        string b64 = Convert.ToBase64String(NzSaveContainer.Pack(Sample(), 1, winLength: 9));

        var package = SaveArchive.TryParsePackage(b64);

        Assert.NotNull(package);
        Assert.Equal(9, package!.WinLength);
        Assert.Equal("2.0.0", package.Data.Version);
    }

    [Fact]
    public void TryParse_Base64WrappedZip_StillReturnsInnerDataOnly()
    {
        string b64 = Convert.ToBase64String(NzSaveContainer.Pack(Sample(), 1, winLength: 7));

        var parsed = SaveArchive.TryParse(b64);

        Assert.NotNull(parsed);
        Assert.Equal(Sample().Id, parsed!.Id);
    }

    [Theory]
    [InlineData(new byte[] { 1, 2, 3, 4 })]
    [InlineData(new byte[] { 0x50, 0x4b, 3, 4, 9, 9 })] // PK magic 但内容损坏
    public void TryUnpack_CorruptOrForeignBytes_ReturnsNull(byte[] raw)
    {
        Assert.Null(NzSaveContainer.TryUnpack(raw));
    }

    [Fact]
    public void TryUnpack_ZipWithoutSaveDataEntry_ReturnsNull()
    {
        using var buffer = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(buffer, System.IO.Compression.ZipArchiveMode.Create))
        using (var s = zip.CreateEntry("readme.txt").Open())
        using (var w = new StreamWriter(s))
        {
            w.Write("no savedata here");
        }
        Assert.Null(NzSaveContainer.TryUnpack(buffer.ToArray()));
    }

    // ---- SaveArchive 统一嗅探 ----

    [Fact]
    public void TryParse_PlainJson_Accepted()
    {
        var parsed = SaveArchive.TryParse(SaveData.Serialize(Sample()));
        Assert.NotNull(parsed);
        Assert.Equal(Sample().Id, parsed!.Id);
    }

    [Fact]
    public void TryParse_Base64WrappedJson_Accepted()
    {
        // 源项目剪贴板格式：btoa(JSON.stringify(saveData))
        string b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(SaveData.Serialize(Sample())));
        var parsed = SaveArchive.TryParse(b64);
        Assert.NotNull(parsed);
        Assert.Equal(Sample().Id, parsed!.Id);
    }

    [Fact]
    public void TryParse_Base64WrappedZipContainer_Accepted()
    {
        string b64 = Convert.ToBase64String(NzSaveContainer.Pack(Sample(), 1));
        var parsed = SaveArchive.TryParse(b64);
        Assert.NotNull(parsed);
        Assert.Equal(Sample().Id, parsed!.Id);
    }

    [Fact]
    public void TryParse_RawZipBytesAsLatin1_Accepted()
    {
        byte[] zip = NzSaveContainer.Pack(Sample(), 1);
        string latin1 = new(zip.Select(b => (char)b).ToArray());
        var parsed = SaveArchive.TryParse(latin1);
        Assert.NotNull(parsed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a save at all")]
    [InlineData("aGVsbG8=")] // 合法 Base64 但内容不是 JSON/zip
    public void TryParse_Garbage_ReturnsNull(string input)
    {
        Assert.Null(SaveArchive.TryParse(input));
    }

    [Fact]
    public void TryParse_V1LegacyJson_MigratesToV2()
    {
        string v1 = """{"moves":[{"x":4,"y":4,"z":0,"player":1}],"boardSize":9,"layers":1}""";
        var parsed = SaveArchive.TryParse(v1);
        Assert.NotNull(parsed);
        Assert.Equal("2.0.0", parsed!.Version);
        Assert.Single(parsed.Moves);
    }

    private static string ReadEntry(System.IO.Compression.ZipArchive zip, string name)
    {
        using var s = zip.GetEntry(name)!.Open();
        using var r = new StreamReader(s, Encoding.UTF8);
        return r.ReadToEnd();
    }
}
