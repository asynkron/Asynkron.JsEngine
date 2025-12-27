using Xunit.Abstractions;

namespace Asynkron.JsEngine.Tests;

public class GroupByTests(ITestOutputHelper output) : FastPathTestBase(output)
{
    [Fact(Timeout = 2000)]
    public async Task Object_GroupBy_GroupsByStringKey()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            const arr = [1, 2, 3, 4, 5, 6];
            const grouped = Object.groupBy(arr, x => x % 2 === 0 ? 'even' : 'odd');
            grouped.even.length === 3 && grouped.odd.length === 3;
        ");
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task Object_GroupBy_GroupsByNumericKey()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            const arr = [1, 2, 3, 4, 5, 6];
            const grouped = Object.groupBy(arr, x => Math.floor(x / 2));
            grouped['0'].length === 1 && grouped['1'].length === 2 && grouped['2'].length === 2;
        ");
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task Map_GroupBy_GroupsIntoMap()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            const arr = [1, 2, 3, 4, 5, 6];
            const grouped = Map.groupBy(arr, x => x % 2);
            grouped.get(0).length === 3 && grouped.get(1).length === 3 && grouped.size === 2;
        ");
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task Map_GroupBy_WithObjectKeys()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            const arr = [1, 2, 3, 4];
            const key1 = {};
            const key2 = {};
            const grouped = Map.groupBy(arr, x => x <= 2 ? key1 : key2);
            grouped.get(key1).length === 2 && grouped.get(key2).length === 2;
        ");
        Assert.True((bool)result!);
    }

    [Fact(Timeout = 2000)]
    public async Task Object_GroupBy_WithIndex()
    {
        await using var engine = CreateEngine();
        var result = await engine.Evaluate(@"
            const arr = ['a', 'b', 'c', 'd'];
            const grouped = Object.groupBy(arr, (x, i) => i < 2 ? 'first' : 'second');
            grouped.first.length === 2 && grouped.second.length === 2;
        ");
        Assert.True((bool)result!);
    }
}
