using Xunit;

// EventBus 为进程级静态；存档槽测试也依赖其隔离，整个测试集串行执行。
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Nmoku.Core.Tests;

internal static class Fixture
{
    public static string Path(string name) =>
        System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    public static string Text(string name) => File.ReadAllText(Path(name));
}
